namespace WowBot.Core.Game.Rotations;

public static class BalanceDruidPvE
{
    public const string Name = "Balance Druid PvE";

    /// <summary>
    /// Общие хелперы — вставляются в оба скрипта
    /// </summary>
    private const string Helpers = @"
    local function IsReady(name)
        local s, d = GetSpellCooldown(name)
        return s ~= nil and s == 0
    end
    local function HasDebuff(unit, debuffName)
        for i = 1, 40 do
            local name = UnitDebuff(unit, i)
            if not name then return false end
            if name == debuffName then return true end
        end
        return false
    end
    local function HasBuffById(spellId)
        for i = 1, 40 do
            local name, _, _, _, _, _, _, _, _, _, sId = UnitBuff('player', i)
            if not name then return false end
            if sId == spellId then return true end
        end
        return false
    end
    local function HasBuffByName(buffName)
        for i = 1, 40 do
            local name = UnitBuff('player', i)
            if not name then return false end
            if name == buffName then return true end
        end
        return false
    end
";

    /// <summary>
    /// Мгновенные спеллы — можно кастовать на бегу
    /// Возвращает true (через WB_CASTED=1) если что-то кастанул
    /// </summary>
    public static string GetInstantScript() => @"
local function WB_Instant()
    if UnitIsDeadOrGhost('player') then return false end
    if not UnitExists('target') then return false end
    if UnitIsDeadOrGhost('target') then return false end
    if not UnitCanAttack('player', 'target') then return false end

    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return false end

    local gcdStart, gcdDur = GetSpellCooldown('Гнев')
    if gcdStart and gcdDur and gcdStart > 0 and gcdDur <= 1.5 then return false end

" + Helpers + @"

    -- Форма совуха
    if not HasBuffByName('Облик лунного совуха') then
        CastSpellByName('Облик лунного совуха')
        return true
    end

    -- Озарение если мана < 30%
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana / maxMana) < 0.3 then
        if IsReady('Озарение') then
            CastSpellByName('Озарение')
            return true
        end
    end

    -- Звездопад (instant)
    if IsReady('Звездопад') then
        CastSpellByName('Звездопад')
        return true
    end

    -- Сила Природы (instant)
    if IsReady('Сила Природы') then
        CastSpellByName('Сила Природы')
        return true
    end

    -- Волшебный огонь (instant)
    if not HasDebuff('target', 'Волшебный огонь') then
        CastSpellByName('Волшебный огонь')
        return true
    end

    -- Рой насекомых (instant)
    if not HasDebuff('target', 'Рой насекомых') then
        CastSpellByName('Рой насекомых')
        return true
    end

    -- Лунный огонь (instant)
    if not HasDebuff('target', 'Лунный огонь') then
        CastSpellByName('Лунный огонь')
        return true
    end

    return false
end

WB_DID_INSTANT = WB_Instant()
";

    /// <summary>
    /// Кастовые спеллы — нужна остановка
    /// </summary>
    public static string GetCastScript() => @"
local function WB_Cast()
    if UnitIsDeadOrGhost('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end

    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return end

    local gcdStart, gcdDur = GetSpellCooldown('Гнев')
    if gcdStart and gcdDur and gcdStart > 0 and gcdDur <= 1.5 then return end

" + Helpers + @"

    -- Eclipse логика
    if not WB_ECLIPSE_STATE then WB_ECLIPSE_STATE = 0 end

    local hasLunar = HasBuffById(48518)
    local hasSolar = HasBuffById(48517)

    if hasLunar and WB_ECLIPSE_STATE ~= 1 then
        WB_ECLIPSE_STATE = 1
    end
    if hasSolar and WB_ECLIPSE_STATE ~= 2 then
        WB_ECLIPSE_STATE = 2
    end

    if WB_ECLIPSE_STATE == 1 then
        CastSpellByName('Звездный огонь')
    else
        CastSpellByName('Гнев')
    end
end

WB_Cast()
";

    /// <summary>
    /// Полная ротация (для режима стоя на месте)
    /// </summary>
    public static string GetScript() => GetInstantScript() + "\nif not WB_DID_INSTANT then\n" + GetCastScript() + "\nend";
}
