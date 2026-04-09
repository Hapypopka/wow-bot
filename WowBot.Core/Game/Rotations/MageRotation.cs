using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class MageRotation : ICombatRotation
{
    public string Name => "Mage (Arcane/Fire/Frost)";
    public string WowClass => "MAGE";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "MAGE";
    public string GetFullScript() => LuaHelpers.WrapDPS("WB_Mag", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("MAGE");

    private const string Body = @"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        if WB_S.AP~=false and IR(12042) then Cast(12042) return end
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.Barrage~=false and HB(44401) and IR(44425) then Cast(44425) return end
        if WB_S.Evoc~=false and MP()<0.35 and IR(12051) then Cast(12051) return end
        if WB_S.AB~=false then Cast(30451) end
    elseif t2>=t1 and t2>=t3 then
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.Combust~=false and IR(11129) then Cast(11129) return end
        if WB_S.LB~=false and not HD('target',44457) then Cast(44457) return end
        if WB_S.Pyro~=false and HB(48108) and IR(11366) then Cast(11366) return end
        if WB_S.Scorch~=false and not HD('target',2948) then Cast(2948) return end
        if WB_S.FB~=false then Cast(133) end
    else
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.DF~=false and IR(44572) then Cast(44572) return end
        if WB_S.IL~=false and HB(44544) and IR(30455) then Cast(30455) return end
        if WB_S.FFB~=false and HB(57761) then Cast(44614) return end
        if WB_S.FBolt~=false then Cast(116) end
    end
";
}
