using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WowBot.Core;
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

    private static readonly string PidLockFile = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
        "attached_pids.txt");
    private int _attachedPid;

    private static HashSet<int> GetLockedPids()
    {
        var pids = new HashSet<int>();
        try
        {
            if (System.IO.File.Exists(PidLockFile))
            {
                foreach (var line in System.IO.File.ReadAllLines(PidLockFile))
                    if (int.TryParse(line.Trim(), out int pid)) pids.Add(pid);
                // Убираем мёртвые процессы
                pids.RemoveWhere(p => { try { Process.GetProcessById(p); return false; } catch { return true; } });
                System.IO.File.WriteAllLines(PidLockFile, pids.Select(p => p.ToString()));
            }
        }
        catch { }
        return pids;
    }

    private static void LockPid(int pid)
    {
        var pids = GetLockedPids();
        pids.Add(pid);
        try { System.IO.File.WriteAllLines(PidLockFile, pids.Select(p => p.ToString())); } catch { }
    }

    private static void UnlockPid(int pid)
    {
        var pids = GetLockedPids();
        pids.Remove(pid);
        try { System.IO.File.WriteAllLines(PidLockFile, pids.Select(p => p.ToString())); } catch { }
    }

    private readonly MemoryReader _memory = new();
    private System.Threading.Timer? _afkTimer;
    private ObjectManager? _objectManager;
    private EndSceneHook? _endSceneHook;
    private BotEngine? _botEngine;
    private string _playerClass = "";

    private DispatcherTimer? _updateTimer;
    private int _gossipCheckTick;
    private OverlayWindow? _overlay;
    private MasterPanel? _masterPanel;
    // NavPanel убрана — навигация встроена в MasterPanel

    private int _autoConnectPid;
    private string _autoConnectRole = "";
    private bool _hiddenMode;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnWindowClosed;
        Loaded += MainWindow_Loaded;

        // Парсим аргументы командной строки: --pid=1234 --role=slave --hidden
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--pid=") && int.TryParse(arg[6..], out int pid))
                _autoConnectPid = pid;
            if (arg.StartsWith("--role="))
                _autoConnectRole = arg[7..];
            if (arg == "--hidden")
                _hiddenMode = true;
        }

        // Скрытый режим — прячем главное окно (оверлеи всё равно появятся)
        if (_hiddenMode)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_autoConnectPid > 0)
        {
            // Автоподключение через аргументы (из лаунчера)
            await Task.Delay(500); // Дать UI загрузиться
            AutoConnect(_autoConnectPid, _autoConnectRole);
        }
    }

    private Process? _pendingProcess; // Для автоподключения из лаунчера

    private void AutoConnect(int pid, string role)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (proc == null || proc.HasExited) return;

            _pendingProcess = proc;
            _autoConnectRole = role;
            BtnAttach_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            WowBot.Core.Logger.Error("AutoConnect failed", ex);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LuaHeader_Click(object sender, MouseButtonEventArgs e) => ToggleLuaConsole();
    private void LuaHeader_BtnClick(object sender, RoutedEventArgs e) => ToggleLuaConsole();

    private void ToggleLuaConsole()
    {
        if (LuaContent.Visibility == Visibility.Collapsed)
        {
            LuaContent.Visibility = Visibility.Visible;
            LuaArrow.Text = "\xE70D";
        }
        else
        {
            LuaContent.Visibility = Visibility.Collapsed;
            LuaArrow.Text = "\xE76C";
        }
    }

    private void ShowMasterPanel()
    {
        if (_masterPanel != null) return;
        _masterPanel = new MasterPanel();
        _masterPanel.OnCommand += (cmd) =>
        {
            if (_botEngine == null) return;
            var hive = _botEngine.Hivemind;
            switch (cmd)
            {
                case "attack": hive.CmdAttack(); break;
                case "follow": hive.CmdFollow(); break;
                case "stop": hive.CmdStop(); break;
                case "auto": hive.CmdAuto(); break;
                case "autopve:on":
                    _botEngine.AutoPveEnabled = true;
                    hive.CmdAutoPve(true);
                    break;
                case "autopve:off":
                    _botEngine.AutoPveEnabled = false;
                    hive.CmdAutoPve(false);
                    break;
                case "inframe:on":
                    _botEngine.InFrameEnabled = true;
                    hive.CmdInFrame(true);
                    // Автоматом включаем Auto — слейвы должны быть в Auto/Attack чтобы approach сработал.
                    hive.CmdAuto();
                    break;
                case "inframe:off":
                    _botEngine.InFrameEnabled = false;
                    hive.CmdInFrame(false);
                    break;
                case "wipe": hive.CmdWipe(); break;
                case "refreshguid":
                    foreach (var s in hive.ConnectedSlaves) s.FollowTargetName = "";
                    hive.NotifySlavesChanged();
                    hive.CmdRefreshGuid();
                    break;
                case "guildtp":
                    // Сначала стоп всем (остановить баффы/ротацию/follow чтобы не сбить каст)
                    hive.CmdStop();
                    // Задержка чтобы стоп дошёл до слейвов
                    Task.Run(async () =>
                    {
                        await Task.Delay(1500);
                        Dispatcher.Invoke(() =>
                        {
                            _endSceneHook?.ExecuteLua("SendChatMessage('.g t','GUILD')", 200);
                            hive.CmdGuildTp();
                        });
                    });
                    break;
                case "guidbytarget":
                    string? tName = hive.GetMasterTargetName();
                    foreach (var s in hive.ConnectedSlaves) s.FollowTargetName = tName ?? "";
                    hive.NotifySlavesChanged();
                    hive.CmdGuidByTarget();
                    break;
            }
        };
        _masterPanel.OnSlaveCommand += (slaveName, cmd) =>
        {
            if (_botEngine == null) return;
            var hive = _botEngine.Hivemind;
            if (cmd == "toggle_ignore")
            {
                hive.ToggleIgnoreGlobal(slaveName);
                return;
            }
            if (cmd == "guidbytarget")
            {
                string? targetName = hive.GetMasterTargetName();
                if (string.IsNullOrEmpty(targetName)) return;
                var si = hive.ConnectedSlaves.FirstOrDefault(s => s.Name == slaveName);
                if (si != null) si.FollowTargetName = targetName;
                hive.NotifySlavesChanged();
                hive.CmdRefreshGuidToSlave(slaveName, targetName);
                return;
            }
            if (cmd.StartsWith("buff_blessing:"))
            {
                string key = cmd.Substring("buff_blessing:".Length);
                hive.CmdSetBuffToSlave(slaveName, "blessing", key);
                return;
            }
            if (cmd.StartsWith("buff_aura:"))
            {
                string key = cmd.Substring("buff_aura:".Length);
                hive.CmdSetBuffToSlave(slaveName, "aura", key);
                return;
            }
            if (cmd.StartsWith("buff_totem_"))
            {
                // buff_totem_earth:Stoneskin, buff_totem_fire:Flametongue, etc.
                int colonIdx = cmd.IndexOf(':');
                if (colonIdx > 0)
                {
                    string element = cmd.Substring("buff_totem_".Length, colonIdx - "buff_totem_".Length);
                    string key = cmd.Substring(colonIdx + 1);
                    hive.CmdSetBuffToSlave(slaveName, $"totem_{element}", key);
                }
                return;
            }
            // Auto sub-toggles
            if (cmd == "auto_toggle_follow")
            {
                var si = hive.ConnectedSlaves.FirstOrDefault(s => s.Name == slaveName);
                if (si != null) si.AutoFollowPaused = !si.AutoFollowPaused;
                hive.SendCommandToSlave(slaveName, WowBot.Core.Game.Hivemind.Command.AutoToggleFollow);
                hive.NotifySlavesChanged();
                return;
            }
            if (cmd == "auto_toggle_attack")
            {
                var si = hive.ConnectedSlaves.FirstOrDefault(s => s.Name == slaveName);
                if (si != null) si.AutoAttackPaused = !si.AutoAttackPaused;
                hive.SendCommandToSlave(slaveName, WowBot.Core.Game.Hivemind.Command.AutoToggleAttack);
                hive.NotifySlavesChanged();
                return;
            }
            var command = cmd switch
            {
                "attack" => WowBot.Core.Game.Hivemind.Command.Attack,
                "follow" => WowBot.Core.Game.Hivemind.Command.Follow,
                "auto" => WowBot.Core.Game.Hivemind.Command.Auto,
                "stop" => WowBot.Core.Game.Hivemind.Command.Stop,
                _ => WowBot.Core.Game.Hivemind.Command.Stop
            };
            hive.SendCommandToSlave(slaveName, command);
        };
        // Heroism
        _masterPanel.OnHeroism += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdHeroism();
        };
        // Stack to MA / Scatter
        _masterPanel.OnStackMA += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdStackToMA();
        };
        _masterPanel.OnScatter += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdSmartScatter();
        };
        // MT/OT taunt
        _masterPanel.OnTauntMT += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdTauntMT();
        };
        _masterPanel.OnTauntOT += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdTauntOT();
        };
        // Ghost: RepopMe + Path recording
        _masterPanel.OnRepop += () =>
        {
            WowBot.Core.Logger.Info($"OnRepop: botEngine={_botEngine != null} hook={_endSceneHook != null}");
            if (_botEngine == null || _endSceneHook == null) return;
            // Сначала слейвы — пока мастер ещё не в загрузке
            _botEngine.Hivemind.SendCommand(WowBot.Core.Game.Hivemind.Command.Wipe, "repop");
            System.Threading.Thread.Sleep(300); // дать время уйти addon message
            // Потом мастер покидает тело + ghost run
            for (int i = 0; i < 3; i++)
            {
                _endSceneHook.ExecuteLua("RepopMe()", 200);
                System.Threading.Thread.Sleep(500);
            }
            _botEngine.StartGhostRun();
            WowBot.Core.Logger.Info("OnRepop: sent RepopMe + ghost run to all");
        };
        _masterPanel.OnRepair += () =>
        {
            if (_botEngine == null) return;
            // Мастер бежит к ремонтнику
            _botEngine.StartRepairRun();
            // Слейвы тоже
            _botEngine.Hivemind.SendCommand(WowBot.Core.Game.Hivemind.Command.Wipe, "repair");
            WowBot.Core.Logger.Info("OnRepair: sent repair run to all");
        };
        System.Windows.Threading.DispatcherTimer? _pathRecordTimer = null;
        List<(float x, float y, float z)>? _recordedPath = null;
        _masterPanel.OnRecordPath += (start) =>
        {
            if (_endSceneHook == null || _objectManager == null) return;
            if (start)
            {
                _recordedPath = new();
                _pathRecordTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _pathRecordTimer.Tick += (s, e) =>
                {
                    var p = _objectManager.LocalPlayer;
                    if (p == null) return;
                    // Не добавлять дубли (если стоим на месте)
                    if (_recordedPath!.Count > 0)
                    {
                        var last = _recordedPath[^1];
                        float dx = p.X - last.x, dy = p.Y - last.y, dz = p.Z - last.z;
                        if (dx * dx + dy * dy + dz * dz < 4f) return; // <2м — пропуск
                    }
                    _recordedPath.Add((p.X, p.Y, p.Z));
                    WowBot.Core.Logger.Info($"PathRecord: point #{_recordedPath.Count} ({p.X:F1},{p.Y:F1},{p.Z:F1})");
                };
                _pathRecordTimer.Start();
                WowBot.Core.Logger.Info("PathRecord: STARTED");
            }
            else
            {
                _pathRecordTimer?.Stop();
                _pathRecordTimer = null;
                if (_recordedPath != null && _recordedPath.Count > 0)
                {
                    string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    string routesDir = System.IO.Path.Combine(basePath, "Routes");
                    System.IO.Directory.CreateDirectory(routesDir);
                    string fileName = $"repair_route_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    string filePath = System.IO.Path.Combine(routesDir, fileName);
                    var lines = _recordedPath.Select(p => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1};{1:F1};{2:F1}", p.x, p.y, p.z));
                    System.IO.File.WriteAllLines(filePath, lines);
                    WowBot.Core.Logger.Info($"PathRecord: SAVED {_recordedPath.Count} points → {filePath}");
                }
                _recordedPath = null;
            }
        };
        // Interact / Gossip
        _masterPanel.OnInteract += () =>
        {
            if (_botEngine == null) return;
            var hive = _botEngine.Hivemind;
            hive.CmdInteract();
            // Через 2с читаем gossip, retry если пусто
            int retries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            timer.Tick += (s2, e2) =>
            {
                retries++;
                var options = hive.GetMasterGossipOptions();
                if (options.Count > 0 || retries >= 3)
                {
                    timer.Stop();
                    if (options.Count > 0)
                        _masterPanel?.ShowGossipOptions(options); _gossipCheckTick = 0;
                }
                else
                {
                    timer.Interval = TimeSpan.FromMilliseconds(1000); // retry через 1с
                }
            };
            timer.Start();
        };
        _masterPanel.OnGossipSelect += (idx) =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdGossipSelect(idx);
            // Мастер тоже выбирает ту же опцию (с очисткой WB_GOSSIP)
            _botEngine.Hivemind.MasterSelectGossip(idx);
            // Через 2с проверяем: подменю или popup, retry до 3 раз
            int selRetries = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            timer.Tick += (s2, e2) =>
            {
                selRetries++;
                var subOptions = _botEngine.Hivemind.GetMasterGossipOptions();
                WowBot.Core.Logger.Info($"Gossip: after select, retry={selRetries} opts={subOptions.Count}");
                if (subOptions.Count > 0)
                {
                    timer.Stop();
                    _masterPanel?.ShowGossipOptions(subOptions); _gossipCheckTick = 0;
                }
                else if (selRetries >= 2)
                {
                    timer.Stop();
                    _masterPanel?.ShowAcceptOnly(); // popup — показать только кнопку Принять
                }
            };
            timer.Start();
        };
        _masterPanel.OnGossipAccept += () =>
        {
            if (_botEngine == null) return;
            _botEngine.Hivemind.CmdGossipAccept();
            // Мастер НЕ принимает — тпшится сам руками после проверки слейвов
            _masterPanel?.HideGossip();
        };
        // Навигация встроена в MasterPanel
        _masterPanel.OnSlaveToggled += (name) =>
        {
            if (_botEngine == null) return;
            var nav = _botEngine.Hivemind.NavSelectedSlaves;
            if (nav.Contains(name)) nav.Remove(name); else nav.Add(name);
            _botEngine.Hivemind.NotifyNavChanged();
        };
        _masterPanel.OnSlavePinToggled += (name) =>
        {
            if (_botEngine == null) return;
            var pins = _botEngine.Hivemind.NavPinnedSlaves;
            if (pins.Contains(name)) pins.Remove(name); else pins.Add(name);
            if (pins.Contains(name)) _botEngine.Hivemind.NavSelectedSlaves.Add(name);
            _botEngine.Hivemind.NotifyNavChanged();
        };
        _masterPanel.Show();
    }

    private void CloseMasterPanel()
    {
        _masterPanel?.Close();
        _masterPanel = null;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        CloseMasterPanel();
        if (_attachedPid != 0) UnlockPid(_attachedPid);
        StopUpdateLoop();
        _overlay?.SaveSettings();
        _overlay?.Close();
        _botEngine?.Dispose();
        _endSceneHook?.Dispose();
        _memory.Dispose();

        // Лаунчер (не hidden) — убить все дочерние скрытые процессы
        if (!_hiddenMode)
        {
            try { System.IO.File.WriteAllText(PidLockFile, ""); } catch { }
            int myPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("WowBot.Injector"))
            {
                if (proc.Id == myPid) continue;
                try { proc.Kill(); } catch { }
            }
        }
        Environment.Exit(0);
    }

    private async void BtnAttach_Click(object sender, RoutedEventArgs e)
    {
        Process wow;
        if (_pendingProcess != null)
        {
            wow = _pendingProcess;
            _pendingProcess = null;
        }
        else
        {
            var wowProcesses = Process.GetProcessesByName("Wow");
            if (wowProcesses.Length == 0)
            {
                TxtStatus.Text = "WoW process not found! Launch WoW first.";
                return;
            }
            if (wowProcesses.Length == 1)
            {
                wow = wowProcesses[0];
            }
            else
            {
                wow = ShowProcessPicker(wowProcesses);
                if (wow == null) return;
            }
        }
        // Показываем лоадер
        PanelDisconnected.Visibility = Visibility.Visible;
        TxtStatus.Text = "Подключение...";
        BtnAttach.IsEnabled = false;
        // Заменяем красный индикатор на жёлтый
        var indicator = PanelDisconnected.Children[0] as System.Windows.Shapes.Ellipse;
        if (indicator != null)
        {
            indicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E"));
        }
        var statusText = PanelDisconnected.Children[1] as TextBlock;
        if (statusText != null) statusText.Text = "Подключение...";

        // Даём UI перерисоваться
        await Task.Delay(50);

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

        BtnAttach.Visibility = Visibility.Collapsed;
        BtnDetach.Visibility = Visibility.Visible;
        _attachedPid = wow.Id;
        LockPid(wow.Id);

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

            // Антиафк теперь в BotEngine.Tick (0x00B499A4)
            BtnExecuteLua.IsEnabled = true;

            // Автодетект класса/спека
            string specName = "Unknown";
            string playerClass = "";
            // Инициализируем логгер ДО детекта класса чтобы видеть логи
            var earlyName = _objectManager.GetPlayerName() ?? "???";
            WowBot.Core.Logger.SetCharName(earlyName);
            WowBot.Core.Logger.Init();

            {
                string lua = "local _,c=UnitClass('player') local _,_,t1=GetTalentTabInfo(1) local _,_,t2=GetTalentTabInfo(2) local _,_,t3=GetTalentTabInfo(3) WB_R=c..'|'..t1..'|'..t2..'|'..t3";
                string? classInfo = _endSceneHook.ExecuteLuaWithResult(lua);
                WowBot.Core.Logger.Info($"DetectClass: capi=[{classInfo ?? "NULL"}]");

                if (classInfo != null && classInfo.Contains("|"))
                {
                    specName = DetectSpec(classInfo);
                    playerClass = classInfo.Split('|')[0];
                }
                else
                {
                    WowBot.Core.Logger.Info("DetectClass: FAILED, retrying in 500ms...");
                    System.Threading.Thread.Sleep(500);
                    classInfo = _endSceneHook.ExecuteLuaWithResult(lua);
                    WowBot.Core.Logger.Info($"DetectClass: retry=[{classInfo ?? "NULL"}]");
                    if (classInfo != null && classInfo.Contains("|"))
                    {
                        specName = DetectSpec(classInfo);
                        playerClass = classInfo.Split('|')[0];
                    }
                }
            }
            // Обновляем UI — connected state
            PanelDisconnected.Visibility = Visibility.Collapsed;
            PanelConnected.Visibility = Visibility.Visible;
            var charName = _objectManager.GetPlayerName() ?? "???";
            TxtCharName.Text = charName;
            TxtSpecName.Text = specName;
            TxtPidInfo.Text = $"PID: {wow.Id}";
            TxtStatus.Text = $"Hooked (PID: {wow.Id}) | {specName}";
            bool isHealer = specName.Contains("Holy") || specName.Contains("Disc") || specName.Contains("Resto");
            WowBot.Core.Logger.Info($"Hooked OK | class={playerClass} spec={specName} healer={isHealer}");

            // Инициализируем BotEngine
            WowBot.Core.Logger.Info("Creating BotEngine...");
            var ctm = new ClickToMove(_memory);
            ctm.SetHook(_endSceneHook);
            var navigation = new Navigation(_memory, _endSceneHook);
            _botEngine = new BotEngine(_endSceneHook, _objectManager, navigation, ctm);
            _botEngine.IsHealer = isHealer;
            _botEngine.WowProcess = wow;
            _playerClass = playerClass;
            _botEngine.PlayerClass = playerClass;
            _botEngine.SpecName = specName;

            AllRotations.ExportScripts(); // экспорт ПЕРЕД загрузкой — гарантирует актуальные скрипты
            // v2: RotationRegistry — ищем C# ротацию по классу+спеку, fallback на AllRotations
            var rotation = WowBot.Core.Game.Rotations.RotationRegistry.Find(playerClass, specName);
            string fullScript, instantScript;
            if (rotation != null)
            {
                fullScript = rotation.GetFullScript();
                instantScript = rotation.GetInstantScript();
                WowBot.Core.Logger.Info($"Rotation: {rotation.Name} (C#) full={fullScript.Length} instant={instantScript.Length}");
            }
            else
            {
                fullScript = AllRotations.GetFullScript(playerClass);
                instantScript = AllRotations.GetInstantScript(playerClass);
                WowBot.Core.Logger.Info($"Rotation: {playerClass} (Lua fallback) full={fullScript.Length} instant={instantScript.Length}");
            }
            _botEngine.LoadRotation(instantScript, fullScript);

            // Навигация: пробуем подключиться к NavServer
            Task.Run(() =>
            {
                bool navOk = _botEngine.ConnectNavServer();
                Dispatcher.Invoke(() => WowBot.Core.Logger.Info($"NavServer: {(navOk ? "connected" : "not available (fallback to direct CTM)")}"));
            });

            // Cleanup: убиваем старый Lua AoE handler если остался от прошлой сессии
            try { _endSceneHook.ExecuteLua("if WB_AOE_FRAME then WB_AOE_FRAME:UnregisterAllEvents() WB_AOE_FRAME=nil end WB_AOE_FLEE=nil WB_AOE_HIT=nil WB_AOE_CD=nil StrafeRightStop()", 100); } catch { }
            _botEngine.OnStatusChanged += status =>
                Dispatcher.Invoke(() => TxtRotationStatus.Text = status);

            // Слейвы подключились — обновить панели
            _botEngine.Hivemind.OnSlavesChanged += () => Dispatcher.Invoke(() =>
            {
                var slaves = _botEngine.Hivemind.ConnectedSlaves.ToList();
                _overlay?.UpdateSlaveList(slaves);
                _masterPanel?.UpdateSlaves(slaves);
                _masterPanel?.UpdateNavSlaves(slaves, _botEngine.Hivemind.NavSelectedSlaves, _botEngine.Hivemind.NavPinnedSlaves);
            });
            _botEngine.Hivemind.OnNavChanged += () => Dispatcher.Invoke(() =>
            {
                var slaves = _botEngine.Hivemind.ConnectedSlaves.ToList();
                _masterPanel?.UpdateNavSlaves(slaves, _botEngine.Hivemind.NavSelectedSlaves, _botEngine.Hivemind.NavPinnedSlaves);
            });

            // Мастер задал бафф → слейв обновляет свои настройки
            _botEngine.Hivemind.OnBuffChanged += (type, key) => Dispatcher.Invoke(() =>
            {
                if (_botEngine == null) return;
                switch (type)
                {
                    case "blessing":
                        _botEngine.SelectedBlessing = key;
                        _overlay?.SetSelectedBlessing(key);
                        break;
                    case "aura":
                        _botEngine.SelectedAura = key;
                        _overlay?.SetSelectedAura(key);
                        break;
                    case "totem_earth":
                        _botEngine.SelectedTotemEarth = key;
                        break;
                    case "totem_fire":
                        _botEngine.SelectedTotemFire = key;
                        break;
                    case "totem_water":
                        _botEngine.SelectedTotemWater = key;
                        break;
                    case "totem_air":
                        _botEngine.SelectedTotemAir = key;
                        break;
                }
            });

            // Авто-переключение UI от Hivemind команд
            _botEngine.Hivemind.OnAutoToggle += (what, on) => Dispatcher.Invoke(() =>
            {
                if (_botEngine == null || _overlay == null) return;
                switch (what)
                {
                    case "rotation":
                        if (on && !_botEngine.RotationEnabled) { _botEngine.ToggleRotation(); _overlay.UpdateRotation(true); }
                        if (!on && _botEngine.RotationEnabled) { _botEngine.ToggleRotation(); _overlay.UpdateRotation(false); }
                        break;
                    case "buffs":
                        if (on) { _botEngine.BuffsEnabled = true; _overlay.UpdateBuffs(true); }
                        break;
                    case "follow":
                        if (on && !_botEngine.FollowEnabled) _botEngine.ToggleFollow();
                        if (!on && _botEngine.FollowEnabled) _botEngine.ToggleFollow();
                        _overlay.UpdateFollow(on, on ? "Hivemind" : "");
                        break;
                }
            });

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
                // Если выключили follow — сбросить SlaveController
                if (!_botEngine.FollowEnabled)
                    _botEngine.SlaveCtrl.CmdStop();
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
            _overlay.OnTotemChanged += (element, key) =>
            {
                if (_botEngine == null) return;
                Logger.Log(LogCat.Buffs, $"UI OnTotemChanged: element={element} key={key}");
                switch (element)
                {
                    case "Земля": _botEngine.SelectedTotemEarth = key; break;
                    case "Огонь": _botEngine.SelectedTotemFire = key; break;
                    case "Вода": _botEngine.SelectedTotemWater = key; break;
                    case "Воздух": _botEngine.SelectedTotemAir = key; break;
                }
                Logger.Log(LogCat.Buffs, $"UI after set: tE={_botEngine.SelectedTotemEarth} tF={_botEngine.SelectedTotemFire} tW={_botEngine.SelectedTotemWater} tA={_botEngine.SelectedTotemAir}");
            };
            _overlay.OnBuffsToggled += (on) =>
            {
                if (_botEngine == null) return;
                _botEngine.BuffsEnabled = on;
                Logger.Log(LogCat.Buffs, $"UI BuffsToggled: {on}");
            };
            _overlay.OnReloadScripts += () =>
            {
                if (_botEngine == null) return;
                _botEngine.ReloadScripts();
                _overlay.UpdateInfo("Scripts reloaded!");
            };
            _overlay.OnLuaExecute += (cmd) =>
            {
                if (_endSceneHook == null) return null;
                try
                {
                    // !status → полный дамп состояния бота
                    if (cmd == "!status" && _botEngine != null && _objectManager != null)
                    {
                        var lp = _objectManager.LocalPlayer;
                        if (lp == null) return "No player";
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"=== BOT STATUS ===");
                        sb.AppendLine($"Class={_botEngine.PlayerClass} Spec={_botEngine.SpecName}");
                        sb.AppendLine($"HP={lp.Health}/{lp.MaxHealth} ({lp.Health*100/lp.MaxHealth}%)");
                        sb.AppendLine($"Rot={_botEngine.RotationEnabled} Follow={_botEngine.FollowEnabled} Buffs={_botEngine.BuffsEnabled}");
                        sb.AppendLine($"Healer={_botEngine.IsHealer} Tank={_botEngine.IsTankSpec} Melee={_botEngine.IsMeleeSpec}");
                        sb.AppendLine($"Units={_objectManager.Units.Count} Players={_objectManager.Players.Count} DynObj={_objectManager.DynObjects.Count}");
                        sb.AppendLine($"AoeAvoid={_botEngine.AoeAvoidEnabled} MoveBehind={_botEngine.MoveBehindEnabled}");
                        var t = _objectManager.GetTarget();
                        sb.AppendLine($"Target={t?.Name ?? "none"} HP={t?.Health ?? 0}/{t?.MaxHealth ?? 0}");
                        return sb.ToString();
                    }
                    // !log [категория] → последние логи из ring buffer
                    if (cmd.StartsWith("!log"))
                    {
                        var parts = cmd.Split(' ', 2);
                        if (parts.Length > 1 && Enum.TryParse<LogCat>(parts[1], true, out var cat))
                            return string.Join("\n", WowBot.Core.Logger.GetRecentLogs(cat, 15));
                        return string.Join("\n", WowBot.Core.Logger.GetRecentLogs(15));
                    }
                    // !dyn → дамп DynObjects + GameObjects
                    if (cmd == "!dyn" && _objectManager != null)
                    {
                        _objectManager.Update();
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"DynObjects: {_objectManager.DynObjects.Count}");
                        foreach (var d in _objectManager.DynObjects)
                            sb.AppendLine($"  spell={d.SpellId} r={d.Radius:F1} pos=({d.X:F0},{d.Y:F0},{d.Z:F0}) caster=0x{d.Caster:X}");
                        // GameObjects дамп
                        var gos = _objectManager.Objects.Where(o => o.Type == WowObjectType.GameObject).ToList();
                        sb.AppendLine($"GameObjects: {gos.Count}");
                        foreach (var go in gos)
                        {
                            uint desc = _memory.ReadUInt32(go.BaseAddress + 0x08);
                            float gx = _memory!.ReadFloat(go.BaseAddress + 0xE8);
                            float gy = _memory.ReadFloat(go.BaseAddress + 0xEC);
                            float gz = _memory.ReadFloat(go.BaseAddress + 0xF0);
                            uint entry = _memory.ReadUInt32(desc + 0x0C); // OBJECT_FIELD_ENTRY
                            ulong createdBy = _memory.ReadUInt64(desc + 0x18);
                            uint bytes1 = _memory.ReadUInt32(desc + 0x44);
                            byte goState = (byte)(bytes1 & 0xFF);
                            byte goType = (byte)((bytes1 >> 8) & 0xFF);
                            sb.AppendLine($"  GO: entry={entry} type={goType} state={goState} pos=({gx:F0},{gy:F0},{gz:F0}) by=0x{createdBy:X}");
                        }
                        sb.AppendLine($"AllObjects: {_objectManager.Objects.Count}");
                        return sb.ToString();
                    }
                    // !terrain → тест ground AoE
                    if (cmd == "!terrain" && _objectManager != null)
                    {
                        var target = _objectManager.GetTarget();
                        if (target == null) return "Нет таргета!";
                        float tx = target.X, ty = target.Y, tz = target.Z;
                        _endSceneHook.ExecuteLua("CastSpellByName('Гроза')", 300);
                        System.Threading.Thread.Sleep(150);
                        bool ok = _endSceneHook.CastTerrainClick(tx, ty, tz);
                        WowBot.Core.Logger.Info($"TerrainClick: ({tx:F1},{ty:F1},{tz:F1}) ok={ok}");
                        return ok ? $"Terrain → ({tx:F1},{ty:F1},{tz:F1}) OK" : "Terrain FAIL";
                    }
                    // !move → полный дамп movement state (для диагностики follow)
                    if (cmd == "!move" && _memory != null && _objectManager != null)
                    {
                        var lp = _objectManager.LocalPlayer;
                        if (lp == null) return "No player";
                        uint pb = lp.BaseAddress;
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"=== MOVEMENT DUMP ===");
                        sb.AppendLine($"PlayerBase=0x{pb:X8}");

                        // Movement struct pointer
                        uint movPtr = _memory.ReadUInt32(pb + 0xD8);
                        sb.AppendLine($"MovementPtr [+D8]=0x{movPtr:X8}");
                        if (movPtr != 0 && movPtr < 0x7FFFFFFF)
                        {
                            byte[] movData = _memory.ReadBytes(movPtr, 256);
                            sb.AppendLine($"Mov 00-3F: {string.Join(" ", movData.Take(64).Select(x => $"{x:X2}"))}");
                            sb.AppendLine($"Mov 40-7F: {string.Join(" ", movData.Skip(64).Take(64).Select(x => $"{x:X2}"))}");
                            sb.AppendLine($"Mov 80-BF: {string.Join(" ", movData.Skip(128).Take(64).Select(x => $"{x:X2}"))}");
                            sb.AppendLine($"Mov C0-FF: {string.Join(" ", movData.Skip(192).Take(64).Select(x => $"{x:X2}"))}");
                        }

                        // Player facing
                        float facing = _memory.ReadFloat(pb + 0x7A8);
                        sb.AppendLine($"Facing [+7A8]={facing:F4}");

                        // CTM activate
                        uint ctmActPtr = _memory.ReadUInt32(0xBD08F4);
                        sb.AppendLine($"CTM_ActivatePtr=0x{ctmActPtr:X8}");
                        if (ctmActPtr != 0 && ctmActPtr < 0x7FFFFFFF)
                        {
                            int ctmEnabled = _memory.ReadInt32(ctmActPtr + 0x30);
                            sb.AppendLine($"CTM_Enabled [+30]={ctmEnabled}");
                            // Дамп вокруг
                            byte[] ctmAct = _memory.ReadBytes(ctmActPtr, 64);
                            sb.AppendLine($"CTM_Act 0-3F: {string.Join(" ", ctmAct.Select(x => $"{x:X2}"))}");
                        }

                        // CTM struct
                        uint ctmB = 0x00CA11D8;
                        sb.AppendLine($"CTM action={_memory.ReadInt32(ctmB+0x1C)}");
                        sb.AppendLine($"CTM X={_memory.ReadFloat(ctmB+0x8C):F2} Y={_memory.ReadFloat(ctmB+0x90):F2} Z={_memory.ReadFloat(ctmB+0x94):F2}");

                        // Unit flags
                        sb.AppendLine($"Player pos=({lp.X:F1},{lp.Y:F1},{lp.Z:F1})");

                        WowBot.Core.Logger.Info(sb.ToString());
                        return sb.ToString();
                    }
                    // !ctm → дамп CTM структуры
                    if (cmd == "!ctm" && _memory != null)
                    {
                        uint b = 0x00CA11D8;
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"CTM Base=0x{b:X8}");
                        sb.AppendLine($"+00 unk:  {_memory.ReadInt32(b):X8}");
                        sb.AppendLine($"+04 time: {_memory.ReadInt32(b+4):X8}");
                        sb.AppendLine($"+08 GUID: {_memory.ReadUInt32(b+8):X8} {_memory.ReadUInt32(b+12):X8}");
                        sb.AppendLine($"+0C prec: {_memory.ReadFloat(b+0x0C):F2}");
                        sb.AppendLine($"+10 unk1: {_memory.ReadFloat(b+0x10):F2}");
                        sb.AppendLine($"+14 unk2: {_memory.ReadFloat(b+0x14):F2}");
                        sb.AppendLine($"+18 face: {_memory.ReadFloat(b+0x18):F4}");
                        sb.AppendLine($"+1C act:  {_memory.ReadInt32(b+0x1C)} (4=move,5=interact,D=stop)");
                        sb.AppendLine($"+8C X:    {_memory.ReadFloat(b+0x8C):F2}");
                        sb.AppendLine($"+90 Y:    {_memory.ReadFloat(b+0x90):F2}");
                        sb.AppendLine($"+94 Z:    {_memory.ReadFloat(b+0x94):F2}");
                        // Дамп 0x00-0x20 raw
                        byte[] raw = _memory.ReadBytes(b, 0xA0);
                        sb.AppendLine($"RAW 0-1F: {string.Join(" ", raw.Take(32).Select(x => $"{x:X2}"))}");
                        sb.AppendLine($"RAW 80-9F: {string.Join(" ", raw.Skip(0x80).Take(32).Select(x => $"{x:X2}"))}");
                        WowBot.Core.Logger.Info(sb.ToString());
                        return sb.ToString();
                    }
                    // !dump 0xADDR → дамп памяти
                    if (cmd.StartsWith("!dump ") && _memory != null)
                    {
                        uint addr = Convert.ToUInt32(cmd[6..].Trim(), 16);
                        byte[] bytes = _memory.ReadBytes(addr, 32);
                        string hex = string.Join(" ", bytes.Select(b => $"{b:X2}"));
                        WowBot.Core.Logger.Info($"Dump 0x{addr:X8}: {hex}");
                        return $"0x{addr:X8}: {hex}";
                    }
                    // Если команда содержит WB_R= — вернуть результат
                    if (cmd.Contains("WB_R=") || cmd.Contains("WB_R ="))
                    {
                        _botEngine?.PauseTick();
                        try
                        {
                            System.Threading.Thread.Sleep(200);
                            // Способ 1: ExecuteLuaWithResult (flag=1 + flag=4)
                            var result = _endSceneHook.ExecuteLuaWithResult(cmd);
                            if (result != null) return result;
                            // Способ 2: fallback — ExecuteLua + читаем WB_R отдельно
                            _endSceneHook.ExecuteLua(cmd, 500);
                            System.Threading.Thread.Sleep(100);
                            var result2 = _endSceneHook.ExecuteLuaWithResult("WB_R=tostring(WB_R)");
                            return result2 ?? "(null — WB_R не удалось прочитать)";
                        }
                        finally { _botEngine?.ResumeTick(); }
                    }
                    _endSceneHook.ExecuteLua(cmd, 500);
                    return "(ok)";
                }
                catch (Exception ex) { return ex.Message; }
            };
            _overlay.OnHivemindCommand += (cmd) =>
            {
                if (_botEngine == null) return;
                var hive = _botEngine.Hivemind;
                switch (cmd)
                {
                    case "role:master":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.Master;
                        _botEngine.EnsureRunning();
                        ShowMasterPanel();
                        break;
                    case "role:slave":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.Slave;
                        hive.ResetSlaveState();
                        _botEngine.EnsureRunning();
                        // Автоскрытие: слейву не нужно главное окно
                        Hide();
                        // Отправить Register мастеру
                        Task.Run(() => { System.Threading.Thread.Sleep(1000); hive.SendRegister(_playerClass); });
                        break;
                    case "role:none":
                        hive.CurrentRole = WowBot.Core.Game.Hivemind.Role.None;
                        CloseMasterPanel();
                        // Вернуть окно
                        Show();
                        break;
                    case "attack": hive.CmdAttack(); break;
                    case "follow": hive.CmdFollow(); break;
                    case "stop": hive.CmdStop(); break;
                    case "scatter": hive.CmdScatter(); break;
                    case "stack": hive.CmdStack(); break;
                    case "ping": hive.CmdPing(); break;
                    case "auto": hive.CmdAuto(); break;
                    default:
                        if (cmd.StartsWith("toggle_slave:"))
                        {
                            string slaveName = cmd["toggle_slave:".Length..];
                            hive.ToggleSlaveSelection(slaveName);
                        }
                        break;
                }
            };
            WowBot.Core.Logger.Info("Loading overlay settings...");
            _overlay.LoadSettings();
            // Применить сохранённые слайдеры в BotEngine (ValueChanged не стреляет если значение не менялось)
            float savedDist = _overlay.GetFollowDistance();
            float savedRange = _overlay.GetMaxTargetRange();
            WowBot.Core.Logger.Info($"Applying sliders: followDist={savedDist} maxRange={savedRange}");
            _botEngine.FollowDistance = savedDist;
            _botEngine.MaxTargetRange = savedRange;
            // ExportScripts уже вызван выше при загрузке ротации
            // Проверяем use-эффект перчаток (слот 10)
            bool hasGlovesUse = false;
            try
            {
                string glovesLua = "GameTooltip:SetOwner(UIParent,'ANCHOR_NONE') GameTooltip:SetInventoryItem('player',10) local h=false for i=1,GameTooltip:NumLines() do local t=_G['GameTooltipTextLeft'..i] if t and t:GetText() and t:GetText():find('Использование:') then h=true break end end GameTooltip:Hide() WB_R=h and '1' or '0'";
                string? glovesResult = _endSceneHook.ExecuteLuaWithResult(glovesLua);
                hasGlovesUse = glovesResult == "1";
                WowBot.Core.Logger.Info($"GlovesCheck: use={hasGlovesUse} result=[{glovesResult}]");
            }
            catch (Exception ex) { WowBot.Core.Logger.Error("GlovesCheck failed", ex); }

            _overlay.UpdateStatus(specName);
            _overlay.SetPlayerClass(playerClass, specName, charName, hasGlovesUse);
            WowBot.Core.Logger.Info("Showing overlay...");
            _overlay.Show();
            WowBot.Core.Logger.Info("Overlay shown OK");

            // Синхронизировать начальное состояние из сохранённых настроек
            if (_botEngine != null)
            {
                if (_overlay.BuffsEnabled)
                {
                    _botEngine.BuffsEnabled = true;
                    WowBot.Core.Logger.Info("Buffs auto-enabled from saved settings");
                }
                _botEngine.SelectedTotemEarth = _overlay.SelectedTotemEarth;
                _botEngine.SelectedTotemFire = _overlay.SelectedTotemFire;
                _botEngine.SelectedTotemWater = _overlay.SelectedTotemWater;
                _botEngine.SelectedTotemAir = _overlay.SelectedTotemAir;
            }

            // Автороль из лаунчера
            if (!string.IsNullOrEmpty(_autoConnectRole) && _botEngine != null)
            {
                var hive = _botEngine.Hivemind;
                switch (_autoConnectRole)
                {
                    case "master":
                        hive.CurrentRole = Hivemind.Role.Master;
                        _botEngine.EnsureRunning();
                        _overlay.SetHivemindRole("master");
                        ShowMasterPanel();
                        WowBot.Core.Logger.Info("AutoConnect: role=Master");
                        break;
                    case "slave":
                        hive.CurrentRole = Hivemind.Role.Slave;
                        hive.ResetSlaveState();
                        _botEngine.EnsureRunning();
                        _overlay.SetHivemindRole("slave");
                        // Автоскрытие: слейву не нужно главное окно
                        Hide();
                        // Отправить Register мастеру через 1 сек (слушатель должен успеть)
                        Task.Run(() => { System.Threading.Thread.Sleep(1000); hive.SendRegister(_playerClass); });
                        WowBot.Core.Logger.Info("AutoConnect: role=Slave");
                        break;
                }
                _autoConnectRole = "";
            }
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

    private async void BtnDetach_Click(object sender, RoutedEventArgs e)
    {
        if (_attachedPid != 0) { UnlockPid(_attachedPid); _attachedPid = 0; }
        StopUpdateLoop();

        var bot = _botEngine;
        var hook = _endSceneHook;
        _botEngine = null;
        _endSceneHook = null;

        var cleanupTask = Task.Run(() =>
        {
            try { bot?.Dispose(); } catch { }
            try { hook?.Dispose(); } catch { }
        });
        await Task.WhenAny(cleanupTask, Task.Delay(2000));

        _memory.Detach();
        _objectManager = null;

        TxtStatus.Text = "Отключено";
        TxtLuaStatus.Text = "";
        TxtRotationStatus.Text = "";
        BtnAttach.Visibility = Visibility.Visible;
        BtnAttach.IsEnabled = true;
        BtnDetach.Visibility = Visibility.Collapsed;
        PanelDisconnected.Visibility = Visibility.Visible;
        PanelConnected.Visibility = Visibility.Collapsed;
        BtnExecuteLua.IsEnabled = false;
        // Сброс индикатора на красный + текст "Отключено"
        var indicator = PanelDisconnected.Children[0] as System.Windows.Shapes.Ellipse;
        if (indicator != null)
            indicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
        var statusText = PanelDisconnected.Children[1] as TextBlock;
        if (statusText != null) statusText.Text = "Отключено";
        ClearDisplay();
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
            // ?выражение → ExecuteLuaWithResult (тест GetLocalizedText)
            // !0xАДРЕС → прочитать 4 байта из памяти WoW
            // !terrain → тест ground AoE: каст Гроза + terrain click на таргет
            WowBot.Core.Logger.Info($"LuaConsole: [{lua}] len={lua.Length} hook={_endSceneHook != null} om={_objectManager != null}");
            if (lua == "!terrain" && _endSceneHook != null && _objectManager != null)
            {
                try
                {
                    var target = _objectManager.GetTarget();
                    if (target == null) { TxtLuaStatus.Text = "Нет таргета!"; return; }
                    float tx = target.X, ty = target.Y, tz = target.Z;
                    TxtLuaStatus.Text = $"Terrain click → ({tx:F1}, {ty:F1}, {tz:F1})";
                    // 1. Каст Грозу
                    _endSceneHook.ExecuteLua("CastSpellByName('Гроза')", 300);
                    System.Threading.Thread.Sleep(100); // ждём targeting mode
                    // 2. Кликаем по земле
                    bool ok = _endSceneHook.CastTerrainClick(tx, ty, tz);
                    TxtLuaStatus.Text += ok ? " OK!" : " FAIL";
                    WowBot.Core.Logger.Info($"TerrainClick test: ({tx:F1},{ty:F1},{tz:F1}) ok={ok}");
                }
                catch (Exception ex) { TxtLuaStatus.Text = $"Terrain error: {ex.Message}"; }
                return;
            }
            // !scan → найти все функции (пролог 55 8B EC) в диапазоне lua функций
            if (lua == "!scan" && _memory != null)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Scanning 0x0084D000-0x0084F000 for function prologues (55 8B EC):");
                    int count = 0;
                    for (uint addr = 0x0084D000; addr < 0x0084F000; addr += 0x10)
                    {
                        byte[] bytes = _memory.ReadBytes(addr, 16);
                        for (int i = 0; i < 16 - 2; i++)
                        {
                            if (bytes[i] == 0x55 && bytes[i + 1] == 0x8B && bytes[i + 2] == 0xEC)
                            {
                                uint funcAddr = addr + (uint)i;
                                sb.AppendLine($"  0x{funcAddr:X8}");
                                count++;
                            }
                        }
                    }
                    sb.AppendLine($"Total: {count} functions");
                    TxtLuaStatus.Text = sb.ToString();
                    // Также в лог
                    WowBot.Core.Logger.Info(sb.ToString());
                }
                catch (Exception ex) { TxtLuaStatus.Text = $"Scan error: {ex.Message}"; }
                return;
            }
            // !dump 0xADDR → дамп 64 байт функции
            if (lua.StartsWith("!dump ") && _memory != null)
            {
                try
                {
                    uint addr = Convert.ToUInt32(lua[6..].Trim(), 16);
                    byte[] bytes = _memory.ReadBytes(addr, 64);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"Dump 0x{addr:X8}:");
                    for (int i = 0; i < 64; i += 16)
                    {
                        sb.Append($"  +{i:X2}: ");
                        for (int j = 0; j < 16 && i + j < 64; j++)
                            sb.Append($"{bytes[i + j]:X2} ");
                        sb.AppendLine();
                    }
                    TxtLuaStatus.Text = sb.ToString();
                    WowBot.Core.Logger.Info(sb.ToString());
                }
                catch (Exception ex) { TxtLuaStatus.Text = $"Dump error: {ex.Message}"; }
                return;
            }
            if (lua.StartsWith("!0x") && _memory != null)
            {
                try
                {
                    uint addr = Convert.ToUInt32(lua[1..].Trim(), 16);
                    uint val = _memory.ReadUInt32(addr);
                    TxtLuaStatus.Text = $"[0x{addr:X8}] = 0x{val:X8} ({val})";
                }
                catch (Exception ex) { TxtLuaStatus.Text = $"Read error: {ex.Message}"; }
                return;
            }

            if (lua.StartsWith("?"))
            {
                string expr = lua[1..].Trim();
                string luaSet = $"WB_R=tostring({expr})";
                string? result = _endSceneHook.ExecuteLuaWithResult(luaSet);
                TxtLuaStatus.Text = result != null ? $"= {result}" : "= nil (или краш)";
                return;
            }

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
            Interval = TimeSpan.FromMilliseconds(500)
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
                if (_masterPanel != null)
                    _masterPanel.Visibility = wowActive ? Visibility.Visible : Visibility.Hidden;
                // NavPanel теперь встроена в MasterPanel
            }

            if (_memory.Process?.HasExited == true)
            {
                BtnDetach_Click(this, new RoutedEventArgs());
                TxtStatus.Text = "WoW process closed.";
                return;
            }

            // ObjectManager.Update() уже вызывается в BotEngine.Tick() каждые 150мс
            // UI просто читает кэшированные данные
            UpdateDisplay();

            // Авто-скрытие gossip по таймеру бездействия (15с без кликов)
            if (_masterPanel != null && _masterPanel.IsGossipListVisible)
            {
                _gossipCheckTick++;
                if (_gossipCheckTick >= 30) // 15с (30 * 500мс)
                {
                    _masterPanel.HideGossipList();
                    _gossipCheckTick = 0;
                }
            }
            else
            {
                _gossipCheckTick = 0;
            }
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
                if (_botEngine.MoveBehindSavedState == null) // не перезаписывать во время Follow
                    _botEngine.MoveBehindEnabled = _overlay.MoveBehindEnabled;
                _botEngine.AoeAvoidEnabled = _overlay.AoeAvoidEnabled;
                _botEngine.MaxTargetRange = _overlay.MaxTargetRange;
                _botEngine.AoeEnabled = _overlay.AoeEnabled;
                _botEngine.AoeMinEnemies = _overlay.AoeMinEnemies;
                _botEngine.ThreatCapEnabled = _overlay.ThreatCapEnabled;
                _botEngine.ThreatCapPercent = _overlay.ThreatCapPercent;
                _botEngine.UseMultiDot = _overlay.UseMultiDot;
                _botEngine.MaxDotTargets = _overlay.MaxDotTargets;
                _botEngine.UseMindSear = _overlay.UseMindSear;
                _botEngine.MindSearTargets = _overlay.MindSearTargets;
                _botEngine.DispManaThreshold = _overlay.DispManaThreshold;
                _botEngine.SFManaThreshold = _overlay.SFManaThreshold;
                _botEngine.BuffsEnabled = _overlay.BuffsEnabled;
                _botEngine.SpellFlagsLua = _overlay.GetSpellFlagsLua();
                _botEngine.EnabledBuffs = _overlay.GetEnabledBuffs();
                _botEngine.AoeSealSwap = _overlay.AoeSealSwap;
                _botEngine.SelectedSeal = _overlay.SelectedSeal;
                _botEngine.SelectedBlessing = _overlay.SelectedBlessing;
                _botEngine.SelectedAura = _overlay.SelectedAura;
                _botEngine.SelectedShout = _overlay.SelectedShout;
                _botEngine.SelectedStance = _overlay.SelectedStance;
                _botEngine.SelectedPresence = _overlay.SelectedPresence;
                _botEngine.SelectedFeralForm = _overlay.SelectedFeralForm;
                _botEngine.SelectedPet = _overlay.SelectedPet;
                _botEngine.SelectedTotemEarth = _overlay.SelectedTotemEarth;
                _botEngine.SelectedTotemFire = _overlay.SelectedTotemFire;
                _botEngine.SelectedTotemWater = _overlay.SelectedTotemWater;
                _botEngine.SelectedTotemAir = _overlay.SelectedTotemAir;
                _botEngine.SelectedWeaponMH = _overlay.SelectedWeaponMH;
                _botEngine.SelectedWeaponOH = _overlay.SelectedWeaponOH;
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
        if (_endSceneHook == null || !_endSceneHook.IsHooked) return;

        // Определяем класс через C API
        string? cls = _endSceneHook.ExecuteLuaWithResult("local _,c=UnitClass('player') WB_R=c");
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
            string? name = _endSceneHook.ExecuteLuaWithResult($"WB_R=tostring(GetSpellInfo({id}))");
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

        // Стильный диалог выбора персонажа
        var dialog = new Window
        {
            Title = "WowBot",
            Width = 340, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0a0f")),
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
        };

        Process? selected = null;

        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e1e2e")),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0a0a0f")),
            Padding = new Thickness(20),
        };

        var stack = new StackPanel();

        var header = new TextBlock
        {
            Text = "Выбери персонажа",
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E")),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(header);

        var lockedPids = GetLockedPids();

        foreach (var (proc, name) in items)
        {
            bool isLocked = lockedPids.Contains(proc.Id);

            var btnBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isLocked ? "#0a0a0e" : "#12121a")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isLocked ? "#151518" : "#1e1e2e")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 3, 0, 3),
                Cursor = isLocked ? Cursors.Arrow : Cursors.Hand,
                Opacity = isLocked ? 0.4 : 1.0,
            };

            var btnStack = new StackPanel();
            btnStack.Children.Add(new TextBlock
            {
                Text = isLocked ? $"{name}  (занят)" : name,
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isLocked ? "#555" : "#e8e6e3")),
            });
            btnStack.Children.Add(new TextBlock
            {
                Text = $"PID {proc.Id}",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a4a5a")),
                Margin = new Thickness(0, 2, 0, 0),
            });

            btnBorder.Child = btnStack;

            if (!isLocked)
            {
                var btn = new Button { Cursor = Cursors.Hand, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
                btn.Template = new ControlTemplate(typeof(Button))
                {
                    VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
                };
                btn.Content = btnBorder;
                var p = proc;
                btn.Click += (s, e) => { selected = p; dialog.Close(); };
                btnBorder.MouseEnter += (s, e) =>
                    btnBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28"));
                btnBorder.MouseLeave += (s, e) =>
                    btnBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12121a"));
                stack.Children.Add(btn);
            }
            else
            {
                stack.Children.Add(btnBorder);
            }
        }

        outerBorder.Child = stack;
        dialog.Content = outerBorder;
        dialog.MouseLeftButtonDown += (s, e) => dialog.DragMove();
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

    // ==================== МУЛЬТИБОКС ====================
    private bool _multiboxExpanded;
    private readonly Dictionary<int, string> _processRoles = new(); // pid → "master"/"slave"/"none"

    private void MultiboxHeader_Click(object sender, RoutedEventArgs e)
    {
        _multiboxExpanded = !_multiboxExpanded;
        MultiboxContent.Visibility = _multiboxExpanded ? Visibility.Visible : Visibility.Collapsed;
        MultiboxArrow.Text = _multiboxExpanded ? "\uE70D" : "\uE76C";
    }

    private void BtnScanWow_Click(object sender, RoutedEventArgs e)
    {
        WowProcessList.Children.Clear();
        var procs = WowScanner.ScanAll();

        if (procs.Count == 0)
        {
            WowProcessList.Children.Add(new TextBlock
            {
                Text = "WoW процессы не найдены",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6b6b7b")),
                FontSize = 11, Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            BtnLaunchAll.Visibility = Visibility.Collapsed;
            BtnCloseAll.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var proc in procs)
        {
            // Загрузить сохранённую роль
            if (!_processRoles.ContainsKey(proc.Pid))
            {
                string saved = LoadSavedRole(proc.CharName);
                _processRoles[proc.Pid] = saved;
            }

            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Имя перса + PID
            var namePanel = new StackPanel();
            namePanel.Children.Add(new TextBlock
            {
                Text = proc.CharName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8e6e3")),
                FontSize = 13, FontWeight = FontWeights.SemiBold,
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = $"PID: {proc.Pid}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a4a5a")),
                FontSize = 9,
            });
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            // Две кнопки: Master / Slave
            string role = _processRoles.GetValueOrDefault(proc.Pid, "none");
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var btnMaster = new Button
            {
                Content = "M",
                Width = 32, Height = 26,
                Foreground = new SolidColorBrush(role == "master" ? (Color)ColorConverter.ConvertFromString("#0a0a0f") : (Color)ColorConverter.ConvertFromString("#C8A84E")),
                Background = new SolidColorBrush(role == "master" ? (Color)ColorConverter.ConvertFromString("#C8A84E") : (Color)ColorConverter.ConvertFromString("#12121a")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E")),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 11, FontWeight = FontWeights.Bold,
                Tag = proc.Pid, Margin = new Thickness(0, 0, 4, 0),
            };
            btnMaster.Click += (s, ev) => { _processRoles[(int)((Button)s).Tag] = _processRoles.GetValueOrDefault((int)((Button)s).Tag) == "master" ? "none" : "master"; SaveRoles(); BtnScanWow_Click(s, ev); };

            var btnSlave = new Button
            {
                Content = "S",
                Width = 32, Height = 26,
                Foreground = new SolidColorBrush(role == "slave" ? (Color)ColorConverter.ConvertFromString("#0a0a0f") : (Color)ColorConverter.ConvertFromString("#5dade2")),
                Background = new SolidColorBrush(role == "slave" ? (Color)ColorConverter.ConvertFromString("#5dade2") : (Color)ColorConverter.ConvertFromString("#12121a")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5dade2")),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 11, FontWeight = FontWeights.Bold,
                Tag = proc.Pid,
            };
            btnSlave.Click += (s, ev) => { _processRoles[(int)((Button)s).Tag] = _processRoles.GetValueOrDefault((int)((Button)s).Tag) == "slave" ? "none" : "slave"; SaveRoles(); BtnScanWow_Click(s, ev); };

            btnPanel.Children.Add(btnMaster);
            btnPanel.Children.Add(btnSlave);
            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);

            row.Child = grid;
            WowProcessList.Children.Add(row);
        }

        BtnLaunchAll.Visibility = Visibility.Visible;
        BtnCloseAll.Visibility = Visibility.Visible;
    }


    private async void BtnLaunchAll_Click(object sender, RoutedEventArgs e)
    {
        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location
            .Replace(".dll", ".exe");

        int launched = 0;
        foreach (var (pid, role) in _processRoles)
        {
            if (role == "none") continue;
            // Задержка между запусками чтобы не конфликтовали
            if (launched > 0) await Task.Delay(1500);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--pid={pid} --role={role} --hidden",
                    UseShellExecute = true,
                    Verb = "runas", // Админ
                });
            }
            catch (Exception ex)
            {
                WowBot.Core.Logger.Error($"Launch failed for PID {pid}", ex);
            }
            launched++;
        }
    }

    private void BtnCloseAll_Click(object sender, RoutedEventArgs e)
    {
        // Очистить лок-файл (боты убиваются и не успевают UnlockPid)
        try { System.IO.File.WriteAllText(PidLockFile, ""); } catch { }

        // Убить все дочерние WowBot.Injector процессы (кроме себя)
        int myPid = Environment.ProcessId;
        foreach (var proc in Process.GetProcessesByName("WowBot.Injector"))
        {
            if (proc.Id == myPid) continue;
            try { proc.Kill(); } catch { }
        }
    }

    private static string RoleDisplayName(string role) => role switch
    {
        "master" => "Master",
        "slave" => "Slave",
        _ => "—"
    };

    private static Color RoleColor(string role) => role switch
    {
        "master" => (Color)ColorConverter.ConvertFromString("#C8A84E"),
        "slave" => (Color)ColorConverter.ConvertFromString("#5dade2"),
        _ => (Color)ColorConverter.ConvertFromString("#4a4a5a"),
    };

    // --- Сохранение/загрузка ролей по имени перса ---
    private static readonly string LauncherConfigPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "launcher.json");

    private void SaveRoles()
    {
        try
        {
            var procs = WowScanner.ScanAll();
            var data = new Dictionary<string, string>();
            foreach (var proc in procs)
            {
                if (_processRoles.TryGetValue(proc.Pid, out var role))
                    data[proc.CharName] = role;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(LauncherConfigPath, json);
        }
        catch { }
    }

    private string LoadSavedRole(string charName)
    {
        try
        {
            if (!System.IO.File.Exists(LauncherConfigPath)) return "none";
            var json = System.IO.File.ReadAllText(LauncherConfigPath);
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data != null && data.TryGetValue(charName, out var role)) return role;
        }
        catch { }
        return "none";
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
