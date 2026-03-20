using WowBot.Core.Memory;

namespace WowBot.Core.Game;

/// <summary>
/// Click-to-Move — записываем координаты в память WoW,
/// клиент сам плавно бежит к точке.
/// </summary>
public class ClickToMove
{
    private readonly MemoryReader _memory;

    // Стандартные адреса для 3.3.5a build 12340
    // Могут отличаться на WoWCircle — нужно проверить
    private const uint CTM_Base = 0x00CA11D8;
    private const uint CTM_Action = CTM_Base + 0x1C;
    private const uint CTM_X = CTM_Base + 0x8C;
    private const uint CTM_Y = CTM_Base + 0x90;
    private const uint CTM_Z = CTM_Base + 0x94;
    private const uint CTM_Precision = CTM_Base + 0x0C;

    // CTM Action types
    public const int ActionNone = 0;
    public const int ActionMoveTo = 0x4;
    public const int ActionInteract = 0x5;
    public const int ActionStop = 0xD;

    public ClickToMove(MemoryReader memory)
    {
        _memory = memory;
    }

    /// <summary>
    /// Двигает персонажа к точке (плавно, WoW сам рулит поворотом)
    /// </summary>
    public void MoveTo(float x, float y, float z, float precision = 1.0f)
    {
        _memory.WriteFloat(CTM_X, x);
        _memory.WriteFloat(CTM_Y, y);
        _memory.WriteFloat(CTM_Z, z);
        _memory.WriteFloat(CTM_Precision, precision);
        _memory.WriteInt32(CTM_Action, ActionMoveTo);
    }

    /// <summary>
    /// Останавливает CTM движение
    /// </summary>
    public void Stop()
    {
        // Сначала ставим координаты на текущую позицию (чтобы WoW не бежал к старой точке)
        // Потом ActionNone — полная остановка
        _memory.WriteInt32(CTM_Action, ActionNone);
    }

    /// <summary>
    /// Проверяет текущий CTM action (0 = idle)
    /// </summary>
    public int GetCurrentAction()
    {
        return _memory.ReadInt32(CTM_Action);
    }
}
