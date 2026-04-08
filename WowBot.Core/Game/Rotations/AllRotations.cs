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
    -- Танк: автотаунт на моба который бьёт НЕ танка
    local function TryTaunt(tauntId)
        if not WB_S or WB_S.AutoTaunt==false then return false end
        if not IR(tauntId) then return false end
        local tv=UnitName('targettarget') local pn=UnitName('player')
        if tv and tv~=pn and UnitAffectingCombat('target') then Cast(tauntId) return true end
        return false
    end
    -- Танк: деф КД по HP порогу (один за раз)
    local function TryDefCD(...)
        if not WB_S or WB_S.DefCD==false then return false end
        local hp=PHP()
        local threshold=(WB_S.DefHP or 40)/100
        if hp>threshold then return false end
        for i=1,select('#',...),2 do
            local id=select(i,...) local buffCheck=select(i+1,...)
            if IR(id) and (not buffCheck or not HB(id)) then Cast(id) return true end
        end
        return false
    end
    local function CastOn(u,id) local n=SN(id) if n and u then RunMacroText('/cast [@'..u..'] '..n) end end
    local function IsTankUnit(u) for i=1,40 do local n,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,i) if not n then return false end if id==48263 or id==25780 or id==5487 or id==9634 then return true end end return false end
    if not WB_RES then WB_RES={} end
    local function TryRes(spellId)
        if WB_S and WB_S.AutoRes==false then return false end
        if UnitAffectingCombat('player') then return false end
        if not IR(spellId) then return false end
        local now=GetTime()
        local nr=GetNumRaidMembers()
        if nr>0 then
            for i=1,nr do local u='raid'..i local g=UnitGUID(u) if g and UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and (not WB_RES[g] or now-WB_RES[g]>60) then WB_RES[g]=now TargetUnit(u) Cast(spellId) return true end end
        else
            for i=1,4 do local u='party'..i local g=UnitGUID(u) if g and UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and (not WB_RES[g] or now-WB_RES[g]>60) then WB_RES[g]=now TargetUnit(u) Cast(spellId) return true end end
        end
        return false
    end
    local function NR(u,id,cid) local sn=SN(id) if not sn then return true end for i=1,40 do local n,_,_,_,_,_,exp=UnitDebuff(u,i) if not n then return true end if n==sn then local left=exp-GetTime() local _,_,_,ct=GetSpellInfo(cid or id) return left<=(ct or 1500)/1000 end end return true end
";

    // Break-CC: автоматическое снятие контроля (стан/страх/корень)
    // Spell IDs: BerserkerRage=18499, EveryManForHimself=59752, WillOfForsaken=7744,
    // EscapeArtist=20589, Lichborne=49039, HandOfFreedom=1044, IceboundFortitude=48792,
    // Blink=1953, DivineShield=642, CloakOfShadows=31224, Trinket(PvP)=42292
    private const string BreakCC = @"
    do
        -- Проверка CC: не можем действовать? (проверяем через movement flags + конкретные дебаффы)
        local isCC = false
        local ccType = ''
        for i=1,40 do
            local n,_,_,_,dt,dur,exp,_,_,_,id = UnitDebuff('player',i)
            if not n then break end
            -- Известные CC механики по debuffType + имени
            if dt == 'Magic' or dt == nil then
                -- Fear/Stun/Root/Incapacitate/Sleep — проверяем через aura
                -- Простой способ: если debuff и мы не можем двигаться/кастить
            end
        end
        -- Альтернативная проверка: если юнит не может атаковать (HasFullControl в 3.3.5 нет)
        -- Проверяем: есть ли конкретные известные CC дебаффы
        local ccSpells = {
            -- FEAR (страх)
            [8122]=1,[10888]=1,[10890]=1,[10892]=1,[48125]=1, -- Ментальный крик (прист)
            [5782]=1,[6213]=1,[6215]=1, -- Страх (варлок)
            [5484]=1,[17928]=1, -- Вой ужаса (варлок)
            [6789]=1,[17925]=1,[17926]=1,[27223]=1,[47859]=1,[47860]=1, -- Лик смерти (варлок)
            [5246]=1, -- Устрашающий крик (воин)
            -- STUN (оглушение)
            [853]=1,[5588]=1,[5589]=1,[10308]=1, -- Молот правосудия (пал)
            [408]=1,[8643]=1, -- Удар по почкам (рога)
            [1833]=1, -- Подлый трюк (рога)
            [46968]=1, -- Ударная волна (воин)
            [12809]=1, -- Сотрясение (воин)
            [44572]=1, -- Глубокая заморозка (маг)
            [49203]=1, -- Леденящий холод (ДК)
            [30283]=1, -- Неистовство Тьмы (варлок)
            -- ROOT (корни/обездвиживание)
            [339]=1,[1062]=1,[5195]=1,[5196]=1,[9852]=1,[9853]=1,[26989]=1,[53308]=1, -- Гнев деревьев (друид)
            [122]=1,[865]=1,[6131]=1,[10230]=1,[27088]=1,[42917]=1, -- Кольцо льда (маг)
            [45524]=1, -- Оковы льда (ДК)
            -- POLYMORPH / INCAPACITATE
            [118]=1,[12824]=1,[12825]=1,[28271]=1,[28272]=1,[61305]=1, -- Превращение (маг)
            [51514]=1, -- Порча (шаман)
            [20066]=1, -- Покаяние (пал)
            [2094]=1, -- Ослепление (рога)
            [6770]=1, -- Ошеломление (рога)
            [1776]=1, -- Выбивание (рога)
            [710]=1, -- Изгнание (варлок)
            [9484]=1,[9485]=1,[10955]=1, -- Оковы нежити (прист)
            -- SLEEP
            [19386]=1,[24132]=1,[24133]=1,[27068]=1,[49011]=1,[49012]=1, -- Укус виверны (хант)
            -- HORROR
            [64044]=1, -- Психический ужас (прист)
            -- BOSS CC (рейдовые)
            [66012]=1, -- Ледяная хватка Синдрагосы
            [69057]=1, -- Костяной шип Лорда Ребрада
            [72293]=1, -- Отметина бессмертного чемпиона (ЛК)
            [68981]=1, -- Исступленная жатва (ЛК — Ужас)
            [69200]=1, -- Леденящий захват (ЛК — Вал'кирия)
            [70337]=1, -- Укус Нерожденного (Синдрагоса)
            [71289]=1, -- Властная порча (Леди Смертный Шепот)
        }
        for i=1,40 do
            local n,_,_,_,_,_,_,_,_,_,id = UnitDebuff('player',i)
            if not n then break end
            if ccSpells[id] then isCC = true break end
        end
        if isCC and UnitAffectingCombat('player') then
            local _,c = UnitClass('player')
            local _,r = UnitRace('player')
            -- Расовые антистан
            if r == 'Human' and IR(59752) then Cast(59752) return end
            if r == 'Scourge' and IR(7744) then Cast(7744) return end
            if r == 'Gnome' and IR(20589) then Cast(20589) return end
            -- Классовые антистан
            if c == 'WARRIOR' and IR(18499) then Cast(18499) return end
            if c == 'DEATHKNIGHT' and IR(48792) then Cast(48792) return end
            if c == 'PALADIN' and IR(1044) then CastSpellByName(SN(1044),'player') return end
            if c == 'MAGE' and IR(1953) then Cast(1953) return end
            if c == 'ROGUE' and IR(31224) then Cast(31224) return end
            if c == 'DRUID' and IR(49039) then Cast(49039) return end
            -- PvP тринкет (слот 13/14)
            local s,d = GetInventoryItemCooldown('player',13) if s == 0 then UseInventoryItem(13) return end
            s,d = GetInventoryItemCooldown('player',14) if s == 0 then UseInventoryItem(14) return end
        end
    end
";

    private const string PreChecksDPS = @"
    if IsMounted() then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
" + BreakCC + @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if not UnitAffectingCombat('target') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
    if not WB_SA or GetTime()-WB_SA>1 then WB_SA=GetTime() RunMacroText('/startattack') end
";

    private const string PreChecksHealer = @"
    if IsMounted() then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
" + BreakCC + @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
";

    // Умный выбор цели хила (вдохновлено NPCBots BuffAndHealGroup + HealTarget):
    // 1. Танки приоритетнее (вес 0.7), хилим даже на 95%
    // 2. Predicted HP: current + dps_taken * 2с — если упадёт < 0, хилим СЕЙЧАС
    // 3. urgency: 0=норм, 1=срочно (predicted<20%), 2=критично (predicted<0)
    // 4. Проактивные HoT: needHoT=true если танк в бою и нет HoT
    // 5. lowCount: сколько людей < 85% (для mass heal)
    private const string HealerFindTarget = @"
    local best,bestHP = nil,1
    local bestW = 999
    local urgency = 0
    local needHoT = false
    local lowCount = 0
    -- IsTankUnit уже определён в Helpers
    local function HasHoT(u)
        for i=1,40 do
            local n,_,_,_,_,_,_,_,_,_,id = UnitBuff(u,i)
            if not n then return false end
            if id==774 or id==139 or id==33763 or id==48068 or id==48441 or id==61301 then return true end
        end
        return false
    end
    -- HP трекинг между тиками (скользящее окно ~2с)
    if not WB_HPT then WB_HPT = {} end
    local now = GetTime()
    local function GetHPSpeed(u, hp)
        local key = UnitGUID(u) or u
        if not WB_HPT[key] then WB_HPT[key] = {hp=hp, t=now, dps=0} end
        local rec = WB_HPT[key]
        local dt = now - rec.t
        if dt > 0.3 then -- обновляем каждые 300мс+
            local dhp = hp - rec.hp -- изменение HP% (отрицательное = теряет)
            rec.dps = dhp / dt -- HP%/сек (отрицательное = урон)
            rec.hp = hp
            rec.t = now
        end
        return rec.dps -- HP%/сек
    end
    local function CheckUnit(u)
        if not UnitExists(u) or UnitIsDeadOrGhost(u) or not UnitIsConnected(u) then return end
        if not CheckInteractDistance(u,4) then return end
        local hp = UnitHealth(u)/UnitHealthMax(u)
        if hp >= 1.0 then return end
        local isTank = IsTankUnit(u)
        -- Predicted HP через 2с на основе реальной скорости потери HP
        local hpSpeed = GetHPSpeed(u, hp)
        local predicted = hp + hpSpeed * 2.0 -- HP% через 2 секунды
        -- urgency
        local urg = 0
        if predicted < 0 then urg = 2
        elseif predicted < 0.2 then urg = 1 end
        -- Вес: танк 0.7, urgency снижает вес дополнительно
        local w = (isTank and hp*0.7 or hp) - urg*0.3
        -- Порог входа: танк < 95%, остальные < 90%
        local threshold = isTank and 0.95 or 0.9
        if hp < threshold and w < bestW then
            best,bestW,bestHP,urgency = u,w,hp,urg
        end
        -- Считаем для mass heal
        if hp < 0.85 then lowCount = lowCount + 1 end
        -- Проактивный HoT: танк в бою > 80% без HoT
        if isTank and hp > 0.8 and hp < 0.95 and UnitAffectingCombat(u) and not HasHoT(u) then needHoT = true end
    end
    CheckUnit('player')
    local nr = GetNumRaidMembers()
    if nr > 0 then
        for i=1,nr do CheckUnit('raid'..i) end
    else
        for i=1,4 do CheckUnit('party'..i) end
    end
    -- Петы: только если все люди на фуле (низкий приоритет)
    if not best then
        if UnitExists('pet') and not UnitIsDeadOrGhost('pet') then CheckUnit('pet') end
        if nr > 0 then
            for i=1,nr do if UnitExists('raidpet'..i) then CheckUnit('raidpet'..i) end end
        else
            for i=1,4 do if UnitExists('partypet'..i) then CheckUnit('partypet'..i) end end
        end
    end
";

    private static string Wrap(string body) =>
        "local function WB_Run()\n" + Helpers + body + "\nend\nlocal ok,err=pcall(WB_Run) if not ok then WB_ERR=err print('WB_ERR: '..tostring(err)) end\n";

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
        -- PROT WARRIOR
        -- [ТАНК] Автотаунт
        if TryTaunt(355) then return end
        -- [ТАНК] Деф КД: Shield Wall(871) → Last Stand(12975) → Shield Block(2565)
        if TryDefCD(871,true, 12975,true, 2565,true) then return end
        -- [ТАНК] AoE угроза: Thunder Clap если 2+ врагов
        if WB_S.AoEThreat~=false and (WB_NCE or 0)>=2 and IR(6343) then Cast(6343) return end
        if WB_S.HS~=false and UnitMana('player')>50 and IR(47449) then Cast(47449) end
        if WB_S.BR==true and UnitMana('player')<30 and IR(2687) then Cast(2687) return end
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
        -- RET (NPCBots-inspired)
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        -- [SURVIVAL] Lay on Hands: HP < 20% (на себя)
        if PHP()<0.2 and IR(633) then CastSpellByName(SN(633),'player') return end
        -- [SURVIVAL] Divine Shield: HP < 15% бабл
        if PHP()<0.15 and IR(642) then Cast(642) return end
        -- [SURVIVAL] Art of War прок → Flash of Light на себя если HP < 50%
        if PHP()<0.5 and HB(59578) and IR(19750) then CastSpellByName(SN(19750),'player') return end
        -- Divine Plea: мана < 10%
        if WB_S.Plea~=false and MP()<0.1 and not HB(54428) and IR(54428) then Cast(54428) return end
        -- Бурст: Avenging Wrath при 3+ стаках Holy Vengeance
        if WB_S.AW~=false then local _,_,_,stk=UnitDebuff('target',SN(31803) or '') if (stk or 0)>=3 and IR(31884) then Cast(31884) return end end
        -- Hammer of Wrath: добивание < 20%
        if WB_S.HoW~=false and THP()<0.2 and IR(24275) then Cast(24275) return end
        -- Judgement
        if WB_S.Judge~=false and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        -- Divine Storm
        if WB_S.DS~=false and IR(53385) then Cast(53385) return end
        -- Crusader Strike
        if WB_S.CS~=false and IR(35395) then Cast(35395) return end
        -- Consecration: только 2+ врагов и враг не двигается (NPCBots)
        if WB_S.Cons~=false and (WB_NCE or 0)>=2 and (GetUnitSpeed('target') or 0)==0 and IR(26573) then Cast(26573) return end
        -- Exorcism: только с Art of War проком (инстант)
        if WB_S.Exo~=false and HB(59578) and IR(879) then Cast(879) return end
        -- Sacred Shield
        if WB_S.SS~=false and not HB(53601) and IR(53601) then Cast(53601) return end
    elseif t2>=t1 and t2>=t3 then
        -- PROT PALADIN (NPCBots-inspired)
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        -- [ТАНК] Автотаунт: Hand of Reckoning (62124) — на таргет
        if TryTaunt(62124) then return end
        -- [ТАНК] Righteous Defense (31789) — на союзника которого бьют (переводит 3 моба)
        if WB_S.AutoTaunt~=false and IR(31789) then
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitAffectingCombat(u) and UnitHealth(u)/UnitHealthMax(u)<0.8 and not IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,31789) return end end
            else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitAffectingCombat(u) and UnitHealth(u)/UnitHealthMax(u)<0.8 and not IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,31789) return end end end
        end
        -- [ТАНК] Деф КД: Divine Protection(498) → Holy Shield(20925)
        if TryDefCD(498,true, 20925,true) then return end
        -- [ТАНК] Hand of Protection на союзника < 20% HP
        if IR(1022) then
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitHealth(u)/UnitHealthMax(u)<0.2 and not IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,1022) return end end
            else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitHealth(u)/UnitHealthMax(u)<0.2 and not IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,1022) return end end end
        end
        -- [ТАНК] AoE угроза: Consecration если 2+ врагов
        if WB_S.AoEThreat~=false and (WB_NCE or 0)>=2 and IR(26573) then Cast(26573) return end
        -- Divine Plea + Holy Shield — поддерживать баффы
        if WB_S.Plea~=false and not HB(54428) and IR(54428) then Cast(54428) return end
        if WB_S.HolyShield~=false and not HB(20925) and IR(20925) then Cast(20925) return end
        if WB_S.SS~=false and not HB(53601) and IR(53601) then Cast(53601) return end
        -- Ротация: AS → HoR → ShoR → Judge → Cons → Holy Wrath
        if WB_S.AS~=false and IR(31935) then Cast(31935) return end
        if WB_S.HoR~=false and IR(53595) then Cast(53595) return end
        if WB_S.ShoR~=false and IR(53600) then Cast(53600) return end
        if WB_S.Judge~=false and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        if WB_S.Cons~=false and IR(26573) then Cast(26573) return end
        if WB_S.HW~=false and IR(2812) then Cast(2812) return end
    else
        -- HOLY
" + HealerFindTarget + @"
        -- Resurrect вне боя (Paladin)
        if TryRes(7328) then return end
        if false then
        end
        -- Dispel перенесён ПОСЛЕ хила (хил приоритетнее)
        -- Mana potion если мана < 20%
        if MP() < 0.2 then
            local s13,_=GetInventoryItemCooldown('player',13)
            local s14,_=GetInventoryItemCooldown('player',14)
            if s13==0 then UseInventoryItem(13) end
            if s14==0 then UseInventoryItem(14) end
        end
        local judgeSpell = WB_S.JoL==true and SN(20271) or SN(53408)
        if UnitAffectingCombat('player') and not HB(53657) and UnitExists('target') and UnitCanAttack('player','target') and judgeSpell and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        -- Beacon of Light: на фокус (приоритет) или первого танка (один раз, не перекидывать)
        if WB_S.Beacon~=false and IR(53563) then
            local bn=SN(53563)
            -- Проверяем: Beacon уже висит на ком-то в группе?
            local function AnyoneHasBeacon()
                if bn then
                    local function HasBn(u) if not UnitExists(u) then return false end for i=1,40 do local n=UnitBuff(u,i) if not n then return false end if n==bn then return true end end return false end
                    if HasBn('player') then return true end
                    local nr=GetNumRaidMembers()
                    if nr>0 then for i=1,nr do if HasBn('raid'..i) then return true end end
                    else for i=1,4 do if HasBn('party'..i) then return true end end end
                end
                return false
            end
            if not AnyoneHasBeacon() then
                -- Фокус приоритет, потом первый танк
                if UnitExists('focus') and not UnitIsDeadOrGhost('focus') and CheckInteractDistance('focus',4) then CastOn('focus',53563) return
                else
                    local nr=GetNumRaidMembers()
                    if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,53563) return end end
                    else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and IsTankUnit(u) and CheckInteractDistance(u,4) then CastOn(u,53563) return end end end
                end
            end
        end
        if WB_S.SS~=false and IR(53601) then
            local sn=SN(53601)
            local function FindWithoutBuff(buffName)
                local function Check(u)
                    if not UnitExists(u) or UnitIsDeadOrGhost(u) then return false end
                    if not IsTankUnit(u) then return false end
                    if not CheckInteractDistance(u,4) then return false end
                    if buffName then for i=1,40 do local n=UnitBuff(u,i) if not n then break end if n==buffName then return false end end end
                    return true
                end
                if UnitExists('focus') and Check('focus') then return 'focus' end
                local nr=GetNumRaidMembers()
                if nr>0 then for i=1,nr do local u='raid'..i if Check(u) then return u end end
                else for i=1,4 do local u='party'..i if Check(u) then return u end end end
                return nil
            end
            local st = FindWithoutBuff(sn)
            if st then CastOn(st,53601) return end
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
        -- Критично: urgency=2 (predicted HP < 0) → кулдауны + быстрый хил
        if urgency>=2 then
            if WB_S.DF~=false and IR(20216) then Cast(20216) end -- Divine Favor (крит)
            if WB_S.LoH~=false and bestHP<0.15 and IR(633) then CastOn(best,633) return end -- Lay on Hands
        end
        -- Срочно: urgency=1 (predicted < 20%) → Holy Shock (инстант) + Flash
        if urgency>=1 and WB_S.HS~=false and IR(20473) then CastOn(best,20473) return end
        -- Обычный хил
        if WB_S.LoH~=false and bestHP<0.15 and IR(633) then CastOn(best,633) return end
        if WB_S.HS~=false and bestHP<0.97 and IR(20473) then CastOn(best,20473) return end
        if urgency>=1 and WB_S.FL~=false then CastOn(best,19750) return end -- Flash при urgency
        if WB_S.HL~=false and bestHP<0.97 then CastOn(best,635) return end -- Holy Light (основной)
        if WB_S.FL~=false and bestHP<0.97 then CastOn(best,19750) return end
        -- Dispel ПОСЛЕ хила (хил приоритетнее)
        if WB_S.Dispel~=false then
            local function HasDD(u) for i=1,40 do local n,_,_,_,dt=UnitDebuff(u,i) if not n then return nil end if dt=='Magic' or dt=='Disease' or dt=='Poison' then return u end end return nil end
            local du=HasDD('player')
            if not du then local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do du=HasDD('raid'..i) if du then break end end else for i=1,4 do du=HasDD('party'..i) if du then break end end end end
            if du and IR(4987) then CastOn(du,4987) return end
        end
        -- [NPCBots] Hand of Freedom (1044) — снятие рутов/замедлений с союзника
        if IR(1044) then
            local function HasRoot(u)
                if not UnitExists(u) or UnitIsDeadOrGhost(u) or not CheckInteractDistance(u,4) then return false end
                for i=1,40 do local n,_,_,_,dt,_,_,_,_,_,id=UnitDebuff(u,i) if not n then return false end
                    if dt=='Magic' then
                        -- Проверяем известные руты/замедления
                        local rootIds={[339]=1,[122]=1,[45524]=1,[116]=1,[120]=1,[8056]=1,[3600]=1}
                        if rootIds[id] then return true end
                    end
                end
                return false
            end
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if HasRoot(u) then CastOn(u,1044) return end end
            else for i=1,4 do local u='party'..i if HasRoot(u) then CastOn(u,1044) return end end end
            if HasRoot('player') then CastOn('player',1044) return end
        end
        -- [NPCBots] Hand of Salvation (1038) — сброс угрозы с ДД который агрит
        if IR(1038) then
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and not IsTankUnit(u) and CheckInteractDistance(u,4) then local ts=UnitThreatSituation(u) if ts and ts>=3 then CastOn(u,1038) return end end end
            else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and not IsTankUnit(u) and CheckInteractDistance(u,4) then local ts=UnitThreatSituation(u) if ts and ts>=3 then CastOn(u,1038) return end end end end
        end
    end
");

    private static string Hunter() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then if WB_S then if WB_S.Rapid~=false and IR(3045) then Cast(3045) end if WB_S.Kill2~=false and IR(34026) then Cast(34026) end if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end end return end
    if not WB_S then WB_S={} end
    -- Управление петом (всегда если пет жив)
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
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
    if WB_S.Track~=false then local ct=UnitCreatureType('target') local tr if ct=='Животное' then tr='Выслеживание животных' elseif ct=='Демон' then tr='Выслеживание демонов' elseif ct=='Дракон' then tr='Выслеживание драконов' elseif ct=='Элементаль' then tr='Выслеживание элементалей' elseif ct=='Великан' then tr='Выслеживание великанов' elseif ct=='Гуманоид' then tr='Выслеживание гуманоидов' elseif ct=='Нежить' then tr='Выслеживание нежити' end if tr and not HasBuff(tr) then CastSpellByName(tr) return end end
    -- [NPCBots] Mend Pet: хилим пета если HP < 75%
    if UnitExists('pet') and not UnitIsDead('pet') and UnitHealth('pet')/UnitHealthMax('pet')<0.75 and IR(136) then Cast(136) end
    -- [NPCBots] Aspect of the Viper: мана < 20% → Viper, мана > 50% → обратно Dragonhawk
    if MP()<0.2 and not HB(34074) and IR(34074) then Cast(34074) return end
    if MP()>0.5 and HB(34074) then if IR(61846) then Cast(61846) elseif IR(13165) then Cast(13165) end return end
    -- [NPCBots] Feign Death: если топ-угроза и враг бьёт не нас
    if UnitThreatSituation('player') and UnitThreatSituation('player')>=3 and IR(5384) then local tv=UnitName('targettarget') local pn=UnitName('player') if tv and tv~=pn then Cast(5384) return end end
    -- [NPCBots] Disengage: если враг в мили-дистанции (НЕ для трапера)
    if not WB_S.Trapper and CheckInteractDistance('target',3) and IR(781) then Cast(781) return end
    -- [NPCBots] Misdirection: на танка в группе (каждые 30с)
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
        if WB_S.Bestial~=false and IR(19574) then Cast(19574) return end
        if WB_S.BW~=false and IR(34471) then Cast(34471) return end
        if WB_S.Serpent~=false and not HD('target',1978) then Cast(1978) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        if WB_S.Arcane~=false and IR(3044) then Cast(3044) return end
        if WB_S.Steady~=false then Cast(56641) end
    elseif t2>=t1 and t2>=t3 then
        -- MM Hunter priority rotation
        if WB_S.Dragonhawk~=false and not HB(61847) and MP()>0.3 then Cast(61847) return end
        -- Volley: через ground AoE (TerrainClick) в BotEngine (приоритет)
        -- Multi-Shot: только если стоим (не на бегу)
        if WB_S.MultiShot~=false and (WB_NCE or 0)>=(WB_AEMIN or 3) and (GetUnitSpeed('player') or 0)==0 and not UnitCastingInfo('player') and IR(2643) then Cast(2643) return end
        -- CDs: Rapid Fire + Kill Command + pet abilities (off-GCD)
        -- Быстрая стрельба: только на жирных и не на бегу
        if WB_S.Rapid~=false and IR(3045) and THP()>0.5 and (GetUnitSpeed('player') or 0)==0 then Cast(3045) end
        if WB_S.Kill2~=false and IR(34026) then Cast(34026) end
        if UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n then if n=='Раж' or n=='Неистовый вой' or n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end end end end end
        -- Tranq Shot: снимаем enrage бафф с таргета
        if IR(19801) then for i=1,40 do local n,_,_,_,dt=UnitBuff('target',i) if not n then break end if dt=='Enrage' then Cast(19801) return end end end
        -- Priority: Serpent Sting > Chimera > Silencing > Aimed > Steady
        -- Укус змеи: не ставить на дохлых (< 10% HP), на боссе 10% = норм
        if WB_S.Serpent~=false and not HD('target',1978) and THP()>0.1 then Cast(1978) return end
        -- Выстрел химеры: ТОЛЬКО если Укус змеи висит (иначе впустую)
        if WB_S.Chimera~=false and HD('target',1978) and IR(53209) then Cast(53209) return end
        if WB_S.Silence~=false and IR(34490) then Cast(34490) return end
        if WB_S.Aimed~=false and IR(19434) then Cast(19434) return end
        -- Trapper: Explosive Trap между основными кастами
        if WB_S.Trapper==true and WB_S.Trap~=false and IR(13813) then Cast(13813) return end
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
        -- DISC (urgency-aware)
" + HealerFindTarget + @"
        -- Resurrect вне боя (Priest)
        if TryRes(2006) then return end
        if false then
        end
        -- Dispel перенесён ПОСЛЕ хила
        -- Mana potion если мана < 20%
        if MP() < 0.2 then
            local s13,_=GetInventoryItemCooldown('player',13)
            local s14,_=GetInventoryItemCooldown('player',14)
            if s13==0 then UseInventoryItem(13) end
            if s14==0 then UseInventoryItem(14) end
        end
        if bestHP>=1.0 then return end
        -- Критично: Pain Suppression
        if WB_S.PS~=false and urgency>=2 and IR(33206) then CastOn(best,33206) return end
        -- PW:Shield (инстант, приоритет при urgency)
        if WB_S.PW~=false and IR(17) then CastOn(best,17) return end
        -- Prayer of Mending проактивно на танка
        if WB_S.PoM~=false and IR(33076) then
            local pomn = SN(33076)
            local hasPom = false
            if pomn and best then for i=1,40 do local n=UnitBuff(best,i) if not n then break end if n==pomn then hasPom=true break end end end
            if not hasPom and best then CastOn(best,33076) return end
        end
        -- Inner Focus + Penance (лучший хил диска)
        if WB_S.Penance~=false and bestHP<0.95 and IR(47540) then
            if IR(14751) and MP()<0.7 then Cast(14751) end
            CastOn(best,47540) return
        end
        -- Pain Suppression при низком HP
        if WB_S.PS~=false and bestHP<0.4 and IR(33206) then CastOn(best,33206) return end
        -- Flash Heal (urgency → Flash вместо ожидания)
        if urgency>=1 and WB_S.Flash~=false then CastOn(best,2061) return end
        if WB_S.Renew~=false and needHoT then CastOn(best,139) return end
        if WB_S.Flash~=false and bestHP<0.95 then CastOn(best,2061) return end
        -- Dispel ПОСЛЕ хила (Disc Priest)
        if WB_S.Dispel~=false then
            local function HasDD(u) for i=1,40 do local n,_,_,_,dt=UnitDebuff(u,i) if not n then return nil,nil end if dt=='Magic' or dt=='Disease' then return u,dt end end return nil,nil end
            local du,ddt=HasDD('player') if not du then local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do du,ddt=HasDD('raid'..i) if du then break end end else for i=1,4 do du,ddt=HasDD('party'..i) if du then break end end end end
            if du then if ddt=='Magic' and IR(527) then CastOn(du,527) return end if ddt=='Disease' and IR(552) then CastOn(du,552) return elseif ddt=='Disease' and IR(528) then CastOn(du,528) return end end
        end
    else
        -- HOLY (urgency-aware, mass heal)
" + HealerFindTarget + @"
        -- Resurrect вне боя (Priest)
        if TryRes(2006) then return end
        if false then
        end
        -- Dispel перенесён ПОСЛЕ хила
        -- Mana potion если мана < 20%
        if MP() < 0.2 then
            local s13,_=GetInventoryItemCooldown('player',13)
            local s14,_=GetInventoryItemCooldown('player',14)
            if s13==0 then UseInventoryItem(13) end
            if s14==0 then UseInventoryItem(14) end
        end
        if bestHP>=1.0 then return end
        -- Критично: Guardian Spirit
        if WB_S.Guardian~=false and urgency>=2 and IR(47788) then CastOn(best,47788) return end
        -- Mass heal: Circle of Healing если 2+ людей < 85%
        if WB_S.CoH~=false and lowCount>=2 and IR(34861) then Cast(34861) return end
        if WB_S.Guardian~=false and bestHP<0.2 and IR(47788) then CastOn(best,47788) return end
        -- Prayer of Mending проактивно на танка
        if WB_S.PoM~=false and IR(33076) then
            local pomn = SN(33076)
            local hasPom = false
            if pomn and best then for i=1,40 do local n=UnitBuff(best,i) if not n then break end if n==pomn then hasPom=true break end end end
            if not hasPom and best then CastOn(best,33076) return end
        end
        -- Проактивный HoT
        if WB_S.Renew~=false and needHoT then CastOn(best,139) return end
        if WB_S.Renew~=false and bestHP<0.95 then CastOn(best,139) return end
        -- Urgency → Flash Heal
        if urgency>=1 and WB_S.Flash~=false then CastOn(best,2061) return end
        -- Inner Focus + Greater Heal
        if WB_S.GHeal~=false and bestHP<0.6 then
            if IR(14751) and MP()<0.7 then Cast(14751) end
            CastOn(best,2060) return
        end
        if WB_S.Flash~=false and bestHP<0.95 then CastOn(best,2061) return end
        if WB_S.Binding~=false and bestHP<0.95 then CastOn(best,32546) return end
        -- Dispel ПОСЛЕ хила (Holy Priest)
        if WB_S.Dispel~=false then
            local function HasDD(u) for i=1,40 do local n,_,_,_,dt=UnitDebuff(u,i) if not n then return nil,nil end if dt=='Magic' or dt=='Disease' then return u,dt end end return nil,nil end
            local du,ddt=HasDD('player') if not du then local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do du,ddt=HasDD('raid'..i) if du then break end end else for i=1,4 do du,ddt=HasDD('party'..i) if du then break end end end end
            if du then if ddt=='Magic' and IR(527) then CastOn(du,527) return end if ddt=='Disease' and IR(552) then CastOn(du,552) return elseif ddt=='Disease' and IR(528) then CastOn(du,528) return end end
        end
    end
");

    private static string DeathKnight() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local hasFF = HD('target',55095)
    local hasBP = HD('target',55078)
    if t1>=t2 and t1>=t3 then
        -- BLOOD DK (tank)
        -- [ТАНК] Автотаунт: Dark Command (56222)
        if TryTaunt(56222) then return end
        -- [ТАНК] Деф КД: Icebound Fortitude(48792) → Vampiric Blood(55233) → Bone Shield(49222)
        if TryDefCD(48792,true, 55233,true, 49222,true) then return end
        -- [ТАНК] AoE угроза: Death and Decay если 2+ врагов
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
        -- RESTO SHAMAN (NPCBots + гайды WotLK, полная система)
" + HealerFindTarget + @"
        -- Resurrect вне боя
        if TryRes(2008) then return end
        if false then
        end
        -- Dispel перенесён ПОСЛЕ хила (хил приоритетнее)
        -- Mana potion
        if MP()<0.2 then local s13,_=GetInventoryItemCooldown('player',13) local s14,_=GetInventoryItemCooldown('player',14) if s13==0 then UseInventoryItem(13) end if s14==0 then UseInventoryItem(14) end end
        -- Mana Tide Totem: мана < 30% (КД 5 мин)
        if MP()<0.3 and IR(16190) then Cast(16190) return end
        -- Earth Shield на танке (авто-поиск, не только фокус)
        if WB_S.ES~=false and IR(974) then
            local esn=SN(974)
            local function NeedES(u) if not UnitExists(u) or UnitIsDeadOrGhost(u) or not CheckInteractDistance(u,4) then return false end if esn then for i=1,40 do local n=UnitBuff(u,i) if not n then break end if n==esn then return false end end end return true end
            -- Фокус приоритет, потом танки
            if UnitExists('focus') and NeedES('focus') then CastOn('focus',974) return end
            local nr=GetNumRaidMembers()
            if nr>0 then for i=1,nr do local u='raid'..i if NeedES(u) and IsTankUnit(u) then CastOn(u,974) return end end
            else for i=1,4 do local u='party'..i if NeedES(u) and IsTankUnit(u) then CastOn(u,974) return end end end
        end
        -- Проактивный Riptide на танка если needHoT
        if needHoT and WB_S.RT~=false and IR(61295) and best then CastOn(best,61295) return end
        if bestHP>=1.0 then return end
        -- [КРИТИЧНО] Tidal Force + NS + Healing Wave (urgency >= 2)
        if urgency>=2 then
            if IR(55198) then Cast(55198) end -- Tidal Force (+60% крит, off-GCD)
            if WB_S.NS~=false and IR(16188) then Cast(16188) CastOn(best,331) return end
        end
        -- Riptide (инстант HoT + прямой хил)
        if WB_S.RT~=false and IR(61295) then CastOn(best,61295) return end
        -- Chain Heal: приоритет на цель с Riptide (+25% хил), 2+ раненых
        if WB_S.CH~=false and lowCount>=2 and IR(1064) then
            -- Ищем цель с Riptide HoT для бонуса Chain Heal
            local rtn=SN(61295)
            local chTarget=best
            if rtn then
                local nr=GetNumRaidMembers()
                if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitHealth(u)/UnitHealthMax(u)<0.9 then for j=1,40 do local n=UnitBuff(u,j) if not n then break end if n==rtn then chTarget=u break end end if chTarget~=best then break end end end
                else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitHealth(u)/UnitHealthMax(u)<0.9 then for j=1,40 do local n=UnitBuff(u,j) if not n then break end if n==rtn then chTarget=u break end end if chTarget~=best then break end end end end
            end
            CastOn(chTarget,1064) return
        end
        -- NS при < 30%
        if WB_S.NS~=false and bestHP<0.3 and IR(16188) then Cast(16188) return end
        -- Tidal Force перед большим хилом если HP < 40%
        if bestHP<0.4 and IR(55198) then Cast(55198) end
        -- Urgency → Lesser Healing Wave
        if urgency>=1 and WB_S.LHW~=false then CastOn(best,8004) return end
        -- Healing Wave (большой хил) на танков или HP < 60%
        if WB_S.HW~=false and bestHP<0.6 then CastOn(best,331) return end
        -- Lesser Healing Wave (филлер)
        if WB_S.LHW~=false and bestHP<0.95 then CastOn(best,8004) return end
        -- Dispel ПОСЛЕ хила
        if WB_S.Dispel~=false then
            local function HasDD(u) for i=1,40 do local n,_,_,_,dt=UnitDebuff(u,i) if not n then return nil end if dt=='Curse' or dt=='Disease' or dt=='Poison' then return u end end return nil end
            local du=HasDD('player')
            if not du then local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do du=HasDD('raid'..i) if du then break end end else for i=1,4 do du=HasDD('party'..i) if du then break end end end end
            if du then if IR(51886) then CastOn(du,51886) return end if IR(526) then CastOn(du,526) return end end
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
    -- Возрождение (боевой рес) — если кто-то в пати/рейде мертв (с guard 120с)
    if WB_S.Rebirth~=false and UnitAffectingCombat('player') and IR(20484) then
        if not WB_RES then WB_RES={} end local now=GetTime()
        local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do local u='raid'..i local g=UnitGUID(u) if g and UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and (not WB_RES[g] or now-WB_RES[g]>120) then WB_RES[g]=now TargetUnit(u) Cast(20484) return end end
        else for i=1,4 do local u='party'..i local g=UnitGUID(u) if g and UnitExists(u) and UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and (not WB_RES[g] or now-WB_RES[g]>120) then WB_RES[g]=now TargetUnit(u) Cast(20484) return end end end
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
            -- CAT DPS (NPCBots + гайды WotLK)
            local hasBerserk = HasBuffById(50334) -- по aura ID (прок пухи не 50334)
            local hasMangle = HD('target',33876)
            local hasRake = HD('target',1822)
            -- Время оставшееся Дикий рев / Разорвать
            local roarLeft,ripLeft = 0,0
            local roarN,ripN = SN(52610),SN(1079)
            if roarN then for i=1,40 do local n,_,_,_,_,_,exp=UnitBuff('player',i) if not n then break end if n==roarN then roarLeft=exp-GetTime() break end end end
            if ripN then for i=1,40 do local n,_,_,_,_,_,exp=UnitDebuff('target',i) if not n then break end if n==ripN then ripLeft=exp-GetTime() break end end end
            -- Тигриное неистовство — только при энергии < 40, НЕ в Берсерке
            if WB_S.TF~=false and not hasBerserk and (UnitPower('player') or 0)<40 and IR(5217) then Cast(5217) return end
            -- Берсерк — ПОСЛЕ Тигриного (TF на КД = только что использовали)
            if WB_S.Berserk~=false and not IR(5217) and roarLeft>5 and IR(50334) then Cast(50334) return end
            -- === ФИНИШЕРЫ ===
            if cp>=1 then
                -- Дикий рев: обновить если < 5с осталось (любое кол-во КП)
                if WB_S.Roar~=false and roarLeft<5 then Cast(52610) return end
            end
            if cp>=5 then
                -- Разорвать: 5 КП, обновлять в пандемик окне (< 4с осталось), только на жирных
                if WB_S.Rip~=false and ripLeft<4 and THP()>0.25 then Cast(1079) return end
                -- Свирепый укус: только если Рев>10с И Рип>10с (или моб дохлый)
                if WB_S.FB~=false and (THP()<0.25 or (roarLeft>10 and ripLeft>10)) then Cast(22568) return end
            end
            -- === AoE: Размах (кот) если врагов >= порог ===
            if WB_S.SwipeCat~=false and (WB_NCE or 0)>=(WB_AEMIN or 3) and IR(62078) then Cast(62078) return end
            -- === БИЛДЕРЫ ===
            -- Волшебный огонь — бесплатно
            if WB_S.FF_cat~=false and not HD('target',16857) and IR(16857) then Cast(16857) return end
            -- Увечье — поддерживать дебафф
            if WB_S.Mangle~=false and not hasMangle then Cast(33876) return end
            -- Растерзать — поддерживать ДоТ
            if WB_S.Rake~=false and not hasRake then Cast(1822) return end
            -- Полоснуть — основной (TODO: проверка за спиной)
            if WB_S.Shred~=false then Cast(5221) return end
            -- Цапнуть — фоллбэк
            Cast(1082)
        else
            -- BEAR TANK
            -- [ТАНК] Автотаунт: Growl (6795)
            if TryTaunt(6795) then return end
            -- [ТАНК] Деф КД: Survival Instincts(61336) → Frenzied Regeneration(22842) → Barkskin(22812)
            if TryDefCD(61336,true, 22842,true, 22812,true) then return end
            -- [ТАНК] AoE угроза: Swipe если 2+ врагов
            if WB_S.AoEThreat~=false and (WB_NCE or 0)>=2 and IR(779) then Cast(779) return end
            if WB_S.Maul~=false and UnitMana('player')>15 then Cast(6807) end
            if WB_S.FF_bear~=false and not HD('target',16857) and IR(16857) then Cast(16857) return end
            if WB_S.Mangle_b~=false and IR(33878) then Cast(33878) return end
            if WB_S.Lacerate~=false then Cast(33745) return end
            if WB_S.Swipe~=false and IR(779) then Cast(779) return end
        end
    else
        -- RESTO DRUID (urgency-aware, proactive HoT, mass heal)
        WB_DBG='rdruid_start'
        -- Tree of Life: пробуем каст, но НЕ блокируем ротацию если не получилось
        if WB_S.ToL~=false and GetShapeshiftForm()~=5 and IR(33891) then Cast(33891) end
" + HealerFindTarget + @"
        -- Resurrect вне боя (Druid)
        if TryRes(50769) then return end
        if false then
        end
        -- Dispel: scan group for dispellable debuffs
        -- Dispel перенесён ПОСЛЕ хила
        -- Mana potion если мана < 20%
        if MP() < 0.2 then
            local s13,_=GetInventoryItemCooldown('player',13)
            local s14,_=GetInventoryItemCooldown('player',14)
            if s13==0 then UseInventoryItem(13) end
            if s14==0 then UseInventoryItem(14) end
        end
        -- Innervate: на себя или ХИЛЕРА с маной < 20% (не на ДПС)
        if IR(29166) then
            if MP() < 0.2 then CastSpellByName(SN(29166), 'player') return end
            local function IsHealerUnit(u)
                for i=1,40 do local n,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,i) if not n then break end
                    if id==33891 or id==48438 or id==974 or id==48068 or id==33076 or id==47788 or id==33206 or id==53563 then return true end
                end return false
            end
            local nr = GetNumRaidMembers()
            if nr > 0 then
                for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitPowerType(u)==0 and UnitMana(u)/UnitManaMax(u)<0.2 and IsHealerUnit(u) then CastOn(u,29166) return end end
            else
                for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitPowerType(u)==0 and UnitMana(u)/UnitManaMax(u)<0.2 and IsHealerUnit(u) then CastOn(u,29166) return end end
            end
        end
        -- Проактивный HoT: Rejuv на танка если нет HoT
        if needHoT and WB_S.Rejuv~=false then
            local rjn=SN(774) local hasRejuv=false if rjn then for i=1,40 do local n=UnitBuff(best or 'player',i) if not n then break end if n==rjn then hasRejuv=true break end end end
            if not hasRejuv and best then CastOn(best,774) return end
        end
        if bestHP>=1.0 then
            -- Все здоровы: раскидываем Rejuv по рейду/пати (проактивно)
            if WB_S.Rejuv~=false and UnitAffectingCombat('player') and MP()>0.5 then
                local rjn=SN(774)
                if rjn then
                    local nr=GetNumRaidMembers()
                    if nr>0 then
                        for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and CheckInteractDistance(u,4) then local has=false for j=1,40 do local n=UnitBuff(u,j) if not n then break end if n==rjn then has=true break end end if not has then CastOn(u,774) return end end end
                    else
                        for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and CheckInteractDistance(u,4) then local has=false for j=1,40 do local n=UnitBuff(u,j) if not n then break end if n==rjn then has=true break end end if not has then CastOn(u,774) return end end end
                    end
                    -- На себя тоже
                    local has=false for j=1,40 do local n=UnitBuff('player',j) if not n then break end if n==rjn then has=true break end end
                    if not has then Cast(774) return end
                end
            end
            -- Rejuv раскидан, все на фуле → Regrowth на танков/хилов без HoT
            if WB_S.Regrowth~=false and MP()>0.4 then
                local rgn=SN(8936)
                if rgn then
                    local function NeedRegrowth(u)
                        if not UnitExists(u) or UnitIsDeadOrGhost(u) or not CheckInteractDistance(u,4) then return false end
                        if not IsTankUnit(u) then return false end
                        for j=1,40 do local n=UnitBuff(u,j) if not n then break end if n==rgn then return false end end
                        return true
                    end
                    local nr=GetNumRaidMembers()
                    if nr>0 then for i=1,nr do local u='raid'..i if NeedRegrowth(u) then CastOn(u,8936) return end end
                    else for i=1,4 do local u='party'..i if NeedRegrowth(u) then CastOn(u,8936) return end end end
                end
            end
            -- Wild Growth на группу если КД готов
            if WB_S.WG~=false and IR(48438) and MP()>0.3 then Cast(48438) return end
            return
        end
        -- Критично: NS + Nourish (инстант большой хил)
        if WB_S.NS~=false and urgency>=2 and IR(17116) then Cast(17116) CastOn(best,50464) return end
        -- Swiftmend (инстант, требует HoT)
        if WB_S.SM~=false and (urgency>=1 or bestHP<0.7) and IR(18562) then
            local rjn,rgn=SN(774),SN(8936) local hasHot=false for i=1,40 do local n=UnitBuff(best,i) if not n then break end if (rjn and n==rjn) or (rgn and n==rgn) then hasHot=true break end end
            if hasHot then CastOn(best,18562) return end
        end
        -- Mass heal: Wild Growth если 2+ людей < 85%
        if WB_S.WG~=false and lowCount>=2 and IR(48438) then CastOn(best,48438) return end
        -- Lifebloom на танке (фокус) — 3 стака
        if WB_S.LB~=false and UnitExists('focus') then
            local lbn=SN(33763) local lbCount=0 if lbn then for i=1,40 do local n,_,_,c=UnitBuff('focus',i) if not n then break end if n==lbn then lbCount=c or 1 break end end end
            if lbCount<3 then CastOn('focus',33763) return end
        end
        -- Rejuv если нет на цели
        if WB_S.Rejuv~=false and bestHP<0.95 then
            local rjn=SN(774) local hasRejuv=false if rjn then for i=1,40 do local n=UnitBuff(best,i) if not n then break end if n==rjn then hasRejuv=true break end end end
            if not hasRejuv then CastOn(best,774) return end
        end
        -- Wild Growth одиночный при нехватке HoT
        if WB_S.WG~=false and bestHP<0.9 and IR(48438) then CastOn(best,48438) return end
        -- Regrowth при сильном уроне или urgency (проверяем что HoT не висит)
        if WB_S.Regrowth~=false and (urgency>=1 or bestHP<0.7) then
            local rgn=SN(8936) local hasRG=false if rgn and best then for i=1,40 do local n=UnitBuff(best,i) if not n then break end if n==rgn then hasRG=true break end end end
            if not hasRG then CastOn(best,8936) return end
        end
        -- Nourish — филлер (сильнее если HoT на цели)
        if WB_S.Nourish~=false and bestHP<0.95 then CastOn(best,50464) return end
        -- Dispel ПОСЛЕ хила (Resto Druid)
        if WB_S.Dispel~=false then
            local function HasDD(u) for i=1,40 do local n,_,_,_,dt=UnitDebuff(u,i) if not n then return nil,nil end if dt=='Curse' or dt=='Poison' then return u,dt end end return nil,nil end
            local du,ddt=HasDD('player') if not du then local nr=GetNumRaidMembers() if nr>0 then for i=1,nr do du,ddt=HasDD('raid'..i) if du then break end end else for i=1,4 do du,ddt=HasDD('party'..i) if du then break end end end end
            if du then if ddt=='Curse' and IR(2782) then CastOn(du,2782) return end if ddt=='Poison' and IR(2893) then CastOn(du,2893) return end end
        end
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
