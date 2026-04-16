using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class HunterRotation : ICombatRotation
{
    public string Name => "Hunter (BM/MM/Survival)";
    public string WowClass => "HUNTER";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "HUNTER";
    public string GetFullScript() => LuaHelpers.WrapFull("WB_Hunt", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("HUNTER");

    private const string Body = @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then if WB_S then if WB_S.Rapid~=false and IR(3045) then Cast(3045) end if WB_S.Kill2~=false and IR(34026) then Cast(34026) end if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end end return end
    if not WB_S then WB_S={} end
    if UnitExists('pet') and not UnitIsDead('pet') then
        if HasBuffById(5384) or UnitIsDeadOrGhost('player') then WB_FD=GetTime() PetPassiveMode() PetFollow() return end
        if WB_FD and GetTime()-WB_FD<5 then PetPassiveMode() PetFollow() return end
    end
    if UnitIsDeadOrGhost('player') then return end
    if UnitExists('pet') and not UnitIsDead('pet') then
        if not UnitAffectingCombat('player') then PetPassiveMode() PetFollow() return end
        if not WB_FD or GetTime()-WB_FD>=5 then if UnitExists('target') and UnitCanAttack('player','target') and not UnitIsDeadOrGhost('target') then PetAttack() end end
    end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
    if WB_S.Track~=false then local ct=UnitCreatureType('target') local tr if ct=='Животное' then tr='Выслеживание животных' elseif ct=='Демон' then tr='Выслеживание демонов' elseif ct=='Дракон' then tr='Выслеживание драконов' elseif ct=='Элементаль' then tr='Выслеживание элементалей' elseif ct=='Великан' then tr='Выслеживание великанов' elseif ct=='Гуманоид' then tr='Выслеживание гуманоидов' elseif ct=='Нежить' then tr='Выслеживание нежити' end if tr and not HasBuff(tr) then CastSpellByName(tr) return end end
    if UnitExists('pet') and not UnitIsDead('pet') and UnitHealth('pet')/UnitHealthMax('pet')<0.75 and IR(136) then Cast(136) end
    if MP()<0.2 and not HB(34074) and IR(34074) then Cast(34074) return end
    if MP()>0.5 and HB(34074) then if IR(61846) then Cast(61846) elseif IR(13165) then Cast(13165) end return end
    if UnitThreatSituation('player') and UnitThreatSituation('player')>=3 and IR(5384) then local tv=UnitName('targettarget') local pn=UnitName('player') if tv and tv~=pn then Cast(5384) return end end
    if not WB_S.Trapper and CheckInteractDistance('target',3) and IR(781) then Cast(781) return end
    if IR(34477) and (not WB_MD or GetTime()-WB_MD>30) then
        local function FindTank()
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and CheckInteractDistance(u,4) then for j=1,40 do local _,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,j) if not id then break end if id==48263 or id==48266 or id==25780 or id==5487 or id==9634 then return u end end end end
            else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and CheckInteractDistance(u,4) then for j=1,40 do local _,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,j) if not id then break end if id==48263 or id==48266 or id==25780 or id==5487 or id==9634 then return u end end end end end
            return nil
        end
        local tank=FindTank()
        if tank then WB_MD=GetTime() CastOn(tank,34477) end
    end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if not HD('target',1130) and WB_S.Mark~=false then Cast(1130) return end
    if WB_S.Kill~=false and THP()<0.2 and IR(53351) then Cast(53351) return end
    if t1>=t2 and t1>=t3 then
        -- BEAST MASTERY
        if WB_S.Bestial~=false and IR(19574) then Cast(19574) return end
        if WB_S.BW~=false and IR(34471) then Cast(34471) return end
        -- AoE: Multi-Shot (такое же условие как у MM)
        if WB_S.MultiShot~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and (GetUnitSpeed('player') or 0)==0 and not UnitCastingInfo('player') and IR(2643) then Cast(2643) return end
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Arcane~=false and IR(3044) then Cast(3044) return end
        if WB_S.Steady~=false then Cast(56641) end
    elseif t2>=t1 and t2>=t3 then
        if WB_S.Dragonhawk~=false and not HB(61847) and MP()>0.3 then Cast(61847) return end
        if WB_S.MultiShot~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and (GetUnitSpeed('player') or 0)==0 and not UnitCastingInfo('player') and IR(2643) then Cast(2643) return end
        if WB_S.Rapid~=false and IR(3045) and THP()>0.5 and (GetUnitSpeed('player') or 0)==0 then Cast(3045) end
        if WB_S.Kill2~=false and IR(34026) then Cast(34026) end
        if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end
        if IR(19801) then for i=1,40 do local n,_,_,_,dt=UnitBuff('target',i) if not n then break end if dt=='Enrage' then Cast(19801) return end end end
        if WB_S.Serpent~=false and not HD('target',1978) and THP()>0.1 then Cast(1978) return end
        if WB_S.Chimera~=false and HD('target',1978) and IR(53209) then Cast(53209) return end
        if WB_S.Silence~=false and IR(34490) then Cast(34490) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Trapper==true and WB_S.Trap~=false and IR(13813) then Cast(13813) return end
        if WB_S.Readiness~=false and IR(23989) and not IR(53209) and not IR(3045) then Cast(23989) return end
        if WB_S.Steady~=false then Cast(56641) end
    else
        -- SURVIVAL
        -- Lock and Load proc (56453) — free Explosive Shot, не тратит cooldown. Приоритет #1.
        if WB_S.Explosive~=false and HB(56453) then Cast(53301) return end
        if WB_S.Explosive~=false and IR(53301) then Cast(53301) return end
        if WB_S.Black~=false and not HD('target',3674) and IR(3674) then Cast(3674) return end
        -- AoE: Multi-Shot при Lock and Load или 3+ врагах
        if WB_S.MultiShot~=false and HB(56453) and (GetUnitSpeed('player') or 0)==0 then Cast(2643) return end
        if WB_S.MultiShot~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and (GetUnitSpeed('player') or 0)==0 and not UnitCastingInfo('player') and IR(2643) then Cast(2643) return end
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Arcane~=false and IR(3044) then Cast(3044) return end
        if WB_S.Steady~=false then Cast(56641) end
    end
";
}
