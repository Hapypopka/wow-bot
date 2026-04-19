using System.Collections.Concurrent;

namespace WowBot.Core;

/// <summary>
/// Категории логов — можно включать/выключать по отдельности
/// </summary>
[Flags]
public enum LogCat
{
    None = 0,
    General = 1 << 0,     // общие (аттач, хук, скрипты)
    Rotation = 1 << 1,    // ротация (ExecRotation)
    Heal = 1 << 2,        // хилер логика
    Tank = 1 << 3,        // танк логика (taunt, def CD)
    AoE = 1 << 4,         // AoE avoidance, ground AoE
    Follow = 1 << 5,      // follow/CTM
    Hivemind = 1 << 6,    // hivemind команды
    Buffs = 1 << 7,       // баффы
    Position = 1 << 8,    // позиционирование (MoveBehind)
    Combat = 1 << 9,      // бой (SmartTaunt, BreakCC)
    Lua = 1 << 10,        // Lua консоль
    Error = 1 << 11,      // ошибки — ВСЕГДА включено

    // Пресеты
    All = ~0,
    Default = General | Rotation | Heal | Tank | AoE | Combat | Buffs | Follow | Position | Error,
    Minimal = General | Error,
    Debug = All,
}

public static class Logger
{
    private static string _logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "wowbot.log");
    private static string _markPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "wowbot_mark.log");
    private static readonly object Lock = new();
    private static string _charName = "";
    private static StreamWriter? _writer;
    private static StreamWriter? _markWriter;
    public static bool IsMarkActive => _markWriter != null;

    // Фильтр — какие категории логировать (по дефолту Default)
    public static LogCat EnabledCategories { get; set; } = LogCat.Default;

    // Ring buffer — последние N логов в памяти (для UI)
    private static readonly ConcurrentQueue<string> RecentLogs = new();
    private const int MaxRecentLogs = 200;

    public static void SetCharName(string name)
    {
        _charName = name ?? "";
        if (!string.IsNullOrEmpty(_charName))
        {
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"wowbot_{_charName}.log");
            _markPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"wowbot_{_charName}_mark.log");
        }
    }

    /// <summary>Открывает mark-файл (труncate). Все последующие записи Log() дублируются туда
    /// до StopMark(). Используется для пометки интересных интервалов юзером.</summary>
    public static void StartMark()
    {
        lock (Lock)
        {
            try { _markWriter?.Dispose(); } catch { }
            _markWriter = null;
            try
            {
                _markWriter = new StreamWriter(_markPath, append: false) { AutoFlush = true };
                _markWriter.WriteLine($"=== MARK START [{_charName}] — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            catch { _markWriter = null; }
        }
        Log(LogCat.General, "USER MARK: started");
    }

    public static void StopMark()
    {
        Log(LogCat.General, "USER MARK: stopped");
        lock (Lock)
        {
            try
            {
                _markWriter?.WriteLine($"=== MARK END — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _markWriter?.Dispose();
            }
            catch { }
            _markWriter = null;
        }
    }

    public static void Init()
    {
        lock (Lock)
        {
            _writer?.Dispose();
            _writer = null;
            try { File.WriteAllText(_logPath, $"=== WowBot Log [{_charName}] — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n"); }
            catch { }
        }
    }

    // === Старые методы (совместимость) — категория General ===
    public static void Info(string msg) => Log(LogCat.General, msg);
    public static void Warn(string msg) => Log(LogCat.General, msg, "WARN");
    public static void Error(string msg) => Log(LogCat.Error, msg, "ERR");
    public static void Error(string msg, Exception ex) => Log(LogCat.Error, $"{msg}: {ex.Message}", "ERR");

    // === Новые методы с категорией ===
    public static void Log(LogCat cat, string msg, string level = "INFO")
    {
        // Error ВСЕГДА логируется
        if (cat != LogCat.Error && (EnabledCategories & cat) == 0) return;

        var line = $"[{DateTime.Now:HH:mm:ss}] {level} [{cat}] {(string.IsNullOrEmpty(_charName) ? "" : $"[{_charName}] ")}{msg}";

        try
        {
            lock (Lock)
            {
                if (_writer != null)
                    _writer.WriteLine(line);
                else
                    File.AppendAllText(_logPath, line + "\n");
                // Дублируем в mark-файл если активен
                if (_markWriter != null)
                {
                    try { _markWriter.WriteLine(line); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            // Последний шанс — в консоль
            try { Console.Error.WriteLine($"Logger FAIL: {ex.Message} | {line}"); } catch { }
        }

        // Ring buffer
        RecentLogs.Enqueue(line);
        while (RecentLogs.Count > MaxRecentLogs)
            RecentLogs.TryDequeue(out _);
    }

    /// <summary>Получить последние N логов (для UI !log команды)</summary>
    public static string[] GetRecentLogs(int count = 20)
    {
        return RecentLogs.TakeLast(count).ToArray();
    }

    /// <summary>Получить последние логи по категории</summary>
    public static string[] GetRecentLogs(LogCat cat, int count = 20)
    {
        return RecentLogs.Where(l => l.Contains($"[{cat}]")).TakeLast(count).ToArray();
    }

    public static void Dispose()
    {
        lock (Lock)
        {
            _writer?.Dispose();
            _writer = null;
            try { _markWriter?.WriteLine($"=== MARK END (Dispose) — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==="); } catch { }
            _markWriter?.Dispose();
            _markWriter = null;
        }
    }
}
