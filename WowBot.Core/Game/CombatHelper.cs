using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

/// <summary>
/// Утилиты для боя: подсчёт врагов, smart taunt, ground AoE, AoE avoidance.
/// Вынесено из BotEngine для изоляции ответственности.
/// </summary>
public class CombatHelper
{
    private readonly ObjectManager _objectManager;
    private readonly EndSceneHook _hook;
    private readonly ClickToMove _ctm;

    // Таунт кулдаун (тики)
    private int _smartTauntTick;

    // AoE Avoidance
    private int _aoeTick;
    private DateTime _aoeFleeUntil = DateTime.MinValue;
    public bool IsAoeFleeing => _aoeFleeUntil > DateTime.UtcNow;

    /// <summary>Сбросить AoE flee</summary>
    public void ResetAoeFlee() => _aoeFleeUntil = DateTime.MinValue;

    // Безопасные AoE дебафы (слоу/баффы без урона) — не бежим
    private static readonly HashSet<int> SafeAoeDebuffs = new()
    {
        68766, // Осквернение (Desecration) ДК — только слоу 50%, без урона
    };

    public CombatHelper(ObjectManager objectManager, EndSceneHook hook, ClickToMove ctm)
    {
        _objectManager = objectManager;
        _hook = hook;
        _ctm = ctm;
    }

    /// <summary>Считает живых мобов рядом с игроком</summary>
    public int CountNearbyEnemies(WowPlayer player, float range = 30f)
    {
        int count = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (player.DistanceTo(unit) > range) continue;
            count++;
        }
        return count;
    }

    /// <summary>Считает живых враждебных мобов в бою рядом с игроком</summary>
    public int CountNearbyCombatEnemies(WowPlayer player, float range = 10f)
    {
        int count = 0;
        var friendlyGuids = new HashSet<ulong>();
        friendlyGuids.Add(_objectManager.LocalPlayerGuid);
        foreach (var p in _objectManager.Players)
            friendlyGuids.Add(p.Guid);

        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (!unit.InCombat) continue;
            if (player.DistanceTo(unit) > range) continue;
            if (unit.TargetGuid == 0) continue;
            if (!friendlyGuids.Contains(unit.TargetGuid)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Считаем живых врагов рядом с таргетом (для AoE решений)</summary>
    public int CountEnemiesNearTarget(WowUnit target, float range = 10f)
    {
        int count = 0;
        var friendlyGuids = new HashSet<ulong>();
        friendlyGuids.Add(_objectManager.LocalPlayerGuid);
        foreach (var p in _objectManager.Players)
            friendlyGuids.Add(p.Guid);
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (friendlyGuids.Contains(unit.Guid)) continue;
            if (unit.TargetGuid != 0 && !friendlyGuids.Contains(unit.TargetGuid)) continue;
            float dx = unit.X - target.X;
            float dy = unit.Y - target.Y;
            float dz = unit.Z - target.Z;
            if (dx * dx + dy * dy + dz * dz <= range * range)
                count++;
        }
        return count;
    }

    /// <summary>Умный таунт: ищет моба бьющего союзника, переключает таргет и таунтит</summary>
    public bool TrySmartTaunt(WowPlayer player, string playerClass, string? spellFlagsLua)
    {
        if (spellFlagsLua?.Contains("AutoTaunt=true") != true) return false;
        _smartTauntTick++;
        if (_smartTauntTick < 5) return false; // каждые ~750мс
        _smartTauntTick = 0;

        ulong myGuid = _objectManager.LocalPlayerGuid;

        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive || !unit.InCombat) continue;
            if (unit.TargetGuid == 0 || unit.TargetGuid == myGuid) continue;
            if (player.DistanceTo(unit) > 30f) continue;

            bool hitsAlly = false;
            foreach (var p in _objectManager.Players)
            {
                if (p.Guid == myGuid) continue;
                if (p.Guid == unit.TargetGuid) { hitsAlly = true; break; }
            }
            if (!hitsAlly) continue;

            string mobName = unit.Name.Replace("'", "\\'");
            if (string.IsNullOrEmpty(mobName)) continue;

            int tauntId = playerClass switch
            {
                "WARRIOR" => 355,
                "PALADIN" => 62124,
                "DEATHKNIGHT" => 56222,
                "DRUID" => 6795,
                _ => 0
            };
            if (tauntId == 0) return false;

            string lua = $"TargetUnit('{mobName}') local n=GetSpellInfo({tauntId}) if n then CastSpellByName(n) end";
            _hook.ExecuteLua(lua, 300);
            Logger.Log(LogCat.Tank, $"SmartTaunt: {mobName} → {tauntId}");
            return true;
        }
        return false;
    }

    /// <summary>Пробуем кастовать ground AoE на таргет (Гроза, Залп и т.д.)</summary>
    public bool TryGroundAoE(WowPlayer player, WowUnit target, bool aoeEnabled, int aoeMinEnemies, string playerClass, string? specName, string? spellFlagsLua)
    {
        if (!aoeEnabled) return false;
        if (player.IsCasting) return false;
        if (player.ChannelingSpellId != 0) return false;

        int nearTarget = CountEnemiesNearTarget(target, 10f);
        if (nearTarget < aoeMinEnemies) return false;

        bool hurricaneEnabled = spellFlagsLua?.Contains("Hurricane=true") == true;

        string? aoeSpell = playerClass switch
        {
            "DRUID" when specName?.Contains("Balance") == true => hurricaneEnabled ? "Гроза" : null,
            "HUNTER" => spellFlagsLua?.Contains("Volley=true") == true ? "WB_VOLLEY" : null,
            _ => null
        };

        if (aoeSpell == null) return false;

        if (aoeSpell == "WB_VOLLEY")
            _hook.ExecuteLua("local n=GetSpellInfo(1510) if n then CastSpellByName(n) end", 200);
        else
            _hook.ExecuteLua($"CastSpellByName('{aoeSpell}')", 200);
        System.Threading.Thread.Sleep(100);
        bool ok = _hook.CastTerrainClick(target.X, target.Y, target.Z);
        Logger.Log(LogCat.AoE, $"GroundAoE: {aoeSpell} → ({target.X:F0},{target.Y:F0},{target.Z:F0}) enemies={nearTarget} ok={ok}");
        return ok;
    }

    /// <summary>AoE Avoidance — убегаем из лужи</summary>
    public bool TryAoEAvoidance(WowPlayer player)
    {
        _aoeTick++;
        if (_aoeTick < 3) return false;
        _aoeTick = 0;

        int dynCount = _objectManager.DynObjects.Count;
        if (dynCount == 0) return false;

        var playerDebuffs = new HashSet<int>(player.GetAuraSpellIds());

        float sumX = 0, sumY = 0;
        int count = 0;
        float maxRadius = 0;

        foreach (var dyn in _objectManager.DynObjects)
        {
            if (!playerDebuffs.Contains(dyn.SpellId)) continue;
            if (SafeAoeDebuffs.Contains(dyn.SpellId)) continue;

            float dx = player.X - dyn.X;
            float dy = player.Y - dyn.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist < dyn.Radius + 2f)
            {
                sumX += dyn.X;
                sumY += dyn.Y;
                count++;
                if (dyn.Radius > maxRadius) maxRadius = dyn.Radius;
            }
        }

        if (count == 0) return false;

        float meanX = sumX / count;
        float meanY = sumY / count;
        float escapeAngle = MathF.Atan2(player.Y - meanY, player.X - meanX);
        float escapeDist = maxRadius + 5f;
        float fleeX = meanX + escapeDist * MathF.Cos(escapeAngle);
        float fleeY = meanY + escapeDist * MathF.Sin(escapeAngle);

        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
        _ctm.MoveTo(fleeX, fleeY, player.Z, 1.0f);
        _aoeFleeUntil = DateTime.UtcNow.AddSeconds(2);
        Logger.Log(LogCat.AoE, $"AoE Flee: {count} DynObj, flee=({fleeX:F0},{fleeY:F0})");
        return true;
    }
}
