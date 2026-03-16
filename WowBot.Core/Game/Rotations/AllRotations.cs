namespace WowBot.Core.Game.Rotations;

/// <summary>
/// Универсальный скрипт — автодетект класса/спека + ротация
/// Все названия спеллов через GetSpellInfo(id) — работает на любом языке клиента
/// </summary>
public static class AllRotations
{
    /// <summary>
    /// Кэш названий спеллов — инициализируется один раз при первом запуске
    /// </summary>
    private const string SpellCache = @"
if not WB_SPELLS then
    WB_SPELLS = {}
    local ids = {
        -- Druid
        48461,48465,48463,48468,53201,33831,770,24858,29166,22812,61384,
        -- Priest
        48160,48300,48125,48127,48156,34914,15487,47585,34433,
        -- Warlock
        47867,47864,47843,47836,47811,47838,50796,47855,47241,59672,47193,59164,17962,47847,
        -- Mage
        42897,42842,42833,42859,55360,12472,12042,12043,55342,44457,
        -- Shaman
        49238,60043,49271,49231,16166,51533,
    }
    for _,id in ipairs(ids) do
        local name = GetSpellInfo(id)
        if name then WB_SPELLS[id] = name end
    end
end
local S = WB_SPELLS
local function CS(id) if S[id] then CastSpellByName(S[id]) end end
";

    private const string Helpers = @"
local function IsReady(id)
    if not S[id] then return false end
    local s,d = GetSpellCooldown(S[id])
    return s ~= nil and s == 0
end
local function HasDebuff(unit, id)
    if not S[id] then return false end
    for i=1,40 do
        local n = UnitDebuff(unit, i)
        if not n then return false end
        if n == S[id] then return true end
    end
    return false
end
local function HasBuff(id)
    if not S[id] then return false end
    for i=1,40 do
        local n = UnitBuff('player', i)
        if not n then return false end
        if n == S[id] then return true end
    end
    return false
end
local function HasBuffById(id)
    for i=1,40 do
        local n,_,_,_,_,_,_,_,_,_,sId = UnitBuff('player', i)
        if not n then return false end
        if sId == id then return true end
    end
    return false
end
";

    private const string PreChecks = @"
local casting = UnitCastingInfo('player')
local channeling = UnitChannelInfo('player')
if casting or channeling then return end
local gS,gD = GetSpellCooldown(61304)
if gS and gS > 0 and gD and gD <= 1.5 then return end
if UnitIsDeadOrGhost('player') then return end
if not UnitExists('target') then return end
if UnitIsDeadOrGhost('target') then return end
if not UnitCanAttack('player', 'target') then return end
";

    public static string GetFullScript() => @"
local function WB_Run()
" + SpellCache + Helpers + PreChecks + @"

    local _, class = UnitClass('player')
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)

    if class == 'DRUID' and t1 >= t2 and t1 >= t3 then
        -- BALANCE DRUID
        if not HasBuff(24858) then CS(24858) return end
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.3 and IsReady(29166) then CS(29166) return end
        if IsReady(53201) then CS(53201) return end
        if IsReady(33831) then CS(33831) return end
        if not HasDebuff('target',770) then CS(770) return end
        if not HasDebuff('target',48468) then CS(48468) return end
        if not HasDebuff('target',48463) then CS(48463) return end
        if not WB_ECL then WB_ECL=0 end
        if HasBuffById(48518) and WB_ECL~=1 then WB_ECL=1 end
        if HasBuffById(48517) and WB_ECL~=2 then WB_ECL=2 end
        if WB_ECL==1 then CS(48465) else CS(48461) end

    elseif class == 'PRIEST' and t3 >= t1 and t3 >= t2 then
        -- SHADOW PRIEST
        if not HasBuff(15473) then CS(15473) return end
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.15 and IsReady(47585) then CS(47585) return end
        if not HasDebuff('target',48160) then CS(48160) return end
        if not HasDebuff('target',48300) then CS(48300) return end
        if not HasDebuff('target',48125) then CS(48125) return end
        if IsReady(48127) then CS(48127) return end
        if mm>0 and m/mm<0.5 and IsReady(34433) then CS(34433) return end
        CS(48156)

    elseif class == 'WARLOCK' and t1 >= t2 and t1 >= t3 then
        -- AFFLICTION
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.2 then CS(57946) return end
        if not HasDebuff('target',47867) then CS(47867) return end
        if not HasDebuff('target',47864) then CS(47864) return end
        if not HasDebuff('target',47843) then CS(47843) return end
        if IsReady(59164) then CS(59164) return end
        CS(47809)

    elseif class == 'WARLOCK' and t2 >= t1 and t2 >= t3 then
        -- DEMONOLOGY
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.2 then CS(57946) return end
        if IsReady(47241) then CS(47241) return end
        if not HasDebuff('target',47864) then CS(47864) return end
        if not HasDebuff('target',47811) then CS(47811) return end
        if not HasDebuff('target',47867) then CS(47867) return end
        if HasBuff(63167) then CS(47855) return end
        if HasBuff(71165) then CS(47838) return end
        CS(47838)

    elseif class == 'WARLOCK' then
        -- DESTRUCTION
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.2 then CS(57946) return end
        if not HasDebuff('target',47811) then CS(47811) return end
        if HasDebuff('target',47811) and IsReady(17962) then CS(17962) return end
        if IsReady(50796) then CS(50796) return end
        if not HasDebuff('target',47864) then CS(47864) return end
        CS(47838)

    elseif class == 'MAGE' and t2 >= t1 and t2 >= t3 then
        -- FIRE MAGE
        if not HasDebuff('target',55360) then CS(55360) return end
        if HasBuffById(48108) then CS(42891) return end
        if IsReady(55342) then CS(55342) return end
        CS(42833)

    elseif class == 'MAGE' and t1 >= t2 and t1 >= t3 then
        -- ARCANE MAGE
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.2 and IsReady(12051) then CS(12051) return end
        if IsReady(55342) then CS(55342) return end
        if IsReady(12042) then CS(12042) return end
        if IsReady(12043) then CS(12043) return end
        if HasBuffById(44401) then CS(42845) return end
        CS(42897)

    elseif class == 'SHAMAN' and t1 >= t2 and t1 >= t3 then
        -- ELEMENTAL SHAMAN
        if IsReady(16166) then CS(16166) return end
        if not HasDebuff('target',49233) then CS(49233) return end
        if IsReady(60043) then CS(60043) return end
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm>0 and m/mm<0.3 and IsReady(59159) then CS(59159) return end
        if HasBuffById(16246) then CS(49271) return end
        CS(49238)
    end
end
WB_Run()
";

    public static string GetInstantScript() => @"
local function WB_Inst()
" + SpellCache + Helpers + @"
    local casting = UnitCastingInfo('player')
    local channeling = UnitChannelInfo('player')
    if casting or channeling then return end
    local gS,gD = GetSpellCooldown(61304)
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end

    local _, class = UnitClass('player')
    if class == 'DRUID' then
        if IsReady(53201) then CS(53201) return end
        if not HasDebuff('target',770) then CS(770) return end
        if not HasDebuff('target',48468) then CS(48468) return end
        if not HasDebuff('target',48463) then CS(48463) return end
    elseif class == 'PRIEST' then
        if not HasDebuff('target',48300) then CS(48300) return end
        if not HasDebuff('target',48125) then CS(48125) return end
    elseif class == 'WARLOCK' then
        if not HasDebuff('target',47864) then CS(47864) return end
        if not HasDebuff('target',47867) then CS(47867) return end
    elseif class == 'MAGE' then
        if not HasDebuff('target',55360) then CS(55360) return end
    elseif class == 'SHAMAN' then
        if not HasDebuff('target',49233) then CS(49233) return end
    end
end
WB_Inst()
";
}
