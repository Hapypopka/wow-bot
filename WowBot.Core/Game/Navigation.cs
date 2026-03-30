using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class Navigation
{
    private readonly MemoryReader _memory;
    private readonly EndSceneHook _hook;

    // Состояние движения
    private bool _isMovingForward;

    // Настройки
    private const float FACE_TOLERANCE = 0.5f;   // Радиан — "уже смотрим" (~29°)

    public bool IsMovingForward => _isMovingForward;

    public Navigation(MemoryReader memory, EndSceneHook hook)
    {
        _memory = memory;
        _hook = hook;
    }

    /// <summary>
    /// Рассчитать угол от одного юнита к другому (мировые координаты)
    /// </summary>
    public static float GetAngleTo(WowUnit from, WowUnit to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float angle = MathF.Atan2(dy, dx);
        if (angle < 0) angle += MathF.PI * 2;
        return angle;
    }

    /// <summary>
    /// Проверить смотрит ли юнит на цель (в пределах tolerance)
    /// </summary>
    public static bool IsFacing(WowUnit from, WowUnit to, float tolerance = FACE_TOLERANCE)
    {
        float needed = GetAngleTo(from, to);
        float current = from.Facing;
        float diff = MathF.Abs(needed - current);
        if (diff > MathF.PI) diff = MathF.PI * 2 - diff;
        return diff < tolerance;
    }

    /// <summary>
    /// Кратчайшая разница углов (с учётом перехода через 0/2π)
    /// </summary>
    private static float AngleDiff(float from, float to)
    {
        float diff = to - from;
        while (diff > MathF.PI) diff -= MathF.PI * 2;
        while (diff < -MathF.PI) diff += MathF.PI * 2;
        return diff;
    }

    // Позиция для детекта движения — "стоит" = не двигался 3 тика подряд (~450мс)
    private float _lastX, _lastY, _lastZ;
    private int _standingTicks;
    private const float MOVE_THRESHOLD = 0.3f;
    private const int STANDING_TICKS_REQUIRED = 3; // 3 тика × 150мс = 450мс

    /// <summary>
    /// Стоит ли игрок (не двигался 3 тика подряд).
    /// </summary>
    public bool IsPlayerStanding(WowUnit player)
    {
        float dx = player.X - _lastX;
        float dy = player.Y - _lastY;
        float dz = player.Z - _lastZ;
        bool moved = (dx * dx + dy * dy + dz * dz) > MOVE_THRESHOLD * MOVE_THRESHOLD;
        _lastX = player.X; _lastY = player.Y; _lastZ = player.Z;

        if (moved)
            _standingTicks = 0;
        else
            _standingTicks++;

        return _standingTicks >= STANDING_TICKS_REQUIRED;
    }

    /// <summary>Обратная совместимость</summary>
    public bool IsPlayerMoving(WowUnit player) => !IsPlayerStanding(player);

    /// <summary>
    /// Мгновенный поворот к цели — запись в память + серверный апдейт.
    /// Не поворачивает на бегу и во время каста.
    /// </summary>
    private bool _turnPending;

    /// <summary>
    /// Поворот к цели. Возвращает true = смотрим (можно кастовать), false = крутимся.
    /// Запись в память + TurnLeftStart → 80мс → TurnLeftStop в фоне.
    /// </summary>
    public bool FaceInstant(WowUnit player, WowUnit target)
    {
        if (_turnPending) return false; // предыдущий Turn ещё в процессе
        if (IsFacing(player, target)) return true;
        if (player.IsCasting) return false;
        if (IsPlayerMoving(player)) return false;

        float needed = GetAngleTo(player, target);
        float diff = AngleDiff(player.Facing, needed);

        _memory.WriteFloat(player.BaseAddress + Offsets.UnitRotation, needed);

        _turnPending = true;
        string cmd = diff > 0 ? "TurnLeftStart()" : "TurnRightStart()";
        Task.Run(async () =>
        {
            try
            {
                _hook.ExecuteLua(cmd, 30);
                await Task.Delay(80);
                _hook.ExecuteLua("TurnLeftStop() TurnRightStop()", 30);
            }
            catch { }
            finally { _turnPending = false; }
        });
        return false;
    }

    /// <summary>
    /// Повернуться к юниту. Вызывать каждый тик.
    /// Возвращает true когда смотрим на цель.
    /// </summary>
    public bool FaceUnit(WowUnit player, WowUnit target)
    {
        if (IsFacing(player, target)) return true;
        if (player.IsCasting) return false;
        FaceInstant(player, target);
        return IsFacing(player, target);
    }

    /// <summary>
    /// Сбросить состояние поворота (при смене цели)
    /// </summary>
    public void ResetTurn()
    {
        _hook.ExecuteLua("TurnLeftStop() TurnRightStop()", 20);
    }

    // === Движение ===

    public void StartMoveForward()
    {
        if (!_isMovingForward)
        {
            _hook.ExecuteLua("MoveForwardStart()", 50);
            _isMovingForward = true;
        }
    }

    public void StopMoveForward()
    {
        if (_isMovingForward)
        {
            _hook.ExecuteLua("MoveForwardStop()", 50);
            _isMovingForward = false;
        }
    }

    /// <summary>Записать facing напрямую в память (без серверного поворота)</summary>
    public void WriteFacing(Entities.WowUnit unit, float facing)
    {
        _memory.WriteFloat(unit.BaseAddress + Offsets.UnitRotation, facing);
    }

    /// <summary>
    /// Полная остановка всего движения
    /// </summary>
    public void StopAll()
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop() TurnLeftStop() TurnRightStop()", 100);
        _isMovingForward = false;
    }
}
