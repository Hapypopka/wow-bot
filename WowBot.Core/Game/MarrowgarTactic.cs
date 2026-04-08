namespace WowBot.Core.Game;

using WowBot.Core.Game.Entities;

/// <summary>
/// Lord Marrowgar (ICC первый босс) — полная тактика.
///
/// Normal Phase: ВСЕ за спиной в мили (танки, мдд, рдд, хилы).
/// Bone Spike: switch target → убить шип → обратно на босса.
/// Bone Storm: отбежать от босса, избегать Coldflame.
/// Coldflame: AoE Avoidance (DynObject) — уже обрабатывается отдельно.
///
/// Spell ID из DBM-Warmane:
/// - Bone Storm: 69076 (aura applied/removed)
/// - Bone Spike Graveyard: 69057, 70826, 72088, 72089 (cast start)
/// - Coldflame: 69146, 70823, 70824, 70825 (periodic damage)
/// - Bone Slice: 69055 (tank split damage)
/// </summary>
public class MarrowgarTactic : IBossTactic
{
    public const int NPC_ID = 36612;
    public string BossName => "Lord Marrowgar";

    // NPC IDs для Bone Spike (3 варианта)
    private static readonly int[] BoneSpikeNpcIds = { 36619, 38712, 38711 };

    // Spell IDs для Combat Log
    private const int SPELL_BONE_STORM = 69076;
    private const int SPELL_BONE_SPIKE_10N = 69057;
    private const int SPELL_BONE_SPIKE_25N = 70826;
    private const int SPELL_BONE_SPIKE_10H = 72088;
    private const int SPELL_BONE_SPIKE_25H = 72089;
    private const int SPELL_COLDFLAME_1 = 69146;
    private const int SPELL_COLDFLAME_2 = 70823;
    private const int SPELL_COLDFLAME_3 = 70824;
    private const int SPELL_COLDFLAME_4 = 70825;

    public int[] WatchSpellIds => new[]
    {
        SPELL_BONE_STORM,
        SPELL_BONE_SPIKE_10N, SPELL_BONE_SPIKE_25N, SPELL_BONE_SPIKE_10H, SPELL_BONE_SPIKE_25H,
        SPELL_COLDFLAME_1, SPELL_COLDFLAME_2, SPELL_COLDFLAME_3, SPELL_COLDFLAME_4,
    };

    // Состояние боя
    private enum Phase { Normal, BoneStorm }
    private Phase _phase = Phase.Normal;
    private bool _hasBoneSpike;
    private int _boneStormTicks; // отсчёт тиков Bone Storm
    private int _repositionTick;

    public void OnCombatStart(BossContext ctx)
    {
        _phase = Phase.Normal;
        _hasBoneSpike = false;
        _boneStormTicks = 0;
        Logger.Info("Marrowgar: combat started — all behind boss in melee!");
    }

    public void OnCombatEnd(BossContext ctx)
    {
        _phase = Phase.Normal;
        _hasBoneSpike = false;
        Logger.Info("Marrowgar: combat ended");
    }

    public void OnEvent(BossContext ctx, BossEvent evt)
    {
        // Bone Storm начался
        if (evt.SpellId == SPELL_BONE_STORM && evt.EventType == "SPELL_AURA_APPLIED")
        {
            _phase = Phase.BoneStorm;
            _boneStormTicks = 0;
            Logger.Info("Marrowgar: BONE STORM started — scatter!");
        }

        // Bone Storm кончился
        if (evt.SpellId == SPELL_BONE_STORM && evt.EventType == "SPELL_AURA_REMOVED")
        {
            _phase = Phase.Normal;
            Logger.Info("Marrowgar: Bone Storm ended — regroup behind boss!");
        }

        // Bone Spike cast
        if (evt.SpellId is SPELL_BONE_SPIKE_10N or SPELL_BONE_SPIKE_25N or SPELL_BONE_SPIKE_10H or SPELL_BONE_SPIKE_25H
            && evt.EventType == "SPELL_CAST_START")
        {
            _hasBoneSpike = true;
            Logger.Info($"Marrowgar: Bone Spike cast! Target: {evt.DestName}");
        }
    }

    public TacticAction Tick(BossContext ctx)
    {
        if (ctx.Player == null || ctx.Boss == null) return TacticAction.DoNothing;

        _repositionTick++;

        // === Приоритет 1: Bone Spike — убить шип (не танки) ===
        if (_hasBoneSpike && !ctx.IsTank)
        {
            var spike = FindBoneSpike(ctx);
            if (spike != null)
            {
                // Нашли шип — switch target
                return TacticAction.SwitchTarget(spike.NpcId);
            }
            else
            {
                // Шипов нет — сброс
                _hasBoneSpike = false;
            }
        }

        // === Bone Storm Phase ===
        if (_phase == Phase.BoneStorm)
        {
            _boneStormTicks++;
            return TickBoneStorm(ctx);
        }

        // === Normal Phase ===
        return TickNormal(ctx);
    }

    private TacticAction TickNormal(BossContext ctx)
    {
        var player = ctx.Player!;
        var boss = ctx.Boss!;

        // Все за спиной босса в мили (3-5yd)
        float dist = player.DistanceTo2D(boss);

        // Репозиция каждые ~1.5с (не спамим CTM)
        if (_repositionTick % 10 == 0 || dist > 8f)
        {
            float behindAngle = boss.Facing + MathF.PI; // за спиной = facing + 180°
            float targetDist = ctx.IsTank ? 1f : 4f; // танк вплотную, остальные за спиной
            float behindX = boss.X + MathF.Cos(behindAngle) * targetDist;
            float behindY = boss.Y + MathF.Sin(behindAngle) * targetDist;

            if (dist > 8f)
            {
                // Далеко — бежим к боссу
                return TacticAction.GoTo(behindX, behindY, boss.Z);
            }
            else if (dist > 5f || !IsBehindBoss(player, boss))
            {
                // Близко но не за спиной — подкорректировать
                return TacticAction.GoTo(behindX, behindY, boss.Z);
            }
        }

        // На месте за спиной — face + бить
        return TacticAction.Attack;
    }

    private TacticAction TickBoneStorm(BossContext ctx)
    {
        var player = ctx.Player!;
        var boss = ctx.Boss!;

        float distToBoss = player.DistanceTo2D(boss);

        // Bone Storm: отбежать от босса на 15+ ярдов
        if (distToBoss < 15f)
        {
            return TacticAction.FleeFrom(boss.X, boss.Y, boss.Z, 20f);
        }

        // Далеко от босса — стоим, бьём (РДД/хилы кастуют, мили ждут)
        if (ctx.IsMelee)
        {
            // Мили: ждём конца шторма, бьём шипы если есть
            if (_hasBoneSpike)
            {
                var spike = FindBoneSpike(ctx);
                if (spike != null)
                    return TacticAction.SwitchTarget(spike.NpcId);
            }
            return TacticAction.Attack; // instants если можем
        }

        // РДД/хилы: бьём на расстоянии
        return TacticAction.Attack;
    }

    private WowUnit? FindBoneSpike(BossContext ctx)
    {
        WowUnit? closest = null;
        float closestDist = float.MaxValue;

        foreach (var unit in ctx.ObjectManager.Units)
        {
            try
            {
                if (!unit.IsAlive) continue;
                foreach (int spikeId in BoneSpikeNpcIds)
                {
                    if (unit.NpcId == spikeId)
                    {
                        float d = ctx.Player!.DistanceTo2D(unit);
                        if (d < closestDist)
                        {
                            closestDist = d;
                            closest = unit;
                        }
                    }
                }
            }
            catch { }
        }
        return closest;
    }

    private static bool IsBehindBoss(WowPlayer player, WowUnit boss)
    {
        float angleToPlayer = MathF.Atan2(player.Y - boss.Y, player.X - boss.X);
        if (angleToPlayer < 0) angleToPlayer += MathF.PI * 2;

        float bossFacing = boss.Facing;
        float behindAngle = bossFacing + MathF.PI;
        if (behindAngle > MathF.PI * 2) behindAngle -= MathF.PI * 2;

        float diff = MathF.Abs(angleToPlayer - behindAngle);
        if (diff > MathF.PI) diff = MathF.PI * 2 - diff;

        return diff < 1.0f; // ~57° — за спиной
    }
}
