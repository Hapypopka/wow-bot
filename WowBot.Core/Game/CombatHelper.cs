using WowBot.Core.Game.Entities;
using WowBot.Core.Game.Generated;

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
    private readonly EnemyCastObserver _castObserver;

    // Таунт кулдаун (тики)
    private int _smartTauntTick;

    // Кэш friendly GUIDs (переиспользуется между вызовами)
    private readonly HashSet<ulong> _friendlyGuids = new();

    // AoE Avoidance (grid safe spot + prediction)
    private (float X, float Y)? _fleeDestination;
    private DateTime _fleeFallbackUntil = DateTime.MinValue;

    // AoE Avoidance margins (безопасные константы, а не данные спеллов)
    private const float AoEFleeTriggerMargin = 1f;  // триггер: меньше 1y от края = опасно
    private const float AoESafetyCap = 2.5f;        // стоп: 2.5y от края уже безопасно — не бежим дальше (гистерезис 1.5y)
    private const float AoEGridRingRadius = 8f;     // кольцо кандидатов вокруг игрока
    private const int AoEGridCandidates = 24;       // точек по окружности (шаг 15°)
    private const float AoEPredictSeconds = 0.5f;   // предсказание движения DynObject
    private const float AoEMovingDynSpeed = 6f;     // Coldflame/Coldflame-like ползут ~6y/s
    private const float AoEHysteresisMargin = 2f;   // не меняем destination если score хуже на <2y
    public bool IsAoeFleeing => _fleeDestination != null || _fleeFallbackUntil > DateTime.UtcNow;

    // Блокировка Ground AoE пока канал идёт.
    // На WoWCircle ChannelingSpellId не обновляется вовремя, поэтому после каста
    // читаем реальное endTime канала через UnitChannelInfo — универсально для любой хасты.
    private DateTime _groundAoECastUntil = DateTime.MinValue;

    /// <summary>Сбросить AoE flee</summary>
    public void ResetAoeFlee() { _fleeDestination = null; _fleeFallbackUntil = DateTime.MinValue; }

    private struct DangerZone
    {
        public float X, Y, VX, VY, Radius;
    }

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
        _castObserver = new EnemyCastObserver(hook);
    }

    public EnemyCastObserver CastObserver => _castObserver;

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

    /// <summary>
    /// AoE Avoidance — grid-based safe spot с предиктом движения лужи.
    ///
    /// Идея: каждый тик собираем DangerZones (вражеские DynObjects с предсказанной позицией через 0.5с).
    /// Генерим 24 кандидата на кольце 10y вокруг игрока, считаем score = min(dist до любой зоны).
    /// Бежим в точку с max score. Гистерезис: пока старая destination близка к лучшей — не меняем.
    /// Выход из flee state — когда все зоны дают player > triggerMargin.
    /// </summary>
    public bool TryAoEAvoidance(WowPlayer player)
    {
        int dynCount = _objectManager.DynObjects.Count;
        if (dynCount == 0)
        {
            if (_fleeDestination != null)
            {
                _fleeDestination = null;
                Logger.Log(LogCat.AoE, "AoE Flee: all clear, resuming");
            }
            return false;
        }

        var friendlyGuids = RefreshFriendlyGuids();
        var dangers = new List<DangerZone>();
        HashSet<int>? playerDebuffs = null;

        foreach (var dyn in _objectManager.DynObjects)
        {
            if (dyn.Caster == player.Guid) continue; // своё заклинание — не бежим от себя
            if (SafeAoeDebuffs.Contains(dyn.SpellId)) continue;

            // Дружественный кастер (Consecration/DnD партии) — бежим ТОЛЬКО если реально получаем
            // дебафф от этой зоны (PvP дуэль, misfired кросс-зона). Иначе мили-DPS будет убегать
            // от своего же танка.
            if (friendlyGuids.Contains(dyn.Caster))
            {
                playerDebuffs ??= new HashSet<int>(player.GetAuraSpellIds());
                if (!playerDebuffs.Contains(dyn.SpellId)) continue;
            }

            // Предикт движения: лужи типа Coldflame ползут радиально от кастера.
            // Для статичных (Consecration, DnD) vx/vy = 0 — зона не двигается.
            float vx = 0, vy = 0;
            var casterUnit = _objectManager.GetUnitByGuid(dyn.Caster);
            if (casterUnit != null)
            {
                float dirX = dyn.X - casterUnit.X;
                float dirY = dyn.Y - casterUnit.Y;
                float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
                // Если DynObject далеко от кастера (>3y) — считаем что он ползёт наружу.
                if (len > 3f)
                {
                    vx = dirX / len * AoEMovingDynSpeed;
                    vy = dirY / len * AoEMovingDynSpeed;
                }
            }

            dangers.Add(new DangerZone { X = dyn.X, Y = dyn.Y, VX = vx, VY = vy, Radius = dyn.Radius });
        }

        if (dangers.Count == 0)
        {
            if (_fleeDestination != null)
            {
                _fleeDestination = null;
                _ctm.NativeStop(); // force server sync — aura сразу увидит что игрок вне зоны
                Logger.Log(LogCat.AoE, "AoE Flee: all clear, resuming (native stop)");
            }
            return false;
        }

        // Проверяем опасность для текущей позиции игрока (сейчас ИЛИ через 0.5с).
        float currentScore = ScoreSpot(player.X, player.Y, dangers);
        bool inDanger = currentScore < AoEFleeTriggerMargin;

        // Если стоим безопасно и не бежали — ничего не делаем.
        if (!inDanger && _fleeDestination == null) return false;

        // Если игрок уже в безопасной зоне (≥ AoESafetyCap до края) — выходим,
        // даже если ещё не дошли до destination. Не надо убегать "ещё дальше ещё безопаснее".
        if (currentScore >= AoESafetyCap)
        {
            if (_fleeDestination != null)
            {
                _fleeDestination = null;
                _ctm.NativeStop(); // force server sync — aura сразу увидит что игрок вне зоны
                Logger.Log(LogCat.AoE, $"AoE Flee: safe (pScore={currentScore:F1}≥{AoESafetyCap}), resuming (native stop)");
            }
            return false;
        }

        // Адаптивный радиус кольца: если лужа большая — ищем точки за её краем.
        // Иначе при касте по центру крупной зоны (DnD 10y, Void Zone 15y) все 8-y кандидаты оказываются
        // внутри зоны, бот не видит "чистой земли" и выбирает середняка → не выходит до края.
        float maxDangerRadius = 0;
        foreach (var d in dangers) if (d.Radius > maxDangerRadius) maxDangerRadius = d.Radius;
        float ringRadius = MathF.Max(AoEGridRingRadius, maxDangerRadius + 3f);

        // Ищем лучшую точку на кольце вокруг игрока.
        var best = FindSafeSpot(player, dangers, ringRadius);

        // Гистерезис: если старая destination всё ещё "почти лучшая" — не дёргаемся.
        if (_fleeDestination.HasValue)
        {
            float oldScore = ScoreSpot(_fleeDestination.Value.X, _fleeDestination.Value.Y, dangers);
            if (oldScore > AoEFleeTriggerMargin && oldScore > best.Score - AoEHysteresisMargin)
            {
                best = (_fleeDestination.Value.X, _fleeDestination.Value.Y, oldScore);
            }
        }

        // Если мы уже в безопасной точке и стоим там — выходим.
        float distToBest = MathF.Sqrt((player.X - best.X) * (player.X - best.X) + (player.Y - best.Y) * (player.Y - best.Y));
        if (!inDanger && distToBest < 1.5f)
        {
            _fleeDestination = null;
            return false;
        }

        _fleeDestination = (best.X, best.Y);
        _fleeFallbackUntil = DateTime.UtcNow.AddSeconds(0.5); // fallback timer на случай если что-то зависло
        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
        _ctm.MoveTo(best.X, best.Y, player.Z, 1.0f);
        Logger.Log(LogCat.AoE, $"AoE Flee: zones={dangers.Count} pScore={currentScore:F1} → ({best.X:F0},{best.Y:F0}) score={best.Score:F1}");
        return true;
    }

    /// <summary>Генерит 24 кандидата на кольце вокруг игрока и возвращает точку с максимальным score.</summary>
    private (float X, float Y, float Score) FindSafeSpot(WowPlayer player, List<DangerZone> dangers, float ringRadius)
    {
        float bestScore = float.MinValue;
        float bestX = player.X;
        float bestY = player.Y;

        for (int i = 0; i < AoEGridCandidates; i++)
        {
            float angle = (float)i / AoEGridCandidates * MathF.PI * 2f;
            float x = player.X + MathF.Cos(angle) * ringRadius;
            float y = player.Y + MathF.Sin(angle) * ringRadius;
            float score = ScoreSpot(x, y, dangers);
            if (score > bestScore)
            {
                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }

        return (bestX, bestY, bestScore);
    }

    /// <summary>Score точки = min(dist до края любой зоны), с учётом движения через AoEPredictSeconds.
    /// Ограничиваем потолком AoESafetyCap: точка в 50y от всего = то же что в 6y. Иначе грид
    /// всегда выбирал бы самую далёкую точку и гистерезис фиксировал бы её навечно (бот бежал за горизонт).</summary>
    private static float ScoreSpot(float x, float y, List<DangerZone> dangers)
    {
        float minDist = float.MaxValue;
        foreach (var d in dangers)
        {
            float distNow = MathF.Sqrt((x - d.X) * (x - d.X) + (y - d.Y) * (y - d.Y)) - d.Radius;
            float px = d.X + d.VX * AoEPredictSeconds;
            float py = d.Y + d.VY * AoEPredictSeconds;
            float distPred = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) - d.Radius;
            float dist = MathF.Min(distNow, distPred);
            if (dist < minDist) minDist = dist;
        }
        return MathF.Min(minDist, AoESafetyCap);
    }

    // ========== Proactive AoE Avoidance ==========
    // Слой 2 защиты: реагируем на вражеские касты ДО импакта.
    // Source: EnemyCastObserver (combat log SPELL_CAST_START)
    // DB: DangerousSpellTable (3865+ спеллов из DBC)
    // Решение: по AoETargetMode решаем куда уклоняться.

    private DateTime _proactiveFleeUntil = DateTime.MinValue;
    private const float ProactiveSafetyMargin = 4f; // +4y за край радиуса
    public bool IsProactiveFleeing => _proactiveFleeUntil > DateTime.UtcNow;

    // Диагностика: логируем впервые увиденные вражеские касты (раз в X, не спамим).
    private readonly HashSet<int> _loggedUnknownCasts = new();
    private readonly HashSet<int> _loggedKnownCasts = new();

    /// <summary>
    /// Проверить активные вражеские касты и уклониться если игрок в зоне удара.
    /// Вызывать каждый тик перед позиционированием/ротацией.
    /// </summary>
    public bool TryProactiveAvoidance(WowPlayer player)
    {
        _castObserver.Tick();
        if (_castObserver.ActiveCasts.Count == 0) return false;

        foreach (var cast in _castObserver.ActiveCasts)
        {
            if (!DangerousSpellTable.TryGet(cast.SpellId, out var spell))
            {
                // Непокрытый кастер — логируем раз на spell_id для диагностики.
                // Потом можно посмотреть какие boss-спеллы пропускает наша DBC таблица.
                if (_loggedUnknownCasts.Add(cast.SpellId))
                {
                    var c = _objectManager.GetUnitByGuid(cast.CasterGuid);
                    Logger.Log(LogCat.AoE, $"Proactive: UNKNOWN hostile cast sid={cast.SpellId} caster={c?.Name ?? "?"}");
                }
                continue;
            }

            if (_loggedKnownCasts.Add(cast.SpellId))
                Logger.Log(LogCat.AoE, $"Proactive: KNOWN hostile cast sid={cast.SpellId} '{spell.Name}' r={spell.Radius}y mode={spell.Mode}");

            var caster = _objectManager.GetUnitByGuid(cast.CasterGuid);
            if (caster == null || !caster.IsAlive) continue;

            float distToCaster = player.DistanceTo(caster);
            float dangerRadius = spell.Radius + ProactiveSafetyMargin;

            switch (spell.Mode)
            {
                case AoETargetMode.AroundCaster:
                    // Вокруг кастера — бежать радиально от него, пока мы ближе чем radius.
                    if (distToCaster < dangerRadius)
                    {
                        FleeRadially(player, caster, dangerRadius, cast.SpellId, spell.Name);
                        return true;
                    }
                    break;

                case AoETargetMode.AroundTarget:
                    // Вокруг цели каста. Если цель — мы, бежать от кастера.
                    // Если цель — другой игрок рядом, тоже уходим (splash damage).
                    if (cast.TargetGuid == player.Guid && distToCaster < dangerRadius)
                    {
                        FleeRadially(player, caster, dangerRadius, cast.SpellId, spell.Name);
                        return true;
                    }
                    break;

                case AoETargetMode.GroundTargeted:
                    // Лужа падает в точку (часто под игроком или в его позицию).
                    // Стратегия: страйф перпендикулярно от кастера на radius + margin.
                    // Работает для Blizzard, Flamestrike, Defile-паттернов.
                    if (distToCaster < 45f) // только если кастер в реалистичном диапазоне
                    {
                        StrafeFromCaster(player, caster, spell.Radius + ProactiveSafetyMargin, cast.SpellId, spell.Name);
                        return true;
                    }
                    break;

                case AoETargetMode.Cone:
                case AoETargetMode.Frontal:
                    // Конус/фронт — страйф вбок от линии между нами и кастером.
                    if (distToCaster < dangerRadius)
                    {
                        StrafeFromCaster(player, caster, spell.Radius + ProactiveSafetyMargin, cast.SpellId, spell.Name);
                        return true;
                    }
                    break;

                case AoETargetMode.Unknown:
                    // Неизвестный тип AoE — осторожность: страйф если близко.
                    if (distToCaster < dangerRadius)
                    {
                        StrafeFromCaster(player, caster, spell.Radius + ProactiveSafetyMargin, cast.SpellId, spell.Name);
                        return true;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>Бежать РАДИАЛЬНО от кастера (для AroundCaster/AroundTarget).</summary>
    private void FleeRadially(WowPlayer player, WowUnit caster, float targetDist, int spellId, string spellName)
    {
        float dx = player.X - caster.X;
        float dy = player.Y - caster.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.1f) { dx = 1; dy = 0; len = 1; }
        float fleeX = caster.X + (dx / len) * targetDist;
        float fleeY = caster.Y + (dy / len) * targetDist;

        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
        _ctm.MoveTo(fleeX, fleeY, player.Z, 1.2f);
        _proactiveFleeUntil = DateTime.UtcNow.AddSeconds(2);
        Logger.Log(LogCat.AoE, $"Proactive flee radial: sid={spellId} '{spellName}' caster={caster.Name} → ({fleeX:F0},{fleeY:F0})");
    }

    /// <summary>
    /// Страйф перпендикулярно от линии кастер→игрок (для GroundTargeted/Cone).
    /// Отходим в сторону на radius+margin, остаёмся на той же дистанции от кастера.
    /// </summary>
    private void StrafeFromCaster(WowPlayer player, WowUnit caster, float strafeDist, int spellId, string spellName)
    {
        // Направление кастер→игрок
        float dx = player.X - caster.X;
        float dy = player.Y - caster.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.1f) { dx = 1; dy = 0; len = 1; }
        // Перпендикуляр (левый)
        float perpX = -dy / len;
        float perpY = dx / len;

        float strafeX = player.X + perpX * strafeDist;
        float strafeY = player.Y + perpY * strafeDist;

        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
        _ctm.MoveTo(strafeX, strafeY, player.Z, 1.2f);
        _proactiveFleeUntil = DateTime.UtcNow.AddSeconds(1.5);
        Logger.Log(LogCat.AoE, $"Proactive strafe: sid={spellId} '{spellName}' caster={caster.Name} → ({strafeX:F0},{strafeY:F0})");
    }
}
