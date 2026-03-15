using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public enum StrafeDirection { None, Left, Right }

public class Navigation
{
    private readonly MemoryReader _memory;
    private readonly EndSceneHook _hook;
    private StrafeDirection _currentStrafe = StrafeDirection.None;
    private bool _isMovingForward;

    public Navigation(MemoryReader memory, EndSceneHook hook)
    {
        _memory = memory;
        _hook = hook;
    }

    public float GetAngleTo(WowUnit from, WowUnit to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float angle = MathF.Atan2(dy, dx);
        if (angle < 0) angle += MathF.PI * 2;
        return angle;
    }

    public bool IsFacing(WowUnit from, WowUnit to, float tolerance = 0.5f)
    {
        float needed = GetAngleTo(from, to);
        float current = from.Facing;
        float diff = MathF.Abs(needed - current);
        if (diff > MathF.PI) diff = MathF.PI * 2 - diff;
        return diff < tolerance;
    }

    /// <summary>
    /// Поворачивает к юниту (только запись в память)
    /// </summary>
    public void FaceUnit(WowUnit player, WowUnit target)
    {
        if (IsFacing(player, target, 0.5f)) return;
        float angle = GetAngleTo(player, target);
        _memory.WriteFloat(player.BaseAddress + Offsets.UnitRotation, angle);
    }

    /// <summary>
    /// Бежит прямо к юниту (лицом к нему + MoveForward)
    /// </summary>
    public void MoveToward(WowUnit player, WowUnit target)
    {
        FaceUnit(player, target);
        StopStrafe();
        if (!_isMovingForward)
        {
            _hook.ExecuteLua("MoveForwardStart()", 100);
            _isMovingForward = true;
        }
    }

    /// <summary>
    /// Strafe к followTarget, лицом к combatTarget
    /// Бежит боком в сторону followTarget, но повёрнут к combatTarget
    /// </summary>
    public void StrafeToward(WowUnit player, WowUnit followTarget, WowUnit combatTarget)
    {
        // Лицом к combatTarget (для кастов)
        FaceUnit(player, combatTarget);

        // Определяем с какой стороны followTarget относительно нашего facing
        float facingAngle = GetAngleTo(player, combatTarget);
        float followAngle = GetAngleTo(player, followTarget);

        float diff = followAngle - facingAngle;
        // Нормализуем в [-PI, PI]
        while (diff > MathF.PI) diff -= MathF.PI * 2;
        while (diff < -MathF.PI) diff += MathF.PI * 2;

        // Если follow-цель слева — strafe left, если справа — strafe right
        StrafeDirection needed = diff > 0 ? StrafeDirection.Left : StrafeDirection.Right;

        // Если follow-цель сзади (угол > 90°), нужен forward + strafe
        bool needForward = MathF.Abs(diff) < MathF.PI * 0.6f; // ~108°

        StopForward();

        if (needForward && !_isMovingForward)
        {
            _hook.ExecuteLua("MoveForwardStart()", 100);
            _isMovingForward = true;
        }

        if (needed != _currentStrafe)
        {
            StopStrafe();
            if (needed == StrafeDirection.Left)
                _hook.ExecuteLua("StrafeLeftStart()", 100);
            else
                _hook.ExecuteLua("StrafeRightStart()", 100);
            _currentStrafe = needed;
        }
    }

    public void StopAll()
    {
        StopForward();
        StopStrafe();
    }

    public void StopForward()
    {
        if (_isMovingForward)
        {
            _hook.ExecuteLua("MoveForwardStop()", 100);
            _isMovingForward = false;
        }
    }

    public void StopStrafe()
    {
        if (_currentStrafe != StrafeDirection.None)
        {
            _hook.ExecuteLua("StrafeLeftStop() StrafeRightStop()", 100);
            _currentStrafe = StrafeDirection.None;
        }
    }

    public void StopMoving()
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop()", 200);
        _isMovingForward = false;
        _currentStrafe = StrafeDirection.None;
    }
}
