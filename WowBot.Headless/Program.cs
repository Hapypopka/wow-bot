// Headless entry-point.
// Phase A walking skeleton: подключается к серверу, входит в мир, демонстрирует IObjectManager.
// В Phase B/C сюда подключим IGameActions и BotEngine.
//
// Запуск: dotnet run -- <account> <password> [logon_host=127.0.0.1] [logon_port=3724] [realm_id=auto]

using WowBot.Adapter.Headless;
using WowBot.HeadlessPoc;

namespace WowBot.Headless;

internal static class Program
{
    private const ushort ClientBuild = 12340;

    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: WowBot.Headless <account> <password> [logon_host] [logon_port] [realm_id]");
            return 1;
        }

        var account = args[0];
        var password = args[1];
        var logonHost = args.Length > 2 ? args[2] : "127.0.0.1";
        var logonPort = args.Length > 3 ? int.Parse(args[3]) : 3724;
        byte? wantedRealmId = args.Length > 4 ? byte.Parse(args[4]) : null;

        Console.WriteLine($"[BOT] Connecting to {logonHost}:{logonPort} as '{account.ToUpperInvariant()}'");

        try
        {
            // ---- Logon ----
            var logon = await LogonClient.LoginAsync(logonHost, logonPort, account, password);
            Console.WriteLine($"[BOT] logon OK. {logon.Realms.Count} realm(s)");

            // ---- Pick realm ----
            var target = wantedRealmId.HasValue
                ? logon.Realms.FirstOrDefault(r => r.Id == wantedRealmId.Value)
                : logon.Realms.OrderByDescending(r => r.NumChars).FirstOrDefault(r => r.NumChars > 0);
            if (target == null) { Console.WriteLine("[BOT] !! realm not found"); return 4; }
            Console.WriteLine($"[BOT] picked realm #{target.Id} '{target.Name}' @ {target.Address}");

            // ---- World server ----
            var (worldHost, worldPort) = LogonClient.ParseAddress(target.Address);
            using var world = new WorldClient(ClientBuild);

            // NavQuery опционально
            var mmapDir = Environment.GetEnvironmentVariable("MMAP_DIR")
                          ?? @"D:\SPP\SPP_Classics_V2\SPP_Server\Modules\wotlk\mmaps";
            if (Directory.Exists(mmapDir))
            {
                world.Nav = new WowBot.HeadlessPoc.Nav.NavQuery(mmapDir);
                Console.WriteLine($"[BOT] NavQuery подключён: {mmapDir}");
            }

            // Warden — CR + pinned response
            var crFile = Environment.GetEnvironmentVariable("CR_FILE");
            if (!string.IsNullOrEmpty(crFile) && File.Exists(crFile))
            {
                world.WardenCr = WardenCrFile.Load(crFile);
                Console.WriteLine($"[BOT] Warden CR: {world.WardenCr.Entries.Count} responses");
            }
            var pinnedFile = Environment.GetEnvironmentVariable("WARDEN_PINNED_FILE");
            if (!string.IsNullOrEmpty(pinnedFile) && File.Exists(pinnedFile))
            {
                var bytes = File.ReadAllBytes(pinnedFile);
                if (bytes.Length == 36)
                {
                    world.WardenPinnedResponse = (bytes.AsSpan(0, 16).ToArray(), bytes.AsSpan(16, 20).ToArray());
                    Console.WriteLine($"[BOT] Warden pinned response подключён");
                }
            }

            // Phase B: подписка на spell-события для tracking результатов кастов
            world.SpellStarted   += (caster, sid) => { /* лог уже в WorldClient */ };
            world.SpellSucceeded += (caster, sid) => { /* лог уже в WorldClient */ };
            world.SpellFailed    += (caster, sid, reason) => { /* лог уже в WorldClient */ };

            await world.ConnectAndAuthAsync(worldHost, worldPort, account.ToUpperInvariant(), target.Id, logon.SessionKey);
            var characters = await world.GetCharactersAsync();
            Console.WriteLine($"[BOT] {characters.Count} character(s) на realm");
            if (characters.Count == 0) return 0;

            var first = characters[0];
            Console.WriteLine($"[BOT] entering world as '{first.Name}'");
            var stats = await world.EnterWorldAsync(first.Guid);
            Console.WriteLine($"[BOT] in world. map={stats.Map} pos=({stats.X:F1},{stats.Y:F1},{stats.Z:F1})");

            // ---- Phase A smoke-test: IObjectManager поверх WorldState ----
            var objectManager = new HeadlessObjectManager(world.WorldState, () => world.LocalPlayerGuid);
            objectManager.Update();
            var lp = objectManager.LocalPlayer;

            Console.WriteLine($"\n=== [PHASE A] IObjectManager smoke-test ===");
            Console.WriteLine($"  LocalPlayerGuid = 0x{objectManager.LocalPlayerGuid:X16}");
            if (lp != null)
            {
                Console.WriteLine($"  LocalPlayer.HP    = {lp.Health}/{lp.MaxHealth} ({lp.HealthPercent:F1}%)");
                Console.WriteLine($"  LocalPlayer.Level = {lp.Level}");
                Console.WriteLine($"  LocalPlayer.Pos   = ({lp.X:F1}, {lp.Y:F1}, {lp.Z:F1})");
                Console.WriteLine($"  LocalPlayer.Name  = '{lp.Name}'");
                Console.WriteLine($"  Units rows        = {objectManager.Units.Count}");
                Console.WriteLine($"  Players rows      = {objectManager.Players.Count}");
                Console.WriteLine($"  DynObjects rows   = {objectManager.DynObjects.Count}");
                Console.WriteLine($"  IsValid           = {objectManager.IsValid()}");
            }
            else
            {
                Console.WriteLine($"  !! LocalPlayer == null. WorldState.Count={world.WorldState.Count}");
            }

            // Heartbeat нужен для движения и для стабильного коннекта
            world.StartHeartbeat();

            // Запускаем IdleAsync В ФОНЕ — он читает SMSG и наполняет WorldState через UpdateObjectParser.
            // Phase B test параллельно шлёт CMSG (под _sendLock в WorldClient) — race-free.
            using var bgCts = new CancellationTokenSource();
            var bgIdle = Task.Run(() => world.IdleAsync(TimeSpan.FromSeconds(60)));

            // 10 секунд на накопление WorldState (CREATE_OBJECT для всех мобов вокруг).
            // SetSelection на манекен может ускорить — сервер шлёт UpdateObject для таргета.
            await Task.Delay(10000);
            objectManager.Update();
            Console.WriteLine($"[BOT] WorldState прогрет: Units={objectManager.Units.Count} Players={objectManager.Players.Count}");

            // Дамп всех NPC unit для диагностики
            var npcs = objectManager.Units.Where(u => u.NpcId > 0).Take(5).ToList();
            Console.WriteLine($"[BOT] NPC юниты в WorldState (первые 5):");
            foreach (var u in npcs)
                Console.WriteLine($"        guid=0x{u.Guid:X16} entry={u.NpcId} lvl={u.Level} hp={u.Health}/{u.MaxHealth}");

            // ---- Phase B smoke-test: IGameActions ----
            var actions = new HeadlessGameActions(world);
            Console.WriteLine($"\n=== [PHASE B] IGameActions smoke-test ===");

            // Найти ближайший Unit (для теста — манекен). Манекены обычно lvl 80 без faction hostility.
            var me = objectManager.LocalPlayer;
            if (me != null)
            {
                // фильтр: только NPC (NpcId/entry > 0), живой, не сами мы.
                // Players имеют entry=0 — отсекаем чтобы случайно не кастовать на проходящего рядом игрока.
                var nearest = objectManager.Units
                    .Where(u => u.Guid != me.Guid && u.IsAlive && u.NpcId > 0)
                    .OrderBy(u => me.DistanceTo(u))
                    .FirstOrDefault();

                if (nearest != null)
                {
                    var dist = me.DistanceTo(nearest);
                    Console.WriteLine($"[B] ближайший Unit: guid=0x{nearest.Guid:X16} entry={nearest.NpcId} lvl={nearest.Level} hp={nearest.Health}/{nearest.MaxHealth} dist={dist:F1}y");

                    // 1. SetTarget
                    Console.WriteLine($"[B] -> SetTarget(0x{nearest.Guid:X16})");
                    await actions.SetTarget(nearest.Guid);
                    await Task.Delay(500);

                    // 2. CastSpell — Curse of Agony (980, rank 1) для Warlock'а на цель
                    const int CurseOfAgony = 980;
                    Console.WriteLine($"[B] -> CastSpell({CurseOfAgony} Curse of Agony) на target");
                    await actions.CastSpell(CurseOfAgony, nearest.Guid);
                    await Task.Delay(2000); // дать DoT тикнуть

                    // 3. AttackTarget — авто-атака
                    Console.WriteLine($"[B] -> AttackTarget(0x{nearest.Guid:X16})");
                    await actions.AttackTarget(nearest.Guid);
                    await Task.Delay(3000);

                    // Re-snapshot — посмотреть, потерял ли манекен HP
                    objectManager.Update();
                    var nearestAfter = objectManager.Units.FirstOrDefault(u => u.Guid == nearest.Guid);
                    if (nearestAfter != null)
                        Console.WriteLine($"[B] target HP: было {nearest.Health}/{nearest.MaxHealth} → стало {nearestAfter.Health}/{nearestAfter.MaxHealth}");

                    // 4. StopAttack
                    Console.WriteLine($"[B] -> StopAttack()");
                    await actions.StopAttack();

                    // 5. ClearTarget
                    Console.WriteLine($"[B] -> ClearTarget()");
                    await actions.ClearTarget();
                    await Task.Delay(300);

                    // 6. Self-buff: Demon Armor (706, rank 1) на себя
                    const int DemonArmor = 706;
                    Console.WriteLine($"[B] -> CastSpell({DemonArmor} Demon Armor) на self");
                    await actions.CastSpell(DemonArmor);  // null target = self
                    await Task.Delay(1500);

                    // 7. /say
                    Console.WriteLine($"[B] -> SendChat(Say, 'Phase B works')");
                    await actions.SendChat(WowBot.Abstractions.Actions.ChatType.Say, "Phase B works");
                }
                else
                {
                    Console.WriteLine($"[B] !! не нашли Unit рядом для теста CastSpell. Только self-buff.");
                    await actions.CastSpell(706);  // Demon Armor self
                    await Task.Delay(1000);
                    await actions.SendChat(WowBot.Abstractions.Actions.ChatType.Say, "Phase B (self only) works");
                }
            }

            // Дождаться завершения фонового idle (он сам выйдет через 60с)
            Console.WriteLine($"\n[BOT] ждём окончания фонового idle (до 60с)...");
            await bgIdle;
            await world.StopHeartbeatAsync();

            // Re-snapshot после idle — посмотреть как изменилось окружение
            objectManager.Update();
            Console.WriteLine($"\n[PHASE A] post-idle snapshot:");
            Console.WriteLine($"  Units = {objectManager.Units.Count}, Players = {objectManager.Players.Count}, DynObjects = {objectManager.DynObjects.Count}");

            // Прямой доступ к WorldState — что реально лежит в нашей entity
            var rawSelf = world.WorldState.Get(world.LocalPlayerGuid);
            Console.WriteLine($"\n[RAW] WorldState.Get(LocalPlayerGuid):");
            if (rawSelf == null)
                Console.WriteLine($"     == null. WorldState.Count={world.WorldState.Count}");
            else
                Console.WriteLine($"     guid=0x{rawSelf.Guid:X16} type={rawSelf.Type} hp={rawSelf.Health}/{rawSelf.MaxHealth} lvl={rawSelf.Level} pos=({rawSelf.X:F1},{rawSelf.Y:F1},{rawSelf.Z:F1}) flags=0x{rawSelf.UnitFlags:X8}");

            Console.WriteLine($"\n[RAW] первые 5 players в WorldState:");
            foreach (var e in world.WorldState.Snapshot().Where(e => e.Type == WowObjectType.Player).Take(5))
                Console.WriteLine($"     guid=0x{e.Guid:X16} hp={e.Health}/{e.MaxHealth} lvl={e.Level} pos=({e.X:F0},{e.Y:F0},{e.Z:F0})");

            Console.WriteLine($"\n[RAW] первые 5 units в WorldState:");
            foreach (var e in world.WorldState.Snapshot().Where(e => e.Type == WowObjectType.Unit).Take(5))
                Console.WriteLine($"     guid=0x{e.Guid:X16} entry={e.Entry} hp={e.Health}/{e.MaxHealth} lvl={e.Level} pos=({e.X:F0},{e.Y:F0},{e.Z:F0})");

            Console.WriteLine($"[BOT] done.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 99;
        }
    }
}
