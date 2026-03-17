using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WowBot.Injector;

public partial class OverlayWindow : Window
{
    // Не забирать фокус у WoW при клике
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
    }
    public event Action? OnRotationToggle;
    public event Action? OnFollowToggle;
    public event Action? OnSetFollowTarget;
    public event Action<float>? OnFollowDistanceChanged;

    // Rotation spell toggles (by spell key)
    private readonly Dictionary<string, ToggleButton> _spellToggles = new();
    private string _playerClass = "";

    // AoE toggles
    private ToggleButton _chkMultiDot = null!, _chkMindSear = null!;
    private Slider _sliderMaxDots = null!, _sliderMindSear = null!;

    // Curse selection (radio-style, only one at a time)
    private string _selectedCurse = "CoA"; // "CoA", "CoD", "CoE", or "" (none)
    private readonly Dictionary<string, ToggleButton> _curseToggles = new();
    private static readonly (string key, string icon, string tooltip)[] CurseOptions =
    {
        ("CoA", "curse_agony.jpg", "Проклятие агонии"),
        ("CoD", "curse_doom.jpg", "Проклятие рока"),
        ("CoE", "curse_elements.jpg", "Проклятие стихий"),
    };

    // Seal selection (radio-style, only one at a time)
    private string _selectedSeal = "SoV"; // "SoV", "SoC", or "" (none)
    private readonly Dictionary<string, ToggleButton> _sealToggles = new();
    private static readonly (string key, string icon, string tooltip)[] SealOptions =
    {
        ("SoV", "seal_vengeance.jpg", "Печать мщения"),
        ("SoC", "seal_command.jpg", "Печать повиновения"),
    };

    // Mana sliders
    private Slider _sliderDispMana = null!, _sliderSFMana = null!;

    // Follow slider
    private Slider _sliderDist = null!;

    // Target checkboxes
    private CheckBox _chkAutoFace = null!, _chkAutoTarget = null!;
    private Slider _sliderMaxRange = null!;

    /// <summary>Проверяет включён ли спелл по ключу</summary>
    public bool IsSpellEnabled(string key) =>
        _spellToggles.TryGetValue(key, out var btn) ? btn.IsChecked == true : true;

    /// <summary>Lua-строка с флагами спеллов: WB_S={VT=true,DP=false,...}</summary>
    public string GetSpellFlagsLua()
    {
        if (_spellToggles.Count == 0 && _curseToggles.Count == 0) return "WB_S={} ";
        var parts = new List<string>();
        foreach (var (key, btn) in _spellToggles)
            parts.Add($"{key}={(btn.IsChecked == true ? "true" : "false")}");
        // Curse flags: CoA/CoD/CoE — только один true
        foreach (var (key, _, _) in CurseOptions)
            parts.Add($"{key}={(_selectedCurse == key ? "true" : "false")}");
        // Seal flags: SoV/SoC — только один true
        foreach (var (key, _, _) in SealOptions)
            parts.Add($"{key}={(_selectedSeal == key ? "true" : "false")}");
        return "WB_S={" + string.Join(",", parts) + "} ";
    }

    // Обратная совместимость
    public bool UseVT => IsSpellEnabled("VT");
    public bool UseDP => IsSpellEnabled("DP");
    public bool UseSWP => IsSpellEnabled("SWP");
    public bool UseMB => IsSpellEnabled("MB");
    public bool UseMF => IsSpellEnabled("MF");
    public bool UseSF => IsSpellEnabled("SF");
    public bool UseDisp => IsSpellEnabled("Disp");
    public string SelectedSeal => _selectedSeal;
    public bool AutoFace => _chkAutoFace?.IsChecked == true;
    public bool AutoSelectTarget => _chkAutoTarget?.IsChecked == true;
    public int MaxTargetRange => (int)(_sliderMaxRange?.Value ?? 30);

    // Спеллы по спекам: (key, icon, tooltip, defaultOn)
    private static readonly Dictionary<string, (string key, string icon, string tooltip, bool on)[]> SpecSpells = new()
    {
        ["Shadow Priest"] = new[]
        {
            ("VT", "vt.jpg", "Прикосновение вампира", true),
            ("DP", "dp.jpg", "Всепожирающая чума", true),
            ("SWP", "swp.jpg", "Слово Тьмы: Боль", true),
            ("MB", "mb.jpg", "Взрыв разума", true),
            ("MF", "mf.jpg", "Пытка разума", true),
            ("SF", "sf.jpg", "Исчадие Тьмы", true),
            ("Disp", "disp.jpg", "Слияние с Тьмой", true),
        },
        ["Balance Druid"] = new[]
        {
            ("Moonkin", "moonkin.jpg", "Облик лунного совуха", true),
            ("Starfall", "starfall.jpg", "Звездопад", true),
            ("Treants", "treants.jpg", "Сила Природы", true),
            ("FF", "faerie_fire.jpg", "Волшебный огонь", true),
            ("IS", "insect_swarm.jpg", "Рой насекомых", true),
            ("MF_d", "moonfire.jpg", "Лунный огонь", true),
            ("Starfire", "starfire.jpg", "Звездный огонь", true),
            ("Wrath", "wrath.jpg", "Гнев", true),
            ("Innervate", "innervate.jpg", "Озарение", true),
        },
        ["Demonology Lock"] = new[]
        {
            ("Meta", "meta.jpg", "Метаморфоза", true),
            ("DemonEmpower", "demon_empower.jpg", "Усиление демона", true),
            ("ImmoAura", "immo_aura.jpg", "Жертвенный костер", true),
            ("Corruption", "corruption.jpg", "Порча", true),
            ("Immolate", "immolate.jpg", "Жертвенный огонь", true),
            ("SoulFire", "soul_fire.jpg", "Огонь души", true),
            ("Incinerate", "incinerate.jpg", "Испепеление", true),
            ("ShadowBolt", "shadow_bolt.jpg", "Стрела Тьмы", true),
            ("LifeTap", "life_tap.jpg", "Жизнеотвод", true),
            ("LTGlyph", "life_tap.jpg", "Символ Жизнеотвода", false),
        },
        ["Ret Paladin"] = new[]
        {
            ("Judge", "judgement.jpg", "Правосудие", true),
            ("CS", "crusader_strike.jpg", "Удар воина Света", true),
            ("DS", "divine_storm.jpg", "Божественная буря", true),
            ("Cons", "consecration.jpg", "Освящение", true),
            ("Exo", "exorcism.jpg", "Экзорцизм", true),
            ("HoW", "hammer_wrath.jpg", "Молот гнева", true),
            ("SS", "sacred_shield.jpg", "Священный щит", true),
        },
    };
    public bool AoeEnabled => BtnAoe.IsChecked == true;
    public bool UseMultiDot => _chkMultiDot?.IsChecked == true;
    public int MaxDotTargets => (int)(_sliderMaxDots?.Value ?? 4);
    public bool UseMindSear => _chkMindSear?.IsChecked == true;
    public int MindSearTargets => (int)(_sliderMindSear?.Value ?? 4);
    public int DispManaThreshold => (int)(_sliderDispMana?.Value ?? 15);
    public int SFManaThreshold => (int)(_sliderSFMana?.Value ?? 50);
    public bool BuffsEnabled => BtnBuffs.IsChecked == true;

    private readonly Dictionary<string, ToggleButton> _buffToggles = new();
    private string _activeSubmenu = "";
    private bool _isDragging;
    private Point _dragStart;

    // --- Persistent settings ---
    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    private Dictionary<string, JsonElement> _saved = new();

    public List<string> GetEnabledBuffs()
    {
        var result = new List<string>();
        foreach (var (name, btn) in _buffToggles)
            if (btn.IsChecked == true) result.Add(name);
        return result;
    }

    private static readonly Dictionary<string, (string spell, string icon, string label, bool defaultOn)[]> ClassBuffs = new()
    {
        ["PRIEST"] = new[]
        {
            ("Молитва стойкости", "fort.jpg", "Молитва стойкости", true),
            ("Молитва духа", "spirit.jpg", "Молитва духа", true),
            ("Молитва защиты от темной магии", "shadow_prot.jpg", "Защита от темной магии", true),
            ("Объятия вампира", "ve.jpg", "Объятия вампира", true),
            ("Внутренний огонь", "inner_fire.jpg", "Внутренний огонь", true),
            ("Защита от страха", "fear_ward.jpg", "Защита от страха", false),
        },
        ["DRUID"] = new[]
        {
            ("Дар дикой природы", "gift_wild.jpg", "Дар дикой природы", true),
            ("Шипы", "thorns.jpg", "Шипы", false),
        },
        ["MAGE"] = new[]
        {
            ("Чародейская гениальность", "spirit.jpg", "Чародейская гениальность", true),
            ("Расплавленная броня", "inner_fire.jpg", "Расплавленная броня", true),
            ("Ледяная броня", "shadow_prot.jpg", "Ледяная броня", false),
            ("Чародейская броня", "spirit.jpg", "Чародейская броня", false),
        },
        ["WARLOCK"] = new[]
        {
            ("Доспех Скверны", "fel_armor.jpg", "Доспех Скверны", true),
            ("WB_SPELLSTONE", "spellstone.jpg", "Камень чар", true),
        },
        ["SHAMAN"] = new[]
        {
            ("Щит молний", "mb.jpg", "Щит молний", true),
            ("Щит воды", "shadow_prot.jpg", "Щит воды", false),
        },
        ["PALADIN"] = new[]
        {
            ("Аура воздаяния", "ret_aura.jpg", "Аура воздаяния", true),
            ("Благословение могущества", "blessing_might.jpg", "Благословение могущества", true),
        },
    };

    public OverlayWindow()
    {
        InitializeComponent();
    }

    // --- Main button: click = menu, drag = move ---
    private void MainButton_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = false;
            _dragStart = e.GetPosition(this);
            MainButton.CaptureMouse();
        }
    }

    private void MainButton_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !MainButton.IsMouseCaptured) return;

        var pos = e.GetPosition(this);
        if (!_isDragging && (Math.Abs(pos.X - _dragStart.X) > 4 || Math.Abs(pos.Y - _dragStart.Y) > 4))
        {
            _isDragging = true;
            MainButton.ReleaseMouseCapture();
            DragMove();
            SaveSettings();
        }
    }

    private void MainButton_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && MainButton.IsMouseCaptured)
        {
            MainButton.ReleaseMouseCapture();
            if (!_isDragging)
            {
                // Это был клик, не drag → toggle menu
                bool isOpen = MenuPanel.Visibility == Visibility.Visible;
                MenuPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
                if (isOpen) SubPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    // --- Menu item clicks → show submenu ---
    private void MenuRotation_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Rotation");
    private void MenuAoe_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Aoe");
    private void MenuBuffs_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Buffs");
    private void MenuFollow_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Follow");
    private void MenuTarget_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Target");

    private void ShowSubmenu(string name)
    {
        if (_activeSubmenu == name && SubPanel.Visibility == Visibility.Visible)
        {
            SubPanel.Visibility = Visibility.Collapsed;
            _activeSubmenu = "";
            return;
        }

        _activeSubmenu = name;
        SubContent.Children.Clear();

        switch (name)
        {
            case "Rotation": BuildRotationSubmenu(); break;
            case "Aoe": BuildAoeSubmenu(); break;
            case "Buffs": BuildBuffsSubmenu(); break;
            case "Follow": BuildFollowSubmenu(); break;
            case "Target": BuildTargetSubmenu(); break;
        }

        SubPanel.Visibility = Visibility.Visible;
    }

    // --- Build submenus ---

    private void BuildRotationSubmenu()
    {
        AddLabel("Заклинания");

        // Определяем спеллы по спеку
        string specKey = TxtSpec.Text ?? "";
        if (!SpecSpells.TryGetValue(specKey, out var spells))
        {
            // Фоллбэк на SP если спек не определён
            SpecSpells.TryGetValue("Shadow Priest", out spells);
        }

        if (spells != null)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var (key, icon, tooltip, defaultOn) in spells)
            {
                bool wasChecked = _spellToggles.TryGetValue(key, out var old)
                    ? old.IsChecked == true
                    : GetSavedBool($"spell_{key}", defaultOn);
                _spellToggles[key] = AddSpellIcon(wrap, icon, tooltip, wasChecked);
            }
            SubContent.Children.Add(wrap);
        }

        if (specKey == "Shadow Priest")
        {
            _sliderDispMana = AddSlider("Мана Слияние", _sliderDispMana?.Value ?? GetSavedDouble("slider_dispMana", 15), 0, 100, 5);
            _sliderSFMana = AddSlider("Мана Исчадие", _sliderSFMana?.Value ?? GetSavedDouble("slider_sfMana", 50), 0, 100, 5);
        }
        else if (specKey == "Balance Druid")
        {
            _sliderDispMana = AddSlider("Мана Озарение", _sliderDispMana?.Value ?? GetSavedDouble("slider_dispMana", 30), 0, 100, 5);
        }
        else if (specKey == "Demonology Lock")
        {
            // Секция выбора проклятия (радио — только одно)
            AddLabel("Выбор проклятия");
            var curseWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var (key, icon, tooltip) in CurseOptions)
            {
                bool isSelected = _selectedCurse == key;
                var toggle = AddSpellIcon(curseWrap, icon, tooltip, isSelected);
                _curseToggles[key] = toggle;

                // Radio-поведение: клик — выбрать это, снять остальные
                var curseKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedCurse = curseKey;
                    foreach (var (k, btn) in _curseToggles)
                        if (k != curseKey) btn.IsChecked = false;
                };
                toggle.Unchecked += (s, e) =>
                {
                    // Если снимают текущий — разрешаем (без проклятия)
                    if (_selectedCurse == curseKey) _selectedCurse = "";
                };
            }
            SubContent.Children.Add(curseWrap);

            _sliderDispMana = AddSlider("Мана Life Tap", _sliderDispMana?.Value ?? GetSavedDouble("slider_dispMana", 30), 0, 100, 5);
        }
    }

    private void BuildAoeSubmenu()
    {
        string specKey = TxtSpec.Text ?? "";

        if (specKey == "Shadow Priest")
        {
            AddLabel("Заклинания");
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            _chkMultiDot = AddSpellIcon(wrap, "vt.jpg", "Мультидот VT", _chkMultiDot?.IsChecked ?? GetSavedBool("chk_multiDot", true));
            _chkMindSear = AddSpellIcon(wrap, "ms.jpg", "Иссушение разума", _chkMindSear?.IsChecked ?? GetSavedBool("chk_mindSear", true));
            SubContent.Children.Add(wrap);

            _sliderMaxDots = AddSlider("Макс. целей VT", _sliderMaxDots?.Value ?? GetSavedDouble("slider_maxDots", 4), 1, 10, 1);
            _sliderMindSear = AddSlider("Целей Mind Sear", _sliderMindSear?.Value ?? GetSavedDouble("slider_mindSear", 4), 2, 15, 1);
        }
        else if (specKey == "Balance Druid")
        {
            AddLabel("AoE (в ротации — авто)");
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            AddSpellIcon(wrap, "starfall.jpg", "Звездопад (авто)", true);
            AddSpellIcon(wrap, "hurricane.jpg", "Гроза (в разработке)", false);
            SubContent.Children.Add(wrap);
        }
        else if (specKey == "Demonology Lock")
        {
            AddLabel("AoE в разработке");
        }
        else
        {
            AddLabel("AoE для этого спека в разработке");
        }
    }

    private void BuildBuffsSubmenu()
    {
        if (_buffToggles.Count == 0)
        {
            AddLabel("Класс не определен");
            return;
        }

        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var (spell, oldToggle) in _buffToggles.ToList())
        {
            // Preserve state, rebuild icon
            bool wasChecked = oldToggle.IsChecked == true;
            string? tooltip = oldToggle.ToolTip?.ToString();
            string iconFile = oldToggle.Tag?.ToString() ?? "";

            var newToggle = AddSpellIcon(wrap, iconFile, tooltip ?? spell, wasChecked);
            _buffToggles[spell] = newToggle;
        }
        SubContent.Children.Add(wrap);

        // Выбор печати для паладина (радио)
        if (_playerClass == "PALADIN")
        {
            AddLabel("Выбор печати");
            var sealWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            foreach (var (key, icon, tooltip) in SealOptions)
            {
                bool isSelected = _selectedSeal == key;
                var toggle = AddSpellIcon(sealWrap, icon, tooltip, isSelected);
                _sealToggles[key] = toggle;

                var sealKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedSeal = sealKey;
                    foreach (var (k, btn) in _sealToggles)
                        if (k != sealKey) btn.IsChecked = false;
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedSeal == sealKey) _selectedSeal = "";
                };
            }
            SubContent.Children.Add(sealWrap);
        }
    }

    private void BuildFollowSubmenu()
    {
        var btn = new Button
        {
            Content = "Set Follow Target",
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8b8d93")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6),
        };
        btn.Click += (s, e) => OnSetFollowTarget?.Invoke();
        SubContent.Children.Add(btn);

        _sliderDist = AddSlider("Дистанция", _sliderDist?.Value ?? GetSavedDouble("slider_dist", 8), 0, 20, 1);
        _sliderDist.ValueChanged += (s, e) => OnFollowDistanceChanged?.Invoke((float)e.NewValue);
    }

    private void BuildTargetSubmenu()
    {
        _chkAutoFace = AddCheckBox("Автоповорот к таргету", _chkAutoFace?.IsChecked ?? GetSavedBool("chk_autoFace", false));
        _chkAutoTarget = AddCheckBox("Автовыбор таргета", _chkAutoTarget?.IsChecked ?? GetSavedBool("chk_autoTarget", false));
        _sliderMaxRange = AddSlider("Макс. дальность", _sliderMaxRange?.Value ?? GetSavedDouble("slider_maxRange", 30), 10, 45, 5);
    }

    // --- Helpers ---

    private ToggleButton AddSpellIcon(WrapPanel wrap, string iconFile, string tooltip, bool isChecked)
    {
        var toggle = new ToggleButton
        {
            Width = 34, Height = 34,
            Margin = new Thickness(1),
            Cursor = Cursors.Hand,
            IsChecked = isChecked,
            ToolTip = tooltip,
            Tag = iconFile,
            Style = (Style)FindResource("SpellIcon"),
        };
        toggle.Checked += (s, e) => SaveSettings();
        toggle.Unchecked += (s, e) => SaveSettings();

        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icons", iconFile);
        if (File.Exists(iconPath))
        {
            toggle.Content = new Image
            {
                Source = new BitmapImage(new Uri(iconPath)),
                Stretch = Stretch.UniformToFill,
            };
        }
        else
        {
            toggle.Content = new TextBlock
            {
                Text = tooltip.Length > 3 ? tooltip[..3] : tooltip,
                Foreground = Brushes.White, FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        wrap.Children.Add(toggle);
        return toggle;
    }

    private Slider AddSlider(string label, double value, double min, double max, double tick)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var txtValue = new TextBlock
        {
            Text = $"{(int)value}",
            Foreground = (Brush)FindResource("Gold"),
            FontSize = 10, FontWeight = FontWeights.Bold,
        };

        var header = new DockPanel();
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("TextDim"),
            FontSize = 10,
        });
        txtValue.SetValue(DockPanel.DockProperty, Dock.Right);
        header.Children.Add(txtValue);
        panel.Children.Add(header);

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value,
            IsSnapToTickEnabled = true, TickFrequency = tick,
            Width = 190,
        };
        slider.ValueChanged += (s, e) => { txtValue.Text = $"{(int)e.NewValue}"; SaveSettings(); };
        panel.Children.Add(slider);

        SubContent.Children.Add(panel);
        return slider;
    }

    private CheckBox AddCheckBox(string label, bool isChecked)
    {
        var chk = new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
            Foreground = (Brush)FindResource("TextLight"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 3),
        };
        chk.Checked += (s, e) => SaveSettings();
        chk.Unchecked += (s, e) => SaveSettings();
        SubContent.Children.Add(chk);
        return chk;
    }

    private void AddLabel(string text)
    {
        SubContent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("TextDim"),
            FontSize = 10, Margin = new Thickness(0, 0, 0, 2),
        });
    }

    // --- Toggle clicks ---
    private void BtnRotation_Click(object sender, RoutedEventArgs e) => OnRotationToggle?.Invoke();
    private void BtnFollow_Click(object sender, RoutedEventArgs e) => OnFollowToggle?.Invoke();
    private void BtnAoe_Click(object sender, RoutedEventArgs e)
    {
        BtnAoe.Content = BtnAoe.IsChecked == true ? "ON" : "OFF";
        SaveSettings();
    }
    private void BtnBuffs_Click(object sender, RoutedEventArgs e)
    {
        BtnBuffs.Content = BtnBuffs.IsChecked == true ? "ON" : "OFF";
        SaveSettings();
    }

    // --- Buff setup ---
    public void SetPlayerClass(string playerClass)
    {
        _playerClass = playerClass;
        _spellToggles.Clear();
        _buffToggles.Clear();
        if (!ClassBuffs.TryGetValue(playerClass, out var buffs)) return;

        // Pre-create toggles with metadata (will be rebuilt in submenu)
        foreach (var (spell, icon, label, defaultOn) in buffs)
        {
            var toggle = new ToggleButton
            {
                IsChecked = GetSavedBool($"buff_{spell}", defaultOn),
                ToolTip = label,
                Tag = icon,
            };
            _buffToggles[spell] = toggle;
        }
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

    public void UpdateInfo(string text) => TxtInfo.Text = text;
    public void UpdateStatus(string text) => TxtSpec.Text = text;

    // --- Settings persistence ---

    private bool GetSavedBool(string key, bool defaultVal) =>
        _saved.TryGetValue(key, out var v) ? v.GetBoolean() : defaultVal;

    private double GetSavedDouble(string key, double defaultVal) =>
        _saved.TryGetValue(key, out var v) ? v.GetDouble() : defaultVal;

    private string GetSavedString(string key, string defaultVal) =>
        _saved.TryGetValue(key, out var v) ? v.GetString() ?? defaultVal : defaultVal;

    public void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            _saved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();

            // Window position
            if (_saved.ContainsKey("pos_x") && _saved.ContainsKey("pos_y"))
            {
                Left = GetSavedDouble("pos_x", Left);
                Top = GetSavedDouble("pos_y", Top);
            }

            // Curse/Seal selection
            _selectedCurse = GetSavedString("curse", "CoA");
            _selectedSeal = GetSavedString("seal", "SoV");

            // Main toggles (AoE, Buffs)
            BtnAoe.IsChecked = GetSavedBool("aoe", false);
            BtnAoe.Content = BtnAoe.IsChecked == true ? "ON" : "OFF";
            BtnBuffs.IsChecked = GetSavedBool("buffs", false);
            BtnBuffs.Content = BtnBuffs.IsChecked == true ? "ON" : "OFF";
        }
        catch { /* corrupted file — ignore, use defaults */ }
    }

    public void SaveSettings()
    {
        try
        {
            var data = new Dictionary<string, object>();

            // Window position
            data["pos_x"] = Left;
            data["pos_y"] = Top;

            // Curse
            data["curse"] = _selectedCurse;
            data["seal"] = _selectedSeal;

            // Main toggles
            data["aoe"] = BtnAoe.IsChecked == true;
            data["buffs"] = BtnBuffs.IsChecked == true;

            // Spell toggles
            foreach (var (key, btn) in _spellToggles)
                data[$"spell_{key}"] = btn.IsChecked == true;

            // Buff toggles
            foreach (var (spell, btn) in _buffToggles)
                data[$"buff_{spell}"] = btn.IsChecked == true;

            // Sliders
            if (_sliderDispMana != null) data["slider_dispMana"] = _sliderDispMana.Value;
            if (_sliderSFMana != null) data["slider_sfMana"] = _sliderSFMana.Value;
            if (_sliderMaxDots != null) data["slider_maxDots"] = _sliderMaxDots.Value;
            if (_sliderMindSear != null) data["slider_mindSear"] = _sliderMindSear.Value;
            if (_sliderDist != null) data["slider_dist"] = _sliderDist.Value;
            if (_sliderMaxRange != null) data["slider_maxRange"] = _sliderMaxRange.Value;

            // Checkboxes
            if (_chkAutoFace != null) data["chk_autoFace"] = _chkAutoFace.IsChecked == true;
            if (_chkAutoTarget != null) data["chk_autoTarget"] = _chkAutoTarget.IsChecked == true;
            if (_chkMultiDot != null) data["chk_multiDot"] = _chkMultiDot.IsChecked == true;
            if (_chkMindSear != null) data["chk_mindSear"] = _chkMindSear.IsChecked == true;

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore write errors */ }
    }
}
