using WowBot.Core.Game;
using WowBot.Core.Game.Entities;

namespace WowBot.Core.Navigation;

/// <summary>
/// Обёртка над AmeisenNavClient для WowBot.
/// Подключается к NavServer, строит пути, проводит юнитов по вейпоинтам через CTM.
/// </summary>
public class NavEngine
{
    private AmeisenNavClient? _client;
    private readonly ClickToMove _ctm;
    private readonly Game.ObjectManager _objectManager;

    // Текущий путь
    private Vector3[]? _currentPath;
    private int _currentWaypoint;
    private bool _isNavigating;

    // Настройки
    public bool IsConnected => _client?.IsConnected == true;
    public bool IsNavigating => _isNavigating;
    public int WaypointsRemaining => _currentPath != null ? _currentPath.Length - _currentWaypoint : 0;

    public NavEngine(ClickToMove ctm, Game.ObjectManager objectManager)
    {
        _ctm = ctm;
        _objectManager = objectManager;
    }

    /// <summary>Подключиться к NavServer</summary>
    public bool Connect(string ip = "127.0.0.1", int port = 47110)
    {
        try
        {
            _client?.Dispose();
            _client = new AmeisenNavClient(ip, port);
            bool ok = _client.TryConnect();
            if (ok)
            {
                Logger.Info($"NavEngine: connected to {ip}:{port}");
            }
            else
            {
                Logger.Info($"NavEngine: failed to connect to {ip}:{port}");
            }
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Info($"NavEngine: connect error: {ex.Message}");
            return false;
        }
    }

    /// <summary>Отключиться</summary>
    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        _isNavigating = false;
        _currentPath = null;
        Logger.Info("NavEngine: disconnected");
    }

    /// <summary>Построить путь от игрока к точке. Возвращает массив вейпоинтов или null.</summary>
    public Vector3[]? GetPath(float fromX, float fromY, float fromZ, float toX, float toY, float toZ, int mapId = 0)
    {
        if (_client == null || !_client.IsConnected) return null;

        try
        {
            var start = new Vector3(fromX, fromY, fromZ);
            var end = new Vector3(toX, toY, toZ);
            return _client.GetPath(mapId, start, end, PathFlags.SmoothCatmullRom | PathFlags.ValidateMoveAlongSurface);
        }
        catch (Exception ex)
        {
            Logger.Info($"NavEngine: GetPath error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Начать навигацию к точке (строит путь и идёт по вейпоинтам)</summary>
    public bool NavigateTo(float toX, float toY, float toZ, int mapId = 0)
    {
        var player = _objectManager.LocalPlayer;
        if (player == null) return false;

        var path = GetPath(player.X, player.Y, player.Z, toX, toY, toZ, mapId);
        if (path == null || path.Length == 0)
        {
            Logger.Info($"NavEngine: no path to ({toX:F0},{toY:F0},{toZ:F0})");
            return false;
        }

        _currentPath = path;
        _currentWaypoint = 0;
        _isNavigating = true;
        Logger.Info($"NavEngine: path {path.Length} waypoints to ({toX:F0},{toY:F0},{toZ:F0})");

        // CTM к первому вейпоинту
        var wp = _currentPath[0];
        _ctm.MoveTo(wp.X, wp.Y, wp.Z, 0.5f);
        return true;
    }

    /// <summary>Начать навигацию к юниту (follow через навмеш)</summary>
    public bool NavigateToUnit(WowUnit target, float stopDistance, int mapId = 0)
    {
        return NavigateTo(target.X, target.Y, target.Z, mapId);
    }

    /// <summary>Вызывать каждый тик (150мс). Продвигает по вейпоинтам.</summary>
    public void Tick()
    {
        if (!_isNavigating || _currentPath == null) return;

        var player = _objectManager.LocalPlayer;
        if (player == null) return;

        var wp = _currentPath[_currentWaypoint];
        float dx = player.X - wp.X;
        float dy = player.Y - wp.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        // Добрались до текущего вейпоинта? (5yd — не застревать на углах/лестницах)
        if (dist < 5f)
        {
            _currentWaypoint++;

            if (_currentWaypoint >= _currentPath.Length)
            {
                _isNavigating = false;
                _currentPath = null;
                Logger.Info("NavEngine: path complete");
                return;
            }

            // Lookahead: CTM к вейпоинту через один (срезаем углы)
            int lookIdx = Math.Min(_currentWaypoint + 1, _currentPath.Length - 1);
            var next = _currentPath[lookIdx];
            _ctm.MoveTo(next.X, next.Y, next.Z, 0.5f);
        }
        else if (_ctm.GetCurrentAction() == 0 && dist > 3f)
        {
            // CTM idle но не добрались — повторить с lookahead
            int lookIdx = Math.Min(_currentWaypoint + 1, _currentPath.Length - 1);
            var next = _currentPath[lookIdx];
            _ctm.MoveTo(next.X, next.Y, next.Z, 0.5f);
        }
    }

    /// <summary>Остановить навигацию</summary>
    public void Stop()
    {
        _isNavigating = false;
        _currentPath = null;
        _currentWaypoint = 0;
    }
}
