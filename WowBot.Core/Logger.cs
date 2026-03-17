using System.Text;

namespace WowBot.Core;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "wowbot.log");
    private static readonly object Lock = new();

    public static void Init()
    {
        lock (Lock)
        {
            File.WriteAllText(LogPath, $"=== WowBot Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
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
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {level} {msg}\n");
            }
        }
        catch { }
    }
}
