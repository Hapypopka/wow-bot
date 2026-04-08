using WowBot.Core.Memory;

namespace WowBot.Core.Game;

/// <summary>
/// Click-to-Move — записываем координаты в память WoW,
/// клиент сам плавно бежит к точке.
/// </summary>
public class ClickToMove
{
    private readonly MemoryReader _memory;
    private EndSceneHook? _hook;
    private uint _playerBase;

    /// <summary>Установить хук для вызова CGPlayer_C__ClickToMove напрямую</summary>
    public void SetHook(EndSceneHook hook) => _hook = hook;
    public void SetPlayerBase(uint playerBase) => _playerBase = playerBase;

    // Стандартные адреса для 3.3.5a build 12340
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
        // Активация CTM — на холодном клиенте [0xBD08F4]+0x30 = 0, CTM игнорируется
        try
        {
            uint ptr = _memory.ReadUInt32(0x00BD08F4);
            if (ptr != 0 && ptr < 0x7FFFFFFF)
            {
                _memory.WriteInt32(ptr + 0x30, 1);
                Logger.Info($"CTM activated: 0x{ptr + 0x30:X8} = 1");
            }
        }
        catch { }
    }

    /// <summary>Прогрев CTM — записываем action=3 (stop) чтобы инициализировать rotation system</summary>
    public void WarmupMovement(uint playerBase)
    {
        try
        {
            // action=3 — тот же stop что делает /follow при завершении
            _memory.WriteInt32(CTM_Action, 3);
            Logger.Info("CTM warmup: action=3 (follow-stop)");
        }
        catch (Exception ex) { Logger.Error("CTM warmup failed", ex); }
    }

    /// <summary>
    /// Двигает персонажа к точке (плавно, WoW сам рулит поворотом)
    /// </summary>
    public void MoveTo(float x, float y, float z, float precision = 1.0f,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "",
        [System.Runtime.CompilerServices.CallerFilePath] string file = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        string src = System.IO.Path.GetFileNameWithoutExtension(file);
        Logger.Log(LogCat.Follow, $"CTM.MoveTo({x:F0},{y:F0},{z:F0}) prec={precision:F1} from {src}.{caller}:{line}");

        // Вызываем настоящую функцию CGPlayer_C__ClickToMove через EndScene хук
        // Это корректно инициализирует movement state machine (без cold start бага)
        if (_hook != null && _hook.IsHooked && _playerBase != 0)
        {
            _hook.CallClickToMove(x, y, z, _playerBase, clickType: 4, precision: precision, timeoutMs: 200);
            return;
        }

        // Fallback: прямая запись (если хук недоступен)
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
        _memory.WriteInt32(CTM_Action, ActionStop);
    }

    /// <summary>
    /// Жёсткая остановка: записывает текущие координаты как цель + Stop
    /// </summary>
    public void StopAt(float x, float y, float z)
    {
        _memory.WriteFloat(CTM_X, x);
        _memory.WriteFloat(CTM_Y, y);
        _memory.WriteFloat(CTM_Z, z);
        _memory.WriteFloat(CTM_Precision, 5.0f);
        _memory.WriteInt32(CTM_Action, ActionMoveTo);
    }

    /// <summary>
    /// Проверяет текущий CTM action (0 = idle)
    /// </summary>
    public int GetCurrentAction()
    {
        return _memory.ReadInt32(CTM_Action);
    }

    public float ReadX() => _memory.ReadFloat(CTM_X);
    public float ReadY() => _memory.ReadFloat(CTM_Y);
    public float ReadZ() => _memory.ReadFloat(CTM_Z);

    /// <summary>Обнулить CTM action (персонаж не двигается)</summary>
    public void ClearAction() => _memory.WriteInt32(CTM_Action, ActionNone);
}
