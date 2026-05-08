// MITM-прокси WoW 3.3.5a — точка входа.
//
// Logon-прокси (3724) + 2-я лега к wowcircle + world-прокси (8085) с двумя K.
// Реальный клиент проходит через нас полностью. Видим SMSG/CMSG, можем инъектить.
//
// Команды в stdin (доступны после входа в мир):
//   say <текст>           — отправить в /say
//   yell <текст>          — отправить в /yell
//   g <текст>             — гильдчат
//   p <текст>             — пати-чат
//   w <ник> <текст>       — личка
//   raw <opcode_hex>      — отправить пустой CMSG с заданным opcode (для тестов)
//   help                  — список команд
//
// Запуск:
//   dotnet run --% -- <account> <password> [--realm x100] [--verbose]

namespace WowBot.MitmProxy;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        // Для cp866/cp1251 поддержки нужно зарегистрировать CodePagesEncodingProvider.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Output — UTF-8 (чтобы кириллица с сервера отображалась корректно).
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Input — НЕ форсим UTF-8! PowerShell пишет в stdin в OEM (cp866 на русской винде).
        // Берём текущую кодовую страницу консоли — что бы там ни было.
        try
        {
            var oemCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            Console.InputEncoding = System.Text.Encoding.GetEncoding(oemCp);
        }
        catch { /* fallback: оставим default */ }

        // Tee Console.Out → файл mitm.log (рядом с проектом). Перезаписывается на каждый запуск.
        var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "mitm.log");
        logPath = System.IO.Path.GetFullPath(logPath);
        var logFile = new System.IO.StreamWriter(logPath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
        logFile.WriteLine($"=== MitmProxy started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        Console.SetOut(new TeeWriter(Console.Out, logFile));
        Console.WriteLine($"[main] log file: {logPath}");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: MitmProxy <account> <password> [--realm <pattern>] [--verbose]");
            return 1;
        }
        var account = args[0];
        var password = args[1];

        var realmPattern = GetArg(args, "--realm") ?? "x100";
        var logonPort = int.Parse(GetArg(args, "--logon-port") ?? "3724");
        var worldPort = int.Parse(GetArg(args, "--world-port") ?? "8085");
        var realHost = GetArg(args, "--real-host") ?? "logon.wowcircle.com";
        var realPort = int.Parse(GetArg(args, "--real-port") ?? "3724");
        WorldProxy.LogVerbose = args.Contains("--verbose");

        Console.WriteLine($"[main] account={account.ToUpper()} realm-pattern='{realmPattern}'");
        Console.WriteLine($"[main] logon proxy :{logonPort} | world proxy :{worldPort} | real-server {realHost}:{realPort}");

        Session? activeSession = null;

        var logon = new LogonProxy(
            port: logonPort,
            account: account,
            password: password,
            fakeRealmAddress: $"127.0.0.1:{worldPort}",
            fakeRealmName: "MITM-Test",
            realServerHost: realHost,
            realServerPort: realPort,
            onSession: s =>
            {
                var picked = s.Realms.FirstOrDefault(r =>
                    r.Name.Contains(realmPattern, StringComparison.OrdinalIgnoreCase) &&
                    !r.Name.Contains('['));
                if (picked == null && s.Realms.Count > 0)
                    picked = s.Realms.OrderByDescending(r => r.NumChars).First();
                s.PickedRealm = picked;
                activeSession = s;
                Console.WriteLine($"[main] picked realm: {(picked != null ? $"#{picked.Id} '{picked.Name}' @ {picked.Address}" : "<none>")}");
            });

        var world = new WorldProxy(worldPort, () => activeSession);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var logonTask = logon.RunAsync(cts.Token);
        var worldTask = world.RunAsync(cts.Token);
        var stdinTask = StdinLoopAsync(world, cts.Token);

        try
        {
            await Task.WhenAny(logonTask, worldTask, stdinTask);
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("[main] shutdown");
        return 0;
    }

    private static async Task StdinLoopAsync(WorldProxy world, CancellationToken ct)
    {
        Console.WriteLine("[stdin] type 'help' after entering the world");
        while (!ct.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(ct);
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;

            var bridge = world.ActiveBridge;
            if (bridge == null && line != "help")
            {
                Console.WriteLine("[stdin] no active bridge — log in first");
                continue;
            }

            try
            {
                var parts = line.Split(' ', 2);
                var cmd = parts[0].ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1] : "";
                switch (cmd)
                {
                    case "help":
                        Console.WriteLine("  say <msg>      | yell <msg>     | g <msg>     | p <msg>");
                        Console.WriteLine("  w <name> <msg> | raw <hexop>");
                        break;
                    case "say":  await bridge!.SendChatSayAsync(arg); break;
                    case "yell": await bridge!.SendChatYellAsync(arg); break;
                    case "g":    await bridge!.SendChatGuildAsync(arg); break;
                    case "p":    await bridge!.SendChatPartyAsync(arg); break;
                    case "w":
                    {
                        var ws = arg.Split(' ', 2);
                        if (ws.Length < 2) { Console.WriteLine("usage: w <name> <msg>"); break; }
                        await bridge!.SendChatWhisperAsync(ws[0], ws[1]); break;
                    }
                    case "raw":
                    {
                        if (!uint.TryParse(arg, System.Globalization.NumberStyles.HexNumber, null, out var op))
                        { Console.WriteLine("usage: raw <hex_opcode>"); break; }
                        await bridge!.SendCmsgAsync(op, Array.Empty<byte>());
                        Console.WriteLine($"[stdin] sent empty CMSG 0x{op:X4}");
                        break;
                    }
                    default:
                        Console.WriteLine($"[stdin] unknown: '{cmd}' — type 'help'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[stdin] error: {ex.Message}");
            }
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}

/// <summary>Дублирует записи в несколько TextWriter одновременно (консоль + файл).</summary>
internal sealed class TeeWriter : System.IO.TextWriter
{
    private readonly System.IO.TextWriter[] _writers;
    public TeeWriter(params System.IO.TextWriter[] writers) { _writers = writers; }
    public override System.Text.Encoding Encoding => _writers[0].Encoding;
    public override void Write(char value) { foreach (var w in _writers) w.Write(value); }
    public override void Write(string? value) { foreach (var w in _writers) w.Write(value); }
    public override void WriteLine(string? value) { foreach (var w in _writers) w.WriteLine(value); }
    public override void WriteLine() { foreach (var w in _writers) w.WriteLine(); }
    public override void Flush() { foreach (var w in _writers) w.Flush(); }
}
