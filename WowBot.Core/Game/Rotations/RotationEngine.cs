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
        _hook.ExecuteLua("TargetUnit('focus')", 500);
        Thread.Sleep(200);
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null && target.Guid != _objectManager.LocalPlayerGuid)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke($"Following focus");
        }
        Thread.Sleep(50);
        _hook.ExecuteLua("TargetLastTarget()", 500);
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

            // mode 0 = стоим + кастуем, mode 1 = бежим к follow
            int moveMode = needsToMove ? 1 : 0;

            // Facing
            if (moveMode == 1)
                _navigation.FaceUnit(player, followTarget!);
            else if (hasTarget)
                _navigation.FaceUnit(player, combatTarget!);

            // Lua: проверка каста + движение
            _hook.ExecuteLua($@"
if UnitCastingInfo('player') or UnitChannelInfo('player') then
    MoveForwardStop()
elseif {moveMode} == 1 then
    MoveForwardStart()
else
    MoveForwardStop()
end
", 300);

            // Ротация — когда стоим и есть таргет
            if (moveMode == 0 && hasTarget)
                _hook.ExecuteLua(_fullScript, 500);
        }
        catch { }
    }

    public void Dispose() => Stop();
}
