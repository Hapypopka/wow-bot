using WowBot.Core.Game.Entities;
using WowBot.Core.Navigation;

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

    /// <summary>NavEngine — навигация через навмеш (опционально)</summary>
    public WowBot.Core.Navigation.NavEngine? NavEngine { get; set; }

    public enum State { Idle, Following, Attacking, Stopped, GoingToPoint }
    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>Кто командует (мастер) — для AssistUnit, не меняется при смене follow target</summary>
    public string CommandSourceName { get; set; } = "";
    /// <summary>За кем следовать — может быть мастер или кто-то другой</summary>
    public string FollowTargetName { get; set; } = "";
    /// <summary>MasterName для совместимости — возвращает CommandSourceName</summary>
    public string MasterName { get => CommandSourceName; set => CommandSourceName = value; }
    public float FollowDistance { get; set; } = 8f;

    private ulong _followTargetGuid;
    private int _stopTimer;

    // GoingToPoint state
    private float _gotoX, _gotoY, _gotoZ;
    private bool _gotoArrived;
    /// <summary>true когда слейв добежал до точки Goto</summary>
    public bool GotoArrived => _gotoArrived;

    public SlaveController(Navigation nav, EndSceneHook hook, ObjectManager objectManager, ClickToMove ctm)
    {
        _nav = nav;
        _hook = hook;
        _objectManager = objectManager;
        _ctm = ctm;
    }

    // === Команды ===

    public ulong FollowTargetGuid => _followTargetGuid;
    public void ResetFollowTargetGuid() => _followTargetGuid = 0;

    /// <summary>Установить command source (мастер) — кого ассистить</summary>
    public void SetCommandSource(string name)
    {
        CommandSourceName = name;
        Logger.Info($"SlaveCtrl: CommandSource='{name}'");
    }

    /// <summary>Установить follow target — за кем бежать (может быть не мастер)</summary>
    public void SetFollowTarget(string name)
    {
        FollowTargetName = name;
        _followTargetGuid = 0; // сброс кэша, найдёт на следующем тике
        Logger.Info($"SlaveCtrl: FollowTarget='{name}'");
    }

    /// <summary>Установить GUID напрямую (от мастера)</summary>
    public void SetMasterGuid(ulong guid, string name)
    {
        _followTargetGuid = guid;
        CommandSourceName = name;
        FollowTargetName = name;
        Logger.Info($"SlaveCtrl: GUID set directly to 0x{guid:X} '{name}'");
    }

    /// <summary>Один раз при старте слейва — найти GUID мастера</summary>
    public void InitMasterGuid(string masterName)
    {
        if (_followTargetGuid != 0) return; // уже найден
        CommandSourceName = masterName;
        FollowTargetName = masterName;
        FindMasterGuid(masterName);
    }

    public void CmdFollow(string masterName)
    {
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() TurnLeftStop() TurnRightStop()", 100);
        _ctm.ClearAction();
        // Follow всегда за мастером (если не переопределено через SetFollowTarget)
        if (string.IsNullOrEmpty(FollowTargetName) || FollowTargetName == CommandSourceName)
            FollowTargetName = masterName;
        CommandSourceName = masterName;
        CurrentState = State.Following;
        Logger.Info($"SlaveCtrl: Following '{FollowTargetName}' (cmd source='{CommandSourceName}')");
    }

    // Legacy
    public ulong MasterGuid => _followTargetGuid;
    public void ResetMasterGuid() => _followTargetGuid = 0;

    public void CmdStop()
    {
        // Остановка через CallClickToMove на свою позицию — перебивает активный CGPlayer movement
        var player = _objectManager.LocalPlayer;
        if (player != null && _hook.IsHooked)
            _hook.CallClickToMove(player.X, player.Y, player.Z, player.BaseAddress,
                clickType: 4, precision: 0.5f, timeoutMs: 200);
        // НЕ вызываем ClearAction — иначе отменим свой же CTM-стоп
        _hook.ExecuteLua("MoveForwardStop() MoveBackwardStop() StrafeLeftStop() StrafeRightStop()", 100);
        CurrentState = State.Stopped;
        _gotoArrived = false;
        _stopTimer = 20; // 3 сек → Idle
        Logger.Info("SlaveCtrl: Stopped");
    }

    /// <summary>Бежать к точке (Ctrl+ПКМ). Сбрасывает follow.</summary>
    public void CmdGotoPoint(float x, float y, float z)
    {
        _gotoX = x;
        _gotoY = y;
        _gotoZ = z;
        _gotoArrived = false;
        CurrentState = State.GoingToPoint;

        // Навмеш: если подключён — строим путь
        if (NavEngine != null && NavEngine.IsConnected)
        {
            NavEngine.NavigateTo(x, y, z);
            Logger.Info($"SlaveCtrl: GoingToPoint NAV ({x:F1}, {y:F1}, {z:F1})");
        }
        else
        {
            _ctm.MoveTo(x, y, z, 1f);
            Logger.Info($"SlaveCtrl: GoingToPoint CTM ({x:F1}, {y:F1}, {z:F1})");
        }
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

            case State.GoingToPoint:
                TickGoingToPoint(player);
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
    /// <summary>CTM к мастеру активен — ещё не добежал</summary>
    public bool IsFollowMoving => _isFollowMoving;
    private float _lastCtmX, _lastCtmY;

    private void TickFollowing(WowPlayer player)
    {
        var master = FindMaster();
        if (master == null) return;

        float dist = player.DistanceTo(master);

        if (dist <= FollowDistance)
        {
            if (_isFollowMoving)
            {
                // Native stop: правильно тормозит клиент без инерции (слейв не проскакивает мастера).
                // Прямая запись ActionStop=0xD в память визуально тормозит плохо — бот по инерции
                // пробегает дальше если мастер резко останавливался.
                _ctm.NativeStop();
                NavEngine?.Stop();
                _isFollowMoving = false;
                Logger.Info($"SlaveCtrl: arrived (dist={dist:F1})");
            }
            return;
        }

        // Точка на земле в FollowDistance от мастера, в сторону игрока
        float dirX = player.X - master.X;
        float dirY = player.Y - master.Y;
        float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (dirLen < 0.1f) { dirX = 1f; dirY = 0f; dirLen = 1f; }
        dirX /= dirLen;
        dirY /= dirLen;
        float goalX = master.X + dirX * FollowDistance;
        float goalY = master.Y + dirY * FollowDistance;

        // Навмеш: если подключён — строим путь в обход стен
        if (NavEngine != null && NavEngine.IsConnected)
        {
            // Не спамим NavEngine если уже идём и мастер не сильно сдвинулся
            float dx = master.X - _lastCtmX;
            float dy = master.Y - _lastCtmY;
            float masterMoved = MathF.Sqrt(dx * dx + dy * dy);

            if (_isFollowMoving && NavEngine.IsNavigating && masterMoved < 5f)
            {
                NavEngine.Tick();
                return;
            }

            // Построить новый путь
            bool navOk = NavEngine.NavigateTo(goalX, goalY, master.Z);
            Logger.Info($"SlaveCtrl: NAV follow dist={dist:F1} navOk={navOk} wp={NavEngine.WaypointsRemaining}");
            if (navOk)
            {
                _lastCtmX = master.X;
                _lastCtmY = master.Y;
                _isFollowMoving = true;
                return;
            }
            // Навмеш не нашёл путь — fallback на прямой CTM ниже
            Logger.Info($"SlaveCtrl: NAV failed, fallback to CTM");
        }

        // Fallback: прямой CTM (как раньше)
        float mdx = master.X - _lastCtmX;
        float mdy = master.Y - _lastCtmY;
        float masterMoved2 = MathF.Sqrt(mdx * mdx + mdy * mdy);
        bool ctmIdle = _ctm.GetCurrentAction() == 0;

        if (_isFollowMoving && masterMoved2 < 3f && !ctmIdle)
            return;

        _ctm.MoveTo(goalX, goalY, master.Z, 0.5f);
        _lastCtmX = master.X;
        _lastCtmY = master.Y;
        _isFollowMoving = true;
    }

    private void TickGoingToPoint(WowPlayer player)
    {
        if (_gotoArrived) return;

        // Навмеш: если навигация активна — тикаем её
        if (NavEngine != null && NavEngine.IsNavigating)
        {
            NavEngine.Tick();
            if (!NavEngine.IsNavigating)
            {
                _gotoArrived = true;
                Logger.Info($"SlaveCtrl: arrived at point NAV ({_gotoX:F1}, {_gotoY:F1}, {_gotoZ:F1})");
            }
            return;
        }

        // Fallback: CTM сам остановит персонажа
        bool ctmIdle = _ctm.GetCurrentAction() == 0;
        if (ctmIdle)
        {
            _gotoArrived = true;
            Logger.Info($"SlaveCtrl: arrived at point CTM ({_gotoX:F1}, {_gotoY:F1}, {_gotoZ:F1})");
        }
    }

    // === Поиск мастера ===

    private void FindMasterGuid(string name)
    {
        _objectManager.Update();

        // 1. Ищем среди игроков через Lua (надёжнее чем Player.Name из памяти)
        string luaFind = $"for i=1,4 do if UnitName('party'..i)=='{name.Replace("'", "\\'")}' then WB_R=UnitGUID('party'..i) return end end WB_R=''";
        string? guidStr = _hook.ExecuteLuaWithResult(luaFind, 0, 500);
        if (!string.IsNullOrEmpty(guidStr) && guidStr.StartsWith("0x"))
        {
            if (ulong.TryParse(guidStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ulong pGuid) && pGuid != 0)
            {
                _followTargetGuid = pGuid;
                Logger.Info($"SlaveCtrl: GUID=0x{_followTargetGuid:X} (Lua party '{name}')");
                return;
            }
        }

        // 2. Ищем среди NPC (Units) — для GuidByTarget, берём ближайшего
        var localPlayer = _objectManager.LocalPlayer;
        WowUnit? bestUnit = null;
        float bestDist = float.MaxValue;
        int candidateCount = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (string.Equals(unit.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                candidateCount++;
                float dist = localPlayer != null
                    ? MathF.Sqrt(MathF.Pow(unit.X - localPlayer.X, 2) + MathF.Pow(unit.Y - localPlayer.Y, 2) + MathF.Pow(unit.Z - localPlayer.Z, 2))
                    : 0;
                Logger.Info($"SlaveCtrl: candidate Unit '{name}' GUID=0x{unit.Guid:X} dist={dist:F1}");
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestUnit = unit;
                }
            }
        }
        if (bestUnit != null)
        {
            _followTargetGuid = bestUnit.Guid;
            Logger.Info($"SlaveCtrl: GUID=0x{_followTargetGuid:X} (nearest Unit '{name}', dist={bestDist:F1}, candidates={candidateCount})");
            return;
        }

        // 3. Fallback: TargetUnit + сохранение/восстановление таргета (для игроков не в пати)
        string safeName = name.Replace("'", "\\'");
        string targetLua = $"WB_OLDTGT=UnitGUID('target') or '' TargetUnit('{safeName}')";
        _hook.ExecuteLua(targetLua, 200);
        System.Threading.Thread.Sleep(200);
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null && string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            _followTargetGuid = target.Guid;
            Logger.Info($"SlaveCtrl: GUID=0x{_followTargetGuid:X} (TargetUnit fallback '{name}')");
        }
        else
        {
            Logger.Warn($"SlaveCtrl: not found '{name}' (players={_objectManager.Players.Count}, units={_objectManager.Units.Count}, target='{target?.Name}')");
        }
        // Восстановить предыдущий таргет
        _hook.ExecuteLua("if WB_OLDTGT and WB_OLDTGT~='' then TargetUnit(WB_OLDTGT) else ClearTarget() end TargetLastTarget()", 200);
    }

    private WowUnit? FindMaster()
    {
        if (_followTargetGuid != 0)
        {
            var unit = _objectManager.GetUnitByGuid(_followTargetGuid);
            if (unit != null) return unit;
            _followTargetGuid = 0;
        }
        if (!string.IsNullOrEmpty(FollowTargetName))
            FindMasterGuid(FollowTargetName);
        return _followTargetGuid != 0 ? _objectManager.GetUnitByGuid(_followTargetGuid) : null;
    }
}
