using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class DeathKnightRotation : ICombatRotation
{
    public string Name => "Death Knight (Blood/Frost/Unholy)";
    public string WowClass => "DEATHKNIGHT";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "DEATHKNIGHT";
    public string GetFullScript() => LuaHelpers.WrapDPS("WB_DK", Body);
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("DEATHKNIGHT");

    private const string Body = @"
    -- Управление гулем
    if UnitExists('pet') and not UnitIsDead('pet') then
        local inCombat = UnitAffectingCombat('player')
        if inCombat then
            if not WB_DK_DEF then PetDefensiveMode() WB_DK_DEF=true end
            if UnitExists('target') and UnitCanAttack('player','target') and not UnitIsDeadOrGhost('target') then PetAttack() end
            if not WB_DK_AC then
                local n,_,_,_,_,ac=GetPetActionInfo(4) if n and not ac then TogglePetAutocast(4) end
                WB_DK_AC=true
            end
        else
            if WB_DK_DEF then PetPassiveMode() PetFollow() WB_DK_DEF=nil end
        end
    end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local hasFF = HD('target',55095)
    local hasBP = HD('target',55078)
    if t1>=t2 and t1>=t3 then
        -- BLOOD (tank)
        if TryTaunt(56222) then return end
        if TryDefCD(48792,true, 55233,true, 49222,true) then return end
        if WB_S.AoEThreat~=false and (WB_NCE or 0)>=2 and IR(43265) then Cast(43265) return end
        if WB_S.IT~=false and not hasFF then Cast(45477) return end
        if WB_S.PS~=false and not hasBP then Cast(45462) return end
        if WB_S.Pest~=false and hasFF and hasBP and IR(50842) then Cast(50842) return end
        if WB_S.VB~=false and PHP()<0.5 and IR(55233) then Cast(55233) return end
        if WB_S.DS~=false and IR(49998) then Cast(49998) return end
        if WB_S.HS~=false and IR(55050) then Cast(55050) return end
        if WB_S.BS~=false then Cast(45902) return end
        if WB_S.RS~=false and IR(56815) then Cast(56815) end
    elseif t2>=t1 and t2>=t3 then
        -- FROST
        if WB_S.IT~=false and not hasFF then Cast(45477) return end
        if WB_S.PS~=false and not hasBP then Cast(45462) return end
        if WB_S.Pest~=false and hasFF and hasBP then
            local ffN,bpN=SN(55095),SN(55078) local ffLeft,bpLeft=0,0
            for i=1,40 do local n,_,_,_,_,_,ex=UnitDebuff('target',i) if not n then break end if ffN and n==ffN then ffLeft=ex-GetTime() end if bpN and n==bpN then bpLeft=ex-GetTime() end end
            if (ffLeft>0 and ffLeft<3) or (bpLeft>0 and bpLeft<3) then if IR(50842) then Cast(50842) return end end
        end
        if WB_S.UA~=false and IR(51271) then Cast(51271) return end
        if WB_S.HB~=false and HB(59052) and IR(49184) then Cast(49184) return end
        if WB_S.Oblit~=false and IR(49020) then Cast(49020) return end
        if WB_S.BS~=false then Cast(45902) return end
        if WB_S.FS~=false and IR(49143) then Cast(49143) return end
        if WB_S.BT~=false and IR(45529) then Cast(45529) return end
        if WB_S.ERW~=false and IR(47568) then Cast(47568) return end
        if WB_S.HoW~=false and IR(57330) then Cast(57330) end
    else
        -- UNHOLY
        if WB_S.IT~=false and not hasFF then Cast(45477) return end
        if WB_S.PS~=false and not hasBP then Cast(45462) return end
        if WB_S.Pest~=false and hasFF and hasBP and (WB_NCE or 0)>=(WB_AEMIN or 3) and IR(50842) then Cast(50842) return end
        if WB_S.DC~=false and UnitMana('player')>80 and IR(47541) then Cast(47541) return end
        if WB_S.Gargoyle~=false and IR(49206) then Cast(49206) return end
        if WB_S.UB~=false and IR(49194) then Cast(49194) return end
        if WB_S.SS~=false and IR(55090) then Cast(55090) return end
        if WB_S.BT~=false and IR(45529) then Cast(45529) return end
        if WB_S.BS~=false and IR(45902) then Cast(45902) return end
        if WB_S.DnD~=false and (WB_NCE or 0)>=(WB_AEMIN or 3) and IR(43265) then Cast(43265) return end
        if WB_S.ERW~=false and IR(47568) then Cast(47568) return end
        if WB_S.DC~=false and IR(47541) then Cast(47541) end
    end
";
}
