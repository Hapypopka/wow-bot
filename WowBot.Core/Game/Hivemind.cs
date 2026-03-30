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
    public enum Command { Follow, Attack, Stop, StopFollow, Auto, Scatter, Stack, Ping, Goto, Register, SetBuff, Wipe, RefreshGuid, Interact, GossipSelect, GossipAccept }

    /// <summary>Информация о подключённом слейве</summary>
    public class SlaveInfo
    {
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool Selected { get; set; } = false; // выбран для адресных команд
        public bool IgnoreGlobal { get; set; } = false; // игнорирует общие команды
        public double LastSeen { get; set; }
        public Command? ActiveCommand { get; set; } = null; // последняя команда
        public string FollowTargetName { get; set; } = ""; // за кем бежит (пусто = мастер)
    }

    /// <summary>Список подключённых слейвов (мастер)</summary>
    public List<SlaveInfo> ConnectedSlaves { get; } = new();
    public event Action? OnSlavesChanged;
    public void NotifySlavesChanged() => OnSlavesChanged?.Invoke();

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
    /// <summary>Флаг: идёт чтение gossip, пропустить hivemind polling</summary>
    public bool GossipReading { get; set; }
    public string MasterName { get; private set; } = "";

    /// <summary>Текущий режим слейва</summary>
    public SlaveMode Mode { get; set; } = SlaveMode.Idle;
    public bool ForceIdle { get; set; } // полный стоп — не бить даже в Idle
    /// <summary>Хилы не хилят (вайп)</summary>
    public bool WipeMode { get; set; } = false;

    // Слейв: последняя полученная команда
    public Command? LastCommand { get; private set; }
    public string LastCommandArg { get; private set; } = "";

    public event Action<string>? OnStatusChanged;
    public event Action<Command, string>? OnCommandReceived;
    /// <summary>Авто-включение UI: ("rotation",true), ("buffs",true)</summary>
    public event Action<string, bool>? OnAutoToggle;
    /// <summary>Мастер задал бафф: ("blessing","BoM"), ("aura","AuRet")</summary>
    public event Action<string, string>? OnBuffChanged;

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

        // Определяем получателей
        var selected = ConnectedSlaves.Where(s => s.Selected).ToList();
        bool isTargeted = selected.Count > 0;

        // Для общей команды — исключить IgnoreGlobal слейвов, адресовать остальным
        string targets;
        List<SlaveInfo> affected;
        if (isTargeted)
        {
            targets = string.Join(",", selected.Select(s => s.Name));
            affected = selected;
        }
        else
        {
            var nonIgnored = ConnectedSlaves.Where(s => !s.IgnoreGlobal).ToList();
            if (nonIgnored.Count > 0 && nonIgnored.Count < ConnectedSlaves.Count)
            {
                // Есть игнорирующие — адресуем только неигнорирующим
                targets = string.Join(",", nonIgnored.Select(s => s.Name));
            }
            else
            {
                targets = ""; // все получат (или игнорирующих нет)
            }
            affected = nonIgnored.Count > 0 ? nonIgnored : ConnectedSlaves;
        }

        string fullArg = string.IsNullOrEmpty(targets) ? arg : $"{arg}~{targets}";
        string msg = $"{cmd}:{fullArg}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);

        // Не обновлять ActiveCommand для служебных команд
        if (cmd != Command.RefreshGuid && cmd != Command.SetBuff && cmd != Command.Wipe &&
            cmd != Command.Interact && cmd != Command.GossipSelect && cmd != Command.GossipAccept)
        {
            foreach (var slave in affected)
                slave.ActiveCommand = cmd;
        }
        OnSlavesChanged?.Invoke();

        Logger.Info($"Hivemind: MASTER sent {cmd} {arg}");
        OnStatusChanged?.Invoke($"Sent: {cmd}");
    }

    /// <summary>Мастер: отправить команду конкретному слейву</summary>
    public void SendCommandToSlave(string slaveName, Command cmd)
    {
        if (CurrentRole != Role.Master) return;
        string masterName = _objectManager.GetPlayerName() ?? "master";
        string arg = cmd == Command.Stop ? "" : masterName;
        string fullArg = $"{arg}~{slaveName}";
        string msg = $"{cmd}:{fullArg}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);

        // Обновить ActiveCommand
        var slave = ConnectedSlaves.FirstOrDefault(s => s.Name == slaveName);
        if (slave != null) slave.ActiveCommand = cmd;
        OnSlavesChanged?.Invoke();

        Logger.Info($"Hivemind: MASTER sent {cmd} to {slaveName}");
    }

    /// <summary>Мастер: слейвы взаимодействуют с таргетом мастера</summary>
    public void CmdInteract()
    {
        if (CurrentRole != Role.Master) return;
        string masterName = _objectManager.GetPlayerName() ?? "master";
        // Слейвы ассистят мастера → получают его таргет → InteractUnit
        Logger.Info($"Interact: MASTER sending Interact, masterName={masterName}");
        SendCommand(Command.Interact, masterName);
        // Мастер тоже открывает диалог (чтобы читать gossip опции)
        _hook.ExecuteLua("InteractUnit('target')", 200);
        Logger.Info("Interact: MASTER InteractUnit('target') executed");
    }

    /// <summary>Мастер: слейвы выбирают пункт gossip</summary>
    public void CmdGossipSelect(int optionIndex)
    {
        Logger.Info($"Gossip: MASTER select option {optionIndex}");
        SendCommand(Command.GossipSelect, optionIndex.ToString());
    }

    /// <summary>Мастер: сам выбирает gossip опцию (чтобы видеть popup)</summary>
    public void MasterSelectGossip(int optionIndex)
    {
        Logger.Info($"Gossip: MASTER self-select option {optionIndex}");
        // Очищаем WB_GOSSIP перед выбором, чтобы не прочитать старые данные
        _hook.ExecuteLua($"WB_GOSSIP=nil SelectGossipOption({optionIndex})", 200);
    }

    /// <summary>Мастер: сам подтверждает popup</summary>
    public void MasterAcceptPopup()
    {
        Logger.Info("Gossip: MASTER self-accept popup");
        _hook.ExecuteLua("StaticPopup1Button1:Click()", 200);
    }

    /// <summary>Проверить открыт ли GossipFrame (лёгкая проверка, не трогает WB_GOSSIP)</summary>
    public bool IsGossipFrameVisible()
    {
        string? result = _hook.ExecuteLuaWithResult("WB_R=GossipFrame and GossipFrame:IsShown() and '1' or '0'", 0, 500);
        return result == "1";
    }

    /// <summary>Проверить открыт ли popup на мастере</summary>
    public bool IsPopupVisible()
    {
        string? result = _hook.ExecuteLuaWithResult("WB_R=StaticPopup1 and StaticPopup1:IsShown() and '1' or '0'", 0, 500);
        return result == "1";
    }

    /// <summary>Мастер: слейвы подтверждают popup</summary>
    public void CmdGossipAccept()
    {
        SendCommand(Command.GossipAccept);
    }

    /// <summary>Мастер: получить gossip опции с таргета мастера</summary>
    public List<string> GetMasterGossipOptions()
    {
        var options = new List<string>();
        // Приостановить hivemind polling чтобы не перезаписал WB_R
        GossipReading = true;
        try
        {
            System.Threading.Thread.Sleep(200); // дать текущему polling завершиться
            string? raw = _hook.ExecuteLuaWithResult(
                "local s='G:' for i=1,20 do local t=select(i*2-1,GetGossipOptions()) if not t then break end s=s..t..'|' end WB_R=s", 0, 1000);
            Logger.Info($"Gossip: raw=[{raw ?? "NULL"}] startsWith_G={raw?.StartsWith("G:")}]");
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("G:")) return options;
            raw = raw.Substring(2);
            if (string.IsNullOrEmpty(raw)) return options;
            foreach (var opt in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
                options.Add(opt);
        }
        finally
        {
            GossipReading = false;
        }
        return options;
    }

    /// <summary>Мастер: бейте мой таргет</summary>
    public void CmdAttack()
    {
        _followActive = false;
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.Attack, name);
    }

    /// <summary>Мастер: все ко мне</summary>
    private bool _followActive;

    public void CmdFollow()
    {
        // Toggle: снять follow, продолжить бить
        if (_followActive)
        {
            _followActive = false;
            SendCommand(Command.StopFollow);
            Logger.Info("CmdFollow: TOGGLE OFF → StopFollow");
            return;
        }
        _followActive = true;
        string name = _objectManager.GetPlayerName() ?? "master";
        Logger.Info($"CmdFollow: TOGGLE ON → Follow {name}");
        SendCommand(Command.Follow, name);
    }

    /// <summary>Мастер: стоп</summary>
    public void CmdStop() { _followActive = false; SendCommand(Command.Stop); }

    /// <summary>Мастер: авторежим (follow + auto-assist)</summary>
    public void CmdAuto()
    {
        _followActive = false;
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

    /// <summary>Мастер: задать бафф слейвам (blessing=BoM, aura=AuRet)</summary>
    /// <summary>Мастер: хилы стоп хилить (вайп)</summary>
    public void CmdWipe() => SendCommand(Command.Wipe);

    /// <summary>Мастер: слейвы обновляют GUID мастера</summary>
    public void CmdRefreshGuid()
    {
        string name = _objectManager.GetPlayerName() ?? "master";
        SendCommand(Command.RefreshGuid, name);
        Task.Run(async () =>
        {
            await Task.Delay(500);
            SendCommand(Command.RefreshGuid, name);
            await Task.Delay(500);
            SendCommand(Command.RefreshGuid, name);
        });
    }

    /// <summary>Мастер: слейвы берут таргет мастера как нового "мастера" для follow</summary>
    public void CmdGuidByTarget()
    {
        var target = _objectManager.GetTarget();
        if (target == null) { Logger.Info("Hivemind: CmdGuidByTarget — no target"); return; }
        string? targetName = target.Name;
        if (string.IsNullOrEmpty(targetName))
        {
            string? luaName = _hook.ExecuteLuaWithResult("WB_R=UnitName('target')");
            targetName = luaName;
        }
        if (string.IsNullOrEmpty(targetName)) return;
        // Отправляем имя таргета — слейв найдёт ближайшего по имени
        SendCommand(Command.RefreshGuid, targetName);
        Logger.Info($"Hivemind: MASTER GuidByTarget → {targetName}");
        Task.Run(async () =>
        {
            await Task.Delay(500);
            SendCommand(Command.RefreshGuid, targetName);
            await Task.Delay(500);
            SendCommand(Command.RefreshGuid, targetName);
        });
    }

    /// <summary>Мастер: задать бафф конкретному слейву</summary>
    public void CmdSetBuffToSlave(string slaveName, string buffType, string buffKey)
    {
        if (CurrentRole != Role.Master) return;
        string msg = $"SetBuff:{buffType}={buffKey}~{slaveName}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);
        Logger.Info($"Hivemind: MASTER SetBuff {buffType}={buffKey} → {slaveName}");
    }

    /// <summary>Получить имя таргета мастера</summary>
    public string? GetMasterTargetName()
    {
        var target = _objectManager.GetTarget();
        if (target == null) return null;
        string? name = target.Name;
        if (string.IsNullOrEmpty(name))
            name = _hook.ExecuteLuaWithResult("WB_R=UnitName('target')");
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>Мастер: конкретному слейву — обновить GUID на указанное имя</summary>
    public void CmdRefreshGuidToSlave(string slaveName, string targetName)
    {
        if (CurrentRole != Role.Master) return;
        string msg = $"RefreshGuid:{targetName}~{slaveName}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);
        Logger.Info($"Hivemind: MASTER RefreshGuid {slaveName} → {targetName}");
    }

    public void CmdSetBuff(string buffType, string buffKey)
    {
        // Шлём напрямую, без адресации — все палы получат
        if (CurrentRole != Role.Master) return;
        string msg = $"SetBuff:{buffType}={buffKey}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);
        Logger.Info($"Hivemind: MASTER SetBuff {buffType}={buffKey}");
    }

    /// <summary>Мастер: отправить слейвов в точку (Ctrl+ПКМ)</summary>
    public void CmdGoto(float x, float y, float z)
    {
        string coords = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1};{1:F1};{2:F1}", x, y, z);

        // Навигация: если выбраны конкретные слейвы — только им
        if (NavSelectedSlaves.Count > 0)
        {
            string targets = string.Join(",", NavSelectedSlaves);
            string fullArg = $"{coords}~{targets}";
            string msg2 = $"Goto:{fullArg}";
            string lua2 = $"SendAddonMessage('{CHANNEL}','{msg2}','PARTY')";
            _hook.ExecuteLua(lua2, 200);

            foreach (var name in NavSelectedSlaves)
            {
                var sl = ConnectedSlaves.FirstOrDefault(s => s.Name == name);
                if (sl != null) sl.ActiveCommand = Command.Goto;
            }

            Logger.Info($"Hivemind: MASTER Goto → [{targets}]");

            // Автоснятие: убрать незакреплённых, оставить закреплённых
            NavSelectedSlaves.RemoveWhere(n => !NavPinnedSlaves.Contains(n));
            OnNavChanged?.Invoke();
            OnSlavesChanged?.Invoke();
            return;
        }

        SendCommand(Command.Goto, coords);
    }

    // --- Навигация: выбор слейвов для Ctrl+ПКМ ---
    public HashSet<string> NavSelectedSlaves { get; } = new();
    public HashSet<string> NavPinnedSlaves { get; } = new(); // закреплённые — не снимаются после Goto
    public event Action? OnNavChanged;
    public void NotifyNavChanged() => OnNavChanged?.Invoke();

    // --- Мастер: Ctrl+ПКМ детекция (быстрый поток) ---
    private float _lastCtmX, _lastCtmY, _lastCtmZ;
    private Thread? _ctmWatchThread;
    private volatile bool _ctmWatchRunning;
    private volatile bool _gotoFired; // флаг для MasterTick — отправить команду
    private volatile bool _needStopMovement; // флаг — остановить движение через Lua
    private volatile int _suppressCtmTicks; // счётчик подавления CTM (каждый тик 5мс)

    private float _gotoX, _gotoY, _gotoZ;
    // Кэш позиции мастера (обновляется из основного тика, читается из быстрого потока)
    private volatile float _cachedPlayerX, _cachedPlayerY, _cachedPlayerZ, _cachedPlayerFacing;
    public void UpdateCachedPosition(float x, float y, float z, float facing)
    {
        _cachedPlayerX = x; _cachedPlayerY = y; _cachedPlayerZ = z; _cachedPlayerFacing = facing;
    }

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
                                // CTM перебивает CTM — "иди на своё место" (из кэша, thread-safe)
                                _ctm.MoveTo(_cachedPlayerX, _cachedPlayerY, _cachedPlayerZ, 0.5f);

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
            existing.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return; // уже зарегистрирован — не перерисовываем
        }

        ConnectedSlaves.Add(new SlaveInfo
        {
            Name = name,
            ClassName = className,
            LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        Logger.Info($"Hivemind: slave registered — {name} ({className})");
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

    /// <summary>Мастер: тогл игнорирования общих команд</summary>
    public void ToggleIgnoreGlobal(string name)
    {
        var slave = ConnectedSlaves.FirstOrDefault(s => s.Name == name);
        if (slave != null)
        {
            slave.IgnoreGlobal = !slave.IgnoreGlobal;
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
    if cmd == 'Register' then
        WB_HIVE_REG = arg or ''
        WB_HIVE_REG_SENDER = sender or ''
        WB_HIVE_REG_TIME = GetTime()
    else
        WB_HIVE_CMD = cmd or ''
        WB_HIVE_ARG = arg or ''
        WB_HIVE_SENDER = sender or ''
        WB_HIVE_TIME = GetTime()
    end
end)
WB_HIVE_REGISTERED = true
WB_HIVE_CMD = ''
WB_HIVE_TIME = 0
WB_HIVE_REG = ''
WB_HIVE_REG_TIME = 0
";

    public static string GetSlaveReadScript() =>
        "WB_R=(WB_HIVE_CMD or '')..'|'..(WB_HIVE_ARG or '')..'|'..(WB_HIVE_SENDER or '')..'|'..(WB_HIVE_TIME or '0')";

    public static string GetRegisterReadScript() =>
        "WB_R=(WB_HIVE_REG or '')..'|'..(WB_HIVE_REG_SENDER or '')..'|'..(WB_HIVE_REG_TIME or '0')";

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
            "SetBuff" => Command.SetBuff,
            "RefreshGuid" => Command.RefreshGuid,
            "Wipe" => Command.Wipe,
            "Register" => Command.Register,
            "StopFollow" => Command.StopFollow,
            "Interact" => Command.Interact,
            "GossipSelect" => Command.GossipSelect,
            "GossipAccept" => Command.GossipAccept,
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
        OnAutoToggle?.Invoke("rotation", false);
        OnAutoToggle?.Invoke("buffs", false);
    }

    public void ExecuteSlaveCommand(Command cmd, string arg)
    {
        // Wipe — хилы стоп
        if (cmd == Command.Wipe)
        {
            WipeMode = !WipeMode;
            Logger.Info($"Hivemind: SLAVE wipe mode = {WipeMode}");
            return;
        }

        // RefreshGuid — сброс GUID мастера, повторный поиск
        if (cmd == Command.RefreshGuid)
        {
            string cleanName = arg;
            // Проверяем адресацию: если есть ~список — только для указанных слейвов
            if (arg.Contains('~'))
            {
                var split = arg.Split('~', 2);
                cleanName = split[0];
                string targetList = split[1];
                string myName = _objectManager.GetPlayerName() ?? "";
                if (!string.IsNullOrEmpty(targetList) && !targetList.Split(',').Contains(myName))
                {
                    Logger.Info($"Hivemind: SLAVE skipping RefreshGuid — not in [{targetList}]");
                    return;
                }
            }
            if (_botEngine?.SlaveCtrl != null)
            {
                _botEngine.SlaveCtrl.ResetMasterGuid();
                _botEngine.SlaveCtrl.InitMasterGuid(cleanName);
                Logger.Info($"Hivemind: SLAVE refreshed master GUID for {cleanName}");
            }
            return;
        }

        // SetBuff — не отменяет текущую команду
        if (cmd == Command.SetBuff)
        {
            string cleanArg2 = arg.Contains('~') ? arg.Split('~', 2)[0] : arg;
            if (cleanArg2.Contains('='))
            {
                var bParts = cleanArg2.Split('=', 2);
                OnBuffChanged?.Invoke(bParts[0], bParts[1]);
                Logger.Info($"Hivemind: SLAVE SetBuff {bParts[0]}={bParts[1]}");
            }
            return;
        }

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
        ForceIdle = false; // новая команда — снять полный стоп

        // Один раз — найти GUID мастера (при первой команде)
        if (!string.IsNullOrEmpty(cleanArg) && cmd != Command.Stop && cmd != Command.Scatter)
            _botEngine?.SlaveCtrl.InitMasterGuid(cleanArg);

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
                if (_botEngine != null) _botEngine.HivemindFollowing = true;
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE follow master={cleanArg} HivemindFollowing=TRUE");
                break;

            case Command.Attack:
                // Бейте таргет — ассистим мастера, ротация, подбег к цели
                MasterName = cleanArg;
                Mode = SlaveMode.Attacking;
                if (_botEngine != null) _botEngine.HivemindFollowing = false;
                _hook.ExecuteLua($"AssistUnit('{cleanArg}') StartAttack()", 200);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE attack target of {cleanArg}");
                break;

            case Command.Auto:
                // Авторежим — follow + auto-assist в бою
                MasterName = cleanArg;
                Mode = SlaveMode.Auto;
                if (_botEngine != null) _botEngine.HivemindFollowing = true;
                _botEngine?.SlaveCtrl.CmdFollow(cleanArg);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE auto mode, master={cleanArg}");
                break;

            case Command.StopFollow:
                // Мягкий стоп — перестать бежать, но продолжить бить
                if (_botEngine != null) _botEngine.HivemindFollowing = false;
                _botEngine?.SlaveCtrl.CmdStop();
                Mode = SlaveMode.Idle; // Idle без ForceIdle = бьёт таргет
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info("Hivemind: SLAVE StopFollow — stop moving, keep fighting");
                break;

            case Command.Stop:
                // Полный стоп — всё выключить, сбросить таргет
                if (_botEngine != null)
                {
                    _botEngine.HivemindFollowing = false;
                    _botEngine.ForceStop();
                }
                _botEngine?.SlaveCtrl.CmdStop();
                _hook.ExecuteLua("ClearTarget()", 100);
                Logger.Info("Hivemind: SLAVE full stop + clear target");
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

            case Command.Interact:
                // Ассистим мастера (получаем его таргет) → InteractUnit
                if (!string.IsNullOrEmpty(cleanArg))
                {
                    string masterN = cleanArg.Replace("'", "\\'");
                    string interactLua = $"AssistUnit('{masterN}') InteractUnit('target')";
                    Logger.Info($"Interact: SLAVE executing [{interactLua}]");
                    _hook.ExecuteLua(interactLua, 300);
                    Logger.Info($"Interact: SLAVE done, master={cleanArg}");
                }
                else
                {
                    Logger.Warn("Interact: SLAVE — empty arg, skip");
                }
                break;

            case Command.GossipSelect:
                // Выбор пункта gossip диалога: arg = номер опции
                if (int.TryParse(cleanArg, out int gossipIdx))
                {
                    // Если gossip закрыт — сначала переоткрыть
                    Logger.Info($"Gossip: SLAVE selecting option {gossipIdx}, checking GossipFrame...");
                    _hook.ExecuteLua(
                        $"if not (GossipFrame and GossipFrame:IsShown()) then InteractUnit('target') end", 300);
                    System.Threading.Thread.Sleep(500);
                    _hook.ExecuteLua($"SelectGossipOption({gossipIdx})", 200);
                    Logger.Info($"Gossip: SLAVE option {gossipIdx} selected");
                }
                else
                {
                    Logger.Warn($"Gossip: SLAVE — invalid option index [{cleanArg}]");
                }
                break;

            case Command.GossipAccept:
                // Подтвердить popup — ждём пока появится (до 3с)
                Logger.Info("Gossip: SLAVE accepting popup, waiting for StaticPopup...");
                Task.Run(async () =>
                {
                    for (int wait = 0; wait < 6; wait++)
                    {
                        await Task.Delay(500);
                        _hook.ExecuteLua("if StaticPopup1 and StaticPopup1:IsShown() then StaticPopup1Button1:Click() WB_ACCEPTED=1 end", 200);
                    }
                    Logger.Info("Gossip: SLAVE popup accept attempts done");
                });
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
