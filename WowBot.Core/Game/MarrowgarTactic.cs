namespace WowBot.Core.Game;

using WowBot.Core.Game.Entities;

/// <summary>
/// Lord Marrowgar (ICC первый босс) — слейв-тактика.
///
/// Механика через InFrame lock (не GoTo/Attack каждый тик — это дёргается):
/// - OnCombatStart: вычисляем InFrame spot (сзади босса), ctx.LockInFrame(x,y,z) → BotEngine.InFrameLockedPos
///   Хил/DPS approach сами используют lock и идут туда, дальше стоят.
/// - OnEvent Bone Storm APPLIED: ctx.UnlockInFrame() → слейвы стоят где есть, ротация кастует что может.
/// - OnEvent Bone Storm REMOVED: пересчёт нового spot (босс двигался) + LockInFrame.
/// - OnEvent Bone Spike cast: Tick() возвращает SwitchTarget (только для мили/рдд, хилы игнорируют).
///
/// Мастер (IsMaster) и танк (IsTank) — DoNothing, тактика не трогает lock и action.
///
/// Spell IDs (Warmane DBM):
/// - Bone Storm: 69076
/// - Bone Spike: 69057 / 70826 / 72088 / 72089
/// - Coldflame: 69146 / 70823-5
/// </summary>
public class MarrowgarTactic : IBossTactic
{
    public const int NPC_ID = 36612;
    public static readonly int[] NPC_IDS = { 36612, 37864, 37957, 37958, 37959 };
    public string BossName => "Lord Marrowgar";

    private static readonly int[] BoneSpikeNpcIds = { 36619, 38712, 38711 };

    private const int SPELL_BONE_STORM = 69076;
    private const int SPELL_BONE_SPIKE_10N = 69057;
    private const int SPELL_BONE_SPIKE_25N = 70826;
    private const int SPELL_BONE_SPIKE_10H = 72088;
    private const int SPELL_BONE_SPIKE_25H = 72089;
    private const int SPELL_COLDFLAME_1 = 69146;
    private const int SPELL_COLDFLAME_2 = 70823;
    private const int SPELL_COLDFLAME_3 = 70824;
    private const int SPELL_COLDFLAME_4 = 70825;
    // Impaled — debuff на игроке когда его засадило в Bone Spike. Stun, нельзя двигаться/бить.
    private static readonly int[] SPELL_IMPALED_IDS = { 69065, 72669, 72670, 72671 };
    private bool _wasImpaled;

    public int[] WatchSpellIds => new[]
    {
        SPELL_BONE_STORM,
        SPELL_BONE_SPIKE_10N, SPELL_BONE_SPIKE_25N, SPELL_BONE_SPIKE_10H, SPELL_BONE_SPIKE_25H,
        SPELL_COLDFLAME_1, SPELL_COLDFLAME_2, SPELL_COLDFLAME_3, SPELL_COLDFLAME_4,
    };

    private enum Phase { Normal, BoneStorm }
    private Phase _phase = Phase.Normal;
    private bool _hasBoneSpike;
    private int _auraLogTick;
    private DateTime _lastSayTime = DateTime.MinValue;
    // Aura 69076 на боссе. Держим Storm активным короткий период после последнего обнаружения,
    // на случай мигания при чтении. Раньше было 15 сек — слишком долго: вихрь кончался, бот ещё
    // 15 сек думал что идёт, не успевал переключиться до OUT OF COMBAT.
    // SPELL_AURA_REMOVED из combat log на SPP не всегда приходит — полагаемся на чтение auras.
    private DateTime _lastStormAuraSeen = DateTime.MinValue;
    private const double StormGraceSec = 2.5;

    /// <summary>Пишет в SAY с кулдауном 1.5с чтобы не спамить от каждого слейва.</summary>
    private void Say(BossContext ctx, string text)
    {
        if (DateTime.UtcNow - _lastSayTime < TimeSpan.FromMilliseconds(1500)) return;
        _lastSayTime = DateTime.UtcNow;
        try { ctx.Hook.ExecuteLua($"SendChatMessage('{text.Replace("'", "\\'")}','SAY')", 100); } catch { }
    }

    public void OnCombatStart(BossContext ctx)
    {
        _phase = Phase.Normal;
        _hasBoneSpike = false;
        _wasImpaled = false;
        Logger.Info("Marrowgar: combat started");
        // Мастер и танки не получают lock — им InFrame не нужен
        if (!ctx.IsMaster && !ctx.IsTank)
            ApplyInFrameLock(ctx);
    }

    public void OnCombatEnd(BossContext ctx)
    {
        _phase = Phase.Normal;
        _hasBoneSpike = false;
        _wasImpaled = false;
        _lastStormAuraSeen = DateTime.MinValue;
        if (!ctx.IsMaster && !ctx.IsTank)
            ctx.UnlockInFrame?.Invoke();
        // Снимаем override чтобы UI снова диктовал AoE avoid
        ctx.ClearAoeAvoid?.Invoke();
        Logger.Info("Marrowgar: combat ended");
    }

    public void OnEvent(BossContext ctx, BossEvent evt)
    {
        // Bone Storm начался — снимаем lock, слейвы стоят где есть (не двигаются к старой точке).
        if (evt.SpellId == SPELL_BONE_STORM && evt.EventType == "SPELL_AURA_APPLIED")
        {
            _phase = Phase.BoneStorm;
            if (!ctx.IsMaster && !ctx.IsTank)
                ctx.UnlockInFrame?.Invoke();
            _lastStormAuraSeen = DateTime.UtcNow;
            Logger.Info("Marrowgar: BONE STORM started — InFrame unlocked");
        }

        // Bone Storm кончился — обнуляем lastSeen чтобы Tick сразу понял что вихрь снят
        // (без 15-секундной grace задержки).
        if (evt.SpellId == SPELL_BONE_STORM && evt.EventType == "SPELL_AURA_REMOVED")
        {
            _lastStormAuraSeen = DateTime.MinValue;
            Logger.Info("Marrowgar: Bone Storm ended event received");
        }

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
        if (ctx.IsMaster || ctx.IsTank) return TacticAction.DoNothing;

        // Impaled (игрок в Bone Spike): stun, движение/атака игрой игнорируются.
        // Не делаем FindBoneSpike / target switch / approach — просто стоим. Ротация на текущем
        // target крутится, но физически кастов не пройдёт пока stunned (это ок — нагружать игру
        // ненужными CTM не будем).
        bool impaled = false;
        try
        {
            foreach (var id in SPELL_IMPALED_IDS)
                if (ctx.Player.HasAura(id)) { impaled = true; break; }
        }
        catch { }
        if (impaled)
        {
            if (!_wasImpaled)
            {
                _wasImpaled = true;
                Logger.Info("Marrowgar: Impaled на игроке — стою в шипе, не двигаюсь");
                Say(ctx, "Я в шипе!");
            }
            return TacticAction.Attack;
        }
        if (_wasImpaled)
        {
            _wasImpaled = false;
            Logger.Info("Marrowgar: Impaled снят — возврат во фрейм");
            if (!ctx.IsMaster && !ctx.IsTank)
                ApplyInFrameLock(ctx);
        }

        // Детект Bone Storm через ауру на боссе. Aura читается нестабильно — держим активным
        // StormGraceSec после последнего обнаружения (реальный Storm ~20 сек).
        try
        {
            var auras = ctx.Boss.GetAuraSpellIds();
            if (auras.Contains(SPELL_BONE_STORM))
            {
                _lastStormAuraSeen = DateTime.UtcNow;
            }
            _auraLogTick++;
            if (_auraLogTick >= 7)
            {
                _auraLogTick = 0;
                var tgt = ctx.ObjectManager.GetUnitByGuid(ctx.Boss.TargetGuid);
                string tgtName = tgt?.Name ?? "?";
                Logger.Info($"Marrowgar: Facing={ctx.Boss.Facing:F2} TargetGuid=0x{ctx.Boss.TargetGuid:X} TargetName='{tgtName}' auras=[{string.Join(",", auras)}]");
            }
        }
        catch { }
        bool hasBoneStormAura = _lastStormAuraSeen > DateTime.MinValue &&
            (DateTime.UtcNow - _lastStormAuraSeen) < TimeSpan.FromSeconds(StormGraceSec);

        if (hasBoneStormAura)
        {
            if (_phase != Phase.BoneStorm)
            {
                _phase = Phase.BoneStorm;
                ctx.UnlockInFrame?.Invoke();
                ctx.SetAoeAvoid?.Invoke(true); // override AoE avoid → ON (UI игнорируется)
                Logger.Info("Marrowgar: BONE STORM detected — InFrame unlocked, AoE avoid ON");
                Say(ctx, "Вихрь! Убегаю от луж");
            }
        }
        else if (_phase == Phase.BoneStorm)
        {
            _phase = Phase.Normal;
            ctx.ClearAoeAvoid?.Invoke(); // снимаем override → AoE avoid возвращается к значению из UI
            if (!ctx.IsMaster && !ctx.IsTank)
                ApplyInFrameLock(ctx);
            Logger.Info("Marrowgar: Bone Storm ended — AoE avoid restored, return to InFrame");
            Say(ctx, "Вихрь кончился, встаю во фрейм");
        }

        // Bone Storm — слейв стоит и кастует (Attack = face+rotation без approach/MoveBehind).
        // AoE avoidance на верхнем уровне BotEngine.Tick имеет приоритет и уведёт из лужи если надо.
        // DoNothing нельзя — SlaveAttackTick вызовется и MoveBehind начнёт гонять вокруг хитбокса.
        if (_phase == Phase.BoneStorm) return TacticAction.Attack;

        // Bone Spike — свич для не-хилов. Сканируем ObjectManager напрямую.
        if (!ctx.IsHealer)
        {
            var spike = FindBoneSpike(ctx);
            if (spike != null)
            {
                if (!_hasBoneSpike)
                {
                    _hasBoneSpike = true;
                    Logger.Info($"Marrowgar: Bone Spike обнаружен — switch");
                    Say(ctx, "Шип! Бью");
                }
                // Пока есть шип — InFrame lock снят (чтобы approach вёл к шипу, не к боссу).
                ctx.UnlockInFrame?.Invoke();
                // Свич только когда таргет не шип. Иначе — DoNothing → обычная логика (approach+rotation на шипе).
                if (ctx.Player.TargetGuid != spike.Guid)
                {
                    return TacticAction.SwitchTarget(spike.NpcId);
                }
                return TacticAction.DoNothing;
            }
            else if (_hasBoneSpike)
            {
                _hasBoneSpike = false;
                Logger.Info("Marrowgar: Bone Spike убит — возврат на босса");
                Say(ctx, "Шип мертв, возврат на босса");
            }
        }

        // InFrame lock обновляется КАЖДЫЙ тик — всегда ровно сзади текущей позиции/facing босса.
        ApplyInFrameLock(ctx);
        return TacticAction.DoNothing;
    }

    /// <summary>Точка сзади босса через boss.Facing (проверено логами — обновляется моментально).</summary>
    private void ApplyInFrameLock(BossContext ctx)
    {
        var boss = ctx.Boss;
        if (boss == null) return;
        float ringRadius = MathF.Max(boss.BoundingRadius + 4f, 8f);
        float angle = boss.Facing + MathF.PI;
        float destX = boss.X + ringRadius * MathF.Cos(angle);
        float destY = boss.Y + ringRadius * MathF.Sin(angle);
        ctx.LockInFrame?.Invoke(destX, destY, boss.Z);
    }

    private WowUnit? FindBoneSpike(BossContext ctx)
    {
        var boss = ctx.Boss;
        if (boss == null) return null;

        WowUnit? closest = null;
        float closestDist = float.MaxValue;
        const float MaxDistFromBoss = 40f; // шип должен быть в арене Marrowgar, не где-то далеко

        foreach (var unit in ctx.ObjectManager.Units)
        {
            try
            {
                if (!unit.IsAlive) continue;
                foreach (int spikeId in BoneSpikeNpcIds)
                {
                    if (unit.NpcId == spikeId)
                    {
                        float distFromBoss = boss.DistanceTo2D(unit);
                        if (distFromBoss > MaxDistFromBoss) continue; // старый спавн где-то далеко

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
}
