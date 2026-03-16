namespace WowBot.Core.Game.Rotations;

/// <summary>
/// Универсальный скрипт — автодетект класса/спека + ротация
/// Все ротации в одном Lua-скрипте
/// </summary>
public static class AllRotations
{
    private const string Helpers = @"
    local function IsReady(name)
        local s, d = GetSpellCooldown(name)
        return s ~= nil and s == 0
    end
    local function HasDebuff(unit, name)
        for i = 1, 40 do
            local n = UnitDebuff(unit, i)
            if not n then return false end
            if n == name then return true end
        end
        return false
    end
    local function HasBuff(name)
        for i = 1, 40 do
            local n = UnitBuff('player', i)
            if not n then return false end
            if n == name then return true end
        end
        return false
    end
    local function HasBuffById(id)
        for i = 1, 40 do
            local n,_,_,_,_,_,_,_,_,_,sId = UnitBuff('player', i)
            if not n then return false end
            if sId == id then return true end
        end
        return false
    end
    local function GCD()
        local s,d = GetSpellCooldown(61304)
        return s and s > 0 and d and d <= 1.5
    end
    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return end
    if GCD() then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
";

    public static string GetFullScript() => @"
local function WB_AutoRotation()
" + Helpers + @"

    -- Автодетект класса и спека
    local _, class = UnitClass('player')
    local _, _, t1 = GetTalentTabInfo(1)
    local _, _, t2 = GetTalentTabInfo(2)
    local _, _, t3 = GetTalentTabInfo(3)

    if class == 'DRUID' then
        if t1 >= t2 and t1 >= t3 then WB_BalanceDruid()
        end
    elseif class == 'PRIEST' then
        if t3 >= t1 and t3 >= t2 then WB_ShadowPriest() end
    elseif class == 'WARLOCK' then
        if t1 >= t2 and t1 >= t3 then WB_AfflictionLock()
        elseif t2 >= t1 and t2 >= t3 then WB_DemonologyLock()
        else WB_DestructionLock() end
    elseif class == 'MAGE' then
        if t1 >= t2 and t1 >= t3 then WB_ArcaneMage()
        elseif t2 >= t1 and t2 >= t3 then WB_FireMage()
        end
    elseif class == 'SHAMAN' then
        if t1 >= t2 and t1 >= t3 then WB_ElementalShaman() end
    end
end

-- ============================================
-- BALANCE DRUID
-- ============================================
function WB_BalanceDruid()
    if not HasBuff('Облик лунного совуха') then
        CastSpellByName('Облик лунного совуха') return end

    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.3 and IsReady('Озарение') then
        CastSpellByName('Озарение') return end

    if IsReady('Звездопад') then CastSpellByName('Звездопад') return end
    if IsReady('Сила Природы') then CastSpellByName('Сила Природы') return end
    if not HasDebuff('target','Волшебный огонь') then CastSpellByName('Волшебный огонь') return end
    if not HasDebuff('target','Рой насекомых') then CastSpellByName('Рой насекомых') return end
    if not HasDebuff('target','Лунный огонь') then CastSpellByName('Лунный огонь') return end

    if not WB_ECLIPSE_STATE then WB_ECLIPSE_STATE = 0 end
    if HasBuffById(48518) and WB_ECLIPSE_STATE ~= 1 then WB_ECLIPSE_STATE = 1 end
    if HasBuffById(48517) and WB_ECLIPSE_STATE ~= 2 then WB_ECLIPSE_STATE = 2 end

    if WB_ECLIPSE_STATE == 1 then CastSpellByName('Звездный огонь')
    else CastSpellByName('Гнев') end
end

-- ============================================
-- SHADOW PRIEST
-- Приоритет: VT > DP > SW:P > MB > MF filler
-- ============================================
function WB_ShadowPriest()
    -- Shadowform
    if not HasBuff('Облик Тени') and not HasBuff('Темная форма') then
        CastSpellByName('Облик Тени') return end

    -- Mana: Dispersia
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.15 and IsReady('Рассеивание') then
        CastSpellByName('Рассеивание') return end

    -- Vampiric Touch (DoT, cast)
    if not HasDebuff('target','Прикосновение вампира') then
        CastSpellByName('Прикосновение вампира') return end

    -- Devouring Plague (DoT, instant)
    if not HasDebuff('target','Всепожирающая чума') then
        CastSpellByName('Всепожирающая чума') return end

    -- Shadow Word: Pain (DoT, instant)
    if not HasDebuff('target','Слово Тени: Боль') then
        CastSpellByName('Слово Тени: Боль') return end

    -- Mind Blast (CD)
    if IsReady('Взрыв разума') then
        CastSpellByName('Взрыв разума') return end

    -- Shadowfiend (mana < 50%)
    if maxMana > 0 and (mana/maxMana) < 0.5 and IsReady('Демон тени') then
        CastSpellByName('Демон тени') return end

    -- Mind Flay (filler, channel)
    CastSpellByName('Пытка разума')
end

-- ============================================
-- AFFLICTION WARLOCK
-- Приоритет: CoA > Corruption > UA > Haunt > SB filler
-- ============================================
function WB_AfflictionLock()
    -- Life Tap if mana < 20%
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.2 then
        CastSpellByName('Жизнеотвод') return end

    -- Curse of Agony
    if not HasDebuff('target','Проклятие агонии') then
        CastSpellByName('Проклятие агонии') return end

    -- Corruption
    if not HasDebuff('target','Порча') then
        CastSpellByName('Порча') return end

    -- Unstable Affliction (cast)
    if not HasDebuff('target','Нестабильная порча') then
        CastSpellByName('Нестабильная порча') return end

    -- Haunt (CD)
    if IsReady('Преследование') then
        CastSpellByName('Преследование') return end

    -- Shadow Bolt (filler)
    CastSpellByName('Стрела Тьмы')
end

-- ============================================
-- DEMONOLOGY WARLOCK
-- Приоритет: Immolate > Corruption > CoD > Incinerate > SB
-- ============================================
function WB_DemonologyLock()
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.2 then
        CastSpellByName('Жизнеотвод') return end

    -- Metamorphosis
    if IsReady('Метаморфоза') then
        CastSpellByName('Метаморфоза') return end

    -- Immolation Aura (in Meta)
    if HasBuff('Метаморфоза') and IsReady('Аура ожога') then
        CastSpellByName('Аура ожога') return end

    -- Corruption
    if not HasDebuff('target','Порча') then
        CastSpellByName('Порча') return end

    -- Immolate
    if not HasDebuff('target','Жертвенный огонь') then
        CastSpellByName('Жертвенный огонь') return end

    -- Curse of Doom
    if not HasDebuff('target','Проклятие рока') then
        CastSpellByName('Проклятие рока') return end

    -- Decimation proc: Soul Fire
    if HasBuff('Уничтожение') then
        CastSpellByName('Ожог души') return end

    -- Molten Core proc: Incinerate
    if HasBuff('Расплавленная сердцевина') then
        CastSpellByName('Испепеление') return end

    -- Incinerate (filler)
    CastSpellByName('Испепеление')
end

-- ============================================
-- DESTRUCTION WARLOCK
-- Приоритет: Immolate > Conflagrate > CoD > Chaos Bolt > Incinerate
-- ============================================
function WB_DestructionLock()
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.2 then
        CastSpellByName('Жизнеотвод') return end

    -- Immolate
    if not HasDebuff('target','Жертвенный огонь') then
        CastSpellByName('Жертвенный огонь') return end

    -- Conflagrate (CD, requires Immolate)
    if HasDebuff('target','Жертвенный огонь') and IsReady('Поджигание') then
        CastSpellByName('Поджигание') return end

    -- Curse of Doom
    if not HasDebuff('target','Проклятие рока') then
        CastSpellByName('Проклятие рока') return end

    -- Chaos Bolt (CD)
    if IsReady('Стрела Хаоса') then
        CastSpellByName('Стрела Хаоса') return end

    -- Corruption (if Backdraft/Empowered Imp)
    if not HasDebuff('target','Порча') then
        CastSpellByName('Порча') return end

    -- Incinerate (filler)
    CastSpellByName('Испепеление')
end

-- ============================================
-- FIRE MAGE
-- Приоритет: Living Bomb > Pyroblast (HotStreak) > Fireball
-- ============================================
function WB_FireMage()
    -- Living Bomb
    if not HasDebuff('target','Живая бомба') then
        CastSpellByName('Живая бомба') return end

    -- Hot Streak proc → Pyroblast (instant)
    if HasBuff('Серия удач') then
        CastSpellByName('Стрела огня') return end

    -- Mirror Image (CD)
    if IsReady('Зеркальное изображение') then
        CastSpellByName('Зеркальное изображение') return end

    -- Combustion (CD)
    if IsReady('Возгорание') then
        CastSpellByName('Возгорание') return end

    -- Fireball (filler)
    CastSpellByName('Огненный шар')
end

-- ============================================
-- ARCANE MAGE
-- Приоритет: AB stack to 4 > AM proc > AB
-- ============================================
function WB_ArcaneMage()
    -- Mana: Evocation
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.2 and IsReady('Прилив сил') then
        CastSpellByName('Прилив сил') return end

    -- Mirror Image (CD)
    if IsReady('Зеркальное изображение') then
        CastSpellByName('Зеркальное изображение') return end

    -- Arcane Power (CD)
    if IsReady('Мощь тайной магии') then
        CastSpellByName('Мощь тайной магии') return end

    -- Presence of Mind (CD) + Arcane Blast
    if IsReady('Концентрация') then
        CastSpellByName('Концентрация') return end

    -- Missile Barrage proc → Arcane Missiles
    if HasBuff('Шквал снарядов') then
        CastSpellByName('Чародейские стрелы') return end

    -- Arcane Blast (main filler, stacks debuff)
    CastSpellByName('Чародейский взрыв')
end

-- ============================================
-- ELEMENTAL SHAMAN
-- Приоритет: FS > LvB > CL (if proc) > LB
-- ============================================
function WB_ElementalShaman()
    -- Elemental Mastery (CD)
    if IsReady('Покорение стихий') then
        CastSpellByName('Покорение стихий') return end

    -- Flame Shock (DoT)
    if not HasDebuff('target','Огненный шок') then
        CastSpellByName('Огненный шок') return end

    -- Lava Burst (CD, always crits with FS)
    if IsReady('Выброс лавы') then
        CastSpellByName('Выброс лавы') return end

    -- Thunderstorm (mana regen, if low)
    local mana = UnitMana('player')
    local maxMana = UnitManaMax('player')
    if maxMana > 0 and (mana/maxMana) < 0.3 and IsReady('Гроза') then
        CastSpellByName('Гроза') return end

    -- Chain Lightning (if Clearcasting proc)
    if HasBuff('Просветление') then
        CastSpellByName('Цепная молния') return end

    -- Lightning Bolt (filler)
    CastSpellByName('Молния')
end

WB_AutoRotation()
";

    public static string GetInstantScript() => @"
local function WB_AutoInstant()
    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return end

    local s,d = GetSpellCooldown(61304)
    if s and s > 0 and d and d <= 1.5 then return end

    if UnitIsDeadOrGhost('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end

    local function IsReady(name)
        local s,d = GetSpellCooldown(name)
        return s ~= nil and s == 0
    end
    local function HasDebuff(unit, name)
        for i=1,40 do
            local n = UnitDebuff(unit, i)
            if not n then return false end
            if n == name then return true end
        end
        return false
    end

    local _, class = UnitClass('player')

    -- Instant spells по классу
    if class == 'DRUID' then
        if IsReady('Звездопад') then CastSpellByName('Звездопад') return end
        if not HasDebuff('target','Волшебный огонь') then CastSpellByName('Волшебный огонь') return end
        if not HasDebuff('target','Рой насекомых') then CastSpellByName('Рой насекомых') return end
        if not HasDebuff('target','Лунный огонь') then CastSpellByName('Лунный огонь') return end
    elseif class == 'PRIEST' then
        if not HasDebuff('target','Всепожирающая чума') then CastSpellByName('Всепожирающая чума') return end
        if not HasDebuff('target','Слово Тени: Боль') then CastSpellByName('Слово Тени: Боль') return end
    elseif class == 'WARLOCK' then
        if not HasDebuff('target','Порча') then CastSpellByName('Порча') return end
        if not HasDebuff('target','Проклятие агонии') and not HasDebuff('target','Проклятие рока') then
            CastSpellByName('Проклятие агонии') return end
    elseif class == 'MAGE' then
        if not HasDebuff('target','Живая бомба') then CastSpellByName('Живая бомба') return end
    elseif class == 'SHAMAN' then
        if not HasDebuff('target','Огненный шок') then CastSpellByName('Огненный шок') return end
    end
end
WB_AutoInstant()
";
}
