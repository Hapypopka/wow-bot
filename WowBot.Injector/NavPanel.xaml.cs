using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WowBot.Injector;

public partial class NavPanel : Window
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public event Action<string>? OnSlaveToggled; // тогл выбора
    public event Action<string>? OnSlavePinToggled; // тогл закрепления

    public NavPanel()
    {
        InitializeComponent();
        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Привязать к окну WoW (не плавает поверх всех)</summary>
    public void SetOwnerHwnd(IntPtr wowHwnd)
    {
        if (wowHwnd == IntPtr.Zero) return;
        var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetParent(myHwnd, wowHwnd);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    public void UpdateSlaves(List<WowBot.Core.Game.Hivemind.SlaveInfo> slaves,
        HashSet<string> selectedNames, HashSet<string> pinnedNames)
    {
        SlaveList.Children.Clear();

        foreach (var slave in slaves)
        {
            bool isSel = selectedNames.Contains(slave.Name);
            bool isPinned = pinnedNames.Contains(slave.Name);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };

            // Кнопка выбора: иконка + ник
            var btn = new Button
            {
                Background = isSel
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a6741"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
                BorderThickness = new Thickness(0),
                Height = 26,
                Cursor = Cursors.Hand,
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
                    sp.Children.Add(new Image
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
            btn.PreviewMouseLeftButtonDown += (s, e) => { OnSlaveToggled?.Invoke(name); e.Handled = true; };
            row.Children.Add(btn);

            // Кнопка 📌 — закрепить
            var pinBtn = new Button
            {
                Content = "📌",
                FontSize = 9,
                Width = 22, Height = 26,
                Background = isPinned
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a5020"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1a1a28")),
                Foreground = isPinned
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8A84E"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = isPinned ? "Закреплён" : "Закрепить",
                Style = null!,
            };
            pinBtn.PreviewMouseLeftButtonDown += (s, e) => { OnSlavePinToggled?.Invoke(name); e.Handled = true; };
            row.Children.Add(pinBtn);

            SlaveList.Children.Add(row);
        }

        TxtHint.Text = selectedNames.Count == 0
            ? "Ctrl+ПКМ → все"
            : $"Ctrl+ПКМ → {string.Join(", ", selectedNames)}";
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
}
