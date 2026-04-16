using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class WarlockRotation : ICombatRotation
{
    public string Name => "Warlock (Affli/Demo/Destro)";
    public string WowClass => "WARLOCK";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "WARLOCK";
    public string GetFullScript() => LuaHelpers.WrapDPS("WB_Lock", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("WARLOCK");

    private const string Body = @"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- AFFLICTION
        if WB_S.LifeTap~=false and MP()<0.15 then Cast(1454) return end
        if WB_S.Haunt~=false and IR(48181) then Cast(48181) return end
        if WB_S.UA~=false and NR('target',30108) then Cast(30108) return end
        if WB_S.Corruption~=false and NR('target',172) then Cast(172) return end
        if WB_S.CoA==true and IsBoss() and NR('target',980) then Cast(980) return end
        if WB_S.CoE==true and IsBoss() and NR('target',1490) then Cast(1490) return end
        if WB_S.Immolate~=false and NR('target',348) then Cast(348) return end
        if WB_S.DF~=false and IR(30283) then Cast(30283) return end
        if WB_S.LTGlyph==true and not HB(63321) then Cast(1454) return end
        if WB_S.LifeTap~=false and MP()<0.3 then Cast(1454) return end
        if WB_S.ShadowBolt~=false then Cast(686) end
    elseif t2>=t1 and t2>=t3 then
        -- DEMONOLOGY
        local tHP = THP()
        local pHP = PHP()
        local pMP = MP()
        if UnitChannelInfo('player') and pHP<0.25 then SpellStopCasting() return end
        if pHP<0.35 and IR(6789) then Cast(6789) return end
        if pHP<0.6 then for b=0,4 do for s=1,GetContainerNumSlots(b) do local l=GetContainerItemLink(b,s) if l and l:find('амень здоровья') then UseContainerItem(b,s) return end end end end
        if pHP<0.7 and IR(6229) then Cast(6229) return end
        if pMP<0.2 and UnitExists('pet') and UnitPower('pet')>300 and IR(18220) then Cast(18220) return end
        if WB_S.LTGlyph==true and not HasBuffById(63321) and pHP>0.3 then Cast(1454) return end
        if WB_S.LifeTap~=false and pMP<0.15 and pHP>0.3 then Cast(1454) return end
        if UnitThreatSituation('player') and UnitThreatSituation('player')>=3 and IR(29858) then Cast(29858) return end
        if WB_S.Meta~=false and not HasBuffById(47241) and IR(47241) then Cast(47241) return end
        if WB_S.DemonEmpower~=false and UnitExists('pet') and IR(47193) then Cast(47193) return end
        if WB_S.ImmoAura~=false and HasBuffById(47241) and IR(50589) and CheckInteractDistance('target',3) then Cast(50589) return end
        if WB_S.SeedOfC~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) then Cast(27243) return end
        if WB_S.CoA==true and IsBoss() and not HD('target',980) then Cast(980) return end
        if WB_S.CoD==true and IsBoss() and not HD('target',603) then Cast(603) return end
        if WB_S.CoE==true and IsBoss() and not HD('target',1490) then Cast(1490) return end
        if WB_S.Corruption~=false and NR('target',172) and (not WB_CORR or GetTime()-WB_CORR>2) then WB_CORR=GetTime() Cast(172) return end
        if WB_S.Immolate~=false and NR('target',348) and (not WB_IMMO or GetTime()-WB_IMMO>2) then WB_IMMO=GetTime() Cast(348) return end
        if WB_S.SoulFire~=false and HB(63167) and IR(6353) then Cast(6353) return end
        if WB_S.Incinerate~=false and HB(71165) then Cast(29722) return end
        if WB_S.LifeTap~=false and pMP<0.3 and pHP>0.3 then Cast(1454) return end
        if tHP<0.25 and GetItemCount(6265)<10 and IR(1120) then Cast(1120) return end
        if WB_S.ShadowBolt~=false then Cast(686) end
    else
        -- DESTRUCTION
        if WB_S.LifeTap~=false and MP()<0.15 then Cast(1454) return end
        if WB_S.Immolate~=false and not HD('target',348) then Cast(348) return end
        if WB_S.Chaos~=false and IR(50796) then Cast(50796) return end
        if WB_S.Conflag~=false and IR(17962) then Cast(17962) return end
        if WB_S.Corruption~=false and not HD('target',172) then Cast(172) return end
        if WB_S.CoD==true and IsBoss() and not HD('target',603) then Cast(603) return end
        if WB_S.CoE==true and IsBoss() and not HD('target',1490) then Cast(1490) return end
        if WB_S.LTGlyph==true and not HB(63321) then Cast(1454) return end
        if WB_S.LifeTap~=false and MP()<0.3 then Cast(1454) return end
        if WB_S.Incinerate~=false then Cast(29722) end
    end
";
}
