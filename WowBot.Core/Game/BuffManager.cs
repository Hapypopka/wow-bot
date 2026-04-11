namespace WowBot.Core.Game;

/// <summary>
/// Управление баффами: печати, ауры, благословения, крики, стойки, власти,
/// формы друида, тотемы шамана, оружие шамана, камень чар, призыв петов.
/// Вынесено из BotEngine для изоляции ответственности.
/// </summary>
public class BuffManager
{
    // === Настройки (из UI) — через свойства с инвалидацией кэша ===
    private string _playerClass = "";
    private string _selectedSeal = "";
    private string _selectedBlessing = "BoM";
    private string _selectedAura = "AuRet";
    private string _selectedShout = "";
    private string _selectedStance = "";
    private string _selectedPresence = "";
    private string _selectedFeralForm = "";
    private string _selectedPet = "";
    private string _selectedTotemEarth = "";
    private string _selectedTotemFire = "";
    private string _selectedTotemWater = "";
    private string _selectedTotemAir = "";
    private string _selectedWeaponMH = "";
    private string _selectedWeaponOH = "";
    private bool _aoeEnabled;
    private bool _aoeSealSwap;
    private List<string> _enabledBuffs = new();

    public string PlayerClass { get => _playerClass; set { if (_playerClass != value) { _playerClass = value; InvalidateCache(); } } }
    public string SelectedSeal { get => _selectedSeal; set { if (_selectedSeal != value) { _selectedSeal = value; InvalidateCache(); } } }
    public string SelectedBlessing { get => _selectedBlessing; set { if (_selectedBlessing != value) { _selectedBlessing = value; InvalidateCache(); } } }
    public string SelectedAura { get => _selectedAura; set { if (_selectedAura != value) { _selectedAura = value; InvalidateCache(); } } }
    public string SelectedShout { get => _selectedShout; set { if (_selectedShout != value) { _selectedShout = value; InvalidateCache(); } } }
    public string SelectedStance { get => _selectedStance; set { if (_selectedStance != value) { _selectedStance = value; InvalidateCache(); } } }
    public string SelectedPresence { get => _selectedPresence; set { if (_selectedPresence != value) { _selectedPresence = value; InvalidateCache(); } } }
    public string SelectedFeralForm { get => _selectedFeralForm; set { if (_selectedFeralForm != value) { _selectedFeralForm = value; InvalidateCache(); } } }
    public string SelectedPet { get => _selectedPet; set { if (_selectedPet != value) { _selectedPet = value; InvalidateCache(); } } }
    public string SelectedTotemEarth { get => _selectedTotemEarth; set { if (_selectedTotemEarth != value) { _selectedTotemEarth = value; InvalidateCache(); } } }
    public string SelectedTotemFire { get => _selectedTotemFire; set { if (_selectedTotemFire != value) { _selectedTotemFire = value; InvalidateCache(); } } }
    public string SelectedTotemWater { get => _selectedTotemWater; set { if (_selectedTotemWater != value) { _selectedTotemWater = value; InvalidateCache(); } } }
    public string SelectedTotemAir { get => _selectedTotemAir; set { if (_selectedTotemAir != value) { _selectedTotemAir = value; InvalidateCache(); } } }
    public string SelectedWeaponMH { get => _selectedWeaponMH; set { if (_selectedWeaponMH != value) { _selectedWeaponMH = value; InvalidateCache(); } } }
    public string SelectedWeaponOH { get => _selectedWeaponOH; set { if (_selectedWeaponOH != value) { _selectedWeaponOH = value; InvalidateCache(); } } }
    public bool AoeEnabled { get => _aoeEnabled; set { if (_aoeEnabled != value) { _aoeEnabled = value; InvalidateCache(); } } }
    public bool AoeSealSwap { get => _aoeSealSwap; set { if (_aoeSealSwap != value) { _aoeSealSwap = value; InvalidateCache(); } } }
    public List<string> EnabledBuffs { get => _enabledBuffs; set { _enabledBuffs = value; InvalidateCache(); } }

    // === Кэш скриптов ===
    private string? _cachedClassBuffScript;
    private string? _cachedFullBuffScript;

    public void InvalidateCache()
    {
        _cachedClassBuffScript = null;
        _cachedFullBuffScript = null;
    }

    // === Статические данные ===
    private static readonly HashSet<string> RaidBuffs = new()
    {
        "Молитва стойкости", "Молитва духа", "Молитва защиты от темной магии",
        "Дар дикой природы", "Чародейская гениальность",
    };

    private static readonly Dictionary<string, string> BuffAliases = new()
    {
        { "Молитва стойкости", "Слово силы: Стойкость" },
        { "Молитва духа", "Божественный дух" },
        { "Молитва защиты от темной магии", "Защита от темной магии" },
        { "Дар дикой природы", "Знак дикой природы" },
        { "Чародейская гениальность", "Чародейский интеллект" },
    };

    private static readonly Dictionary<string, (string reagent, string fallback)> BuffReagents = new()
    {
        { "Молитва стойкости", ("Свеча благочестия", "Слово силы: Стойкость") },
        { "Молитва духа", ("Свеча благочестия", "Божественный дух") },
        { "Молитва защиты от темной магии", ("Свеча благочестия", "Защита от темной магии") },
        { "Дар дикой природы", ("Дикий шиполист", "Знак дикой природы") },
        { "Чародейская гениальность", ("Чародейский порошок", "Чародейский интеллект") },
    };

    /// <summary>Быстрая проверка только классовых баффов (стойка/форма/власть/аура/печать) — каждые 0.5 сек</summary>
    public string BuildClassBuffScript()
    {
        if (_cachedClassBuffScript != null) return _cachedClassBuffScript;
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_CB() ");
        sb.Append("if IsMounted() then return end ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");

        bool hasAnything = false;

        // Стойка воина
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedStance))
        {
            var (stanceForm, stanceSpell) = SelectedStance switch
            {
                "Battle" => (1, "Боевая стойка"),
                "Defensive" => (2, "Оборонительная стойка"),
                "Berserker" => (3, "Стойка берсерка"),
                _ => (0, "")
            };
            if (stanceForm > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={stanceForm} then CastSpellByName('{stanceSpell}') return end ");
                hasAnything = true;
            }
        }

        // Власть ДК
        if (PlayerClass == "DEATHKNIGHT" && !string.IsNullOrEmpty(SelectedPresence))
        {
            var (presForm, presSpell) = SelectedPresence switch
            {
                "Blood" => (1, "Власть крови"),
                "Frost" => (2, "Власть льда"),
                "Unholy" => (3, "Власть нечестивости"),
                _ => (0, "")
            };
            if (presForm > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={presForm} then CastSpellByName('{presSpell}') return end ");
                hasAnything = true;
            }
        }

        // Форма друида
        if (PlayerClass == "DRUID" && !string.IsNullOrEmpty(SelectedFeralForm))
        {
            var (formId, formSpell) = SelectedFeralForm switch
            {
                "Cat" => (3, "Облик кошки"),
                "Bear" => (1, "Облик лютого медведя"),
                _ => (0, "")
            };
            if (formId > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={formId} then CastSpellByName('{formSpell}') return end ");
                hasAnything = true;
            }
        }

        // Аура паладина
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedAura))
        {
            string auraSpell = SelectedAura switch
            {
                "AuRet" => "Аура воздаяния",
                "AuDev" => "Аура благочестия",
                "AuCru" => "Аура воина Света",
                "AuFrost" => "Аура защиты от магии льда",
                "AuFire" => "Аура защиты от огня",
                "AuShadow" => "Аура защиты от темной магии",
                "AuConc" => "Аура сосредоточенности",
                _ => ""
            };
            if (!string.IsNullOrEmpty(auraSpell))
            {
                sb.Append($"local function HasB(u,n) for i=1,40 do local b=UnitBuff(u,i) if not b then return false end if b==n then return true end end return false end ");
                sb.Append($"if not HasB('player','{auraSpell}') then CastSpellByName('{auraSpell}') return end ");
                hasAnything = true;
            }
        }

        // Печать паладина (быстрая проверка)
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedSeal))
        {
            if (!hasAnything)
                sb.Append("local function HasB(u,n) for i=1,40 do local b=UnitBuff(u,i) if not b then return false end if b==n then return true end end return false end ");
            if (AoeEnabled && AoeSealSwap && (SelectedSeal == "SoV" || SelectedSeal == "SoC"))
            {
                sb.Append("if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') end ");
                sb.Append("else if not HasB('player','Печать мщения') then CastSpellByName('Печать мщения') end end ");
            }
            else
            {
                string sealSpell = SelectedSeal switch
                {
                    "SoV" => "Печать мщения",
                    "SoC" => "Печать повиновения",
                    "SoW" => "Печать мудрости",
                    "SoL" => "Печать Света",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(sealSpell))
                    sb.Append($"if not HasB('player','{sealSpell}') then CastSpellByName('{sealSpell}') return end ");
            }
            hasAnything = true;
        }

        if (!hasAnything) { _cachedClassBuffScript = ""; return ""; }
        sb.Append("end WB_CB()");
        _cachedClassBuffScript = sb.ToString();
        return _cachedClassBuffScript;
    }

    /// <summary>Полная проверка всех баффов — каждые ~3 сек</summary>
    public string BuildBuffScript()
    {
        if (_cachedFullBuffScript != null) return _cachedFullBuffScript;
        if (EnabledBuffs.Count == 0 && string.IsNullOrEmpty(SelectedSeal) && string.IsNullOrEmpty(SelectedBlessing) && string.IsNullOrEmpty(SelectedAura) && string.IsNullOrEmpty(SelectedShout) && string.IsNullOrEmpty(SelectedStance) && string.IsNullOrEmpty(SelectedPresence) && string.IsNullOrEmpty(SelectedFeralForm) && string.IsNullOrEmpty(SelectedTotemEarth) && string.IsNullOrEmpty(SelectedTotemFire) && string.IsNullOrEmpty(SelectedTotemWater) && string.IsNullOrEmpty(SelectedTotemAir)) return "";

        var selfBuffs = new List<string>();
        var raidBuffs = new List<string>();

        foreach (var buff in EnabledBuffs)
        {
            if (RaidBuffs.Contains(buff))
                raidBuffs.Add(buff);
            else
                selfBuffs.Add(buff);
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_Buff() ");
        sb.Append("if IsMounted() then return end ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");
        sb.Append("if UnitIsDeadOrGhost('player') then return end ");
        sb.Append("local function HasB(unit,name) for i=1,40 do local n=UnitBuff(unit,i) if not n then return false end if n==name then return true end end return false end ");

        // Аура паладина
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedAura))
        {
            string auraSpell = SelectedAura switch
            {
                "AuRet" => "Аура воздаяния", "AuDev" => "Аура благочестия", "AuCru" => "Аура воина Света",
                "AuFrost" => "Аура защиты от магии льда", "AuFire" => "Аура защиты от огня",
                "AuShadow" => "Аура защиты от темной магии", "AuConc" => "Аура сосредоточенности", _ => ""
            };
            if (!string.IsNullOrEmpty(auraSpell))
            {
                var au = auraSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{au}') then CastSpellByName('{au}') return end ");
            }
        }

        // Печать паладина
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedSeal))
        {
            if (AoeEnabled && AoeSealSwap && (SelectedSeal == "SoV" || SelectedSeal == "SoC"))
            {
                sb.Append("if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') return end ");
                sb.Append("else if not HasB('player','Печать мщения') then CastSpellByName('Печать мщения') return end end ");
            }
            else
            {
                string sealSpell = SelectedSeal switch { "SoV" => "Печать мщения", "SoC" => "Печать повиновения", "SoW" => "Печать мудрости", "SoL" => "Печать Света", _ => "" };
                if (!string.IsNullOrEmpty(sealSpell))
                {
                    var ss = sealSpell.Replace("'", "\\'");
                    sb.Append($"if not HasB('player','{ss}') then CastSpellByName('{ss}') return end ");
                }
            }
        }

        // Благословение паладина
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedBlessing))
        {
            var (blessSpell, greatSpell) = SelectedBlessing switch
            {
                "BoM" => ("Благословение могущества", "Великое благословение могущества"),
                "BoK" => ("Благословение королей", "Великое благословение королей"),
                "BoW" => ("Благословение мудрости", "Великое благословение мудрости"),
                "BoS" => ("Благословение неприкосновенности", "Великое благословение неприкосновенности"),
                _ => ("", "")
            };
            if (!string.IsNullOrEmpty(blessSpell))
            {
                var bs = blessSpell.Replace("'", "\\'");
                var gs = greatSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{bs}') and not HasB('player','{gs}') then TargetUnit('player') if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end ");
                sb.Append($"local nr=GetNumRaidMembers() ");
                sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end ");
                sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end end ");
            }
        }

        // Крик воина
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedShout))
        {
            string shoutSpell = SelectedShout switch { "Battle" => "Боевой крик", "Commanding" => "Командирский крик", _ => "" };
            if (!string.IsNullOrEmpty(shoutSpell))
            {
                var sh = shoutSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{sh}') then CastSpellByName('{sh}') return end ");
            }
        }

        // Стойка воина
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedStance))
        {
            var (stanceForm, stanceSpell) = SelectedStance switch { "Battle" => (1, "Боевая стойка"), "Defensive" => (2, "Оборонительная стойка"), "Berserker" => (3, "Стойка берсерка"), _ => (0, "") };
            if (stanceForm > 0)
            {
                var st = stanceSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={stanceForm} then CastSpellByName('{st}') return end ");
            }
        }

        // Власть ДК
        if (PlayerClass == "DEATHKNIGHT" && !string.IsNullOrEmpty(SelectedPresence))
        {
            var (presForm, presSpell) = SelectedPresence switch { "Blood" => (1, "Власть крови"), "Frost" => (2, "Власть льда"), "Unholy" => (3, "Власть нечестивости"), _ => (0, "") };
            if (presForm > 0)
            {
                var pr = presSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={presForm} then CastSpellByName('{pr}') return end ");
            }
        }

        // Форма ферал друида
        if (PlayerClass == "DRUID" && !string.IsNullOrEmpty(SelectedFeralForm))
        {
            var (formId, formSpell) = SelectedFeralForm switch { "Cat" => (3, "Облик кошки"), "Bear" => (1, "Облик лютого медведя"), _ => (0, "") };
            if (formId > 0)
            {
                var fs = formSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={formId} then CastSpellByName('{fs}') return end ");
            }
        }

        // Тотемы шамана
        if (PlayerClass == "SHAMAN")
        {
            var earthIds = new Dictionary<string, int> { ["Stoneskin"] = 58753, ["SoE"] = 58643, ["Tremor"] = 8143 };
            var fireIds = new Dictionary<string, int> { ["Flametongue"] = 58656, ["FrostRes"] = 58745 };
            var waterIds = new Dictionary<string, int> { ["ManaSpring"] = 58774, ["HealStream"] = 58757, ["Cleansing"] = 8170, ["FireRes"] = 58739 };
            var airIds = new Dictionary<string, int> { ["WrathOfAir"] = 3738, ["Windfury"] = 8512, ["NatureRes"] = 58749 };

            // Забиваем набор во все 3 Call-спелла: Elements(133-136), Ancestors(137-140), Spirits(141-144)
            // Так неважно какой "Зов" выбран у юзера — тотемы всегда правильные
            if (!string.IsNullOrEmpty(SelectedTotemFire) && fireIds.TryGetValue(SelectedTotemFire, out int fireId))
                sb.Append($"SetMultiCastSpell(133,{fireId}) SetMultiCastSpell(137,{fireId}) SetMultiCastSpell(141,{fireId}) ");
            if (!string.IsNullOrEmpty(SelectedTotemEarth) && earthIds.TryGetValue(SelectedTotemEarth, out int earthId))
                sb.Append($"SetMultiCastSpell(134,{earthId}) SetMultiCastSpell(138,{earthId}) SetMultiCastSpell(142,{earthId}) ");
            if (!string.IsNullOrEmpty(SelectedTotemWater) && waterIds.TryGetValue(SelectedTotemWater, out int waterId))
                sb.Append($"SetMultiCastSpell(135,{waterId}) SetMultiCastSpell(139,{waterId}) SetMultiCastSpell(143,{waterId}) ");
            if (!string.IsNullOrEmpty(SelectedTotemAir) && airIds.TryGetValue(SelectedTotemAir, out int airId))
                sb.Append($"SetMultiCastSpell(136,{airId}) SetMultiCastSpell(140,{airId}) SetMultiCastSpell(144,{airId}) ");
        }

        // Аспект охотника
        if (PlayerClass == "HUNTER")
        {
            bool hasDragon = selfBuffs.Remove("Дух дракондора");
            bool hasViper = selfBuffs.Remove("Дух гадюки");
            if (hasDragon)
            {
                sb.Append("local m=UnitMana('player')/UnitManaMax('player') ");
                sb.Append("if not UnitAffectingCombat('player') then ");
                sb.Append("if m<1 and not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
                sb.Append("if m>=1 and not HasB('player','Дух дракондора') then CastSpellByName('Дух дракондора') return end ");
                sb.Append("else ");
                sb.Append("if m<0.3 and not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
                sb.Append("if m>0.8 and not HasB('player','Дух дракондора') then CastSpellByName('Дух дракондора') return end ");
                sb.Append("end ");
                sb.Append("if not HasB('player','Дух дракондора') and not HasB('player','Дух гадюки') then CastSpellByName('Дух дракондора') return end ");
            }
            else if (hasViper)
            {
                sb.Append("if not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
            }
        }

        // Камень чар
        if (selfBuffs.Remove("WB_SPELLSTONE"))
        {
            sb.Append("local hasEnch=GetWeaponEnchantInfo() ");
            sb.Append("if not hasEnch then ");
            sb.Append("if GetItemCount('Могучий камень чар')>0 then UseItemByName('Могучий камень чар') PickupInventoryItem(16) return end ");
            sb.Append("CastSpellByName('Создание камня чар') return end ");
        }

        // Призыв пета варлока
        if (PlayerClass == "WARLOCK" && !string.IsNullOrEmpty(SelectedPet))
        {
            int petSpellId = SelectedPet switch { "Felguard" => 30146, "Felhunter" => 691, "Imp" => 688, "Voidwalker" => 697, "Succubus" => 712, _ => 0 };
            if (petSpellId != 0)
            {
                sb.Append($"if not UnitAffectingCombat('player') then ");
                sb.Append($"if not UnitExists('pet') then WB_PET=nil local n=GetSpellInfo({petSpellId}) if n then CastSpellByName(n) end return end ");
                sb.Append($"if UnitExists('pet') and not WB_PET then WB_PET={petSpellId} end ");
                sb.Append($"if UnitExists('pet') and WB_PET and WB_PET~={petSpellId} then PetDismiss() WB_PET=nil return end ");
                sb.Append($"end ");
            }
        }

        // Призыв пета хантера
        if (PlayerClass == "HUNTER" && selfBuffs.Remove("WB_HUNTER_PET"))
        {
            sb.Append("if not UnitAffectingCombat('player') then ");
            sb.Append("if not UnitExists('pet') then CastSpellByName('Призыв питомца') return end ");
            sb.Append("if UnitIsDead('pet') then CastSpellByName('Воскрешение питомца') return end ");
            sb.Append("end ");
        }

        // ДК: авто-призыв гуля + Костяной щит
        if (PlayerClass == "DEATHKNIGHT")
        {
            if (selfBuffs.Remove("WB_DK_PET"))
            {
                sb.Append("if not UnitExists('pet') or UnitIsDead('pet') then ");
                sb.Append("local n=GetSpellInfo(46584) if n and IsUsableSpell(n) and GetSpellCooldown(n)==0 then CastSpellByName(n) return end end ");
            }
            if (selfBuffs.Remove("WB_BONE_SHIELD"))
            {
                sb.Append("local n=GetSpellInfo(49222) if n and IsUsableSpell(n) then ");
                sb.Append("local has=false for i=1,40 do local bn=UnitBuff('player',i) if not bn then break end if bn==n then has=true break end end ");
                sb.Append("if not has and GetSpellCooldown(n)==0 then CastSpellByName(n) return end end ");
            }
        }

        // Оружие шамана
        var weaponNames = new Dictionary<string, string> { ["FT"] = "Оружие языка пламени", ["EL"] = "Оружие жизни земли", ["WF"] = "Оружие неистовства ветра" };
        selfBuffs.Remove("WB_WEAPON_FT"); selfBuffs.Remove("WB_WEAPON_EL"); selfBuffs.Remove("WB_WEAPON_WF");
        selfBuffs.Remove("WB_WEAPON_MH_FT"); selfBuffs.Remove("WB_WEAPON_MH_EL"); selfBuffs.Remove("WB_WEAPON_MH_WF");
        selfBuffs.Remove("WB_WEAPON_OH_FT"); selfBuffs.Remove("WB_WEAPON_OH_EL"); selfBuffs.Remove("WB_WEAPON_OH_WF");

        string? mhKey = null, ohKey = null;
        if (!string.IsNullOrEmpty(SelectedWeaponMH) && weaponNames.ContainsKey(SelectedWeaponMH)) mhKey = SelectedWeaponMH;
        if (!string.IsNullOrEmpty(SelectedWeaponOH) && weaponNames.ContainsKey(SelectedWeaponOH)) ohKey = SelectedWeaponOH;

        if (mhKey != null || ohKey != null)
        {
            sb.Append("local mhE,_,_,ohE=GetWeaponEnchantInfo() ");
            if (mhKey != null)
            {
                sb.Append($"if not mhE or (WB_WMH and WB_WMH~='{mhKey}') then WB_WMH='{mhKey}' CastSpellByName('{weaponNames[mhKey]}') return end ");
                sb.Append($"if not WB_WMH then WB_WMH='{mhKey}' end ");
            }
            if (ohKey != null)
            {
                sb.Append($"if not ohE or (WB_WOH and WB_WOH~='{ohKey}') then WB_WOH='{ohKey}' CastSpellByName('{weaponNames[ohKey]}') PickupInventoryItem(17) return end ");
                sb.Append($"if not WB_WOH then WB_WOH='{ohKey}' end ");
            }
        }

        // Self-баффы
        foreach (var buff in selfBuffs)
        {
            var s = buff.Replace("'", "\\'");
            sb.Append($"if not HasB('player','{s}') then CastSpellByName('{s}') return end ");
        }

        // Рейд-баффы
        foreach (var buff in raidBuffs)
        {
            var s = buff.Replace("'", "\\'");
            string singleBuff = s;
            if (BuffAliases.TryGetValue(buff, out var alias))
                singleBuff = alias.Replace("'", "\\'");

            string reagent = "", fallback = "";
            bool hasReagent = BuffReagents.TryGetValue(buff, out var reagentInfo);
            if (hasReagent)
            {
                reagent = reagentInfo.reagent.Replace("'", "\\'");
                fallback = reagentInfo.fallback.Replace("'", "\\'");
            }

            sb.Append($"if not HasB('player','{s}') and not HasB('player','{singleBuff}') then ");
            if (hasReagent)
                sb.Append($"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else CastSpellByName('{fallback}') end ");
            else
                sb.Append($"CastSpellByName('{s}') ");
            sb.Append("return end ");

            string castLogic = hasReagent
                ? $"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else TargetUnit(u) CastSpellByName('{fallback}') TargetLastTarget() end"
                : $"TargetUnit(u) CastSpellByName('{s}') TargetLastTarget()";

            sb.Append($"local nr=GetNumRaidMembers() ");
            sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end ");
            sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end end ");
        }

        sb.Append("end WB_Buff()");
        _cachedFullBuffScript = sb.ToString();
        return _cachedFullBuffScript;
    }
}
