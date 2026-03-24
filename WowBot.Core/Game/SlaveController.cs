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

    public enum State { Idle, Following, Attacking, Stopped }
    public State CurrentState { get; private set; } = State.Idle;

    public string MasterName { get; set; } = "";
    public float FollowDistance { get; set; } = 8f;

    private ulong _masterGuid;
    private int _stopTimer;

    public SlaveController(Navigation nav, EndSceneHook hook, ObjectManager objectManager)
    {
        _nav = nav;
        _hook = hook;
        _objectManager = objectManager;
    }

    // === Команды ===

    public void CmdFollow(string masterName)
    {
        _nav.StopAll();
        MasterName = masterName;
        CurrentState = State.Following;
        _followRetargetTick = 0;
        // WoW нативно бежит к мастеру
        _hook.ExecuteLua($"TargetUnit('{masterName}') StartAttack()", 200);
        Logger.Info($"SlaveCtrl: Following {masterName}");
    }

    public void CmdStop()
    {
        _nav.StopAll();
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
                // Ничего — юзер управляет вручную
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

    private int _followRetargetTick;

    private void TickFollowing(WowPlayer player)
    {
        // Проверяем дистанцию до мастера (через текущий таргет)
        var target = _objectManager.GetTarget();
        if (target != null && target.Type == WowObjectType.Player)
        {
            float dist = player.DistanceTo(target);
            if (dist <= FollowDistance)
            {
                // Достаточно близко — стоим
                return;
            }
        }

        // Периодически повторяем TargetUnit + StartAttack (WoW нативно бежит к таргету)
        _followRetargetTick++;
        if (_followRetargetTick >= 10) // каждые ~1.5 сек
        {
            _followRetargetTick = 0;
            _hook.ExecuteLua($"TargetUnit('{MasterName}') StartAttack()", 200);
        }
    }

    // === Поиск мастера ===

    private void FindMasterGuid(string name)
    {
        // TargetUnit через Lua → читаем GUID
        _hook.ExecuteLua($"TargetUnit('{name}')", 200);
        System.Threading.Thread.Sleep(150);
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _masterGuid = target.Guid;
            Logger.Info($"SlaveCtrl: master GUID=0x{_masterGuid:X}");
        }
    }

    private WowUnit? FindMaster()
    {
        if (_masterGuid != 0)
        {
            var unit = _objectManager.GetUnitByGuid(_masterGuid);
            if (unit != null) return unit;
            _masterGuid = 0;
        }
        // GUID потерян — ищем заново
        if (!string.IsNullOrEmpty(MasterName))
            FindMasterGuid(MasterName);
        return _masterGuid != 0 ? _objectManager.GetUnitByGuid(_masterGuid) : null;
    }
}
