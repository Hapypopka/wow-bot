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
    public enum Command { Follow, Attack, Stop, Auto, Scatter, Stack, Ping }

    /// <summary>Режим слейва — каждая команда отменяет предыдущую</summary>
    public enum SlaveMode { Idle, Following, Attacking, Auto }

    private Role _currentRole = Role.None;
    public Role CurrentRole
    {
        get => _currentRole;
        set
        {
            _currentRole = value;
            Mode = SlaveMode.Idle;
            MasterName = "";
            LastCommand = null;
            LastCommandArg = "";
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
        LastCommand = cmd;
        LastCommandArg = arg;

        // Каждая команда отменяет предыдущую
        ResetMode();

        switch (cmd)
        {
            case Command.Follow:
            case Command.Stack:
                // Ко мне — только следовать, не бить
                MasterName = arg;
                Mode = SlaveMode.Following;
                _botEngine?.SlaveCtrl.CmdFollow(arg);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE follow master={arg}");
                break;

            case Command.Attack:
                // Бейте таргет — ассистим мастера, ротация, подбег к цели
                MasterName = arg;
                Mode = SlaveMode.Attacking;
                _hook.ExecuteLua($"AssistUnit('{arg}') StartAttack()", 200);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE attack target of {arg}");
                break;

            case Command.Auto:
                // Авторежим — follow + auto-assist в бою
                MasterName = arg;
                Mode = SlaveMode.Auto;
                _botEngine?.SlaveCtrl.CmdFollow(arg);
                OnAutoToggle?.Invoke("rotation", true);
                OnAutoToggle?.Invoke("buffs", true);
                Logger.Info($"Hivemind: SLAVE auto mode, master={arg}");
                break;

            case Command.Stop:
                // Стоп — всё отменено в ResetMode()
                Logger.Info("Hivemind: SLAVE stop");
                break;

            case Command.Scatter:
                _hook.ExecuteLua("MoveForwardStart() MoveForwardStop() MoveBackwardStart()", 200);
                Task.Run(async () => { await Task.Delay(1500); _hook.ExecuteLua("MoveBackwardStop()", 100); });
                Logger.Info($"Hivemind: SLAVE scatter {arg}m");
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
