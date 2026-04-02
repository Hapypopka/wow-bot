using System.Text;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class EndSceneHook : IDisposable
{
    private readonly MemoryReader _memory;

    private uint _allocBase;
    private uint _codecaveAddr;
    private uint _luaStringAddr;       // буфер для Lua-строки (4096)
    private uint _flagAddr;            // флаг: 0=idle, 1=exec, 3=exec+read, 4=readonly, 5=terrainclick, 2=done
    private uint _botStringAddr;       // "bot\0"
    private uint _varNameAddr;         // имя переменной для GetLocalizedText ("WB_R\0")
    private uint _returnPtrAddr;       // сюда запишем указатель на результат
    private uint _terrainClickAddr;    // структура TerrainClick (24 байт: GUID8 + XYZ12 + btn4)
    private uint _ctmCallAddr;          // структура CTM call (20 байт: clickType4 + GUID8 + XYZ12 + precision4)
    private uint _endSceneAddr;
    private byte[] _originalBytes = Array.Empty<byte>();
    private int _stolenByteCount;

    private bool _isHooked;

    private const int CodecaveSize = 512;  // увеличил под два пути
    private const int LuaBufferSize = 32768; // 32KB — хилерские скрипты ~16KB с dispel/resurrect
    private const int TotalAllocSize = CodecaveSize + LuaBufferSize + 192; // +32 TerrainClick + 28 CTM call

    public bool IsHooked => _isHooked;

    public EndSceneHook(MemoryReader memory)
    {
        _memory = memory;
    }

    public uint FindEndScene()
    {
        // Способ 1: стандартная цепочка указателей WoW
        uint pDevice1 = _memory.ReadUInt32(Offsets.DevicePtr1);
        Logger.Info($"FindEndScene: DevicePtr1 [0x{Offsets.DevicePtr1:X8}] = 0x{pDevice1:X8}");
        if (pDevice1 != 0)
        {
            uint pDevice2 = _memory.ReadUInt32(pDevice1 + Offsets.DevicePtr2Offset);
            Logger.Info($"FindEndScene: DevicePtr2 [+0x{Offsets.DevicePtr2Offset:X}] = 0x{pDevice2:X8}");
            if (pDevice2 != 0)
            {
                uint vTable = _memory.ReadUInt32(pDevice2);
                Logger.Info($"FindEndScene: VTable = 0x{vTable:X8}");
                if (vTable != 0)
                {
                    uint es = _memory.ReadUInt32(vTable + Offsets.EndSceneOffset);
                    Logger.Info($"FindEndScene: EndScene [vtable+0x{Offsets.EndSceneOffset:X}] = 0x{es:X8}");
                    if (es != 0)
                    {
                        byte[] hdr = _memory.ReadBytes(es, 16);
                        Logger.Info($"FindEndScene: first bytes = {FormatBytes(hdr)}");
                        return es;
                    }
                }
            }
            Logger.Warn("FindEndScene: default pointer chain broken");
        }

        // Способ 2: универсальный — через dummy D3D9 device + поиск модуля
        Logger.Info("Trying D3D9 dummy device method...");
        if (_memory.Process != null)
        {
            uint d3dResult = D3D9Helper.FindEndSceneInProcess(_memory.Process.Id);
            if (d3dResult != 0)
            {
                byte[] hdr = _memory.ReadBytes(d3dResult, 16);
                Logger.Info($"FindEndScene: D3D9Helper result=0x{d3dResult:X8}, bytes={FormatBytes(hdr)}");
                return d3dResult;
            }
        }

        Logger.Error("EndScene not found by any method");
        return 0;
    }

    public string GetDiagnostics()
    {
        var sb = new StringBuilder();
        uint pDevice1 = _memory.ReadUInt32(Offsets.DevicePtr1);
        sb.AppendLine($"DevicePtr1 [0x{Offsets.DevicePtr1:X8}] = 0x{pDevice1:X8}");
        if (pDevice1 == 0) { sb.AppendLine("ERROR: DevicePtr1 is NULL"); return sb.ToString(); }

        uint pDevice2 = _memory.ReadUInt32(pDevice1 + Offsets.DevicePtr2Offset);
        sb.AppendLine($"DevicePtr2 [+0x{Offsets.DevicePtr2Offset:X}] = 0x{pDevice2:X8}");

        if (pDevice2 == 0) { sb.AppendLine("ERROR: DevicePtr2 is NULL"); return sb.ToString(); }
        uint vTable = _memory.ReadUInt32(pDevice2);
        sb.AppendLine($"VTable = 0x{vTable:X8}");
        uint endScene = _memory.ReadUInt32(vTable + Offsets.EndSceneOffset);
        sb.AppendLine($"EndScene = 0x{endScene:X8}");
        if (endScene == 0) { sb.AppendLine("ERROR: EndScene is NULL"); return sb.ToString(); }
        byte[] bytes = _memory.ReadBytes(endScene, 32);
        sb.AppendLine($"\nFirst 32 bytes: {FormatBytes(bytes)}");
        if (bytes[0] == 0xE9) sb.AppendLine("WARNING: Already hooked (starts with JMP)!");
        return sb.ToString();
    }

    private int CalculateStolenBytes(byte[] bytes)
    {
        int offset = 0;
        while (offset < 5 && offset < bytes.Length)
        {
            int instrLen = GetInstructionLength(bytes, offset);
            if (instrLen <= 0) return 5;
            offset += instrLen;
        }
        return offset;
    }

    private int GetInstructionLength(byte[] code, int offset)
    {
        if (offset >= code.Length) return -1;
        byte b = code[offset];
        return b switch
        {
            0x55 or 0x56 or 0x57 or 0x50 or 0x51 or 0x52 or 0x53 => 1,
            0x58 or 0x59 or 0x5D or 0x5E or 0x5F => 1,
            0x90 or 0xC3 or 0xCC or 0x60 or 0x61 or 0x9C or 0x9D => 1,
            0x6A or 0xEB or 0x74 or 0x75 => 2,
            0xE8 or 0xE9 or 0x68 or 0xB8 or 0xB9 or 0xA1 => 5,
            0x8B or 0x89 or 0xFF or 0x33 or 0x2B or 0x03 or 0x3B or 0x85 => GetModRMLength(code, offset),
            0x83 => GetModRMLength(code, offset) + 1,
            0x81 => GetModRMLength(code, offset) + 4,
            _ => -1
        };
    }

    private int GetModRMLength(byte[] code, int offset)
    {
        if (offset + 1 >= code.Length) return 2;
        byte modrm = code[offset + 1];
        int mod = (modrm >> 6) & 3;
        int rm = modrm & 7;
        int len = 2;
        if (mod != 3 && rm == 4) len++;
        if (mod == 0 && rm == 5) len += 4;
        else if (mod == 1) len += 1;
        else if (mod == 2) len += 4;
        return len;
    }

    private byte[] FixupStolenBytes(byte[] bytes, int count, uint originalAddr, uint newAddr)
    {
        byte[] result = new byte[count];
        Array.Copy(bytes, result, count);
        int offset = 0;
        while (offset < count)
        {
            byte b = result[offset];
            if ((b == 0xE8 || b == 0xE9) && offset + 5 <= count)
            {
                int oldRel = BitConverter.ToInt32(result, offset + 1);
                uint target = (uint)(originalAddr + offset + 5 + oldRel);
                int newRel = (int)(target - (newAddr + offset + 5));
                BitConverter.GetBytes(newRel).CopyTo(result, offset + 1);
                offset += 5;
            }
            else
            {
                int instrLen = GetInstructionLength(result, offset);
                offset += instrLen > 0 ? instrLen : 1;
            }
        }
        return result;
    }

    public bool Install()
    {
        if (_isHooked) return true;

        _endSceneAddr = FindEndScene();
        if (_endSceneAddr == 0)
            throw new Exception("EndScene not found!");
        Logger.Info($"Installing hook: EndScene=0x{_endSceneAddr:X8}");

        InstallCore();

        // Верификация: проверяем что хук работает
        _memory.WriteString(_luaStringAddr, "local x=1");
        _memory.WriteUInt32(_flagAddr, 1);
        if (!WaitForCompletion(1000))
        {
            Logger.Warn("Hook verify failed — unhooking");
            Uninstall();
            throw new Exception("EndScene hook installed but not responding");
        }
        Logger.Info("Hook verified OK");
        return true;
    }

    private void InstallCore()
    {
        byte[] headerBytes = _memory.ReadBytes(_endSceneAddr, 16);
        _stolenByteCount = CalculateStolenBytes(headerBytes);
        if (_stolenByteCount < 5)
            throw new Exception($"Cannot determine instruction boundary (stolen={_stolenByteCount})");

        _originalBytes = _memory.ReadBytes(_endSceneAddr, _stolenByteCount);

        _allocBase = _memory.AllocateMemory(TotalAllocSize);
        if (_allocBase == 0)
            throw new Exception("Failed to allocate memory in WoW process.");

        // Memory layout
        _codecaveAddr = _allocBase;
        _luaStringAddr = _allocBase + CodecaveSize;
        _flagAddr = _luaStringAddr + LuaBufferSize;
        _botStringAddr = _flagAddr + 4;
        _varNameAddr = _botStringAddr + 8;       // "WB_R\0"
        _returnPtrAddr = _varNameAddr + 8;       // 4 bytes for result pointer

        _terrainClickAddr = _returnPtrAddr + 4;     // 24 байт: GUID(8) + X(4) + Y(4) + Z(4) + btn(4)
        _ctmCallAddr = _terrainClickAddr + 24;       // 28 байт: clickType(4) + GUID(8) + X(4) + Y(4) + Z(4) + precision(4)

        _memory.WriteString(_botStringAddr, "bot");
        _memory.WriteString(_varNameAddr, "WB_R");
        _memory.WriteUInt32(_flagAddr, 0);
        _memory.WriteUInt32(_returnPtrAddr, 0);

        byte[] codecave = BuildCodecave();
        _memory.WriteBytes(_codecaveAddr, codecave);

        // Patch EndScene
        byte[] jmpPatch = new byte[_stolenByteCount];
        jmpPatch[0] = 0xE9;
        BitConverter.GetBytes((int)(_codecaveAddr - _endSceneAddr - 5)).CopyTo(jmpPatch, 1);
        for (int i = 5; i < _stolenByteCount; i++) jmpPatch[i] = 0x90;
        _memory.WriteBytes(_endSceneAddr, jmpPatch);

        _isHooked = true;
    }

    private byte[] BuildCodecave()
    {
        var asm = new List<byte>();

        // pushad / pushfd
        asm.Add(0x60);
        asm.Add(0x9C);

        // mov eax, [_flagAddr]
        asm.Add(0xA1);
        asm.AddRange(BitConverter.GetBytes(_flagAddr));

        // --- Проверка flag == 1 (exec only) ---
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x01); // cmp eax, 1
        int jeExecPos = asm.Count;
        asm.Add(0x0F); asm.Add(0x84); asm.AddRange(new byte[4]); // je rel32

        // --- Проверка flag == 3 (exec + read result) ---
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x03); // cmp eax, 3
        int jeExecResultPos = asm.Count;
        asm.Add(0x0F); asm.Add(0x84); asm.AddRange(new byte[4]); // je rel32

        // --- Проверка flag == 4 (read only — lua_getfield без DoString) ---
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x04); // cmp eax, 4
        int jeReadOnlyPos = asm.Count;
        asm.Add(0x0F); asm.Add(0x84); asm.AddRange(new byte[4]); // je rel32

        // --- Проверка flag == 5 (terrain click — ground AoE) ---
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x05); // cmp eax, 5
        int jeTerrainPos = asm.Count;
        asm.Add(0x0F); asm.Add(0x84); asm.AddRange(new byte[4]); // je rel32

        // --- Проверка flag == 6 (CTM call — CGPlayer_C__ClickToMove) ---
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x06); // cmp eax, 6
        int jeCtmCallPos = asm.Count;
        asm.Add(0x0F); asm.Add(0x84); asm.AddRange(new byte[4]); // je rel32

        // --- Ничего не делаем → skip (rel32) ---
        int jmpSkipPos = asm.Count;
        asm.Add(0xE9); asm.AddRange(new byte[4]); // jmp rel32 placeholder

        // === exec_lua (flag==1): call Lua_DoString, set flag=2 ===
        int execLuaLabel = asm.Count;
        { int off = execLuaLabel - jeExecPos - 6; // je rel32 = 6 bytes
          asm[jeExecPos+2]=(byte)(off&0xFF); asm[jeExecPos+3]=(byte)((off>>8)&0xFF);
          asm[jeExecPos+4]=(byte)((off>>16)&0xFF); asm[jeExecPos+5]=(byte)((off>>24)&0xFF); }

        EmitLuaDoStringCall(asm);
        EmitSetFlag(asm, 2);
        // jmp skip (rel32)
        int jmpSkip2Pos = asm.Count;
        asm.Add(0xE9); asm.AddRange(new byte[4]); // jmp rel32 placeholder

        // === exec_lua_with_result (flag==3): Lua_DoString + lua_getfield + lua_tolstring ===
        int execResultLabel = asm.Count;
        { int off = execResultLabel - jeExecResultPos - 6;
          asm[jeExecResultPos+2]=(byte)(off&0xFF); asm[jeExecResultPos+3]=(byte)((off>>8)&0xFF);
          asm[jeExecResultPos+4]=(byte)((off>>16)&0xFF); asm[jeExecResultPos+5]=(byte)((off>>24)&0xFF); }

        EmitLuaDoStringCall(asm);

        // lua_getfield(L, LUA_GLOBALSINDEX, "WB_R") — cdecl
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_varNameAddr));              // push "WB_R"
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(unchecked((uint)(-10002)))); // push -10002
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState)); // push [LuaState]
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaGetField));
        asm.Add(0xFF); asm.Add(0xD0);                                                 // call eax
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);                                  // add esp,12

        // lua_tolstring(L, -1, 0) — читает строку с верха стека
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes((uint)0));                   // push 0
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(unchecked((uint)(-1))));      // push -1
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState)); // push [LuaState]
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaToLString));
        asm.Add(0xFF); asm.Add(0xD0);                                                 // call eax
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);                                  // add esp,12

        // mov [_returnPtrAddr], eax
        asm.Add(0xA3); asm.AddRange(BitConverter.GetBytes(_returnPtrAddr));

        // lua_settop(L, 0) — очистка стека
        asm.Add(0x6A); asm.Add(0x00);                                                 // push 0
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState)); // push [LuaState]
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaSetTop));
        asm.Add(0xFF); asm.Add(0xD0);                                                 // call eax
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x08);                                  // add esp,8

        EmitSetFlag(asm, 2);

        // jmp to skip
        int jmpSkip3Pos = asm.Count;
        asm.Add(0xE9); asm.AddRange(new byte[4]);

        // === flag==4: ТОЛЬКО lua_getfield + lua_tolstring (без DoString) ===
        int readOnlyLabel = asm.Count;
        { int off = readOnlyLabel - jeReadOnlyPos - 6;
          asm[jeReadOnlyPos+2]=(byte)(off&0xFF); asm[jeReadOnlyPos+3]=(byte)((off>>8)&0xFF);
          asm[jeReadOnlyPos+4]=(byte)((off>>16)&0xFF); asm[jeReadOnlyPos+5]=(byte)((off>>24)&0xFF); }

        // lua_getfield([LuaState], -10002, "WB_R")
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_varNameAddr));
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(unchecked((uint)(-10002))));
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState));
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaGetField));
        asm.Add(0xFF); asm.Add(0xD0);
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);

        // lua_tolstring([LuaState], -1, 0)
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes((uint)0));
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(unchecked((uint)(-1))));
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState));
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaToLString));
        asm.Add(0xFF); asm.Add(0xD0);
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);

        // mov [_returnPtrAddr], eax
        asm.Add(0xA3); asm.AddRange(BitConverter.GetBytes(_returnPtrAddr));

        // lua_settop([LuaState], 0)
        asm.Add(0x6A); asm.Add(0x00);
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(Offsets.LuaState));
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaSetTop));
        asm.Add(0xFF); asm.Add(0xD0);
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x08);

        EmitSetFlag(asm, 2);

        // jmp to skip
        int jmpSkip4Pos = asm.Count;
        asm.Add(0xE9); asm.AddRange(new byte[4]);

        // === flag==5: HandleTerrainClick(struct*) — ground AoE ===
        int terrainLabel = asm.Count;
        { int off = terrainLabel - jeTerrainPos - 6;
          asm[jeTerrainPos+2]=(byte)(off&0xFF); asm[jeTerrainPos+3]=(byte)((off>>8)&0xFF);
          asm[jeTerrainPos+4]=(byte)((off>>16)&0xFF); asm[jeTerrainPos+5]=(byte)((off>>24)&0xFF); }

        // push _terrainClickAddr (pointer to struct)
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_terrainClickAddr));
        // call HandleTerrainClick (cdecl)
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.HandleTerrainClick));
        asm.Add(0xFF); asm.Add(0xD0); // call eax
        // add esp, 4 (cdecl cleanup)
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x04);

        EmitSetFlag(asm, 2);

        // jmp to skip
        int jmpSkip5Pos = asm.Count;
        asm.Add(0xE9); asm.AddRange(new byte[4]);

        // === flag==6: CGPlayer_C__ClickToMove(this, clickType, guidPtr, posPtr, precision) ===
        int ctmCallLabel = asm.Count;
        { int off = ctmCallLabel - jeCtmCallPos - 6;
          asm[jeCtmCallPos+2]=(byte)(off&0xFF); asm[jeCtmCallPos+3]=(byte)((off>>8)&0xFF);
          asm[jeCtmCallPos+4]=(byte)((off>>16)&0xFF); asm[jeCtmCallPos+5]=(byte)((off>>24)&0xFF); }

        // Получаем playerBase: [ClientConnection] → [+ObjMgrOffset] → [+LocalPlayerOffset]
        // Проще: читаем из ObjectManager — playerBase уже известен C#-стороне, запишем в struct
        // _ctmCallAddr layout: clickType(4) + GUID(8) + X(4) + Y(4) + Z(4) + precision(4) + playerBase(4)
        // precision float через FPU
        // push precision (float) — загружаем из struct+24
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(_ctmCallAddr + 24)); // push [precision]
        // push posPtr (указатель на X,Y,Z в struct+12)
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_ctmCallAddr + 12)); // push posAddr
        // push guidPtr (указатель на GUID в struct+4)
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_ctmCallAddr + 4)); // push guidAddr
        // push clickType
        asm.Add(0xFF); asm.Add(0x35); asm.AddRange(BitConverter.GetBytes(_ctmCallAddr)); // push [clickType]
        // mov ecx, [playerBase] — из struct+28
        asm.Add(0x8B); asm.Add(0x0D); asm.AddRange(BitConverter.GetBytes(_ctmCallAddr + 28)); // mov ecx, [playerBase]
        // call CGPlayer_C__ClickToMove (0x00727400)
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes((uint)0x00727400));
        asm.Add(0xFF); asm.Add(0xD0); // call eax

        EmitSetFlag(asm, 2);

        // === skip label — patch jmp rel32 ===
        int skipLabel = asm.Count;
        { // jmpSkipPos (E9 rel32)
            int off = skipLabel - jmpSkipPos - 5;
            asm[jmpSkipPos + 1] = (byte)(off & 0xFF);
            asm[jmpSkipPos + 2] = (byte)((off >> 8) & 0xFF);
            asm[jmpSkipPos + 3] = (byte)((off >> 16) & 0xFF);
            asm[jmpSkipPos + 4] = (byte)((off >> 24) & 0xFF);
        }
        { // jmpSkip2Pos (E9 rel32)
            int off = skipLabel - jmpSkip2Pos - 5;
            asm[jmpSkip2Pos + 1] = (byte)(off & 0xFF);
            asm[jmpSkip2Pos + 2] = (byte)((off >> 8) & 0xFF);
            asm[jmpSkip2Pos + 3] = (byte)((off >> 16) & 0xFF);
            asm[jmpSkip2Pos + 4] = (byte)((off >> 24) & 0xFF);
        }
        { // jmpSkip3Pos (E9 rel32)
            int off = skipLabel - jmpSkip3Pos - 5;
            asm[jmpSkip3Pos + 1] = (byte)(off & 0xFF);
            asm[jmpSkip3Pos + 2] = (byte)((off >> 8) & 0xFF);
            asm[jmpSkip3Pos + 3] = (byte)((off >> 16) & 0xFF);
            asm[jmpSkip3Pos + 4] = (byte)((off >> 24) & 0xFF);
        }
        { // jmpSkip4Pos (E9 rel32) — terrain click
            int off = skipLabel - jmpSkip4Pos - 5;
            asm[jmpSkip4Pos + 1] = (byte)(off & 0xFF);
            asm[jmpSkip4Pos + 2] = (byte)((off >> 8) & 0xFF);
            asm[jmpSkip4Pos + 3] = (byte)((off >> 16) & 0xFF);
            asm[jmpSkip4Pos + 4] = (byte)((off >> 24) & 0xFF);
        }
        { // jmpSkip5Pos (E9 rel32) — ctm call
            int off = skipLabel - jmpSkip5Pos - 5;
            asm[jmpSkip5Pos + 1] = (byte)(off & 0xFF);
            asm[jmpSkip5Pos + 2] = (byte)((off >> 8) & 0xFF);
            asm[jmpSkip5Pos + 3] = (byte)((off >> 16) & 0xFF);
            asm[jmpSkip5Pos + 4] = (byte)((off >> 24) & 0xFF);
        }

        // popfd / popad
        asm.Add(0x9D);
        asm.Add(0x61);

        // Stolen bytes
        uint stolenAddr = (uint)(_codecaveAddr + asm.Count);
        byte[] fixedBytes = FixupStolenBytes(_originalBytes, _stolenByteCount, _endSceneAddr, stolenAddr);
        asm.AddRange(fixedBytes);

        // jmp back
        asm.Add(0xE9);
        int jmpBackTarget = (int)(_endSceneAddr + _stolenByteCount);
        int jmpBackFrom = (int)(_codecaveAddr + asm.Count + 4);
        asm.AddRange(BitConverter.GetBytes(jmpBackTarget - jmpBackFrom));

        return asm.ToArray();
    }

    private void EmitLuaDoStringCall(List<byte> asm)
    {
        // push 0
        asm.Add(0x6A); asm.Add(0x00);
        // push _botStringAddr
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_botStringAddr));
        // push _luaStringAddr
        asm.Add(0x68); asm.AddRange(BitConverter.GetBytes(_luaStringAddr));
        // mov eax, Lua_DoString
        asm.Add(0xB8); asm.AddRange(BitConverter.GetBytes(Offsets.LuaDoString));
        // call eax
        asm.Add(0xFF); asm.Add(0xD0);
        // add esp, 12
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);
    }

    private void EmitSetFlag(List<byte> asm, int value)
    {
        // mov [_flagAddr], value
        asm.Add(0xC7); asm.Add(0x05);
        asm.AddRange(BitConverter.GetBytes(_flagAddr));
        asm.AddRange(BitConverter.GetBytes(value));
    }

    // --- Public API ---

    public bool ExecuteLua(string luaCode, int timeoutMs = 1000)
    {
        if (!_isHooked) throw new InvalidOperationException("Hook not installed.");
        _memory.WriteString(_luaStringAddr, luaCode);
        _memory.WriteUInt32(_flagAddr, 1);
        return WaitForCompletion(timeoutMs);
    }

    /// <summary>
    /// Кликает по земле в указанных координатах (для ground-targeted AoE).
    /// Спелл должен быть уже в режиме прицеливания (SpellIsTargeting).
    /// </summary>
    public bool CastTerrainClick(float x, float y, float z, int timeoutMs = 500)
    {
        if (!_isHooked) return false;
        // Записываем структуру TerrainClick: GUID(8) + X(4) + Y(4) + Z(4) + MouseBtn(4)
        _memory.WriteUInt32(_terrainClickAddr, 0);     // GUID low
        _memory.WriteUInt32(_terrainClickAddr + 4, 0); // GUID high
        _memory.WriteFloat(_terrainClickAddr + 8, x);
        _memory.WriteFloat(_terrainClickAddr + 12, y);
        _memory.WriteFloat(_terrainClickAddr + 16, z);
        _memory.WriteUInt32(_terrainClickAddr + 20, 1); // Left click
        // Триггерим flag=5
        _memory.WriteUInt32(_flagAddr, 5);
        return WaitForCompletion(timeoutMs);
    }

    /// <summary>
    /// Вызывает настоящую CGPlayer_C__ClickToMove через EndScene (flag=6).
    /// Корректно инициализирует CTM-систему, поворачивает модель, работает с холодного старта.
    /// </summary>
    public bool CallClickToMove(float x, float y, float z, uint playerBase, int clickType = 4, float precision = 0.5f, int timeoutMs = 500)
    {
        if (!_isHooked) return false;
        // struct layout: clickType(4) + GUID(8) + X(4) + Y(4) + Z(4) + precision(4) + playerBase(4)
        _memory.WriteInt32(_ctmCallAddr, clickType);
        _memory.WriteUInt32(_ctmCallAddr + 4, 0);   // GUID low
        _memory.WriteUInt32(_ctmCallAddr + 8, 0);   // GUID high
        _memory.WriteFloat(_ctmCallAddr + 12, x);
        _memory.WriteFloat(_ctmCallAddr + 16, y);
        _memory.WriteFloat(_ctmCallAddr + 20, z);
        _memory.WriteFloat(_ctmCallAddr + 24, precision);
        _memory.WriteUInt32(_ctmCallAddr + 28, playerBase);
        _memory.WriteUInt32(_flagAddr, 6);
        return WaitForCompletion(timeoutMs);
    }

    /// <summary>
    /// Выполняет Lua и читает значение переменной WB_R через Lua C API
    /// Шаг 1: ExecuteLua (flag=1) — выполняет Lua код (ставит WB_R)
    /// Шаг 2: flag=3 — lua_getfield + lua_tolstring (читает WB_R)
    /// </summary>
    public string? ExecuteLuaWithResult(string luaCode, uint playerBase = 0, int timeoutMs = 2000)
    {
        if (!_isHooked) throw new InvalidOperationException("Hook not installed.");

        // Шаг 1: DoString (flag=1)
        _memory.WriteString(_luaStringAddr, luaCode);
        _memory.WriteUInt32(_flagAddr, 1);
        if (!WaitForCompletion(timeoutMs))
            return null;

        // Ждём 1 EndScene кадр чтобы Lua обработал
        Thread.Sleep(50);

        // Шаг 2: только getfield (flag=4)
        _memory.WriteUInt32(_returnPtrAddr, 0);
        _memory.WriteUInt32(_flagAddr, 4);
        if (!WaitForCompletion(timeoutMs))
            return null;

        uint resultPtr = _memory.ReadUInt32(_returnPtrAddr);
        if (resultPtr == 0) return null;

        return _memory.ReadString(resultPtr, 4096);
    }

    private bool WaitForCompletion(int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            uint flag = _memory.ReadUInt32(_flagAddr);
            if (flag == 2) { _memory.WriteUInt32(_flagAddr, 0); return true; }
            if (flag == 0) return false;
            Thread.Sleep(1);
        }
        Logger.Warn($"ExecuteLua timeout ({timeoutMs}ms)");
        _memory.WriteUInt32(_flagAddr, 0);
        return false;
    }

    public void Uninstall()
    {
        if (!_isHooked) return;
        if (_originalBytes.Length > 0 && _endSceneAddr != 0)
            _memory.WriteBytes(_endSceneAddr, _originalBytes);
        Thread.Sleep(100);
        if (_allocBase != 0) { _memory.FreeMemory(_allocBase); _allocBase = 0; }
        _isHooked = false;
    }

    public void Dispose() => Uninstall();

    private static string FormatBytes(byte[] bytes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++) sb.Append($"{bytes[i]:X2} ");
        return sb.ToString().TrimEnd();
    }
}
