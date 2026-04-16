using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class RogueRotation : ICombatRotation
{
    public string Name => "Rogue (Assassination/Combat/Subtlety)";
    public string WowClass => "ROGUE";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "ROGUE";
    public string GetFullScript() => LuaHelpers.WrapDPS("WB_Rog", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("ROGUE");

    private const string Body = @"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local cp = CP()
    if t1>=t2 and t1>=t3 then
        -- ASSASSINATION
        if WB_S.Envenom~=false and cp>=4 and IR(32645) then Cast(32645) return end
        if WB_S.Rupture~=false and cp>=4 and not HD('target',1943) then Cast(1943) return end
        if WB_S.HFB~=false and not HB(51662) and IR(51662) then Cast(51662) return end
        -- AoE: Fan of Knives (35 energy)
        if WB_S.FoK~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and UnitMana('player')>=35 then Cast(51723) return end
        if WB_S.Mutilate~=false then Cast(1329) end
    elseif t2>=t1 and t2>=t3 then
        -- COMBAT
        if WB_S.SnD~=false and cp>=1 and not HB(5171) then Cast(5171) return end
        -- AoE: Blade Flurry на 2+ целях (2 мин CD, 15 сек +20% att speed + cleave), Fan of Knives на 3+
        if WB_S.BladeF~=false and (WB_NCET or 0)>=2 and not HB(13877) and IR(13877) then Cast(13877) return end
        if WB_S.FoK~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and UnitMana('player')>=35 then Cast(51723) return end
        if WB_S.Rupture~=false and cp>=5 and not HD('target',1943) then Cast(1943) return end
        if WB_S.Evis~=false and cp>=5 then Cast(2098) return end
        if WB_S.KS~=false and IR(51690) then Cast(51690) return end
        if WB_S.SS~=false then Cast(1752) end
    else
        -- SUBTLETY
        if WB_S.Evis~=false and cp>=5 then Cast(2098) return end
        if WB_S.Rupture~=false and cp>=5 and not HD('target',1943) then Cast(1943) return end
        -- AoE: Fan of Knives
        if WB_S.FoK~=false and (WB_NCET or 0)>=(WB_AEMIN or 3) and UnitMana('player')>=35 then Cast(51723) return end
        if WB_S.Hemo~=false and not HD('target',16511) then Cast(16511) return end
        if WB_S.BS~=false then Cast(53) end
    end
";
}
