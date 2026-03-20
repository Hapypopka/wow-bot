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
";

    private const string PreChecksDPS = @"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    if not UnitAffectingCombat('target') then return end
    if not UnitExists('target') then return end
    if UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player', 'target') then return end
";

    private const string PreChecksHealer = @"
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
        "local function WB_Run()\n" + Helpers + body + "\nend\nWB_Run()\n";

    private static string WrapDPS(string body) => Wrap(PreChecksDPS + body);
    private static string WrapHealer(string body) => Wrap(PreChecksHealer + body);

    // ==================== PER-CLASS SCRIPTS ====================

    private static string Warrior() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- ARMS
        if WB_S.Reck~=false and IsReady('Безрассудство') then CastSpellByName('Безрассудство') return end
        if WB_S.Execute~=false and THP()<0.2 and IsReady('Казнь') then CastSpellByName('Казнь') return end
        if WB_S.Rend~=false and not HasDebuff('target','Кровопускание') then CastSpellByName('Кровопускание') return end
        if WB_S.MS~=false and IsReady('Смертельный удар') then CastSpellByName('Смертельный удар') return end
        if WB_S.OP~=false and IsReady('Превосходство') then CastSpellByName('Превосходство') return end
        if WB_S.Slam~=false and HasBuff('Тактик кровопролития') then CastSpellByName('Мощный удар') return end
        if WB_S.Cleave~=false then CastSpellByName('Рассекающий удар') return end
        CastSpellByName('Удар героя')
    elseif t2>=t1 and t2>=t3 then
        -- FURY
        if WB_S.Reck~=false and IsReady('Безрассудство') then CastSpellByName('Безрассудство') return end
        if WB_S.Execute~=false and THP()<0.2 and IsReady('Казнь') then CastSpellByName('Казнь') return end
        if WB_S.BT~=false and IsReady('Кровожадность') then CastSpellByName('Кровожадность') return end
        if WB_S.WW~=false and IsReady('Вихрь') then CastSpellByName('Вихрь') return end
        if WB_S.Slam~=false and HasBuff('Тактик кровопролития') then CastSpellByName('Мощный удар') return end
        CastSpellByName('Удар героя')
    else
        -- PROT
        if WB_S.ShieldSlam~=false and IsReady('Мощный удар щитом') then CastSpellByName('Мощный удар щитом') return end
        if WB_S.Revenge~=false and IsReady('Реванш') then CastSpellByName('Реванш') return end
        if WB_S.Devastate~=false then CastSpellByName('Сокрушение') return end
        if WB_S.TC~=false and IsReady('Удар грома') then CastSpellByName('Удар грома') return end
        if WB_S.ShockW~=false and IsReady('Ударная волна') then CastSpellByName('Ударная волна') return end
        CastSpellByName('Удар героя')
    end
");

    private static string Paladin() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)

    if t3>=t1 and t3>=t2 then
        -- RET
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.AW~=false and IsReady('Гнев карателя') then CastSpellByName('Гнев карателя') return end
        if WB_S.HoW~=false and THP()<0.2 and IsReady('Молот гнева') then CastSpellByName('Молот гнева') return end
        if WB_S.Judge~=false and IsReady('Правосудие мудрости') then CastSpellByName('Правосудие мудрости') return end
        if WB_S.DS~=false and IsReady('Божественная буря') then CastSpellByName('Божественная буря') return end
        if WB_S.CS~=false and IsReady('Удар воина Света') then CastSpellByName('Удар воина Света') return end
        if WB_S.Cons~=false and IsReady('Освящение') then CastSpellByName('Освящение') return end
        if WB_S.Exo~=false and HasBuff('Искусство войны') and IsReady('Экзорцизм') then CastSpellByName('Экзорцизм') return end
        if WB_S.SS~=false and not HasBuff('Священный щит') and IsReady('Священный щит') then CastSpellByName('Священный щит') return end
    elseif t2>=t1 and t2>=t3 then
        -- PROT
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.AW~=false and IsReady('Гнев карателя') then CastSpellByName('Гнев карателя') return end
        if WB_S.HoR~=false and IsReady('Молот праведника') then CastSpellByName('Молот праведника') return end
        if WB_S.ShoR~=false and IsReady('Щит праведности') then CastSpellByName('Щит праведности') return end
        if WB_S.HolyShield~=false and IsReady('Щит небес') then CastSpellByName('Щит небес') return end
        if WB_S.Judge~=false and IsReady('Правосудие мудрости') then CastSpellByName('Правосудие мудрости') return end
        if WB_S.Cons~=false and IsReady('Освящение') then CastSpellByName('Освящение') return end
        if WB_S.HW~=false and IsReady('Гнев небес') then CastSpellByName('Гнев небес') return end
        if WB_S.AS~=false and IsReady('Щит мстителя') then CastSpellByName('Щит мстителя') return end
    else
        -- HOLY
" + HealerFindTarget + @"
        local judgeSpell = WB_S.JoL==true and 'Правосудие света' or 'Правосудие мудрости'
        if UnitAffectingCombat('player') and not HasBuff('Безупречное правосудие') and UnitExists('target') and UnitCanAttack('player','target') and IsReady(judgeSpell) then CastSpellByName(judgeSpell) return end
        if WB_S.Beacon~=false and UnitExists('focus') and IsReady('Частица Света') then
            local hasB=false for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n=='Частица Света' then hasB=true break end end
            if not hasB then TargetUnit('focus') CastSpellByName('Частица Света') TargetLastTarget() return end
        end
        if WB_S.SS~=false and UnitExists('focus') and IsReady('Священный щит') then
            local hasS=false for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n=='Священный щит' then hasS=true break end end
            if not hasS then TargetUnit('focus') CastSpellByName('Священный щит') TargetLastTarget() return end
        end
        if WB_S.Plea~=false and MP()<0.7 and IsReady('Святая клятва') then
            if IsReady('Гнев карателя') then CastSpellByName('Гнев карателя')
            elseif IsReady('Божественное просветление') then CastSpellByName('Божественное просветление')
            else UseInventoryItem(13) UseInventoryItem(14) end
            CastSpellByName('Святая клятва') return
        end
        if bestHP>=1.0 then
            if UnitAffectingCombat('player') and UnitExists('target') and UnitCanAttack('player','target') and not HasDebuff('target',judgeSpell) and IsReady(judgeSpell) then CastSpellByName(judgeSpell) end
            return
        end
        if WB_S.DF~=false and bestHP<0.3 and IsReady('Божественное одобрение') then CastSpellByName('Божественное одобрение') end
        if WB_S.LoH~=false and bestHP<0.15 and IsReady('Возложение рук') then TargetUnit(best) CastSpellByName('Возложение рук') return end
        if WB_S.HS~=false and bestHP<0.97 and IsReady('Шок небес') then TargetUnit(best) CastSpellByName('Шок небес') return end
        if WB_S.HL~=false and bestHP<0.97 then TargetUnit(best) CastSpellByName('Свет небес') return end
        if WB_S.FL~=false and bestHP<0.97 then TargetUnit(best) CastSpellByName('Вспышка Света') return end
    end
");

    private static string Hunter() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
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
    if not HasDebuff('target','Метка охотника') and WB_S.Mark~=false then CastSpellByName('Метка охотника') return end
    if WB_S.Kill~=false and THP()<0.2 and IsReady('Убийственный выстрел') then CastSpellByName('Убийственный выстрел') return end
    if t1>=t2 and t1>=t3 then
        if WB_S.Bestial~=false and IsReady('Звериный гнев') then CastSpellByName('Звериный гнев') return end
        if WB_S.BW~=false and IsReady('Повелитель зверей') then CastSpellByName('Повелитель зверей') return end
        if WB_S.Serpent~=false and not HasDebuff('target','Укус змеи') then CastSpellByName('Укус змеи') return end
        if WB_S.Aimed~=false and IsReady('Прицельный выстрел') then CastSpellByName('Прицельный выстрел') return end
        if WB_S.Arcane~=false and IsReady('Чародейский выстрел') then CastSpellByName('Чародейский выстрел') return end
        if WB_S.Steady~=false then CastSpellByName('Верный выстрел') end
    elseif t2>=t1 and t2>=t3 then
        if WB_S.Dragonhawk~=false and not HasBuff('Дух дракондора') and MP()>0.3 then CastSpellByName('Дух дракондора') return end
        if WB_S.CotW~=false and UnitClassification('target')=='worldboss' and UnitExists('pet') and not UnitIsDead('pet') then for i=1,10 do local n=GetPetActionInfo(i) if n and n=='Зов дикой природы' then local _,_,_,_,_,cd=GetPetActionCooldown(i) if cd==0 then CastPetAction(i) end break end end end
        if WB_S.Rapid~=false and IsReady('Быстрая стрельба') then CastSpellByName('Быстрая стрельба') return end
        if WB_S.Serpent~=false and not HasDebuff('target','Укус змеи') then CastSpellByName('Укус змеи') return end
        if WB_S.Chimera~=false and IsReady('Выстрел химеры') then CastSpellByName('Выстрел химеры') return end
        if WB_S.Volley~=false and (WB_NE or 0)>1 and IsReady('Залп') then CastSpellByName('Залп') return end
        if WB_S.Trap~=false and IsReady('Взрывная ловушка') and CheckInteractDistance('target',3) then CastSpellByName('Взрывная ловушка') return end
        if WB_S.Aimed~=false and IsReady('Прицельный выстрел') then CastSpellByName('Прицельный выстрел') return end
        if WB_S.Silence~=false and IsReady('Глушащий выстрел') then CastSpellByName('Глушащий выстрел') return end
        if WB_S.Readiness~=false then local chCD=(WB_S.Chimera~=false) and CDLeft('Выстрел химеры') or 0 local rpCD=(WB_S.Rapid~=false) and CDLeft('Быстрая стрельба') or 0 if chCD>5 and rpCD>5 and IsReady('Готовность') then CastSpellByName('Готовность') return end end
        if WB_S.Steady~=false then local chCD=(WB_S.Chimera~=false) and CDLeft('Выстрел химеры') or 99 local aiCD=(WB_S.Aimed~=false) and CDLeft('Прицельный выстрел') or 99 local ksCD=(WB_S.Kill~=false and THP()<0.2) and CDLeft('Убийственный выстрел') or 99 if chCD>2 and aiCD>2 and ksCD>2 then CastSpellByName('Верный выстрел') end end
    else
        if WB_S.Explosive~=false and IsReady('Разрывной выстрел') then CastSpellByName('Разрывной выстрел') return end
        if WB_S.Black~=false and not HasDebuff('target','Черная стрела') and IsReady('Черная стрела') then CastSpellByName('Черная стрела') return end
        if WB_S.Serpent~=false and not HasDebuff('target','Укус змеи') then CastSpellByName('Укус змеи') return end
        if WB_S.Aimed~=false and IsReady('Прицельный выстрел') then CastSpellByName('Прицельный выстрел') return end
        if WB_S.Arcane~=false and IsReady('Чародейский выстрел') then CastSpellByName('Чародейский выстрел') return end
        if WB_S.Steady~=false then CastSpellByName('Верный выстрел') end
    end
");

    private static string Rogue() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local cp = CP()
    if t1>=t2 and t1>=t3 then
        if WB_S.Envenom~=false and cp>=4 and IsReady('Расправа') then CastSpellByName('Расправа') return end
        if WB_S.Rupture~=false and cp>=4 and not HasDebuff('target','Рваная рана') then CastSpellByName('Рваная рана') return end
        if WB_S.HFB~=false and not HasBuff('Жажда убийства') and IsReady('Жажда убийства') then CastSpellByName('Жажда убийства') return end
        if WB_S.Mutilate~=false then CastSpellByName('Увечье') end
    elseif t2>=t1 and t2>=t3 then
        if WB_S.SnD~=false and cp>=1 and not HasBuff('Потрошение') then CastSpellByName('Потрошение') return end
        if WB_S.Rupture~=false and cp>=5 and not HasDebuff('target','Рваная рана') then CastSpellByName('Рваная рана') return end
        if WB_S.Evis~=false and cp>=5 then CastSpellByName('Потрошение') return end
        if WB_S.KS~=false and IsReady('Череда убийств') then CastSpellByName('Череда убийств') return end
        if WB_S.SS~=false then CastSpellByName('Коварный удар') end
    else
        if WB_S.Evis~=false and cp>=5 then CastSpellByName('Потрошение') return end
        if WB_S.Rupture~=false and cp>=5 and not HasDebuff('target','Рваная рана') then CastSpellByName('Рваная рана') return end
        if WB_S.Hemo~=false and not HasDebuff('target','Кровоизлияние') then CastSpellByName('Кровоизлияние') return end
        if WB_S.BS~=false then CastSpellByName('Удар в спину') end
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
        if not HasBuff('Облик Тьмы') then CastSpellByName('Облик Тьмы') return end
        if WB_S.Disp~=false and MP()<0.15 and IsReady('Слияние с Тьмой') then CastSpellByName('Слияние с Тьмой') return end
        if WB_S.VT~=false and not HasDebuff('target','Прикосновение вампира') then if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') return end end
        if WB_S.DP~=false and not HasDebuff('target','Всепожирающая чума') then if not WB_DP or GetTime()-WB_DP>2 then WB_DP=GetTime() CastSpellByName('Всепожирающая чума') return end end
        if WB_S.SWP~=false and not HasDebuff('target','Слово Тьмы: Боль') then if not WB_SWP or GetTime()-WB_SWP>2 then WB_SWP=GetTime() CastSpellByName('Слово Тьмы: Боль') return end end
        local _,_,_,_,mbPts = GetTalentInfo(3,8)
        if WB_S.MB~=false and mbPts and mbPts>0 and IsReady('Взрыв разума') then CastSpellByName('Взрыв разума') return end
        if WB_S.SF~=false and MP()<0.5 and IsReady('Исчадие Тьмы') then CastSpellByName('Исчадие Тьмы') return end
        if WB_S.MF~=false then CastSpellByName('Пытка разума') end
    elseif t1>=t2 then
        -- DISC
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.PW~=false and bestHP<0.9 and IsReady('Слово силы: Щит') then TargetUnit(best) CastSpellByName('Слово силы: Щит') return end
        if WB_S.Penance~=false and bestHP<0.85 and IsReady('Исповедь') then TargetUnit(best) CastSpellByName('Исповедь') return end
        if WB_S.PS~=false and bestHP<0.4 and IsReady('Подавление боли') then TargetUnit(best) CastSpellByName('Подавление боли') return end
        if WB_S.Flash~=false and bestHP<0.7 then TargetUnit(best) CastSpellByName('Быстрое исцеление') return end
        if WB_S.PoM~=false and IsReady('Молитва восстановления') then TargetUnit(best) CastSpellByName('Молитва восстановления') return end
        if WB_S.Renew~=false and bestHP<0.9 then TargetUnit(best) CastSpellByName('Обновление') return end
    else
        -- HOLY
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.CoH~=false and bestHP<0.85 and IsReady('Круг исцеления') then CastSpellByName('Круг исцеления') return end
        if WB_S.Guardian~=false and bestHP<0.2 and IsReady('Оберегающий дух') then TargetUnit(best) CastSpellByName('Оберегающий дух') return end
        if WB_S.PoM~=false and IsReady('Молитва восстановления') then TargetUnit(best) CastSpellByName('Молитва восстановления') return end
        if WB_S.Renew~=false and bestHP<0.9 then TargetUnit(best) CastSpellByName('Обновление') return end
        if WB_S.Flash~=false and bestHP<0.7 then TargetUnit(best) CastSpellByName('Быстрое исцеление') return end
        if WB_S.GHeal~=false and bestHP<0.5 then TargetUnit(best) CastSpellByName('Великое исцеление') return end
        if WB_S.Binding~=false and bestHP<0.8 then TargetUnit(best) CastSpellByName('Связующее исцеление') return end
    end
");

    private static string DeathKnight() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    local hasFF = HasDebuff('target','Озноб')
    local hasBP = HasDebuff('target','Кровавая чума')
    if t1>=t2 and t1>=t3 then
        -- BLOOD (tank)
        if WB_S.IT~=false and not hasFF then CastSpellByName('Ледяное прикосновение') return end
        if WB_S.PS~=false and not hasBP then CastSpellByName('Удар чумы') return end
        if WB_S.Pest~=false and hasFF and hasBP and IsReady('Мор') then CastSpellByName('Мор') return end
        if WB_S.VB~=false and PHP()<0.5 and IsReady('Кровь вампира') then CastSpellByName('Кровь вампира') return end
        if WB_S.DS~=false and IsReady('Удар смерти') then CastSpellByName('Удар смерти') return end
        if WB_S.HS~=false and IsReady('Удар в сердце') then CastSpellByName('Удар в сердце') return end
        if WB_S.BS~=false then CastSpellByName('Кровавый удар') return end
        if WB_S.RS~=false and IsReady('Рунический удар') then CastSpellByName('Рунический удар') end
    elseif t2>=t1 and t2>=t3 then
        -- FROST
        if WB_S.IT~=false and not hasFF then CastSpellByName('Ледяное прикосновение') return end
        if WB_S.PS~=false and not hasBP then CastSpellByName('Удар чумы') return end
        if WB_S.Pest~=false and hasFF and hasBP and IsReady('Мор') then CastSpellByName('Мор') return end
        if WB_S.UA~=false and IsReady('Несокрушимая броня') then CastSpellByName('Несокрушимая броня') return end
        if WB_S.HB~=false and HasBuff('Морозный жар') and IsReady('Ледяной удар') then CastSpellByName('Ледяной удар') return end
        if WB_S.Oblit~=false and IsReady('Уничтожение') then CastSpellByName('Уничтожение') return end
        if WB_S.BS~=false then CastSpellByName('Кровавый удар') return end
        if WB_S.FS~=false and IsReady('Лик смерти') then CastSpellByName('Лик смерти') return end
        if WB_S.HB2~=false and IsReady('Ледяной удар') then CastSpellByName('Ледяной удар') end
    else
        -- UNHOLY
        if WB_S.IT~=false and not hasFF then CastSpellByName('Ледяное прикосновение') return end
        if WB_S.PS~=false and not hasBP then CastSpellByName('Удар чумы') return end
        if WB_S.Pest~=false and hasFF and hasBP and IsReady('Мор') then CastSpellByName('Мор') return end
        if WB_S.Gargoyle~=false and IsReady('Призыв горгульи') then CastSpellByName('Призыв горгульи') return end
        if WB_S.UB~=false and IsReady('Нечестивая порча') then CastSpellByName('Нечестивая порча') return end
        if WB_S.DnD~=false and IsReady('Смерть и разложение') then CastSpellByName('Смерть и разложение') return end
        if WB_S.SS~=false and IsReady('Удар Плети') then CastSpellByName('Удар Плети') return end
        if WB_S.BS~=false then CastSpellByName('Кровавый удар') return end
        if WB_S.DC~=false and IsReady('Лик смерти') then CastSpellByName('Лик смерти') end
    end
");

    private static string Shaman() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t3>=t1 and t3>=t2 then
        -- RESTO
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.RT~=false and bestHP<0.9 and IsReady('Быстрина') then TargetUnit(best) CastSpellByName('Быстрина') return end
        if WB_S.NS~=false and bestHP<0.3 and IsReady('Природная стремительность') then CastSpellByName('Природная стремительность') return end
        if WB_S.CH~=false and bestHP<0.75 and IsReady('Цепное исцеление') then TargetUnit(best) CastSpellByName('Цепное исцеление') return end
        if WB_S.LHW~=false and bestHP<0.7 then TargetUnit(best) CastSpellByName('Малая волна исцеления') return end
        if WB_S.HW~=false and bestHP<0.5 then TargetUnit(best) CastSpellByName('Волна исцеления') return end
        if WB_S.ES~=false and UnitExists('focus') and IsReady('Щит земли') then
            local hasES=false for i=1,40 do local n=UnitBuff('focus',i) if not n then break end if n=='Щит земли' then hasES=true break end end
            if not hasES then TargetUnit('focus') CastSpellByName('Щит земли') TargetLastTarget() return end
        end
    else
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if t1>=t2 and t1>=t3 then
            -- ELEMENTAL
            if WB_S.FS~=false and not HasDebuff('target','Огненный шок') then CastSpellByName('Огненный шок') return end
            if WB_S.LvB~=false and IsReady('Вскипание лавы') then CastSpellByName('Вскипание лавы') return end
            if WB_S.TnL~=false and IsReady('Гром и молния') then CastSpellByName('Гром и молния') return end
            if WB_S.CL~=false and IsReady('Цепная молния') then CastSpellByName('Цепная молния') return end
            if WB_S.LB~=false then CastSpellByName('Молния') end
        else
            -- ENHANCEMENT
            if WB_S.LS~=false and not HasBuff('Щит молний') then CastSpellByName('Щит молний') return end
            if WB_S.Wolves~=false and IsReady('Дух дикого волка') then CastSpellByName('Дух дикого волка') return end
            if WB_S.SR~=false and IsReady('Ярость шамана') then CastSpellByName('Ярость шамана') return end
            if WB_S.SS~=false and IsReady('Удар бури') then CastSpellByName('Удар бури') return end
            if WB_S.FS~=false and not HasDebuff('target','Огненный шок') then CastSpellByName('Огненный шок') return end
            if WB_S.ES~=false and IsReady('Земной шок') then CastSpellByName('Земной шок') return end
            if WB_S.LvB~=false and HasBuff('Водоворот готов!') and IsReady('Вскипание лавы') then CastSpellByName('Вскипание лавы') return end
            if WB_S.LB_MW~=false and HasBuff('Водоворот готов!') then CastSpellByName('Молния') end
        end
    end
");

    private static string Mage() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- ARCANE
        if WB_S.AP~=false and IsReady('Мощь тайной магии') then CastSpellByName('Мощь тайной магии') return end
        if WB_S.Mirror~=false and IsReady('Зеркальное изображение') then CastSpellByName('Зеркальное изображение') return end
        if WB_S.Barrage~=false and HasBuff('Шквал чародейских стрел') and IsReady('Чародейские стрелы') then CastSpellByName('Чародейские стрелы') return end
        if WB_S.Evoc~=false and MP()<0.35 and IsReady('Прилив сил') then CastSpellByName('Прилив сил') return end
        if WB_S.AB~=false then CastSpellByName('Чародейская вспышка') end
    elseif t2>=t1 and t2>=t3 then
        -- FIRE
        if WB_S.Mirror~=false and IsReady('Зеркальное изображение') then CastSpellByName('Зеркальное изображение') return end
        if WB_S.Combust~=false and IsReady('Возгорание') then CastSpellByName('Возгорание') return end
        if WB_S.LB~=false and not HasDebuff('target','Живая бомба') then CastSpellByName('Живая бомба') return end
        if WB_S.Pyro~=false and HasBuff('Жар души!') and IsReady('Огненная глыба') then CastSpellByName('Огненная глыба') return end
        if WB_S.Scorch~=false and not HasDebuff('target','Ожог') then CastSpellByName('Ожог') return end
        if WB_S.FB~=false then CastSpellByName('Огненный шар') end
    else
        -- FROST
        if WB_S.Mirror~=false and IsReady('Зеркальное изображение') then CastSpellByName('Зеркальное изображение') return end
        if WB_S.DF~=false and IsReady('Глубокая заморозка') then CastSpellByName('Глубокая заморозка') return end
        if WB_S.IL~=false and HasBuff('Пальцы мороза') and IsReady('Ледяное копье') then CastSpellByName('Ледяное копье') return end
        if WB_S.FFB~=false and HasBuff('Заморозка разума') then CastSpellByName('Стрела ледяного огня') return end
        if WB_S.FBolt~=false then CastSpellByName('Ледяная стрела') end
    end
");

    private static string Warlock() => WrapDPS(@"
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)
    if t1>=t2 and t1>=t3 then
        -- AFFLICTION
        if WB_S.LifeTap~=false and MP()<0.15 then CastSpellByName('Жизнеотвод') return end
        if WB_S.Haunt~=false and IsReady('Блуждающий дух') then CastSpellByName('Блуждающий дух') return end
        if WB_S.UA~=false and not HasDebuff('target','Нестабильное колдовство') then CastSpellByName('Нестабильное колдовство') return end
        if WB_S.Corruption~=false and not HasDebuff('target','Порча') then CastSpellByName('Порча') return end
        if WB_S.CoA==true and not HasDebuff('target','Проклятие агонии') then CastSpellByName('Проклятие агонии') return end
        if WB_S.CoE==true and not HasDebuff('target','Проклятие стихий') then CastSpellByName('Проклятие стихий') return end
        if WB_S.Immolate~=false and not HasDebuff('target','Жертвенный огонь') then CastSpellByName('Жертвенный огонь') return end
        if WB_S.DF~=false and IsReady('Неистовство Тьмы') then CastSpellByName('Неистовство Тьмы') return end
        if WB_S.LTGlyph==true and not HasBuff('Жизнеотвод') then CastSpellByName('Жизнеотвод') return end
        if WB_S.LifeTap~=false and MP()<0.3 then CastSpellByName('Жизнеотвод') return end
        if WB_S.ShadowBolt~=false then CastSpellByName('Стрела Тьмы') end
    elseif t2>=t1 and t2>=t3 then
        -- DEMONOLOGY
        if WB_S.Meta~=false and not HasBuff('Метаморфоза') and IsReady('Метаморфоза') then CastSpellByName('Метаморфоза') return end
        if WB_S.DemonEmpower~=false and IsReady('Демоническое могущество') then CastSpellByName('Демоническое могущество') return end
        if WB_S.LifeTap~=false and MP()<0.15 then CastSpellByName('Жизнеотвод') return end
        if WB_S.ImmoAura~=false and HasBuff('Метаморфоза') and IsReady('Жертвенный костер') and CheckInteractDistance('target',3) then CastSpellByName('Жертвенный костер') return end
        if WB_S.Corruption~=false and not HasDebuff('target','Порча') then CastSpellByName('Порча') return end
        if WB_S.Immolate~=false and not HasDebuff('target','Жертвенный огонь') then CastSpellByName('Жертвенный огонь') return end
        if WB_S.CoA==true and not HasDebuff('target','Проклятие агонии') then CastSpellByName('Проклятие агонии') return end
        if WB_S.CoD==true and not HasDebuff('target','Проклятие рока') then CastSpellByName('Проклятие рока') return end
        if WB_S.CoE==true and not HasDebuff('target','Проклятие стихий') then CastSpellByName('Проклятие стихий') return end
        if WB_S.SoulFire~=false and HasBuff('Истребление') and IsReady('Ожог души') then CastSpellByName('Ожог души') return end
        if WB_S.Incinerate~=false and HasBuff('Огненные недра') then CastSpellByName('Испепеление') return end
        if WB_S.LTGlyph==true and not HasBuff('Жизнеотвод') then CastSpellByName('Жизнеотвод') return end
        if WB_S.LifeTap~=false and MP()<0.3 then CastSpellByName('Жизнеотвод') return end
        if WB_S.ShadowBolt~=false then CastSpellByName('Стрела Тьмы') end
    else
        -- DESTRUCTION
        if WB_S.LifeTap~=false and MP()<0.15 then CastSpellByName('Жизнеотвод') return end
        if WB_S.Immolate~=false and not HasDebuff('target','Жертвенный огонь') then CastSpellByName('Жертвенный огонь') return end
        if WB_S.Chaos~=false and IsReady('Стрела Хаоса') then CastSpellByName('Стрела Хаоса') return end
        if WB_S.Conflag~=false and IsReady('Поджигание') then CastSpellByName('Поджигание') return end
        if WB_S.Corruption~=false and not HasDebuff('target','Порча') then CastSpellByName('Порча') return end
        if WB_S.CoD==true and not HasDebuff('target','Проклятие рока') then CastSpellByName('Проклятие рока') return end
        if WB_S.CoE==true and not HasDebuff('target','Проклятие стихий') then CastSpellByName('Проклятие стихий') return end
        if WB_S.LTGlyph==true and not HasBuff('Жизнеотвод') then CastSpellByName('Жизнеотвод') return end
        if WB_S.LifeTap~=false and MP()<0.3 then CastSpellByName('Жизнеотвод') return end
        if WB_S.Incinerate~=false then CastSpellByName('Испепеление') end
    end
");

    private static string Druid() => Wrap(@"
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    if UnitIsDeadOrGhost('player') then return end
    if not WB_S then WB_S={} end
    local _,_,t1 = GetTalentTabInfo(1)
    local _,_,t2 = GetTalentTabInfo(2)
    local _,_,t3 = GetTalentTabInfo(3)

    if t1>=t2 and t1>=t3 then
        -- BALANCE
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        if WB_S.Moonkin~=false and not HasBuff('Облик лунного совуха') then CastSpellByName('Облик лунного совуха') return end
        if WB_S.Innervate~=false and MP()<0.3 and IsReady('Озарение') then CastSpellByName('Озарение') return end
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
    elseif t2>=t1 and t2>=t3 then
        -- FERAL (Cat/Bear по форме)
        if not UnitAffectingCombat('target') then return end
        if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
        local inCat = HasBuffById(768)
        local inBear = HasBuffById(9634) or HasBuffById(5487)
        if not inCat and not inBear then
            if WB_S.Bear==true then CastSpellByName('Облик лютого медведя') else CastSpellByName('Облик кошки') end
            return
        end
        if inCat then
            local cp = CP()
            if WB_S.Berserk~=false and IsReady('Берсерк') then CastSpellByName('Берсерк') return end
            if WB_S.TF~=false and IsReady('Тигриное неистовство') then CastSpellByName('Тигриное неистовство') return end
            if WB_S.Roar~=false and cp>=1 and not HasBuff('Дикий рев') then CastSpellByName('Дикий рев') return end
            if WB_S.Mangle~=false and not HasDebuff('target','Увечье (кошка)') then CastSpellByName('Увечье (кошка)') return end
            if WB_S.Rake~=false and not HasDebuff('target','Растерзать') then CastSpellByName('Растерзать') return end
            if WB_S.Rip~=false and cp>=5 and not HasDebuff('target','Разорвать') then CastSpellByName('Разорвать') return end
            if WB_S.FB~=false and cp>=5 then CastSpellByName('Свирепый укус') return end
            if WB_S.Shred~=false then CastSpellByName('Полоснуть') return end
            CastSpellByName('Цапнуть')
        else
            if WB_S.FF_bear~=false and not HasDebuff('target','Волшебный огонь (зверь)') then CastSpellByName('Волшебный огонь (зверь)') return end
            if WB_S.Mangle_b~=false and IsReady('Увечье (медведь)') then CastSpellByName('Увечье (медведь)') return end
            if WB_S.Lacerate~=false then CastSpellByName('Растерзать') return end
            if WB_S.Swipe~=false and IsReady('Размах (медведь)') then CastSpellByName('Размах (медведь)') return end
            if WB_S.Maul~=false then CastSpellByName('Трепка') end
        end
    else
        -- RESTO
        if WB_S.ToL~=false and not HasBuff('Древо Жизни') and IsReady('Древо Жизни') then CastSpellByName('Древо Жизни') return end
" + HealerFindTarget + @"
        if bestHP>=1.0 then return end
        if WB_S.NS~=false and bestHP<0.3 and IsReady('Природная стремительность') then CastSpellByName('Природная стремительность') return end
        if WB_S.WG~=false and bestHP<0.85 and IsReady('Буйный рост') then CastSpellByName('Буйный рост') return end
        if WB_S.SM~=false and bestHP<0.2 and IsReady('Быстрое восстановление') then TargetUnit(best) CastSpellByName('Быстрое восстановление') return end
        if WB_S.Rejuv~=false and bestHP<0.9 then TargetUnit(best) CastSpellByName('Омоложение') return end
        if WB_S.LB~=false and bestHP<0.85 then TargetUnit(best) CastSpellByName('Жизнецвет') return end
        if WB_S.Regrowth~=false and bestHP<0.6 then TargetUnit(best) CastSpellByName('Восстановление') return end
        if WB_S.Nourish~=false and bestHP<0.7 then TargetUnit(best) CastSpellByName('Целительное прикосновение') return end
    end
");

    // ==================== PUBLIC API ====================

    public static string GetFullScript(string playerClass = "") => playerClass switch
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
    if IsReady('Звездопад') then CastSpellByName('Звездопад') return end
    if not HasDebuff('target','Волшебный огонь') then CastSpellByName('Волшебный огонь') return end
    if not HasDebuff('target','Рой насекомых') then CastSpellByName('Рой насекомых') return end
    if not HasDebuff('target','Лунный огонь') then CastSpellByName('Лунный огонь') return end",
        "PRIEST" => @"
    if not HasDebuff('target','Всепожирающая чума') then CastSpellByName('Всепожирающая чума') return end
    if not HasDebuff('target','Слово Тьмы: Боль') then CastSpellByName('Слово Тьмы: Боль') return end",
        "WARLOCK" => @"
    if not HasDebuff('target','Порча') then CastSpellByName('Порча') return end",
        "PALADIN" => @"
    if IsReady('Молот гнева') then CastSpellByName('Молот гнева') return end
    if IsReady('Правосудие мудрости') then CastSpellByName('Правосудие мудрости') return end
    if IsReady('Божественная буря') then CastSpellByName('Божественная буря') return end
    if IsReady('Удар воина Света') then CastSpellByName('Удар воина Света') return end",
        "HUNTER" => @"
    if IsReady('Убийственный выстрел') then CastSpellByName('Убийственный выстрел') return end
    if IsReady('Чародейский выстрел') then CastSpellByName('Чародейский выстрел') return end",
        "DEATHKNIGHT" => @"
    if not HasDebuff('target','Озноб') then CastSpellByName('Ледяное прикосновение') return end
    if not HasDebuff('target','Кровавая чума') then CastSpellByName('Удар чумы') return end",
        "MAGE" => @"
    if HasBuff('Пальцы мороза') then CastSpellByName('Ледяное копье') return end
    if HasBuff('Жар души!') then CastSpellByName('Огненная глыба') return end",
        _ => ""
    }) + @"
end
WB_Inst()
";
}
