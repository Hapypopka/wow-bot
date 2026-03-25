using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

/// <summary>
/// Управляет слейвом — следование, атака, стоп.
/// Вызывать Tick() каждые 150мс из BotEngine.
/// </summary>
public class SlaveController
{
    private readonly Navigation _nav;
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly ClickToMove _ctm;

    public enum State { Idle, Following, Attacking, Stopped }
    public State CurrentState { get; private set; } = State.Idle;

    public string MasterName { get; set; } = "";
    public float FollowDistance { get; set; } = 8f;

    private ulong _masterGuid;
    private int _stopTimer;

    public SlaveController(Navigation nav, EndSceneHook hook, ObjectManager objectManager, ClickToMove ctm)
    {
        _nav = nav;
        _hook = hook;
        _objectManager = objectManager;
        _ctm = ctm;
    }

    // === Команды ===

    public void ResetMasterGuid() => _masterGuid = 0;

    /// <summary>Один раз при старте слейва — найти GUID мастера через TargetUnit</summary>
    public void InitMasterGuid(string masterName)
    {
        if (_masterGuid != 0) return; // уже найден
        MasterName = masterName;
        FindMasterGuid(masterName);
    }

    public void CmdFollow(string masterName)
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() TurnLeftStop() TurnRightStop()", 100);
        _ctm.ClearAction();
        MasterName = masterName;
        CurrentState = State.Following;
        Logger.Info($"SlaveCtrl: Following {masterName}");
    }

    public void CmdStop()
    {
        // CTM на свою позицию — мгновенная остановка (CTM перебивает CTM)
        var player = _objectManager.LocalPlayer;
        if (player != null)
            _ctm.MoveTo(player.X, player.Y, player.Z, 0.5f);
        // НЕ вызываем ClearAction — иначе отменим свой же CTM-стоп
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop()", 100);
        CurrentState = State.Stopped;
        _stopTimer = 20; // 3 сек → Idle
        Logger.Info("SlaveCtrl: Stopped");
    }

    // === Tick ===

    public void Tick()
    {
        var player = _objectManager.LocalPlayer;
        if (player == null) return;

        switch (CurrentState)
        {
            case State.Idle:
                break;

            case State.Following:
                TickFollowing(player);
                break;

            case State.Stopped:
                _stopTimer--;
                if (_stopTimer <= 0)
                {
                    CurrentState = State.Idle;
                    Logger.Info("SlaveCtrl: Stopped → Idle");
                }
                break;
        }
    }

    private bool _isFollowMoving;
    private float _lastCtmX, _lastCtmY;

    private void TickFollowing(WowPlayer player)
    {
        var master = FindMaster();
        if (master == null) return;

        float dist = player.DistanceTo(master);

        if (dist <= FollowDistance)
        {
            if (_isFollowMoving) { _ctm.Stop(); _isFollowMoving = false; }
            return;
        }

        // Не спамим CTM если мастер стоит и CTM ещё бежит
        float dx = master.X - _lastCtmX;
        float dy = master.Y - _lastCtmY;
        float masterMoved = MathF.Sqrt(dx * dx + dy * dy);
        bool ctmIdle = _ctm.GetCurrentAction() == 0;

        if (_isFollowMoving && masterMoved < 3f && !ctmIdle)
            return;

        // Точка на земле в FollowDistance от мастера, в сторону игрока
        float dirX = player.X - master.X;
        float dirY = player.Y - master.Y;
        float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (dirLen < 0.1f) { dirX = 1f; dirY = 0f; dirLen = 1f; }
        dirX /= dirLen;
        dirY /= dirLen;
        float goalX = master.X + dirX * FollowDistance;
        float goalY = master.Y + dirY * FollowDistance;

        _ctm.MoveTo(goalX, goalY, master.Z, 0.5f);
        _lastCtmX = master.X;
        _lastCtmY = master.Y;
        _isFollowMoving = true;
    }

    // === Поиск мастера ===

    private void FindMasterGuid(string name)
    {
        // TargetUnit для точного поиска по имени, потом TargetLastTarget чтобы не сломать таргет
        _hook.ExecuteLua($"TargetUnit('{name}')", 200);
        System.Threading.Thread.Sleep(150);
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _masterGuid = target.Guid;
            Logger.Info($"SlaveCtrl: master GUID=0x{_masterGuid:X}");
        }
        _hook.ExecuteLua("TargetLastTarget()", 100);
    }

    private WowUnit? FindMaster()
    {
        if (_masterGuid != 0)
        {
            var unit = _objectManager.GetUnitByGuid(_masterGuid);
            if (unit != null) return unit;
            _masterGuid = 0;
        }
        if (!string.IsNullOrEmpty(MasterName))
            FindMasterGuid(MasterName);
        return _masterGuid != 0 ? _objectManager.GetUnitByGuid(_masterGuid) : null;
    }
}
