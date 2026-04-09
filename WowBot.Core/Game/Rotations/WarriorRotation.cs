using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class WarriorRotation : ICombatRotation
{
    public string Name => "Warrior (Arms/Fury/Prot)";
    public string WowClass => "WARRIOR";

    public bool IsMatch(string playerClass, string? specName) =>
        playerClass == "WARRIOR";

    public string GetFullScript() => LuaHelpers.WrapDPS("WB_War", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("WARRIOR");

    private const string Body = @"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- ARMS
        if WB_S.Reck~=false and IR(1719) then Cast(1719) return end
        if WB_S.Execute~=false and THP()<0.2 and IR(5308) then Cast(5308) return end
        if WB_S.Rend~=false and not HD('target',772) then Cast(772) return end
        if WB_S.MS~=false and IR(12294) then Cast(12294) return end
        if WB_S.OP~=false and IR(7384) then Cast(7384) return end
        if WB_S.Slam~=false and HB(46916) then Cast(1464) return end
        if WB_S.Cleave~=false then Cast(845) return end
        Cast(47449)
    elseif t2>=t1 and t2>=t3 then
        -- FURY
        if WB_S.Reck~=false and IR(1719) then Cast(1719) return end
        if WB_S.Execute~=false and THP()<0.2 and IR(5308) then Cast(5308) return end
        if WB_S.BT~=false and IR(23881) then Cast(23881) return end
        if WB_S.WW~=false and IR(1680) then Cast(1680) return end
        if WB_S.Slam~=false and HB(46916) then Cast(1464) return end
        Cast(47449)
    else
        -- PROT
        if TryTaunt(355) then return end
        if TryDefCD(871,true, 12975,true, 2565,true) then return end
        if WB_S.AoEThreat~=false and (WB_NCE or 0)>=2 and IR(6343) then Cast(6343) return end
        if WB_S.HS~=false and UnitMana('player')>50 and IR(47449) then Cast(47449) end
        if WB_S.BR==true and UnitMana('player')<30 and IR(2687) then Cast(2687) return end
        if WB_S.ShieldSlam~=false and IR(23922) then Cast(23922) return end
        if WB_S.Revenge~=false then local rn=SN(6572) if rn then local u,_=IsUsableSpell(rn) if u then CastSpellByName(rn) return end end end
        if WB_S.TC~=false and IR(6343) then Cast(6343) return end
        if WB_S.ShockW~=false and IR(46968) then Cast(46968) return end
        if WB_S.Devastate~=false then Cast(20243) return end
    end
";
}
