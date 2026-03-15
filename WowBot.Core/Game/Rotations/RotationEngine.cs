using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game.Rotations;

public class RotationEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private Timer? _timer;
    private string _rotationScript = "";
    private bool _isRunning;
    private int _tickIntervalMs = 150;

    private ulong _followGuid;
    private bool _isMoving;
    private float _followDistance = 8f; // дистанция при которой останавливается

    public bool IsRunning => _isRunning;
    public ulong FollowGuid => _followGuid;
    public event Action<string>? OnStatusChanged;

    public RotationEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
    }

    public void LoadRotation(string luaScript)
    {
        _rotationScript = luaScript;
    }

    /// <summary>
    /// Запоминает текущий таргет как цель для follow
    /// </summary>
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

    /// <summary>
    /// Запоминает фокус как цель для follow (через быстрый swap таргета)
    /// </summary>
    public void SetFollowFromFocus()
    {
        // Таргетим фокус → читаем GUID → возвращаем таргет
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

    /// <summary>
    /// Убирает цель follow
    /// </summary>
    public void ClearFollowTarget()
    {
        _followGuid = 0;
        if (_isMoving)
        {
            _navigation.StopMoving();
            _isMoving = false;
        }
        OnStatusChanged?.Invoke("Follow cleared");
    }

    public void Start()
    {
        if (_isRunning || !_hook.IsHooked || string.IsNullOrEmpty(_rotationScript)) return;

        // Автоматически подхватить фокус как follow-цель
        if (_followGuid == 0)
        {
            SetFollowFromFocus();
        }

        _isRunning = true;
        _timer = new Timer(Tick, null, 0, _tickIntervalMs);
        OnStatusChanged?.Invoke(_followGuid != 0 ? "Rotation ACTIVE + Following focus" : "Rotation ACTIVE (no follow)");
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        if (_isMoving)
        {
            try { _navigation.StopMoving(); } catch { }
            _isMoving = false;
        }
        OnStatusChanged?.Invoke("Rotation STOPPED");
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

            // --- Follow логика ---
            if (_followGuid != 0)
            {
                var followTarget = _objectManager.GetUnitByGuid(_followGuid);
                if (followTarget != null)
                {
                    float dist = player.DistanceTo(followTarget);

                    if (dist > _followDistance)
                    {
                        // Далеко — бежим к цели (не кастуем)
                        _navigation.FaceUnit(player, followTarget);
                        if (!_isMoving)
                        {
                            _hook.ExecuteLua("MoveForwardStart()", 100);
                            _isMoving = true;
                        }
                        return; // Не кастуем пока бежим
                    }
                    else if (_isMoving)
                    {
                        // Подбежали — останавливаемся
                        _hook.ExecuteLua("MoveForwardStop()", 100);
                        _isMoving = false;
                    }
                }
            }

            // --- Face target перед кастом ---
            var target = _objectManager.GetTarget();
            if (target != null && target.IsAlive)
            {
                _navigation.FaceUnit(player, target);
            }

            // --- Ротация ---
            _hook.ExecuteLua(_rotationScript, 500);
        }
        catch { }
    }

    public void Dispose() => Stop();
}
