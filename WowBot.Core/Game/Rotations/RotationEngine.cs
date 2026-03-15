using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game.Rotations;

public class RotationEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private Timer? _timer;
    private string _instantScript = "";
    private string _castScript = "";
    private string _fullScript = "";
    private bool _isRunning;
    private int _tickIntervalMs = 150;

    private ulong _followGuid;
    private float _followDistance = 8f;

    public bool IsRunning => _isRunning;
    public ulong FollowGuid => _followGuid;
    public event Action<string>? OnStatusChanged;

    public RotationEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
    }

    public void LoadRotation(string instantScript, string castScript, string fullScript)
    {
        _instantScript = instantScript;
        _castScript = castScript;
        _fullScript = fullScript;
    }

    public void SetFollowTarget()
    {
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke($"Follow set");
        }
    }

    public void SetFollowFromFocus()
    {
        _hook.ExecuteLua("TargetUnit('focus')", 300);
        Thread.Sleep(50);
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke($"Following focus");
        }
        _hook.ExecuteLua("TargetLastTarget()", 300);
    }

    public void ClearFollowTarget()
    {
        _followGuid = 0;
        _hook.ExecuteLua("MoveForwardStop() StrafeLeftStop() StrafeRightStop()", 200);
        OnStatusChanged?.Invoke("Follow cleared");
    }

    public void Start()
    {
        if (_isRunning || !_hook.IsHooked) return;
        if (_followGuid == 0) SetFollowFromFocus();
        _isRunning = true;
        _timer = new Timer(Tick, null, 0, _tickIntervalMs);
        OnStatusChanged?.Invoke(_followGuid != 0 ? "ACTIVE + Following" : "ACTIVE");
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        _hook.ExecuteLua("MoveForwardStop() StrafeLeftStop() StrafeRightStop()", 200);
        OnStatusChanged?.Invoke("STOPPED");
    }

    public void Toggle()
    {
        if (_isRunning) Stop(); else Start();
    }

    private void Tick(object? state)
    {
        if (!_isRunning || !_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

            var combatTarget = _objectManager.GetTarget();
            bool hasTarget = combatTarget != null && combatTarget.IsAlive;

            WowUnit? followTarget = _followGuid != 0 ? _objectManager.GetUnitByGuid(_followGuid) : null;
            float followDist = followTarget != null ? player.DistanceTo(followTarget) : 0;
            bool needsToMove = followTarget != null && followDist > _followDistance;

            // C# только считает направления и пишет facing в память
            // Вся логика движения + каста — в одном Lua вызове

            // Определяем режим: 0=стоим, 1=бежим(нет таргета), 2=strafe(есть таргет)
            int moveMode = 0;
            int strafeDir = 0; // 0=none, 1=left, 2=right

            if (needsToMove && hasTarget)
            {
                moveMode = 2;

                // Лицом к таргету (через память)
                _navigation.FaceUnit(player, combatTarget!);

                // Определяем strafe direction
                float facingAngle = _navigation.GetAngleTo(player, combatTarget!);
                float followAngle = _navigation.GetAngleTo(player, followTarget!);
                float diff = followAngle - facingAngle;
                while (diff > MathF.PI) diff -= MathF.PI * 2;
                while (diff < -MathF.PI) diff += MathF.PI * 2;
                strafeDir = diff > 0 ? 1 : 2;
            }
            else if (needsToMove && !hasTarget)
            {
                moveMode = 1;
                // Лицом к follow-таргету
                _navigation.FaceUnit(player, followTarget!);
            }
            else if (!needsToMove && hasTarget)
            {
                moveMode = 0;
                _navigation.FaceUnit(player, combatTarget!);
            }

            // Один Lua-скрипт: проверяет каст → управляет движением → кастует
            string lua = BuildTickScript(moveMode, strafeDir);
            _hook.ExecuteLua(lua, 500);
        }
        catch { }
    }

    private string BuildTickScript(int moveMode, int strafeDir)
    {
        return $@"
WB_MOVE_MODE = {moveMode}
WB_STRAFE_DIR = {strafeDir}

-- Если кастуем — ничего не делаем (не двигаемся, не кастуем)
local casting = UnitCastingInfo('player')
local channeling = UnitChannelInfo('player')
if casting or channeling then
    -- Остановить движение во время каста
    MoveForwardStop()
    StrafeLeftStop()
    StrafeRightStop()
    return
end

if WB_MOVE_MODE == 1 then
    -- Бежим к follow (нет таргета)
    StrafeLeftStop()
    StrafeRightStop()
    MoveForwardStart()

elseif WB_MOVE_MODE == 2 then
    -- Strafe к follow + instant спеллы на бегу
    MoveForwardStop()
    if WB_STRAFE_DIR == 1 then
        StrafeRightStop()
        StrafeLeftStart()
    else
        StrafeLeftStop()
        StrafeRightStart()
    end

    -- Instant спеллы на бегу
    {_instantScript}

elseif WB_MOVE_MODE == 0 then
    -- Стоим на месте, полная ротация
    MoveForwardStop()
    StrafeLeftStop()
    StrafeRightStop()

    {_fullScript}
end
";
    }

    public void Dispose() => Stop();
}
