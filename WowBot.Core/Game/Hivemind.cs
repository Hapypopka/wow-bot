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
    public enum Command { Follow, Attack, Stop, Scatter, Stack, Focus, Loot, Ping }

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

    /// <summary>Мастер: все ко мне (follow)</summary>
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

    /// <summary>
    /// Вызывать каждый тик (150мс) из BotEngine.
    /// Follow — CTM к мастеру (как "Следование").
    /// Attack — ретаргетим таргет мастера каждые 2 сек.
    /// </summary>
    public void SlaveTickFollow()
    {
        if (!_followMaster || string.IsNullOrEmpty(MasterName)) return;

        // Follow без атаки — полностью через штатный BotEngine follow (SetFollowGuid)
        // SlaveTickFollow нужен ТОЛЬКО для режима атаки (ретаргет)
        if (!_followAttack) return;

        // Режим атаки: ретаргетим таргет мастера каждые 2 сек
        _retargetTick++;
        if (_retargetTick >= 13)
        {
            _retargetTick = 0;
            _hook.ExecuteLua($"AssistUnit('{MasterName}')", 200);
        }
    }

    private Entities.WowUnit? FindPlayerByName(string name)
    {
        // Ищем по GUID пати-мемберов через Lua — ненадёжно
        // Лучше: ищем ближайшего игрока (не себя) — в пати из 2 это мастер
        // TODO: когда будет больше 2 игроков — искать по имени через память
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
        return closest;
    }

    // ==================== СЛЕЙВ ====================

    /// <summary>
    /// Lua-скрипт для слейва: регистрирует слушатель аддон-канала.
    /// Сохраняет последнюю команду в глобальную WB_HIVE_CMD/WB_HIVE_ARG.
    /// </summary>
    public static string GetSlaveListenerScript() => $@"
if not WB_HIVE_REGISTERED then
    local f = CreateFrame('Frame')
    f:RegisterEvent('CHAT_MSG_ADDON')
    f:SetScript('OnEvent', function(self, event, prefix, msg, channel, sender)
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
end
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
                // Включаем штатный follow BotEngine (CTM, работает на любой дистанции)
                MasterName = arg;
                _followMaster = true;
                _followAttack = false;
                _wantRotation = false;
                // Находим мастера и ставим как follow target
                var masterUnit = FindPlayerByName(arg);
                if (masterUnit != null && _botEngine != null)
                    _botEngine.SetFollowGuid(masterUnit.Guid);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE follow {arg} via BotEngine");
                break;

            case Command.Attack:
                // Берём таргет мастера + включаем авто-атаку
                MasterName = arg;
                _followMaster = true;
                _followAttack = true;
                _wantRotation = true;
                _hook.ExecuteLua($"AssistUnit('{arg}')", 200);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE attack target of {arg}");
                break;

            case Command.Stop:
                _followMaster = false;
                _followAttack = false;
                _wantRotation = false;
                _botEngine?.StopFollow();
                // Записываем свою позицию как CTM цель с большим precision — персонаж "уже на месте" и встаёт
                var me = _objectManager.LocalPlayer;
                if (me != null)
                    _ctm.StopAt(me.X, me.Y, me.Z);
                // Повторный стоп через 150мс и 300мс — на случай если тик перезаписал
                Task.Run(async () => {
                    await Task.Delay(150);
                    var me2 = _objectManager.LocalPlayer;
                    if (me2 != null)
                        _ctm.StopAt(me2.X, me2.Y, me2.Z);
                    await Task.Delay(150);
                    var me3 = _objectManager.LocalPlayer;
                    if (me3 != null)
                        _ctm.StopAt(me3.X, me3.Y, me3.Z);
                });
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

            default:
                OnCommandReceived?.Invoke(cmd, arg);
                break;
        }

        OnStatusChanged?.Invoke($"Cmd: {cmd}");
    }
}
