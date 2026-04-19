using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

/// <summary>
/// Единый исполнитель боевой логики — работает одинаково в solo и slave.
/// Решает проблему "фича работает в solo, не работает в slave".
///
/// Вся боевая логика в одном месте:
/// 1. AoE Avoidance (убежать из лужи)
/// 2. CombatPositioning (MoveBehind / RangedPos)
/// 3. TryGroundAoE (Гроза, Залп)
/// 4. FaceInstant (поворот)
/// 5. TrySmartTaunt (для танков)
/// 6. ExecuteRotation (Lua скрипт)
/// </summary>
public class CombatExecutor
{
    private readonly EndSceneHook _hook;
    private readonly Navigation _navigation;
    private readonly CombatPositioning _combatPositioning;
    private readonly CombatHelper _combatHelper;
    private readonly ClickToMove _ctm;

    // Slave approach state
    private bool _slaveApproaching;
    private int _cexLogTick;

    public CombatExecutor(EndSceneHook hook, Navigation navigation,
        CombatPositioning combatPositioning, CombatHelper combatHelper, ClickToMove ctm)
    {
        _hook = hook;
        _navigation = navigation;
        _combatPositioning = combatPositioning;
        _combatHelper = combatHelper;
        _ctm = ctm;
    }

    /// <summary>
    /// Выполнить один тик боя. Вызывается из BotEngine для ЛЮБОГО режима (solo/slave/auto).
    /// </summary>
    /// <param name="player">Локальный игрок</param>
    /// <param name="target">Текущий таргет (может быть null)</param>
    /// <param name="options">Настройки боя</param>
    /// <returns>true если что-то сделали (каст, движение)</returns>
    public bool ExecuteCombatTick(WowPlayer player, WowUnit? target, CombatOptions options)
    {
        if (target == null || !target.IsAlive) return false;
        // Слейв в Attacking/Auto режиме бьёт даже если таргет не в бою (Bone Spike, стоящие мобы).
        if (!options.NoCombatCheck && !target.InCombat) return false;
        if (player.IsCasting) return false;

        // 0. Proactive AoE Avoidance — уклониться от вражеского каста ДО импакта.
        // Высший приоритет: если двигаемся, прерываем ротацию.
        if (_combatHelper.TryProactiveAvoidance(player))
            return true;

        // 1. Позиционирование (MoveBehind / RangedPos) — ЕДИНЫЙ для solo и slave
        _combatPositioning.IsMelee = options.IsMeleeSpec;
        _combatPositioning.IsTank = options.IsTankSpec;
        _combatPositioning.IsHealer = options.IsHealer;
        _combatPositioning.PlayerClass = options.PlayerClass;
        _combatPositioning.SpecName = options.SpecName;

        bool movingBehind = false;
        // В режиме "Во фрейм" MoveBehind отключён — каждый слейв стоит на своей точке кольца.
        if (options.MoveBehindEnabled && !options.InFrameMode)
        {
            if (_combatPositioning.TryMoveBehind(player, target))
            {
                movingBehind = true;
                _cexLogTick++;
                if (_cexLogTick % 7 == 0)
                    Logger.Log(LogCat.Follow, $"CEX: MoveBehind active (InFrameMode={options.InFrameMode})");
            }
            else if (_combatPositioning.TryRangedPosition(player, target))
                _navigation.FaceInstant(player, target);
        }
        else if (options.InFrameMode && _combatPositioning.IsMovingBehind)
        {
            // InFrame только что активирован — сбрасываем MoveBehind флаг иначе он блокирует approach
            _combatPositioning.ResetMoveBehind();
        }

        // Если MoveBehind активно двигает — кастуем без approach. Но только если InFrame не включён.
        if (_combatPositioning.IsMovingBehind && !options.InFrameMode)
        {
            string dc = options.IsMeleeSpec ? "" :
                "if WB_STOP_CAST and GetTime()<WB_STOP_CAST then SpellStopCasting() return end ";
            _hook.ExecuteLua(dc + options.EnemyCountLua + options.SpellFlagsLua + options.RotationScript, 500);
            return true;
        }

        // 2. Ground AoE (Гроза, Залп)
        if (!movingBehind && _combatHelper.TryGroundAoE(player, target,
                options.AoeEnabled, options.AoeMinEnemies, options.PlayerClass, options.SpecName, options.SpellFlagsLua))
            return true;

        // 3. Smart Taunt (для танков)
        if (!options.IsHealer && options.PlayerInCombat)
            _combatHelper.TrySmartTaunt(player, options.PlayerClass, options.SpellFlagsLua);

        // 4. Approach для slave (C# CTM) — учитываем хитбокс
        if (options.NeedApproach)
        {
            // InFrame работает только если таргет в бою (босс с активной тактикой).
            if (options.InFrameMode && options.InFrameLockedPos.HasValue && target.InCombat)
            {
                var lp = options.InFrameLockedPos.Value;
                float distToSpot = MathF.Sqrt((player.X - lp.X) * (player.X - lp.X) + (player.Y - lp.Y) * (player.Y - lp.Y));
                if (_cexLogTick % 7 == 0)
                    Logger.Log(LogCat.Follow, $"CEX: InFrame approach dist={distToSpot:F1} pos=({player.X:F0},{player.Y:F0}) lock=({lp.X:F0},{lp.Y:F0}) approaching={_slaveApproaching}");

                // Единый порог 1y без гистерезиса. Больше 1y — идём. Меньше — "прибыл",
                // освобождаем approach и даём сработать FaceInstant в блоке #5 ниже.
                if (distToSpot > 1f)
                {
                    float ctmDx = lp.X - _ctm.ReadX();
                    float ctmDy = lp.Y - _ctm.ReadY();
                    float ctmDriftSq = ctmDx * ctmDx + ctmDy * ctmDy;
                    int action = _ctm.GetCurrentAction();
                    if (action != 4 || ctmDriftSq > 4f)
                    {
                        _ctm.MoveTo(lp.X, lp.Y, lp.Z, 1.0f);
                    }
                    _slaveApproaching = true;
                    return true;
                }
                else if (_slaveApproaching)
                {
                    StopApproach();
                }
            }
            else
            {
                bool isMelee = options.IsMeleeSpec;
                float meleeRange = MathF.Max(target.CombatReach + player.CombatReach + 4f / 3f, 5f);
                float maxDist = isMelee ? meleeRange : 28f;
                float dist = player.DistanceTo(target);
                if (_cexLogTick % 7 == 0)
                    Logger.Log(LogCat.Follow, $"CEX: Normal approach isMelee={isMelee} dist={dist:F1} maxDist={maxDist:F1}");
                if (dist > maxDist)
                {
                    float angle = MathF.Atan2(player.Y - target.Y, player.X - target.X);
                    float stopDist = isMelee ? MathF.Max(meleeRange - 1.5f, 1.5f) : 25f;
                    float destX = target.X + stopDist * MathF.Cos(angle);
                    float destY = target.Y + stopDist * MathF.Sin(angle);
                    // Face во время движения не нужен — CTM поворачивает в сторону destination.
                    float ctmDx = destX - _ctm.ReadX();
                    float ctmDy = destY - _ctm.ReadY();
                    float driftSq = ctmDx * ctmDx + ctmDy * ctmDy;
                    if (_ctm.GetCurrentAction() != 4 || driftSq > 4f)
                    {
                        _ctm.MoveTo(destX, destY, target.Z, 1.5f);
                    }
                    _slaveApproaching = true;
                    return true;
                }
                else if (_slaveApproaching)
                {
                    StopApproach();
                }
            }
        }

        // 5. Поворот к таргету
        if (!movingBehind && !options.IsHealer && options.AutoFace)
            _navigation.FaceInstant(player, target);

        // 6. Ротация (кастеры стопают каст при Disrupting Shout)
        string dangerCheck = options.IsMeleeSpec ? "" :
            "if WB_STOP_CAST and GetTime()<WB_STOP_CAST then SpellStopCasting() return end ";
        string script = dangerCheck + options.EnemyCountLua + options.SpellFlagsLua + options.RotationScript;
        _hook.ExecuteLua(script, 500);
        return true;
    }

    /// <summary>Остановить slave approach</summary>
    public void StopApproach()
    {
        if (_slaveApproaching)
        {
            _slaveApproaching = false;
        }
    }

    public bool IsApproaching => _slaveApproaching;
}

/// <summary>Параметры для CombatExecutor — собираются в BotEngine из текущего состояния</summary>
public record CombatOptions
{
    // Что кастовать
    public string RotationScript { get; init; } = "";
    public string EnemyCountLua { get; init; } = "";
    public string SpellFlagsLua { get; init; } = "";

    // Кто мы
    public string PlayerClass { get; init; } = "";
    public string? SpecName { get; init; }
    public bool IsMeleeSpec { get; init; }
    public bool IsTankSpec { get; init; }
    public bool IsHealer { get; init; }
    public bool PlayerInCombat { get; init; }
    /// <summary>true → игнорируем `target.InCombat` (для слейвов: Attacking/Auto бьют даже не-боевые цели).</summary>
    public bool NoCombatCheck { get; init; }
    /// <summary>Режим "Во фрейм": approach ведёт слейва в зафиксированную точку (считается ОДИН раз при
    /// включении режима, потом слейв стоит там независимо от движений цели).</summary>
    public bool InFrameMode { get; init; }
    public (float X, float Y, float Z)? InFrameLockedPos { get; init; }

    // Настройки
    public bool MoveBehindEnabled { get; init; }
    public bool AoeEnabled { get; init; }
    public int AoeMinEnemies { get; init; } = 3;
    public bool AutoFace { get; init; } = true;

    // Slave-специфика (approach через Lua MoveForwardStart)
    public bool NeedApproach { get; init; }
}
