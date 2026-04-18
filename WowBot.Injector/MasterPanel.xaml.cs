using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace WowBot.Injector;

public partial class MasterPanel : Window
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int HK_ATTACK = 1;
    private const int HK_FOLLOW = 2;
    private const int HK_STOP = 3;

    public event Action<string>? OnCommand;
    public event Action<string>? OnSlaveToggled;     // навигация: тогл выбора
    public event Action<string>? OnSlavePinToggled;  // навигация: тогл закрепления

    // Per-slave buff selections (сохраняем между перерисовками)
    private readonly Dictionary<string, string> _slaveBlessing = new();
    private readonly Dictionary<string, string> _slaveAura = new();
    private readonly Dictionary<string, string> _slaveTotemEarth = new();
    private readonly Dictionary<string, string> _slaveTotemFire = new();
    private readonly Dictionary<string, string> _slaveTotemWater = new();
    private readonly Dictionary<string, string> _slaveTotemAir = new();
    private bool _navExpanded;

    private Dictionary<string, Key> _hotkeys = new();
    private string? _waitingForHotkey; // which command we're setting
    private Button? _waitingButton;

    public MasterPanel()
    {
        InitializeComponent();
        SourceInitialized += (s, e) =>
        {
            // Сделать окно не-активируемым — клик не забирает фокус у WoW
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (s, e) => RegisterAll();
        Closed += (s, e) => UnregisterAll();
    }

    /// <summary>Привязать к окну WoW</summary>
    public void SetOwnerHwnd(IntPtr wowHwnd)
    {
        if (wowHwnd == IntPtr.Zero) return;
        var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetParent(myHwnd, wowHwnd);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void ResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double newW = Width + e.HorizontalChange;
        if (newW >= MinWidth) Width = newW;
    }

    // --- Buttons ---
    private void BtnAttack_Click(object sender, RoutedEventArgs e) => SendCommand("attack");
    private void BtnFollow_Click(object sender, RoutedEventArgs e) => SendCommand("follow");
    private void BtnStop_Click(object sender, RoutedEventArgs e) => SendCommand("stop");

    private void BtnAuto_Click(object sender, RoutedEventArgs e) => SendCommand("auto");

    private void BtnRefreshGuid_Click(object sender, RoutedEventArgs e) => OnCommand?.Invoke("refreshguid");
    private void BtnGuidByTarget_Click(object sender, RoutedEventArgs e) => OnCommand?.Invoke("guidbytarget");
    private void BtnGuildTp_Click(object sender, RoutedEventArgs e) => OnCommand?.Invoke("guildtp");

    private bool _wipeOn;
    private void BtnWipe_Click(object sender, RoutedEventArgs e)
    {
        _wipeOn = !_wipeOn;
        BtnWipe.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_wipeOn ? "#6b2222" : "#1a1a28"));
        BtnWipe.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_wipeOn ? "#ff4444" : "#888"));
        OnCommand?.Invoke("wipe");
    }

    private bool _autoPveOn;
    private void BtnAutoPve_Click(object sender, RoutedEventArgs e)
    {
        _autoPveOn = !_autoPveOn;
        BtnAutoPve.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_autoPveOn ? "#4a6741" : "#1a1a28"));
        BtnAutoPve.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_autoPveOn ? "#fff" : "#888"));
        OnCommand?.Invoke(_autoPveOn ? "autopve:on" : "autopve:off");
    }

    private bool _inFrameOn;
    private void BtnInFrame_Click(object sender, RoutedEventArgs e)
    {
        _inFrameOn = !_inFrameOn;
        BtnInFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_inFrameOn ? "#4a6741" : "#1a1a28"));
        BtnInFrame.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_inFrameOn ? "#fff" : "#888"));
        OnCommand?.Invoke(_inFrameOn ? "inframe:on" : "inframe:off");
    }

    private string _activeGlobalCmd = "";

    private void SendCommand(string cmd)
    {
        OnCommand?.Invoke(cmd);
        SetActiveGlobalCommand(cmd);
    }

    public void SetActiveGlobalCommand(string cmd)
    {
        _activeGlobalCmd = cmd;
        // Сбросить все большие кнопки
        var allBtns = new[] { (BtnAttack, "attack"), (BtnFollow, "follow"), (BtnAuto, "auto"), (BtnStop, "stop") };
        foreach (var (btn, key) in allBtns)
        {
            bool active = key == cmd;
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#4a6741" : "#1a1a28"));
        }
        // Сбросить подсветку StackMA/Scatter если другая команда
        if (cmd != "stackma" && cmd != "scatter")
        {
            BtnStackMA.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a2a1a"));
            BtnStackMA.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ac88a"));
            BtnScatter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a1a1a"));
            BtnScatter.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c88a8a"));
        }
    }

    // --- Collapse/Expand ---
    private void BtnCollapse_Click(object sender, RoutedEventArgs e)
    {
        bool collapsed = ContentPanel.Visibility == Visibility.Visible;
        ContentPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        BtnCollapse.Content = collapsed ? "▼" : "▬";
        BtnCollapse.ToolTip = collapsed ? "Развернуть" : "Свернуть";
    }

    // --- Settings toggle ---
    private void BtnSettings_Click(object sender, MouseButtonEventArgs e) => ToggleSettings();
    private void BtnSettings_BtnClick(object sender, RoutedEventArgs e) => ToggleSettings();

    private void ToggleSettings()
    {
        HotkeyPanel.Visibility = HotkeyPanel.Visibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Hotkey assignment ---
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private System.Windows.Threading.DispatcherTimer? _captureTimer;

    private void BtnSetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _waitingForHotkey = btn.Tag as string;
        _waitingButton = btn;
        btn.Content = "Нажми клавишу...";
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a4a28"));

        // Поллим клавиатуру через GetAsyncKeyState (работает без фокуса)
        _captureTimer?.Stop();
        _captureTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _captureTimer.Tick += CaptureTimer_Tick;
        _captureTimer.Start();
    }

    private void CaptureTimer_Tick(object? sender, EventArgs e)
    {
        if (_waitingForHotkey == null || _waitingButton == null)
        {
            _captureTimer?.Stop();
            return;
        }

        // Проверяем F1-F12, цифры, буквы
        for (int vk = 0x70; vk <= 0x7B; vk++) // F1-F12
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                var key = KeyInterop.KeyFromVirtualKey(vk);
                AssignHotkey(key);
                return;
            }
        }
        for (int vk = 0x30; vk <= 0x39; vk++) // 0-9
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                var key = KeyInterop.KeyFromVirtualKey(vk);
                AssignHotkey(key);
                return;
            }
        }
        for (int vk = 0x41; vk <= 0x5A; vk++) // A-Z
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                var key = KeyInterop.KeyFromVirtualKey(vk);
                AssignHotkey(key);
                return;
            }
        }
        // Escape — отмена
        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
        {
            _captureTimer?.Stop();
            _waitingButton!.Content = "Нажми...";
            _waitingButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28"));
            _waitingForHotkey = null;
        }
    }

    private void AssignHotkey(Key key)
    {
        _captureTimer?.Stop();
        _hotkeys[_waitingForHotkey!] = key;
        _waitingButton!.Content = key.ToString();
        _waitingButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28"));
        UpdateHotkeyLabels();
        RegisterAll();
        _waitingForHotkey = null;
    }

    private void BtnClearHotkeys_Click(object sender, RoutedEventArgs e)
    {
        UnregisterAll();
        _hotkeys.Clear();
        BtnSetHkAttack.Content = "Нажми...";
        BtnSetHkFollow.Content = "Нажми...";
        BtnSetHkStop.Content = "Нажми...";
        UpdateHotkeyLabels();
    }

    private void UpdateHotkeyLabels()
    {
        TxtHkAttack.Text = _hotkeys.ContainsKey("attack") ? _hotkeys["attack"].ToString() : "";
        TxtHkFollow.Text = _hotkeys.ContainsKey("follow") ? _hotkeys["follow"].ToString() : "";
        TxtHkStop.Text = _hotkeys.ContainsKey("stop") ? _hotkeys["stop"].ToString() : "";
    }

    // --- Global hotkeys via WndProc ---
    private void RegisterAll()
    {
        var source = System.Windows.Interop.HwndSource.FromHwnd(
            new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);

        UnregisterAll();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (_hotkeys.ContainsKey("attack"))
            RegisterHotKey(handle, HK_ATTACK, 0, (uint)KeyInterop.VirtualKeyFromKey(_hotkeys["attack"]));
        if (_hotkeys.ContainsKey("follow"))
            RegisterHotKey(handle, HK_FOLLOW, 0, (uint)KeyInterop.VirtualKeyFromKey(_hotkeys["follow"]));
        if (_hotkeys.ContainsKey("stop"))
            RegisterHotKey(handle, HK_STOP, 0, (uint)KeyInterop.VirtualKeyFromKey(_hotkeys["stop"]));
    }

    private void UnregisterAll()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HK_ATTACK);
        UnregisterHotKey(handle, HK_FOLLOW);
        UnregisterHotKey(handle, HK_STOP);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312) // WM_HOTKEY
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HK_ATTACK: Dispatcher.Invoke(() => SendCommand("attack")); break;
                case HK_FOLLOW: Dispatcher.Invoke(() => SendCommand("follow")); break;
                case HK_STOP: Dispatcher.Invoke(() => SendCommand("stop")); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }


    // --- Slave panel ---


    public event Action<string, string>? OnSlaveCommand; // (slaveName, command)

    public void UpdateSlaves(List<WowBot.Core.Game.Hivemind.SlaveInfo> slaves)
    {
        SlavePanel.Children.Clear();
        if (slaves.Count == 0) return;

        foreach (var slave in slaves)
        {
            // Синхронизировать настройки бафов из слейва (если мастер ещё не менял)
            if (slave.BuffSettings.Count > 0)
            {
                if (!_slaveBlessing.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("bl", out var bl)) _slaveBlessing[slave.Name] = bl;
                if (!_slaveAura.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("au", out var au)) _slaveAura[slave.Name] = au;
                if (!_slaveTotemEarth.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("tE", out var tE)) _slaveTotemEarth[slave.Name] = tE;
                if (!_slaveTotemFire.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("tF", out var tF)) _slaveTotemFire[slave.Name] = tF;
                if (!_slaveTotemWater.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("tW", out var tW)) _slaveTotemWater[slave.Name] = tW;
                if (!_slaveTotemAir.ContainsKey(slave.Name) && slave.BuffSettings.TryGetValue("tA", out var tA)) _slaveTotemAir[slave.Name] = tA;
            }

            bool isIdle = slave.ActiveCommand == null || slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.Stop;
            string bgColor = isIdle ? "#3d1f1f" : "#1a1a28";

            // Строка 1: [🎯][иконка] Ник → За кем  [⚔][🏃][🔄][⏹][🔒]
            var row = new Grid { Margin = new Thickness(0, 1, 0, 0), Height = 28 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 🎯
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ник
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // команды

            // 🎯 слева
            var targetBtn = new Button
            {
                Content = "🎯", FontSize = 9, Width = 22, Height = 28,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    string.IsNullOrEmpty(slave.FollowTargetName) ? "#1a1a28" : "#2a3a5a")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6688cc")),
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Бежать за моим таргетом", Style = null!,
            };
            string tn = slave.Name;
            targetBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveCommand?.Invoke(tn, "guidbytarget"); e.Handled = true; };
            Grid.SetColumn(targetBtn, 0);
            row.Children.Add(targetBtn);

            // Середина: иконка + ник + follow target
            var namePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(1, 0, 1, 0),
            };

            string iconPath = GetClassIconPath(slave.ClassName);
            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    namePanel.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath)),
                        Width = 18, Height = 18,
                        Margin = new Thickness(3, 0, 3, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                catch { }
            }

            // Ник
            namePanel.Children.Add(new TextBlock
            {
                Text = slave.Name,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c8aa6e")),
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 70,
            });

            // → За кем / 📍 к точке / 📌 к наводчику / 💥 разбег
            bool isGoto = slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.Goto;
            bool isStackMA = slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.StackMA;
            bool isScatter = slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.Scatter;
            if (isStackMA)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "📌",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8ac88a")),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                    ToolTip = "Бежит к наводчику",
                });
            }
            else if (isScatter)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "💥",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c88a8a")),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                    ToolTip = "Разбежался",
                });
            }
            else if (isGoto)
            {
                namePanel.Children.Add(new TextBlock
                {
                    Text = "📍",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e8c840")),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                    ToolTip = "Бежит к точке (Ctrl+ПКМ)",
                });
            }
            else
            {
                string followLabel = string.IsNullOrEmpty(slave.FollowTargetName) ? "М" : slave.FollowTargetName;
                bool isCustomFollow = !string.IsNullOrEmpty(slave.FollowTargetName);
                namePanel.Children.Add(new TextBlock
                {
                    Text = $"→{followLabel}",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isCustomFollow ? "#6688cc" : "#4a6741")),
                    FontSize = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 50,
                });
            }

            Grid.SetColumn(namePanel, 1);
            row.Children.Add(namePanel);

            // Правая часть: [⚔][🏃][🔄][⏹][🔒]
            var cmdPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var commands = new (string cmd, string icon, string tip)[]
            {
                ("attack", "⚔", "Бейте"), ("follow", "🏃", "Ко мне"),
                ("auto", "🔄", "Авто"), ("stop", "⏹", "Стоп"),
            };
            foreach (var (cmd, icon, tip) in commands)
            {
                var ac = slave.ActiveCommand;
                string acStr = ac?.ToString() ?? "";
                bool isActive = acStr.ToLower() == cmd ||
                    (cmd == "follow" && acStr == "Stack") ||
                    (cmd == "follow" && acStr == "StackMA");
                var cmdBtn = new Button
                {
                    Content = icon, FontSize = 11, Width = 24, Height = 28,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#4a6741" : "#1a1a28")),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    Margin = new Thickness(1, 0, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip, Style = null!,
                };
                string sn = slave.Name; string cn = cmd;
                cmdBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveCommand?.Invoke(sn, cn); e.Handled = true; };
                cmdPanel.Children.Add(cmdBtn);
            }

            // 🔒
            var lockBtn = new Button
            {
                Content = slave.IgnoreGlobal ? "🔒" : "🔓", FontSize = 9,
                Width = 20, Height = 28,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(slave.IgnoreGlobal ? "#6b3a3a" : "#1a1a28")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = slave.IgnoreGlobal ? "Игнорирует общие" : "Слушает общие", Style = null!,
            };
            string ln = slave.Name;
            lockBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveCommand?.Invoke(ln, "toggle_ignore"); e.Handled = true; };
            cmdPanel.Children.Add(lockBtn);

            // ▼ раскрыть бафы (палы + шаманы)
            string slaveClassUp = slave.ClassName.ToUpperInvariant();
            if (slaveClassUp == "PALADIN" || slaveClassUp == "SHAMAN")
            {
                var buffBtn = new Button
                {
                    Content = "▼", FontSize = 8, Width = 18, Height = 28,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a5d65")),
                    BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    Margin = new Thickness(1, 0, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Настроить бафы", Style = null!,
                };
                // Панель бафов (скрыта по умолчанию)
                var buffPanel = slaveClassUp == "SHAMAN"
                    ? BuildShamanBuffPanel(slave.Name)
                    : BuildSlaveBuffPanel(slave.Name);
                buffBtn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    buffPanel.Visibility = buffPanel.Visibility == Visibility.Visible
                        ? Visibility.Collapsed : Visibility.Visible;
                    buffBtn.Content = buffPanel.Visibility == Visibility.Visible ? "▲" : "▼";
                    e.Handled = true;
                };
                cmdPanel.Children.Add(buffBtn);

                Grid.SetColumn(cmdPanel, 2);
                row.Children.Add(cmdPanel);
                SlavePanel.Children.Add(row);
                SlavePanel.Children.Add(buffPanel);
                // Auto sub-toggles после бафов
                if (slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.Auto)
                    SlavePanel.Children.Add(BuildAutoSubPanel(slave));
            }
            else
            {
                Grid.SetColumn(cmdPanel, 2);
                row.Children.Add(cmdPanel);
                SlavePanel.Children.Add(row);
                // Auto sub-toggles
                if (slave.ActiveCommand == WowBot.Core.Game.Hivemind.Command.Auto)
                    SlavePanel.Children.Add(BuildAutoSubPanel(slave));
            }
        }
    }

    private Border BuildAutoSubPanel(WowBot.Core.Game.Hivemind.SlaveInfo slave)
    {
        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12141a")),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(22, 0, 0, 1),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var label = new TextBlock
        {
            Text = "Авто:",
            FontSize = 9,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        panel.Children.Add(label);

        // Follow toggle
        bool followOn = !slave.AutoFollowPaused;
        var followBtn = new Button
        {
            Content = "🏃", FontSize = 11, Width = 28, Height = 22,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(followOn ? "#4a6741" : "#3d1f1f")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = followOn ? "Follow ВКЛ — нажми чтобы выключить" : "Follow ВЫКЛ — нажми чтобы включить",
            Style = null!,
        };
        string fn = slave.Name;
        followBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveCommand?.Invoke(fn, "auto_toggle_follow"); e.Handled = true; };
        panel.Children.Add(followBtn);

        // Attack toggle
        bool attackOn = !slave.AutoAttackPaused;
        var attackBtn = new Button
        {
            Content = "⚔", FontSize = 11, Width = 28, Height = 22,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(attackOn ? "#4a6741" : "#3d1f1f")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = attackOn ? "Атака ВКЛ — нажми чтобы выключить" : "Атака ВЫКЛ — нажми чтобы включить",
            Style = null!,
        };
        string an = slave.Name;
        attackBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveCommand?.Invoke(an, "auto_toggle_attack"); e.Handled = true; };
        panel.Children.Add(attackBtn);

        border.Child = panel;
        return border;
    }

    private Border BuildSlaveBuffPanel(string slaveName)
    {
        string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string iconsDir = System.IO.Path.Combine(basePath, "Icons");

        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12141a")),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(22, 0, 0, 1),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Visibility = Visibility.Collapsed,
        };

        var stack = new StackPanel();

        // Благословение
        var blessLabel = new TextBlock
        {
            Text = "Благословение", FontSize = 9,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a5d65")),
            Margin = new Thickness(0, 0, 0, 2),
        };
        stack.Children.Add(blessLabel);

        var blessWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var blessings = new (string key, string icon, string tip)[]
        {
            ("BoM", "blessing_might.jpg", "Могущества"),
            ("BoK", "blessing_kings.jpg", "Королей"),
            ("BoW", "blessing_wisdom.jpg", "Мудрости"),
        };
        _slaveBlessing.TryGetValue(slaveName, out var curBlessing);
        var blessButtons = new List<Button>();
        foreach (var (key, icon, tip) in blessings)
        {
            bool isActive = curBlessing == key;
            var btn = CreateBuffIcon(iconsDir, icon, tip, isActive);
            string bk = key; string sn = slaveName;
            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _slaveBlessing[sn] = bk;
                OnSlaveCommand?.Invoke(sn, $"buff_blessing:{bk}");
                foreach (var b in blessButtons) SetBuffIconActive(b, false);
                SetBuffIconActive(btn, true);
                e.Handled = true;
            };
            blessButtons.Add(btn);
            blessWrap.Children.Add(btn);
        }
        stack.Children.Add(blessWrap);

        // Аура
        var auraLabel = new TextBlock
        {
            Text = "Аура", FontSize = 9,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a5d65")),
            Margin = new Thickness(0, 0, 0, 2),
        };
        stack.Children.Add(auraLabel);

        var auraWrap = new WrapPanel();
        var auras = new (string key, string icon, string tip)[]
        {
            ("AuRet", "aura_retribution.jpg", "Воздаяния"),
            ("AuDev", "aura_devotion.jpg", "Воина Света"),
            ("AuFrost", "aura_frost.jpg", "Защиты от льда"),
            ("AuFire", "aura_fire.jpg", "Защиты от огня"),
            ("AuShadow", "aura_shadow.jpg", "Защиты от тьмы"),
            ("AuConc", "aura_concentration.jpg", "Сосредоточенности"),
        };
        _slaveAura.TryGetValue(slaveName, out var curAura);
        var auraButtons = new List<Button>();
        foreach (var (key, icon, tip) in auras)
        {
            bool isActive = curAura == key;
            var btn = CreateBuffIcon(iconsDir, icon, tip, isActive);
            string ak = key; string sn = slaveName;
            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _slaveAura[sn] = ak;
                OnSlaveCommand?.Invoke(sn, $"buff_aura:{ak}");
                foreach (var b in auraButtons) SetBuffIconActive(b, false);
                SetBuffIconActive(btn, true);
                e.Handled = true;
            };
            auraButtons.Add(btn);
            auraWrap.Children.Add(btn);
        }
        stack.Children.Add(auraWrap);

        border.Child = stack;
        return border;
    }

    private Border BuildShamanBuffPanel(string slaveName)
    {
        string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string iconsDir = System.IO.Path.Combine(basePath, "Icons");

        var border = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12141a")),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(22, 0, 0, 1),
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Visibility = Visibility.Collapsed,
        };

        var stack = new StackPanel();

        var totemGroups = new (string label, string element, (string key, string icon, string tip)[] options, Dictionary<string, string> dict)[]
        {
            ("Земля", "earth", new[] {
                ("Stoneskin", "spell_nature_stoneskintotem.jpg", "Каменной кожи"),
                ("SoE", "spell_nature_earthbindtotem.jpg", "Силы земли"),
                ("Tremor", "spell_nature_tremortotem.jpg", "Трепета"),
            }, _slaveTotemEarth),
            ("Огонь", "fire", new[] {
                ("Flametongue", "spell_nature_guardianward.jpg", "Языка пламени"),
                ("FrostRes", "frost_resistance.jpg", "Защиты от льда"),
            }, _slaveTotemFire),
            ("Вода", "water", new[] {
                ("ManaSpring", "spell_nature_manaregentotem.jpg", "Источника маны"),
                ("HealStream", "healing_stream.jpg", "Исцеляющего потока"),
                ("Cleansing", "spell_nature_diseasecleansingtotem.jpg", "Очищения"),
                ("FireRes", "fire_resistance.jpg", "Защиты от огня"),
            }, _slaveTotemWater),
            ("Воздух", "air", new[] {
                ("WrathOfAir", "wrath_of_air.jpg", "Гнева воздуха"),
                ("Windfury", "windfury_totem.jpg", "Неистовства ветра"),
                ("NatureRes", "nature_resistance.jpg", "Защиты от природы"),
            }, _slaveTotemAir),
        };

        // Энх шаман: нет tF в настройках → fire totem управляется ротацией
        bool isEnhShaman = !_slaveTotemFire.ContainsKey(slaveName);

        foreach (var (label, element, options, dict) in totemGroups)
        {
            // Для энха скрыть выбор fire totem — Magma Totem ставится автоматически
            if (element == "fire" && isEnhShaman)
            {
                var fireLbl = new TextBlock
                {
                    Text = "Огонь: Магма (авто)", FontSize = 9, FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
                    Margin = new Thickness(0, 0, 0, 4),
                    ToolTip = "Для энха тотем огня не выбирается — Magma Totem ставится через ротацию",
                };
                stack.Children.Add(fireLbl);
                continue;
            }

            var lbl = new TextBlock
            {
                Text = label, FontSize = 9,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a5d65")),
                Margin = new Thickness(0, 0, 0, 2),
            };
            stack.Children.Add(lbl);

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            dict.TryGetValue(slaveName, out var curKey);
            var buttons = new List<Button>();
            foreach (var (key, icon, tip) in options)
            {
                bool isActive = curKey == key;
                var btn = CreateBuffIcon(iconsDir, icon, tip, isActive);
                string tk = key; string sn = slaveName; string el = element;
                var localDict = dict;
                btn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    localDict[sn] = tk;
                    OnSlaveCommand?.Invoke(sn, $"buff_totem_{el}:{tk}");
                    foreach (var b in buttons) SetBuffIconActive(b, false);
                    SetBuffIconActive(btn, true);
                    e.Handled = true;
                };
                buttons.Add(btn);
                wrap.Children.Add(btn);
            }
            stack.Children.Add(wrap);
        }

        border.Child = stack;
        return border;
    }

    private static void SetBuffIconActive(Button btn, bool active)
    {
        btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#c8aa6e" : "#3a3a28"));
        btn.Opacity = active ? 1.0 : 0.4;
    }

    private static Button CreateBuffIcon(string iconsDir, string iconFile, string tooltip, bool isActive = false)
    {
        var btn = new Button
        {
            Width = 26, Height = 26,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#c8aa6e" : "#3a3a28")),
            BorderThickness = new Thickness(1.5),
            Opacity = isActive ? 1.0 : 0.4,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 3, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip, Style = null!,
        };
        string path = System.IO.Path.Combine(iconsDir, iconFile);
        if (System.IO.File.Exists(path))
        {
            try
            {
                btn.Content = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(path)),
                    Width = 22, Height = 22,
                };
            }
            catch { btn.Content = tooltip.Substring(0, Math.Min(2, tooltip.Length)); }
        }
        else
        {
            btn.Content = tooltip.Substring(0, Math.Min(2, tooltip.Length));
            btn.FontSize = 8;
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c8aa6e"));
        }
        return btn;
    }

    private static string GetClassIconPath(string className)
    {
        string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string iconName = className.ToUpperInvariant() switch
        {
            "WARRIOR" => "mortal_strike.jpg",
            "PALADIN" => "seal_vengeance.jpg",
            "HUNTER" => "steady_shot.jpg",
            "ROGUE" => "backstab.jpg",
            "PRIEST" => "holy_light.jpg",
            "DEATHKNIGHT" => "frost_strike.jpg",
            "SHAMAN" => "chain_lightning.jpg",
            "MAGE" => "fireball.jpg",
            "WARLOCK" => "shadow_bolt.jpg",
            "DRUID" => "moonkin.jpg",
            _ => ""
        };
        if (string.IsNullOrEmpty(iconName)) return "";
        string path = System.IO.Path.Combine(basePath, "Icons", iconName);
        return System.IO.File.Exists(path) ? path : "";
    }

    // --- Heroism ---
    public event Action? OnHeroism;
    private void BtnHeroism_Click(object sender, RoutedEventArgs e)
    {
        OnHeroism?.Invoke();
        BtnHeroism.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a3a1a"));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (s, ev) => { ((System.Windows.Threading.DispatcherTimer)s!).Stop(); BtnHeroism.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")); };
        timer.Start();
    }

    // --- Stack to MA / Scatter ---
    public event Action? OnStackMA;
    public event Action? OnScatter;
    public event Action? OnTauntMT;
    public event Action? OnTauntOT;

    private void BtnTauntMT_Click(object sender, RoutedEventArgs e) => OnTauntMT?.Invoke();
    private void BtnTauntOT_Click(object sender, RoutedEventArgs e) => OnTauntOT?.Invoke();

    private void BtnStackMA_Click(object sender, RoutedEventArgs e)
    {
        OnStackMA?.Invoke();
        SetActiveGlobalCommand("stackma");
        BtnStackMA.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a5a2a"));
        BtnStackMA.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ccffcc"));
    }

    private void BtnScatter_Click(object sender, RoutedEventArgs e)
    {
        OnScatter?.Invoke();
        SetActiveGlobalCommand("scatter");
        BtnScatter.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a2a2a"));
        BtnScatter.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffcccc"));
    }

    // --- Ghost: RepopMe + Path recording ---
    public event Action? OnRepop;
    public event Action<bool>? OnRecordPath; // true=start, false=stop
    private bool _recording;

    public event Action? OnRepair;
    private void BtnRepop_Click(object sender, RoutedEventArgs e) => OnRepop?.Invoke();
    private void BtnRepair_Click(object sender, RoutedEventArgs e) => OnRepair?.Invoke();

    private void BtnRecordPath_Click(object sender, RoutedEventArgs e)
    {
        _recording = !_recording;
        BtnRecordPath.Content = _recording ? "⏹ Стоп запись" : "📍 Запись пути";
        BtnRecordPath.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_recording ? "#5a2a2a" : "#1a1a2a"));
        OnRecordPath?.Invoke(_recording);
    }

    private void FlashButton(System.Windows.Controls.Button btn, string bgOn, string fgOn, string bgOff, string fgOff)
    {
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgOn));
        btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgOn));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (s, ev) =>
        {
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgOff));
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgOff));
            timer.Stop();
        };
        timer.Start();
    }

    // --- Interact / Gossip ---
    public event Action? OnInteract;
    public event Action<int>? OnGossipSelect;
    public event Action? OnGossipAccept;

    private void BtnInteract_Click(object sender, RoutedEventArgs e)
    {
        OnInteract?.Invoke();
        // Подсветка
        BtnInteract.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2a3a5a"));
        BtnInteract.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88bbee"));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (s, ev) =>
        {
            BtnInteract.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28"));
            BtnInteract.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888"));
            timer.Stop();
        };
        timer.Start();
    }

    private void BtnGossipAccept_Click(object sender, RoutedEventArgs e) => OnGossipAccept?.Invoke();

    /// <summary>Показать gossip опции</summary>
    public void ShowGossipOptions(List<string> options)
    {
        GossipPanel.Children.Clear();
        if (options.Count == 0)
        {
            GossipPanel.Visibility = Visibility.Collapsed;
            BtnGossipAccept.Visibility = Visibility.Collapsed;
            return;
        }

        for (int i = 0; i < options.Count; i++)
        {
            int idx = i + 1;
            var btn = new Button
            {
                Style = null!,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ccc")),
                BorderThickness = new Thickness(0),
                Height = 22,
                Margin = new Thickness(0, 1, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 0, 6, 0),
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = $"{idx}.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E")),
                FontSize = 10, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            sp.Children.Add(new TextBlock
            {
                Text = options[i],
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 240,
            });
            btn.Content = sp;

            btn.PreviewMouseLeftButtonDown += (s, ev) =>
            {
                OnGossipSelect?.Invoke(idx);
                // Показать лоадер, заблокировать повторные клики
                ShowLoading();
                ev.Handled = true;
            };
            GossipPanel.Children.Add(btn);
        }

        GossipPanel.Visibility = Visibility.Visible;
        BtnGossipAccept.Visibility = Visibility.Visible;
    }

    /// <summary>Скрыть gossip панель</summary>
    public void HideGossip()
    {
        GossipPanel.Visibility = Visibility.Collapsed;
        BtnGossipAccept.Visibility = Visibility.Collapsed;
    }

    /// <summary>Только gossip список (не кнопка Принять) — для автоскрытия</summary>
    public bool IsGossipListVisible => GossipPanel.Visibility == Visibility.Visible;

    /// <summary>Показать лоадер вместо gossip списка</summary>
    public void ShowLoading()
    {
        GossipPanel.Children.Clear();
        var txt = new TextBlock
        {
            Text = "⏳ Загрузка...",
            FontSize = 10,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };
        GossipPanel.Children.Add(txt);
        GossipPanel.Visibility = Visibility.Visible;
        BtnGossipAccept.Visibility = Visibility.Collapsed;
    }

    /// <summary>Скрыть только список gossip (оставить кнопку Принять)</summary>
    public void HideGossipList()
    {
        GossipPanel.Children.Clear();
        GossipPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Показать только кнопку Принять (для popup подтверждения)</summary>
    public void ShowAcceptOnly()
    {
        GossipPanel.Children.Clear();
        GossipPanel.Visibility = Visibility.Collapsed;
        BtnGossipAccept.Visibility = Visibility.Visible;
    }

    // --- Navigation dropdown ---
    private void BtnNavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navExpanded = !_navExpanded;
        NavSlaveList.Visibility = _navExpanded ? Visibility.Visible : Visibility.Collapsed;
        TxtNavArrow.Text = _navExpanded ? " ▲" : " ▼";
    }

    public void UpdateNavSlaves(List<WowBot.Core.Game.Hivemind.SlaveInfo> slaves,
        HashSet<string> selectedNames, HashSet<string> pinnedNames)
    {
        NavSlaveList.Children.Clear();

        foreach (var slave in slaves)
        {
            bool isSel = selectedNames.Contains(slave.Name);
            bool isPinned = pinnedNames.Contains(slave.Name);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };

            // Кнопка выбора: иконка + ник
            var btn = new Button
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isSel ? "#4a6741" : "#1a1a28")),
                BorderThickness = new Thickness(0),
                Height = 26,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(5, 0, 5, 0),
                Style = null!,
                MinWidth = 120,
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            string iconPath = GetClassIconPath(slave.ClassName);
            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    sp.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath)),
                        Width = 18, Height = 18,
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                catch { }
            }
            sp.Children.Add(new TextBlock
            {
                Text = slave.Name,
                Foreground = isSel ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#aaa")),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
            btn.Content = sp;

            string name = slave.Name;
            btn.PreviewMouseLeftButtonDown += (s, ev) => { OnSlaveToggled?.Invoke(name); ev.Handled = true; };
            row.Children.Add(btn);

            // Кнопка 📌
            var pinBtn = new Button
            {
                Content = "📌", FontSize = 9,
                Width = 22, Height = 26,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isPinned ? "#5a5020" : "#1a1a28")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isPinned ? "#C8A84E" : "#555")),
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = isPinned ? "Закреплён" : "Закрепить",
                Style = null!,
            };
            pinBtn.PreviewMouseLeftButtonDown += (s, ev) => { OnSlavePinToggled?.Invoke(name); ev.Handled = true; };
            row.Children.Add(pinBtn);

            NavSlaveList.Children.Add(row);
        }

        TxtNavHint.Text = selectedNames.Count == 0
            ? "Ctrl+ПКМ → все"
            : $"Ctrl+ПКМ → {string.Join(", ", selectedNames)}";
    }

    // --- Save/Load hotkeys ---
    public Dictionary<string, string> GetHotkeySettings()
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in _hotkeys)
            result[kv.Key] = kv.Value.ToString();
        return result;
    }

    public void LoadHotkeySettings(Dictionary<string, string>? settings)
    {
        if (settings == null) return;
        foreach (var kv in settings)
        {
            if (Enum.TryParse<Key>(kv.Value, out var key))
            {
                _hotkeys[kv.Key] = key;
            }
        }
        UpdateHotkeyLabels();
        // Update button texts
        if (_hotkeys.ContainsKey("attack")) BtnSetHkAttack.Content = _hotkeys["attack"].ToString();
        if (_hotkeys.ContainsKey("follow")) BtnSetHkFollow.Content = _hotkeys["follow"].ToString();
        if (_hotkeys.ContainsKey("stop")) BtnSetHkStop.Content = _hotkeys["stop"].ToString();
    }
}
