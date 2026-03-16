using WowBot.Core.Memory;

namespace WowBot.Core.Game;

/// <summary>
/// Читает результаты Lua через макрос.
/// 1. Lua пишет значение в макрос: EditMacro(1, 'WB', 1, value)
/// 2. C# читает строку по найденному адресу в памяти
/// </summary>
public class LuaReader
{
    private readonly MemoryReader _memory;
    private readonly EndSceneHook _hook;
    private uint _macroAddr;
    private bool _initialized;

    public bool IsInitialized => _initialized;

    public LuaReader(MemoryReader memory, EndSceneHook hook)
    {
        _memory = memory;
        _hook = hook;
    }

    /// <summary>
    /// Находит адрес макроса в памяти (вызвать один раз после Attach)
    /// </summary>
    public bool Initialize()
    {
        // Двухпроходный скан — находим адрес который обновляется при EditMacro

        // Проход 1: пишем маркер A
        string markerA = "WBMA_" + (Environment.TickCount % 100000);
        _hook.ExecuteLua($"if GetNumMacros() == 0 then CreateMacro('WB', 1, '{markerA}') else EditMacro(1, 'WB', 1, '{markerA}') end", 500);
        System.Threading.Thread.Sleep(200);
        _hook.ExecuteLua("local x=1", 100);
        System.Threading.Thread.Sleep(100);

        byte[] needleA = System.Text.Encoding.UTF8.GetBytes(markerA);
        var candidates = ScanForAllStrings(needleA);

        if (candidates.Count == 0) return false;

        // Проход 2: пишем маркер B
        string markerB = "WBMB_" + (Environment.TickCount % 100000);
        _hook.ExecuteLua($"EditMacro(1, 'WB', 1, '{markerB}')", 500);
        System.Threading.Thread.Sleep(200);
        _hook.ExecuteLua("local x=1", 100);
        System.Threading.Thread.Sleep(100);

        // Проверяем какие адреса обновились на маркер B
        byte[] needleB = System.Text.Encoding.UTF8.GetBytes(markerB);
        foreach (uint addr in candidates)
        {
            string val = _memory.ReadString(addr, markerB.Length + 5);
            if (val.StartsWith(markerB))
            {
                _macroAddr = addr;
                _initialized = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Выполняет Lua, результат должен быть записан в WB_R.
    /// Lua скрипт должен заканчиваться: EditMacro(1,'WB',1,WB_R)
    /// </summary>
    public string? Execute(string luaCode, int timeoutMs = 2000)
    {
        if (!_initialized) return null;

        // Очищаем макрос (пишем пустую метку)
        _memory.WriteString(_macroAddr, "\0");
        System.Threading.Thread.Sleep(20);

        // Выполняем Lua (должен записать результат в макрос)
        _hook.ExecuteLua(luaCode, timeoutMs);
        System.Threading.Thread.Sleep(150);

        // Читаем результат
        string result = _memory.ReadString(_macroAddr, 255);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// Быстрый хелпер: выполнить Lua выражение и вернуть строку
    /// </summary>
    public string? Eval(string luaExpression)
    {
        // Простой Lua — сначала вычисляем, потом пишем в макрос
        string lua = $"WB_R = tostring({luaExpression}) EditMacro(1, 'WB', 1, WB_R)";
        return Execute(lua);
    }

    private List<uint> ScanForAllStrings(byte[] needle)
    {
        var results = new List<uint>();

        uint[][] regions = {
            new uint[] { 0x01000000, 0x08000000 },
            new uint[] { 0x08000000, 0x20000000 },
            new uint[] { 0x20000000, 0x30000000 },
        };

        foreach (var region in regions)
        {
            uint start = region[0];
            uint end = region[1];

            for (uint addr = start; addr < end; addr += 4096)
            {
                try
                {
                    byte[] block = _memory.ReadBytes(addr, 4096 + needle.Length);
                    for (int i = 0; i < 4096; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < needle.Length; j++)
                        {
                            if (block[i + j] != needle[j]) { match = false; break; }
                        }
                        if (match)
                            results.Add(addr + (uint)i);
                    }
                }
                catch { }
            }
        }

        return results;
    }
}
