using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WowBot.Core.Game;
using WowBot.Core.Game.Entities;
using WowBot.Core.Game.Rotations;
using WowBot.Core.Memory;

namespace WowBot.Injector;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly MemoryReader _memory = new();
    private ObjectManager? _objectManager;
    private EndSceneHook? _endSceneHook;
    private BotEngine? _botEngine;
    private LuaReader? _luaReader;
    private DispatcherTimer? _updateTimer;
    private OverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        StopUpdateLoop();
        _overlay?.SaveSettings();
        _overlay?.Close();
        _botEngine?.Dispose();
        _endSceneHook?.Dispose();
        _memory.Dispose();
        Environment.Exit(0);
    }

    private void BtnAttach_Click(object sender, RoutedEventArgs e)
    {
        var wowProcesses = Process.GetProcessesByName("Wow");
        if (wowProcesses.Length == 0)
        {
            TxtStatus.Text = "WoW process not found! Launch WoW first.";
            return;
        }

        Process wow;
        if (wowProcesses.Length == 1)
        {
            wow = wowProcesses[0];
        }
        else
        {
            // Несколько WoW — показываем выбор с именами персонажей
            wow = ShowProcessPicker(wowProcesses);
            if (wow == null) return;
        }
        WowBot.Core.Logger.Init();
        WowBot.Core.Logger.Info($"Attaching to WoW PID={wow.Id}");

        // Используем PROCESS_ALL_ACCESS для записи в память (хук)
        if (!_memory.AttachForInject(wow))
        {
            TxtStatus.Text = $"Failed to attach to PID {wow.Id}. Try running as Administrator.";
            return;
        }

        _objectManager = new ObjectManager(_memory);

        if (!_objectManager.IsValid())
        {
            TxtStatus.Text = "ObjectManager not found. Are you logged in with a character?";
            _memory.Detach();
            _objectManager = null;
            return;
        }

        BtnAttach.IsEnabled = false;
        BtnDetach.IsEnabled = true;
        BtnDump.IsEnabled = true;

        _endSceneHook = new EndSceneHook(_memory);

        // Пробуем установить хук (FindEndScene с автосканом оффсетов)
        try
        {
            _endSceneHook.Install();

            // Диагностика после Install (чтобы показать найденный оффсет)
            string diag = _endSceneHook.GetDiagnostics();
            var diagPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "endscene_diag.txt");
            System.IO.File.WriteAllText(diagPath, diag);
            LstObjects.ItemsSource = diag.Split('\n').ToList();
            TxtStatus.Text = $"Attached + Hooked (PID: {wow.Id})";
            TxtLuaStatus.Text = "Hook active. Enter Lua and press Run or Enter.";
            BtnExecuteLua.IsEnabled = true;

            // Инициализируем LuaReader (чтение Lua через макрос)
            _luaReader = new LuaReader(_memory, _endSceneHook);
            _luaReader.Initialize();

            // Автодетект класса/спека
            string specName = "Unknown";
            string playerClass = "";
            if (_luaReader.IsInitialized)
            {
                // Простой Lua — без анонимных функций
                string lua = "local _,c=UnitClass('player') local _,_,t1=GetTalentTabInfo(1) local _,_,t2=GetTalentTabInfo(2) local _,_,t3=GetTalentTabInfo(3) EditMacro(1,'WB',1,c..'|'..t1..'|'..t2..'|'..t3)";
                string? classInfo = _luaReader.Execute(lua);
                if (classInfo != null && classInfo.Contains("|"))
                {
                    specName = DetectSpec(classInfo);
                    playerClass = classInfo.Split('|')[0];
                }
            }
            TxtStatus.Text = $"Hooked (PID: {wow.Id}) | {specName}";
            bool isHealer = specName.Contains("Holy") || specName.Contains("Disc") || specName.Contains("Resto");
            WowBot.Core.Logger.Info($"Hooked OK | class={playerClass} spec={specName} healer={isHealer}");

            // Инициализируем BotEngine
            WowBot.Core.Logger.Info("Creating BotEngine...");
            var navigation = new Navigation(_memory, _endSceneHook);
            var ctm = new ClickToMove(_memory);
            _botEngine = new BotEngine(_endSceneHook, _objectManager, navigation, ctm);
            _botEngine.IsHealer = isHealer;
            _botEngine.PlayerClass = playerClass;
            _botEngine.LuaReader = _luaReader;
            var fullScript = AllRotations.GetFullScript(playerClass);
            var instantScript = AllRotations.GetInstantScript(playerClass);
            WowBot.Core.Logger.Info($"Scripts generated: full={fullScript.Length} instant={instantScript.Length}");
            _botEngine.LoadRotation(instantScript, fullScript);
            _botEngine.OnStatusChanged += status =>
                Dispatcher.Invoke(() => TxtRotationStatus.Text = status);
            BtnRotationToggle.IsEnabled = true;
            BtnSetFollow.IsEnabled = true;
            BtnClearFollow.IsEnabled = true;
            BtnScanSpells.IsEnabled = true;

            // Открываем оверлей
            WowBot.Core.Logger.Info("Creating overlay...");
            _overlay = new OverlayWindow();
            _overlay.OnRotationToggle += () =>
            {
                if (_botEngine == null) return;
                _botEngine.ToggleRotation();
                _overlay.UpdateRotation(_botEngine.RotationEnabled);
            };
            _overlay.OnFollowToggle += () =>
            {
                if (_botEngine == null) return;
                _botEngine.ToggleFollow();
                _overlay.UpdateFollow(_botEngine.FollowEnabled);
            };
            _overlay.OnSetFollowTarget += () =>
            {
                if (_botEngine == null) return;
                _botEngine.SetFollowTarget();
            };
            _overlay.OnFollowDistanceChanged += (dist) =>
            {
                if (_botEngine != null)
                    _botEngine.FollowDistance = dist;
            };
            _overlay.OnHivemindCommand += (cmd) =>
            {
                if (_botEngine == null) return;
                var hive = _botEngine.Hivemind;
                switch (cmd)
                {
                    case "role:master":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.Master;
                        break;
                    case "role:slave":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.Slave;
                        hive.ResetSlaveState();
                        _botEngine.EnsureRunning();
                        break;
                    case "role:none":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.None;
                        break;
                    case "attack": hive.CmdAttack(); break;
                    case "follow": hive.CmdFollow(); break;
                    case "stop": hive.CmdStop(); break;
                    case "scatter": hive.CmdScatter(); break;
                    case "stack": hive.CmdStack(); break;
                    case "ping": hive.CmdPing(); break;
                }
            };
            WowBot.Core.Logger.Info("Loading overlay settings...");
            _overlay.LoadSettings();
            _overlay.UpdateStatus(specName);
            _overlay.SetPlayerClass(playerClass, specName);
            WowBot.Core.Logger.Info("Showing overlay...");
            _overlay.Show();
            WowBot.Core.Logger.Info("Overlay shown OK");
        }
        catch (Exception ex)
        {
            // Диагностика при ошибке
            string diag = _endSceneHook.GetDiagnostics();
            var diagPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "endscene_diag.txt");
            System.IO.File.WriteAllText(diagPath, diag);
            LstObjects.ItemsSource = diag.Split('\n').ToList();

            WowBot.Core.Logger.Error($"Hook/Init failed: {ex.Message}\n{ex.StackTrace}", ex);
            TxtStatus.Text = $"Attached (PID: {wow.Id}) — hook failed: {ex.Message}";
            TxtLuaStatus.Text = ex.Message.Contains("EndScene")
                ? "Автоскан оффсетов не нашёл EndScene. См. endscene_diag.txt"
                : $"Hook error: {ex.Message}";
        }

        StartUpdateLoop();
    }

    private void BtnDetach_Click(object sender, RoutedEventArgs e)
    {
        StopUpdateLoop();

        // Останавливаем бот и снимаем хук
        _botEngine?.Dispose();
        _botEngine = null;
        _endSceneHook?.Dispose();
        _endSceneHook = null;

        _memory.Detach();
        _objectManager = null;

        TxtStatus.Text = "Not connected";
        TxtLuaStatus.Text = "Hook not installed";
        TxtRotationStatus.Text = "Not active";
        BtnAttach.IsEnabled = true;
        BtnDetach.IsEnabled = false;
        BtnDump.IsEnabled = false;
        BtnExecuteLua.IsEnabled = false;
        BtnRotationToggle.IsEnabled = false;
        BtnRotationToggle.Content = "Start Rotation";
        ClearDisplay();
    }

    // --- Rotation + Follow ---

    private void BtnRotationToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_botEngine == null) return;
        _botEngine.ToggleRotation();
        BtnRotationToggle.Content = _botEngine.RotationEnabled ? "Stop Rotation" : "Start Rotation";
    }

    private void BtnSetFollow_Click(object sender, RoutedEventArgs e)
    {
        if (_botEngine == null) return;
        _botEngine.SetFollowTarget();
    }

    private void BtnClearFollow_Click(object sender, RoutedEventArgs e)
    {
        if (_botEngine == null) return;
        _botEngine.ClearFollowTarget();
    }

    // --- Lua Console ---

    private void BtnExecuteLua_Click(object sender, RoutedEventArgs e)
    {
        ExecuteLuaFromTextBox();
    }

    private void TxtLua_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteLuaFromTextBox();
            e.Handled = true;
        }
    }

    private void BtnQuickLua_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string lua)
        {
            ExecuteLua(lua);
        }
    }

    private void ExecuteLuaFromTextBox()
    {
        string lua = TxtLua.Text.Trim();
        if (string.IsNullOrEmpty(lua)) return;
        ExecuteLua(lua);
    }

    private void ExecuteLua(string lua)
    {
        if (_endSceneHook == null || !_endSceneHook.IsHooked)
        {
            TxtLuaStatus.Text = "Hook not installed!";
            return;
        }

        try
        {
            bool success = _endSceneHook.ExecuteLua(lua);
            TxtLuaStatus.Text = success
                ? $"OK: {lua}"
                : $"Timeout/fail: {lua}";
        }
        catch (Exception ex)
        {
            TxtLuaStatus.Text = $"Error: {ex.Message}";
        }
    }

    // --- Update Loop ---

    private void StartUpdateLoop()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _updateTimer.Tick += UpdateTick;
        _updateTimer.Start();
    }

    private void StopUpdateLoop()
    {
        _updateTimer?.Stop();
        _updateTimer = null;
    }

    private void UpdateTick(object? sender, EventArgs e)
    {
        try
        {
            if (_objectManager == null || !_memory.IsAttached) return;

            // Показывать оверлей только когда WoW активен
            if (_overlay != null && _memory.Process != null)
            {
                var fg = GetForegroundWindow();
                GetWindowThreadProcessId(fg, out uint fgPid);
                bool wowActive = fgPid == (uint)_memory.Process.Id;
                _overlay.Visibility = wowActive ? Visibility.Visible : Visibility.Hidden;
            }

            if (_memory.Process?.HasExited == true)
            {
                BtnDetach_Click(this, new RoutedEventArgs());
                TxtStatus.Text = "WoW process closed.";
                return;
            }

            _objectManager.Update();
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            WowBot.Core.Logger.Error($"UpdateTick error: {ex.Message}\n{ex.StackTrace}");
            TxtStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateDisplay()
    {
        if (_objectManager == null) return;

        var player = _objectManager.LocalPlayer;
        var name = _objectManager.GetPlayerName();

        TxtName.Text = string.IsNullOrEmpty(name) ? "?" : name;

        if (player != null)
        {
            TxtHealth.Text = $"{player.Health} / {player.MaxHealth} ({player.HealthPercent:F0}%)";
            TxtMana.Text = $"{player.Mana} / {player.MaxMana} ({player.ManaPercent:F0}%)";
            TxtLevel.Text = player.Level.ToString();
            TxtPosition.Text = $"X:{player.X:F1}  Y:{player.Y:F1}  Z:{player.Z:F1}  F:{player.Facing:F2}";

            var target = _objectManager.GetTarget();
            if (target != null)
                TxtTarget.Text = $"HP: {target.HealthPercent:F0}% | Dist: {player.DistanceTo(target):F1}yd | Lvl {target.Level}";
            else
                TxtTarget.Text = "No target";
        }

        var items = new List<string>();
        int totalUnits = _objectManager.Units.Count;
        int totalPlayers = _objectManager.Players.Count;

        TxtObjectCount.Text = $"OBJECTS ({totalUnits} units, {totalPlayers} players)";

        foreach (var p in _objectManager.Players.Take(20))
        {
            string marker = p.Guid == _objectManager.LocalPlayerGuid ? " [YOU]" : "";
            items.Add($"[Player] Lvl {p.Level} HP:{p.HealthPercent:F0}%{marker}");
        }

        foreach (var u in _objectManager.Units.ToList()
            .Where(u => u.IsAlive && player != null && player.DistanceTo(u) < 100)
            .OrderBy(u => player != null ? player.DistanceTo(u) : 0)
            .Take(30))
        {
            float dist = player != null ? player.DistanceTo(u) : 0;
            string uName = u.Name;
            string nameTag = !string.IsNullOrEmpty(uName) ? $" \"{uName}\"" : "";
            items.Add($"[Unit]{nameTag} Lvl {u.Level} HP:{u.HealthPercent:F0}% Dist:{dist:F0}yd");
        }

        LstObjects.ItemsSource = items;

        // Обновляем оверлей
        if (_overlay != null && player != null)
        {
            _overlay.UpdateRotation(_botEngine?.RotationEnabled == true);

            // Синхронизируем настройки панели → BotEngine
            if (_botEngine != null)
            {
                _botEngine.AutoFace = _overlay.AutoFace;
                _botEngine.AutoSelectTarget = _overlay.AutoSelectTarget;
                _botEngine.MaxTargetRange = _overlay.MaxTargetRange;
                _botEngine.AoeEnabled = _overlay.AoeEnabled;
                _botEngine.UseMultiDot = _overlay.UseMultiDot;
                _botEngine.MaxDotTargets = _overlay.MaxDotTargets;
                _botEngine.UseMindSear = _overlay.UseMindSear;
                _botEngine.MindSearTargets = _overlay.MindSearTargets;
                _botEngine.DispManaThreshold = _overlay.DispManaThreshold;
                _botEngine.SFManaThreshold = _overlay.SFManaThreshold;
                _botEngine.BuffsEnabled = _overlay.BuffsEnabled;
                _botEngine.SpellFlagsLua = _overlay.GetSpellFlagsLua();
                _botEngine.EnabledBuffs = _overlay.GetEnabledBuffs();
                _botEngine.SelectedSeal = _overlay.SelectedSeal;
                _botEngine.SelectedBlessing = _overlay.SelectedBlessing;
                _botEngine.SelectedAura = _overlay.SelectedAura;
            }

            bool followActive = _botEngine?.FollowEnabled == true;
            string followInfo = "";
            if (followActive && _botEngine != null && _botEngine.FollowGuid != 0)
            {
                var fu = _objectManager.GetUnitByGuid(_botEngine.FollowGuid);
                followInfo = fu != null ? $"{player.DistanceTo(fu):F0}yd" : "lost";
            }
            _overlay.UpdateFollow(followActive, followInfo);

            var tgt = _objectManager.GetTarget();
            string info = $"HP: {player.HealthPercent:F0}%";
            if (tgt != null)
                info += $" | Target: {tgt.HealthPercent:F0}% {player.DistanceTo(tgt):F0}yd";
            _overlay.UpdateInfo(info);
        }
    }

    // Spell ID → English name для всех классов
    private static readonly Dictionary<string, (int id, string eng)[]> ClassSpells = new()
    {
        ["DRUID"] = new[] {
            (48461,"Wrath"), (48465,"Starfire"), (48463,"Moonfire"), (48468,"Insect Swarm"),
            (53201,"Starfall"), (33831,"Force of Nature"), (770,"Faerie Fire"), (24858,"Moonkin Form"),
            (29166,"Innervate"), (22812,"Barkskin"), (61384,"Typhoon"), (48518,"Eclipse Lunar buff"),
            (48517,"Eclipse Solar buff"),
        },
        ["PRIEST"] = new[] {
            (15473,"Shadowform"), (48160,"Vampiric Touch"), (48300,"Devouring Plague"),
            (48125,"Shadow Word: Pain"), (48127,"Mind Blast"), (48156,"Mind Flay"),
            (47585,"Dispersion"), (34433,"Shadowfiend"), (48168,"Shadow Word: Death"),
            (15487,"Silence"), (34914,"Vampiric Embrace"), (48071,"Flash Heal"),
            (48063,"Greater Heal"), (48068,"Renew"),
        },
        ["WARLOCK"] = new[] {
            (47867,"Curse of Agony"), (47864,"Corruption"), (47843,"Unstable Affliction"),
            (47836,"Seed of Corruption"), (47811,"Immolate"), (47838,"Incinerate"),
            (50796,"Chaos Bolt"), (17962,"Conflagrate"), (47855,"Soul Fire"),
            (59164,"Haunt"), (47241,"Metamorphosis"), (47193,"Demonic Empowerment"),
            (57946,"Life Tap"), (47809,"Shadow Bolt"), (47815,"Searing Pain"),
            (48181,"Drain Life"), (47857,"Drain Soul"),
        },
        ["MAGE"] = new[] {
            (42833,"Fireball"), (42897,"Arcane Blast"), (42845,"Arcane Missiles"),
            (42859,"Frostbolt"), (55360,"Living Bomb"), (42891,"Pyroblast"),
            (55342,"Mirror Image"), (12042,"Arcane Power"), (12043,"Presence of Mind"),
            (12051,"Evocation"), (12472,"Icy Veins"), (44457,"Living Bomb r1"),
            (42931,"Cone of Cold"), (42926,"Flamestrike"),
        },
        ["SHAMAN"] = new[] {
            (49238,"Lightning Bolt"), (49271,"Chain Lightning"), (60043,"Lava Burst"),
            (49233,"Flame Shock"), (49231,"Earth Shock"), (16166,"Elemental Mastery"),
            (59159,"Thunderstorm"), (51533,"Feral Spirit"), (49276,"Wind Shear"),
        },
    };

    private void BtnScanSpells_Click(object sender, RoutedEventArgs e)
    {
        if (_luaReader == null || !_luaReader.IsInitialized) return;

        // Определяем класс
        string lua = "local _,c=UnitClass('player') EditMacro(1,'WB',1,c)";
        string? cls = _luaReader.Execute(lua);
        if (cls == null || !ClassSpells.ContainsKey(cls))
        {
            TxtStatus.Text = $"Unknown class: {cls}";
            return;
        }

        TxtStatus.Text = $"Scanning {cls} spells...";
        var spells = ClassSpells[cls];
        var results = new List<string> { $"=== {cls} Spells ===" };

        foreach (var (id, eng) in spells)
        {
            string? name = _luaReader.Eval($"GetSpellInfo({id})");
            results.Add($"{id} = {name ?? "nil"} ({eng})");
        }

        // Сохраняем в файл
        var path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "spells.txt");
        System.IO.File.WriteAllLines(path, results);

        LstObjects.ItemsSource = results;
        TxtStatus.Text = $"Scanned {results.Count - 1} spells → spells.txt";
    }

    private void BtnDump_Click(object sender, RoutedEventArgs e)
    {
        if (!_memory.IsAttached || _endSceneHook == null) return;

        TxtStatus.Text = "Writing macro + scanning memory...";
        BtnDump.IsEnabled = false;

        Task.Run(() =>
        {
            // Пишем уникальную строку в макрос
            _endSceneHook.ExecuteLua("EditMacro(1, 'WB', 1, 'WBTEST9988')", 500);
            Thread.Sleep(200);

            var results = new List<string>();
            results.Add("=== MACRO SCAN — looking for 'WBSCAN7749' ===");

            // Ищем строку в памяти WoW (сканируем основные регионы)
            byte[] needle = System.Text.Encoding.UTF8.GetBytes("WBTEST9988");

            // Сканируем регионы где WoW хранит данные
            uint[][] regions = {
                new uint[] { 0x00800000, 0x01200000 },  // .data/.rdata
                new uint[] { 0x01200000, 0x02000000 },  // heap
                new uint[] { 0x02000000, 0x04000000 },  // heap
                new uint[] { 0x04000000, 0x08000000 },  // heap
                new uint[] { 0x08000000, 0x10000000 },  // heap
                new uint[] { 0x10000000, 0x30000000 },  // heap/mapped
            };

            foreach (var region in regions)
            {
                uint start = region[0];
                uint end = region[1];
                uint step = 4096; // читаем по 4KB

                for (uint addr = start; addr < end; addr += step)
                {
                    try
                    {
                        byte[] block = _memory.ReadBytes(addr, (int)step + needle.Length);
                        for (int i = 0; i < (int)step; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < needle.Length; j++)
                            {
                                if (block[i + j] != needle[j]) { match = false; break; }
                            }
                            if (match)
                            {
                                uint foundAddr = addr + (uint)i;
                                results.Add($"*** FOUND at 0x{foundAddr:X8} ***");
                            }
                        }
                    }
                    catch { }
                }
            }

            // Проверяем старые адреса — что там сейчас
            uint[] oldAddrs = { 0x12EF0217, 0x1CC858B0, 0x22419B1C };
            foreach (var a in oldAddrs)
            {
                string val = _memory.ReadString(a, 20);
                results.Add($"Old addr 0x{a:X8} now contains: '{val}'");
            }

            if (results.Count == 1)
                results.Add("Nothing found.");

            var dumpPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "macro_scan.txt");
            System.IO.File.WriteAllLines(dumpPath, results);

            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = $"Scan done! {results.Count - 1} results. Saved to macro_scan.txt";
                BtnDump.IsEnabled = true;
            });
        });
    }

    private Process? ShowProcessPicker(Process[] processes)
    {
        // Читаем имя персонажа для каждого процесса
        var items = new List<(Process proc, string name)>();
        foreach (var proc in processes)
        {
            string charName = "???";
            try
            {
                var tmpMem = new MemoryReader();
                if (tmpMem.Attach(proc))
                {
                    charName = tmpMem.ReadString(WowBot.Core.Game.Offsets.PlayerName, 20);
                    if (string.IsNullOrEmpty(charName)) charName = "???";
                    tmpMem.Detach();
                }
            }
            catch { }
            items.Add((proc, charName));
        }

        // Показываем диалог выбора
        var dialog = new Window
        {
            Title = "Выбери персонажа",
            Width = 300, Height = 50 + items.Count * 45,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#1a1a2e")),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
        };

        Process? selected = null;
        var stack = new StackPanel { Margin = new Thickness(10) };

        foreach (var (proc, name) in items)
        {
            var btn = new Button
            {
                Content = $"{name}  (PID {proc.Id})",
                Height = 35,
                Margin = new Thickness(0, 3, 0, 3),
                FontSize = 14,
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#252830")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            var p = proc;
            btn.Click += (s, e) => { selected = p; dialog.Close(); };
            stack.Children.Add(btn);
        }

        dialog.Content = stack;
        dialog.ShowDialog();
        return selected;
    }

    private string DetectSpec(string classInfo)
    {
        var parts = classInfo.Split('|');
        if (parts.Length < 4) return classInfo;

        string cls = parts[0];
        int.TryParse(parts[1], out int t1);
        int.TryParse(parts[2], out int t2);
        int.TryParse(parts[3], out int t3);

        return cls switch
        {
            "WARRIOR" => t1 >= t2 && t1 >= t3 ? "Arms Warrior" :
                         t2 >= t1 && t2 >= t3 ? "Fury Warrior" : "Prot Warrior",
            "PALADIN" => t1 >= t2 && t1 >= t3 ? "Holy Paladin" :
                         t2 >= t1 && t2 >= t3 ? "Prot Paladin" : "Ret Paladin",
            "HUNTER" => t1 >= t2 && t1 >= t3 ? "BM Hunter" :
                        t2 >= t1 && t2 >= t3 ? "MM Hunter" : "Survival Hunter",
            "ROGUE" => t1 >= t2 && t1 >= t3 ? "Assassination Rogue" :
                       t2 >= t1 && t2 >= t3 ? "Combat Rogue" : "Subtlety Rogue",
            "PRIEST" => t3 >= t1 && t3 >= t2 ? "Shadow Priest" :
                        t1 >= t2 ? "Disc Priest" : "Holy Priest",
            "DEATHKNIGHT" => t1 >= t2 && t1 >= t3 ? "Blood DK" :
                             t2 >= t1 && t2 >= t3 ? "Frost DK" : "Unholy DK",
            "SHAMAN" => t1 >= t2 && t1 >= t3 ? "Elemental Shaman" :
                        t2 >= t1 && t2 >= t3 ? "Enhancement Shaman" : "Resto Shaman",
            "MAGE" => t1 >= t2 && t1 >= t3 ? "Arcane Mage" :
                      t2 >= t1 && t2 >= t3 ? "Fire Mage" : "Frost Mage",
            "WARLOCK" => t1 >= t2 && t1 >= t3 ? "Affliction Lock" :
                         t2 >= t1 && t2 >= t3 ? "Demonology Lock" : "Destruction Lock",
            "DRUID" => t1 >= t2 && t1 >= t3 ? "Balance Druid" :
                       t2 >= t1 && t2 >= t3 ? "Feral Druid" : "Resto Druid",
            _ => $"{cls} ({t1}/{t2}/{t3})"
        };
    }

    private void ClearDisplay()
    {
        TxtName.Text = "-";
        TxtHealth.Text = "-";
        TxtMana.Text = "-";
        TxtLevel.Text = "-";
        TxtPosition.Text = "-";
        TxtTarget.Text = "-";
        TxtObjectCount.Text = "OBJECTS (0)";
        LstObjects.ItemsSource = null;
    }
}
