using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WowBot.Core.Game;
using WowBot.Core.Game.Entities;
using WowBot.Core.Game.Rotations;
using WowBot.Core.Memory;

namespace WowBot.Injector;

public partial class MainWindow : Window
{
    private readonly MemoryReader _memory = new();
    private ObjectManager? _objectManager;
    private EndSceneHook? _endSceneHook;
    private RotationEngine? _rotationEngine;
    private DispatcherTimer? _updateTimer;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _rotationEngine?.Dispose();
        _endSceneHook?.Dispose();
        _memory.Dispose();
    }

    private void BtnAttach_Click(object sender, RoutedEventArgs e)
    {
        var wowProcesses = Process.GetProcessesByName("Wow");
        if (wowProcesses.Length == 0)
        {
            TxtStatus.Text = "WoW process not found! Launch WoW first.";
            return;
        }

        var wow = wowProcesses[0];

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

        // Сначала диагностика EndScene
        _endSceneHook = new EndSceneHook(_memory);
        string diag = _endSceneHook.GetDiagnostics();

        // Сохраняем диагностику в файл
        var diagPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "endscene_diag.txt");
        System.IO.File.WriteAllText(diagPath, diag);

        // Показываем в списке объектов
        LstObjects.ItemsSource = diag.Split('\n').ToList();

        // Пробуем установить хук
        try
        {
            _endSceneHook.Install();
            TxtStatus.Text = $"Attached + Hooked (PID: {wow.Id})";
            TxtLuaStatus.Text = "Hook active. Enter Lua and press Run or Enter.";
            BtnExecuteLua.IsEnabled = true;

            // Инициализируем навигацию и ротацию
            var navigation = new Navigation(_memory, _endSceneHook);
            _rotationEngine = new RotationEngine(_endSceneHook, _objectManager, navigation);
            _rotationEngine.LoadRotation(BalanceDruidPvE.GetScript());
            _rotationEngine.OnStatusChanged += status =>
                Dispatcher.Invoke(() => TxtRotationStatus.Text = status);
            BtnRotationToggle.IsEnabled = true;
            BtnSetFollow.IsEnabled = true;
            BtnClearFollow.IsEnabled = true;
            BtnScanSpells.IsEnabled = true;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Attached (PID: {wow.Id}) — hook failed: {ex.Message}";
            TxtLuaStatus.Text = $"Hook error: {ex.Message}";
            // Диагностика сохранена, можно продолжить без хука
        }

        StartUpdateLoop();
    }

    private void BtnDetach_Click(object sender, RoutedEventArgs e)
    {
        StopUpdateLoop();

        // Останавливаем ротацию и снимаем хук
        _rotationEngine?.Dispose();
        _rotationEngine = null;
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
        if (_rotationEngine == null) return;
        _rotationEngine.Toggle();
        BtnRotationToggle.Content = _rotationEngine.IsRunning ? "Stop Rotation" : "Start Rotation";
    }

    private void BtnSetFollow_Click(object sender, RoutedEventArgs e)
    {
        if (_rotationEngine == null) return;
        _rotationEngine.SetFollowTarget();
    }

    private void BtnClearFollow_Click(object sender, RoutedEventArgs e)
    {
        if (_rotationEngine == null) return;
        _rotationEngine.ClearFollowTarget();
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
        if (_objectManager == null || !_memory.IsAttached) return;

        try
        {
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

        foreach (var u in _objectManager.Units
            .Where(u => u.IsAlive && player != null && player.DistanceTo(u) < 100)
            .OrderBy(u => player != null ? player.DistanceTo(u) : 0)
            .Take(30))
        {
            float dist = player != null ? player.DistanceTo(u) : 0;
            items.Add($"[Unit] Lvl {u.Level} HP:{u.HealthPercent:F0}% Dist:{dist:F0}yd");
        }

        LstObjects.ItemsSource = items;
    }

    private void BtnScanSpells_Click(object sender, RoutedEventArgs e)
    {
        if (_endSceneHook == null || !_endSceneHook.IsHooked) return;

        TxtStatus.Text = "Scanning druid spells...";

        // Все важные спеллы Balance Druid + общие друид спеллы (по ID)
        string lua = @"
local spells = {
    {48461, 'Wrath max'},
    {48465, 'Starfire max'},
    {48463, 'Moonfire max'},
    {48468, 'Insect Swarm max'},
    {53201, 'Starfall max'},
    {33831, 'Force of Nature'},
    {770,   'Faerie Fire'},
    {24858, 'Moonkin Form'},
    {29166, 'Innervate'},
    {22812, 'Barkskin'},
    {61384, 'Typhoon'},
    {53307, 'Thorns max'},
    {48470, 'Gift of the Wild max'},
    {48441, 'Rejuvenation max'},
    {48443, 'Regrowth max'},
    {48378, 'Healing Touch max'},
    {29166, 'Innervate'},
    {18562, 'Swiftmend'},
    {48451, 'Lifebloom max'},
    {53251, 'Wild Growth max'},
    {48477, 'Rebirth max'},
    {20484, 'Rebirth r1'},
    {16857, 'Faerie Fire (Feral)'},
    {49800, 'Rip max'},
    {48574, 'Rake max'},
    {769,   'Swipe'},
    {33917, 'Mangle'},
    {50256, 'Maul max'},
    {2782,  'Remove Curse'},
    {8946,  'Cure Poison'},
    {33786, 'Cyclone'},
    {2637,  'Hibernate'},
    {16689, 'Natures Grasp'},
    {53312, 'Natures Grasp max'},
    {16979, 'Feral Charge'},
    {49376, 'Feral Charge Cat'},
    {5229,  'Enrage'},
    {22842, 'Frenzied Regeneration'},
    {61336, 'Survival Instincts'},
    {5215,  'Prowl'},
    {48518, 'Eclipse (Lunar) buff'},
    {48517, 'Eclipse (Solar) buff'},
}
DEFAULT_CHAT_FRAME:AddMessage('|cff00ff88=== [WB] Spell Scan ===|r')
for _, s in ipairs(spells) do
    local id, eng = s[1], s[2]
    local name = GetSpellInfo(id)
    if name then
        DEFAULT_CHAT_FRAME:AddMessage('|cff00ff88[WB]|r ' .. id .. ' = |cffFFFF00' .. name .. '|r  (' .. eng .. ')')
    end
end
DEFAULT_CHAT_FRAME:AddMessage('|cff00ff88=== [WB] Scan Complete ===|r')
";

        _endSceneHook.ExecuteLua(lua, 3000);
        TxtStatus.Text = "Scan done! Check WoW chat.";
    }

    private void BtnDump_Click(object sender, RoutedEventArgs e)
    {
        if (_objectManager?.LocalPlayer == null || !_memory.IsAttached) return;

        var player = _objectManager.LocalPlayer;
        uint descBase = _memory.ReadUInt32(player.BaseAddress + 0x08);

        var lines = new List<string>();
        lines.Add($"=== DESCRIPTOR DUMP for {_objectManager.GetPlayerName()} ===");
        lines.Add($"BaseAddress: 0x{player.BaseAddress:X8}");
        lines.Add($"DescriptorBase: 0x{descBase:X8}");
        lines.Add("");

        for (int i = 0; i < 256; i++)
        {
            int val = _memory.ReadInt32(descBase + (uint)(i * 4));
            if (val != 0 || i < 10)
                lines.Add($"[0x{i:X3}] idx={i,-4} val={val,-12} (0x{(uint)val:X8})");
        }

        var dumpPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "descriptor_dump.txt");
        System.IO.File.WriteAllLines(dumpPath, lines);

        LstObjects.ItemsSource = lines;
        TxtStatus.Text = $"Dump saved to {dumpPath}";
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
