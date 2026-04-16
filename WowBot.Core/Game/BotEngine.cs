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
    private int _registerTick = 20; // стартует с порога — первый Register сразу
    private int _blessingCooldown; // пропустить N баф-чеков после каста благословения
    private double _lastRegisterTime;
    private double _lastAckTime;
    private int _listenerCheckTick; // проверка живости листенера
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
        Hivemind.OnBuffChanged += (key, value) =>
        {
            switch (key)
            {
                case "seal": SelectedSeal = value; _buffManager.SelectedSeal = value; break;
                case "blessing": SelectedBlessing = value; _buffManager.SelectedBlessing = value; break;
                case "aura": SelectedAura = value; _buffManager.SelectedAura = value; break;
                case "shout": SelectedShout = value; _buffManager.SelectedShout = value; break;
                case "stance": SelectedStance = value; _buffManager.SelectedStance = value; break;
                case "presence": SelectedPresence = value; _buffManager.SelectedPresence = value; break;
                case "totem_earth": SelectedTotemEarth = value; _buffManager.SelectedTotemEarth = value; break;
                case "totem_fire": SelectedTotemFire = value; _buffManager.SelectedTotemFire = value; break;
                case "totem_water": SelectedTotemWater = value; _buffManager.SelectedTotemWater = value; break;
                case "totem_air": SelectedTotemAir = value; _buffManager.SelectedTotemAir = value; break;
                case "weapon_mh": SelectedWeaponMH = value; _buffManager.SelectedWeaponMH = value; break;
                case "weapon_oh": SelectedWeaponOH = value; _buffManager.SelectedWeaponOH = value; break;
            }
            Logger.Info($"BuffChanged: {key}={value}");
        };
        _combatHelper = new CombatHelper(objectManager, hook, ctm);
        _combatExecutor = new CombatExecutor(hook, navigation, _combatPositioning, _combatHelper, ctm);
        _multiDotHelper = new MultiDotHelper(objectManager);

        // Навигация через навмеш (опционально)
        NavEngine = new WowBot.Core.Navigation.NavEngine(ctm, objectManager);
        SlaveCtrl.NavEngine = NavEngine;
        _commandSources.Add(UserCommands);
        _commandSources.Add(HivemindCommands);
    }

    /// <summary>
    /// Обработать все ожидающие команды из всех источников.
    /// Вызывается в начале Tick() — единый обработчик для UI и Hivemind.
    /// </summary>
    private void ProcessPendingCommands()
    {
        foreach (var source in _commandSources)
        {
            while (source.HasPendingCommand)
            {
                var cmd = source.DequeueCommand();
                if (cmd != null)
                    ProcessCommand(cmd);
            }
        }
    }

    /// <summary>Единый обработчик команд — и UI, и Hivemind проходят здесь</summary>
    public void ProcessCommand(Abstractions.BotCommand cmd)
    {
        Logger.Info($"ProcessCommand: {cmd.Type} from {cmd.Source} target={cmd.TargetName}");

        switch (cmd.Type)
        {
            // === Тоглы (UI) ===
            case Abstractions.BotCommandType.StartRotation:
                _rotationEnabled = true;
                EnsureRunning();
                OnStatusChanged?.Invoke(GetStatusText());
                break;

            case Abstractions.BotCommandType.StopRotation:
                _rotationEnabled = false;
                StopAoeMovement();
                if (!_followEnabled && !_buffsEnabled && !Hivemind.IsActive) StopTimer();
                OnStatusChanged?.Invoke(GetStatusText());
                break;

            case Abstractions.BotCommandType.StartBuffs:
                BuffsEnabled = true;
                break;

            case Abstractions.BotCommandType.StopBuffs:
                BuffsEnabled = false;
                break;

            // === Движение ===
            case Abstractions.BotCommandType.Follow:
                if (cmd.Source == "Hivemind" && !string.IsNullOrEmpty(cmd.TargetName))
                {
                    // Slave follow: через Hivemind + SlaveCtrl
                    Hivemind.MasterName = cmd.TargetName;
                    Hivemind.Mode = Hivemind.SlaveMode.Following;
                    SlaveCtrl.CmdFollow(cmd.TargetName);
                    HivemindFollowing = true;
                    _rotationEnabled = true;
                    BuffsEnabled = true;
                    // Мили: сохранить MoveBehind и выключить (не забегать за спину при Follow)
                    if (IsMeleeSpec)
                    {
                        MoveBehindSavedState = MoveBehindEnabled;
                        MoveBehindEnabled = false;
                        Logger.Info($"Follow: MoveBehind OFF (saved={MoveBehindSavedState})");
                    }
                }
                else
                {
                    // Solo follow: простой toggle
                    _followEnabled = true;
                }
                EnsureRunning();
                OnStatusChanged?.Invoke(GetStatusText());
                break;

            case Abstractions.BotCommandType.Stop:
                if (cmd.Source == "Hivemind")
                {
                    Hivemind.Mode = Hivemind.SlaveMode.Idle;
                    Hivemind.ForceIdle = true;
                    HivemindFollowing = false;
                    SlaveCtrl.CmdStop();
                    _hook.ExecuteLua("ClearTarget()", 100);
                }
                else
                {
                    // Solo: просто выключить follow
                    _followEnabled = false;
                    StopFollowMovement();
                }
                if (!_followEnabled && !_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive) StopTimer();
                OnStatusChanged?.Invoke(GetStatusText());
                break;

            case Abstractions.BotCommandType.MoveTo:
                Hivemind.Mode = Hivemind.SlaveMode.GoingToPoint;
                HivemindFollowing = false;
                SlaveCtrl.CmdStop();
                SlaveCtrl.CmdGotoPoint(cmd.X, cmd.Y, cmd.Z);
                _rotationEnabled = true;
                BuffsEnabled = true;
                EnsureRunning();
                break;

            // === Бой ===
            case Abstractions.BotCommandType.Attack:
                Hivemind.MasterName = cmd.TargetName;
                Hivemind.Mode = Hivemind.SlaveMode.Attacking;
                HivemindFollowing = false;
                SlaveCtrl.CmdStop();
                _hook.ExecuteLua($"AssistUnit('{cmd.TargetName.Replace("'", "\\'")}') StartAttack()", 200);
                _rotationEnabled = true;
                BuffsEnabled = true;
                // Мили: восстановить MoveBehind из сохранённого состояния
                if (MoveBehindSavedState != null)
                {
                    MoveBehindEnabled = MoveBehindSavedState.Value;
                    MoveBehindSavedState = null;
                }
                EnsureRunning();
                break;

            case Abstractions.BotCommandType.Auto:
                Hivemind.MasterName = cmd.TargetName;
                Hivemind.Mode = Hivemind.SlaveMode.Auto;
                Hivemind.AutoPauseFollow = false;
                Hivemind.AutoPauseAttack = false;
                SlaveCtrl.CmdFollow(cmd.TargetName);
                HivemindFollowing = true;
                _rotationEnabled = true;
                BuffsEnabled = true;
                // Мили: восстановить MoveBehind
                if (MoveBehindSavedState != null)
                {
                    MoveBehindEnabled = MoveBehindSavedState.Value;
                    MoveBehindSavedState = null;
                }
                EnsureRunning();
                break;

            case Abstractions.BotCommandType.AutoToggleFollow:
                if (Hivemind.Mode != Hivemind.SlaveMode.Auto) break;
                Hivemind.AutoPauseFollow = !Hivemind.AutoPauseFollow;
                if (Hivemind.AutoPauseFollow)
                {
                    SlaveCtrl.CmdStop();
                    HivemindFollowing = false;
                }
                else
                {
                    SlaveCtrl.CmdFollow(Hivemind.MasterName);
                    HivemindFollowing = true;
                }
                break;

            case Abstractions.BotCommandType.AutoToggleAttack:
                if (Hivemind.Mode != Hivemind.SlaveMode.Auto) break;
                Hivemind.AutoPauseAttack = !Hivemind.AutoPauseAttack;
                if (Hivemind.AutoPauseAttack)
                    _hook.ExecuteLua("ClearTarget()", 100);
                break;

            case Abstractions.BotCommandType.Scatter:
                // Scatter: разбежаться + остановить Follow (стоять и бить)
                Hivemind.Mode = Hivemind.SlaveMode.Attacking;
                HivemindFollowing = false;
                var scPlayer = _objectManager?.LocalPlayer;
                if (scPlayer != null)
                {
                    float scatterDist = 12f;
                    if (cmd.X > 0) scatterDist = cmd.X; // дистанция через X
                    string scName = _objectManager!.GetPlayerName() ?? "slave";
                    int hash = 0;
                    foreach (char c in scName) hash = hash * 31 + c;
                    float angle = (hash & 0xFFFF) / 65535f * 2f * MathF.PI + scPlayer.Facing * 0.3f;
                    _ctm.MoveTo(
                        scPlayer.X + scatterDist * MathF.Cos(angle),
                        scPlayer.Y + scatterDist * MathF.Sin(angle),
                        scPlayer.Z, 1.5f);
                }
                break;

            case Abstractions.BotCommandType.Wipe:
                Hivemind.WipeMode = !Hivemind.WipeMode;
                break;

            case Abstractions.BotCommandType.Interact:
                if (!string.IsNullOrEmpty(cmd.TargetName))
                {
                    string masterN = cmd.TargetName.Replace("'", "\\'");
                    _hook.ExecuteLua($"AssistUnit('{masterN}') InteractUnit('target')", 300);
                }
                break;
        }
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

    // Command Sources — единый поток команд от UI и Hivemind
    public UserCommandSource UserCommands { get; } = new();
    public HivemindCommandSource HivemindCommands { get; } = new();
    private readonly List<Abstractions.ICommandSource> _commandSources = new();

    public bool MoveBehindEnabled { get; set; }
    public bool? MoveBehindSavedState { get; private set; } // сохранённое состояние перед Follow

    public bool AoeAvoidEnabled { get; set; } = true;

    // Auto-mode: кулдаун на follow после боя (не фолловить сразу когда InCombat сбросился)
    private DateTime _lastCombatTime = DateTime.MinValue;

    // Ghost route — полёт призрака по маршруту + repair route
    private List<(float x, float y, float z)>? _ghostRoute;
    private List<(float x, float y, float z)>? _repairRoute;
    private int _ghostRouteIndex; // -1 = ждём загрузки
    private bool _ghostRunning;
    private int _ghostAliveCheckTick;
    private bool _ghostSawNull; // видели LocalPlayer == null (загрузка началась)
    private enum GhostPhase { Flying, WaitingCorpse, RepairRun, Repairing }
    private GhostPhase _ghostPhase;

    public void StartRepairRun()
    {
        if (_repairRoute == null || _repairRoute.Count == 0)
            LoadGhostRoute();
        if (_repairRoute == null || _repairRoute.Count == 0)
        {
            Logger.Info("RepairRun: нет маршрута (Routes/repair_route_*.txt)");
            return;
        }
        _ghostRunning = true;
        _ghostAliveCheckTick = 0;
        StartRepairPhase();
        EnsureRunning();
    }

    public void StartGhostRun()
    {
        if (_ghostRoute == null || _ghostRoute.Count == 0)
            LoadGhostRoute();
        if (_ghostRoute == null || _ghostRoute.Count == 0)
        {
            Logger.Info("GhostRun: нет маршрута (Routes/*.txt)");
            return;
        }
        _ghostRouteIndex = -1; // -1 = ждём загрузки
        _ghostPhase = GhostPhase.Flying;
        _ghostRunning = true;
        _ghostAliveCheckTick = 0;
        _ghostSawNull = false;
        EnsureRunning();
        Logger.Info($"GhostRun: STARTED, {_ghostRoute.Count} points, waiting for load screen");
    }

    private void LoadGhostRoute()
    {
        string basePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string routesDir = System.IO.Path.Combine(basePath, "Routes");
        if (!System.IO.Directory.Exists(routesDir)) return;

        _ghostRoute = LoadRoute(routesDir, "ghost_route_*.txt");
        _repairRoute = LoadRoute(routesDir, "repair_route_*.txt");

        if (_ghostRoute != null) Logger.Info($"GhostRun: loaded ghost route {_ghostRoute.Count} points");
        if (_repairRoute != null) Logger.Info($"GhostRun: loaded repair route {_repairRoute.Count} points");
    }

    private static List<(float x, float y, float z)>? LoadRoute(string dir, string pattern)
    {
        var files = System.IO.Directory.GetFiles(dir, pattern).OrderByDescending(f => f).ToArray();
        if (files.Length == 0) return null;
        var route = new List<(float x, float y, float z)>();
        foreach (var line in System.IO.File.ReadAllLines(files[0]))
        {
            var parts = line.Split(';');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float z))
                route.Add((x, y, z));
        }
        return route.Count > 0 ? route : null;
    }

    private void GhostRunTick()
    {
        if (!_ghostRunning) return;

        // Безопасность: если жив и в Flying фазе — ghost run завис, остановить
        if (_ghostPhase == GhostPhase.Flying)
        {
            var p = _objectManager.LocalPlayer;
            if (p != null && !p.IsDead && p.Health > 1)
            {
                try
                {
                    var alive = _hook.ExecuteLuaWithResult("WB_R=UnitIsGhost('player') and '1' or '0'");
                    if (alive == "0")
                    {
                        _ghostRunning = false;
                        Logger.Info("GhostRunTick: player alive, aborting ghost run");
                        return;
                    }
                }
                catch { }
            }
        }

        switch (_ghostPhase)
        {
            case GhostPhase.Flying:
                GhostFlyTick();
                break;
            case GhostPhase.WaitingCorpse:
                GhostWaitCorpseTick();
                break;
            case GhostPhase.RepairRun:
                RepairRunTick();
                break;
            case GhostPhase.Repairing:
                RepairTick();
                break;
        }
    }

    private void GhostFlyTick()
    {
        if (_ghostRoute == null) { _ghostRunning = false; return; }

        // Ждём загрузки: RepopMe → загрузка (player=null) → кладбище (player!=null + IsGhost)
        if (_ghostRouteIndex == -1)
        {
            var player = _objectManager.LocalPlayer;

            // Фаза 1: ждём начало загрузки (player пропадает)
            if (!_ghostSawNull)
            {
                if (player == null)
                {
                    _ghostSawNull = true;
                    Logger.Info("GhostRun: load screen detected (player=null)");
                }
                else
                {
                    // Уже призрак без загрузки (повторное нажатие) — пропускаем ожидание
                    try
                    {
                        var preCheck = _hook.ExecuteLuaWithResult("WB_R=UnitIsGhost('player') and '1' or '0'");
                        if (preCheck == "1") { _ghostSawNull = true; Logger.Info("GhostRun: already ghost, skipping load wait"); }
                    }
                    catch { }
                }
                if (!_ghostSawNull) return;
            }

            // Фаза 2: ждём окончание загрузки (player появился + призрак)
            if (player == null) return; // ещё грузится

            try
            {
                var check = _hook.ExecuteLuaWithResult("WB_R=UnitIsGhost('player') and '1' or '0'");
                if (check != "1") return;
            }
            catch { return; }

            // Загрузились на кладбище! Найти ближайшую точку маршрута
            float bestDist = float.MaxValue;
            int bestIdx = 0;
            for (int i = 0; i < _ghostRoute.Count; i++)
            {
                var p = _ghostRoute[i];
                float d = (player.X - p.x) * (player.X - p.x) + (player.Y - p.y) * (player.Y - p.y);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            _ghostRouteIndex = bestIdx;
            Logger.Info($"GhostRun: loaded on graveyard! Nearest point={bestIdx}/{_ghostRoute.Count} dist={MathF.Sqrt(bestDist):F0}");
            return;
        }

        var pl = _objectManager.LocalPlayer;
        if (pl == null) return;

        // Проверка: ещё призрак?
        _ghostAliveCheckTick++;
        if (_ghostAliveCheckTick >= 33)
        {
            _ghostAliveCheckTick = 0;
            try
            {
                var ghostCheck = _hook.ExecuteLuaWithResult("WB_R=UnitIsDeadOrGhost('player') and '1' or '0'");
                if (ghostCheck == "0")
                {
                    // Воскресли раньше (кто-то зарезал) — сразу к ремонту
                    StartRepairPhase();
                    return;
                }
            }
            catch { }
        }

        if (_ghostRouteIndex >= _ghostRoute.Count)
        {
            _hook.ExecuteLua("RetrieveCorpse()", 200);
            Logger.Info("GhostRun: RetrieveCorpse — waiting for load screen");
            _ghostPhase = GhostPhase.WaitingCorpse;
            return;
        }

        var target = _ghostRoute[_ghostRouteIndex];
        float dx = pl.X - target.x, dy = pl.Y - target.y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 15f)
        {
            _ghostRouteIndex++;
            Logger.Info($"GhostRun: point {_ghostRouteIndex}/{_ghostRoute.Count} reached");
            return;
        }

        _ctm.MoveTo(target.x, target.y, target.z, 10f);
    }

    private void GhostWaitCorpseTick()
    {
        // Ждём пока загрузимся живыми (после RetrieveCorpse)
        try
        {
            var check = _hook.ExecuteLuaWithResult("WB_R=UnitIsDeadOrGhost('player') and '1' or '0'");
            if (check == "0")
            {
                Logger.Info("GhostRun: alive after RetrieveCorpse");
                StartRepairPhase();
            }
        }
        catch { } // ещё грузится
    }

    private void StartRepairPhase()
    {
        if (_repairRoute == null || _repairRoute.Count == 0)
        {
            _ghostRunning = false;
            Logger.Info("GhostRun: DONE (no repair route)");
            return;
        }

        // Найти ближайшую точку repair route (ждём загрузку)
        var player = _objectManager.LocalPlayer;
        if (player == null) return; // ещё грузится — ждём

        float bestDist = float.MaxValue;
        int bestIdx = 0;
        for (int i = 0; i < _repairRoute.Count; i++)
        {
            var p = _repairRoute[i];
            float d = (player.X - p.x) * (player.X - p.x) + (player.Y - p.y) * (player.Y - p.y);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        _ghostRouteIndex = bestIdx;
        _ghostPhase = GhostPhase.RepairRun;
        Logger.Info($"RepairRun: START nearest={bestIdx}/{_repairRoute.Count} dist={MathF.Sqrt(bestDist):F0}");
    }

    private void RepairRunTick()
    {
        if (_repairRoute == null) { _ghostRunning = false; return; }

        var pl = _objectManager.LocalPlayer;
        if (pl == null) return;

        if (_ghostRouteIndex >= _repairRoute.Count)
        {
            // Дошли до ремонтника — починиться
            _ghostPhase = GhostPhase.Repairing;
            _ghostAliveCheckTick = 0;
            Logger.Info("RepairRun: arrived at NPC, repairing");
            return;
        }

        var target = _repairRoute[_ghostRouteIndex];
        float dx = pl.X - target.x, dy = pl.Y - target.y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 5f)
        {
            _ghostRouteIndex++;
            Logger.Info($"RepairRun: point {_ghostRouteIndex}/{_repairRoute.Count} reached");
            return;
        }

        _ctm.MoveTo(target.x, target.y, target.z, 3f);
    }

    private void RepairTick()
    {
        _ghostAliveCheckTick++;
        if (_ghostAliveCheckTick < 10) return; // дать время подойти
        _ghostAliveCheckTick = 0;

        // Interact с ремонтником + ремонт
        try
        {
            _hook.ExecuteLua("InteractUnit('Алхимик Финкльштейн')", 300);
            Logger.Info("RepairRun: InteractUnit sent");
            System.Threading.Thread.Sleep(1500);
            _hook.ExecuteLua("RepairAllItems()", 200);
            Logger.Info("RepairRun: RepairAllItems sent");
        }
        catch (Exception ex) { Logger.Error($"RepairRun error: {ex.Message}"); }

        _ghostRunning = false;
        Logger.Info("GhostRun: FULLY DONE (ghost → repair)");
    }

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

    /// <summary>Перечитать скрипты (hot-reload: C# ротация или Lua с диска)</summary>
    public void ReloadScripts()
    {
        if (string.IsNullOrEmpty(PlayerClass)) return;
        var rotation = Rotations.RotationRegistry.Find(PlayerClass, _specName);
        if (rotation != null)
        {
            LoadRotation(rotation.GetInstantScript(), rotation.GetFullScript());
            Logger.Info($"Scripts reloaded: {rotation.Name} (C#)");
        }
        else
        {
            var fullScript = Rotations.AllRotations.GetFullScript(PlayerClass);
            var instantScript = Rotations.AllRotations.GetInstantScript(PlayerClass);
            LoadRotation(instantScript, fullScript);
            Logger.Info($"Scripts reloaded: {PlayerClass} (Lua fallback)");
        }
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
    private int _slaveAttackLogTick;
    private void SlaveAttackTick(Entities.WowPlayer player, string enemyCountLua)
    {
        if (AoeAvoidEnabled && IsAoeFleeing) return;
        if (AoeAvoidEnabled && TryAoEAvoidance(player)) return;

        var slaveTarget = _objectManager.GetTarget();
        _slaveAttackLogTick++;
        if (_slaveAttackLogTick >= 33) // каждые ~5с
        {
            _slaveAttackLogTick = 0;
            int ctmAction = _ctm.GetCurrentAction();
            float ctmX = _ctm.ReadX(), ctmY = _ctm.ReadY();
            float dist = slaveTarget != null ? player.DistanceTo(slaveTarget) : -1;
            Logger.Log(LogCat.Combat, $"SlaveAttack: target={slaveTarget?.Name ?? "NULL"} alive={slaveTarget?.IsAlive} combat={slaveTarget?.InCombat} type={slaveTarget?.Type} casting={player.IsCasting} dist={dist:F1} ctm={ctmAction} ctmXY=({ctmX:F0},{ctmY:F0}) pos=({player.X:F0},{player.Y:F0})");
            // Лог тотемов шамана
            if (PlayerClass == "SHAMAN")
            {
                string? totemLog = _hook.ExecuteLuaWithResult("WB_R=WB_TOTEM_LOG or '' WB_TOTEM_LOG=nil");
                if (!string.IsNullOrEmpty(totemLog))
                    Logger.Info($"TOTEM: {totemLog}");
            }
        }

        bool isAttackingMode = Hivemind.Mode == Hivemind.SlaveMode.Attacking;
        if (slaveTarget == null || !slaveTarget.IsAlive || (!isAttackingMode && !slaveTarget.InCombat))
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
        bool playerInCombat = player.InCombat;

        // Запоминаем время последнего боя
        if (playerInCombat || hasAutoTarget)
            _lastCombatTime = DateTime.UtcNow;

        // Кулдаун: не фолловить 3 сек после боя (InCombat сбрасывается мгновенно между мобами)
        bool recentCombat = (DateTime.UtcNow - _lastCombatTime).TotalSeconds < 3.0;

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
        else if ((playerInCombat || recentCombat) && !Hivemind.AutoPauseAttack)
        {
            // В бою или недавно был в бою — ждём AssistUnit, НЕ фолловить
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
            {
                SlaveCtrl.FollowDistance = _followDistance;
                SlaveCtrl.Tick();
            }
        }
    }

    /// <summary>Hivemind: установка слушателя, чтение команд, register, ACK</summary>
    private void HivemindCommTick()
    {
        if (Hivemind.CurrentRole != Hivemind.Role.Slave && Hivemind.CurrentRole != Hivemind.Role.Master) return;

        // Устанавливаем слушатель (один раз) + проверка живости каждые ~30 сек
        if (!Hivemind._slaveListenerInstalled)
        {
            _hook.ExecuteLua(Game.Hivemind.GetSlaveListenerScript(), 500);
            Hivemind._slaveListenerInstalled = true;
            _listenerCheckTick = 0;
            _lastRegisterTime = 0;
            _lastAckTime = 0;
            Logger.Info("Hivemind: slave listener installed");
        }
        else
        {
            _listenerCheckTick++;
            if (_listenerCheckTick >= 200) // ~30 сек (200 * 150мс)
            {
                _listenerCheckTick = 0;
                var alive = _hook.ExecuteLuaWithResult("WB_R=WB_HIVE_REGISTERED and '1' or '0'");
                if (alive != "1")
                {
                    Logger.Info("Hivemind: listener DEAD — reinstalling");
                    _hook.ExecuteLua(Game.Hivemind.GetSlaveListenerScript(), 500);
                    _lastRegisterTime = 0;
                    _lastAckTime = 0;
                }
            }
        }

        // Читаем команды
        _hiveCheckTick++;
        if (_hiveCheckTick < 3 || Hivemind.GossipReading) return;
        _hiveCheckTick = 0;

        // Батч: мастер читает cmd+register+ACK одним Lua вызовом, слейв — только cmd
        string? response;
        if (Hivemind.CurrentRole == Hivemind.Role.Master)
        {
            // Один вызов вместо трёх: cmd|arg|sender|time#reg|regSender|regTime#ack|ackTime
            response = _hook.ExecuteLuaWithResult(
                "local rq=WB_HIVE_REG_Q or '' WB_HIVE_REG_Q='' " +
                "WB_R=(WB_HIVE_CMD or '')..'|'..(WB_HIVE_ARG or '')..'|'..(WB_HIVE_SENDER or '')..'|'..(WB_HIVE_TIME or '0')" +
                "..'§'..rq" +
                "..'§'..(WB_HIVE_ACK or '')..'|'..(WB_HIVE_ACK_TIME or '0')");
        }
        else
        {
            response = _hook.ExecuteLuaWithResult(Hivemind.GetSlaveReadScript());
        }
        // Диагностика: логировать только если есть команда (cmd часть не пустая)
        if (_logTick == 0)
            Logger.Log(LogCat.Hivemind, $"Hivemind: raw response=[{response ?? "NULL"}]");

        if (response != null)
        {
            // Парсим cmd часть (до первого # или всё для слейва)
            string cmdPart = response;
            string? regPart = null, ackPart = null;

            if (Hivemind.CurrentRole == Hivemind.Role.Master)
            {
                var sections = response.Split('§');
                cmdPart = sections.Length > 0 ? sections[0] : "";
                regPart = sections.Length > 1 ? sections[1] : null;
                ackPart = sections.Length > 2 ? sections[2] : null;
            }

            // Команды
            var (cmd, arg, sender, time) = Hivemind.ParseSlaveResponse(cmdPart);
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

            // Register + ACK (мастер) — из того же ответа
            if (Hivemind.CurrentRole == Hivemind.Role.Master)
            {
                // Register очередь: "name;class;buffs@sender|name2;class2@sender2"
                if (!string.IsNullOrEmpty(regPart))
                {
                    var entries = regPart.Split('|');
                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrEmpty(entry)) continue;
                        // формат: name;class;buffs@sender
                        var atParts = entry.Split('@', 2);
                        string regData = atParts[0];
                        if (!string.IsNullOrEmpty(regData))
                        {
                            Logger.Info($"Hivemind: REGISTER received — [{regData}]");
                            Hivemind.ExecuteSlaveCommand(Hivemind.Command.Register, regData);
                        }
                    }
                }

                if (ackPart != null)
                {
                    var ackParts = ackPart.Split('|');
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
        }
        else if (Hivemind.CurrentRole == Hivemind.Role.Master)
        {
            Hivemind.TickAck();
        }

        // Slave Register (каждые ~10 сек)
        if (Hivemind.CurrentRole == Hivemind.Role.Slave)
        {
            _registerTick++;
            if (_registerTick >= 20) // ~3 сек (было 66 = 10 сек)
            {
                _registerTick = 0;
                Hivemind.SendRegister(PlayerClass, GetBuffSettingsString());
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
        ProcessCommand(new Abstractions.BotCommand
        {
            Type = _followEnabled ? Abstractions.BotCommandType.Stop : Abstractions.BotCommandType.Follow,
            Source = "UI"
        });
    }

    public void ToggleRotation()
    {
        ProcessCommand(new Abstractions.BotCommand
        {
            Type = _rotationEnabled ? Abstractions.BotCommandType.StopRotation : Abstractions.BotCommandType.StartRotation,
            Source = "UI"
        });
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

    private int _tickRunning; // защита от re-entrant Timer
    private void Tick(object? state)
    {
        if (Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0) return; // тик уже выполняется
        try { TickCore(state); }
        finally { Interlocked.Exchange(ref _tickRunning, 0); }
    }
    private void TickCore(object? state)
    {
        if (_tickPaused) return;
        AntiAfkTick(); // всегда, даже если бот неактивен

        // Обработать команды из очереди (UI, Hivemind)
        ProcessPendingCommands();

        if (!_followEnabled && !_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive && !_ghostRunning) return;
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

            // Ghost run — полёт призрака по маршруту (приоритет над всем)
            if (_ghostRunning)
            {
                GhostRunTick();
                return;
            }

            // Мёртвый слейв: слушаем Hivemind (для RepopMe) + авто-принятие реса
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && player.IsDead)
            {
                HivemindCommTick(); // чтобы получить RepopMe
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
            // WB_NCET: мобы рядом с таргетом (для рейнж DPS, которые стоят далеко от мобов)
            var currentTarget = _objectManager.GetTarget();
            int enemiesNearTarget = currentTarget != null && currentTarget.IsAlive
                ? _combatHelper.CountEnemiesNearTarget(currentTarget, 10f)
                : combatEnemies;
            string glovesLua = IsSpellEnabled("Gloves") ? "do local s,d=GetInventoryItemCooldown('player',10) if s==0 and UnitAffectingCombat('player') then UseInventoryItem(10) end end " : "";
            string enemyCountLua = $"WB_NE={nearbyEnemies} WB_NCE={combatEnemies} WB_NCET={enemiesNearTarget} WB_AEMIN={AoeMinEnemies} " + glovesLua;

            // BossEngine CLEU listener — ставится для всех (master/solo/slave) для Disrupting Shout и тактик
            if (player.InCombat && !BossEngine.IsActive)
                BossEngine.InstallListener();

            // === HIVEMIND: коммуникация (addon messages, register, ACK) ===
            HivemindCommTick();

            // === БАФФЫ (единые для solo и slave) ===
            BuffTick(player);

            // === HIVEMIND SLAVE: хилер всегда хилит (если не Wipe) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && IsHealer && !Hivemind.WipeMode)
            {
                bool inAutoMode = Hivemind.Mode == Hivemind.SlaveMode.Auto;
                bool inCombat = player.InCombat;

                // AssistUnit ВСЕГДА в авто — чтобы хил получил таргет мастера
                if (inAutoMode && !Hivemind.AutoPauseAttack)
                    Hivemind.SlaveAutoTick();

                var autoTarget = _objectManager.GetTarget();
                bool isAttacking = Hivemind.Mode == Hivemind.SlaveMode.Attacking;
                bool hasAutoTarget = autoTarget != null && autoTarget.IsAlive &&
                    autoTarget.Type != WowObjectType.Player && (autoTarget.InCombat || isAttacking);

                // В бою (авто или атака): позиционируемся как ДПС (HPal=мили, остальные=рейнж)
                if ((inAutoMode || isAttacking) && (inCombat || hasAutoTarget))
                {
                    var healTarget = _objectManager.GetTarget();
                    if (healTarget != null && healTarget.IsAlive && healTarget.InCombat &&
                        healTarget.Type != WowObjectType.Player)
                    {
                        bool isMeleeHealer = PlayerClass == "PALADIN"; // HPal = мили хилер
                        // Approach: HPal → мили рейндж, остальные хилы → 28 ярдов
                        float meleeRange = MathF.Max(healTarget.CombatReach + player.CombatReach + 4f / 3f, 5f);
                        float maxDist = isMeleeHealer ? meleeRange : 28f;
                        float dist = player.DistanceTo(healTarget);
                        if (dist > maxDist)
                        {
                            float angle = MathF.Atan2(player.Y - healTarget.Y, player.X - healTarget.X);
                            float stopDist = isMeleeHealer ? MathF.Max(meleeRange - 1.5f, 1.5f) : 25f;
                            float destX = healTarget.X + stopDist * MathF.Cos(angle);
                            float destY = healTarget.Y + stopDist * MathF.Sin(angle);
                            _navigation.FaceInstant(player, healTarget);
                            _ctm.MoveTo(destX, destY, healTarget.Z, 1.5f);
                        }
                    }
                }
                else
                {
                    // Вне боя или не авто: follow к мастеру
                    bool healerFollow = Hivemind.Mode == Hivemind.SlaveMode.Following ||
                        Hivemind.Mode == Hivemind.SlaveMode.GoingToPoint ||
                        (inAutoMode && !Hivemind.AutoPauseFollow);
                    if (healerFollow)
                    {
                        SlaveCtrl.FollowDistance = _followDistance;
                        SlaveCtrl.Tick();
                    }
                }

                // Хил ВСЕГДА — каждые ~500мс
                _healerTickCount++;
                if (_healerTickCount >= 3)
                {
                    _healerTickCount = 0;
                    string dangerCheck = "if WB_STOP_CAST and GetTime()<WB_STOP_CAST then SpellStopCasting() return end ";
                    string script = dangerCheck + enemyCountLua + SpellFlagsLua + _fullScript;
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
            if (_logTick >= 33) { _logTick = 0; var t = _objectManager.GetTarget(); var p = _objectManager.LocalPlayer; Logger.Info($"Tick: rot={_rotationEnabled} follow={_followEnabled} buffs={_buffsEnabled} target={t?.Name ?? "none"} alive={t?.IsAlive} NCE={CountNearbyCombatEnemies(p!)} dyn={_objectManager.DynObjects.Count} flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 200))}\""); if (t != null && p != null) Logger.Info($"Hitbox: player BR={p.BoundingRadius:F2} CR={p.CombatReach:F2} | target BR={t.BoundingRadius:F2} CR={t.CombatReach:F2} dist={p.DistanceTo(t):F2} name={t.Name}"); }

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

    /// <summary>Строка текущих настроек бафов для Register</summary>
    public string GetBuffSettingsString()
    {
        var parts = new List<string>();
        if (PlayerClass == "SHAMAN")
        {
            if (!string.IsNullOrEmpty(SelectedTotemEarth)) parts.Add($"tE={SelectedTotemEarth}");
            if (!string.IsNullOrEmpty(SelectedTotemFire)) parts.Add($"tF={SelectedTotemFire}");
            if (!string.IsNullOrEmpty(SelectedTotemWater)) parts.Add($"tW={SelectedTotemWater}");
            if (!string.IsNullOrEmpty(SelectedTotemAir)) parts.Add($"tA={SelectedTotemAir}");
        }
        if (PlayerClass == "PALADIN")
        {
            if (!string.IsNullOrEmpty(SelectedBlessing)) parts.Add($"bl={SelectedBlessing}");
            if (!string.IsNullOrEmpty(SelectedAura)) parts.Add($"au={SelectedAura}");
        }
        return string.Join(",", parts);
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
