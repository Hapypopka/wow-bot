namespace WowBot.Core.Game;

/// <summary>
/// Босс-тактики: автоматическое поведение на боссах ЦЛК.
/// Вызывается из BotEngine.Tick() когда AutoPve включен.
/// </summary>
public class BossTactics
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly ClickToMove _ctm;
    private readonly Navigation _navigation;

    private int _tacticTick;
    private int _coldflameMoveTicks;
    private bool _boneStormActive;
    private bool _isMelee; // мили или рдд (определяется по классу)

    // NPC IDs
    public const int NPC_MARROWGAR = 36612;
    public const int NPC_BONE_SPIKE = 36619;

    public bool IsMelee { get => _isMelee; set => _isMelee = value; }
    public bool IsHealer { get; set; }

    public BossTactics(EndSceneHook hook, ObjectManager objectManager, ClickToMove ctm, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _ctm = ctm;
        _navigation = navigation;
    }

    /// <summary>
    /// Тик автопве. Возвращает true если тактика активна.
    /// </summary>
    public bool Tick(Entities.WowPlayer player, string enemyCountLua, string spellFlagsLua, string fullScript)
    {
        _tacticTick++;
        if (_tacticTick >= 7) _tacticTick = 0;

        // Двигаемся от лужи
        if (_coldflameMoveTicks > 0)
        {
            _coldflameMoveTicks--;
            return true;
        }

        // Ищем боссов
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive || !unit.InCombat) continue;
            try
            {
                if (unit.NpcId == NPC_MARROWGAR)
                    return TickMarrowgar(player, unit);
            }
            catch { }
        }

        return false;
    }

    // ==================== LORD MARROWGAR ====================

    private bool TickMarrowgar(Entities.WowPlayer player, Entities.WowUnit boss)
    {
        // === Определяем фазу: Bone Storm или нет ===
        if (_tacticTick == 0)
        {
            string? bsCheck = _hook.ExecuteLuaWithResult(
                "local bs=0 for i=1,40 do local n=UnitBuff('target',i) if not n then break end if n=='Вихрь костей' then bs=1 break end end WB_R=bs");
            bool wasBoneStorm = _boneStormActive;
            _boneStormActive = bsCheck == "1";

            if (_boneStormActive && !wasBoneStorm)
                Logger.Info("BossTactics: Marrowgar — BONE STORM started!");
            if (!_boneStormActive && wasBoneStorm)
                Logger.Info("BossTactics: Marrowgar — Bone Storm ended, regroup!");
        }

        // === Coldflame check (всегда, обе фазы) ===
        if (_tacticTick == 0)
        {
            string? cfCheck = _hook.ExecuteLuaWithResult(
                "local cf=0 for i=1,40 do local n=UnitDebuff('player',i) if n and (n=='Хладное пламя' or n=='Холодное пламя' or n=='Coldflame') then cf=1 break end end WB_R=cf");
            if (cfCheck == "1")
            {
                _hook.ExecuteLua("MoveForwardStart()", 50);
                _coldflameMoveTicks = 4; // ~0.6 сек
                Logger.Info("BossTactics: Marrowgar — dodging Coldflame!");
                Task.Run(async () =>
                {
                    await Task.Delay(600);
                    _hook.ExecuteLua("MoveForwardStop()", 50);
                });
                return true;
            }
        }

        // === Bone Spike — приоритет для всех (кроме хилов) ===
        if (!IsHealer && _tacticTick == 0)
        {
            bool foundSpike = false;
            foreach (var u in _objectManager.Units)
            {
                try
                {
                    if (u.IsAlive && u.NpcId == NPC_BONE_SPIKE)
                    {
                        _hook.ExecuteLua("TargetUnit('Костяной шип')", 100);
                        foundSpike = true;
                        Logger.Info("BossTactics: targeting Bone Spike");
                        break;
                    }
                }
                catch { }
            }

            // Если нет шипа и не Bone Storm — таргетим босса
            if (!foundSpike && !_boneStormActive)
            {
                var currentTarget = _objectManager.GetTarget();
                if (currentTarget == null || !currentTarget.IsAlive || currentTarget.NpcId != NPC_MARROWGAR)
                {
                    _hook.ExecuteLua("TargetUnit('Лорд Ребрад')", 100);
                }
            }
        }

        // === Поведение по фазе ===
        if (_boneStormActive)
        {
            // BONE STORM PHASE
            if (_isMelee)
            {
                // МДД: стоят на месте, бьют шипы рядом, НЕ бегут к боссу
                // Ничего не делаем с движением — стоим
            }
            else if (!IsHealer)
            {
                // РДД: бьют босса на расстоянии + приоритет шипы
                // Стоим, кастим (босс двигается сам)
            }
            // Хилы: просто хилят (управляет BotEngine healer tick)
        }
        else
        {
            // NORMAL PHASE — все подбегают к боссу вплотную (за спину)
            float dist = player.DistanceTo2D(boss);
            if (dist > 5f)
            {
                // Вычисляем точку за спиной босса (facing + PI)
                float bossFacing = boss.Facing;
                float behindX = boss.X + MathF.Cos(bossFacing) * 3f; // за спиной
                float behindY = boss.Y + MathF.Sin(bossFacing) * 3f;
                _ctm.MoveTo(behindX, behindY, boss.Z, 0.5f);
            }

            // Face boss
            if (!IsHealer)
                _navigation.FaceUnit(player, boss);
        }

        return true;
    }
}
