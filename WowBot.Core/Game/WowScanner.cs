using System.Diagnostics;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

/// <summary>
/// Сканирует запущенные WoW процессы и читает имя персонажа из памяти.
/// Не требует хука — только ReadProcessMemory.
/// </summary>
public static class WowScanner
{
    public record WowProcessInfo(int Pid, string ProcessName, string CharName);

    /// <summary>
    /// Находит все WoW.exe процессы и читает имя персонажа из каждого.
    /// </summary>
    public static List<WowProcessInfo> ScanAll()
    {
        var results = new List<WowProcessInfo>();
        var wowProcesses = Process.GetProcessesByName("WoW");

        foreach (var proc in wowProcesses)
        {
            try
            {
                var mem = new MemoryReader();
                if (!mem.Attach(proc))
                {
                    mem.Detach();
                    continue;
                }

                string charName = mem.ReadString(Offsets.PlayerName, 40);
                mem.Detach();

                // Если имя пустое — персонаж не залогинен
                if (string.IsNullOrWhiteSpace(charName) || charName.Length < 2)
                    charName = "(не залогинен)";

                results.Add(new WowProcessInfo(proc.Id, proc.ProcessName, charName));
            }
            catch
            {
                // Не удалось прочитать — пропускаем
            }
        }

        return results;
    }
}
