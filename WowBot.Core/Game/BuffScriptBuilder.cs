namespace WowBot.Core.Game;

/// <summary>
/// Генерация Lua-скриптов для баффов: аура, печать, благословение, стойки, тотемы и т.д.
/// Извлечено из BotEngine для уменьшения размера класса.
/// </summary>
public class BuffScriptBuilder
{
    // --- Свойства, устанавливаемые из UI (OverlayWindow -> BotEngine -> сюда) ---
    public string PlayerClass { get; set; } = "";
    public string SelectedSeal { get; set; } = "";
    public string SelectedBlessing { get; set; } = "BoM";
    public string SelectedAura { get; set; } = "AuRet";
    public string SelectedShout { get; set; } = "";
    public string SelectedStance { get; set; } = "";
    public string SelectedPresence { get; set; } = "";
    public string SelectedFeralForm { get; set; } = "";
    public string SelectedTotemEarth { get; set; } = "";
    public string SelectedTotemFire { get; set; } = "";
    public string SelectedTotemWater { get; set; } = "";
    public string SelectedTotemAir { get; set; } = "";
    public bool AoeSealSwap { get; set; } = false;
    public bool AoeEnabled { get; set; }
    public List<string> EnabledBuffs { get; set; } = new();

    // --- Статические словари ---

    // Баффы которые кастуются на группу/рейд (через TargetUnit)
    private static readonly HashSet<string> RaidBuffs = new()
    {
        "Молитва стойкости", "Молитва духа", "Молитва защиты от темной магии",
        "Дар дикой природы", "Чародейская гениальность",
    };

    // Баффы-аналоги: рейдовый бафф покрывает одиночный (проверяем оба)
    private static readonly Dictionary<string, string> BuffAliases = new()
    {
        { "Молитва стойкости", "Слово силы: Стойкость" },
        { "Молитва духа", "Божественный дух" },
        { "Молитва защиты от темной магии", "Защита от темной магии" },
        { "Дар дикой природы", "Знак дикой природы" },
        { "Чародейская гениальность", "Чародейский интеллект" },
    };

    // Реагенты для рейд-баффов: prayer -> (reagent, fallback single-target spell)
    private static readonly Dictionary<string, (string reagent, string fallback)> BuffReagents = new()
    {
        { "Молитва стойкости", ("Свеча благочестия", "Слово силы: Стойкость") },
        { "Молитва духа", ("Свеча благочестия", "Божественный дух") },
        { "Молитва защиты от темной магии", ("Свеча благочестия", "Защита от темной магии") },
        { "Дар дикой природы", ("Дикий шиполист", "Знак дикой природы") },
        { "Чародейская гениальность", ("Чародейский порошок", "Чародейский интеллект") },
    };

    /// <summary>Быстрая проверка только классовых баффов (стойка/форма/власть/аура/печать) -- каждые 0.5 сек</summary>
    public string BuildClassBuffScript(int combatEnemyCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_CB() ");
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
                sb.Append($"if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') end ");
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

        if (!hasAnything) return "";
        sb.Append("end WB_CB()");
        return sb.ToString();
    }

    /// <summary>Полная проверка всех баффов (аура/печать/благословение/стойка/формы/тотемы/self/рейд)</summary>
    public string BuildBuffScript()
    {
        // Аура/печать/благословение кастуются даже если нет обычных баффов
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

        // Все в одну строку -- многострочный Lua через AppendLine триггерит taint WeakAuras
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_Buff() ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");
        sb.Append("if UnitIsDeadOrGhost('player') then return end ");
        sb.Append("local function HasB(unit,name) for i=1,40 do local n=UnitBuff(unit,i) if not n then return false end if n==name then return true end end return false end ");

        // Аура паладина (только для PALADIN)
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
                var au = auraSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{au}') then CastSpellByName('{au}') return end ");
            }
        }

        // Печать паладина (только для PALADIN)
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedSeal))
        {
            // AoE свап: SoV<->SoC при 2+ врагах (только для рет/прот)
            if (AoeEnabled && AoeSealSwap && (SelectedSeal == "SoV" || SelectedSeal == "SoC"))
            {
                sb.Append("if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') return end ");
                sb.Append("else if not HasB('player','Печать мщения') then CastSpellByName('Печать мщения') return end end ");
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
                {
                    var ss = sealSpell.Replace("'", "\\'");
                    sb.Append($"if not HasB('player','{ss}') then CastSpellByName('{ss}') return end ");
                }
            }
        }

        // Благословение паладина (только для PALADIN) -- проверяем пати/рейд
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
                // Сначала себя (TargetUnit('player') чтобы Великое благословение бафнуло свой класс)
                sb.Append($"if not HasB('player','{bs}') and not HasB('player','{gs}') then TargetUnit('player') if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end ");
                // Потом пати/рейд в радиусе 30 ярдов
                sb.Append($"local nr=GetNumRaidMembers() ");
                sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end ");
                sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end end ");
            }
        }

        // Крик воина (только для WARRIOR)
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedShout))
        {
            string shoutSpell = SelectedShout switch
            {
                "Battle" => "Боевой крик",
                "Commanding" => "Командирский крик",
                _ => ""
            };
            if (!string.IsNullOrEmpty(shoutSpell))
            {
                var sh = shoutSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{sh}') then CastSpellByName('{sh}') return end ");
            }
        }

        // Стойка воина (через GetShapeshiftForm: 1=боевая, 2=защитная, 3=берсерк)
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
                var st = stanceSpell.Replace("'", "\\'");
                Logger.Info($"BuildBuff: stance={SelectedStance} form={stanceForm} spell={stanceSpell}");
                sb.Append($"if GetShapeshiftForm()~={stanceForm} then CastSpellByName('{st}') return end ");
            }
        }

        // Власть ДК (через GetShapeshiftForm: 1=кровь, 2=лед, 3=нечестивость)
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
                var pr = presSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={presForm} then CastSpellByName('{pr}') return end ");
            }
        }

        // Форма ферал друида (через GetShapeshiftForm: 1=медведь, 3=кот)
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
                var fs = formSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={formId} then CastSpellByName('{fs}') return end ");
            }
        }

        // Тотемы шамана -- SetMultiCastSpell + Зов Духов
        if (PlayerClass == "SHAMAN")
        {
            // Spell ID для каждого тотема (max rank 3.3.5a)
            var earthIds = new Dictionary<string, int>
            {
                ["Stoneskin"] = 58753, ["SoE"] = 58643, ["Tremor"] = 8143,
            };
            var fireIds = new Dictionary<string, int>
            {
                ["Flametongue"] = 58656, ["FrostRes"] = 58745,
            };
            var waterIds = new Dictionary<string, int>
            {
                ["ManaSpring"] = 58774, ["HealStream"] = 58757, ["Cleansing"] = 8170, ["FireRes"] = 58739,
            };
            var airIds = new Dictionary<string, int>
            {
                ["WrathOfAir"] = 3738, ["Windfury"] = 8512, ["NatureRes"] = 58749,
            };

            // Слоты Зова Духов: 141=fire, 142=earth, 143=water, 144=air
            Logger.Info($"Totems: fire={SelectedTotemFire} earth={SelectedTotemEarth} water={SelectedTotemWater} air={SelectedTotemAir}");
            bool hasAny = false;
            if (!string.IsNullOrEmpty(SelectedTotemFire) && fireIds.TryGetValue(SelectedTotemFire, out int fireId))
            { sb.Append($"SetMultiCastSpell(141,{fireId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemEarth) && earthIds.TryGetValue(SelectedTotemEarth, out int earthId))
            { sb.Append($"SetMultiCastSpell(142,{earthId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemWater) && waterIds.TryGetValue(SelectedTotemWater, out int waterId))
            { sb.Append($"SetMultiCastSpell(143,{waterId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemAir) && airIds.TryGetValue(SelectedTotemAir, out int airId))
            { sb.Append($"SetMultiCastSpell(144,{airId}) "); hasAny = true; }

        }

        // Аспект охотника (авто-переключение по мане: дракондор/гадюка)
        if (PlayerClass == "HUNTER")
        {
            bool hasDragon = selfBuffs.Remove("Дух дракондора");
            bool hasViper = selfBuffs.Remove("Дух гадюки");
            if (hasDragon)
            {
                // Дракондор включен -> авто-переключение на гадюку для реген маны
                // Вне боя: гадюка если мана <100%, дракондор если мана полная
                // В бою: гадюка при мане <30%, дракондор при мане >80% (гистерезис)
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

        // Камень чар (проверка чары на оружии, создание + применение)
        if (selfBuffs.Remove("WB_SPELLSTONE"))
        {
            sb.Append("local hasEnch=GetWeaponEnchantInfo() ");
            sb.Append("if not hasEnch then ");
            sb.Append("if GetItemCount('Могучий камень чар')>0 then UseItemByName('Могучий камень чар') PickupInventoryItem(16) return end ");
            sb.Append("CastSpellByName('Создание камня чар') return end ");
        }


        // Оружие шамана (проверка через GetWeaponEnchantInfo + смена выбора)
        string? shamanWeapon = null;
        string? shamanWeaponKey = null;
        if (selfBuffs.Remove("WB_WEAPON_FT")) { shamanWeapon = "Оружие языка пламени"; shamanWeaponKey = "FT"; }
        if (selfBuffs.Remove("WB_WEAPON_EL")) { shamanWeapon = "Оружие жизни земли"; shamanWeaponKey = "EL"; }
        if (selfBuffs.Remove("WB_WEAPON_WF")) { shamanWeapon = "Оружие неистовства ветра"; shamanWeaponKey = "WF"; }
        if (shamanWeapon != null)
        {
            sb.Append($"local hasEnch=GetWeaponEnchantInfo() ");
            sb.Append($"if not hasEnch or (WB_WEAPON_LAST and WB_WEAPON_LAST~='{shamanWeaponKey}') then WB_WEAPON_LAST='{shamanWeaponKey}' CastSpellByName('{shamanWeapon}') return end ");
            sb.Append($"if not WB_WEAPON_LAST then WB_WEAPON_LAST='{shamanWeaponKey}' end ");
        }

        // Self-баффы
        foreach (var buff in selfBuffs)
        {
            var s = buff.Replace("'", "\\'");
            sb.Append($"if not HasB('player','{s}') then CastSpellByName('{s}') return end ");
        }

        // Рейд-баффы: проверяем себя + пати/рейд
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

            // Проверить: нужен ли кому бафф? Ищем первого без бафа
            // Себя
            sb.Append($"if not HasB('player','{s}') and not HasB('player','{singleBuff}') then ");
            if (hasReagent)
                sb.Append($"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else CastSpellByName('{fallback}') end ");
            else
                sb.Append($"CastSpellByName('{s}') ");
            sb.Append("return end ");

            // Пати/рейд
            string castLogic = hasReagent
                ? $"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else TargetUnit(u) CastSpellByName('{fallback}') TargetLastTarget() end"
                : $"TargetUnit(u) CastSpellByName('{s}') TargetLastTarget()";

            sb.Append($"local nr=GetNumRaidMembers() ");
            sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end ");
            sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end end ");
        }

        sb.Append("end WB_Buff()");
        return sb.ToString();
    }

    /// <summary>Проверяет, есть ли хоть что-то для баффов (для условий в BotEngine.Tick)</summary>
    public bool HasAnyBuffSettings()
    {
        return EnabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) ||
               !string.IsNullOrEmpty(SelectedBlessing) || !string.IsNullOrEmpty(SelectedAura) ||
               !string.IsNullOrEmpty(SelectedShout) || !string.IsNullOrEmpty(SelectedStance) ||
               !string.IsNullOrEmpty(SelectedPresence) || !string.IsNullOrEmpty(SelectedFeralForm) ||
               !string.IsNullOrEmpty(SelectedTotemEarth) || !string.IsNullOrEmpty(SelectedTotemFire) ||
               !string.IsNullOrEmpty(SelectedTotemWater) || !string.IsNullOrEmpty(SelectedTotemAir);
    }
}
