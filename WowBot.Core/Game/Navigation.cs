using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class Navigation
{
    private readonly MemoryReader _memory;
    private readonly EndSceneHook _hook;

    // Состояние поворота
    private enum TurnState { Idle, Turning, Done }
    private TurnState _turnState = TurnState.Idle;
    private float _targetFacing;
    private int _turnTicks; // Счётчик тиков с начала поворота

    // Состояние движения
    private bool _isMovingForward;

    // Настройки
    private const float FACE_TOLERANCE = 0.3f;   // Радиан — "уже смотрим" (~17°)
    private const int MAX_TURN_TICKS = 5;         // Макс тиков на поворот (750мс)

    public bool IsMovingForward => _isMovingForward;
    public bool IsTurning => _turnState == TurnState.Turning;

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

    /// <summary>
    /// Повернуться к юниту. Вызывать каждый тик.
    /// Возвращает true когда поворот завершён.
    /// НЕ перезаписывает facing каждый тик — пишет один раз, ждёт, стопит.
    /// </summary>
    public bool FaceUnit(WowUnit player, WowUnit target)
    {
        // Уже смотрим на цель?
        if (IsFacing(player, target))
        {
            FinishTurn();
            return true;
        }

        // Не поворачиваем во время каста
        if (player.IsCasting) return false;

        float needed = GetAngleTo(player, target);

        switch (_turnState)
        {
            case TurnState.Idle:
                // Начинаем поворот: записываем facing + TurnStart
                _targetFacing = needed;
                _turnTicks = 0;

                // Записываем facing в память (мгновенный локальный поворот)
                _memory.WriteFloat(player.BaseAddress + Offsets.UnitRotation, needed);

                // Определяем направление и запускаем серверный поворот
                float diff = AngleDiff(player.Facing, needed);
                if (diff > 0)
                    _hook.ExecuteLua("TurnRightStop() TurnLeftStart()", 30);
                else
                    _hook.ExecuteLua("TurnLeftStop() TurnRightStart()", 30);

                _turnState = TurnState.Turning;
                return false;

            case TurnState.Turning:
                _turnTicks++;

                // Проверяем: довернулись?
                if (IsFacing(player, target) || _turnTicks >= MAX_TURN_TICKS)
                {
                    FinishTurn();
                    return true;
                }

                // Ещё не довернулись — НЕ перезаписываем, просто ждём
                return false;

            case TurnState.Done:
                // Поворот завершён, сброс для следующего
                _turnState = TurnState.Idle;
                return true;
        }

        return false;
    }

    /// <summary>
    /// Завершить поворот — остановить TurnStart
    /// </summary>
    private void FinishTurn()
    {
        if (_turnState == TurnState.Turning)
        {
            _hook.ExecuteLua("TurnLeftStop() TurnRightStop()", 30);
        }
        _turnState = TurnState.Done;
    }

    /// <summary>
    /// Сбросить состояние поворота (при смене цели)
    /// </summary>
    public void ResetTurn()
    {
        if (_turnState == TurnState.Turning)
            _hook.ExecuteLua("TurnLeftStop() TurnRightStop()", 30);
        _turnState = TurnState.Idle;
        _turnTicks = 0;
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

    /// <summary>
    /// Полная остановка всего движения
    /// </summary>
    public void StopAll()
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop() TurnLeftStop() TurnRightStop()", 100);
        _isMovingForward = false;
        _turnState = TurnState.Idle;
        _turnTicks = 0;
    }
}
