using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

public class FollowEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private Timer? _timer;
    private bool _isRunning;

    private ulong _followGuid;
    private float _followDistance = 8f;

    public bool IsRunning => _isRunning;
    public ulong FollowGuid => _followGuid;
    public bool NeedsToMove { get; private set; }
    public event Action<string>? OnStatusChanged;

    public FollowEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
    }

    public void SetFollowTarget()
    {
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke("Follow set");
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
            OnStatusChanged?.Invoke("Following focus");
        }
        Thread.Sleep(50);
        _hook.ExecuteLua("TargetLastTarget()", 500);
    }

    public void ClearFollowTarget()
    {
        _followGuid = 0;
        NeedsToMove = false;
        _hook.ExecuteLua("MoveForwardStop()", 200);
        OnStatusChanged?.Invoke("Follow cleared");
    }

    public void Start()
    {
        if (_isRunning || !_hook.IsHooked) return;
        if (_followGuid == 0) SetFollowFromFocus();
        _isRunning = true;
        _timer = new Timer(Tick, null, 0, 150);
        OnStatusChanged?.Invoke(_followGuid != 0 ? "Following" : "No target");
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        NeedsToMove = false;
        _hook.ExecuteLua("MoveForwardStop()", 200);
        OnStatusChanged?.Invoke("Follow stopped");
    }

    public void Toggle()
    {
        if (_isRunning) Stop(); else Start();
    }

    private void Tick(object? state)
    {
        if (!_isRunning || !_hook.IsHooked || _followGuid == 0) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

            var followTarget = _objectManager.GetUnitByGuid(_followGuid);
            if (followTarget == null) return;

            float dist = player.DistanceTo(followTarget);
            NeedsToMove = dist > _followDistance;

            if (NeedsToMove)
            {
                _navigation.FaceUnit(player, followTarget);

                _hook.ExecuteLua(@"
                    if not UnitCastingInfo('player') and not UnitChannelInfo('player') then
                        MoveForwardStart()
                    else
                        MoveForwardStop()
                    end
                ", 200);
            }
            else
            {
                _hook.ExecuteLua("MoveForwardStop()", 100);
            }
        }
        catch { }
    }

    public void Dispose() => Stop();
}
