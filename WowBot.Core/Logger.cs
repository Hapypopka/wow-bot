using System.Text;

namespace WowBot.Core;

public static class Logger
{
    private static string _logPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "wowbot.log");
    private static readonly object Lock = new();
    private static string _charName = "";

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
            File.WriteAllText(_logPath, $"=== WowBot Log [{_charName}] — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERR ", msg);
    public static void Error(string msg, Exception ex) => Write("ERR ", $"{msg}: {ex.Message}");

    private static void Write(string level, string msg)
    {
        try
        {
            var prefix = string.IsNullOrEmpty(_charName) ? "" : $"[{_charName}] ";
            lock (Lock)
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {level} {prefix}{msg}\n");
            }
        }
        catch { }
    }
}
