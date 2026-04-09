using System.Linq;
using WowBot.Core.Game.Entities;

namespace WowBot.Core.Game;

public class BotEngine : IDisposable
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly Navigation _navigation;
    private readonly ClickToMove _ctm;
    private Timer? _timer;

    private string _instantScript = "";
    private string _fullScript = "";
    private string _fullScriptNoCombatCheck = "";

    private bool _followEnabled;
    private volatile bool _rotationEnabled;
    private bool _autoFace = true;

    /// <summary>Hivemind Follow — SlaveController рулит движением, BotEngine только кастует</summary>
    public bool HivemindFollowing { get; set; }
    private bool _autoSelectTarget = true;
    private float _maxTargetRange = 30f;
    private ulong _followGuid;
    private float _followDistance = 8f;

    // AoE settings
    private bool _aoeEnabled;
    private bool _useMultiDot = true;
    private int _maxDotTargets = 4;
    private bool _useMindSear = true;
    private int _mindSearTargets = 4;

    public bool FollowEnabled => _followEnabled;
    public bool AutoFace { get => _autoFace; set => _autoFace = value; }
    public bool AutoSelectTarget { get => _autoSelectTarget; set => _autoSelectTarget = value; }
    public float MaxTargetRange { get => _maxTargetRange; set => _maxTargetRange = value; }
    public bool RotationEnabled => _rotationEnabled;
    public ulong FollowGuid => _followGuid;
    public float FollowDistance
    {
        get => _followDistance;
        set => _followDistance = Math.Clamp(value, 0f, 20f);
    }

    // AoE properties
    public bool AoeEnabled { get => _aoeEnabled; set => _aoeEnabled = value; }
    public int AoeMinEnemies { get; set; } = 3; // глобальный порог: от скольки врагов AoE
    public bool UseMultiDot { get => _useMultiDot; set => _useMultiDot = value; }
    public int MaxDotTargets { get => _maxDotTargets; set => _maxDotTargets = value; }
    public bool UseMindSear { get => _useMindSear; set => _useMindSear = value; }
    public int MindSearTargets { get => _mindSearTargets; set => _mindSearTargets = value; }

    // Buff settings
    private bool _buffsEnabled;
    private List<string> _enabledBuffs = new();
    private int _buffCheckTick;

    public bool BuffsEnabled
    {
        get => _buffsEnabled;
        set
        {
            _buffsEnabled = value;
            if (value) EnsureRunning();
            else if (!_followEnabled && !_rotationEnabled && !Hivemind.IsActive) StopTimer();
        }
    }
    public List<string> EnabledBuffs { get => _enabledBuffs; set => _enabledBuffs = value; }
    public string SpellFlagsLua { get; set; } = "";

    // CountNearbyEnemies, CountNearbyCombatEnemies → делегирование в CombatHelper
    private int CountNearbyEnemies(WowPlayer player, float range = 30f) => _combatHelper.CountNearbyEnemies(player, range);
    private int CountNearbyCombatEnemies(WowPlayer player, float range = 10f) => _combatHelper.CountNearbyCombatEnemies(player, range);

    // TryGroundAoE, CountEnemiesNearTarget → делегирование в CombatHelper
    private bool TryGroundAoE(Entities.WowPlayer player, Entities.WowUnit target) =>
        _combatHelper.TryGroundAoE(player, target, _aoeEnabled, AoeMinEnemies, PlayerClass, _specName, SpellFlagsLua);

    private bool IsSpellEnabled(string key) => SpellFlagsLua?.Contains($"{key}=true") == true;

    // TrySmartTaunt → делегирование в CombatHelper
    private bool TrySmartTaunt(Entities.WowPlayer player) =>
        _combatHelper.TrySmartTaunt(player, PlayerClass, SpellFlagsLua);

    // AoE Avoidance → делегирование в CombatHelper
    public bool IsAoeFleeing => _combatHelper.IsAoeFleeing;

    private bool TryAoEAvoidance(Entities.WowPlayer player) => _combatHelper.TryAoEAvoidance(player);

    public string SelectedSeal { get; set; } = "";
    public string SelectedBlessing { get; set; } = "BoM";
    public string SelectedAura { get; set; } = "AuRet";
    public string SelectedShout { get; set; } = "";
    public string SelectedStance { get; set; } = "";
    public string SelectedPresence { get; set; } = "";
    public string SelectedFeralForm { get; set; } = "";
    public string SelectedPet { get; set; } = "";
    public bool AoeSealSwap { get; set; } = false;
    public bool AutoPveEnabled { get; set; } = false;
    public BossTactics BossTactics { get; private set; }
    public BossEngine BossEngine { get; private set; }
    private int _autoPveTick;
    public string SelectedTotemEarth { get; set; } = "";
    public string SelectedTotemFire { get; set; } = "";
    public string SelectedTotemWater { get; set; } = "";
    public string SelectedTotemAir { get; set; } = "";
    public string SelectedWeaponMH { get; set; } = ""; // Shaman MH: "WF", "FT", "EL"
    public string SelectedWeaponOH { get; set; } = ""; // Shaman OH: "WF", "FT", "EL"
    public bool IsHealer { get; set; }
    public System.Diagnostics.Process? WowProcess { get; set; }
    public string PlayerClass { get; set; } = "";
    private bool _isMovingForward;
    private int _healerTickCount;
    private int _registerTick;
    private int _blessingCooldown; // пропустить N баф-чеков после каста благословения
    private double _lastRegisterTime;
    private double _lastAckTime;
    private uint _hiveMacroAddr;
    private int _hiveAddrRetry;

    /// <summary>Найти адрес макроса #2 в памяти WoW</summary>
    private uint FindHiveMacroAddr()
    {
        // Пишем маркер в макрос #2
        string marker = "WBHM_" + (Environment.TickCount % 100000);
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
    private string? _specName;
    public string? SpecName { get => _specName; set => _specName = value; }

    // Mana thresholds (из оверлея, в процентах 0-100)
    public int DispManaThreshold { get; set; } = 15;
    public int SFManaThreshold { get; set; } = 50;

    public event Action<string>? OnStatusChanged;

    // Hivemind (мультибоксинг)
    public Hivemind Hivemind { get; private set; }
    public SlaveController SlaveCtrl { get; private set; }
    public double LastHiveCheck { get; set; }
    private Hivemind.Command? _lastHiveCmd;
    // _slaveListenerInstalled хранится в Hivemind

    public WowBot.Core.Navigation.NavEngine? NavEngine { get; private set; }

    public BotEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation, ClickToMove ctm)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
        _ctm = ctm;
        Hivemind = new Hivemind(hook, objectManager, navigation, ctm);
        Hivemind.SetBotEngine(this);
        SlaveCtrl = new SlaveController(navigation, hook, objectManager, ctm);
        BossTactics = new BossTactics(hook, objectManager, ctm, navigation);
        BossEngine = new BossEngine(hook, objectManager, ctm, navigation);
        _combatPositioning = new CombatPositioning(ctm);
        _buffManager = new BuffManager();
        _combatHelper = new CombatHelper(objectManager, hook, ctm);
        _combatExecutor = new CombatExecutor(hook, navigation, _combatPositioning, _combatHelper);
        _multiDotHelper = new MultiDotHelper(objectManager);

        // Навигация через навмеш (опционально)
        NavEngine = new WowBot.Core.Navigation.NavEngine(ctm, objectManager);
        SlaveCtrl.NavEngine = NavEngine;
    }

    /// <summary>Подключиться к NavServer (вызывать из UI)</summary>
    public bool ConnectNavServer(string ip = "127.0.0.1", int port = 47110)
    {
        return NavEngine?.Connect(ip, port) ?? false;
    }

    private readonly CombatPositioning _combatPositioning;
    private readonly BuffManager _buffManager;
    private readonly CombatHelper _combatHelper;
    private readonly CombatExecutor _combatExecutor;
    private readonly MultiDotHelper _multiDotHelper;

    public bool MoveBehindEnabled { get; set; }

    public bool AoeAvoidEnabled { get; set; } = true;

    public bool IsTankSpec => _specName is "Prot Warrior" or "Prot Paladin" or "Blood DK";
    // Feral Druid: танк только в Bear Form — определяется через IsTankUnit (бафф Bear Form)

    public bool IsMeleeSpec => PlayerClass is "WARRIOR" or "ROGUE" or "DEATHKNIGHT" ||
        (PlayerClass == "PALADIN" && _specName != "Holy Paladin") ||
        (PlayerClass == "DRUID" && _specName?.Contains("Feral") == true) ||
        (PlayerClass == "SHAMAN" && _specName?.Contains("Enhancement") == true);

    // Антиафк — пишем TickCount + снимаем AFK через Lua каждые ~30с
    private int _afkTickCounter;
    private void AntiAfkTick()
    {
        _afkTickCounter++;
        if (_afkTickCounter < 200) return; // ~30с (200 * 150мс)
        _afkTickCounter = 0;
        try
        {
            // 1. Пишем в память — предотвращаем будущий AFK
            uint tickCount = (uint)Environment.TickCount;
            _objectManager.Memory.WriteUInt32(0x00B499A4, tickCount);
            // 2. Снимаем текущий AFK через Lua (если уже стоит)
            if (_hook.IsHooked)
                _hook.ExecuteLua("if UnitIsAFK('player') then SendChatMessage('','AFK') end", 100);
            Logger.Info($"AntiAFK: wrote {tickCount} + lua clear");
        }
        catch { }
    }

    public LuaReader? LuaReader { get; set; }

    public void LoadRotation(string instantScript, string fullScript)
    {
        _instantScript = instantScript;
        _fullScript = fullScript;
        // Версия без проверки комбата — для слейва Hivemind
        _fullScriptNoCombatCheck = fullScript
            .Replace("not UnitAffectingCombat('player') and not UnitAffectingCombat('target')", "false")
            .Replace("not UnitAffectingCombat('target')", "false")
            .Replace("not UnitAffectingCombat('player')", "false");
    }

    /// <summary>Перечитать скрипты с диска (hot-reload без перезапуска)</summary>
    public void ReloadScripts()
    {
        if (string.IsNullOrEmpty(PlayerClass)) return;
        var fullScript = Rotations.AllRotations.GetFullScript(PlayerClass);
        var instantScript = Rotations.AllRotations.GetInstantScript(PlayerClass);
        LoadRotation(instantScript, fullScript);
        Logger.Info($"Scripts reloaded for {PlayerClass}");
    }

    // --- Follow target ---

    public void SetFollowTarget()
    {
        _objectManager.Update();
        var target = _objectManager.GetTarget();
        if (target != null)
        {
            _followGuid = target.Guid;
            OnStatusChanged?.Invoke("Follow target set");
        }
    }

    public void ClearFollowTarget()
    {
        _followGuid = 0;
        OnStatusChanged?.Invoke("Follow target cleared");
    }

    /// <summary>Установить follow по GUID напрямую (для Hivemind)</summary>
    public void SetFollowGuid(ulong guid)
    {
        _followGuid = guid;
        _followEnabled = true;
        Logger.Info($"SetFollowGuid: GUID=0x{guid:X} followEnabled={_followEnabled}");
        EnsureRunning();
    }

    /// <summary>Выключить follow (для Hivemind stop)</summary>
    public void StopFollow()
    {
        _followEnabled = false;
        _followGuid = 0;
        StopFollowMovement();
    }

    /// <summary>Полный стоп — выключить ротацию, follow, бафы напрямую</summary>
    public void ForceStop()
    {
        _rotationEnabled = false;
        _followEnabled = false;
        _buffsEnabled = false;
        _followGuid = 0;
        HivemindFollowing = false;
        Hivemind.Mode = Hivemind.SlaveMode.Idle;
        Hivemind.ForceIdle = true; // не бить даже в Idle
        Logger.Info("BotEngine: ForceStop — all disabled");
    }

    /// <summary>
    /// CTM follow — считает точку на земле в stopDistance от цели, бежит туда.
    /// Не спамит CTM если цель стоит и CTM ещё работает.
    /// </summary>
    private float _lastCtmX, _lastCtmY;
    private bool _isFollowMoving;
    private void FollowViaCTM(WowUnit player, WowUnit target, float stopDistance)
    {
        float dist = player.DistanceTo(target);

        if (dist <= stopDistance)
        {
            if (_isFollowMoving) { _ctm.Stop(); _isFollowMoving = false; }
            if (_isMovingForward) { _hook.ExecuteLua("MoveForwardStop()", 50); _isMovingForward = false; }
            return;
        }

        // Не спамим если цель стоит и CTM ещё бежит
        float dx = target.X - _lastCtmX;
        float dy = target.Y - _lastCtmY;
        float targetMoved = MathF.Sqrt(dx * dx + dy * dy);
        bool ctmIdle = _ctm.GetCurrentAction() == 0;

        if (_isFollowMoving && targetMoved < 3f && !ctmIdle)
            return;

        // Точка на земле в stopDistance от цели, в сторону игрока
        float dirX = player.X - target.X;
        float dirY = player.Y - target.Y;
        float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (dirLen < 0.1f) { dirX = 1f; dirY = 0f; dirLen = 1f; } // на случай если стоим на цели
        dirX /= dirLen;
        dirY /= dirLen;
        float goalX = target.X + dirX * stopDistance;
        float goalY = target.Y + dirY * stopDistance;

        Logger.Log(LogCat.Follow, $"FollowCTM: dist={dist:F1} stop={stopDistance:F1} player=({player.X:F1},{player.Y:F1}) target=({target.X:F1},{target.Y:F1}) goal=({goalX:F1},{goalY:F1}) ctmIdle={ctmIdle} tgtMoved={targetMoved:F1}");
        _ctm.MoveTo(goalX, goalY, target.Z, 0.5f);
        _lastCtmX = target.X;
        _lastCtmY = target.Y;
        _isFollowMoving = true;
    }

    /// <summary>Создать CombatOptions из текущего состояния BotEngine</summary>
    private CombatOptions MakeCombatOptions(string enemyCountLua, bool needApproach = false, bool noCombatCheck = false)
    {
        return new CombatOptions
        {
            RotationScript = noCombatCheck ? _fullScriptNoCombatCheck : _fullScript,
            EnemyCountLua = enemyCountLua,
            SpellFlagsLua = SpellFlagsLua,
            PlayerClass = PlayerClass,
            SpecName = _specName,
            IsMeleeSpec = IsMeleeSpec,
            IsTankSpec = IsTankSpec,
            IsHealer = IsHealer,
            PlayerInCombat = _objectManager.LocalPlayer?.InCombat == true,
            MoveBehindEnabled = MoveBehindEnabled,
            AoeEnabled = _aoeEnabled,
            AoeMinEnemies = AoeMinEnemies,
            AutoFace = _autoFace,
            NeedApproach = needApproach,
        };
    }

    /// <summary>Слейв: подбег к таргету + ротация. Используется в Attacking и Auto.</summary>
    private void SlaveAttackTick(Entities.WowPlayer player, string enemyCountLua)
    {
        if (AoeAvoidEnabled && IsAoeFleeing) return;
        if (AoeAvoidEnabled && TryAoEAvoidance(player)) return;

        var slaveTarget = _objectManager.GetTarget();
        if (slaveTarget == null || !slaveTarget.IsAlive || !slaveTarget.InCombat)
        {
            _combatExecutor.StopApproach();
            return;
        }

        // Единый боевой тик — тот же код что и solo!
        _combatExecutor.ExecuteCombatTick(player, slaveTarget,
            MakeCombatOptions(enemyCountLua, needApproach: true, noCombatCheck: true));
    }

    /// <summary>Follow/GoToPoint: SlaveCtrl двигает, бьём на ходу instants, стоя — полная ротация</summary>
    private void SlaveFollowCombatTick(WowPlayer player, string enemyCountLua)
    {
        var fTarget = _objectManager.GetTarget();
        bool fHasTarget = fTarget != null && fTarget.IsAlive && fTarget.Type != WowObjectType.Player && fTarget.InCombat;
        if (!fHasTarget) return;

        bool standing = _navigation.IsPlayerStanding(player);
        if (!standing)
        {
            // Бежим — только instants на ходу
            _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
        }
        else
        {
            // Стоим — полный бой через CombatExecutor (face + AoE + rotation)
            _combatExecutor.ExecuteCombatTick(player, fTarget,
                MakeCombatOptions(enemyCountLua, noCombatCheck: true));
        }
    }

    /// <summary>BossEngine тик (общий для Attacking и Auto)</summary>
    private bool TryBossEngineTick(WowPlayer player, string enemyCountLua)
    {
        if (!AutoPveEnabled) return false;
        BossEngine.IsMelee = IsMeleeSpec;
        BossEngine.IsHealer = IsHealer;
        BossEngine.IsTank = IsTankSpec;
        if (!BossEngine.IsActive) BossEngine.InstallListener();
        return BossEngine.Tick(player, enemyCountLua, SpellFlagsLua, _fullScript);
    }

    /// <summary>Auto mode: в бою → attack, вне боя → follow</summary>
    private void SlaveAutoModeTick(WowPlayer player, string enemyCountLua)
    {
        if (!Hivemind.AutoPauseAttack)
            Hivemind.SlaveAutoTick();

        var autoTarget = _objectManager.GetTarget();
        bool hasAutoTarget = !Hivemind.AutoPauseAttack &&
            autoTarget != null && autoTarget.IsAlive &&
            autoTarget.Type != WowObjectType.Player && autoTarget.InCombat;

        if (hasAutoTarget)
        {
            if (HivemindFollowing)
            {
                HivemindFollowing = false;
                SlaveCtrl.CmdStop();
                Logger.Info("Auto: IN COMBAT — stop follow, switch to attack mode");
            }
            SlaveAttackTick(player, enemyCountLua);
        }
        else
        {
            if (!HivemindFollowing && !Hivemind.AutoPauseFollow)
            {
                HivemindFollowing = true;
                string masterName = Hivemind.MasterName;
                if (!string.IsNullOrEmpty(masterName))
                    SlaveCtrl.CmdFollow(masterName);
                Logger.Info("Auto: OUT OF COMBAT — resume follow");
            }
            if (!Hivemind.AutoPauseFollow)
                SlaveCtrl.Tick();
        }
    }

    /// <summary>Hivemind: установка слушателя, чтение команд, register, ACK</summary>
    private void HivemindCommTick()
    {
        if (Hivemind.CurrentRole != Hivemind.Role.Slave && Hivemind.CurrentRole != Hivemind.Role.Master) return;

        // Устанавливаем слушатель (один раз)
        if (!Hivemind._slaveListenerInstalled)
        {
            _hook.ExecuteLua(Game.Hivemind.GetSlaveListenerScript(), 500);
            Hivemind._slaveListenerInstalled = true;
            Logger.Info("Hivemind: slave listener installed");
            if (_hiveMacroAddr == 0 && LuaReader != null && LuaReader.IsInitialized)
            {
                System.Threading.Thread.Sleep(300);
                _hiveMacroAddr = FindHiveMacroAddr();
                Logger.Log(LogCat.Hivemind, $"Hivemind: macro#2 addr=0x{_hiveMacroAddr:X8}");
            }
        }

        // Читаем команды
        _hiveCheckTick++;
        if (_hiveCheckTick < 3 || Hivemind.GossipReading) return;
        _hiveCheckTick = 0;

        // Команды мастера (для слейвов)
        string checkLua = Hivemind.GetSlaveReadScript();
        string? response = _hook.ExecuteLuaWithResult(checkLua);
        if (_logTick == 0) Logger.Log(LogCat.Hivemind, $"Hivemind: raw response=[{response ?? "NULL"}]");
        if (response != null)
        {
            var (cmd, arg, sender, time) = Hivemind.ParseSlaveResponse(response);
            bool isNew = time > LastHiveCheck || (time == LastHiveCheck && cmd != _lastHiveCmd);
            if (cmd != null && isNew)
            {
                LastHiveCheck = time;
                _lastHiveCmd = cmd;
                if (Hivemind.CurrentRole == Hivemind.Role.Slave &&
                    !string.IsNullOrEmpty(Hivemind.MasterName) &&
                    !string.IsNullOrEmpty(sender) &&
                    sender != Hivemind.MasterName)
                {
                    if (_logTick == 0) Logger.Log(LogCat.Hivemind, $"Hivemind: IGNORED {cmd} from {sender} (my master={Hivemind.MasterName})");
                }
                else
                {
                    Logger.Log(LogCat.Hivemind, $"Hivemind: received {cmd} from {sender} arg={arg} time={time}");
                    Hivemind.ExecuteSlaveCommand(cmd.Value, arg);
                }
            }
        }

        // Register + ACK (для мастера)
        if (Hivemind.CurrentRole == Hivemind.Role.Master)
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
                        Hivemind.ExecuteSlaveCommand(Hivemind.Command.Register, regParts[0]);
                    }
                }
            }

            string? ackResp = _hook.ExecuteLuaWithResult("WB_R=(WB_HIVE_ACK or '')..'|'..(WB_HIVE_ACK_TIME or '0')");
            if (ackResp != null)
            {
                var ackParts = ackResp.Split('|');
                if (ackParts.Length >= 2 && !string.IsNullOrEmpty(ackParts[0]))
                {
                    double.TryParse(ackParts[1], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double ackTime);
                    if (ackTime > _lastAckTime)
                    {
                        _lastAckTime = ackTime;
                        var ap = ackParts[0].Split('~', 2);
                        if (ap.Length == 2 && int.TryParse(ap[0], out int ackSeq))
                            Hivemind.ReceiveAck(ackSeq, ap[1]);
                    }
                }
            }
            Hivemind.TickAck();
        }

        // Slave Register (каждые ~10 сек)
        if (Hivemind.CurrentRole == Hivemind.Role.Slave)
        {
            _registerTick++;
            if (_registerTick >= 66)
            {
                _registerTick = 0;
                Hivemind.SendRegister(PlayerClass);
            }
        }

        // Master: Ctrl+ПКМ
        if (Hivemind.CurrentRole == Hivemind.Role.Master)
            Hivemind.MasterTick();
    }

    /// <summary>Баффы — единый тик для solo и slave</summary>
    private void BuffTick(WowPlayer player)
    {
        if (!_buffsEnabled) return;
        bool hasBuff = _enabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) ||
            !string.IsNullOrEmpty(SelectedBlessing) || !string.IsNullOrEmpty(SelectedAura) ||
            !string.IsNullOrEmpty(SelectedShout) || !string.IsNullOrEmpty(SelectedStance) ||
            !string.IsNullOrEmpty(SelectedPresence) || !string.IsNullOrEmpty(SelectedFeralForm) ||
            !string.IsNullOrEmpty(SelectedTotemEarth) || !string.IsNullOrEmpty(SelectedTotemFire) ||
            !string.IsNullOrEmpty(SelectedTotemWater) || !string.IsNullOrEmpty(SelectedTotemAir);
        if (!hasBuff) return;

        _buffCheckTick++;
        bool classBuffCheck = _buffCheckTick % 3 == 0;
        bool fullBuffCheck = _buffCheckTick >= 20;
        if (fullBuffCheck) _buffCheckTick = 0;
        if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
        {
            string buffScript = fullBuffCheck ? BuildBuffScript() : BuildClassBuffScript();
            if (!string.IsNullOrEmpty(buffScript))
            {
                if (fullBuffCheck) Logger.Log(LogCat.Buffs, $"ExecBuffs: len={buffScript.Length}");
                _hook.ExecuteLua(buffScript, 500);
                if (fullBuffCheck) _blessingCooldown = 40;
            }
        }
    }

    /// <summary>Полная остановка follow</summary>
    private void StopFollowMovement()
    {
        _ctm.Stop();
        if (_isMovingForward) { _hook.ExecuteLua("MoveForwardStop()", 50); _isMovingForward = false; }
    }

    // --- Toggle ---

    public void ToggleFollow()
    {
        _followEnabled = !_followEnabled;
        if (_followEnabled) EnsureRunning();
        else if (!_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive) StopTimer();
        if (!_followEnabled)
            StopFollowMovement();
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void ToggleRotation()
    {
        _rotationEnabled = !_rotationEnabled;
        Logger.Info($"ToggleRotation: {_rotationEnabled}, follow={_followEnabled}, buffs={_buffsEnabled}");
        if (_rotationEnabled) EnsureRunning();
        else
        {
            StopAoeMovement(); // остановить strafe/approach при выключении ротации
            if (!_followEnabled && !_buffsEnabled && !Hivemind.IsActive) StopTimer();
        }
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void StopAll()
    {
        _followEnabled = false;
        _rotationEnabled = false;
        StopTimer();
        StopFollowMovement();
        StopAoeMovement();
        OnStatusChanged?.Invoke("Stopped");
    }

    /// <summary>Остановка AoE flee движения + approach</summary>
    private void StopAoeMovement()
    {
        _combatHelper.ResetAoeFlee();
        _combatExecutor.StopApproach();
    }

    private volatile bool _tickPaused;
    public void PauseTick() => _tickPaused = true;
    public void ResumeTick() => _tickPaused = false;

    public void EnsureRunning()
    {
        if (_timer != null) return;
        _timer = new Timer(Tick, null, 0, 150);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private string GetStatusText()
    {
        if (_followEnabled && _rotationEnabled) return "Follow + Rotation";
        if (_followEnabled) return "Follow only";
        if (_rotationEnabled) return "Rotation only";
        return "Stopped";
    }

    // --- Main tick ---
    private int _logTick;
    private int _hiveCheckTick;

    private void Tick(object? state)
    {
        if (_tickPaused) return;
        AntiAfkTick(); // всегда, даже если бот неактивен

        if (!_followEnabled && !_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive) return;
        if (!_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;
            _ctm.SetPlayerBase(player.BaseAddress);

            // Кэш позиции для быстрого потока CTM watch
            if (Hivemind.CurrentRole == Hivemind.Role.Master)
                Hivemind.UpdateCachedPosition(player.X, player.Y, player.Z, player.Facing);

            // Авто-принятие реса для слейвов
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && player.IsDead)
            {
                _hook.ExecuteLua("AcceptResurrect()", 100);
                return;
            }

            // AoE Avoidance: ПРИОРИТЕТ НАД ВСЕМ — выбежать из лужи
            // Если flee активен — пропускаем ВСЁ (follow, rotation, hivemind) на 2 секунды
            if (IsAoeFleeing) return;
            if (AoeAvoidEnabled && TryAoEAvoidance(player)) return;

            // Глобальный декремент кулдаунов
            if (_blessingCooldown > 0) _blessingCooldown--;

            // Считаем мобов рядом для AoE (Залп охотника и т.п.)
            int nearbyEnemies = CountNearbyEnemies(player);
            int combatEnemies = CountNearbyCombatEnemies(player);
            string glovesLua = IsSpellEnabled("Gloves") ? "do local s,d=GetInventoryItemCooldown('player',10) if s==0 and UnitAffectingCombat('player') then UseInventoryItem(10) end end " : "";
            string enemyCountLua = $"WB_NE={nearbyEnemies} WB_NCE={combatEnemies} WB_AEMIN={AoeMinEnemies} " + glovesLua;

            // === HIVEMIND: коммуникация (addon messages, register, ACK) ===
            HivemindCommTick();

            // === БАФФЫ (единые для solo и slave) ===
            BuffTick(player);

            // === HIVEMIND SLAVE: хилер всегда хилит (если не Wipe) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && IsHealer && !Hivemind.WipeMode)
            {
                // Follow к мастеру / к точке если есть команда follow/auto/goto (и follow не на паузе)
                bool healerFollow = Hivemind.Mode == Hivemind.SlaveMode.Following ||
                    Hivemind.Mode == Hivemind.SlaveMode.GoingToPoint ||
                    (Hivemind.Mode == Hivemind.SlaveMode.Auto && !Hivemind.AutoPauseFollow);
                if (healerFollow)
                {
                    SlaveCtrl.FollowDistance = _followDistance;
                    SlaveCtrl.Tick();
                }
                // Хил ВСЕГДА — каждые ~500мс
                _healerTickCount++;
                if (_healerTickCount >= 3)
                {
                    _healerTickCount = 0;
                    string script = enemyCountLua + SpellFlagsLua + _fullScript;
                    _hook.ExecuteLua(script, 500);
                }
                return;
            }

            // === HIVEMIND SLAVE: выполнение режима (DPS) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave)
            {
                SlaveCtrl.FollowDistance = _followDistance;

                // Если вышли из боевого режима — остановить Lua approach
                if (Hivemind.Mode != Hivemind.SlaveMode.Attacking && Hivemind.Mode != Hivemind.SlaveMode.Auto && _combatExecutor.IsApproaching)
                {
                    _combatExecutor.StopApproach();
                }

                switch (Hivemind.Mode)
                {
                    case Hivemind.SlaveMode.Following:
                    case Hivemind.SlaveMode.GoingToPoint:
                        // Follow / GoToPoint — SlaveCtrl рулит движением, бой на ходу
                        SlaveCtrl.Tick();
                        SlaveFollowCombatTick(player, enemyCountLua);
                        break;

                    case Hivemind.SlaveMode.Attacking:
                        // Атака — BossEngine (если есть) → CombatExecutor
                        if (TryBossEngineTick(player, enemyCountLua)) break;
                        SlaveAttackTick(player, enemyCountLua);
                        break;

                    case Hivemind.SlaveMode.Auto:
                        // Авто: в бою → атака, вне боя → follow
                        if (TryBossEngineTick(player, enemyCountLua)) break;
                        SlaveAutoModeTick(player, enemyCountLua);
                        break;

                    case Hivemind.SlaveMode.Idle:
                        // Idle — бьём таргет если есть (через CombatExecutor)
                        if (Hivemind.ForceIdle || !_rotationEnabled) break;
                        var idleTarget = _objectManager.GetTarget();
                        _combatExecutor.ExecuteCombatTick(player, idleTarget,
                            MakeCombatOptions(enemyCountLua, noCombatCheck: true));
                        break;
                }
                return;
            }


            // Лог каждые ~5 сек (33 тиков по 150мс)
            _logTick++;
            if (_logTick >= 33) { _logTick = 0; var t = _objectManager.GetTarget(); var p = _objectManager.LocalPlayer; Logger.Info($"Tick: rot={_rotationEnabled} follow={_followEnabled} buffs={_buffsEnabled} target={t?.Name ?? "none"} alive={t?.IsAlive} NCE={CountNearbyCombatEnemies(p!)} dyn={_objectManager.DynObjects.Count} flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 200))}\""); if (t != null && p != null) Logger.Info($"Hitbox: player BR={p.BoundingRadius:F2} CR={p.CombatReach:F2} | target BR={t.BoundingRadius:F2} CR={t.CombatReach:F2} name={t.Name}"); }

            // Баффы solo обрабатываются в BuffTick() выше



            if (!_followEnabled && !_rotationEnabled && !HivemindFollowing) return;
            if (_logTick == 0) Logger.Log(LogCat.Follow, $"Tick: follow={_followEnabled} rot={_rotationEnabled} hiveFollow={HivemindFollowing}");

            var target = _objectManager.GetTarget();
            bool hasTarget = target != null && target.IsAlive;

            WowUnit? followTarget = _followGuid != 0 ? _objectManager.GetUnitByGuid(_followGuid) : null;
            float followDist = followTarget != null ? player.DistanceTo(followTarget) : 0;
            bool needsToMove = _followEnabled && followTarget != null && followDist > _followDistance;

            // === ТОЛЬКО FOLLOW ===
            if (_followEnabled && !_rotationEnabled)
            {
                if (followTarget != null)
                    FollowViaCTM(player, followTarget, _followDistance);
                return;
            }

            // === Автовыбор таргета (только в бою) ===
            bool targetTooFar = hasTarget && player.DistanceTo(target!) > _maxTargetRange;
            bool playerInCombat = player.InCombat;
            if (_autoSelectTarget && playerInCombat && (!hasTarget || targetTooFar))
            {
                // Приоритет: ассист танка → ближайший враг
                string assistLua = @"
                    local function DoAssist()
                        local nr=GetNumRaidMembers()
                        if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) then
                            for j=1,40 do local _,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,j) if not id then break end
                            if id==48263 or id==25780 or id==5487 or id==9634 then AssistUnit(u) return end end
                        end end
                        else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) then
                            for j=1,40 do local _,_,_,_,_,_,_,_,_,_,id=UnitBuff(u,j) if not id then break end
                            if id==48263 or id==25780 or id==5487 or id==9634 then AssistUnit(u) return end end
                        end end end
                        TargetNearestEnemy()
                    end DoAssist()";
                _hook.ExecuteLua(assistLua, 200);
                return;
            }

            // === ТОЛЬКО ROTATION ===
            bool targetInCombat = hasTarget && target!.InCombat;
            if (!_followEnabled && (_rotationEnabled || HivemindFollowing))
            {
                // Hivemind Follow: тот же код что и slave Following
                if (HivemindFollowing)
                {
                    SlaveFollowCombatTick(player, enemyCountLua);
                    return;
                }

                // Единый боевой тик — тот же CombatExecutor что и slave!
                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    var opts = MakeCombatOptions(enemyCountLua);
                    // Solo: используем GetRotationScript (с мультидот) вместо plain _fullScript
                    _combatExecutor.ExecuteCombatTick(player, target,
                        opts with { RotationScript = GetRotationScript(player) });
                }
                return;
            }

            // === ОБА: FOLLOW + ROTATION ===

            bool isCasting = player.IsCasting;

            if (needsToMove)
            {
                // БЕЖИМ к follow + instants на ходу БЕЗ поворота
                _ctm.MoveTo(followTarget!.X, followTarget.Y, followTarget.Z, _followDistance);
                if (hasTarget && targetInCombat && !isCasting)
                    _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
            }
            else if (isCasting)
            {
                // НА МЕСТЕ кастуем — стоп движения (только когда уже добежал)
                _ctm.Stop();
            }
            else
            {
                // В дистанции — единый боевой тик
                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    var opts = MakeCombatOptions(enemyCountLua);
                    _combatExecutor.ExecuteCombatTick(player, target,
                        opts with { RotationScript = GetRotationScript(player) });
                }
            }
        }
        catch (Exception ex) { Logger.Error("Tick error", ex); }
    }

    // --- AoE Multi-dot ---

    /// <summary>
    /// Выбирает скрипт: AoE мультидот или обычная ротация.
    /// C# собирает имена мобов рядом, Lua проверяет дебаффы и кастует.
    /// </summary>
    // GetRotationScript → делегирование в MultiDotHelper
    private string GetRotationScript(WowPlayer player) =>
        _multiDotHelper.GetRotationScript(player, _fullScript,
            _aoeEnabled, _useMultiDot, _maxDotTargets,
            _useMindSear, _mindSearTargets,
            DispManaThreshold, SFManaThreshold);

    // --- Buff system ---

    // RaidBuffs, BuffAliases, BuffReagents → перенесены в BuffManager

    /// <summary>Синхронизировать настройки баффов из BotEngine в BuffManager</summary>
    private void SyncBuffSettings()
    {
        _buffManager.PlayerClass = PlayerClass;
        _buffManager.SelectedSeal = SelectedSeal;
        _buffManager.SelectedBlessing = SelectedBlessing;
        _buffManager.SelectedAura = SelectedAura;
        _buffManager.SelectedShout = SelectedShout;
        _buffManager.SelectedStance = SelectedStance;
        _buffManager.SelectedPresence = SelectedPresence;
        _buffManager.SelectedFeralForm = SelectedFeralForm;
        _buffManager.SelectedPet = SelectedPet;
        _buffManager.SelectedTotemEarth = SelectedTotemEarth;
        _buffManager.SelectedTotemFire = SelectedTotemFire;
        _buffManager.SelectedTotemWater = SelectedTotemWater;
        _buffManager.SelectedTotemAir = SelectedTotemAir;
        _buffManager.SelectedWeaponMH = SelectedWeaponMH;
        _buffManager.SelectedWeaponOH = SelectedWeaponOH;
        _buffManager.AoeEnabled = _aoeEnabled;
        _buffManager.AoeSealSwap = AoeSealSwap;
        _buffManager.EnabledBuffs = _enabledBuffs;
    }

    /// <summary>Делегирование в BuffManager (обратная совместимость)</summary>
    private string BuildClassBuffScript()
    {
        SyncBuffSettings();
        return _buffManager.BuildClassBuffScript();
    }

    private string BuildBuffScript()
    {
        SyncBuffSettings();
        return _buffManager.BuildBuffScript();
    }

    public void Dispose()
    {
        StopAll();
    }
}
