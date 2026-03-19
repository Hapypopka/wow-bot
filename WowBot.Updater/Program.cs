using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace WowBot.Updater;

class Program
{
    // Сервер с патчами
    const string SERVER = "http://45.131.187.128:8099";
    const string VERSION_URL = $"{SERVER}/version.txt";
    const string PATCH_URL = $"{SERVER}/patch.zip";
    const string PREV_PATCH_URL = $"{SERVER}/previous_patch.zip";

    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string VersionFile = Path.Combine(AppDir, "version.txt");
    static readonly string BackupDir = Path.Combine(AppDir, "_backup");

    static async Task Main(string[] args)
    {
        Console.Title = "WowBot Updater";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════╗");
        Console.WriteLine("║       WowBot Updater v1.0        ║");
        Console.WriteLine("╚══════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        bool rollback = args.Length > 0 && args[0] == "--rollback";

        if (rollback)
        {
            await DoRollback();
        }
        else
        {
            await DoUpdate();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Нажми любую клавишу...");
        Console.ResetColor();
        Console.ReadKey();
    }

    static async Task DoUpdate()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Проверяем версию
        string localVersion = File.Exists(VersionFile)
            ? File.ReadAllText(VersionFile).Trim()
            : "0";

        Console.Write("Проверяю обновления... ");

        string remoteVersion;
        try
        {
            remoteVersion = (await http.GetStringAsync(VERSION_URL)).Trim();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка: не могу подключиться к серверу.\n{ex.Message}");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"Локальная: v{localVersion} | Сервер: v{remoteVersion}");
        Console.ResetColor();

        if (localVersion == remoteVersion)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ У тебя последняя версия!");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Доступно обновление v{remoteVersion}. Обновить? [Y/n]: ");
        Console.ResetColor();

        var key = Console.ReadKey();
        Console.WriteLine();
        if (key.Key == ConsoleKey.N)
        {
            Console.WriteLine("Отменено.");
            return;
        }

        // Бэкап текущих файлов
        Console.Write("Создаю бэкап... ");
        CreateBackup();
        Console.WriteLine("OK");

        // Скачиваем патч
        Console.Write("Скачиваю патч... ");
        byte[] patchData;
        try
        {
            patchData = await http.GetByteArrayAsync(PATCH_URL);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка скачивания: {ex.Message}");
            Console.ResetColor();
            return;
        }
        Console.WriteLine($"OK ({patchData.Length / 1024} KB)");

        // Распаковываем
        Console.Write("Устанавливаю... ");
        try
        {
            // Распаковываем прямо из памяти — без записи zip на диск (антивирус блокирует)
            using var zipStream = new System.IO.MemoryStream(patchData);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var destPath = Path.Combine(AppDir, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                entry.ExtractToFile(destPath, overwrite: true);
            }

            // Обновляем версию
            File.WriteAllText(VersionFile, remoteVersion);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка: {ex.Message}");
            Console.WriteLine("Восстанавливаю бэкап...");
            RestoreBackup();
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OK");
        Console.WriteLine($"\n✓ Обновлено до v{remoteVersion}! Запускай WowBot.Injector.exe");
        Console.ResetColor();
    }

    static async Task DoRollback()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Откат на предыдущую версию...");
        Console.ResetColor();

        if (Directory.Exists(BackupDir))
        {
            Console.Write("Восстанавливаю из бэкапа... ");
            RestoreBackup();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK\n✓ Откатил на предыдущую версию.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Нет бэкапа для отката.");
            Console.ResetColor();
        }
    }

    static void CreateBackup()
    {
        if (Directory.Exists(BackupDir))
            Directory.Delete(BackupDir, true);
        Directory.CreateDirectory(BackupDir);

        // Бэкапим только DLL и иконки (не весь рантайм)
        string[] toBackup = { "WowBot.Core.dll", "WowBot.Injector.dll", "version.txt" };
        foreach (var file in toBackup)
        {
            var src = Path.Combine(AppDir, file);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(BackupDir, file), true);
        }

        // Бэкап иконок
        var iconsDir = Path.Combine(AppDir, "Icons");
        var backupIcons = Path.Combine(BackupDir, "Icons");
        if (Directory.Exists(iconsDir))
        {
            Directory.CreateDirectory(backupIcons);
            foreach (var f in Directory.GetFiles(iconsDir))
                File.Copy(f, Path.Combine(backupIcons, Path.GetFileName(f)), true);
        }
    }

    static void RestoreBackup()
    {
        if (!Directory.Exists(BackupDir)) return;

        foreach (var file in Directory.GetFiles(BackupDir))
        {
            var dest = Path.Combine(AppDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }

        var backupIcons = Path.Combine(BackupDir, "Icons");
        if (Directory.Exists(backupIcons))
        {
            var iconsDir = Path.Combine(AppDir, "Icons");
            Directory.CreateDirectory(iconsDir);
            foreach (var f in Directory.GetFiles(backupIcons))
                File.Copy(f, Path.Combine(iconsDir, Path.GetFileName(f)), true);
        }
    }
}
