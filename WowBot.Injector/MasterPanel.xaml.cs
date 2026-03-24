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

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int HK_ATTACK = 1;
    private const int HK_FOLLOW = 2;
    private const int HK_STOP = 3;

    public event Action<string>? OnCommand;

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

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    // --- Buttons ---
    private void BtnAttack_Click(object sender, RoutedEventArgs e) => SendCommand("attack");
    private void BtnFollow_Click(object sender, RoutedEventArgs e) => SendCommand("follow");
    private void BtnStop_Click(object sender, RoutedEventArgs e) => SendCommand("stop");

    private void BtnAuto_Click(object sender, RoutedEventArgs e) => SendCommand("auto");

    private void SendCommand(string cmd)
    {
        OnCommand?.Invoke(cmd);
        // Flash button
        var btn = cmd switch { "attack" => BtnAttack, "follow" => BtnFollow, "auto" => BtnAuto, _ => BtnStop };
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a6741"));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (s, e) => { btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")); timer.Stop(); };
        timer.Start();
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
