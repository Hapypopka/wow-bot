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

    private const string CHANNEL = "WBHIVE";

    public enum Role { None, Master, Slave }
    public enum Command { Follow, Attack, Stop, Scatter, Stack, Focus, Loot, Ping }

    public Role CurrentRole { get; set; } = Role.None;
    public bool IsActive => CurrentRole != Role.None;
    public string MasterName { get; private set; } = "";

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
                // Ассистим мастера (берём его таргет) + follow
                _hook.ExecuteLua($"AssistUnit('{arg}')", 200);
                MasterName = arg;
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE follow {arg}");
                break;

            case Command.Attack:
                // Берём таргет мастера и атакуем
                _hook.ExecuteLua($"AssistUnit('{arg}')", 200);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE attack target of {arg}");
                break;

            case Command.Stop:
                _hook.ExecuteLua("SpellStopCasting() PetPassiveMode()", 200);
                _ctm.Stop();
                OnCommandReceived?.Invoke(cmd, "");
                Logger.Info("Hivemind: SLAVE stop");
                break;

            case Command.Stack:
                // Бежим к мастеру
                _hook.ExecuteLua($"FollowUnit('{arg}')", 200);
                OnCommandReceived?.Invoke(cmd, arg);
                Logger.Info($"Hivemind: SLAVE stack on {arg}");
                break;

            case Command.Scatter:
                // Отбежать — рандомное направление
                var player = _objectManager.LocalPlayer;
                if (player != null)
                {
                    float dist = 10f;
                    float.TryParse(arg, out dist);
                    var rng = new Random();
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    float x = player.X + dist * (float)Math.Cos(angle);
                    float y = player.Y + dist * (float)Math.Sin(angle);
                    _ctm.MoveTo(x, y, player.Z, 1f);
                }
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
