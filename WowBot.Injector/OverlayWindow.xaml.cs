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
    private string _playerSpec = "";

    private static readonly Dictionary<string, string> SpecDisplayNames = new()
    {
        ["Arms Warrior"] = "Армс Воин",
        ["Fury Warrior"] = "Фури Воин",
        ["Prot Warrior"] = "Прот Воин",
        ["Holy Paladin"] = "Холи Паладин",
        ["Prot Paladin"] = "Прот Паладин",
        ["Ret Paladin"] = "Мущщинский класс",
        ["BM Hunter"] = "БМ Охотник",
        ["MM Hunter"] = "ММ Охотник",
        ["Survival Hunter"] = "Сурв Охотник",
        ["Assassination Rogue"] = "Мутилейт Разбойник",
        ["Combat Rogue"] = "Комбат Разбойник",
        ["Subtlety Rogue"] = "Сабтл Разбойник",
        ["Disc Priest"] = "Диск Прист",
        ["Holy Priest"] = "Холи Прист",
        ["Shadow Priest"] = "Шадоу Прист",
        ["Blood DK"] = "Блад ДК",
        ["Frost DK"] = "Фрост ДК",
        ["Unholy DK"] = "Анхоли ДК",
        ["Elemental Shaman"] = "Элем Шаман",
        ["Enhancement Shaman"] = "Энханс Шаман",
        ["Resto Shaman"] = "Ресто Шаман",
        ["Arcane Mage"] = "Аркан Маг",
        ["Fire Mage"] = "Фаер Маг",
        ["Frost Mage"] = "Фрост Маг",
        ["Affliction Lock"] = "Аффли Варлок",
        ["Demonology Lock"] = "Демо Варлок",
        ["Destruction Lock"] = "Дестро Варлок",
        ["Balance Druid"] = "Баланс Друид",
        ["Feral Druid"] = "Ферал Друид",
        ["Resto Druid"] = "Ресто Друид",
    };

    // AoE toggles
    private ToggleButton? _aoeSocToggle;
    private ToggleButton _chkMultiDot = null!, _chkMindSear = null!;
    private Slider _sliderMaxDots = null!, _sliderMindSear = null!;

    // Curse selection (radio-style, only one at a time)
    private string _selectedCurse = ""; // "CoA", "CoD", "CoE", or "" (none)
    private readonly Dictionary<string, ToggleButton> _curseToggles = new();
    private static readonly (string key, string icon, string tooltip)[] CurseOptions =
    {
        ("CoA", "curse_agony.jpg", "Проклятие агонии"),
        ("CoD", "curse_doom.jpg", "Проклятие рока"),
        ("CoE", "curse_elements.jpg", "Проклятие стихий"),
    };

    // Totem selection (radio-style, 4 elements) — SHAMAN only
    private string _selectedTotemEarth = "";
    private string _selectedTotemFire = "";
    private string _selectedTotemWater = "";
    private string _selectedTotemAir = "";
    private readonly Dictionary<string, ToggleButton> _totemEarthToggles = new();
    private readonly Dictionary<string, ToggleButton> _totemFireToggles = new();
    private readonly Dictionary<string, ToggleButton> _totemWaterToggles = new();
    private readonly Dictionary<string, ToggleButton> _totemAirToggles = new();
    private static readonly (string key, string icon, string tooltip)[] TotemEarthOptions =
    {
        ("Stoneskin", "stoneskin_totem.jpg", "Тотем каменной кожи"),
        ("SoE", "strength_of_earth.jpg", "Тотем силы земли"),
        ("Tremor", "tremor_totem.jpg", "Тотем трепета"),
    };
    private static readonly (string key, string icon, string tooltip)[] TotemFireOptions =
    {
        ("Flametongue", "flametongue_totem.jpg", "Тотем языка пламени"),
        ("FrostRes", "frost_resistance.jpg", "Тотем защиты от магии льда"),
    };
    private static readonly (string key, string icon, string tooltip)[] TotemWaterOptions =
    {
        ("ManaSpring", "mana_spring.jpg", "Тотем источника маны"),
        ("HealStream", "healing_stream.jpg", "Тотем исцеляющего потока"),
        ("Cleansing", "cleansing_totem.jpg", "Тотем очищения"),
        ("FireRes", "fire_resistance.jpg", "Тотем защиты от огня"),
    };
    private static readonly (string key, string icon, string tooltip)[] TotemAirOptions =
    {
        ("WrathOfAir", "wrath_of_air.jpg", "Тотем гнева воздуха"),
        ("Windfury", "windfury_totem.jpg", "Тотем неистовства ветра"),
        ("NatureRes", "nature_resistance.jpg", "Тотем защиты от сил природы"),
    };

    // Seal selection (radio-style, only one at a time)
    private string _selectedSeal = ""; // Только для PALADIN
    private readonly Dictionary<string, ToggleButton> _sealToggles = new();
    private static readonly (string key, string icon, string tooltip)[] SealOptionsRet =
    {
        ("SoV", "seal_vengeance.jpg", "Печать мщения"),
        ("SoC", "seal_command.jpg", "Печать повиновения"),
    };
    private static readonly (string key, string icon, string tooltip)[] SealOptionsHoly =
    {
        ("SoW", "seal_wisdom.jpg", "Печать мудрости"),
        ("SoL", "seal_light.jpg", "Печать Света"),
    };
    private (string key, string icon, string tooltip)[] SealOptions =>
        _playerSpec == "Holy Paladin" ? SealOptionsHoly : SealOptionsRet;

    // Judgement selection for all Paladins (radio-style)
    private string _selectedJudgement = ""; // Для всех паладинов
    private readonly Dictionary<string, ToggleButton> _judgementToggles = new();
    private static readonly (string key, string icon, string tooltip)[] JudgementOptions =
    {
        ("JoW", "judgement_wisdom.jpg", "Правосудие мудрости"),
        ("JoL", "judgement_light.jpg", "Правосудие света"),
    };

    // Blessing selection (radio-style, only one at a time)
    private string _selectedBlessing = ""; // Только для PALADIN
    private readonly Dictionary<string, ToggleButton> _blessingToggles = new();
    private static readonly (string key, string icon, string tooltip)[] BlessingOptions =
    {
        ("BoM", "blessing_might.jpg", "Благословение могущества"),
        ("BoK", "blessing_kings.jpg", "Благословение королей"),
        ("BoW", "blessing_wisdom.jpg", "Благословение мудрости"),
        ("BoS", "blessing_sanctuary.jpg", "Благословение неприкосновенности"),
    };

    // Aura selection for Paladin (radio-style)
    private string _selectedAura = ""; // Только для PALADIN
    private readonly Dictionary<string, ToggleButton> _auraToggles = new();
    private static readonly (string key, string icon, string tooltip)[] AuraOptions =
    {
        ("AuRet", "aura_retribution.jpg", "Аура воздаяния"),
        ("AuDev", "aura_devotion.jpg", "Аура благочестия"),
        ("AuCru", "aura_crusader.jpg", "Аура воина Света"),
        ("AuFrost", "aura_frost.jpg", "Аура защиты от магии льда"),
        ("AuFire", "aura_fire.jpg", "Аура защиты от огня"),
        ("AuShadow", "aura_shadow.jpg", "Аура защиты от темной магии"),
        ("AuConc", "aura_concentration.jpg", "Аура сосредоточенности"),
    };

    // Shout selection for Warrior (radio-style)
    private string _selectedShout = ""; // Только для WARRIOR
    private readonly Dictionary<string, ToggleButton> _shoutToggles = new();
    private static readonly (string key, string icon, string tooltip)[] ShoutOptions =
    {
        ("Battle", "battle_shout.jpg", "Боевой крик"),
        ("Commanding", "commanding_shout.jpg", "Командирский крик"),
    };

    // Stance selection for Warrior (radio-style)
    private string _selectedStance = ""; // Только для WARRIOR
    private readonly Dictionary<string, ToggleButton> _stanceToggles = new();
    private static readonly (string key, string icon, string tooltip)[] StanceOptions =
    {
        ("Battle", "battle_stance.jpg", "Боевая стойка"),
        ("Defensive", "defensive_stance.jpg", "Оборонительная стойка"),
        ("Berserker", "berserker_stance.jpg", "Стойка берсерка"),
    };

    // Presence selection for DK (radio-style)
    private string _selectedPresence = ""; // Только для DEATHKNIGHT
    private readonly Dictionary<string, ToggleButton> _presenceToggles = new();
    private static readonly (string key, string icon, string tooltip)[] PresenceOptions =
    {
        ("Blood", "blood_presence.jpg", "Власть крови"),
        ("Frost", "frost_presence.jpg", "Власть льда"),
        ("Unholy", "unholy_presence.jpg", "Власть нечестивости"),
    };

    // Feral form selection (radio-style)
    private string _selectedFeralForm = ""; // Только для DRUID feral
    private readonly Dictionary<string, ToggleButton> _feralFormToggles = new();
    private static readonly (string key, string icon, string tooltip)[] FeralFormOptions =
    {
        ("Cat", "cat_form.jpg", "Облик кошки"),
        ("Bear", "bear_form.jpg", "Облик лютого медведя"),
    };

    // Mana sliders
    private Slider _sliderDispMana = null!, _sliderSFMana = null!;

    // Follow slider
    private Slider _sliderDist = null!;

    // Target checkboxes
    private CheckBox _chkAutoFace = null!, _chkAutoTarget = null!;
    private Slider _sliderMaxRange = null!;
    private Slider? _sliderDefHP;
    private CheckBox? _chkDefAll;

    /// <summary>Проверяет включён ли спелл по ключу</summary>
    public bool IsSpellEnabled(string key) =>
        _spellToggles.TryGetValue(key, out var btn) ? btn.IsChecked == true : true;

    /// <summary>Lua-строка с флагами спеллов: WB_S={VT=true,DP=false,...}</summary>
    public string GetSpellFlagsLua()
    {
        // Snapshot коллекции чтобы избежать "Collection was modified" при конкурентном доступе
        var toggles = _spellToggles.ToArray();
        if (toggles.Length == 0 && _curseToggles.Count == 0) return "WB_S={} ";
        var parts = new List<string>();
        foreach (var (key, btn) in toggles)
            parts.Add($"{key}={(btn.IsChecked == true ? "true" : "false")}");
        // Curse flags: только для варлока
        if (_playerClass == "WARLOCK")
            foreach (var (key, _, _) in CurseOptions)
                parts.Add($"{key}={(_selectedCurse == key ? "true" : "false")}");
        // Seal/Judgement/Blessing flags: только для паладина
        if (_playerClass == "PALADIN")
        {
            foreach (var (key, _, _) in SealOptions)
                parts.Add($"{key}={(_selectedSeal == key ? "true" : "false")}");
            foreach (var (key, _, _) in JudgementOptions)
                parts.Add($"{key}={(_selectedJudgement == key ? "true" : "false")}");
            foreach (var (key, _, _) in BlessingOptions)
                parts.Add($"{key}={(_selectedBlessing == key ? "true" : "false")}");
        }
        // DefHP slider + DefAll для прот воина
        if (_playerClass == "WARRIOR" && _sliderDefHP != null)
            parts.Add($"DefHP={(int)_sliderDefHP.Value}");
        if (_playerClass == "WARRIOR" && _chkDefAll != null)
            parts.Add($"DefAll={(_chkDefAll.IsChecked == true ? "true" : "false")}");
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
    public bool AoeSealSwap => _aoeSocToggle?.IsChecked == true;
    public string SelectedSeal => _selectedSeal;
    public string SelectedBlessing => _selectedBlessing;
    public void SetSelectedBlessing(string key) { _selectedBlessing = key; SaveSettings(); }
    public void SetSelectedAura(string key) { _selectedAura = key; SaveSettings(); }
    public string SelectedAura => _selectedAura;
    public string SelectedShout => _selectedShout;
    public string SelectedStance => _selectedStance;
    public string SelectedPresence => _selectedPresence;
    public string SelectedFeralForm => _selectedFeralForm;
    public string SelectedTotemEarth => _selectedTotemEarth;
    public string SelectedTotemFire => _selectedTotemFire;
    public string SelectedTotemWater => _selectedTotemWater;
    public string SelectedTotemAir => _selectedTotemAir;
    private bool _autoFaceDefault = true;
    public bool AutoFace => _chkAutoFace != null ? (_chkAutoFace.IsChecked == true) : _autoFaceDefault;
    public bool AutoSelectTarget => _chkAutoTarget?.IsChecked == true;
    public int MaxTargetRange => (int)(_sliderMaxRange?.Value ?? 30);
    public float GetFollowDistance() => (float)(_sliderDist?.Value ?? GetSavedDouble("slider_dist", 8));
    public float GetMaxTargetRange() => (float)(_sliderMaxRange?.Value ?? GetSavedDouble("slider_maxRange", 30));

    // Спеллы по спекам: (key, icon, tooltip, defaultOn)
    private static readonly Dictionary<string, (string key, string icon, string tooltip, bool on)[]> SpecSpells = new()
    {
        // ==================== WARRIOR ====================
        ["Arms Warrior"] = new[]
        {
            ("Reck", "recklessness.jpg", "Безрассудство", true),
            ("MS", "mortal_strike.jpg", "Смертельный удар", true),
            ("Rend", "rend.jpg", "Кровопускание", true),
            ("OP", "overpower.jpg", "Превосходство", true),
            ("Execute", "execute.jpg", "Казнь", true),
            ("Slam", "slam.jpg", "Мощный удар", true),
            ("Cleave", "cleave.jpg", "Рассекающий удар", true),
        },
        ["Fury Warrior"] = new[]
        {
            ("Reck", "recklessness.jpg", "Безрассудство", true),
            ("BT", "bloodthirst.jpg", "Кровожадность", true),
            ("WW", "whirlwind.jpg", "Вихрь", true),
            ("Execute", "execute.jpg", "Казнь", true),
            ("Slam", "slam.jpg", "Мощный удар", true),
        },
        ["Prot Warrior"] = new[]
        {
            ("HS", "heroic_strike.jpg", "Удар героя", true),
            ("BR", "berserker_rage.jpg", "Кровавая ярость", false),
            ("SW", "shield_wall.jpg", "Глухая оборона", true),
            ("LS", "last_stand.jpg", "Ни шагу назад", true),
            ("SB", "shield_block.jpg", "Блок щитом", true),
            ("ShieldSlam", "shield_slam.jpg", "Мощный удар щитом", true),
            ("Revenge", "revenge.jpg", "Реванш", true),
            ("TC", "thunder_clap.jpg", "Удар грома", true),
            ("ShockW", "shockwave.jpg", "Ударная волна", true),
            ("Devastate", "devastate.jpg", "Сокрушение", true),
        },
        // ==================== PALADIN ====================
        ["Ret Paladin"] = new[]
        {
            ("AW", "avenging_wrath.jpg", "Гнев карателя", true),
            ("Judge", "judgement.jpg", "Правосудие", true),
            ("CS", "crusader_strike.jpg", "Удар воина Света", true),
            ("DS", "divine_storm.jpg", "Божественная буря", true),
            ("Cons", "consecration.jpg", "Освящение", true),
            ("Exo", "exorcism.jpg", "Экзорцизм", true),
            ("HoW", "hammer_wrath.jpg", "Молот гнева", true),
            ("SS", "sacred_shield.jpg", "Священный щит", true),
        },
        ["Prot Paladin"] = new[]
        {
            ("Plea", "divine_plea.jpg", "Святая клятва", true),
            ("AW", "avenging_wrath.jpg", "Гнев карателя", true),
            ("HoR", "hammer_righteous.jpg", "Молот праведника", true),
            ("ShoR", "shield_righteousness.jpg", "Щит праведности", true),
            ("HolyShield", "holy_shield.jpg", "Щит небес", true),
            ("Judge", "judgement.jpg", "Правосудие", true),
            ("Cons", "consecration.jpg", "Освящение", true),
            ("HW", "holy_wrath.jpg", "Гнев небес", true),
            ("AS", "avengers_shield.jpg", "Щит мстителя", true),
            ("SS", "sacred_shield.jpg", "Священный щит", false),
        },
        ["Holy Paladin"] = new[]
        {
            ("HL", "holy_light.jpg", "Свет небес", true),
            ("FL", "flash_light.jpg", "Вспышка Света", false),
            ("HS", "holy_shock.jpg", "Шок небес", true),
            ("Beacon", "beacon.jpg", "Частица Света", true),
            ("SS", "sacred_shield.jpg", "Священный щит", true),
            ("Plea", "divine_plea.jpg", "Святая клятва", true),
            ("DF", "divine_favor.jpg", "Божественное одобрение", true),
            ("LoH", "lay_on_hands.jpg", "Возложение рук", false),
        },
        // ==================== HUNTER ====================
        ["BM Hunter"] = new[]
        {
            ("Pet", "kill_command.jpg", "Питомец (призыв/атака)", true),
            ("Track", "hunters_mark.jpg", "Выслеживание (авто по цели)", true),
            ("Mark", "hunters_mark.jpg", "Метка охотника", true),
            ("Kill", "kill_shot.jpg", "Убийственный выстрел", true),
            ("BW", "beast_within.jpg", "Повелитель зверей", true),
            ("Bestial", "bestial_wrath.jpg", "Звериный гнев", true),
            ("Kill2", "kill_command.jpg", "Команда Взять", true),
            ("Serpent", "serpent_sting.jpg", "Укус змеи", true),
            ("Aimed", "aimed_shot.jpg", "Прицельный выстрел", true),
            ("Arcane", "arcane_shot.jpg", "Чародейский выстрел", true),
            ("Steady", "steady_shot.jpg", "Верный выстрел", true),
        },
        ["MM Hunter"] = new[]
        {
            ("Pet", "kill_command.jpg", "Питомец (призыв/атака)", true),
            ("Track", "hunters_mark.jpg", "Выслеживание (авто по цели)", true),
            ("Dragonhawk", "aimed_shot.jpg", "Дух дракондора (в бою)", true),
            ("Mark", "hunters_mark.jpg", "Метка охотника", true),
            ("Kill", "kill_shot.jpg", "Убийственный выстрел", true),
            ("Rapid", "rapid_fire.jpg", "Быстрая стрельба", true),
            ("Kill2", "kill_command.jpg", "Команда Взять", true),
            ("CotW", "call_wild.jpg", "Зов дикой природы", true),
            ("Chimera", "chimera_shot.jpg", "Выстрел химеры", true),
            ("Serpent", "serpent_sting.jpg", "Укус змеи", true),
            ("Volley", "aimed_shot.jpg", "Залп (AoE, если >1 цели)", true),
            ("Aimed", "aimed_shot.jpg", "Прицельный выстрел", true),
            ("Trap", "explosive_shot.jpg", "Взрывная ловушка (по КД)", true),
            ("Readiness", "rapid_fire.jpg", "Готовность (сброс КД)", true),
            ("Silence", "silencing_shot.jpg", "Глушащий выстрел", true),
            ("Steady", "steady_shot.jpg", "Верный выстрел", true),
        },
        ["Survival Hunter"] = new[]
        {
            ("Pet", "kill_command.jpg", "Питомец (призыв/атака)", true),
            ("Track", "hunters_mark.jpg", "Выслеживание (авто по цели)", true),
            ("Mark", "hunters_mark.jpg", "Метка охотника", true),
            ("Kill", "kill_shot.jpg", "Убийственный выстрел", true),
            ("Explosive", "explosive_shot.jpg", "Разрывной выстрел", true),
            ("Black", "black_arrow.jpg", "Черная стрела", true),
            ("Serpent", "serpent_sting.jpg", "Укус змеи", true),
            ("Aimed", "aimed_shot.jpg", "Прицельный выстрел", true),
            ("Arcane", "arcane_shot.jpg", "Чародейский выстрел", true),
            ("Steady", "steady_shot.jpg", "Верный выстрел", true),
        },
        // ==================== ROGUE ====================
        ["Assassination Rogue"] = new[]
        {
            ("HFB", "hunger_blood.jpg", "Жажда убийства", true),
            ("Envenom", "envenom.jpg", "Расправа", true),
            ("Rupture", "rupture.jpg", "Рваная рана", true),
            ("Mutilate", "mutilate.jpg", "Увечье", true),
        },
        ["Combat Rogue"] = new[]
        {
            ("KS", "killing_spree.jpg", "Череда убийств", true),
            ("SnD", "slice_dice.jpg", "Потрошение", true),
            ("Rupture", "rupture.jpg", "Рваная рана", true),
            ("Evis", "eviscerate.jpg", "Потрошение", true),
            ("SS", "sinister_strike.jpg", "Коварный удар", true),
        },
        ["Subtlety Rogue"] = new[]
        {
            ("Hemo", "hemorrhage.jpg", "Кровоизлияние", true),
            ("Rupture", "rupture.jpg", "Рваная рана", true),
            ("Evis", "eviscerate.jpg", "Потрошение", true),
            ("BS", "backstab.jpg", "Удар в спину", true),
        },
        // ==================== PRIEST ====================
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
        ["Disc Priest"] = new[]
        {
            ("PW", "pw_shield.jpg", "Слово силы: Щит", true),
            ("Penance", "penance.jpg", "Исповедь", true),
            ("PS", "pain_suppression.jpg", "Подавление боли", true),
            ("Flash", "flash_heal.jpg", "Быстрое исцеление", true),
            ("PoM", "prayer_mending.jpg", "Молитва восстановления", true),
            ("Renew", "renew.jpg", "Обновление", true),
        },
        ["Holy Priest"] = new[]
        {
            ("CoH", "circle_healing.jpg", "Круг исцеления", true),
            ("Guardian", "guardian_spirit.jpg", "Оберегающий дух", true),
            ("PoM", "prayer_mending.jpg", "Молитва восстановления", true),
            ("Renew", "renew.jpg", "Обновление", true),
            ("Flash", "flash_heal.jpg", "Быстрое исцеление", true),
            ("GHeal", "greater_heal.jpg", "Великое исцеление", true),
            ("Binding", "binding_heal.jpg", "Связующее исцеление", true),
        },
        // ==================== DEATH KNIGHT ====================
        ["Blood DK"] = new[]
        {
            ("IT", "icy_touch.jpg", "Ледяное прикосновение", true),
            ("PS", "plague_strike.jpg", "Удар чумы", true),
            ("Pest", "pestilence.jpg", "Мор", true),
            ("DS", "death_strike.jpg", "Удар смерти", true),
            ("HS", "heart_strike.jpg", "Удар в сердце", true),
            ("BS", "blood_strike.jpg", "Кровавый удар", true),
            ("RS", "rune_strike.jpg", "Рунический удар", true),
            ("VB", "vampiric_blood.jpg", "Кровь вампира", true),
        },
        ["Frost DK"] = new[]
        {
            ("IT", "icy_touch.jpg", "Ледяное прикосновение", true),
            ("PS", "plague_strike.jpg", "Удар чумы", true),
            ("Pest", "pestilence.jpg", "Мор (обновление)", true),
            ("UA", "unbreakable_armor.jpg", "Несокрушимая броня", true),
            ("HB", "howling_blast.jpg", "Воющий ветер (Rime прок)", true),
            ("Oblit", "obliterate.jpg", "Уничтожение", true),
            ("BS", "blood_strike.jpg", "Кровавый удар", true),
            ("FS", "frost_strike.jpg", "Ледяной удар", true),
            ("BT", "blood_tap.jpg", "Кровоотвод", true),
            ("ERW", "empower_rune.jpg", "Усиление рунического оружия", true),
            ("HoW", "horn_winter.jpg", "Зимний горн", true),
        },
        ["Unholy DK"] = new[]
        {
            ("IT", "icy_touch.jpg", "Ледяное прикосновение", true),
            ("PS", "plague_strike.jpg", "Удар чумы", true),
            ("Pest", "pestilence.jpg", "Мор", true),
            ("Gargoyle", "gargoyle.jpg", "Призыв горгульи", true),
            ("UB", "unholy_blight.jpg", "Нечестивая порча", true),
            ("DnD", "death_decay.jpg", "Смерть и разложение", true),
            ("SS", "scourge_strike.jpg", "Удар Плети", true),
            ("BS", "blood_strike.jpg", "Кровавый удар", true),
            ("DC", "death_coil.jpg", "Лик смерти", true),
        },
        // ==================== SHAMAN ====================
        ["Elemental Shaman"] = new[]
        {
            ("CallSpirits", "chain_lightning.jpg", "Зов Духов (тотемы в бою)", true),
            ("FS", "flame_shock.jpg", "Огненный шок", true),
            ("LvB", "lava_burst.jpg", "Вскипание лавы", true),
            ("TnL", "thunderstorm.jpg", "Гром и молния", true),
            ("CL", "chain_lightning.jpg", "Цепная молния", true),
            ("LB", "lightning_bolt.jpg", "Молния", true),
        },
        ["Enhancement Shaman"] = new[]
        {
            ("CallSpirits", "chain_lightning.jpg", "Зов Духов (тотемы в бою)", true),
            ("Searing", "flametongue_totem.jpg", "Тотем опаляющего пламени (авто)", true),
            ("LS", "lightning_shield.jpg", "Щит молний", true),
            ("Wolves", "feral_spirit.jpg", "Дух дикого волка", true),
            ("SR", "shamanistic_rage.jpg", "Ярость шамана", true),
            ("SS", "stormstrike.jpg", "Удар бури", true),
            ("FS", "flame_shock.jpg", "Огненный шок", true),
            ("ES", "earth_shock.jpg", "Земной шок", true),
            ("LvB", "lava_burst.jpg", "Вскипание лавы", true),
            ("LB_MW", "lightning_bolt.jpg", "Молния (Водоворот)", true),
        },
        ["Resto Shaman"] = new[]
        {
            ("CallSpirits", "chain_lightning.jpg", "Зов Духов (тотемы в бою)", true),
            ("RT", "riptide.jpg", "Быстрина", true),
            ("NS", "natures_swift.jpg", "Природная стремительность", true),
            ("CH", "chain_heal.jpg", "Цепное исцеление", true),
            ("LHW", "lesser_hw.jpg", "Малая волна исцеления", true),
            ("HW", "healing_wave.jpg", "Волна исцеления", true),
            ("ES", "earth_shield.jpg", "Щит земли", true),
        },
        // ==================== DRUID ====================
        ["Balance Druid"] = new[]
        {
            ("Rebirth", "rebirth.jpg", "Возрождение", true),
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
        ["Feral Druid"] = new[]
        {
            ("Rebirth", "rebirth.jpg", "Возрождение", true),
            // Кот
            ("Roar", "savage_roar.jpg", "Дикий рев", true),
            ("TF", "tigers_fury.jpg", "Тигриное неистовство", true),
            ("Berserk", "berserk.jpg", "Берсерк", true),
            ("FF_cat", "faerie_fire.jpg", "Волшебный огонь (зверь)", true),
            ("Mangle", "mangle_cat.jpg", "Увечье (кошка)", true),
            ("Rake", "faerie_fire.jpg", "Глубокая рана", true),
            ("Rip", "rip.jpg", "Разорвать", true),
            ("FB", "ferocious_bite.jpg", "Свирепый укус", true),
            ("Shred", "shred.jpg", "Полоснуть", true),
            // Медведь
            ("FF_bear", "faerie_fire.jpg", "Волшебный огонь (медведь)", true),
            ("Mangle_b", "mangle_bear.jpg", "Увечье (медведь)", true),
            ("Lacerate", "lacerate.jpg", "Увечье (медведь доп)", true),
            ("Swipe", "swipe_bear.jpg", "Размах", true),
            ("Maul", "maul.jpg", "Трепка", true),
        },
        ["Resto Druid"] = new[]
        {
            ("Rebirth", "rebirth.jpg", "Возрождение", true),
            ("ToL", "tree_life.jpg", "Древо Жизни", true),
            ("WG", "wild_growth.jpg", "Буйный рост", true),
            ("NS", "natures_swift.jpg", "Природная стремительность", true),
            ("SM", "swiftmend.jpg", "Быстрое восстановление", true),
            ("Rejuv", "rejuvenation.jpg", "Омоложение", true),
            ("LB", "lifebloom.jpg", "Жизнецвет", true),
            ("Regrowth", "regrowth.jpg", "Восстановление", true),
            ("Nourish", "nourish.jpg", "Покровительство Природы", true),
        },
        // ==================== MAGE ====================
        ["Arcane Mage"] = new[]
        {
            ("AP", "arcane_power.jpg", "Мощь тайной магии", true),
            ("Mirror", "mirror_image.jpg", "Зеркальное изображение", true),
            ("Barrage", "arcane_missiles.jpg", "Чародейские стрелы", true),
            ("Evoc", "evocation.jpg", "Прилив сил", true),
            ("AB", "arcane_blast.jpg", "Чародейская вспышка", true),
        },
        ["Fire Mage"] = new[]
        {
            ("Mirror", "mirror_image.jpg", "Зеркальное изображение", true),
            ("Combust", "combustion.jpg", "Возгорание", true),
            ("LB", "living_bomb.jpg", "Живая бомба", true),
            ("Pyro", "pyroblast.jpg", "Огненная глыба", true),
            ("Scorch", "scorch.jpg", "Ожог", true),
            ("FB", "fireball.jpg", "Огненный шар", true),
        },
        ["Frost Mage"] = new[]
        {
            ("Mirror", "mirror_image.jpg", "Зеркальное изображение", true),
            ("DF", "deep_freeze.jpg", "Глубокая заморозка", true),
            ("IL", "ice_lance.jpg", "Ледяное копье", true),
            ("FFB", "frostfire_bolt.jpg", "Стрела ледяного огня", true),
            ("FBolt", "frostbolt.jpg", "Ледяная стрела", true),
        },
        // ==================== WARLOCK ====================
        ["Affliction Lock"] = new[]
        {
            ("Haunt", "haunt.jpg", "Блуждающий дух", true),
            ("UA", "unstable_aff.jpg", "Нестабильное колдовство", true),
            ("Corruption", "corruption.jpg", "Порча", true),
            ("Immolate", "immolate.jpg", "Жертвенный огонь", true),
            ("DF", "shadow_bolt.jpg", "Неистовство Тьмы", true),
            ("ShadowBolt", "shadow_bolt.jpg", "Стрела Тьмы", true),
            ("LifeTap", "life_tap.jpg", "Жизнеотвод", true),
            ("LTGlyph", "life_tap.jpg", "Символ Жизнеотвода", false),
        },
        ["Demonology Lock"] = new[]
        {
            ("Meta", "meta.jpg", "Метаморфоза", true),
            ("DemonEmpower", "demon_empower.jpg", "Демоническое могущество", true),
            ("ImmoAura", "immo_aura.jpg", "Жертвенный костер", true),
            ("Corruption", "corruption.jpg", "Порча", true),
            ("Immolate", "immolate.jpg", "Жертвенный огонь", true),
            ("SoulFire", "soul_fire.jpg", "Ожог души", true),
            ("Incinerate", "incinerate.jpg", "Испепеление", true),
            ("ShadowBolt", "shadow_bolt.jpg", "Стрела Тьмы", true),
            ("LifeTap", "life_tap.jpg", "Жизнеотвод", true),
            ("LTGlyph", "life_tap.jpg", "Символ Жизнеотвода", false),
        },
        ["Destruction Lock"] = new[]
        {
            ("Chaos", "chaos_bolt.jpg", "Стрела Хаоса", true),
            ("Conflag", "conflagrate.jpg", "Поджигание", true),
            ("Immolate", "immolate.jpg", "Жертвенный огонь", true),
            ("Corruption", "corruption.jpg", "Порча", true),
            ("Incinerate", "incinerate.jpg", "Испепеление", true),
            ("LifeTap", "life_tap.jpg", "Жизнеотвод", true),
            ("LTGlyph", "life_tap.jpg", "Символ Жизнеотвода", false),
        },
        // DRUID toggles в SpecSpells
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
    private static readonly string SettingsDir = AppDomain.CurrentDomain.BaseDirectory;
    private string _charName = "";
    private string SettingsPath => string.IsNullOrEmpty(_charName)
        ? Path.Combine(SettingsDir, "settings.json")
        : Path.Combine(SettingsDir, $"settings_{_charName}.json");
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
            ("Чародейская гениальность", "arcane_brilliance.jpg", "Чародейская гениальность", true),
            ("Раскаленный доспех", "molten_armor.jpg", "Раскаленный доспех", true),
            ("Ледяной доспех", "frost_armor.jpg", "Ледяной доспех", false),
            ("Магический доспех", "mage_armor.jpg", "Магический доспех", false),
        },
        ["WARLOCK"] = new[]
        {
            ("Доспех Скверны", "fel_armor.jpg", "Доспех Скверны", true),
            ("WB_SPELLSTONE", "spellstone.jpg", "Камень чар", true),
        },
        ["SHAMAN"] = new[]
        {
            ("Щит молний", "lightning_shield.jpg", "Щит молний", true),
            ("Водный щит", "water_shield.jpg", "Водный щит", false),
            ("WB_WEAPON_FT", "shaman_weapon_ft.jpg", "Оружие языка пламени", false),
            ("WB_WEAPON_EL", "shaman_weapon_el.jpg", "Оружие жизни земли", false),
            ("WB_WEAPON_WF", "shaman_weapon_wf.jpg", "Оружие неистовства ветра", false),
        },
        // WARRIOR: крики и стойки через радио-выбор (ShoutOptions/StanceOptions), не здесь
        ["HUNTER"] = new[]
        {
            ("Дух дракондора", "aimed_shot.jpg", "Дух дракондора", true),
            ("Дух гадюки", "serpent_sting.jpg", "Дух гадюки", true),
        },
        ["ROGUE"] = Array.Empty<(string, string, string, bool)>(),
        // DEATHKNIGHT: власти через радио-выбор (PresenceOptions), не здесь
        ["PALADIN"] = Array.Empty<(string, string, string, bool)>(),
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
    private void MenuHivemind_Click(object s, MouseButtonEventArgs e) => ShowSubmenu("Hivemind");
    private void MenuReload_Click(object s, MouseButtonEventArgs e) => OnReloadScripts?.Invoke();
    public event Action? OnReloadScripts;
    public event Func<string, string?>? OnLuaExecute;

    private Window? _luaPopup;

    private void LuaToggle_Click(object s, MouseButtonEventArgs e)
    {
        if (_luaPopup != null && _luaPopup.IsVisible)
        {
            _luaPopup.Close();
            _luaPopup = null;
            return;
        }

        _luaPopup = new Window
        {
            Title = "Lua",
            Width = 450, Height = 180,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0d0f14")),
            Topmost = true,
            Left = Left, Top = Top + ActualHeight + 4,
        };

        var stack = new StackPanel { Margin = new Thickness(4) };
        var input = new TextBox
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16181e")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c8aa6e")),
            BorderThickness = new Thickness(0),
            FontSize = 11, FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(4, 3, 4, 3),
        };
        var output = new TextBlock
        {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5ab57a")),
            FontSize = 10, FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
        };
        var scroll = new ScrollViewer
        {
            MaxHeight = 120, Margin = new Thickness(0, 4, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = output,
        };

        input.KeyDown += (s2, e2) =>
        {
            if (e2.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
            {
                string cmd = input.Text.Trim();
                string? result = OnLuaExecute?.Invoke(cmd);
                output.Text = result ?? "(ok)";
                input.SelectAll();
                e2.Handled = true;
            }
            if (e2.Key == Key.Escape)
            {
                _luaPopup?.Close();
                _luaPopup = null;
                e2.Handled = true;
            }
        };

        stack.Children.Add(input);
        stack.Children.Add(scroll);
        _luaPopup.Content = stack;
        _luaPopup.Show();
        input.Focus();
    }

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
            case "Hivemind": BuildHivemindSubmenu(); break;
        }

        SubPanel.Visibility = Visibility.Visible;
    }

    // --- Build submenus ---

    private void BuildRotationSubmenu()
    {
        AddLabel("Заклинания");

        // Определяем спеллы по спеку
        string specKey = _playerSpec ?? "";
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
                    SaveSettings();
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

        // Дефабилки прот воина
        if (_playerSpec == "Prot Warrior")
        {
            _sliderDefHP = AddSlider("HP деф. бафов %", _sliderDefHP?.Value ?? GetSavedDouble("slider_defHP", 40), 10, 80, 5);
            _chkDefAll = AddCheckBox("Все дефы разом", _chkDefAll?.IsChecked ?? GetSavedBool("chk_defAll", false));
        }

        // Выбор правосудия для всех паладинов (радио)
        if (_playerClass == "PALADIN")
        {
            AddLabel("Выбор правосудия");
            var judgeWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _judgementToggles.Clear();
            foreach (var (key, icon, tooltip) in JudgementOptions)
            {
                bool isSelected = _selectedJudgement == key;
                var toggle = AddSpellIcon(judgeWrap, icon, tooltip, isSelected);
                _judgementToggles[key] = toggle;

                var jKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedJudgement = jKey;
                    foreach (var (k, btn) in _judgementToggles)
                        if (k != jKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedJudgement == jKey) _selectedJudgement = "";
                };
            }
            SubContent.Children.Add(judgeWrap);
        }
    }

    private Slider _sliderAoeMin = null!;
    public int AoeMinEnemies => (int)(_sliderAoeMin?.Value ?? GetSavedDouble("slider_aoeMin", 3));

    private void BuildAoeSubmenu()
    {
        string specKey = _playerSpec ?? "";

        // Глобальный ползунок: мин. врагов для AoE (для всех классов)
        _sliderAoeMin = AddSlider("Мин. врагов для AoE", _sliderAoeMin?.Value ?? GetSavedDouble("slider_aoeMin", 3), 2, 10, 1);
        _sliderAoeMin.ValueChanged += (s, e) => SaveSettings();

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
            AddLabel("AoE");
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            _spellToggles["Hurricane"] = AddSpellIcon(wrap, "hurricane.jpg", "Гроза (по порогу врагов)", _spellToggles.TryGetValue("Hurricane", out var hBtn) ? hBtn.IsChecked == true : GetSavedBool("spell_Hurricane", true));
            SubContent.Children.Add(wrap);
        }
        else if (specKey == "Ret Paladin" || specKey == "Prot Paladin")
        {
            AddLabel("AoE печати");
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
            var btn = AddSpellIcon(wrap, "seal_command.jpg", "Печать повиновения (2+ врагов)", GetSavedBool("aoe_SoC", true));
            btn.Checked += (s, e) => SaveSettings();
            btn.Unchecked += (s, e) => SaveSettings();
            _aoeSocToggle = btn;
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
        if (_buffToggles.Count == 0 && _playerClass != "PALADIN" && _playerClass != "WARRIOR" && _playerClass != "DEATHKNIGHT" && !(_playerClass == "DRUID" && _playerSpec == "Feral Druid"))
        {
            AddLabel("Класс не определен");
            return;
        }

        // Шаман: щиты и оружие — радио (взаимоисключающие)
        var shamanShields = new[] { "Щит молний", "Водный щит" };
        var shamanWeapons = new[] { "WB_WEAPON_FT", "WB_WEAPON_EL", "WB_WEAPON_WF" };
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var (spell, oldToggle) in _buffToggles.ToList())
        {
            // Preserve state, rebuild icon
            bool wasChecked = oldToggle.IsChecked == true;
            string? tooltip = oldToggle.ToolTip?.ToString();
            string iconFile = oldToggle.Tag?.ToString() ?? "";

            var newToggle = AddSpellIcon(wrap, iconFile, tooltip ?? spell, wasChecked);
            _buffToggles[spell] = newToggle;

            // Радио-логика для щитов шамана
            if (_playerClass == "SHAMAN" && shamanShields.Contains(spell))
            {
                var thisSpell = spell;
                newToggle.Checked += (s, e) =>
                {
                    foreach (var sh in shamanShields)
                        if (sh != thisSpell && _buffToggles.ContainsKey(sh))
                            _buffToggles[sh].IsChecked = false;
                };
            }
            // Радио-логика для оружия шамана
            if (_playerClass == "SHAMAN" && shamanWeapons.Contains(spell))
            {
                var thisSpell = spell;
                newToggle.Checked += (s, e) =>
                {
                    foreach (var w in shamanWeapons)
                        if (w != thisSpell && _buffToggles.ContainsKey(w))
                            _buffToggles[w].IsChecked = false;
                };
            }
        }
        SubContent.Children.Add(wrap);

        // Выбор ауры для паладина (радио)
        if (_playerClass == "PALADIN")
        {
            AddLabel("Выбор ауры");
            var auraWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _auraToggles.Clear();
            foreach (var (key, icon, tooltip) in AuraOptions)
            {
                bool isSelected = _selectedAura == key;
                var toggle = AddSpellIcon(auraWrap, icon, tooltip, isSelected);
                _auraToggles[key] = toggle;

                var aKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedAura = aKey;
                    foreach (var (k, btn) in _auraToggles)
                        if (k != aKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedAura == aKey) _selectedAura = "";
                };
            }
            SubContent.Children.Add(auraWrap);
        }

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
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedSeal == sealKey) _selectedSeal = "";
                };
            }
            SubContent.Children.Add(sealWrap);

            // Правосудие перенесено в раздел Rotation

            // Выбор благословения (радио)
            AddLabel("Выбор благословения");
            var blessWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            foreach (var (key, icon, tooltip) in BlessingOptions)
            {
                // Благословение неприкосновенности только для прот паладина (талант)
                if (key == "BoS" && _playerSpec != "Prot Paladin") continue;
                bool isSelected = _selectedBlessing == key;
                var toggle = AddSpellIcon(blessWrap, icon, tooltip, isSelected);
                _blessingToggles[key] = toggle;

                var blessKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedBlessing = blessKey;
                    foreach (var (k, btn) in _blessingToggles)
                        if (k != blessKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedBlessing == blessKey) _selectedBlessing = "";
                };
            }
            SubContent.Children.Add(blessWrap);
        }

        // Выбор формы для ферал друида (радио)
        if (_playerClass == "DRUID" && _playerSpec == "Feral Druid")
        {
            AddLabel("Выбор формы");
            var formWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _feralFormToggles.Clear();
            foreach (var (key, icon, tooltip) in FeralFormOptions)
            {
                bool isSelected = _selectedFeralForm == key;
                var toggle = AddSpellIcon(formWrap, icon, tooltip, isSelected);
                _feralFormToggles[key] = toggle;

                var formKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedFeralForm = formKey;
                    foreach (var (k, btn) in _feralFormToggles)
                        if (k != formKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedFeralForm == formKey) _selectedFeralForm = "";
                };
            }
            SubContent.Children.Add(formWrap);
        }

        // Выбор крика для воина (радио)
        if (_playerClass == "WARRIOR")
        {
            AddLabel("Выбор крика");
            var shoutWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _shoutToggles.Clear();
            foreach (var (key, icon, tooltip) in ShoutOptions)
            {
                bool isSelected = _selectedShout == key;
                var toggle = AddSpellIcon(shoutWrap, icon, tooltip, isSelected);
                _shoutToggles[key] = toggle;

                var shoutKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedShout = shoutKey;
                    foreach (var (k, btn) in _shoutToggles)
                        if (k != shoutKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedShout == shoutKey) _selectedShout = "";
                };
            }
            SubContent.Children.Add(shoutWrap);

            AddLabel("Выбор стойки");
            var stanceWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _stanceToggles.Clear();
            foreach (var (key, icon, tooltip) in StanceOptions)
            {
                bool isSelected = _selectedStance == key;
                var toggle = AddSpellIcon(stanceWrap, icon, tooltip, isSelected);
                _stanceToggles[key] = toggle;

                var stanceKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedStance = stanceKey;
                    foreach (var (k, btn) in _stanceToggles)
                        if (k != stanceKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedStance == stanceKey) _selectedStance = "";
                };
            }
            SubContent.Children.Add(stanceWrap);
        }

        // Выбор власти для ДК (радио)
        if (_playerClass == "DEATHKNIGHT")
        {
            AddLabel("Выбор власти");
            var presenceWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _presenceToggles.Clear();
            foreach (var (key, icon, tooltip) in PresenceOptions)
            {
                bool isSelected = _selectedPresence == key;
                var toggle = AddSpellIcon(presenceWrap, icon, tooltip, isSelected);
                _presenceToggles[key] = toggle;

                var presKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedPresence = presKey;
                    foreach (var (k, btn) in _presenceToggles)
                        if (k != presKey) btn.IsChecked = false;
                    SaveSettings();
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedPresence == presKey) _selectedPresence = "";
                };
            }
            SubContent.Children.Add(presenceWrap);
        }

        // Тотемы шамана (4 стихии, радио)
        if (_playerClass == "SHAMAN")
        {
            void AddTotemRadio(string label, (string key, string icon, string tooltip)[] options,
                string elementId, Dictionary<string, ToggleButton> toggles)
            {
                string currentSel = elementId switch
                {
                    "earth" => _selectedTotemEarth, "fire" => _selectedTotemFire,
                    "water" => _selectedTotemWater, "air" => _selectedTotemAir, _ => ""
                };
                AddLabel(label);
                var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
                toggles.Clear();
                foreach (var (key, icon, tooltip) in options)
                {
                    bool isSelected = currentSel == key;
                    var toggle = AddSpellIcon(wrap, icon, tooltip, isSelected);
                    toggles[key] = toggle;

                    var tKey = key;
                    var elId = elementId;
                    toggle.Checked += (s, e) =>
                    {
                        switch (elId) {
                            case "earth": _selectedTotemEarth = tKey; break;
                            case "fire": _selectedTotemFire = tKey; break;
                            case "water": _selectedTotemWater = tKey; break;
                            case "air": _selectedTotemAir = tKey; break;
                        }
                        foreach (var (k, btn) in toggles)
                            if (k != tKey) btn.IsChecked = false;
                        SaveSettings();
                        OnTotemChanged?.Invoke(label, tKey);
                    };
                    toggle.Unchecked += (s, e) =>
                    {
                        // Только если этот тотем был выбран (не затирать выбор нового)
                        switch (elId) {
                            case "earth": if (_selectedTotemEarth == tKey) _selectedTotemEarth = ""; break;
                            case "fire": if (_selectedTotemFire == tKey) _selectedTotemFire = ""; break;
                            case "water": if (_selectedTotemWater == tKey) _selectedTotemWater = ""; break;
                            case "air": if (_selectedTotemAir == tKey) _selectedTotemAir = ""; break;
                        }
                        OnTotemChanged?.Invoke(label, "");
                    };
                }
                SubContent.Children.Add(wrap);
            }

            AddTotemRadio("Земля", TotemEarthOptions, "earth", _totemEarthToggles);
            AddTotemRadio("Огонь", TotemFireOptions, "fire", _totemFireToggles);
            AddTotemRadio("Вода", TotemWaterOptions, "water", _totemWaterToggles);
            AddTotemRadio("Воздух", TotemAirOptions, "air", _totemAirToggles);
        }
    }

    private void BuildFollowSubmenu()
    {
        var btn = new Button
        {
            Content = "Цель следования",
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
        _chkAutoFace = AddCheckBox("Автоповорот к таргету", _chkAutoFace?.IsChecked ?? GetSavedBool("chk_autoFace", true));
        _chkAutoTarget = AddCheckBox("Автовыбор таргета", _chkAutoTarget?.IsChecked ?? GetSavedBool("chk_autoTarget", true));
        _sliderMaxRange = AddSlider("Макс. дальность", _sliderMaxRange?.Value ?? GetSavedDouble("slider_maxRange", 30), 10, 45, 5);
    }

    // --- Hivemind ---
    public event Action<string, string>? OnTotemChanged; // (element, key)
    public event Action<string>? OnHivemindCommand;
    private string _hivemindRole = "none"; // "none", "master", "slave"

    /// <summary>Установить роль извне (из лаунчера)</summary>
    public void SetHivemindRole(string role)
    {
        _hivemindRole = role;
        // Перерисовать подменю если открыто
        if (_activeSubmenu == "Hivemind") ShowSubmenu("Hivemind");
    }
    private bool _autoSwitch = true;
    private bool _alwaysAssist = false;
    private List<WowBot.Core.Game.Hivemind.SlaveInfo> _slaveList = new();

    /// <summary>Обновить список слейвов из Hivemind</summary>
    public void UpdateSlaveList(List<WowBot.Core.Game.Hivemind.SlaveInfo> slaves)
    {
        _slaveList = slaves;
        if (_activeSubmenu == "Hivemind") ShowSubmenu("Hivemind");
    }

    private static string GetClassIcon(string className)
    {
        string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string iconName = className.ToUpperInvariant() switch
        {
            "WARRIOR" => "warrior.jpg",
            "PALADIN" => "paladin.jpg",
            "HUNTER" => "hunter.jpg",
            "ROGUE" => "rogue.jpg",
            "PRIEST" => "priest.jpg",
            "DEATHKNIGHT" => "dk.jpg",
            "SHAMAN" => "shaman.jpg",
            "MAGE" => "mage.jpg",
            "WARLOCK" => "warlock.jpg",
            "DRUID" => "druid.jpg",
            _ => ""
        };
        if (string.IsNullOrEmpty(iconName)) return "";
        string path = System.IO.Path.Combine(basePath, "Icons", iconName);
        return System.IO.File.Exists(path) ? path : "";
    }

    private void BuildHivemindSubmenu()
    {
        AddLabel("Режим");

        // Кнопки выбора роли
        var rolePanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };

        var btnMaster = new Button
        {
            Content = "Мастер", Width = 90, Margin = new Thickness(2),
            Background = _hivemindRole == "master"
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a6741"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 11, Cursor = Cursors.Hand,
        };
        btnMaster.Click += (s, e) => { _hivemindRole = "master"; OnHivemindCommand?.Invoke("role:master"); ShowSubmenu("Hivemind"); };

        var btnSlave = new Button
        {
            Content = "Слейв", Width = 90, Margin = new Thickness(2),
            Background = _hivemindRole == "slave"
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a6741"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 11, Cursor = Cursors.Hand,
        };
        btnSlave.Click += (s, e) => { _hivemindRole = "slave"; OnHivemindCommand?.Invoke("role:slave"); ShowSubmenu("Hivemind"); };

        var btnOff = new Button
        {
            Content = "Выкл", Width = 60, Margin = new Thickness(2),
            Background = _hivemindRole == "none"
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6b3a3a"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontSize = 11, Cursor = Cursors.Hand,
        };
        btnOff.Click += (s, e) => { _hivemindRole = "none"; OnHivemindCommand?.Invoke("role:none"); ShowSubmenu("Hivemind"); };

        rolePanel.Children.Add(btnMaster);
        rolePanel.Children.Add(btnSlave);
        rolePanel.Children.Add(btnOff);
        SubContent.Children.Add(rolePanel);

        // Команды мастера
        if (_hivemindRole == "master")
        {
            AddLabel("Команды");

            AddHiveButton("⚔ Бейте таргет", "attack");
            AddHiveButton("🏃 Ко мне", "follow");
            AddHiveButton("🔄 Авто", "auto");
            AddHiveButton("⏹ Стоп", "stop");

            // Панель слейвов
            if (_slaveList.Count > 0)
            {
                AddLabel("Слейвы");
                var hint = new TextBlock
                {
                    Text = "Клик = выбрать. Пусто = команды для всех.",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
                    FontSize = 9, Margin = new Thickness(5, 0, 0, 4),
                };
                SubContent.Children.Add(hint);

                foreach (var slave in _slaveList)
                {
                    var slaveBtn = new Button
                    {
                        Background = slave.Selected
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4a6741"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
                        Foreground = slave.Selected
                            ? Brushes.White
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888")),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(6, 4, 6, 4),
                        Cursor = Cursors.Hand,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 1, 0, 1),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };

                    // Иконка класса + имя
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    string classIcon = GetClassIcon(slave.ClassName);
                    if (!string.IsNullOrEmpty(classIcon))
                    {
                        try
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(classIcon)),
                                Width = 18, Height = 18,
                                Margin = new Thickness(0, 0, 6, 0),
                            };
                            sp.Children.Add(img);
                        }
                        catch { }
                    }
                    sp.Children.Add(new TextBlock { Text = slave.Name, VerticalAlignment = VerticalAlignment.Center });
                    slaveBtn.Content = sp;

                    string slaveName = slave.Name;
                    slaveBtn.Click += (s, e) => { OnHivemindCommand?.Invoke($"toggle_slave:{slaveName}"); ShowSubmenu("Hivemind"); };
                    SubContent.Children.Add(slaveBtn);
                }
            }
            else
            {
                var noSlaves = new TextBlock
                {
                    Text = "Слейвы не подключены",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")),
                    FontSize = 10, Margin = new Thickness(5, 6, 0, 0),
                };
                SubContent.Children.Add(noSlaves);
            }
        }
        else if (_hivemindRole == "slave")
        {
            AddLabel("Слейв-режим активен");
            AddLabel("Жду команды мастера...");

            // Тоглы
            var chkAutoSwitch = new System.Windows.Controls.CheckBox
            {
                Content = "Автосвич (свич цели если умерла)",
                IsChecked = _autoSwitch,
                Foreground = new SolidColorBrush(Colors.LightGray),
                Margin = new Thickness(5, 5, 0, 0),
                FontSize = 11,
            };
            chkAutoSwitch.Checked += (s, e) => { _autoSwitch = true; OnHivemindCommand?.Invoke("autoswitch:on"); };
            chkAutoSwitch.Unchecked += (s, e) => { _autoSwitch = false; OnHivemindCommand?.Invoke("autoswitch:off"); };
            SubContent.Children.Add(chkAutoSwitch);

            var chkAlwaysAssist = new System.Windows.Controls.CheckBox
            {
                Content = "Всегда бить цель мастера",
                IsChecked = _alwaysAssist,
                Foreground = new SolidColorBrush(Colors.LightGray),
                Margin = new Thickness(5, 3, 0, 0),
                FontSize = 11,
            };
            chkAlwaysAssist.Checked += (s, e) => { _alwaysAssist = true; OnHivemindCommand?.Invoke("alwaysassist:on"); };
            chkAlwaysAssist.Unchecked += (s, e) => { _alwaysAssist = false; OnHivemindCommand?.Invoke("alwaysassist:off"); };
            SubContent.Children.Add(chkAlwaysAssist);
        }
    }

    private void AddHiveButton(string label, string command)
    {
        var btn = new Button
        {
            Content = label,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#c0c0c0")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = Cursors.Hand,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 1, 0, 1),
        };
        btn.Click += (s, e) => OnHivemindCommand?.Invoke(command);
        SubContent.Children.Add(btn);
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
        bool iconLoaded = false;
        if (File.Exists(iconPath))
        {
            try
            {
                toggle.Content = new Image
                {
                    Source = new BitmapImage(new Uri(iconPath)),
                    Stretch = Stretch.UniformToFill,
                };
                iconLoaded = true;
            }
            catch { /* битый файл — покажем текст */ }
        }
        if (!iconLoaded)
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
    public void SetPlayerClass(string playerClass, string specName = "", string charName = "")
    {
        _playerClass = playerClass;
        _playerSpec = specName;
        _charName = charName;
        // Перегружаем настройки из файла этого персонажа
        LoadSettings();
        _spellToggles.Clear();
        _buffToggles.Clear();

        // Устанавливаем дефолты ТОЛЬКО для нужного класса
        if (playerClass == "PALADIN")
        {
            // Дефолт ауры по спеку: прот→воина Света, хпал→сосредоточенности, рет→воздаяния
            string defaultAura = _playerSpec == "Prot Paladin" ? "AuDev"
                : _playerSpec == "Holy Paladin" ? "AuConc" : "AuRet";
            _selectedAura = GetSavedString("aura", defaultAura);
            string defaultBlessing = _playerSpec == "Prot Paladin" ? "BoS" : "BoM";
            _selectedBlessing = GetSavedString("blessing", defaultBlessing);
            _selectedSeal = _playerSpec == "Holy Paladin"
                ? GetSavedString("seal", "SoW")
                : GetSavedString("seal", "SoV");
            _selectedJudgement = GetSavedString("judgement", "JoW");
        }
        else
        {
            _selectedAura = "";
            _selectedBlessing = "";
            _selectedSeal = "";
            _selectedJudgement = "";
        }
        // Форма ферала: дефолт — кот
        if (playerClass == "DRUID" && (_playerSpec == "Feral Druid"))
        {
            _selectedFeralForm = GetSavedString("feralForm", "Cat");
        }
        else
        {
            _selectedFeralForm = "";
        }

        // Крик воина: дефолт — боевой для фури/армс, командирский для прота
        // Стойка воина: дефолт — защитная для прота, боевая для армс, берсерк для фури
        if (playerClass == "WARRIOR")
        {
            string defaultShout = _playerSpec == "Prot Warrior" ? "Commanding" : "Battle";
            _selectedShout = GetSavedString("shout", defaultShout);
            string defaultStance = _playerSpec == "Prot Warrior" ? "Defensive"
                : _playerSpec == "Fury Warrior" ? "Berserker" : "Battle";
            _selectedStance = GetSavedString("stance", defaultStance);
        }
        else
        {
            _selectedShout = "";
            _selectedStance = "";
        }
        // Власть ДК: дефолт — кровь для блада, лёд для фроста, нечестивость для анхоли
        if (playerClass == "DEATHKNIGHT")
        {
            string defaultPresence = _playerSpec == "Blood DK" ? "Blood"
                : _playerSpec == "Frost DK" ? "Frost" : "Unholy";
            _selectedPresence = GetSavedString("presence", defaultPresence);
        }
        else
        {
            _selectedPresence = "";
        }
        _selectedCurse = playerClass == "WARLOCK" ? GetSavedString("curse", "CoA") : "";

        // Тотемы шамана: дефолты по спеку
        if (playerClass == "SHAMAN")
        {
            string defAir = _playerSpec == "Enhancement Shaman" ? "Windfury" : "WrathOfAir";
            string defEarth = "SoE"; // Сила земли для всех спеков
            _selectedTotemEarth = GetSavedString("totemEarth", defEarth);
            _selectedTotemFire = GetSavedString("totemFire", "Flametongue");
            _selectedTotemWater = GetSavedString("totemWater", "ManaSpring");
            _selectedTotemAir = GetSavedString("totemAir", defAir);
        }
        else
        {
            _selectedTotemEarth = "";
            _selectedTotemFire = "";
            _selectedTotemWater = "";
            _selectedTotemAir = "";
        }

        // Pre-create spell toggles from SpecSpells (для GetSpellFlagsLua)
        string specKey = specName;
        if (SpecSpells.TryGetValue(specKey, out var spells))
        {
            foreach (var (key, icon, label, defaultOn) in spells)
            {
                var toggle = new ToggleButton
                {
                    IsChecked = GetSavedBool($"spell_{key}", defaultOn),
                    ToolTip = label,
                    Tag = icon,
                };
                _spellToggles[key] = toggle;
            }
        }

        // Pre-create AoE spell toggles (не отображаются в ротации, но нужны для SpellFlagsLua)
        if (specKey == "Balance Druid" && !_spellToggles.ContainsKey("Hurricane"))
            _spellToggles["Hurricane"] = new ToggleButton { IsChecked = GetSavedBool("spell_Hurricane", true) };

        // Pre-create buff toggles from ClassBuffs
        if (!ClassBuffs.TryGetValue(playerClass, out var buffs)) return;
        // Дефолт оружия шамана по спеку
        string defWeapon = _playerSpec == "Enhancement Shaman" ? "WB_WEAPON_WF"
            : _playerSpec == "Resto Shaman" ? "WB_WEAPON_EL" : "WB_WEAPON_FT";
        foreach (var (spell, icon, label, defaultOn) in buffs)
        {
            bool defOn = defaultOn;
            // Шаман: оружие — дефолт по спеку
            if (playerClass == "SHAMAN" && spell.StartsWith("WB_WEAPON_"))
                defOn = spell == defWeapon;
            var toggle = new ToggleButton
            {
                IsChecked = GetSavedBool($"buff_{spell}", defOn),
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

    public void UpdateBuffs(bool active)
    {
        BtnBuffs.IsChecked = active;
        BtnBuffs.Content = active ? "ON" : "OFF";
    }

    public void UpdateInfo(string text) => TxtInfo.Text = text;
    public void UpdateStatus(string text) =>
        TxtSpec.Text = SpecDisplayNames.TryGetValue(text, out var ru) ? ru : text;

    // --- Settings persistence ---

    private bool GetSavedBool(string key, bool defaultVal) =>
        _saved.TryGetValue(key, out var v) ? v.GetBoolean() : defaultVal;

    private double GetSavedDouble(string key, double defaultVal) =>
        _saved.TryGetValue(key, out var v) ? v.GetDouble() : defaultVal;

    private string GetSavedString(string key, string defaultVal)
    {
        if (!_saved.TryGetValue(key, out var v)) return defaultVal;
        var s = v.GetString();
        return s ?? defaultVal; // пустая строка = осознанный выбор "ничего"
    }

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

            // Curse/Seal/Judgement — НЕ грузим здесь, грузим в SetPlayerClass по классу

            // AutoFace default from settings
            _autoFaceDefault = GetSavedBool("chk_autoFace", true);

            // Main toggles (AoE, Buffs)
            BtnAoe.IsChecked = GetSavedBool("aoe", true);
            BtnAoe.Content = BtnAoe.IsChecked == true ? "ON" : "OFF";
            BtnBuffs.IsChecked = GetSavedBool("buffs", false);
            BtnBuffs.Content = BtnBuffs.IsChecked == true ? "ON" : "OFF";

            // Слайдеры — применить сохранённые значения (триггерит ValueChanged → BotEngine)
            if (_sliderDist != null) _sliderDist.Value = GetSavedDouble("slider_dist", 8);
            if (_sliderMaxRange != null) _sliderMaxRange.Value = GetSavedDouble("slider_maxRange", 30);
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
            data["blessing"] = _selectedBlessing;
            data["judgement"] = _selectedJudgement;
            data["aura"] = _selectedAura;
            data["shout"] = _selectedShout;
            data["feralForm"] = _selectedFeralForm;
            data["stance"] = _selectedStance;
            data["presence"] = _selectedPresence;
            data["totemEarth"] = _selectedTotemEarth;
            data["totemFire"] = _selectedTotemFire;
            data["totemWater"] = _selectedTotemWater;
            data["totemAir"] = _selectedTotemAir;

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
            // Sliders — если UI не создан, сохраняем предыдущее значение
            if (_sliderDispMana != null) data["slider_dispMana"] = _sliderDispMana.Value;
            else if (_saved.ContainsKey("slider_dispMana")) data["slider_dispMana"] = GetSavedDouble("slider_dispMana", 30);
            if (_sliderSFMana != null) data["slider_sfMana"] = _sliderSFMana.Value;
            else if (_saved.ContainsKey("slider_sfMana")) data["slider_sfMana"] = GetSavedDouble("slider_sfMana", 50);
            if (_sliderMaxDots != null) data["slider_maxDots"] = _sliderMaxDots.Value;
            else if (_saved.ContainsKey("slider_maxDots")) data["slider_maxDots"] = GetSavedDouble("slider_maxDots", 4);
            if (_sliderMindSear != null) data["slider_mindSear"] = _sliderMindSear.Value;
            else if (_saved.ContainsKey("slider_mindSear")) data["slider_mindSear"] = GetSavedDouble("slider_mindSear", 4);
            if (_sliderAoeMin != null) data["slider_aoeMin"] = _sliderAoeMin.Value;
            else if (_saved.ContainsKey("slider_aoeMin")) data["slider_aoeMin"] = GetSavedDouble("slider_aoeMin", 3);
            if (_sliderDist != null) data["slider_dist"] = _sliderDist.Value;
            else if (_saved.ContainsKey("slider_dist")) data["slider_dist"] = GetSavedDouble("slider_dist", 8);
            if (_sliderMaxRange != null) data["slider_maxRange"] = _sliderMaxRange.Value;
            else if (_saved.ContainsKey("slider_maxRange")) data["slider_maxRange"] = GetSavedDouble("slider_maxRange", 30);
            if (_sliderDefHP != null) data["slider_defHP"] = _sliderDefHP.Value;
            else if (_saved.ContainsKey("slider_defHP")) data["slider_defHP"] = GetSavedDouble("slider_defHP", 40);
            if (_chkDefAll != null) data["chk_defAll"] = _chkDefAll.IsChecked == true;
            else if (_saved.ContainsKey("chk_defAll")) data["chk_defAll"] = GetSavedBool("chk_defAll", false);

            // AoE toggles
            if (_aoeSocToggle != null) data["aoe_SoC"] = _aoeSocToggle.IsChecked == true;
            else if (_saved.ContainsKey("aoe_SoC")) data["aoe_SoC"] = GetSavedBool("aoe_SoC", true);

            // Checkboxes — если UI не создан, сохраняем предыдущее значение из _saved
            data["chk_autoFace"] = _chkAutoFace != null ? _chkAutoFace.IsChecked == true : GetSavedBool("chk_autoFace", true);
            data["chk_autoTarget"] = _chkAutoTarget != null ? _chkAutoTarget.IsChecked == true : GetSavedBool("chk_autoTarget", true);
            if (_chkMultiDot != null) data["chk_multiDot"] = _chkMultiDot.IsChecked == true;
            else if (_saved.ContainsKey("chk_multiDot")) data["chk_multiDot"] = GetSavedBool("chk_multiDot", true);
            if (_chkMindSear != null) data["chk_mindSear"] = _chkMindSear.IsChecked == true;
            else if (_saved.ContainsKey("chk_mindSear")) data["chk_mindSear"] = GetSavedBool("chk_mindSear", true);

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore write errors */ }
    }
}
