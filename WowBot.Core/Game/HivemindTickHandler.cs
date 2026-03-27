namespace WowBot.Core.Game;

/// <summary>
/// Обрабатывает Hivemind-логику в тике BotEngine:
/// установка слушателя, чтение команд, register, Ctrl+CTM, авто-рес.
/// </summary>
public class HivemindTickHandler
{
    private readonly Hivemind _hivemind;
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;

    private int _hiveCheckTick;
    private int _registerTick;
    private double _lastRegisterTime;
    private uint _hiveMacroAddr;

    /// <summary>Последнее время обработанной hivemind-команды</summary>
    public double LastHiveCheck { get; set; }

    public HivemindTickHandler(Hivemind hivemind, EndSceneHook hook, ObjectManager objectManager)
    {
        _hivemind = hivemind;
        _hook = hook;
        _objectManager = objectManager;
    }

    /// <summary>Найти адрес макроса #2 в памяти WoW</summary>
    private uint FindHiveMacroAddr()
    {
        // Пишем маркер в макрос #2
        string marker = "WBHM_" + (System.Environment.TickCount % 100000);
        _hook.ExecuteLua($"EditMacro(2,'WH',1,'{marker}')", 300);
        System.Threading.Thread.Sleep(300);

        // Сканируем память
        byte[] needle = System.Text.Encoding.UTF8.GetBytes(marker);
        uint[][] regions = { new uint[] { 0x01000000, 0x20000000 }, new uint[] { 0x20000000, 0x30000000 } };
        foreach (var region in regions)
        {
            for (uint addr = region[0]; addr < region[1]; addr += 4096)
            {
                try
                {
                    byte[] block = _objectManager.Memory.ReadBytes(addr, 4096 + needle.Length);
                    for (int i = 0; i < 4096; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < needle.Length; j++)
                            if (block[i + j] != needle[j]) { match = false; break; }
                        if (match) return addr + (uint)i;
                    }
                }
                catch { }
            }
        }
        return 0;
    }

    /// <summary>
    /// Обновить кэш позиции мастера (вызывается из BotEngine.Tick).
    /// </summary>
    public void UpdateMasterPosition(Entities.WowPlayer player)
    {
        if (_hivemind.CurrentRole == Hivemind.Role.Master)
            _hivemind.UpdateCachedPosition(player.X, player.Y, player.Z, player.Facing);
    }

    /// <summary>
    /// Авто-принятие реса для слейвов. Возвращает true если игрок мёртв (нужен return в Tick).
    /// </summary>
    public bool HandleSlaveAutoRes(Entities.WowPlayer player)
    {
        if (_hivemind.CurrentRole == Hivemind.Role.Slave && player.IsDead)
        {
            _hook.ExecuteLua("AcceptResurrect()", 100);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Слушатель + чтение команд + register + Ctrl+CTM.
    /// Вызывается каждый тик из BotEngine.
    /// </summary>
    /// <param name="logTick">Текущий logTick (для условного логирования)</param>
    /// <param name="luaReader">LuaReader для поиска адреса макроса</param>
    /// <param name="playerClass">Класс персонажа (для Register)</param>
    public void Tick(int logTick, LuaReader? luaReader, string playerClass)
    {
        // === HIVEMIND: слушаем addon messages (и мастер, и слейв) ===
        if (_hivemind.CurrentRole == Hivemind.Role.Slave || _hivemind.CurrentRole == Hivemind.Role.Master)
        {
            // Устанавливаем слушатель (один раз)
            if (!_hivemind._slaveListenerInstalled)
            {
                _hook.ExecuteLua(Hivemind.GetSlaveListenerScript(), 500);
                _hivemind._slaveListenerInstalled = true;
                Logger.Info("Hivemind: slave listener installed");
                // Найти адрес макроса #2 для прямого чтения
                if (_hiveMacroAddr == 0 && luaReader != null && luaReader.IsInitialized)
                {
                    System.Threading.Thread.Sleep(300);
                    _hiveMacroAddr = FindHiveMacroAddr();
                    Logger.Info($"Hivemind: macro#2 addr=0x{_hiveMacroAddr:X8}");
                }
            }

            // Читаем команды через Lua C API (без макросов!)
            _hiveCheckTick++;
            if (_hiveCheckTick >= 3 && !_hivemind.GossipReading)
            {
                _hiveCheckTick = 0;

                // Команды мастера (для слейвов)
                string checkLua = Hivemind.GetSlaveReadScript();
                string? response = _hook.ExecuteLuaWithResult(checkLua);
                if (logTick == 0) Logger.Info($"Hivemind: raw response=[{response ?? "NULL"}]");
                if (response != null)
                {
                    var (cmd, arg, sender, time) = Hivemind.ParseSlaveResponse(response);
                    if (cmd != null && time > LastHiveCheck)
                    {
                        LastHiveCheck = time;
                        // Слейв: игнорировать команды от чужих мастеров
                        if (_hivemind.CurrentRole == Hivemind.Role.Slave &&
                            !string.IsNullOrEmpty(_hivemind.MasterName) &&
                            !string.IsNullOrEmpty(sender) &&
                            sender != _hivemind.MasterName)
                        {
                            if (logTick == 0) Logger.Info($"Hivemind: IGNORED {cmd} from {sender} (my master={_hivemind.MasterName})");
                            // не выполняем
                        }
                        else
                        {
                            Logger.Info($"Hivemind: received {cmd} from {sender} arg={arg} time={time}");
                            _hivemind.ExecuteSlaveCommand(cmd.Value, arg);
                        }
                    }
                }

                // Register от слейвов (для мастера) — отдельные Lua переменные
                if (_hivemind.CurrentRole == Hivemind.Role.Master)
                {
                    string regLua = Hivemind.GetRegisterReadScript();
                    string? regResponse = _hook.ExecuteLuaWithResult(regLua);
                    if (regResponse != null)
                    {
                        var regParts = regResponse.Split('|');
                        if (regParts.Length >= 3 && !string.IsNullOrEmpty(regParts[0]))
                        {
                            double.TryParse(regParts[2], System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double regTime);
                            if (regTime > _lastRegisterTime)
                            {
                                _lastRegisterTime = regTime;
                                _hivemind.ExecuteSlaveCommand(Hivemind.Command.Register, regParts[0]);
                            }
                        }
                    }
                }
            }
        }

        // === HIVEMIND SLAVE: периодический Register (каждые ~10 сек) ===
        if (_hivemind.CurrentRole == Hivemind.Role.Slave)
        {
            _registerTick++;
            if (_registerTick >= 66) // 66 * 150мс ≈ 10 сек
            {
                _registerTick = 0;
                _hivemind.SendRegister(playerClass);
            }
        }

        // === HIVEMIND MASTER: Ctrl+ПКМ → направить слейвов ===
        if (_hivemind.CurrentRole == Hivemind.Role.Master)
        {
            _hivemind.MasterTick();
        }
    }
}
