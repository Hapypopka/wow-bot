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

    // Кэш friendly GUIDs (переиспользуется между вызовами)
    private readonly HashSet<ulong> _friendlyGuids = new();

    // AoE Avoidance
    private int _aoeTick;
    private DateTime _aoeFleeUntil = DateTime.MinValue;
    public bool IsAoeFleeing => _aoeFleeUntil > DateTime.UtcNow;

    // Блокировка Ground AoE пока канал идёт.
    // На WoWCircle ChannelingSpellId не обновляется вовремя, поэтому после каста
    // читаем реальное endTime канала через UnitChannelInfo — универсально для любой хасты.
    private DateTime _groundAoECastUntil = DateTime.MinValue;

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

    private HashSet<ulong> RefreshFriendlyGuids()
    {
        _friendlyGuids.Clear();
        _friendlyGuids.Add(_objectManager.LocalPlayerGuid);
        foreach (var p in _objectManager.Players)
            _friendlyGuids.Add(p.Guid);
        return _friendlyGuids;
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

    /// <summary>Считает живых враждебных мобов в бою рядом с игроком (для танков).
    /// Строгая проверка: моб должен атаковать кого-то из группы.</summary>
    public int CountNearbyCombatEnemies(WowPlayer player, float range = 10f)
    {
        int count = 0;
        var friendlyGuids = RefreshFriendlyGuids();

        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (!unit.InCombat) continue;
            if (player.DistanceTo(unit) > range) continue;

            // UnitFlags фильтр
            if (unit.IsNotAttackable) continue;

            // Owner chain фильтр — петы/тотемы/гули союзников
            ulong owner = unit.OwnerGuid;
            if (owner != 0 && friendlyGuids.Contains(owner)) continue;

            // Должен атаковать кого-то из группы
            if (unit.TargetGuid == 0) continue;
            if (!friendlyGuids.Contains(unit.TargetGuid)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Считаем живых врагов рядом с таргетом (для AoE решений).
    /// Hybrid подход: fast-path через память (UnitFlags + OwnerGUID + TargetGuid).</summary>
    public int CountEnemiesNearTarget(WowUnit target, float range = 10f)
    {
        int count = 0;
        var friendlyGuids = RefreshFriendlyGuids();
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (friendlyGuids.Contains(unit.Guid)) continue;

            // 1. UnitFlags фильтр — critters, квестодатели, неуязвимые
            if (unit.IsNotAttackable) continue;

            // 2. Owner chain фильтр — свой/союзный пет/тотем/гуль
            ulong owner = unit.OwnerGuid;
            if (owner != 0 && friendlyGuids.Contains(owner)) continue;

            // 3. Поведенческий фильтр — не считаем мирных мобов с TargetGuid=0
            if (unit.TargetGuid == 0)
            {
                if (!unit.InCombat) continue; // стоит мирно — не враг
            }
            else if (!friendlyGuids.Contains(unit.TargetGuid))
            {
                continue; // атакует кого-то не из нашей группы
            }

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

        // Канал ещё идёт (знаем реальное endTime от прошлого каста)
        if (DateTime.UtcNow < _groundAoECastUntil) return false;

        int nearTarget = CountEnemiesNearTarget(target, 10f);
        if (nearTarget < aoeMinEnemies) return false;

        bool hurricaneEnabled = spellFlagsLua?.Contains("Hurricane=true") == true;

        string? aoeSpell = playerClass switch
        {
            "DRUID" when specName?.Contains("Balance") == true => hurricaneEnabled ? "Гроза" : null,
            "HUNTER" => spellFlagsLua?.Contains("Volley=true") == true ? "WB_VOLLEY" : null,
            "MAGE" when specName?.Contains("Fire") == true => spellFlagsLua?.Contains("Flamestrike=true") == true ? "WB_FLAMESTRIKE" : null,
            "MAGE" when specName?.Contains("Frost") == true => spellFlagsLua?.Contains("Blizzard=true") == true ? "WB_BLIZZARD" : null,
            "MAGE" when specName?.Contains("Arcane") == true => spellFlagsLua?.Contains("Blizzard=true") == true ? "WB_BLIZZARD" : null,
            _ => null
        };

        if (aoeSpell == null) return false;

        // Объединённая проверка: скорость + GCD. В беге и на GCD каст не начнётся.
        int checkSpellId = aoeSpell switch
        {
            "WB_VOLLEY" => 58433,       // Volley rank 6
            "WB_FLAMESTRIKE" => 42926,  // Flamestrike rank 9
            "WB_BLIZZARD" => 42940,     // Blizzard rank 8
            _ => 48467                   // Hurricane rank 6 (default Druid Balance)
        };
        string checkLua =
            $"local n=GetSpellInfo({checkSpellId}) " +
            $"local sp = GetUnitSpeed('player') or 0 " +
            $"local cdLeft = 0 " +
            $"if n then local s,d = GetSpellCooldown(n) if s and s > 0 then cdLeft = (s + d) - GetTime() end end " +
            $"WB_R = tostring(sp) .. '|' .. tostring(cdLeft)";
        string? checkResult = _hook.ExecuteLuaWithResult(checkLua);
        var parts = (checkResult ?? "0|0").Split('|');
        double speed = 0, cdLeft = 0;
        if (parts.Length >= 1) double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed);
        if (parts.Length >= 2) double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cdLeft);

        if (speed > 0.1)
        {
            Logger.Log(LogCat.AoE, $"GroundAoE: skipped, speed={speed:F2} (moving)");
            return false;
        }
        if (cdLeft > 0.1)
        {
            Logger.Log(LogCat.AoE, $"GroundAoE: skipped, GCD/cd={cdLeft:F2}s");
            return false;
        }

        // Balance Druid: Гроза кастится только в форме Moonkin (24858), иначе меньше урона
        if (aoeSpell == "Гроза")
        {
            var auras = player.GetAuraSpellIds();
            if (!auras.Contains(24858))
            {
                _hook.ExecuteLua("local n=GetSpellInfo(24858) if n then CastSpellByName(n) end", 200);
                Logger.Log(LogCat.AoE, "GroundAoE: no Moonkin form → dance first");
                return false; // на следующем тике форма будет, скастим Грозу
            }
        }

        if (aoeSpell == "WB_VOLLEY")
            _hook.ExecuteLua("local n=GetSpellInfo(1510) if n then CastSpellByName(n) end", 200);
        else if (aoeSpell == "WB_FLAMESTRIKE")
            _hook.ExecuteLua("local n=GetSpellInfo(42926) if n then CastSpellByName(n) end", 200);
        else if (aoeSpell == "WB_BLIZZARD")
            _hook.ExecuteLua("local n=GetSpellInfo(42940) if n then CastSpellByName(n) end", 200);
        else
            _hook.ExecuteLua($"CastSpellByName('{aoeSpell}')", 200);
        System.Threading.Thread.Sleep(100);
        bool ok = _hook.CastTerrainClick(target.X, target.Y, target.Z);

        if (ok)
        {
            // Ждём 400мс чтобы канал успел начаться, потом читаем реальное endTime из WoW
            System.Threading.Thread.Sleep(400);
            string? result = _hook.ExecuteLuaWithResult(
                "local _,_,_,_,_,endTime = UnitChannelInfo('player') if endTime then WB_R = tostring(endTime/1000 - GetTime()) else WB_R = '0' end");
            double remaining = 0;
            double.TryParse(result, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out remaining);
            if (remaining > 0.5)
            {
                _groundAoECastUntil = DateTime.UtcNow.AddSeconds(remaining);
                Logger.Log(LogCat.AoE, $"GroundAoE: {aoeSpell} → ({target.X:F0},{target.Y:F0},{target.Z:F0}) enemies={nearTarget} channel={remaining:F1}s");
            }
            else
            {
                // Канал не зарегистрировался — значит каст не удался (был в беге? прерван?).
                // Короткий throttle 1.5s — дать бросить таргет/остановиться, потом попробовать снова
                _groundAoECastUntil = DateTime.UtcNow.AddSeconds(1.5);
                Logger.Log(LogCat.AoE, $"GroundAoE: {aoeSpell} FAILED — channel not registered (retry in 1.5s)");
            }
        }
        else
        {
            Logger.Log(LogCat.AoE, $"GroundAoE: {aoeSpell} → ({target.X:F0},{target.Y:F0},{target.Z:F0}) enemies={nearTarget} ok=False");
        }
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
            if (dyn.Caster == player.Guid) continue; // своё заклинание (Hurricane, Volley, DnD, Consecration) — не убегаем
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
