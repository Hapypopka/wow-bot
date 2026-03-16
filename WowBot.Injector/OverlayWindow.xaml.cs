using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WowBot.Injector;

public partial class OverlayWindow : Window
{
    public event Action? OnRotationToggle;
    public event Action? OnFollowToggle;
    public event Action? OnSetFollowTarget;
    public event Action<float>? OnFollowDistanceChanged;

    // Настройки ротации — доступны из MainWindow
    public bool UseVT => ChkVT.IsChecked == true;
    public bool UseDP => ChkDP.IsChecked == true;
    public bool UseSWP => ChkSWP.IsChecked == true;
    public bool UseMB => ChkMB.IsChecked == true;
    public bool UseMF => ChkMF.IsChecked == true;
    public bool UseSF => ChkSF.IsChecked == true;
    public bool UseDisp => ChkDisp.IsChecked == true;
    public bool AutoFace => ChkAutoFace.IsChecked == true;
    public int DispManaThreshold => (int)SliderDispMana.Value;
    public int SFManaThreshold => (int)SliderSFMana.Value;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    // --- Rotation ---
    private void ToggleRotation_Click(object sender, MouseButtonEventArgs e) { }

    private void BtnRotation_Click(object sender, RoutedEventArgs e)
    {
        OnRotationToggle?.Invoke();
    }

    // --- Follow ---
    private void ToggleFollow_Click(object sender, MouseButtonEventArgs e) { }

    private void BtnFollow_Click(object sender, RoutedEventArgs e)
    {
        OnFollowToggle?.Invoke();
    }

    private void BtnSetFollow_Click2(object sender, RoutedEventArgs e)
    {
        OnSetFollowTarget?.Invoke();
    }

    // --- Sliders ---
    private void SliderDispMana_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtDispMana != null) TxtDispMana.Text = $"{(int)e.NewValue}";
    }

    private void SliderSFMana_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSFMana != null) TxtSFMana.Text = $"{(int)e.NewValue}";
    }

    private void SliderDist2_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtDist != null) TxtDist.Text = $"{(int)e.NewValue}";
        OnFollowDistanceChanged?.Invoke((float)e.NewValue);
    }

    // --- Updates from MainWindow ---
    public void UpdateRotation(bool active)
    {
        BtnRotation.IsChecked = active;
        BtnRotation.Content = active ? "ON" : "OFF";
    }

    public void UpdateFollow(bool active, string info = "")
    {
        BtnFollow.IsChecked = active;
        BtnFollow.Content = active ? "ON" : "OFF";
        TxtFollowInfo.Text = active && !string.IsNullOrEmpty(info) ? $"Follow: {info}" : "";
    }

    public void UpdateInfo(string text)
    {
        TxtInfo.Text = text;
    }

    public void UpdateStatus(string text)
    {
        TxtSpec.Text = text;
    }
}
