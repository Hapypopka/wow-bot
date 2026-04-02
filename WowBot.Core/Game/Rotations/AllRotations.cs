using System.IO;

namespace WowBot.Core.Game.Rotations;

public static class AllRotations
{
    private const string Helpers = @"
    local function IsReady(name)
        local s,d = GetSpellCooldown(name)
        return s ~= nil and s == 0
    end
    local function HasDebuff(unit, name)
        for i=1,40 do local n = UnitDebuff(unit, i) if not n then return false end if n == name then return true end end
        return false
    end
    local function HasBuff(name)
        for i=1,40 do local n = UnitBuff('player', i) if not n then return false end if n == name then return true end end
        return false
    end
    local function HasBuffById(id)
        for i=1,40 do local n,_,_,_,_,_,_,_,_,_,sId = UnitBuff('player', i) if not n then return false end if sId == id then return true end end
        return false
    end
    local function MP() local m,mm=UnitMana('player'),UnitManaMax('player') if mm==0 then return 1 end return m/mm end
    local function THP() local h,hm=UnitHealth('target'),UnitHealthMax('target') if hm==0 then return 1 end return h/hm end
    local function PHP() local h,hm=UnitHealth('player'),UnitHealthMax('player') if hm==0 then return 1 end return h/hm end
    local function CP() return GetComboPoints('player','target') or 0 end
    local function CDLeft(name) local s,d=GetSpellCooldown(name) if not s or s==0 then return 0 end return s+d-GetTime() end
    local function IsUsable(name) local u,nm=IsUsableSpell(name) if not u then return false end local s,d=GetSpellCooldown(name) return s~=nil and s==0 end
    local _SN={} local function SN(id) if not _SN[id] then _SN[id]=GetSpellInfo(id) end return _SN[id] end
    local function Cast(id) local n=SN(id) if n then CastSpellByName(n) end end
    local function IR(id) local n=SN(id) if not n then return false end return IsReady(n) end
    local function HB(id) local sn=SN(id) if not sn then return false end return HasBuff(sn) end
    local function HD(u,id) local sn=SN(id) if not sn then return false end return HasDebuff(u,sn) end
    local function IU(id) local n=SN(id) if not n then return false end return IsUsable(n) end
    local function NR(u,id,cid) local sn=SN(id) if not sn then return true end for i=1,40 do local n,_,_,_,_,_,exp=UnitDebuff(u,i) if not n then return true end if n==sn then local left=exp-GetTime() local _,_,_,ct=GetSpellInfo(cid or id) return left<=(ct or 1500)/1000 end end return true end
";

    private const string PreChecksDPS = @"
    if IsMounted() then return end
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    if not UnitAffectingCombat('target') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
";

    private const string PreChecksHealer = @"
    if IsMounted() then return end
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
";

    private const string HealerFindTarget = @"
    local best,bestHP = nil,1
    local hp = PHP()
    if hp < bestHP then best,bestHP = 'player',hp end
    local nr = GetNumRaidMembers()
    if nr > 0 then
        for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) then hp=UnitHealth(u)/UnitHealthMax(u) if hp<bestHP then best,bestHP=u,hp end end end
    else
        for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) then hp=UnitHealth(u)/UnitHealthMax(u) if hp<bestHP then best,bestHP=u,hp end end end
    end
";

    private static string Wrap(string body) =>
        "local function WB_Run()\n" + Helpers + body + "\nend\nlocal ok,err=pcall(WB_Run) if not ok then WB_ERR=err end\n";

    private static string WrapDPS(string body) => Wrap(PreChecksDPS + body);
    private static string WrapHealer(string body) => Wrap(PreChecksHealer + body);

    // ==================== PER-CLASS SCRIPTS ====================

    private static string Warrior() => WrapDPS(@"
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
        if WB_S.HS~=false and UnitMana('player')>50 and IR(47449) then Cast(47449) end
        if WB_S.BR==true and UnitMana('player')<30 and IR(2687) then Cast(2687) return end
        local defHP = (WB_S.DefHP or 40) / 100
        if PHP()<=defHP then
            local hasSW = HB(871)
            local hasLS = HB(12975)
            local anyDef = hasSW or hasLS
            if WB_S.DefAll==true then
                if WB_S.SW~=false and not hasSW and IR(871) then Cast(871) return end
                if WB_S.LS~=false and not hasLS and IR(12975) then Cast(12975) return end
            else
                if not anyDef then
                    if WB_S.SW~=false and IR(871) then Cast(871) return end
                    if WB_S.LS~=false and IR(12975) then Cast(12975) return end
                end
            end
        end
        if WB_S.SB~=false and not HB(2565) and IR(2565) then Cast(2565) return end
        if WB_S.ShieldSlam~=false and IR(23922) then Cast(23922) return end
        if WB_S.Revenge~=false then local rn=SN(6572) if rn then local u,_=IsUsableSpell(rn) if u then CastSpellByName(rn) return end end end
        if WB_S.TC~=false and IR(6343) then Cast(6343) return end
        if WB_S.ShockW~=false and IR(46968) then Cast(46968) return end
        if WB_S.Devastate~=false then Cast(20243) return end
    end
");

    private static string Paladin() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local judgeSpell = WB_S.JoL==true and SN(20271) or SN(53408)

    if t3>=t1 and t3>=t2 then
        -- RET
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.AW~=false then local _,_,_,stk=UnitDebuff('target',SN(31803) or '') if (stk or 0)>=3 and IR(31884) then Cast(31884) return end end
        if WB_S.HoW~=false and THP()<0.2 and IR(24275) then Cast(24275) return end
        if WB_S.Judge~=false and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        if WB_S.DS~=false and IR(53385) then Cast(53385) return end
        if WB_S.CS~=false and IR(35395) then Cast(35395) return end
        if WB_S.Cons~=false and IR(26573) then Cast(26573) return end
        if WB_S.Exo~=false and HB(59578) and IR(879) then Cast(879) return end
        if WB_S.SS~=false and not HB(53601) and IR(53601) then Cast(53601) return end
    elseif t2>=t1 and t2>=t3 then
        -- PROT
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.Plea~=false and not HB(54428) and IR(54428) then Cast(54428) return end
        if WB_S.AW~=false and IR(31884) then Cast(31884) return end
        if WB_S.HoR~=false and IR(53595) then Cast(53595) return end
        if WB_S.ShoR~=false and IR(53600) then Cast(53600) return end
        if WB_S.HolyShield~=false and IR(20925) then Cast(20925) return end
        if WB_S.Judge~=false and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        if WB_S.Cons~=false and IR(26573) then Cast(26573) return end
        if WB_S.HW~=false and IR(2812) then Cast(2812) return end
        if WB_S.AS~=false and IR(31935) then Cast(31935) return end
        if WB_S.SS~=false and not HB(53601) and IR(53601) then Cast(53601) return end
    else
        -- HOLY
" + HealerFindTarget + @"
        local judgeSpell = WB_S.JoL==true and SN(20271) or SN(53408)
        if UnitAffectingCombat('player') and not HB(53657) and UnitExists('target') and UnitCanAttack('player','target') and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        if WB_S.Beacon~=false and UnitExists('focus') and IR(53563) then
            local bn=SN(53563) local hasB=false if bn then for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n==bn then hasB=true break end end end
            if not hasB then TargetUnit('focus') Cast(53563) TargetLastTarget() return end
        end
        if WB_S.SS~=false and UnitExists('focus') and IR(53601) then
            local sn=SN(53601) local hasS=false if sn then for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n==sn then hasS=true break end end end
            if not hasS then TargetUnit('focus') Cast(53601) TargetLastTarget() return end
        end
        if WB_S.Plea~=false and MP()<0.7 and IR(54428) then
            if IR(31884) then Cast(31884)
            elseif IR(20216) then Cast(20216)
            else UseInventoryItem(13) UseInventoryItem(14) end
            Cast(54428) return
        end
        if bestHP>=1.0 then
            if UnitAffectingCombat('player') and UnitExists('target') and UnitCanAttack('player','target') and judgeSpell and not HasDebuff('target',judgeSpell) and IsReady(judgeSpell) then CastSpellByName(judgeSpell) end
            return
        end
        if WB_S.DF~=false and bestHP<0.3 and IR(20216) then Cast(20216) end
        if WB_S.LoH~=false and bestHP<0.15 and IR(633) then TargetUnit(best) Cast(633) return end
        if WB_S.HS~=false and bestHP<0.97 and IR(20473) then TargetUnit(best) Cast(20473) return end
        if WB_S.HL~=false and bestHP<0.97 then TargetUnit(best) Cast(635) return end
        if WB_S.FL~=false and bestHP<0.97 then TargetUnit(best) Cast(19750) return end
    end
");

    private static string Hunter() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then if WB_S then if WB_S.Rapid~=false and IR(3045) then Cast(3045) end if WB_S.Kill2~=false and IR(34026) then Cast(34026) end if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end end return end
    if not WB_S then WB_S={} end
    if WB_S.Pet~=false and UnitExists('pet') and not UnitIsDead('pet') then
        if HasBuffById(5384) or UnitIsDeadOrGhost('player') then WB_FD=GetTime() PetPassiveMode() PetFollow() return end
        if WB_FD and GetTime()-WB_FD<5 then PetPassiveMode() PetFollow() return end
    end
    if UnitIsDeadOrGhost('player') then return end
    if WB_S.Pet~=false then
        if not UnitExists('pet') then CastSpellByName('Призыв питомца') return end
        if UnitIsDead('pet') then CastSpellByName('Воскрешение питомца') return end
        if not UnitAffectingCombat('player') then PetPassiveMode() PetFollow() return end
        if not WB_FD or GetTime()-WB_FD>=5 then if UnitExists('target') and UnitCanAttack('player','target') and not UnitIsDeadOrGhost('target') then PetAttack() end end
    end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
    if WB_S.Track~=false then local ct=UnitCreatureType('target') local tr if ct=='Животное' then tr='Выслеживание животных' elseif ct=='Демон' then tr='Выслеживание демонов' elseif ct=='Дракон' then tr='Выслеживание драконов' elseif ct=='Элементаль' then tr='Выслеживание элементалей' elseif ct=='Великан' then tr='Выслеживание великанов' elseif ct=='Гуманоид' then tr='Выслеживание гуманоидов' elseif ct=='Нежить' then tr='Выслеживание нежити' end if tr and not HasBuff(tr) then CastSpellByName(tr) return end end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if not HD('target',1130) and WB_S.Mark~=false then Cast(1130) return end
    if WB_S.Kill~=false and THP()<0.2 and IR(53351) then Cast(53351) return end
    if t1>=t2 and t1>=t3 then
        if WB_S.Bestial~=false and IR(19574) then Cast(19574) return end
        if WB_S.BW~=false and IR(34471) then Cast(34471) return end
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Arcane~=false and IR(3044) then Cast(3044) return end
        if WB_S.Steady~=false then Cast(56641) end
    elseif t2>=t1 and t2>=t3 then
        -- MM Hunter priority rotation + trap weaving
        if WB_S.Dragonhawk~=false and not HB(61847) and MP()>0.3 then Cast(61847) return end
        if WB_S.Volley~=false and (WB_NE or 0)>1 and IR(1510) then Cast(1510) return end
        -- CDs: Rapid Fire + Kill Command + pet abilities (off-GCD or weave)
        if WB_S.Rapid~=false and IR(3045) then Cast(3045) end
        if WB_S.Kill2~=false and IR(34026) then Cast(34026) end
        if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end
        -- Priority: Serpent Sting > Chimera > Silencing > Aimed > Trap > Steady
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Chimera~=false and IR(53209) then Cast(53209) return end
        if WB_S.Silence~=false and IR(34490) then Cast(34490) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Trap~=false and IR(13813) then Cast(13813) return end
        -- Readiness: reset CDs when Chimera + Rapid Fire both on CD
        if WB_S.Readiness~=false and IR(23989) and not IR(53209) and not IR(3045) then Cast(23989) return end
        if WB_S.Steady~=false then Cast(56641) end
    else
        if WB_S.Explosive~=false and IR(53301) then Cast(53301) return end
        if WB_S.Black~=false and not HD('target',3674) and IR(3674) then Cast(3674) return end
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Arcane~=false and IR(3044) then Cast(3044) return end
        if WB_S.Steady~=false then Cast(56641) end
    end
");

    private static string Rogue() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local cp = CP()
    if t1>=t2 and t1>=t3 then
        if WB_S.Envenom~=false and cp>=4 and IR(32645) then Cast(32645) return end
        if WB_S.Rupture~=false and cp>=4 and not HD('target',1943) then Cast(1943) return end
        if WB_S.HFB~=false and not HB(51662) and IR(51662) then Cast(51662) return end
        if WB_S.Mutilate~=false then Cast(1329) end
    elseif t2>=t1 and t2>=t3 then
        if WB_S.SnD~=false and cp>=1 and not HB(5171) then Cast(5171) return end
        if WB_S.Rupture~=false and cp>=5 and not HD('target',1943) then Cast(1943) return end
        if WB_S.Evis~=false and cp>=5 then Cast(2098) return end
        if WB_S.KS~=false and IR(51690) then Cast(51690) return end
        if WB_S.SS~=false then Cast(1752) end
    else
        if WB_S.Evis~=false and cp>=5 then Cast(2098) return end
        if WB_S.Rupture~=false and cp>=5 and not HD('target',1943) then Cast(1943) return end
        if WB_S.Hemo~=false and not HD('target',16511) then Cast(16511) return end
        if WB_S.BS~=false then Cast(53) end
    end
");

    private static string Priest() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t3>=t1 and t3>=t2 then
        -- SHADOW
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if not HB(15473) then Cast(15473) return end
        if WB_S.Disp~=false and MP()<0.15 and IR(47585) then Cast(47585) return end
        if WB_S.VT~=false and NR('target',34914) then if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() Cast(34914) return end end
        if WB_S.DP~=false and NR('target',2944) then if not WB_DP or GetTime()-WB_DP>2 then WB_DP=GetTime() Cast(2944) return end end
        if WB_S.SWP~=false and NR('target',589) then if not WB_SWP or GetTime()-WB_SWP>2 then WB_SWP=GetTime() Cast(589) return end end
        local _,_,_,_,mbPts = GetTalentInfo(3,8)
        if WB_S.MB~=false and mbPts and mbPts>0 and IR(8092) then Cast(8092) return end
        if WB_S.SF~=false and MP()<0.5 and IR(34433) then Cast(34433) return end
        if WB_S.MF~=false then Cast(15407) end
    elseif t1>=t2 then
        -- DISC
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.PW~=false and bestHP<0.99 and IR(17) then TargetUnit(best) Cast(17) return end
        if WB_S.Penance~=false and bestHP<0.95 and IR(47540) then TargetUnit(best) Cast(47540) return end
        if WB_S.PS~=false and bestHP<0.4 and IR(33206) then TargetUnit(best) Cast(33206) return end
        if WB_S.Flash~=false and bestHP<0.99 then TargetUnit(best) Cast(2061) return end
        if WB_S.PoM~=false and IR(33076) then TargetUnit(best) Cast(33076) return end
        if WB_S.Renew~=false and bestHP<0.99 then TargetUnit(best) Cast(139) return end
    else
        -- HOLY
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.CoH~=false and bestHP<0.95 and IR(34861) then Cast(34861) return end
        if WB_S.Guardian~=false and bestHP<0.2 and IR(47788) then TargetUnit(best) Cast(47788) return end
        if WB_S.PoM~=false and IR(33076) then TargetUnit(best) Cast(33076) return end
        if WB_S.Renew~=false and bestHP<0.99 then TargetUnit(best) Cast(139) return end
        if WB_S.Flash~=false and bestHP<0.95 then TargetUnit(best) Cast(2061) return end
        if WB_S.GHeal~=false and bestHP<0.6 then TargetUnit(best) Cast(2060) return end
        if WB_S.Binding~=false and bestHP<0.95 then TargetUnit(best) Cast(32546) return end
    end
");

    private static string DeathKnight() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local hasFF = HD('target',55095)
    local hasBP = HD('target',55078)
    if t1>=t2 and t1>=t3 then
        -- BLOOD (tank)
        if WB_S.IT~=false and not hasFF then Cast(45477) return end
        if WB_S.PS~=false and not hasBP then Cast(45462) return end
        if WB_S.Pest~=false and hasFF and hasBP and IR(50842) then Cast(50842) return end
        if WB_S.VB~=false and PHP()<0.5 and IR(55233) then Cast(55233) return end
        if WB_S.DS~=false and IR(49998) then Cast(49998) return end
        if WB_S.HS~=false and IR(55050) then Cast(55050) return end
        if WB_S.BS~=false then Cast(45902) return end
        if WB_S.RS~=false and IR(56815) then Cast(56815) end
    elseif t2>=t1 and t2>=t3 then
        -- FROST DK
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
        if WB_S.Pest~=false and hasFF and hasBP and IR(50842) then Cast(50842) return end
        if WB_S.Gargoyle~=false and IR(49206) then Cast(49206) return end
        if WB_S.UB~=false and IR(49194) then Cast(49194) return end
        if WB_S.DnD~=false and IR(43265) then Cast(43265) return end
        if WB_S.SS~=false and IR(55090) then Cast(55090) return end
        if WB_S.BS~=false then Cast(45902) return end
        if WB_S.DC~=false and IR(47541) then Cast(47541) end
    end
");

    private static string Shaman() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    -- Тотемы: в бою → Зов Духов, вне боя → Возвращение тотемов
    if WB_S.CallSpirits~=false then
        local inCombat = UnitAffectingCombat('player')
        local hasTotem = false
        for i=1,4 do local _,_,_,d = GetTotemInfo(i) if d and d>0 then hasTotem=true break end end
        -- В бою + нет тотемов → поставить
        if inCombat and not hasTotem and IsReady('Зов Духов') then CastSpellByName('Зов Духов') return end
        -- В бою + тотемы далеко (нет баффа воздуха) → возвращение, след тик поставит заново
        if inCombat and hasTotem then
            local hasBuff=false for i=1,40 do local n=UnitBuff('player',i) if not n then break end if n=='Тотем гнева воздуха' or n=='Тотем неистовства ветра' or n=='Сопротивление силам природы' then hasBuff=true break end end
            if not hasBuff and IsReady('Возвращение тотемов') then CastSpellByName('Возвращение тотемов') return end
        end
        -- Вне боя + тотемы стоят → подобрать
        if not inCombat and hasTotem and IsReady('Возвращение тотемов') then CastSpellByName('Возвращение тотемов') return end
    end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t3>=t1 and t3>=t2 then
        -- RESTO
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.RT~=false and bestHP<0.99 and IR(61295) then TargetUnit(best) Cast(61295) return end
        if WB_S.NS~=false and bestHP<0.3 and IR(16188) then Cast(16188) return end
        if WB_S.CH~=false and bestHP<0.95 and IR(1064) then TargetUnit(best) Cast(1064) return end
        if WB_S.LHW~=false and bestHP<0.99 then TargetUnit(best) Cast(8004) return end
        if WB_S.HW~=false and bestHP<0.6 then TargetUnit(best) Cast(331) return end
        if WB_S.ES~=false and UnitExists('focus') and IR(974) then
            local esn=SN(974) local hasES=false if esn then for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n==esn then hasES=true break end end end
            if not hasES then TargetUnit('focus') Cast(974) TargetLastTarget() return end
        end
    else
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if t1>=t2 and t1>=t3 then
            -- ELEMENTAL
            if WB_S.FS~=false and not HD('target',8050) then Cast(8050) return end
            if WB_S.LvB~=false and IR(51505) then Cast(51505) return end
            if WB_S.TnL~=false and IR(51490) then Cast(51490) return end
            if WB_S.CL~=false and IR(421) then Cast(421) return end
            if WB_S.LB~=false then Cast(403) end
        else
            -- ENHANCEMENT
            if WB_S.LS~=false and not HB(324) then Cast(324) return end
            if WB_S.Wolves~=false and IR(51533) then Cast(51533) return end
            if WB_S.SR~=false and IR(30823) then Cast(30823) return end
            -- Searing Totem: обновлять если нет огненного тотема (60с жизни)
            if WB_S.Searing~=false then local _,_,_,fd=GetTotemInfo(1) if not fd or fd==0 then CastSpellByName('Тотем опаляющего пламени') return end end
            if WB_S.SS~=false and IR(17364) then Cast(17364) return end
            if WB_S.FS~=false and not HD('target',8050) then Cast(8050) return end
            if WB_S.ES~=false and IR(8042) then Cast(8042) return end
            if WB_S.LvB~=false and HasBuff('Водоворот готов!') and IR(51505) then Cast(51505) return end
            if WB_S.LB_MW~=false and HasBuff('Водоворот готов!') then Cast(403) end
        end
    end
");

    private static string Mage() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- ARCANE
        if WB_S.AP~=false and IR(12042) then Cast(12042) return end
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.Barrage~=false and HB(44401) and IR(44425) then Cast(44425) return end
        if WB_S.Evoc~=false and MP()<0.35 and IR(12051) then Cast(12051) return end
        if WB_S.AB~=false then Cast(30451) end
    elseif t2>=t1 and t2>=t3 then
        -- FIRE
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.Combust~=false and IR(11129) then Cast(11129) return end
        if WB_S.LB~=false and not HD('target',44457) then Cast(44457) return end
        if WB_S.Pyro~=false and HB(48108) and IR(11366) then Cast(11366) return end
        if WB_S.Scorch~=false and not HD('target',2948) then Cast(2948) return end
        if WB_S.FB~=false then Cast(133) end
    else
        -- FROST
        if WB_S.Mirror~=false and IR(55342) then Cast(55342) return end
        if WB_S.DF~=false and IR(44572) then Cast(44572) return end
        if WB_S.IL~=false and HB(44544) and IR(30455) then Cast(30455) return end
        if WB_S.FFB~=false and HB(57761) then Cast(44614) return end
        if WB_S.FBolt~=false then Cast(116) end
    end
");

    private static string Warlock() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- AFFLICTION (precast refresh)
        if WB_S.LifeTap~=false and MP()<0.15 then Cast(1454) return end
        if WB_S.Haunt~=false and IR(48181) then Cast(48181) return end
        if WB_S.UA~=false and NR('target',30108) then Cast(30108) return end
        if WB_S.Corruption~=false and NR('target',172) then Cast(172) return end
        if WB_S.CoA==true and NR('target',980) then Cast(980) return end
        if WB_S.CoE==true and NR('target',1490) then Cast(1490) return end
        if WB_S.Immolate~=false and NR('target',348) then Cast(348) return end
        if WB_S.DF~=false and IR(30283) then Cast(30283) return end
        if WB_S.LTGlyph==true and not HB(63321) then Cast(1454) return end
        if WB_S.LifeTap~=false and MP()<0.3 then Cast(1454) return end
        if WB_S.ShadowBolt~=false then Cast(686) end
    elseif t2>=t1 and t2>=t3 then
        -- DEMONOLOGY (spell ID based, вдохновлено NPCBots)
        -- Spell IDs: ShadowBolt=686 Corruption=172 Immolate=348 Incinerate=29722 SoulFire=6353
        -- LifeTap=1454 DarkPact=18220 DeathCoil=6789 ShadowWard=6229 Soulshatter=29858
        -- Meta=47241 DemonicEmpower=47193 ImmoAura=50589 DrainSoul=1120
        -- CurseAgony=980 CurseDoom=603 CurseElements=1490
        -- Buffs: Decimation=63167 MoltenCore=71165 LifeTapGlyph=63321 Meta=47241
        local tHP = THP()
        local pHP = PHP()
        local pMP = MP()
        -- [ВЫЖИВАНИЕ] Прерывание Hellfire если HP < 25%
        if UnitChannelInfo('player') and pHP<0.25 then SpellStopCasting() return end
        -- [ВЫЖИВАНИЕ] Death Coil: self-heal + урон (HP < 35%)
        if pHP<0.35 and IR(6789) then Cast(6789) return end
        -- [ВЫЖИВАНИЕ] Healthstone: HP < 60%
        if pHP<0.6 then for b=0,4 do for s=1,GetContainerNumSlots(b) do local l=GetContainerItemLink(b,s) if l and l:find('амень здоровья') then UseContainerItem(b,s) return end end end end
        -- [ВЫЖИВАНИЕ] Shadow Ward (HP < 70%)
        if pHP<0.7 and IR(6229) then Cast(6229) return end
        -- [МАНА] Dark Pact: мана пета → своя мана
        if pMP<0.2 and UnitExists('pet') and UnitPower('pet')>300 and IR(18220) then Cast(18220) return end
        -- [МАНА] Life Tap: глиф (поддержка бафа SP)
        if WB_S.LTGlyph==true and not HasBuffById(63321) and pHP>0.3 then Cast(1454) return end
        -- [МАНА] Life Tap: мало маны
        if WB_S.LifeTap~=false and pMP<0.15 and pHP>0.3 then Cast(1454) return end
        -- [УГРОЗА] Soulshatter: сброс агро
        if UnitThreatSituation('player') and UnitThreatSituation('player')>=3 and IR(29858) then Cast(29858) return end
        -- [БУРСТ] Meta
        if WB_S.Meta~=false and not HasBuffById(47241) and IR(47241) then Cast(47241) return end
        -- [БУРСТ] Demonic Empowerment (только если есть пет)
        if WB_S.DemonEmpower~=false and UnitExists('pet') and IR(47193) then Cast(47193) return end
        -- [МЕТА] Immolation Aura (в мете, ближний бой)
        if WB_S.ImmoAura~=false and HasBuffById(47241) and IR(50589) and CheckInteractDistance('target',3) then Cast(50589) return end
        -- [AOE] Семя порчи — спам если врагов >= порог из ползунка
        if WB_S.SeedOfC~=false and (WB_NCE or 0)>=(WB_AEMIN or 3) then Cast(27243) return end
        -- [ПРОКЛЯТИЕ] всегда навешиваем если нет
        if WB_S.CoA==true and not HD('target',980) then Cast(980) return end
        if WB_S.CoD==true and not HD('target',603) then Cast(603) return end
        if WB_S.CoE==true and not HD('target',1490) then Cast(1490) return end
        -- [ДОТЫ] навешиваем/обновляем если нет или скоро кончится (castTime precast)
        if WB_S.Corruption~=false and NR('target',172) and (not WB_CORR or GetTime()-WB_CORR>2) then WB_CORR=GetTime() Cast(172) return end
        if WB_S.Immolate~=false and NR('target',348) and (not WB_IMMO or GetTime()-WB_IMMO>2) then WB_IMMO=GetTime() Cast(348) return end
        -- [ПРОКИ] Soul Fire с Decimation
        if WB_S.SoulFire~=false and HB(63167) and IR(6353) then Cast(6353) return end
        -- [ПРОКИ] Incinerate с Molten Core
        if WB_S.Incinerate~=false and HB(71165) then Cast(29722) return end
        -- [МАНА] Life Tap: средняя мана
        if WB_S.LifeTap~=false and pMP<0.3 and pHP>0.3 then Cast(1454) return end
        -- [ОСКОЛКИ] Drain Soul: только для фарма осколков (< 10 шт)
        if tHP<0.25 and GetItemCount(6265)<10 and IR(1120) then Cast(1120) return end
        -- [ФИЛЛЕР] Shadow Bolt
        if WB_S.ShadowBolt~=false then Cast(686) end
    else
        -- DESTRUCTION
        if WB_S.LifeTap~=false and MP()<0.15 then Cast(1454) return end
        if WB_S.Immolate~=false and not HD('target',348) then Cast(348) return end
        if WB_S.Chaos~=false and IR(50796) then Cast(50796) return end
        if WB_S.Conflag~=false and IR(17962) then Cast(17962) return end
        if WB_S.Corruption~=false and not HD('target',172) then Cast(172) return end
        if WB_S.CoD==true and not HD('target',603) then Cast(603) return end
        if WB_S.CoE==true and not HD('target',1490) then Cast(1490) return end
        if WB_S.LTGlyph==true and not HB(63321) then Cast(1454) return end
        if WB_S.LifeTap~=false and MP()<0.3 then Cast(1454) return end
        if WB_S.Incinerate~=false then Cast(29722) end
    end
");

    private static string Druid() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    -- Возрождение (боевой рес) — если кто-то в пати/рейде мертв
    if WB_S.Rebirth~=false and UnitAffectingCombat('player') and IR(20484) then
        local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) then TargetUnit(u) Cast(20484) return end end
        else for i=1,4 do local u='party'..i if UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) then TargetUnit(u) Cast(20484) return end end end
    end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)

    if t1>=t2 and t1>=t3 then
        -- BALANCE
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.Moonkin~=false and not HB(24858) then Cast(24858) return end
        if WB_S.Innervate~=false and MP()<0.3 and IR(29166) then Cast(29166) return end
        if WB_S.Starfall~=false and IR(48505) then Cast(48505) return end
        if WB_S.Treants~=false and IR(33831) then Cast(33831) return end
        if WB_S.FF~=false and not HD('target',770) then Cast(770) return end
        if WB_S.IS~=false and not HD('target',5570) then Cast(5570) return end
        if WB_S.MF_d~=false and not HD('target',8921) then Cast(8921) return end
        if not WB_ECL then WB_ECL=0 end
        if HasBuffById(48518) and WB_ECL~=1 then WB_ECL=1 end
        if HasBuffById(48517) and WB_ECL~=2 then WB_ECL=2 end
        if WB_ECL==1 and WB_S.Starfire~=false then Cast(2912)
        elseif WB_S.Wrath~=false then Cast(5176) end
    elseif t2>=t1 and t2>=t3 then
        -- FERAL (Cat/Bear по форме через GetShapeshiftForm: 1=bear, 3=cat)
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        local form = GetShapeshiftForm()
        local energy = UnitPower and UnitPower('player') or UnitMana('player')
        local cp = CP()
        if not WB_FLOG then WB_FLOG=0 end
        if GetTime()-WB_FLOG>2 then WB_FLOG=GetTime() end
        if form~=1 and form~=3 then return end
        if form==3 then
            -- CAT DPS (приоритетная система с комбо-поинтами)
            local hasRoar = HB(52610)
            local hasBerserk = HB(50334)
            local hasRip = HD('target',1079)
            local hasMangle = HD('target',33876)
            local hasRake = HD('target',1822)
            -- Тигриное неистовство — по КД, не в берсерке (даёт +60 энергии + бафф урона)
            if WB_S.TF~=false and not hasBerserk and IR(5217) then Cast(5217) return end
            -- === ФИНИШЕРЫ (5 КП) ===
            if cp>=5 then
                -- Дикий рев спал → обновить (приоритет над рипом)
                if WB_S.Roar~=false and not hasRoar then Cast(52610) return end
                -- Разорвать не висит → повесить
                if WB_S.Rip~=false and not hasRip then Cast(1079) return end
                -- Всё висит → Свирепый укус
                if WB_S.FB~=false and hasRoar and hasRip then Cast(22568) return end
            end
            -- Дикий рев спал и есть хоть 1 КП → срочно обновить
            if WB_S.Roar~=false and not hasRoar and cp>=1 then Cast(52610) return end
            -- Берсерк — только если рев и рип уже висят
            if WB_S.Berserk~=false and hasRoar and hasRip and IR(50334) then Cast(50334) return end
            -- === БИЛДЕРЫ (набираем КП) ===
            -- Волшебный огонь — бесплатно, -5% брони
            if WB_S.FF_cat~=false and not HD('target',16857) and IR(16857) then Cast(16857) return end
            -- Увечье — поддерживать дебафф (+30% блид урон)
            if WB_S.Mangle~=false and not hasMangle then Cast(33876) return end
            -- Растерзать (Rake) — поддерживать ДоТ (даёт 1 КП)
            if WB_S.Rake~=false and not hasRake then Cast(1822) return end
            -- Полоснуть (Shred) — основной билдер КП
            if WB_S.Shred~=false then Cast(5221) return end
            -- Цапнуть — фоллбэк (если не за спиной для Полоснуть)
            Cast(1082)
        else
            -- BEAR TANK
            if WB_S.Maul~=false and UnitMana('player')>15 then Cast(6807) end
            if WB_S.FF_bear~=false and not HD('target',16857) and IR(16857) then Cast(16857) return end
            if WB_S.Mangle_b~=false and IR(33878) then Cast(33878) return end
            if WB_S.Lacerate~=false then Cast(33745) return end
            if WB_S.Swipe~=false and IR(779) then Cast(779) return end
        end
    else
        -- RESTO DRUID
        if WB_S.ToL~=false and not HB(33891) and IR(33891) then Cast(33891) return end
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        -- Экстренный хил: NS + целительное прикосновение (инстант)
        if WB_S.NS~=false and bestHP<0.3 and IR(17116) then Cast(17116) TargetUnit(best) Cast(50464) return end
        -- Быстрое восстановление (Swiftmend) — требует Омоложение или Восстановление на цели
        if WB_S.SM~=false and bestHP<0.7 and IR(18562) then
            local rjn,rgn=SN(774),SN(8936) local hasHot=false for i=1,40 do local n=UnitBuff(best,i) if not n then break end if (rjn and n==rjn) or (rgn and n==rgn) then hasHot=true break end end
            if hasHot then TargetUnit(best) Cast(18562) return end
        end
        -- Буйный рост (Wild Growth) — АоЕ хил при нескольких раненых
        if WB_S.WG~=false and bestHP<0.95 and IR(48438) then TargetUnit(best) Cast(48438) return end
        -- Омоложение (Rejuv) — поддерживать на раненых
        if WB_S.Rejuv~=false and bestHP<0.99 then
            local rjn=SN(774) local hasRejuv=false if rjn then for i=1,40 do local n=UnitBuff(best,i) if not n then break end if n==rjn then hasRejuv=true break end end end
            if not hasRejuv then TargetUnit(best) Cast(774) return end
        end
        -- Жизнецвет (Lifebloom) — поддерживать на танке (фокус)
        if WB_S.LB~=false and UnitExists('focus') then
            local lbn=SN(33763) local lbCount=0 if lbn then for i=1,40 do local n,_,_,c=UnitBuff('focus',i) if not n then break end if n==lbn then lbCount=c or 1 break end end end
            if lbCount<3 then TargetUnit('focus') Cast(33763) TargetLastTarget() return end
        end
        -- Восстановление (Regrowth) — при сильном уроне
        if WB_S.Regrowth~=false and bestHP<0.7 then TargetUnit(best) Cast(8936) return end
        -- Покровительство Природы (Nourish) — филлер (сильнее если HoT на цели)
        if WB_S.Nourish~=false and bestHP<0.95 then TargetUnit(best) Cast(50464) return end
    end
");

    // ==================== PUBLIC API ====================

    private static readonly string ScriptsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");

    /// <summary>Загрузить скрипт из файла scripts/{class}.lua, fallback на встроенный</summary>
    public static string GetFullScript(string playerClass = "")
    {
        // Попытка загрузить из файла
        var filePath = Path.Combine(ScriptsDir, $"{playerClass.ToLower()}.lua");
        if (File.Exists(filePath))
        {
            try
            {
                var script = File.ReadAllText(filePath);
                if (!string.IsNullOrWhiteSpace(script))
                {
                    Logger.Info($"Loaded script from file: {filePath}");
                    return script;
                }
            }
            catch { /* fallback to built-in */ }
        }

        return GetBuiltInScript(playerClass);
    }

    public static string GetBuiltInScript(string playerClass) => playerClass switch
    {
        "WARRIOR" => Warrior(),
        "PALADIN" => Paladin(),
        "HUNTER" => Hunter(),
        "ROGUE" => Rogue(),
        "PRIEST" => Priest(),
        "DEATHKNIGHT" => DeathKnight(),
        "SHAMAN" => Shaman(),
        "MAGE" => Mage(),
        "WARLOCK" => Warlock(),
        "DRUID" => Druid(),
        _ => Mage(), // fallback
    };

    /// <summary>Экспортировать все встроенные скрипты в папку scripts/</summary>
    public static void ExportScripts()
    {
        Directory.CreateDirectory(ScriptsDir);
        var classes = new[] { "WARRIOR", "PALADIN", "HUNTER", "ROGUE", "PRIEST", "DEATHKNIGHT", "SHAMAN", "MAGE", "WARLOCK", "DRUID" };
        foreach (var cls in classes)
        {
            var path = Path.Combine(ScriptsDir, $"{cls.ToLower()}.lua");
            // Всегда перезаписываем встроенными (hot-reload: юзер правит → жмёт Reload)
            File.WriteAllText(path, GetBuiltInScript(cls));
        }
        Logger.Info($"Exported {classes.Length} scripts to {ScriptsDir}");
    }

    public static string GetInstantScript(string playerClass = "") => @"
local function WB_Inst()
" + Helpers + @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitAffectingCombat('target') then return end
    if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
" + (playerClass switch
    {
        "DRUID" => @"
    if IR(48505) then Cast(48505) return end
    if not HD('target',770) then Cast(770) return end
    if not HD('target',5570) then Cast(5570) return end
    if not HD('target',8921) then Cast(8921) return end",
        "PRIEST" => @"
    if not HD('target',2944) then Cast(2944) return end
    if not HD('target',589) then Cast(589) return end",
        "WARLOCK" => @"
    if not HD('target',172) then Cast(172) return end",
        "PALADIN" => @"
    if IR(24275) then Cast(24275) return end
    if IR(53408) then Cast(53408) return end
    if IR(53385) then Cast(53385) return end
    if IR(35395) then Cast(35395) return end",
        "HUNTER" => @"
    if IR(53351) then Cast(53351) return end
    if IR(3044) then Cast(3044) return end",
        "DEATHKNIGHT" => @"
    if not HD('target',55095) then Cast(45477) return end
    if not HD('target',55078) then Cast(45462) return end",
        "MAGE" => @"
    if HB(44544) then Cast(30455) return end
    if HB(48108) then Cast(11366) return end",
        _ => ""
    }) + @"
end
WB_Inst()
";
}
