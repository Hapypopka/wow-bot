using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class Navigation
{
    private readonly MemoryReader _memory;
    private readonly EndSceneHook _hook;

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

    public bool IsFacing(WowUnit from, WowUnit to, float tolerance = 0.8f)
    {
        float needed = GetAngleTo(from, to);
        float current = from.Facing;
        float diff = MathF.Abs(needed - current);
        if (diff > MathF.PI) diff = MathF.PI * 2 - diff;
        return diff < tolerance;
    }

    /// <summary>
    /// Поворачивает к юниту (только запись в память, без движения)
    /// </summary>
    public void FaceUnit(WowUnit player, WowUnit target)
    {
        if (IsFacing(player, target, 0.5f)) return;
        float angle = GetAngleTo(player, target);
        _memory.WriteFloat(player.BaseAddress + Offsets.UnitRotation, angle);
    }

    public void StopMoving()
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop()", 200);
    }
}
