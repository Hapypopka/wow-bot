using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WowBot.Injector;

public partial class OverlayWindow : Window
{
    // Click-through: мышь проходит сквозь окно (кроме перетаскивания)
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Делаем окно click-through (мышь проходит сквозь)
        var hwnd = new WindowInteropHelper(this).Handle;
        int extStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extStyle | WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// Включает перетаскивание (временно убирает click-through)
    /// </summary>
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    public void UpdateRotationStatus(string status, bool isActive)
    {
        TxtRotation.Text = $"[{(isActive ? "ON" : "OFF")}] {status}";
        TxtRotation.Foreground = isActive
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xff, 0x88))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe9, 0x45, 0x60));
    }

    public void UpdatePlayerHp(int hp, int maxHp, float percent)
    {
        TxtPlayerHp.Text = $"HP: {hp}/{maxHp} ({percent:F0}%)";
    }

    public void UpdateTarget(string info)
    {
        TxtTargetInfo.Text = $"Target: {info}";
    }

    public void UpdateFollow(string info)
    {
        TxtFollowInfo.Text = $"Follow: {info}";
    }
}
