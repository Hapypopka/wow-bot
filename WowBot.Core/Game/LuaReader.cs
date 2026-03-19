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
    /// Находит адрес макроса в памяти (вызвать один раз после Attach).
    /// Пробует до 3 раз — первый раз может создать макрос, второй найдёт.
    /// </summary>
    public bool Initialize()
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Logger.Info($"LuaReader: attempt {attempt}/3");
            if (TryInitialize(attempt))
                return true;
            System.Threading.Thread.Sleep(500);
        }
        Logger.Error("LuaReader: all attempts failed");
        return false;
    }

    // Разбиваем маркер в Lua конкатенацией чтобы полная строка НЕ попала в Lua буфер
    private static string LuaConcatMarker(string marker)
    {
        int mid = marker.Length / 2;
        string a = marker[..mid];
        string b = marker[mid..];
        return $"'{a}'..'{b}'";
    }

    private bool TryInitialize(int attempt)
    {
        // Убедимся что макрос существует
        string ensureMacro = "local g,c = GetNumMacros() if g == 0 then CreateMacro('WB', 1, 'init') end";
        _hook.ExecuteLua(ensureMacro, 500);
        System.Threading.Thread.Sleep(300);

        // Проход 1: пишем маркер A (длинный, чтобы WoW выделил большой буфер)
        string markerA = "WBMA_" + (Environment.TickCount % 100000) + "_PADDING_FOR_BUFFER_SIZE";
        _hook.ExecuteLua($"EditMacro(1, 'WB', 1, {LuaConcatMarker(markerA)})", 500);
        System.Threading.Thread.Sleep(300);

        byte[] needleA = System.Text.Encoding.UTF8.GetBytes(markerA);
        var candidates = ScanForAllStrings(needleA);
        Logger.Info($"LuaReader: markerA='{markerA}' candidates={candidates.Count}");

        if (candidates.Count == 0)
        {
            Logger.Warn("LuaReader: no candidates for markerA");
            return false;
        }

        // Проход 2: пишем маркер B (такой же длинный)
        string markerB = "WBMB_" + (Environment.TickCount % 100000) + "_PADDING_FOR_BUFFER_SIZE";
        _hook.ExecuteLua($"EditMacro(1, 'WB', 1, {LuaConcatMarker(markerB)})", 500);
        System.Threading.Thread.Sleep(300);

        // Проверяем кандидатов из прохода 1
        foreach (uint addr in candidates)
        {
            string val = _memory.ReadString(addr, markerB.Length + 5);
            if (val.StartsWith(markerB))
            {
                _macroAddr = addr;
                _initialized = true;
                Logger.Info($"LuaReader: found macro at 0x{addr:X8}");
                return true;
            }
        }

        // Кандидаты из прохода 1 не совпали — полный скан на markerB
        Logger.Info($"LuaReader: candidates stale, full scan for markerB");
        var freshCandidates = ScanForAllStrings(System.Text.Encoding.UTF8.GetBytes(markerB));
        if (freshCandidates.Count > 0)
        {
            _macroAddr = freshCandidates[^1];
            _initialized = true;
            Logger.Info($"LuaReader: found macro via full scan at 0x{_macroAddr:X8} ({freshCandidates.Count} matches)");
            return true;
        }

        Logger.Warn($"LuaReader: markerB not found (attempt had {candidates.Count} stale candidates)");
        return false;
    }

    /// <summary>
    /// Выполняет Lua, результат должен быть записан в WB_R.
    /// Lua скрипт должен заканчиваться: EditMacro(1,'WB',1,WB_R)
    /// </summary>
    public string? Execute(string luaCode, int timeoutMs = 2000)
    {
        if (!_initialized) return null;

        // Очищаем макрос ЧЕРЕЗ LUA (не WriteString — WoW может сдвинуть буфер)
        string clearTag = "WB_CLR";
        _hook.ExecuteLua($"EditMacro(1,'WB',1,'{clearTag}')", 500);
        System.Threading.Thread.Sleep(200);

        // Проверяем что адрес актуален — если нет, пересканим
        string check = _memory.ReadString(_macroAddr, clearTag.Length + 2);
        if (!check.StartsWith(clearTag))
        {
            // Адрес сдвинулся — ищем заново
            var newCandidates = ScanForAllStrings(System.Text.Encoding.UTF8.GetBytes(clearTag));
            if (newCandidates.Count > 0)
            {
                _macroAddr = newCandidates[^1];
                Logger.Info($"LuaReader: macro addr shifted to 0x{_macroAddr:X8}");
            }
            else
            {
                Logger.Warn("LuaReader: lost macro address");
                return null;
            }
        }

        // Выполняем Lua (должен записать результат в макрос)
        _hook.ExecuteLua(luaCode, timeoutMs);
        System.Threading.Thread.Sleep(300);

        // Читаем результат — пробуем дважды
        for (int i = 0; i < 2; i++)
        {
            string result = _memory.ReadString(_macroAddr, 255);
            if (!string.IsNullOrEmpty(result) && result != clearTag)
                return result;
            System.Threading.Thread.Sleep(200);
        }

        // Последняя попытка — может адрес опять сдвинулся
        // Ищем паттерн результата (класс содержит '|')
        Logger.Warn("LuaReader: result not at expected addr, trying rescan");
        return null;
    }

    /// <summary>
    /// Быстрый хелпер: выполнить Lua выражение и вернуть строку
    /// </summary>
    public string? Eval(string luaExpression)
    {
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
