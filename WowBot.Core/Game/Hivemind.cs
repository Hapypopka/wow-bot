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
    public enum Command { Follow, Attack, Stop, Scatter, Stack, Focus, Loot, Ping, ToggleAssist }

    private Role _currentRole = Role.None;
    public Role CurrentRole
    {
        get => _currentRole;
        set
        {
            _currentRole = value;
            // Сброс состояния при смене роли
            _followMaster = false;
            _followAttack = false;
            MasterName = "";
            LastCommand = null;
            LastCommandArg = "";
        }
    }
    public bool IsActive => CurrentRole != Role.None;
    public string MasterName { get; private set; } = "";

    // Слейв: состояние follow/attack
    private bool _followMaster;
    private bool _followAttack;
    private bool _wantRotation;
    public bool IsFollowing => _followMaster;
    public bool WantRotation => _wantRotation;

    // Слейв: последняя полученная команда
    public Command? LastCommand { get; private set; }
    public string LastCommandArg { get; private set; } = "";

    public event Action<string>? OnStatusChanged;
    public event Action<Command, string>? OnCommandReceived;
    /// <summary>Авто-включение UI: ("rotation",true), ("buffs",true), ("follow",true)</summary>
    public event Action<string, bool>? OnAutoToggle;

    public Hivemind(EndSceneHook hook, ObjectManager objectManager, Navigation navigation, ClickToMove ctm)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
        _ctm = ctm;
    }

    // ==================== МАСТЕР ====================

    /// <summary>Отправить команду всем слейвам</summary>
    public void SendCommand(Command cmd, string arg = "")
    {
        if (CurrentRole != Role.Master) return;

        string msg = $"{cmd}:{arg}";
        string lua = $"SendAddonMessage('{CHANNEL}','{msg}','PARTY')";
        _hook.ExecuteLua(lua, 200);

        Logger.Info($"Hivemind: MASTER sent {cmd} {arg}");
        OnStatusChanged?.Invoke($"Sent: {cmd}");
    }

    /// <summary>Мастер: бейте мой таргет</summary>
    public void CmdAttack()
    {
        var player = _objectManager.LocalPlayer;
        if (player == null) return;
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

    /// <summary>Мастер: переключить AlwaysAssist у слейвов</summary>
    public void CmdToggleAssist() => SendCommand(Command.ToggleAssist);

    // ==================== СЛЕЙВ: ПОСТОЯННЫЙ FOLLOW ====================

    /// <summary>Сбросить Lua глобалы при включении слейва</summary>
    public void ResetSlaveState()
    {
        _followMaster = false;
        _followAttack = false;
        _wantRotation = false;
        // Сбрасываем Lua глобалы И listener
        _hook.ExecuteLua("WB_HIVE_CMD='' WB_HIVE_ARG='' WB_HIVE_TIME=0 WB_HIVE_REGISTERED=nil", 200);
        _slaveListenerInstalled = false;
        if (_botEngine != null) _botEngine.LastHiveCheck = 0; // Сброс
        Logger.Info("Hivemind: slave state reset");
    }

    // Флаг для BotEngine
    internal bool _slaveListenerInstalled;

    private int _retargetTick;
    private bool _hasTarget; // Слейв уже взял таргет от мастера

    /// <summary>Автосвич: если таргет умер — берёт новый от мастера</summary>
    public bool AutoSwitch { get; set; } = true;

    /// <summary>Всегда ассистить мастера (ретаргет каждые 2 сек). По дефолту OFF.</summary>
    public bool AlwaysAssist { get; set; } = false;

    /// <summary>
    /// Вызывать каждый тик (150мс) из BotEngine.
    /// Attack — проверяем жив ли таргет, автосвич если умер.
    /// </summary>
    public void SlaveTickFollow()
    {
        if (!_followMaster || string.IsNullOrEmpty(MasterName)) return;

        // Follow без атаки — полностью через штатный BotEngine follow (SetFollowGuid)
        if (!_followAttack) return;

        // Проверяем таргет каждые ~1 сек
        _retargetTick++;
        if (_retargetTick < 7) return;
        _retargetTick = 0;

        // AlwaysAssist — всегда бьём таргет мастера + подбег
        if (AlwaysAssist)
        {
            _hook.ExecuteLua($"AssistUnit('{MasterName}') StartAttack()", 200);
            _hasTarget = true;
            return;
        }

        // Если у слейва уже есть живой таргет — не трогаем
        if (_hasTarget)
        {
            // Проверяем жив ли наш таргет через Lua
            _hook.ExecuteLua(
                "if not UnitExists('target') or UnitIsDead('target') then WB_TARGET_DEAD=1 else WB_TARGET_DEAD=0 end", 100);
            // Читаем результат на следующем тике (упрощённо: проверяем через ObjectManager)
            var target = _objectManager.GetTarget();
            if (target == null || !target.IsAlive)
            {
                _hasTarget = false;
                if (AutoSwitch)
                {
                    // Таргет умер — берём новый от мастера
                    _hook.ExecuteLua($"AssistUnit('{MasterName}') StartAttack()", 200);
                    _hasTarget = true;
                    Logger.Info("Hivemind: SLAVE auto-switch target (old died)");
                }
            }
            return;
        }

        // Нет таргета — берём от мастера
        _hook.ExecuteLua($"AssistUnit('{MasterName}') StartAttack()", 200);
        _hasTarget = true;
    }

    private ulong _masterGuid;

    private Entities.WowUnit? FindPlayerByName(string name)
    {
        // Если уже знаем GUID мастера — ищем напрямую
        if (_masterGuid != 0)
        {
            var cached = _objectManager.GetUnitByGuid(_masterGuid);
            if (cached != null) return cached;
            _masterGuid = 0;
        }

        var player = _objectManager.LocalPlayer;
        if (player == null) return null;

        // Ищем ближайшего игрока (не себя) — в пати это мастер
        // Name для игроков ненадёжен (WoW хранит имена игроков отдельно от NPC)
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

    // ==================== СЛЕЙВ ====================

    /// <summary>
    /// Lua-скрипт для слейва: регистрирует слушатель аддон-канала.
    /// Сохраняет последнюю команду в глобальную WB_HIVE_CMD/WB_HIVE_ARG.
    /// </summary>
    public static string GetSlaveListenerScript() => $@"
if WB_HIVE_FRAME then WB_HIVE_FRAME:UnregisterAllEvents() WB_HIVE_FRAME:SetScript('OnEvent',nil) end
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
end)
WB_HIVE_REGISTERED = true
WB_HIVE_CMD = ''
WB_HIVE_ARG = ''
WB_HIVE_SENDER = ''
WB_HIVE_TIME = 0
";

    /// <summary>
    /// Lua-скрипт для чтения последней команды слейвом.
    /// Возвращает CMD|ARG|SENDER|TIME через макрос.
    /// </summary>
    public static string GetSlaveReadScript() =>
        "EditMacro(1,'WB',1,(WB_HIVE_CMD or '')..'|'..(WB_HIVE_ARG or '')..'|'..(WB_HIVE_SENDER or '')..'|'..(WB_HIVE_TIME or '0'))";

    /// <summary>
    /// Парсит ответ от слейва: "CMD|ARG|SENDER|TIME"
    /// </summary>
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
            "Scatter" => Command.Scatter,
            "Stack" => Command.Stack,
            "Focus" => Command.Focus,
            "Loot" => Command.Loot,
            "Ping" => Command.Ping,
            "ToggleAssist" => Command.ToggleAssist,
            _ => null
        };

        double.TryParse(parts[3], System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double time);

        return (cmd, parts[1], parts[2], time);
    }

    /// <summary>
    /// Выполнить команду слейвом. Вызывается из BotEngine tick.
    /// </summary>
    public void ExecuteSlaveCommand(Command cmd, string arg)
    {
        LastCommand = cmd;
        LastCommandArg = arg;

        switch (cmd)
        {
            case Command.Follow:
            case Command.Stack:
                // Ко мне → SlaveController.Following
                _botEngine?.SlaveCtrl.CmdFollow(arg);
                MasterName = arg;
                _followMaster = true;
                _followAttack = false;
                _wantRotation = false;
                OnAutoToggle?.Invoke("follow", true);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE follow master={arg}");
                break;

            case Command.Attack:
                // Бейте таргет: ассистим мастера + ротация
                _botEngine?.SlaveCtrl.CmdStop(); // Сброс follow
                _botEngine?.StopFollow();
                MasterName = arg;
                _followMaster = true;
                _followAttack = true;
                _wantRotation = true;
                _hasTarget = false;
                _hook.ExecuteLua($"AssistUnit('{arg}') StartAttack()", 200);
                _hasTarget = true;
                // Авто-включение ротации + баффов в UI
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                OnAutoToggle?.Invoke("follow", false);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE attack target of {arg}");
                break;

            case Command.Stop:
                _followMaster = false;
                _followAttack = false;
                _wantRotation = false;
                _botEngine?.SlaveCtrl.CmdStop();
                OnCommandReceived?.Invoke(cmd, "");
                Logger.Info("Hivemind: SLAVE stop");
                break;

            case Command.Scatter:
                // Отбежать — отменяем follow, бежим назад 1.5 сек
                _followMaster = false;
                _followAttack = false;
                _wantRotation = false;
                _hook.ExecuteLua("MoveForwardStart() MoveForwardStop() MoveBackwardStart()", 200);
                Task.Run(async () => { await Task.Delay(1500); _hook.ExecuteLua("MoveBackwardStop()", 100); });
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE scatter {arg}m");
                break;

            case Command.Ping:
                // Ответить мастеру что жив
                string myName = _objectManager.GetPlayerName() ?? "slave";
                _hook.ExecuteLua($"SendAddonMessage('{CHANNEL}','Pong:{myName}','PARTY')", 200);
                Logger.Info("Hivemind: SLAVE pong");
                break;

            case Command.ToggleAssist:
                AlwaysAssist = !AlwaysAssist;
                OnCommandReceived?.Invoke(cmd, AlwaysAssist ? "on" : "off");
                Logger.Info($"Hivemind: SLAVE AlwaysAssist = {AlwaysAssist}");
                break;

            default:
                OnCommandReceived?.Invoke(cmd, arg);
                break;
        }

        OnStatusChanged?.Invoke($"Cmd: {cmd}");
    }
}
