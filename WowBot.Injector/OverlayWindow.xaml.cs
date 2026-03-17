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
    public bool AutoSelectTarget => ChkAutoTarget.IsChecked == true;
    public bool AoeEnabled => BtnAoe.IsChecked == true;
    public bool UseMultiDot => ChkMultiDot.IsChecked == true;
    public int MaxDotTargets => (int)SliderMaxDots.Value;
    public bool UseMindSear => ChkMindSear.IsChecked == true;
    public int MindSearTargets => (int)SliderMindSear.Value;
    public int DispManaThreshold => (int)SliderDispMana.Value;
    public int SFManaThreshold => (int)SliderSFMana.Value;
    public bool BuffsEnabled => BtnBuffs.IsChecked == true;

    // Баффы — динамические чекбоксы по классу
    private readonly Dictionary<string, CheckBox> _buffCheckboxes = new();

    /// <summary>Возвращает список включённых баффов (русские названия заклинаний)</summary>
    public List<string> GetEnabledBuffs()
    {
        var result = new List<string>();
        foreach (var (name, chk) in _buffCheckboxes)
            if (chk.IsChecked == true) result.Add(name);
        return result;
    }

    // Баффы по классам: (название спелла, описание для UI, включен по умолчанию)
    private static readonly Dictionary<string, (string spell, string label, bool defaultOn)[]> ClassBuffs = new()
    {
        ["PRIEST"] = new[]
        {
            ("Молитва стойкости", "Молитва стойкости", true),
            ("Молитва духа", "Молитва духа", true),
            ("Молитва защиты от темной магии", "Защита от темной магии", true),
            ("Объятия вампира", "Объятия вампира", true),
            ("Внутренний огонь", "Внутренний огонь", true),
            ("Защита от страха", "Защита от страха", false),
        },
        ["DRUID"] = new[]
        {
            ("Дар дикой природы", "Дар дикой природы", true),
            ("Шипы", "Шипы", false),
        },
        ["MAGE"] = new[]
        {
            ("Чародейская гениальность", "Чародейская гениальность", true),
            ("Ледяная броня", "Ледяная броня", false),
            ("Расплавленная броня", "Расплавленная броня", true),
            ("Чародейская броня", "Чародейская броня", false),
        },
        ["WARLOCK"] = new[]
        {
            ("Скверноброня", "Скверноброня", true),
        },
        ["SHAMAN"] = new[]
        {
            ("Щит молний", "Щит молний", true),
            ("Щит воды", "Щит воды", false),
        },
    };

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

    private void BtnAoe_Click(object sender, RoutedEventArgs e)
    {
        BtnAoe.Content = BtnAoe.IsChecked == true ? "ON" : "OFF";
    }

    private void SliderMaxDots_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMaxDots != null) TxtMaxDots.Text = $"{(int)e.NewValue}";
    }

    private void SliderMindSear_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMindSearTargets != null) TxtMindSearTargets.Text = $"{(int)e.NewValue}";
    }


    private void BtnBuffs_Click(object sender, RoutedEventArgs e)
    {
        BtnBuffs.Content = BtnBuffs.IsChecked == true ? "ON" : "OFF";
    }

    /// <summary>Вызывается при Attach — создаёт чекбоксы баффов по классу</summary>
    public void SetPlayerClass(string playerClass)
    {
        PanelBuffs.Children.Clear();
        _buffCheckboxes.Clear();

        if (!ClassBuffs.TryGetValue(playerClass, out var buffs))
            return;

        foreach (var (spell, label, defaultOn) in buffs)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16181e")),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 1, 0, 0),
            };
            var chk = new CheckBox
            {
                Content = label,
                IsChecked = defaultOn,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b8d93")),
                FontSize = 11,
            };
            border.Child = chk;
            PanelBuffs.Children.Add(border);
            _buffCheckboxes[spell] = chk;
        }
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
