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
    private bool _isMoving;
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
            OnStatusChanged?.Invoke($"Follow set (dist<{_followDistance}yd)");
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
            OnStatusChanged?.Invoke($"Following focus (dist<{_followDistance}yd)");
        }
        _hook.ExecuteLua("TargetLastTarget()", 300);
    }

    public void ClearFollowTarget()
    {
        _followGuid = 0;
        _navigation.StopAll();
        _isMoving = false;
        OnStatusChanged?.Invoke("Follow cleared");
    }

    public void Start()
    {
        if (_isRunning || !_hook.IsHooked) return;

        if (_followGuid == 0) SetFollowFromFocus();

        _isRunning = true;
        _timer = new Timer(Tick, null, 0, _tickIntervalMs);
        OnStatusChanged?.Invoke(_followGuid != 0 ? "ACTIVE + Following" : "ACTIVE (no follow)");
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        _navigation.StopAll();
        _isMoving = false;
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

            WowUnit? followTarget = null;
            float followDist = 0;

            if (_followGuid != 0)
                followTarget = _objectManager.GetUnitByGuid(_followGuid);

            if (followTarget != null)
                followDist = player.DistanceTo(followTarget);

            bool needsToMove = followTarget != null && followDist > _followDistance;
            if (needsToMove && hasTarget)
            {
                // === РЕЖИМ: бежим к follow + атакуем ===

                // 1. Пробуем instant-спеллы на бегу (лицом к таргету, strafe к follow)
                _navigation.StrafeToward(player, followTarget!, combatTarget!);
                _isMoving = true;

                // Кастуем instant-спеллы (работают на бегу)
                _hook.ExecuteLua(_instantScript, 400);

                // Не кастуем cast-спеллы на бегу — только instants
            }
            else if (needsToMove && !hasTarget)
            {
                // === РЕЖИМ: просто бежим к follow (нет таргета) ===
                _navigation.MoveToward(player, followTarget!);
                _isMoving = true;
            }
            else
            {
                // === РЕЖИМ: стоим на месте, полная ротация ===
                if (_isMoving)
                {
                    _navigation.StopAll();
                    _isMoving = false;
                }

                if (hasTarget)
                {
                    _navigation.FaceUnit(player, combatTarget!);
                    _hook.ExecuteLua(_fullScript, 500);
                }
            }
        }
        catch { }
    }

    public void Dispose() => Stop();
}
