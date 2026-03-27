using System.Text;

namespace WowBot.Core;

public static class Logger
{
    private static string _logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "wowbot.log");
    private static readonly object Lock = new();
    private static string _charName = "";

    private static StreamWriter? _writer;
    private static System.Threading.Timer? _flushTimer;

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private const int MaxRotatedFiles = 3;

    public static void SetCharName(string name)
    {
        _charName = name ?? "";
        if (!string.IsNullOrEmpty(_charName))
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"wowbot_{_charName}.log");
    }

    public static void Init()
    {
        lock (Lock)
        {
            CloseWriter();
            _writer = new StreamWriter(_logPath, append: false, Encoding.UTF8)
            {
                AutoFlush = false
            };
            _writer.WriteLine($"=== WowBot Log [{_charName}] — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            _flushTimer?.Dispose();
            _flushTimer = new System.Threading.Timer(_ =>
            {
                lock (Lock)
                {
                    try { _writer?.Flush(); } catch { }
                }
            }, null, 2000, 2000);
        }
    }

    public static void Info(string msg) => Write("INFO", msg, flush: false);
    public static void Warn(string msg) => Write("WARN", msg, flush: false);
    public static void Error(string msg) => Write("ERR ", msg, flush: true);
    public static void Error(string msg, Exception ex) => Write("ERR ", $"{msg}: {ex.Message}", flush: true);

    /// <summary>
    /// Закрыть лог (flush + dispose). Вызывать при выходе.
    /// </summary>
    public static void Shutdown()
    {
        lock (Lock)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            CloseWriter();
        }
    }

    private static void Write(string level, string msg, bool flush)
    {
        try
        {
            var prefix = string.IsNullOrEmpty(_charName) ? "" : $"[{_charName}] ";
            lock (Lock)
            {
                if (_writer == null) return;

                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level} {prefix}{msg}");

                if (flush)
                    _writer.Flush();

                // Check rotation after write
                try
                {
                    if (_writer.BaseStream.Length >= MaxFileSize)
                        Rotate();
                }
                catch { }
            }
        }
        catch { }
    }

    private static void Rotate()
    {
        CloseWriter();

        // Shift existing rotated files: .2 → delete, .1 → .2, current → .1
        for (int i = MaxRotatedFiles - 1; i >= 1; i--)
        {
            string src = $"{_logPath}.{i}";
            string dst = $"{_logPath}.{i + 1}";
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }
            catch { }
        }

        // Current → .1
        try
        {
            string first = $"{_logPath}.1";
            if (File.Exists(first)) File.Delete(first);
            File.Move(_logPath, first);
        }
        catch { }

        // Open fresh file
        _writer = new StreamWriter(_logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = false
        };
        _writer.WriteLine($"=== WowBot Log [{_charName}] — {DateTime.Now:yyyy-MM-dd HH:mm:ss} (rotated) ===");
    }

    private static void CloseWriter()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch { }
        _writer = null;
    }
}
