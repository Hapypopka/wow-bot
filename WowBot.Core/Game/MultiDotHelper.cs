using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

/// <summary>
/// AoE Multi-Dot: Shadow Priest мультидот ротация.
/// Собирает имена мобов рядом из ObjectManager, генерирует Lua-скрипт
/// который дотает все таргеты VT → DP → SWP → Mind Blast → Mind Sear/Flay.
/// Вынесено из BotEngine.
/// </summary>
public class MultiDotHelper
{
    private readonly ObjectManager _objectManager;

    public MultiDotHelper(ObjectManager objectManager)
    {
        _objectManager = objectManager;
    }

    /// <summary>
    /// Получить скрипт: мультидот если SP + AoE, иначе обычная ротация.
    /// </summary>
    public string GetRotationScript(WowPlayer player, string fullScript,
        bool aoeEnabled, bool useMultiDot, int maxDotTargets,
        bool useMindSear, int mindSearTargets,
        int dispManaThreshold, int sfManaThreshold)
    {
        if (!aoeEnabled || !useMultiDot)
            return fullScript;

        var mainTarget = _objectManager.GetTarget();
        if (mainTarget == null || !mainTarget.IsAlive)
            return fullScript;

        // Собираем уникальные имена мобов рядом
        var nearbyNames = new List<string>();
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (unit.Guid == mainTarget.Guid) continue;
            if (player.DistanceTo(unit) > 30f) continue;

            string name = unit.Name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!nearbyNames.Contains(name))
                nearbyNames.Add(name);
            if (nearbyNames.Count >= maxDotTargets)
                break;
        }

        if (nearbyNames.Count == 0)
            return fullScript;

        // Мобы в 10м от таргета (для Mind Sear)
        int unitsNearTarget = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (unit.Guid == mainTarget.Guid) continue;
            if (mainTarget.DistanceTo(unit) <= 10f)
                unitsNearTarget++;
        }

        var luaNames = string.Join(",", nearbyNames.Select(n => "'" + n.Replace("'", "\\'") + "'"));
        int mindSearCount = useMindSear ? mindSearTargets : 999;
        float dispThreshold = dispManaThreshold / 100f;
        float sfThreshold = sfManaThreshold / 100f;

        return @"
local function WB_AoE()
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    local gS,gD = GetSpellCooldown('Прикосновение вампира')
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') or UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player','target') then return end

    local function IsReady(name)
        local s,d=GetSpellCooldown(name)
        return s~=nil and s==0
    end
    local function HasBuff(name)
        for i=1,40 do local n=UnitBuff('player',i) if not n then return false end if n==name then return true end end
        return false
    end
    local function HasDebuff(unit,name)
        for i=1,40 do local n=UnitDebuff(unit,i) if not n then return false end if n==name then return true end end
        return false
    end
    local function MP()
        local m,mm=UnitMana('player'),UnitManaMax('player')
        if mm==0 then return 1 end
        return m/mm
    end

    if not HasBuff('Облик Тьмы') then CastSpellByName('Облик Тьмы') return end
    if MP() < " + dispThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" and IsReady('Слияние с Тьмой') then CastSpellByName('Слияние с Тьмой') return end
    if MP() < " + sfThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" and IsReady('Исчадие Тьмы') then CastSpellByName('Исчадие Тьмы') return end

    if not HasDebuff('target','Прикосновение вампира') then if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') return end end
    if not HasDebuff('target','Всепожирающая чума') then if not WB_DP or GetTime()-WB_DP>2 then WB_DP=GetTime() CastSpellByName('Всепожирающая чума') return end end
    if not HasDebuff('target','Слово Тьмы: Боль') then if not WB_SWP or GetTime()-WB_SWP>2 then WB_SWP=GetTime() CastSpellByName('Слово Тьмы: Боль') return end end

    local mainGUID=UnitGUID('target')
    local casted=false
    local mobs={" + luaNames + @"}
    for _,name in ipairs(mobs) do
        TargetUnit(name)
        if UnitGUID('target')~=mainGUID and UnitExists('target') and not UnitIsDeadOrGhost('target') and UnitCanAttack('player','target') and not HasDebuff('target','Прикосновение вампира') then
            if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') casted=true end
        end
        if UnitGUID('target')~=mainGUID then TargetLastTarget() end
        if casted then return end
    end
    if UnitGUID('target')~=mainGUID then TargetLastTarget() end

    local _,_,_,_,mbPts = GetTalentInfo(3,8)
    if mbPts and mbPts > 0 and IsReady('Взрыв разума') then CastSpellByName('Взрыв разума') return end
    if " + unitsNearTarget + @" >= " + mindSearCount + @" then CastSpellByName('Иссушение разума') return end
    CastSpellByName('Пытка разума')
end
WB_AoE()
";
    }
}
