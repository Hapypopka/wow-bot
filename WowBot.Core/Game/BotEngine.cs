using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

public class BotEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private Timer? _timer;

    private string _instantScript = "";
    private string _fullScript = "";

    private bool _followEnabled;
    private bool _rotationEnabled;
    private ulong _followGuid;
    private float _followDistance = 8f;

    public bool FollowEnabled => _followEnabled;
    public bool RotationEnabled => _rotationEnabled;
    public ulong FollowGuid => _followGuid;
    public float FollowDistance
    {
        get => _followDistance;
        set => _followDistance = Math.Clamp(value, 0f, 20f);
    }

    public event Action<string>? OnStatusChanged;

    public BotEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
    }

    public void LoadRotation(string instantScript, string fullScript)
    {
        _instantScript = instantScript;
        _fullScript = fullScript;
    }

    // --- Follow target ---

    public void SetFollowTarget()
    {
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke("Follow target set");
        }
    }

    public void ClearFollowTarget()
    {
        _followGuid = 0;
        OnStatusChanged?.Invoke("Follow target cleared");
    }

    // --- Toggle ---

    public void ToggleFollow()
    {
        _followEnabled = !_followEnabled;
        if (_followEnabled) EnsureRunning();
        else if (!_rotationEnabled) StopTimer();
        if (!_followEnabled)
            _hook.ExecuteLua("MoveForwardStop()", 100);
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void ToggleRotation()
    {
        _rotationEnabled = !_rotationEnabled;
        if (_rotationEnabled) EnsureRunning();
        else if (!_followEnabled) StopTimer();
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void StopAll()
    {
        _followEnabled = false;
        _rotationEnabled = false;
        StopTimer();
        _hook.ExecuteLua("MoveForwardStop()", 100);
        OnStatusChanged?.Invoke("Stopped");
    }

    private void EnsureRunning()
    {
        if (_timer != null) return;
        _timer = new Timer(Tick, null, 0, 150);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private string GetStatusText()
    {
        if (_followEnabled && _rotationEnabled) return "Follow + Rotation";
        if (_followEnabled) return "Follow only";
        if (_rotationEnabled) return "Rotation only";
        return "Stopped";
    }

    // --- Main tick ---

    private void Tick(object? state)
    {
        if (!_followEnabled && !_rotationEnabled) return;
        if (!_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

            var target = _objectManager.GetTarget();
            bool hasTarget = target != null && target.IsAlive;

            WowUnit? followTarget = _followGuid != 0 ? _objectManager.GetUnitByGuid(_followGuid) : null;
            float followDist = followTarget != null ? player.DistanceTo(followTarget) : 0;
            bool needsToMove = _followEnabled && followTarget != null && followDist > _followDistance;

            // === ТОЛЬКО FOLLOW ===
            if (_followEnabled && !_rotationEnabled)
            {
                if (needsToMove)
                {
                    _navigation.FaceUnit(player, followTarget!);
                    _hook.ExecuteLua("MoveForwardStart()", 100);
                }
                else
                {
                    _hook.ExecuteLua("MoveForwardStop()", 100);
                }
                return;
            }

            // === ТОЛЬКО ROTATION ===
            if (!_followEnabled && _rotationEnabled)
            {
                if (hasTarget)
                {
                    _navigation.FaceUnit(player, target!);
                    _hook.ExecuteLua(_fullScript, 500);
                }
                return;
            }

            // === ОБА: FOLLOW + ROTATION ===

            // Кастуем? → стоим, не двигаемся
            _hook.ExecuteLua(@"
                if UnitCastingInfo('player') or UnitChannelInfo('player') then
                    MoveForwardStop()
                end
            ", 100);

            if (needsToMove)
            {
                // БЕЖИМ к follow + instants на бегу
                // Лицом к follow для движения
                _navigation.FaceUnit(player, followTarget!);
                _hook.ExecuteLua("MoveForwardStart()", 100);

                // Кратко повернуться к таргету для instants
                if (hasTarget)
                {
                    _navigation.FaceUnit(player, target!);
                    _hook.ExecuteLua(_instantScript, 300);
                    // Вернуть facing к follow для движения
                    _navigation.FaceUnit(player, followTarget!);
                }
            }
            else
            {
                // СТОИМ — полная ротация, лицом к таргету
                _hook.ExecuteLua("MoveForwardStop()", 100);

                if (hasTarget)
                {
                    _navigation.FaceUnit(player, target!);
                    _hook.ExecuteLua(_fullScript, 500);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopAll();
    }
}
