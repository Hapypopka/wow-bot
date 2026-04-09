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

    // Slave approach state
    private bool _slaveApproaching;

    public CombatExecutor(EndSceneHook hook, Navigation navigation,
        CombatPositioning combatPositioning, CombatHelper combatHelper)
    {
        _hook = hook;
        _navigation = navigation;
        _combatPositioning = combatPositioning;
        _combatHelper = combatHelper;
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
        if (target == null || !target.IsAlive || !target.InCombat) return false;
        if (player.IsCasting) return false;

        // 1. Позиционирование (MoveBehind / RangedPos) — ЕДИНЫЙ для solo и slave
        _combatPositioning.IsMelee = options.IsMeleeSpec;
        _combatPositioning.IsTank = options.IsTankSpec;
        _combatPositioning.IsHealer = options.IsHealer;
        _combatPositioning.PlayerClass = options.PlayerClass;
        _combatPositioning.SpecName = options.SpecName;

        bool movingBehind = false;
        if (options.MoveBehindEnabled)
        {
            if (_combatPositioning.TryMoveBehind(player, target))
                movingBehind = true;
            else if (_combatPositioning.TryRangedPosition(player, target))
                _navigation.FaceInstant(player, target);
        }

        // Если MoveBehind активно двигает — кастуем без approach
        if (_combatPositioning.IsMovingBehind)
        {
            _hook.ExecuteLua(options.EnemyCountLua + options.SpellFlagsLua + options.RotationScript, 500);
            return true;
        }

        // 2. Ground AoE (Гроза, Залп)
        if (!movingBehind && _combatHelper.TryGroundAoE(player, target,
                options.AoeEnabled, options.AoeMinEnemies, options.PlayerClass, options.SpecName, options.SpellFlagsLua))
            return true;

        // 3. Smart Taunt (для танков)
        if (!options.IsHealer && options.PlayerInCombat)
            _combatHelper.TrySmartTaunt(player, options.PlayerClass, options.SpellFlagsLua);

        // 4. Approach для slave (Lua MoveForwardStart/Stop)
        string approachLua = "";
        if (options.NeedApproach)
        {
            bool isMelee = options.IsMeleeSpec;
            int distId = isMelee ? 3 : 1;
            approachLua = $"if not CheckInteractDistance('target',{distId}) then MoveForwardStart() WB_FWD=1 " +
                          $"elseif WB_FWD then MoveForwardStop() WB_FWD=nil end ";
            _slaveApproaching = true;
        }

        // 5. Поворот к таргету
        if (!movingBehind && !options.IsHealer && options.AutoFace)
            _navigation.FaceInstant(player, target);

        // 6. Ротация
        string script = approachLua + options.EnemyCountLua + options.SpellFlagsLua + options.RotationScript;
        _hook.ExecuteLua(script, 500);
        return true;
    }

    /// <summary>Остановить slave approach</summary>
    public void StopApproach()
    {
        if (_slaveApproaching)
        {
            try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
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

    // Настройки
    public bool MoveBehindEnabled { get; init; }
    public bool AoeEnabled { get; init; }
    public int AoeMinEnemies { get; init; } = 3;
    public bool AutoFace { get; init; } = true;

    // Slave-специфика (approach через Lua MoveForwardStart)
    public bool NeedApproach { get; init; }
}
