// Headless WoW 3.3.5a POC.
// Делает: logon → SRP6 → realmlist → world auth → CMSG_CHAR_ENUM → печать персонажей.
// НЕ делает: вход в мир, движение, кастинг.
//
// Запуск: dotnet run -- <account> <password> [logon_host=127.0.0.1] [logon_port=3724] [realm_id=auto]

namespace WowBot.HeadlessPoc;

internal static class Program
{
    private const ushort ClientBuild = 12340; // 3.3.5a

    static async Task<int> Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "nav-test") return NavTest.Run();
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: HeadlessPoc <account> <password> [logon_host] [logon_port] [realm_id]");
            Console.WriteLine("       HeadlessPoc nav-test  (проверить связь с AmeisenNavServer)");
            return 1;
        }

        var account = args[0].ToUpperInvariant();
        var password = args[1].ToUpperInvariant();
        var logonHost = args.Length > 2 ? args[2] : "127.0.0.1";
        var logonPort = args.Length > 3 ? int.Parse(args[3]) : 3724;
        byte? wantedRealmId = args.Length > 4 ? byte.Parse(args[4]) : null;

        Console.WriteLine($"[POC] Connecting to {logonHost}:{logonPort} as '{account}'");

        try
        {
            // ---- Stage 1+2: logon + realmlist (через LogonClient) ----
            var logon = await LogonClient.LoginAsync(logonHost, logonPort, account, password);
            var sessionKey = logon.SessionKey;
            var realms = logon.Realms;
            Console.WriteLine($"[POC] <- logon OK. {realms.Count} realm(s) total");

            // ---- Stage 2: pick realm ----
            Realm? target;
            if (wantedRealmId.HasValue)
            {
                target = realms.FirstOrDefault(r => r.Id == wantedRealmId.Value);
                if (target == null) { Console.WriteLine($"[POC] !! realm id={wantedRealmId} not found"); return 4; }
            }
            else
            {
                target = realms.OrderByDescending(r => r.NumChars).FirstOrDefault(r => r.NumChars > 0);
                if (target == null) { Console.WriteLine("[POC] !! no realm with characters found"); return 4; }
            }
            Console.WriteLine($"[POC] picked realm #{target.Id} '{target.Name}' (chars={target.NumChars}) @ {target.Address}");

            // ---- Stage 3: world server ----
            var (worldHost, worldPort) = LogonClient.ParseAddress(target.Address);
            using var world = new WorldClient(ClientBuild);

            // NavQuery — опционально, если указана MMAP_DIR с навмешами
            var mmapDir = Environment.GetEnvironmentVariable("MMAP_DIR")
                          ?? @"D:\SPP\SPP_Classics_V2\SPP_Server\Modules\wotlk\mmaps";
            if (Directory.Exists(mmapDir))
            {
                world.Nav = new Nav.NavQuery(mmapDir);
                Console.WriteLine($"[POC] NavQuery подключён: {mmapDir}");
            }

            // Warden CR база — опционально. Если указан CR_FILE с путём к .cr из vmangos/warden_modules,
            // обрабатываем HASH_REQUEST через lookup и переключаем RC4 keys → проходим Warden handshake.
            var crFile = Environment.GetEnvironmentVariable("CR_FILE");
            if (!string.IsNullOrEmpty(crFile) && File.Exists(crFile))
            {
                try
                {
                    world.WardenCr = WardenCrFile.Load(crFile);
                    Console.WriteLine($"[POC] Warden CR подключён: {crFile} ({world.WardenCr.Entries.Count} pre-computed responses)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[POC] !! не удалось загрузить CR: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[POC] CR_FILE не задан — Warden пассивен (kick через ~2 минуты)");
            }

            // Pinned response — захваченная через MITM пара (seed → reply) для конкретного сервера/модуля.
            // Используется когда seed сервера НЕ входит в pool vmangos .cr (как у WoWCircle).
            // Файл: 36 байт = seed[16] + reply[20]. Пишется MitmProxy.HandleCmsgWardenPlaintext.
            var pinnedFile = Environment.GetEnvironmentVariable("WARDEN_PINNED_FILE");
            if (!string.IsNullOrEmpty(pinnedFile) && File.Exists(pinnedFile))
            {
                var bytes = File.ReadAllBytes(pinnedFile);
                if (bytes.Length == 36)
                {
                    var seed = bytes.AsSpan(0, 16).ToArray();
                    var reply = bytes.AsSpan(16, 20).ToArray();
                    world.WardenPinnedResponse = (seed, reply);
                    Console.WriteLine($"[POC] Warden PINNED подключён: seed={Convert.ToHexString(seed)[..16]}.. reply={Convert.ToHexString(reply)[..16]}..");
                }
                else
                {
                    Console.WriteLine($"[POC] !! WARDEN_PINNED_FILE плохого размера {bytes.Length} (ожидалось 36)");
                }
            }

            await world.ConnectAndAuthAsync(worldHost, worldPort, account, target.Id, sessionKey);
            var characters = await world.GetCharactersAsync();

            Console.WriteLine($"\n=== {characters.Count} character(s) on '{target.Name}' ===");
            foreach (var c in characters)
            {
                Console.WriteLine($"  guid=0x{c.Guid:X16}  {c.Name,-15}  lvl {c.Level,2}  {RaceName(c.Race),-10} {ClassName(c.Class),-12}  map={c.Map}");
            }

            if (characters.Count == 0) return 0;

            // ---- Stage 4: enter world ----
            var first = characters[0];
            Console.WriteLine($"\n[POC] entering world as '{first.Name}' (guid 0x{first.Guid:X16})");
            var stats = await world.EnterWorldAsync(first.Guid);
            Console.WriteLine($"  position : map={stats.Map} ({stats.X:F1}, {stats.Y:F1}, {stats.Z:F1})");

            // ---- Stage 5: optional chat send ----
            var sayMsg = Environment.GetEnvironmentVariable("SAY");
            var yellMsg = Environment.GetEnvironmentVariable("YELL");
            var whisperTo = Environment.GetEnvironmentVariable("WHISPER_TO");
            var whisperMsg = Environment.GetEnvironmentVariable("WHISPER_MSG");

            if (!string.IsNullOrEmpty(sayMsg)) await world.SayAsync(sayMsg);
            if (!string.IsNullOrEmpty(yellMsg)) await world.YellAsync(yellMsg);
            if (!string.IsNullOrEmpty(whisperTo) && !string.IsNullOrEmpty(whisperMsg))
                await world.WhisperAsync(whisperTo, whisperMsg);

            if (Environment.GetEnvironmentVariable("FRIENDS") == "1")
                await world.ContactListAsync();
            var whoMin = Environment.GetEnvironmentVariable("WHO_MIN");
            var whoMax = Environment.GetEnvironmentVariable("WHO_MAX");
            if (!string.IsNullOrEmpty(whoMin) && !string.IsNullOrEmpty(whoMax))
                await world.WhoAsync(uint.Parse(whoMin), uint.Parse(whoMax),
                    Environment.GetEnvironmentVariable("WHO_NAME") ?? "");

            // ---- Stage 6: heartbeat ON (нужен для движения) + опц. движение ----
            world.StartHeartbeat();

            // дать серверу осознать что мы залогинились (LOGIN_VERIFY_WORLD должен прийти)
            await Task.Delay(2000);

            if (int.TryParse(Environment.GetEnvironmentVariable("MOVE_FORWARD"), out var mf) && mf > 0)
                await world.MoveForwardAsync(TimeSpan.FromSeconds(mf));

            var moveTo = Environment.GetEnvironmentVariable("MOVE_TO");
            if (!string.IsNullOrEmpty(moveTo))
            {
                var parts = moveTo.Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var tx)
                                       && float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var ty))
                    await world.MoveToAsync(tx, ty);
            }

            world.CommandMode = Environment.GetEnvironmentVariable("COMMAND_MODE") == "1";
            var idleSeconds = int.TryParse(Environment.GetEnvironmentVariable("IDLE_SEC"), out var s) ? s : 90;
            Console.WriteLine($"\n[POC] idle {idleSeconds}s +heartbeat{(world.CommandMode ? " +commands" : "")}");
            await world.IdleAsync(TimeSpan.FromSeconds(idleSeconds));
            await world.StopHeartbeatAsync();

            // Анализ опкодов — ищем кандидаты на anti-cheat ping
            Console.WriteLine($"\n=== opcode stats ===");
            Console.WriteLine($"  opcode  count   first    last  interval  size  hex");
            foreach (var kv in world.OpcodeStatistics
                .OrderBy(p => p.Value.FirstAt)
                .Where(p => p.Value.Count >= 1))
            {
                var op = kv.Key;
                var st = kv.Value;
                var interval = st.Count > 1 ? (st.LastAt - st.FirstAt) / (st.Count - 1) : 0;
                var hex = st.FirstBodyHex != null ? Convert.ToHexString(st.FirstBodyHex) : "";
                Console.WriteLine($"  0x{op:X4} {st.Count,5}  {st.FirstAt,6:F1}  {st.LastAt,6:F1}    {interval,6:F1}  {st.FirstBodySize,4}  {hex}");
            }

            // Финальный отчёт о мире
            Console.WriteLine($"\n=== world snapshot ({world.World.Count} entities) ===");
            var snap = world.World.Snapshot();
            var grouped = snap.GroupBy(e => e.Type).OrderByDescending(g => g.Count());
            foreach (var g in grouped)
                Console.WriteLine($"  {g.Key}: {g.Count()}");

            var units = snap.Where(e => e.Type == WowObjectType.Unit || e.Type == WowObjectType.Player)
                            .Where(e => e.MaxHealth > 0 || e.Level > 0)
                            .Take(15);
            foreach (var u in units)
            {
                var hpStr = u.MaxHealth > 0 ? $" {u.Health}/{u.MaxHealth}" : "";
                Console.WriteLine($"  [{u.Type}] guid=0x{u.Guid:X16} entry={u.Entry} lvl{u.Level}{hpStr} ({u.X:F0},{u.Y:F0},{u.Z:F0})");
            }
            Console.WriteLine($"[POC] idle done, still connected — heartbeat works ✓");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[POC] FAILED: {ex.GetType().Name}: {ex.Message}");
            return 99;
        }
    }

    private static string RaceName(byte r) => r switch
    {
        1 => "Human", 2 => "Orc", 3 => "Dwarf", 4 => "NightElf",
        5 => "Undead", 6 => "Tauren", 7 => "Gnome", 8 => "Troll",
        10 => "BloodElf", 11 => "Draenei", _ => $"r{r}"
    };

    private static string ClassName(byte c) => c switch
    {
        1 => "Warrior", 2 => "Paladin", 3 => "Hunter", 4 => "Rogue",
        5 => "Priest", 6 => "DeathKnight", 7 => "Shaman", 8 => "Mage",
        9 => "Warlock", 11 => "Druid", _ => $"c{c}"
    };
}
