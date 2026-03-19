using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WowBot.Updater;

class Program
{
    const string RELEASES_URL = "https://api.github.com/repos/Hapypopka/wow-bot/releases/latest";

    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string VersionFile = Path.Combine(AppDir, "version.txt");
    static readonly string BackupDir = Path.Combine(AppDir, "_backup");

    static async Task Main(string[] args)
    {
        Console.Title = "WowBot Updater";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════╗");
        Console.WriteLine("║       WowBot Updater             ║");
        Console.WriteLine("╚══════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--rollback")
        {
            DoRollback();
            WaitAndExit();
            return;
        }

        // Проверяем что бот не запущен
        while (true)
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("WowBot.Injector");
            if (procs.Length == 0) break;
            Write("⚠ WowBot запущен! Закрой его и нажми любую клавишу...\n", ConsoleColor.Yellow);
            Console.ReadKey(true);
        }

        await DoUpdate();
        WaitAndExit();
    }

    static async Task DoUpdate()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("User-Agent", "WowBot-Updater");

        string localVersion = File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "0";

        // Проверяем GitHub Release
        Write("Проверяю обновления... ", ConsoleColor.White);

        string remoteVersion;
        string downloadUrl;
        try
        {
            var json = await http.GetStringAsync(RELEASES_URL);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            remoteVersion = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "?";
            downloadUrl = "";

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == "patch.zip")
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            WriteErr($"Не могу подключиться к GitHub.\n{ex.Message}");
            return;
        }

        Console.WriteLine($"v{localVersion} → v{remoteVersion}");

        if (localVersion == remoteVersion)
        {
            Write("✓ У тебя последняя версия!\n", ConsoleColor.Green);
            return;
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            WriteErr("patch.zip не найден в Release.");
            return;
        }

        // Бэкап
        Write("Создаю бэкап... ", ConsoleColor.White);
        CreateBackup();
        Console.WriteLine("OK");

        // Скачиваем
        Write("Скачиваю обновление... ", ConsoleColor.White);
        byte[] patchData;
        try
        {
            patchData = await http.GetByteArrayAsync(downloadUrl);
        }
        catch (Exception ex)
        {
            WriteErr($"Ошибка скачивания: {ex.Message}");
            return;
        }
        Console.WriteLine($"OK ({patchData.Length / 1024} KB)");

        // Устанавливаем
        Write("Устанавливаю... ", ConsoleColor.White);
        try
        {
            using var zipStream = new MemoryStream(patchData);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var destPath = Path.Combine(AppDir, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
            File.WriteAllText(VersionFile, remoteVersion);
        }
        catch (Exception ex)
        {
            WriteErr($"Ошибка установки: {ex.Message}\nВосстанавливаю бэкап...");
            RestoreBackup();
            return;
        }
        Console.WriteLine("OK");

        Console.WriteLine();
        Write($"✓ Обновлено до v{remoteVersion}! Запускай WowBot.Injector.exe\n", ConsoleColor.Green);
    }

    static void DoRollback()
    {
        if (Directory.Exists(BackupDir))
        {
            Write("Откатываю... ", ConsoleColor.Yellow);
            RestoreBackup();
            Write("OK\n✓ Откатил на предыдущую версию.\n", ConsoleColor.Green);
        }
        else
        {
            WriteErr("Нет бэкапа для отката.");
        }
    }

    static void CreateBackup()
    {
        if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
        Directory.CreateDirectory(BackupDir);

        foreach (var file in new[] { "WowBot.Core.dll", "WowBot.Injector.dll", "version.txt" })
        {
            var src = Path.Combine(AppDir, file);
            if (File.Exists(src)) File.Copy(src, Path.Combine(BackupDir, file), true);
        }

        var iconsDir = Path.Combine(AppDir, "Icons");
        if (Directory.Exists(iconsDir))
        {
            var backupIcons = Path.Combine(BackupDir, "Icons");
            Directory.CreateDirectory(backupIcons);
            foreach (var f in Directory.GetFiles(iconsDir))
                File.Copy(f, Path.Combine(backupIcons, Path.GetFileName(f)), true);
        }
    }

    static void RestoreBackup()
    {
        if (!Directory.Exists(BackupDir)) return;
        foreach (var file in Directory.GetFiles(BackupDir))
            File.Copy(file, Path.Combine(AppDir, Path.GetFileName(file)), true);

        var backupIcons = Path.Combine(BackupDir, "Icons");
        if (Directory.Exists(backupIcons))
        {
            var iconsDir = Path.Combine(AppDir, "Icons");
            Directory.CreateDirectory(iconsDir);
            foreach (var f in Directory.GetFiles(backupIcons))
                File.Copy(f, Path.Combine(iconsDir, Path.GetFileName(f)), true);
        }
    }

    static void Write(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    static void WriteErr(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    static void WaitAndExit()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Нажми любую клавишу...");
        Console.ResetColor();
        Console.ReadKey();
    }
}
