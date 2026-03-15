using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WowBot.Injector;

public partial class OverlayWindow : Window
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x00, 0xff, 0x88));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xe9, 0x45, 0x60));

    public event Action? OnRotationToggle;
    public event Action? OnFollowToggle;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void BtnRotation_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OnRotationToggle?.Invoke();
    }

    private void BtnFollow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OnFollowToggle?.Invoke();
    }

    public void UpdateRotation(bool active)
    {
        TxtRotationState.Text = active ? "ON" : "OFF";
        TxtRotationState.Foreground = active ? Green : Red;
        RotationDot.Background = active ? Green : Red;
    }

    public void UpdateFollow(bool active, string info = "")
    {
        TxtFollowState.Text = active ? "ON" : "OFF";
        TxtFollowState.Foreground = active ? Green : Red;
        FollowDot.Background = active ? Green : Red;
        if (!string.IsNullOrEmpty(info))
            TxtFollowState.Text = $"ON {info}";
    }

    public void UpdateInfo(string text)
    {
        TxtInfo.Text = text;
    }

    public void UpdateStatus(string text)
    {
        TxtStatus.Text = text;
    }
}
