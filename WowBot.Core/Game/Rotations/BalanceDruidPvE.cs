namespace WowBot.Core.Game.Rotations;

public static class BalanceDruidPvE
{
    public const string Name = "Balance Druid PvE";

    public static string GetScript() => @"
local function WB_Rotation()
    if UnitIsDeadOrGhost('player') then return end

    -- Если кастуем/канал — стоим, ждем
    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return end

    -- Нет таргета или мертв
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end

    -- GCD проверка
    local gcdStart, gcdDur = GetSpellCooldown('Гнев')
    if gcdStart and gcdDur and gcdStart > 0 and gcdDur <= 1.5 then return end

    -- Хелперы
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

    -- 0. Форма совуха
    if not HasBuffByName('Облик лунного совуха') then
        CastSpellByName('Облик лунного совуха')
        return
    end

    -- 1. Озарение если мана < 30%
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana / maxMana) < 0.3 then
        if IsReady('Озарение') then
            CastSpellByName('Озарение')
            return
        end
    end

    -- 2. Звездопад по КД
    if IsReady('Звездопад') then
        CastSpellByName('Звездопад')
        return
    end

    -- 3. Сила Природы по КД
    if IsReady('Сила Природы') then
        CastSpellByName('Сила Природы')
        return
    end

    -- 4. Волшебный огонь
    if not HasDebuff('target', 'Волшебный огонь') then
        CastSpellByName('Волшебный огонь')
        return
    end

    -- 5. Рой насекомых
    if not HasDebuff('target', 'Рой насекомых') then
        CastSpellByName('Рой насекомых')
        return
    end

    -- 6. Лунный огонь
    if not HasDebuff('target', 'Лунный огонь') then
        CastSpellByName('Лунный огонь')
        return
    end

    -- 7. Eclipse
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

WB_Rotation()
";
}
