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

    /// <summary>Вычисляет точку за спиной таргета</summary>
    private static (float x, float y, float z) GetBehindPosition(WowUnit target, float distance = 2.0f)
    {
        float behindAngle = target.Facing + MathF.PI;
        float x = target.X + distance * MathF.Cos(behindAngle);
        float y = target.Y + distance * MathF.Sin(behindAngle);
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
    public bool TryMoveBehind(WowPlayer player, WowUnit target)
    {
        // Только мили ДПС, не танк, не хилер
        if (!IsMelee || IsTank || IsHealer) return false;
        if (!target.IsAlive || !target.InCombat) return false;
        if (player.IsCasting) return false;

        // Интервал: рога/кот — каждые 4 тика (600мс), остальные — 10 тиков (1.5с)
        int interval = (PlayerClass == "ROGUE" || (PlayerClass == "DRUID" && SpecName?.Contains("Feral") == true)) ? 4 : 10;
        _repositionTick++;
        if (_repositionTick < interval) return false;
        _repositionTick = 0;

        // Уже за спиной — не двигаемся
        if (IsBehind(target, player)) return false;

        // Слишком далеко — сначала подбежать (CTM approach обработает)
        if (player.DistanceTo(target) > 8f) return false;

        // Вычисляем точку за спиной
        var (x, y, z) = GetBehindPosition(target);
        _ctm.MoveTo(x, y, z, 0.5f);
        Logger.Info($"MoveBehind: ({x:F1},{y:F1}) behind target facing={target.Facing:F2}");
        return true;
    }

    /// <summary>
    /// Ранж ДПС: встаёт на дистанции сбоку-сзади от таргета.
    /// Возвращает true если двигается.
    /// </summary>
    public bool TryRangedPosition(WowPlayer player, WowUnit target)
    {
        if (IsMelee || IsTank) return false;
        if (IsHealer) return false; // хилеры позиционируются по-другому
        if (!target.IsAlive || !target.InCombat) return false;
        if (player.IsCasting) return false;

        _repositionTick++;
        if (_repositionTick < 15) return false; // каждые ~2.25с
        _repositionTick = 0;

        float dist = player.DistanceTo(target);
        // Слишком близко (< 15м) или в передней дуге — перепозиция
        if (dist > 15f && !IsInFrontArc(target, player.X, player.Y)) return false;

        var (x, y, z) = GetRangedPosition(target, player);
        _ctm.MoveTo(x, y, z, 1.0f);
        Logger.Info($"RangedPos: ({x:F1},{y:F1}) at range from target");
        return true;
    }

    /// <summary>Нормализация угла в диапазон [-PI, PI]</summary>
    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2;
        while (angle < -MathF.PI) angle += MathF.PI * 2;
        return angle;
    }
}
