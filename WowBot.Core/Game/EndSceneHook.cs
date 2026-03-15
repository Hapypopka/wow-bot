using System.Text;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class EndSceneHook : IDisposable
{
    private readonly MemoryReader _memory;

    private uint _allocBase;
    private uint _codecaveAddr;
    private uint _luaStringAddr;       // буфер для Lua-строки (4096)
    private uint _flagAddr;            // флаг: 0=idle, 1=exec, 3=exec+read, 2=done
    private uint _botStringAddr;       // "bot\0"
    private uint _varNameAddr;         // имя переменной для GetLocalizedText ("WB_R\0")
    private uint _returnPtrAddr;       // сюда запишем указатель на результат
    private uint _playerBasePatchOffset; // смещение в codecave где лежит playerBase (push imm32)

    private uint _endSceneAddr;
    private byte[] _originalBytes = Array.Empty<byte>();
    private int _stolenByteCount;

    private bool _isHooked;

    private const int CodecaveSize = 512;  // увеличил под два пути
    private const int LuaBufferSize = 8192; // увеличил для больших скриптов
    private const int TotalAllocSize = CodecaveSize + LuaBufferSize + 128;

    public bool IsHooked => _isHooked;

    public EndSceneHook(MemoryReader memory)
    {
        _memory = memory;
    }

    public uint FindEndScene()
    {
        uint pDevice1 = _memory.ReadUInt32(Offsets.DevicePtr1);
        if (pDevice1 == 0) return 0;
        uint pDevice2 = _memory.ReadUInt32(pDevice1 + Offsets.DevicePtr2Offset);
        if (pDevice2 == 0) return 0;
        uint vTable = _memory.ReadUInt32(pDevice2);
        if (vTable == 0) return 0;
        return _memory.ReadUInt32(vTable + Offsets.EndSceneOffset);
    }

    public string GetDiagnostics()
    {
        var sb = new StringBuilder();
        uint pDevice1 = _memory.ReadUInt32(Offsets.DevicePtr1);
        sb.AppendLine($"DevicePtr1 [0x{Offsets.DevicePtr1:X8}] = 0x{pDevice1:X8}");
        if (pDevice1 == 0) { sb.AppendLine("ERROR: DevicePtr1 is NULL"); return sb.ToString(); }
        uint pDevice2 = _memory.ReadUInt32(pDevice1 + Offsets.DevicePtr2Offset);
        sb.AppendLine($"DevicePtr2 = 0x{pDevice2:X8}");
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
        return true;
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
        // cmp eax, 1
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x01);
        // je exec_lua
        int jeExecPos = asm.Count;
        asm.Add(0x74); asm.Add(0x00); // placeholder

        // --- Проверка flag == 3 (exec + read result) ---
        // cmp eax, 3
        asm.Add(0x83); asm.Add(0xF8); asm.Add(0x03);
        // je exec_lua_with_result
        int jeExecResultPos = asm.Count;
        asm.Add(0x74); asm.Add(0x00); // placeholder

        // --- Ничего не делаем → skip ---
        // jmp skip
        int jmpSkipPos = asm.Count;
        asm.Add(0xEB); asm.Add(0x00); // placeholder

        // === exec_lua (flag==1): call Lua_DoString, set flag=2 ===
        int execLuaLabel = asm.Count;
        asm[jeExecPos + 1] = (byte)(execLuaLabel - jeExecPos - 2);

        EmitLuaDoStringCall(asm);
        EmitSetFlag(asm, 2);
        // jmp skip
        int jmpSkip2Pos = asm.Count;
        asm.Add(0xEB); asm.Add(0x00); // placeholder

        // === exec_lua_with_result (flag==3): call Lua_DoString, call GetLocalizedText, store ptr, set flag=2 ===
        int execResultLabel = asm.Count;
        asm[jeExecResultPos + 1] = (byte)(execResultLabel - jeExecResultPos - 2);

        EmitLuaDoStringCall(asm);

        // Call GetLocalizedText(playerBase, varName, 0)
        // push 0
        asm.Add(0x6A); asm.Add(0x00);
        // push _varNameAddr
        asm.Add(0x68);
        asm.AddRange(BitConverter.GetBytes(_varNameAddr));
        // push playerBase (placeholder 0xDEADBEEF — патчится из C# перед каждым вызовом)
        asm.Add(0x68);
        _playerBasePatchOffset = (uint)asm.Count; // запоминаем смещение для патча
        asm.AddRange(BitConverter.GetBytes(0xDEADBEEF));
        // mov eax, GetLocalizedText
        asm.Add(0xB8);
        asm.AddRange(BitConverter.GetBytes(Offsets.LuaGetLocalizedText));
        // call eax
        asm.Add(0xFF); asm.Add(0xD0);
        // add esp, 12
        asm.Add(0x83); asm.Add(0xC4); asm.Add(0x0C);

        // mov [_returnPtrAddr], eax — сохраняем указатель на строку результата
        asm.Add(0xA3);
        asm.AddRange(BitConverter.GetBytes(_returnPtrAddr));

        EmitSetFlag(asm, 2);

        // === skip label ===
        int skipLabel = asm.Count;
        asm[jmpSkipPos + 1] = (byte)(skipLabel - jmpSkipPos - 2);
        asm[jmpSkip2Pos + 1] = (byte)(skipLabel - jmpSkip2Pos - 2);

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
    /// Выполняет Lua и читает значение переменной WB_R
    /// </summary>
    public string? ExecuteLuaWithResult(string luaCode, uint playerBase, int timeoutMs = 2000)
    {
        if (!_isHooked) throw new InvalidOperationException("Hook not installed.");
        if (playerBase == 0) return null;

        // Патчим playerBase прямо в codecave (push imm32)
        _memory.WriteUInt32(_codecaveAddr + _playerBasePatchOffset, playerBase);
        _memory.WriteUInt32(_returnPtrAddr, 0);
        _memory.WriteString(_luaStringAddr, luaCode);
        _memory.WriteUInt32(_flagAddr, 3); // flag=3 → exec + read

        if (!WaitForCompletion(timeoutMs))
            return null;

        // Читаем указатель на строку результата
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
