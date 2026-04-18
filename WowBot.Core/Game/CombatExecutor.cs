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
                movingBehind = true;
            else if (_combatPositioning.TryRangedPosition(player, target))
                _navigation.FaceInstant(player, target);
        }

        // Если MoveBehind активно двигает — кастуем без approach
        if (_combatPositioning.IsMovingBehind)
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
            if (options.InFrameMode && options.InFrameLockedPos.HasValue)
            {
                // Зафиксированная точка — один раз вычислена при включении, больше не пересчитывается.
                var lp = options.InFrameLockedPos.Value;
                float distToSpot = MathF.Sqrt((player.X - lp.X) * (player.X - lp.X) + (player.Y - lp.Y) * (player.Y - lp.Y));
                if (distToSpot > 1.5f)
                {
                    _navigation.FaceInstant(player, target);
                    _ctm.MoveTo(lp.X, lp.Y, lp.Z, 1.5f);
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
                if (dist > maxDist)
                {
                    // Мили: внутрь мили рейнджа с запасом. Рейнж: на 25 ярдов
                    float angle = MathF.Atan2(player.Y - target.Y, player.X - target.X);
                    float stopDist = isMelee ? MathF.Max(meleeRange - 1.5f, 1.5f) : 25f;
                    float destX = target.X + stopDist * MathF.Cos(angle);
                    float destY = target.Y + stopDist * MathF.Sin(angle);
                    _navigation.FaceInstant(player, target);
                    _ctm.MoveTo(destX, destY, target.Z, 1.5f);
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
