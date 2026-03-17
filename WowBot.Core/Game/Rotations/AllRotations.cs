namespace WowBot.Core.Game.Rotations;

public static class AllRotations
{
    private const string Helpers = @"
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
    local function HasBuff(name)
        for i=1,40 do
            local n = UnitBuff('player', i)
            if not n then return false end
            if n == name then return true end
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
    local function MP()
        local m,mm = UnitMana('player'),UnitManaMax('player')
        if mm == 0 then return 1 end
        return m/mm
    end
";

    private const string PreChecks = @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    local gS,gD = GetSpellCooldown('Прикосновение вампира')
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
";

    public static string GetFullScript() => @"
local function WB_Run()
" + Helpers + PreChecks + @"
    local _, class = UnitClass('player')
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)

    if class == 'DRUID' and t1 >= t2 and t1 >= t3 then
        if not WB_S then WB_S={} end
        if WB_S.Moonkin~=false and not HasBuff('Облик лунного совуха') then CastSpellByName('Облик лунного совуха') return end
        if WB_S.Innervate~=false and MP() < 0.3 and IsReady('Озарение') then CastSpellByName('Озарение') return end
        if WB_S.Starfall~=false and IsReady('Звездопад') then CastSpellByName('Звездопад') return end
        if WB_S.Treants~=false and IsReady('Сила Природы') then CastSpellByName('Сила Природы') return end
        if WB_S.FF~=false and not HasDebuff('target','Волшебный огонь') then CastSpellByName('Волшебный огонь') return end
        if WB_S.IS~=false and not HasDebuff('target','Рой насекомых') then CastSpellByName('Рой насекомых') return end
        if WB_S.MF_d~=false and not HasDebuff('target','Лунный огонь') then CastSpellByName('Лунный огонь') return end
        if not WB_ECL then WB_ECL=0 end
        if HasBuffById(48518) and WB_ECL~=1 then WB_ECL=1 end
        if HasBuffById(48517) and WB_ECL~=2 then WB_ECL=2 end
        if WB_ECL==1 and WB_S.Starfire~=false then CastSpellByName('Звездный огонь')
        elseif WB_S.Wrath~=false then CastSpellByName('Гнев') end

    elseif class == 'PRIEST' and t3 >= t1 and t3 >= t2 then
        if not WB_S then WB_S={} end
        if not HasBuff('Облик Тьмы') then CastSpellByName('Облик Тьмы') return end
        if WB_S.Disp~=false and MP() < 0.15 and IsReady('Слияние с Тьмой') then CastSpellByName('Слияние с Тьмой') return end
        if WB_S.VT~=false and not HasDebuff('target','Прикосновение вампира') then if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') return end end
        if WB_S.DP~=false and not HasDebuff('target','Всепожирающая чума') then if not WB_DP or GetTime()-WB_DP>2 then WB_DP=GetTime() CastSpellByName('Всепожирающая чума') return end end
        if WB_S.SWP~=false and not HasDebuff('target','Слово Тьмы: Боль') then if not WB_SWP or GetTime()-WB_SWP>2 then WB_SWP=GetTime() CastSpellByName('Слово Тьмы: Боль') return end end
        local _,_,_,_,mbPts = GetTalentInfo(3,8)
        if WB_S.MB~=false and mbPts and mbPts > 0 and IsReady('Взрыв разума') then CastSpellByName('Взрыв разума') return end
        if WB_S.SF~=false and MP() < 0.5 and IsReady('Исчадие Тьмы') then CastSpellByName('Исчадие Тьмы') return end
        if WB_S.MF~=false then CastSpellByName('Пытка разума') end
    end
end
WB_Run()
";

    public static string GetInstantScript() => @"
local function WB_Inst()
" + Helpers + @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    local gS,gD = GetSpellCooldown('Прикосновение вампира')
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end

    local _, class = UnitClass('player')
    if class == 'DRUID' then
        if IsReady('Звездопад') then CastSpellByName('Звездопад') return end
        if not HasDebuff('target','Волшебный огонь') then CastSpellByName('Волшебный огонь') return end
        if not HasDebuff('target','Рой насекомых') then CastSpellByName('Рой насекомых') return end
        if not HasDebuff('target','Лунный огонь') then CastSpellByName('Лунный огонь') return end
    elseif class == 'PRIEST' then
        if not HasDebuff('target','Всепожирающая чума') then CastSpellByName('Всепожирающая чума') return end
        if not HasDebuff('target','Слово Тьмы: Боль') then CastSpellByName('Слово Тьмы: Боль') return end
    end
end
WB_Inst()
";
}
