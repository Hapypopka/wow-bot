namespace WowBot.Core.Game;

/// <summary>
/// Hivemind — мультибоксинг через SendAddonMessage.
/// Мастер шлёт команды, слейвы слушают и выполняют.
/// Канал: WBHIVE, формат: CMD:arg
/// </summary>
public class Hivemind
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private readonly ClickToMove _ctm;
    private BotEngine? _botEngine;

    private const string CHANNEL = "WBHIVE";

    public void SetBotEngine(BotEngine engine) => _botEngine = engine;

    public enum Role { None, Master, Slave }
    public enum Command { Follow, Attack, Stop, Auto, Scatter, Stack, Ping, Goto, Register }

    /// <summary>Информация о подключённом слейве</summary>
    public class SlaveInfo
    {
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool Selected { get; set; } = false; // выбран для адресных команд
        public double LastSeen { get; set; }
    }

    /// <summary>Список подключённых слейвов (мастер)</summary>
    public List<SlaveInfo> ConnectedSlaves { get; } = new();
    public event Action? OnSlavesChanged;

    /// <summary>Режим слейва — каждая команда отменяет предыдущую</summary>
    public enum SlaveMode { Idle, Following, Attacking, Auto }

    private Role _currentRole = Role.None;
    public Role CurrentRole
    {
        get => _currentRole;
        set
        {
            StopCtmWatch();
            _currentRole = value;
            Mode = SlaveMode.Idle;
            MasterName = "";
            LastCommand = null;
            LastCommandArg = "";
            if (value == Role.Master) StartCtmWatch();
        }
    }
    public bool IsActive => CurrentRole != Role.None;
    public string MasterName { get; private set; } = "";

    /// <summary>Текущий режим слейва</summary>
    public SlaveMode Mode { get; private set; } = SlaveMode.Idle;

    // Слейв: последняя полученная команда
    public Command? LastCommand { get; private set; }
    public string LastCommandArg { get; private set; } = "";

    public event Action<string>? OnStatusChanged;
    public event Action<Command, string>? OnCommandReceived;
    /// <summary>Авто-включение UI: ("rotation",true), ("buffs",true)</summary>
    public event Action<string, bool>? OnAutoToggle;

    public Hivemind(EndSceneHook hook, ObjectManager objectManager, Navigation navigation, ClickToMove ctm)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
        _ctm = ctm;
    }

    // ==================== МАСТЕР ====================

    /// <summary>Получить список выбранных слейвов (пусто = все)</summary>
    private string GetTargetList()
    {
        var selected = ConnectedSlaves.Where(s => s.Selected).Select(s => s.Name).ToList();
        return selected.Count > 0 ? string.Join(",", selected) : "";
    }

    /// <summary>Отправить команду слейвам (адресно или всем)</summary>
    public void SendCommand(Command cmd, string arg = "")
    {
        if (CurrentRole != Role.Master) return;

        // Если есть выбранные слейвы — добавляем |список
        string targets = GetTargetList();
        string fullArg = string.IsNullOrEmpty(targets) ? arg : $"{arg}~{targets}";

        string msg = $"{cmd}:{fullArg}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);

        Logger.Info($"Hivemind: MASTER sent {cmd} {arg}");
        OnStatusChanged?.Invoke($"Sent: {cmd}");
    }

    /// <summary>Мастер: бейте мой таргет</summary>
    public void CmdAttack()
    {
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.Attack, name);
    }

    /// <summary>Мастер: все ко мне</summary>
    public void CmdFollow()
    {
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.Follow, name);
    }

    /// <summary>Мастер: стоп</summary>
    public void CmdStop() => SendCommand(Command.Stop);

    /// <summary>Мастер: авторежим (follow + auto-assist)</summary>
    public void CmdAuto()
    {
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.Auto, name);
    }

    /// <summary>Мастер: рассыпьтесь на N метров</summary>
    public void CmdScatter(int meters = 10) => SendCommand(Command.Scatter, meters.ToString());

    /// <summary>Мастер: стакайтесь на мне</summary>
    public void CmdStack()
    {
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.Stack, name);
    }

    /// <summary>Мастер: пинг (проверка связи)</summary>
    public void CmdPing() => SendCommand(Command.Ping);

    /// <summary>Мастер: отправить слейвов в точку (Ctrl+ПКМ)</summary>
    public void CmdGoto(float x, float y, float z)
    {
        string coords = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1};{1:F1};{2:F1}", x, y, z);
        SendCommand(Command.Goto, coords);
    }

    // --- Мастер: Ctrl+ПКМ детекция (быстрый поток) ---
    private float _lastCtmX, _lastCtmY, _lastCtmZ;
    private Thread? _ctmWatchThread;
    private volatile bool _ctmWatchRunning;
    private volatile bool _gotoFired; // флаг для MasterTick — отправить команду
    private volatile bool _needStopMovement; // флаг — остановить движение через Lua
    private volatile int _suppressCtmTicks; // счётчик подавления CTM (каждый тик 5мс)

    private float _gotoX, _gotoY, _gotoZ;

    /// <summary>Запустить быстрый поток отслеживания Ctrl+CTM</summary>
    public void StartCtmWatch()
    {
        if (_ctmWatchRunning) return;
        _ctmWatchRunning = true;
        _lastCtmX = _ctm.ReadX();
        _lastCtmY = _ctm.ReadY();
        _lastCtmZ = _ctm.ReadZ();

        _ctmWatchThread = new Thread(() =>
        {
            while (_ctmWatchRunning)
            {
                try
                {
                    bool ctrlDown = Memory.WinApi.IsKeyDown(Memory.WinApi.VK_LCONTROL);

                    if (ctrlDown)
                    {
                        int action = _ctm.GetCurrentAction();
                        if (action == ClickToMove.ActionMoveTo)
                        {
                            float cx = _ctm.ReadX();
                            float cy = _ctm.ReadY();
                            float cz = _ctm.ReadZ();

                            float dx = cx - _lastCtmX;
                            float dy = cy - _lastCtmY;
                            float moved = MathF.Sqrt(dx * dx + dy * dy);

                            if (moved > 1f)
                            {
                                // CTM перебивает CTM — "иди на своё место"
                                var p = _objectManager.LocalPlayer;
                                if (p != null)
                                {
                                    float facing = p.Facing;
                                    _ctm.MoveTo(p.X, p.Y, p.Z, 0.5f);
                                    // Восстановить facing чтобы мастер не повернулся
                                    _navigation.WriteFacing(p, facing);
                                }

                                _gotoX = cx;
                                _gotoY = cy;
                                _gotoZ = cz;
                                _gotoFired = true;

                                _lastCtmX = cx;
                                _lastCtmY = cy;
                                _lastCtmZ = cz;
                            }
                        }
                    }
                    else
                    {
                        // Обновляем baseline когда Ctrl не зажат
                        _lastCtmX = _ctm.ReadX();
                        _lastCtmY = _ctm.ReadY();
                        _lastCtmZ = _ctm.ReadZ();
                    }
                }
                catch { /* memory read fail — ignore */ }

                Thread.Sleep(5); // 5мс — ловим клик за 1 кадр WoW
            }
        })
        { IsBackground = true, Name = "CtmWatch" };
        _ctmWatchThread.Start();
        Logger.Info("Hivemind: CTM watch thread started");
    }

    public void StopCtmWatch()
    {
        _ctmWatchRunning = false;
        _ctmWatchThread = null;
    }

    /// <summary>Мастер тик: отправить Goto если быстрый поток поймал клик</summary>
    public void MasterTick()
    {
        if (CurrentRole != Role.Master) return;

        if (_gotoFired)
        {
            _gotoFired = false;
            CmdGoto(_gotoX, _gotoY, _gotoZ);
            Logger.Info($"Hivemind: MASTER Ctrl+CTM → Goto({_gotoX:F1}, {_gotoY:F1}, {_gotoZ:F1})");
        }
    }

    /// <summary>Мастер: зарегистрировать слейва (или обновить)</summary>
    public void RegisterSlave(string name, string className)
    {
        var existing = ConnectedSlaves.FirstOrDefault(s => s.Name == name);
        if (existing != null)
        {
            existing.ClassName = className;
            existing.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            ConnectedSlaves.Add(new SlaveInfo
            {
                Name = name,
                ClassName = className,
                LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Logger.Info($"Hivemind: slave registered — {name} ({className})");
        }
        OnSlavesChanged?.Invoke();
    }

    /// <summary>Мастер: тогл выбора слейва</summary>
    public void ToggleSlaveSelection(string name)
    {
        var slave = ConnectedSlaves.FirstOrDefault(s => s.Name == name);
        if (slave != null)
        {
            slave.Selected = !slave.Selected;
            OnSlavesChanged?.Invoke();
        }
    }

    // ==================== СЛЕЙВ: СОСТОЯНИЕ ====================

    /// <summary>Сбросить при включении слейва</summary>
    public void ResetSlaveState()
    {
        Mode = SlaveMode.Idle;
        _hook.ExecuteLua("WB_HIVE_CMD='' WB_HIVE_ARG='' WB_HIVE_TIME=0 WB_HIVE_REGISTERED=nil", 200);
        _slaveListenerInstalled = false;
        if (_botEngine != null) _botEngine.LastHiveCheck = 0;
        Logger.Info("Hivemind: slave state reset");
    }

    /// <summary>Слейв: отправить Register мастеру (имя + класс)</summary>
    public void SendRegister(string playerClass)
    {
        string name = _objectManager.GetPlayerName() ?? "unknown";
        string msg = $"Register:{name};{playerClass}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);
        Logger.Info($"Hivemind: SLAVE sent Register — {name} ({playerClass})");
    }

    internal bool _slaveListenerInstalled;
    private int _autoAssistTick;

    /// <summary>
    /// Авторежим тик: AssistUnit каждые ~1 сек, если таргет жив — ротация.
    /// Вызывать из BotEngine.Tick().
    /// </summary>
    public void SlaveAutoTick()
    {
        if (Mode != SlaveMode.Auto || string.IsNullOrEmpty(MasterName)) return;

        _autoAssistTick++;
        if (_autoAssistTick < 7) return; // каждые ~1 сек
        _autoAssistTick = 0;

        // Сбросить таргет + взять таргет мастера. Если у мастера нет цели — слейв без таргета → follow
        _hook.ExecuteLua($"ClearTarget() AssistUnit('{MasterName}')", 200);
    }

    private ulong _masterGuid;

    private Entities.WowUnit? FindPlayerByName(string name)
    {
        if (_masterGuid != 0)
        {
            var cached = _objectManager.GetUnitByGuid(_masterGuid);
            if (cached != null) return cached;
            _masterGuid = 0;
        }

        var player = _objectManager.LocalPlayer;
        if (player == null) return null;

        Entities.WowUnit? closest = null;
        float closestDist = float.MaxValue;
        foreach (var p in _objectManager.Players)
        {
            if (p.Guid == _objectManager.LocalPlayerGuid) continue;
            float d = player.DistanceTo(p);
            if (d < closestDist)
            {
                closestDist = d;
                closest = p;
            }
        }
        if (closest != null)
        {
            _masterGuid = closest.Guid;
            Logger.Info($"Hivemind: found master GUID=0x{_masterGuid:X} dist={closestDist:F1}");
        }
        return closest;
    }

    /// <summary>Получить юнит мастера</summary>
    public Entities.WowUnit? GetMasterUnit() => FindPlayerByName(MasterName);

    // ==================== СЛЕЙВ: КОМАНДЫ ====================

    public static string GetSlaveListenerScript() => $@"
if WB_HIVE_FRAME then WB_HIVE_FRAME:UnregisterAllEvents() WB_HIVE_FRAME:SetScript('OnEvent',nil) end
local g,_=GetNumMacros() if g<2 then CreateMacro('WH',1,'init') end
WB_HIVE_FRAME = CreateFrame('Frame')
WB_HIVE_FRAME:RegisterEvent('CHAT_MSG_ADDON')
WB_HIVE_FRAME:SetScript('OnEvent', function(self, event, prefix, msg, channel, sender)
    if prefix ~= '{CHANNEL}' then return end
    if sender == UnitName('player') then return end
    local cmd, arg = strsplit(':', msg, 2)
    WB_HIVE_CMD = cmd or ''
    WB_HIVE_ARG = arg or ''
    WB_HIVE_SENDER = sender or ''
    WB_HIVE_TIME = GetTime()
    WB_HIVE_NEW=1
end)
WB_HIVE_REGISTERED = true
WB_HIVE_CMD = ''
WB_HIVE_TIME = 0
";

    public static string GetSlaveReadScript() =>
        "WB_R=(WB_HIVE_CMD or '')..'|'..(WB_HIVE_ARG or '')..'|'..(WB_HIVE_SENDER or '')..'|'..(WB_HIVE_TIME or '0')";

    public static (Command? cmd, string arg, string sender, double time) ParseSlaveResponse(string? response)
    {
        if (string.IsNullOrEmpty(response)) return (null, "", "", 0);
        var parts = response.Split('|');
        if (parts.Length < 4) return (null, "", "", 0);

        Command? cmd = parts[0] switch
        {
            "Follow" => Command.Follow,
            "Attack" => Command.Attack,
            "Stop" => Command.Stop,
            "Auto" => Command.Auto,
            "Scatter" => Command.Scatter,
            "Stack" => Command.Stack,
            "Ping" => Command.Ping,
            "Goto" => Command.Goto,
            "Register" => Command.Register,
            _ => null
        };

        double.TryParse(parts[3], System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double time);

        return (cmd, parts[1], parts[2], time);
    }

    /// <summary>Общий сброс — вызывается перед каждой командой</summary>
    private void ResetMode()
    {
        Mode = SlaveMode.Idle;
        _autoAssistTick = 0;
        _botEngine?.SlaveCtrl.CmdStop();
        // Сбросить таргет и авто-атаку чтобы Lua из предыдущей команды не повлияла
        _hook.ExecuteLua("ClearTarget() if IsCurrentAction(72) then StopAttack() end", 100);
        OnAutoToggle?.Invoke("rotation", false);
        OnAutoToggle?.Invoke("buffs", false);
    }

    public void ExecuteSlaveCommand(Command cmd, string arg)
    {
        // Register от слейва → мастер запоминает (не выполняет как команду)
        if (cmd == Command.Register)
        {
            if (CurrentRole == Role.Master)
            {
                var regParts = arg.Split(';');
                if (regParts.Length >= 2)
                    RegisterSlave(regParts[0], regParts[1]);
            }
            return;
        }

        // Мастер не выполняет команды слейвов
        if (CurrentRole != Role.Slave) return;

        // Фильтр адресации: если в arg есть ~список — проверяем своё имя
        string cleanArg = arg;
        if (arg.Contains('~'))
        {
            var split = arg.Split('~', 2);
            cleanArg = split[0];
            string targetList = split[1];
            string myName = _objectManager.GetPlayerName() ?? "";
            if (!string.IsNullOrEmpty(targetList) && !targetList.Split(',').Contains(myName))
            {
                Logger.Info($"Hivemind: SLAVE skipping {cmd} — not in target list [{targetList}]");
                return; // команда не для нас
            }
        }

        LastCommand = cmd;
        LastCommandArg = cleanArg;

        // Каждая команда отменяет предыдущую
        ResetMode();

        switch (cmd)
        {
            case Command.Follow:
            case Command.Stack:
                // Ко мне — только следовать, не бить
                MasterName = cleanArg;
                Mode = SlaveMode.Following;
                _botEngine?.SlaveCtrl.CmdFollow(cleanArg);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE follow master={cleanArg}");
                break;

            case Command.Attack:
                // Бейте таргет — ассистим мастера, ротация, подбег к цели
                MasterName = cleanArg;
                Mode = SlaveMode.Attacking;
                _hook.ExecuteLua($"AssistUnit('{cleanArg}') StartAttack()", 200);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE attack target of {cleanArg}");
                break;

            case Command.Auto:
                // Авторежим — follow + auto-assist в бою
                MasterName = cleanArg;
                Mode = SlaveMode.Auto;
                _botEngine?.SlaveCtrl.CmdFollow(cleanArg);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE auto mode, master={cleanArg}");
                break;

            case Command.Stop:
                // Стоп — всё отменено в ResetMode()
                Logger.Info("Hivemind: SLAVE stop");
                break;

            case Command.Scatter:
                _hook.ExecuteLua("MoveForwardStart() MoveForwardStop() MoveBackwardStart()", 200);
                Task.Run(async () => { await Task.Delay(1500); _hook.ExecuteLua("MoveBackwardStop()", 100); });
                Logger.Info($"Hivemind: SLAVE scatter {cleanArg}m");
                break;

            case Command.Goto:
                // Направить слейва в точку — парсим координаты
                var coords = cleanArg.Split(';');
                if (coords.Length == 3 &&
                    float.TryParse(coords[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float gx) &&
                    float.TryParse(coords[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float gy) &&
                    float.TryParse(coords[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float gz))
                {
                    Mode = SlaveMode.Following; // чтобы хилер тоже двигался
                    _ctm.MoveTo(gx, gy, gz, 1f);
                    Logger.Info($"Hivemind: SLAVE goto ({gx:F1}, {gy:F1}, {gz:F1})");
                }
                break;

            case Command.Ping:
                string myName = _objectManager.GetPlayerName() ?? "slave";
                _hook.ExecuteLua($"SendAddonMessage('{CHANNEL}','Pong:{myName}','PARTY')", 200);
                Logger.Info("Hivemind: SLAVE pong");
                break;
        }

        OnCommandReceived?.Invoke(cmd, arg);
        OnStatusChanged?.Invoke($"Cmd: {cmd}");
    }
}
