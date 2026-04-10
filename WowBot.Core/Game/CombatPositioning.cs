using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

/// <summary>
/// Позиционирование в бою — MoveBehind (мили за спину), RangedPos (ранж сбоку), TankPos (танк лицом от группы).
/// Вдохновлено NPCBots bot_ai.cpp: MoveBehind(), CalculateAttackPos(), AdjustTankingPosition()
/// </summary>
public class CombatPositioning
{
    private readonly ClickToMove _ctm;
    private int _repositionTick;

    // Роль (устанавливается BotEngine перед Tick)
    public bool IsMelee { get; set; }
    public bool IsTank { get; set; }
    public bool IsHealer { get; set; }
    public string PlayerClass { get; set; } = "";
    public string? SpecName { get; set; }

    public CombatPositioning(ClickToMove ctm)
    {
        _ctm = ctm;
    }

    /// <summary>Проверяет, находится ли точка в передней дуге юнита (PI = 180°)</summary>
    private static bool IsInFrontArc(WowUnit unit, float pointX, float pointY)
    {
        float angleToPoint = MathF.Atan2(pointY - unit.Y, pointX - unit.X);
        float diff = NormalizeAngle(angleToPoint - unit.Facing);
        return MathF.Abs(diff) < MathF.PI / 2; // передние 180° = ±90°
    }

    /// <summary>Проверяет, находится ли игрок за спиной таргета</summary>
    public static bool IsBehind(WowUnit target, WowUnit player)
    {
        return !IsInFrontArc(target, player.X, player.Y);
    }

    /// <summary>Вычисляет точку за спиной таргета. WoW melee range = target.CR + player.CR + 4/3</summary>
    private static (float x, float y, float z) GetBehindPosition(WowUnit target, WowUnit player)
    {
        float effectiveReach = target.CombatReach + player.CombatReach + 0.66f;
        effectiveReach = MathF.Max(effectiveReach, 2.0f);
        float behindAngle = target.Facing + MathF.PI;
        float x = target.X + effectiveReach * MathF.Cos(behindAngle);
        float y = target.Y + effectiveReach * MathF.Sin(behindAngle);
        return (x, y, target.Z);
    }

    /// <summary>Вычисляет точку сбоку-сзади от таргета для ранж ДПС</summary>
    private static (float x, float y, float z) GetRangedPosition(WowUnit target, WowUnit player, float distance = 25f)
    {
        // Угол от таргета к игроку + смещение чтобы не стоять в лоб
        float angleToPlayer = MathF.Atan2(player.Y - target.Y, player.X - target.X);
        // Сместиться на ~60° от текущей позиции к боку/спине
        float sideAngle = target.Facing + MathF.PI * 0.75f; // ~135° от лица = сбоку-сзади
        float x = target.X + distance * MathF.Cos(sideAngle);
        float y = target.Y + distance * MathF.Sin(sideAngle);
        return (x, y, target.Z);
    }

    /// <summary>
    /// Мили ДПС: двигается за спину таргета.
    /// Возвращает true если двигается (пропустить ротацию на этот тик).
    /// </summary>
    public bool IsMovingBehind { get; private set; }

    private int _logTick;

    public bool TryMoveBehind(WowPlayer player, WowUnit target)
    {
        // Только мили ДПС, не танк, не хилер
        if (!IsMelee || IsTank || IsHealer) return false;
        if (!target.IsAlive || !target.InCombat) return false;
        if (player.IsCasting) return false;

        float dist = player.DistanceTo(target);
        bool behind = IsBehind(target, player);
        bool inFront = IsInFrontArc(target, player.X, player.Y);

        // Логируем каждые ~3с
        _logTick++;
        if (_logTick >= 20) { _logTick = 0; Logger.Info($"MoveBehind check: behind={behind} inFront={inFront} dist={dist:F1} moving={IsMovingBehind} player=({player.X:F0},{player.Y:F0}) target=({target.X:F0},{target.Y:F0}) tFacing={target.Facing:F2}"); }

        // Интервал: рога/кот — 4 тика (600мс), остальные — 10 тиков (1.5с)
        int interval = (PlayerClass == "ROGUE" || (PlayerClass == "DRUID" && SpecName?.Contains("Feral") == true)) ? 3 : 5;
        _repositionTick++;
        if (_repositionTick < interval) { return IsMovingBehind; }
        _repositionTick = 0;

        // Уже за спиной — остановиться и дать паузу (не бежать обратно вперёд)
        if (behind)
        {
            if (IsMovingBehind)
            {
                // Только что добежали — останавливаем MoveForward чтобы approach не потащил обратно
                IsMovingBehind = false;
                _repositionTick = -(interval * 3); // пауза ~4.5с перед следующей проверкой
                Logger.Info("MoveBehind: arrived behind, pausing");
            }
            return false;
        }

        // Слишком далеко — сначала подбежать (approach обработает)
        if (dist > 12f) { IsMovingBehind = false; return false; }

        // Точка за спиной на краю мили рейнджа (target.CR + player.CR + 4/3)
        var (x, y, z) = GetBehindPosition(target, player);
        _ctm.MoveTo(x, y, z, 0.5f);
        IsMovingBehind = true;
        Logger.Info($"MoveBehind: GO ({x:F1},{y:F1}) behind target facing={target.Facing:F2} dist={dist:F1} inFront={inFront}");
        return true;
    }

    /// <summary>
    /// Ранж ДПС: встаёт на дистанции сбоку-сзади от таргета.
    /// Возвращает true если двигается.
    /// </summary>
    public bool TryRangedPosition(WowPlayer player, WowUnit target)
    {
        // Отключено: ренджи просто стоят и кастуют, approach подведёт если далеко
        return false;
    }

    /// <summary>Нормализация угла в диапазон [-PI, PI]</summary>
    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2;
        while (angle < -MathF.PI) angle += MathF.PI * 2;
        return angle;
    }
}
