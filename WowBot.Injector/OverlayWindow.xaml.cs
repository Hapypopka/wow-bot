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

    // Judgement selection for Holy Paladin (radio-style)
    private string _selectedJudgement = ""; // Только для Holy Paladin
    private readonly Dictionary<string, ToggleButton> _judgementToggles = new();
    private static readonly (string key, string icon, string tooltip)[] JudgementOptionsHoly =
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
    };

    // Aura selection for Paladin (radio-style)
    private string _selectedAura = ""; // Только для PALADIN
    private readonly Dictionary<string, ToggleButton> _auraToggles = new();
    private static readonly (string key, string icon, string tooltip)[] AuraOptions =
    {
        ("AuRet", "aura_retribution.jpg", "Аура воздаяния"),
        ("AuDev", "aura_devotion.jpg", "Аура воина Света"),
        ("AuFrost", "aura_frost.jpg", "Аура защиты от магии льда"),
        ("AuFire", "aura_fire.jpg", "Аура защиты от огня"),
        ("AuShadow", "aura_shadow.jpg", "Аура защиты от темной магии"),
        ("AuConc", "aura_concentration.jpg", "Аура сосредоточенности"),
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
            foreach (var (key, _, _) in JudgementOptionsHoly)
                parts.Add($"{key}={(_selectedJudgement == key ? "true" : "false")}");
            foreach (var (key, _, _) in BlessingOptions)
                parts.Add($"{key}={(_selectedBlessing == key ? "true" : "false")}");
        }
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
    public string SelectedBlessing => _selectedBlessing;
    public string SelectedAura => _selectedAura;
    public bool AutoFace => _chkAutoFace?.IsChecked == true;
    public bool AutoSelectTarget => _chkAutoTarget?.IsChecked == true;
    public int MaxTargetRange => (int)(_sliderMaxRange?.Value ?? 30);

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
            ("ShieldSlam", "shield_slam.jpg", "Мощный удар щитом", true),
            ("Revenge", "revenge.jpg", "Реванш", true),
            ("Devastate", "devastate.jpg", "Сокрушение", true),
            ("TC", "thunder_clap.jpg", "Удар грома", true),
            ("ShockW", "shockwave.jpg", "Ударная волна", true),
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
            ("AW", "avenging_wrath.jpg", "Гнев карателя", true),
            ("HoR", "hammer_righteous.jpg", "Молот праведника", true),
            ("ShoR", "shield_righteousness.jpg", "Щит праведности", true),
            ("HolyShield", "holy_shield.jpg", "Щит небес", true),
            ("Judge", "judgement.jpg", "Правосудие", true),
            ("Cons", "consecration.jpg", "Освящение", true),
            ("HW", "holy_wrath.jpg", "Гнев небес", true),
            ("AS", "avengers_shield.jpg", "Щит мстителя", true),
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
            ("Pest", "pestilence.jpg", "Мор", true),
            ("UA", "unbreakable_armor.jpg", "Несокрушимая броня", true),
            ("HB", "howling_blast.jpg", "Ледяной удар (прок)", true),
            ("Oblit", "obliterate.jpg", "Уничтожение", true),
            ("BS", "blood_strike.jpg", "Кровавый удар", true),
            ("FS", "frost_strike.jpg", "Лик смерти", true),
            ("HB2", "howling_blast.jpg", "Ледяной удар", true),
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
            ("FS", "flame_shock.jpg", "Огненный шок", true),
            ("LvB", "lava_burst.jpg", "Вскипание лавы", true),
            ("TnL", "thunderstorm.jpg", "Гром и молния", true),
            ("CL", "chain_lightning.jpg", "Цепная молния", true),
            ("LB", "lightning_bolt.jpg", "Молния", true),
        },
        ["Enhancement Shaman"] = new[]
        {
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
            ("RT", "riptide.jpg", "Быстрина", true),
            ("NS", "natures_swift.jpg", "Природная стремительность", true),
            ("CH", "chain_heal.jpg", "Цепное исцеление", true),
            ("LHW", "lesser_hw.jpg", "Малая волна исцеления", true),
            ("HW", "healing_wave.jpg", "Волна исцеления", true),
            ("ES", "earth_shield.jpg", "Щит земли", true),
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
        // ==================== DRUID ====================
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
        ["Feral Druid"] = new[]
        {
            ("Bear", "bear_form.jpg", "Режим медведя", false),
            ("Berserk", "berserk.jpg", "Берсерк", true),
            ("TF", "tigers_fury.jpg", "Тигриное неистовство", true),
            ("Roar", "savage_roar.jpg", "Дикий рев", true),
            ("Mangle", "mangle_cat.jpg", "Увечье (кошка)", true),
            ("Rake", "faerie_fire.jpg", "Растерзать", true),
            ("Rip", "rip.jpg", "Разорвать", true),
            ("FB", "ferocious_bite.jpg", "Свирепый укус", true),
            ("Shred", "shred.jpg", "Полоснуть", true),
            ("FF_bear", "faerie_fire.jpg", "Волшебный огонь (зверь)", true),
            ("Mangle_b", "mangle_bear.jpg", "Увечье (медведь)", true),
            ("Lacerate", "lacerate.jpg", "Растерзать", true),
            ("Swipe", "swipe_bear.jpg", "Размах (медведь)", true),
            ("Maul", "maul.jpg", "Трепка", true),
        },
        ["Resto Druid"] = new[]
        {
            ("ToL", "tree_life.jpg", "Древо Жизни", true),
            ("WG", "wild_growth.jpg", "Буйный рост", true),
            ("NS", "natures_swift.jpg", "Природная стремительность", true),
            ("SM", "swiftmend.jpg", "Быстрое восстановление", true),
            ("Rejuv", "rejuvenation.jpg", "Омоложение", true),
            ("LB", "lifebloom.jpg", "Жизнецвет", true),
            ("Regrowth", "regrowth.jpg", "Восстановление", true),
            ("Nourish", "nourish.jpg", "Целительное прикосновение", true),
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
        },
        ["WARRIOR"] = new[]
        {
            ("Боевой крик", "heroic_strike.jpg", "Боевой крик", true),
            ("Командирский крик", "heroic_strike.jpg", "Командирский крик", false),
        },
        ["HUNTER"] = new[]
        {
            ("Дух дракондора", "aimed_shot.jpg", "Дух дракондора", true),
            ("Дух гадюки", "serpent_sting.jpg", "Дух гадюки", true),
        },
        ["ROGUE"] = Array.Empty<(string, string, string, bool)>(),
        ["DEATHKNIGHT"] = new[]
        {
            ("Власть крови", "blood_strike.jpg", "Власть крови", false),
            ("Власть льда", "icy_touch.jpg", "Власть льда", false),
            ("Власть нечестивости", "unholy_blight.jpg", "Власть нечестивости", false),
        },
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
        string specKey = _playerSpec ?? "";

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
        if (_buffToggles.Count == 0 && _playerClass != "PALADIN")
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
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedSeal == sealKey) _selectedSeal = "";
                };
            }
            SubContent.Children.Add(sealWrap);

            // Выбор правосудия для хпала (радио)
            if (_playerSpec == "Holy Paladin")
            {
                AddLabel("Выбор правосудия");
                var judgeWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
                foreach (var (key, icon, tooltip) in JudgementOptionsHoly)
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

            // Выбор благословения (радио)
            AddLabel("Выбор благословения");
            var blessWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            foreach (var (key, icon, tooltip) in BlessingOptions)
            {
                bool isSelected = _selectedBlessing == key;
                var toggle = AddSpellIcon(blessWrap, icon, tooltip, isSelected);
                _blessingToggles[key] = toggle;

                var blessKey = key;
                toggle.Checked += (s, e) =>
                {
                    _selectedBlessing = blessKey;
                    foreach (var (k, btn) in _blessingToggles)
                        if (k != blessKey) btn.IsChecked = false;
                };
                toggle.Unchecked += (s, e) =>
                {
                    if (_selectedBlessing == blessKey) _selectedBlessing = "";
                };
            }
            SubContent.Children.Add(blessWrap);
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
    public event Action<string>? OnHivemindCommand;
    private string _hivemindRole = "none"; // "none", "master", "slave"
    private bool _autoSwitch = true;
    private bool _alwaysAssist = false;

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
            AddHiveButton("🛑 Стоп", "stop");
            AddHiveButton("↔ Рассыпьтесь", "scatter");
            AddHiveButton("📌 Стакайтесь", "stack");
            AddHiveButton("📡 Пинг", "ping");
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
    public void SetPlayerClass(string playerClass, string specName = "")
    {
        _playerClass = playerClass;
        _playerSpec = specName;
        _spellToggles.Clear();
        _buffToggles.Clear();

        // Устанавливаем дефолты ТОЛЬКО для нужного класса
        if (playerClass == "PALADIN")
        {
            _selectedAura = GetSavedString("aura", "AuRet");
            _selectedBlessing = GetSavedString("blessing", "BoM");
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
        _selectedCurse = playerClass == "WARLOCK" ? GetSavedString("curse", "CoA") : "";
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
    public void UpdateStatus(string text) =>
        TxtSpec.Text = SpecDisplayNames.TryGetValue(text, out var ru) ? ru : text;

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

            // Curse/Seal/Judgement — НЕ грузим здесь, грузим в SetPlayerClass по классу

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
            data["blessing"] = _selectedBlessing;
            data["judgement"] = _selectedJudgement;
            data["aura"] = _selectedAura;

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
