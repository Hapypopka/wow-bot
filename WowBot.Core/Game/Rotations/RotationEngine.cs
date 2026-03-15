using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game.Rotations;

public class RotationEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private Timer? _timer;
    private string _fullScript = "";
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public event Action<string>? OnStatusChanged;

    public RotationEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
    }

    public void LoadRotation(string fullScript)
    {
        _fullScript = fullScript;
    }

    public void Start()
    {
        if (_isRunning || !_hook.IsHooked || string.IsNullOrEmpty(_fullScript)) return;
        _isRunning = true;
        _timer = new Timer(Tick, null, 0, 150);
        OnStatusChanged?.Invoke("Rotation ON");
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        OnStatusChanged?.Invoke("Rotation OFF");
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

            var target = _objectManager.GetTarget();
            if (target == null || !target.IsAlive) return;

            // Face target
            _navigation.FaceUnit(player, target);

            // Rotation
            _hook.ExecuteLua(_fullScript, 500);
        }
        catch { }
    }

    public void Dispose() => Stop();
}
