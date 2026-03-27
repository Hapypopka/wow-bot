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

    private volatile bool _followEnabled;
    private volatile bool _rotationEnabled;
    private bool _autoFace = true;
    private bool _autoSelectTarget = true;
    private float _maxTargetRange = 30f;
    private ulong _followGuid;
    private float _followDistance = 8f;

    // AoE settings
    private volatile bool _aoeEnabled;
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
    public bool AoeEnabled { get => _aoeEnabled; set { _aoeEnabled = value; _buffBuilder.AoeEnabled = value; } }
    public bool UseMultiDot { get => _useMultiDot; set => _useMultiDot = value; }
    public int MaxDotTargets { get => _maxDotTargets; set => _maxDotTargets = value; }
    public bool UseMindSear { get => _useMindSear; set => _useMindSear = value; }
    public int MindSearTargets { get => _mindSearTargets; set => _mindSearTargets = value; }

    // Buff settings
    private volatile bool _buffsEnabled;
    private readonly BuffScriptBuilder _buffBuilder = new();
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
    public List<string> EnabledBuffs { get => _buffBuilder.EnabledBuffs; set => _buffBuilder.EnabledBuffs = value; }
    public string SpellFlagsLua { get; set; } = "";

    /// Считает живых мобов рядом с игроком (для AoE ротаций, напр. Залп охотника)
    private int CountNearbyEnemies(WowPlayer player, float range = 30f)
    {
        int count = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (player.DistanceTo(unit) > range) continue;
            count++;
        }
        return count;
    }
    private int CountNearbyCombatEnemies(WowPlayer player, float range = 10f)
    {
        int count = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (!unit.InCombat) continue;
            if (player.DistanceTo(unit) > range) continue;
            count++;
        }
        return count;
    }
    public string SelectedSeal { get => _buffBuilder.SelectedSeal; set => _buffBuilder.SelectedSeal = value; }
    public string SelectedBlessing { get => _buffBuilder.SelectedBlessing; set => _buffBuilder.SelectedBlessing = value; }
    public string SelectedAura { get => _buffBuilder.SelectedAura; set => _buffBuilder.SelectedAura = value; }
    public string SelectedShout { get => _buffBuilder.SelectedShout; set => _buffBuilder.SelectedShout = value; }
    public string SelectedStance { get => _buffBuilder.SelectedStance; set => _buffBuilder.SelectedStance = value; }
    public string SelectedPresence { get => _buffBuilder.SelectedPresence; set => _buffBuilder.SelectedPresence = value; }
    public string SelectedFeralForm { get => _buffBuilder.SelectedFeralForm; set => _buffBuilder.SelectedFeralForm = value; }
    public bool AoeSealSwap { get => _buffBuilder.AoeSealSwap; set => _buffBuilder.AoeSealSwap = value; }
    public bool AutoPveEnabled { get; set; } = false;
    public BossTactics BossTactics { get; private set; }
    private int _autoPveTick;
    public string SelectedTotemEarth { get => _buffBuilder.SelectedTotemEarth; set => _buffBuilder.SelectedTotemEarth = value; }
    public string SelectedTotemFire { get => _buffBuilder.SelectedTotemFire; set => _buffBuilder.SelectedTotemFire = value; }
    public string SelectedTotemWater { get => _buffBuilder.SelectedTotemWater; set => _buffBuilder.SelectedTotemWater = value; }
    public string SelectedTotemAir { get => _buffBuilder.SelectedTotemAir; set => _buffBuilder.SelectedTotemAir = value; }
    public bool IsHealer { get; set; }
    public System.Diagnostics.Process? WowProcess { get; set; }
    private string _playerClass = "";
    public string PlayerClass
    {
        get => _playerClass;
        set { _playerClass = value; _buffBuilder.PlayerClass = value; }
    }
    private bool _isMovingForward;
    private int _healerTickCount;
    private int _blessingCooldown; // пропустить N баф-чеков после каста благословения
    private HivemindTickHandler _hivemindHandler;
    private string? _specName;
    public string? SpecName { get => _specName; set => _specName = value; }

    // Mana thresholds (из оверлея, в процентах 0-100)
    public int DispManaThreshold { get; set; } = 15;
    public int SFManaThreshold { get; set; } = 50;

    public event Action<string>? OnStatusChanged;

    // Hivemind (мультибоксинг)
    public Hivemind Hivemind { get; private set; }
    public SlaveController SlaveCtrl { get; private set; }
    public double LastHiveCheck
    {
        get => _hivemindHandler.LastHiveCheck;
        set => _hivemindHandler.LastHiveCheck = value;
    }
    // _slaveListenerInstalled хранится в Hivemind

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
        _hivemindHandler = new HivemindTickHandler(Hivemind, hook, objectManager);
        // Антиафк — всегда пока бот заатачен
        _afkTimer = new Timer(AfkTick, null, 30_000, 30_000); // каждые 30 сек
    }

    private Timer? _afkTimer;
    private void AfkTick(object? state)
    {
        try
        {
            // Снимаем AFK через Lua если стоит
            _hook.ExecuteLua("if UnitIsAFK('player') then SendChatMessage('','AFK') end", 200);
            Logger.Info("AntiAFK: Lua AFK check");
        }
        catch { /* hook может быть занят */ }
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

        Logger.Info($"FollowCTM: dist={dist:F1} stop={stopDistance:F1} player=({player.X:F1},{player.Y:F1}) target=({target.X:F1},{target.Y:F1}) goal=({goalX:F1},{goalY:F1}) ctmIdle={ctmIdle} tgtMoved={targetMoved:F1}");
        _ctm.MoveTo(goalX, goalY, target.Z, 0.5f);
        _lastCtmX = target.X;
        _lastCtmY = target.Y;
        _isFollowMoving = true;
    }

    /// <summary>Слейв: подбег к таргету + ротация. Используется в Attacking и Auto.</summary>
    private void SlaveAttackTick(Entities.WowPlayer player, string enemyCountLua)
    {
        var slaveTarget = _objectManager.GetTarget();
        if (slaveTarget == null || !slaveTarget.IsAlive || !slaveTarget.InCombat) return;

        float distToTarget = player.DistanceTo2D(slaveTarget);
        bool isMelee = PlayerClass == "WARRIOR" || PlayerClass == "ROGUE" ||
                       PlayerClass == "DEATHKNIGHT" ||
                       (PlayerClass == "PALADIN" && !IsHealer) ||
                       (PlayerClass == "DRUID" && _specName?.Contains("Feral") == true) ||
                       (PlayerClass == "SHAMAN" && _specName?.Contains("Enhancement") == true);
        float castRange = isMelee ? 8f : 30f;
        float stopDist = isMelee ? 2f : 23f;

        // Мили: ВСЕГДА бежать к мобу (танк может тащить) + бить если в дистанции
        // Рейндж: бежать если далеко, стоять и кастовать если близко
        if (isMelee)
        {
            // Всегда CTM к мобу
            if (distToTarget > stopDist)
            {
                float dirX = player.X - slaveTarget.X;
                float dirY = player.Y - slaveTarget.Y;
                float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen > 0.1f) { dirX /= dirLen; dirY /= dirLen; }
                _ctm.MoveTo(slaveTarget.X + dirX * stopDist, slaveTarget.Y + dirY * stopDist, slaveTarget.Z, 0.5f);
            }
            // Бить если в дистанции (даже на бегу)
            if (distToTarget <= castRange)
            {
                _navigation.FaceUnit(player, slaveTarget);
                string script = enemyCountLua + SpellFlagsLua + _fullScriptNoCombatCheck;
                _hook.ExecuteLua(script, 500);
            }
        }
        else
        {
            // Рейндж: подбег или каст
            if (distToTarget > castRange)
            {
                float dirX = player.X - slaveTarget.X;
                float dirY = player.Y - slaveTarget.Y;
                float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen > 0.1f) { dirX /= dirLen; dirY /= dirLen; }
                _ctm.MoveTo(slaveTarget.X + dirX * stopDist, slaveTarget.Y + dirY * stopDist, slaveTarget.Z, 0.5f);
            }
            else
            {
                _navigation.FaceUnit(player, slaveTarget);
                string script = enemyCountLua + SpellFlagsLua + _fullScriptNoCombatCheck;
                _hook.ExecuteLua(script, 500);
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
        else if (!_followEnabled && !_buffsEnabled && !Hivemind.IsActive) StopTimer();
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void StopAll()
    {
        _followEnabled = false;
        _rotationEnabled = false;
        StopTimer();
        StopFollowMovement();
        OnStatusChanged?.Invoke("Stopped");
    }

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

    private void Tick(object? state)
    {
        if (!_followEnabled && !_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive) return;
        if (!_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

            // Кэш позиции мастера + авто-рес слейвов
            _hivemindHandler.UpdateMasterPosition(player);
            if (_hivemindHandler.HandleSlaveAutoRes(player)) return;

            // Глобальный декремент кулдаунов
            if (_blessingCooldown > 0) _blessingCooldown--;

            // Считаем мобов рядом для AoE (Залп охотника и т.п.)
            int nearbyEnemies = CountNearbyEnemies(player);
            int combatEnemies = CountNearbyCombatEnemies(player);
            string enemyCountLua = $"WB_NE={nearbyEnemies} WB_NCE={combatEnemies} ";

            // === HIVEMIND: слушатель, команды, register, Ctrl+CTM ===
            _hivemindHandler.Tick(_logTick, LuaReader, PlayerClass);

            // === БАФФЫ ДЛЯ СЛЕЙВОВ (до return из слейв-блоков) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && _buffsEnabled && _buffBuilder.HasAnyBuffSettings())
            {
                _buffCheckTick++;
                bool classBuffCheck = _buffCheckTick % 3 == 0;
                bool fullBuffCheck = _buffCheckTick >= 20;
                if (fullBuffCheck) _buffCheckTick = 0;
                if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
                {
                    string buffScript = fullBuffCheck ? _buffBuilder.BuildBuffScript() : _buffBuilder.BuildClassBuffScript(combatEnemies);
                    if (!string.IsNullOrEmpty(buffScript))
                    {
                        _hook.ExecuteLua(buffScript, 500);
                    }
                }
            }

            // === HIVEMIND SLAVE: хилер всегда хилит (если не Wipe) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && IsHealer && !Hivemind.WipeMode)
            {
                // Follow к мастеру если есть команда follow/auto
                if (Hivemind.Mode == Hivemind.SlaveMode.Following || Hivemind.Mode == Hivemind.SlaveMode.Auto)
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

                switch (Hivemind.Mode)
                {
                    case Hivemind.SlaveMode.Following:
                        // Ко мне — follow + продолжать бить
                        SlaveCtrl.Tick();
                        // Ротация продолжается если есть таргет
                        var fTarget = _objectManager.GetTarget();
                        if (fTarget != null && fTarget.IsAlive && fTarget.Type != WowObjectType.Player && fTarget.InCombat)
                        {
                            _navigation.FaceUnit(player, fTarget);
                            string fScript = enemyCountLua + SpellFlagsLua + _fullScript;
                            _hook.ExecuteLua(fScript, 500);
                        }
                        break;

                    case Hivemind.SlaveMode.Attacking:
                        // AutoPve тактики (если включено и босс найден)
                        BossTactics.IsHealer = IsHealer;
                        BossTactics.IsMelee = PlayerClass is "WARRIOR" or "PALADIN" or "ROGUE" or "DEATHKNIGHT" ||
                            (PlayerClass == "DRUID" && SpecName?.Contains("Feral") == true) ||
                            (PlayerClass == "SHAMAN" && SpecName?.Contains("Enhancement") == true);
                        if (AutoPveEnabled && BossTactics.Tick(player, enemyCountLua, SpellFlagsLua, _fullScript))
                        {
                            // Ротация — босс-тактика управляет подбегом, ротацию кастим
                            string atkScript = enemyCountLua + SpellFlagsLua + _fullScript;
                            _hook.ExecuteLua(atkScript, 500);
                        }
                        else
                        {
                            SlaveAttackTick(player, enemyCountLua);
                        }
                        break;

                    case Hivemind.SlaveMode.Auto:
                        // AutoPve тактики (если включено и босс найден)
                        if (AutoPveEnabled && BossTactics.Tick(player, enemyCountLua, SpellFlagsLua, _fullScript))
                        {
                            string autoScript = enemyCountLua + SpellFlagsLua + _fullScript;
                            _hook.ExecuteLua(autoScript, 500);
                        }
                        else
                        {
                            // Обычный авторежим
                            Hivemind.SlaveAutoTick();
                            var autoTarget = _objectManager.GetTarget();
                            if (autoTarget != null && autoTarget.IsAlive && autoTarget.Type != WowObjectType.Player && autoTarget.InCombat)
                                SlaveAttackTick(player, enemyCountLua);
                            else
                                SlaveCtrl.Tick();
                        }
                        break;

                    case Hivemind.SlaveMode.Idle:
                        // Стоп — не двигаемся, но бьём таргет если есть
                        var idleTarget = _objectManager.GetTarget();
                        if (idleTarget != null && idleTarget.IsAlive && idleTarget.Type != WowObjectType.Player && idleTarget.InCombat)
                        {
                            _navigation.FaceUnit(player, idleTarget);
                            string idleScript = enemyCountLua + SpellFlagsLua + _fullScript;
                            _hook.ExecuteLua(idleScript, 500);
                        }
                        break;
                }
                return;
            }


            // Лог каждые ~5 сек (33 тиков по 150мс)
            _logTick++;
            if (_logTick >= 33) { _logTick = 0; var t = _objectManager.GetTarget(); string totemDbg = ""; try { totemDbg = _hook.ExecuteLuaWithResult("WB_R=tostring(WB_TOTEM_DBG or 'nil')") ?? ""; } catch {} Logger.Info($"Tick: rot={_rotationEnabled} follow={_followEnabled} buffs={_buffsEnabled} target={t?.Name ?? "none"} alive={t?.IsAlive} totem=[{totemDbg}] flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 80))}\""); }

            // === БАФФЫ ===
            if (_buffsEnabled && _buffBuilder.HasAnyBuffSettings())
            {
                _buffCheckTick++;
                bool classBuffCheck = _buffCheckTick % 3 == 0;
                bool fullBuffCheck = _buffCheckTick >= 20;
                if (fullBuffCheck) _buffCheckTick = 0;
                if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
                {
                    string buffScript = fullBuffCheck ? _buffBuilder.BuildBuffScript() : _buffBuilder.BuildClassBuffScript(combatEnemies);
                    if (!string.IsNullOrEmpty(buffScript))
                    {
                        if (fullBuffCheck) Logger.Info($"ExecBuffs: len={buffScript.Length} seal={SelectedSeal}");
                        _hook.ExecuteLua(buffScript, 500);
                        if (fullBuffCheck) _blessingCooldown = 40;
                        // Хилер-слейв: не прерываем — должен хилить после баффов
                        if (!(Hivemind.CurrentRole == Hivemind.Role.Slave && IsHealer))
                            return;
                    }
                }
            }



            if (!_followEnabled && !_rotationEnabled) return;

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
                _hook.ExecuteLua("TargetNearestEnemy()", 200);
                return;
            }

            // === ТОЛЬКО ROTATION ===
            bool targetInCombat = hasTarget && target!.InCombat;
            if (!_followEnabled && _rotationEnabled)
            {
                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    if (hasTarget && targetInCombat && _autoFace && !IsHealer) _navigation.FaceUnit(player, target!);
                    string script = enemyCountLua + SpellFlagsLua + GetRotationScript(player);
                    if (_logTick == 0) Logger.Info($"ExecRotation: scriptLen={script.Length} healer={IsHealer}");
                    _hook.ExecuteLua(script, 500);
                }
                return;
            }

            // === ОБА: FOLLOW + ROTATION ===

            bool isCasting = player.IsCasting;

            if (isCasting)
            {
                // КАСТУЕМ — стоп движения
                _ctm.Stop();
            }
            else if (needsToMove)
            {
                // БЕЖИМ к follow — CTM к координатам цели
                _ctm.MoveTo(followTarget!.X, followTarget.Y, followTarget.Z, _followDistance);

                // Instants на бегу БЕЗ поворота (только в бою)
                if (hasTarget && targetInCombat)
                    _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
            }
            else
            {
                // В дистанции — полная ротация (только в бою)
                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    if (hasTarget && targetInCombat && _autoFace) _navigation.FaceUnit(player, target!);
                    string script = enemyCountLua + SpellFlagsLua + GetRotationScript(player);
                    _hook.ExecuteLua(script, 500);
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
    private string GetRotationScript(WowPlayer player)
    {
        if (!_aoeEnabled || !_useMultiDot)
            return _fullScript;

        var mainTarget = _objectManager.GetTarget();
        if (mainTarget == null || !mainTarget.IsAlive)
            return _fullScript;

        // Собираем уникальные имена мобов рядом (кроме основного таргета)
        var nearbyNames = new List<string>();
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (unit.Guid == mainTarget.Guid) continue;
            if (player.DistanceTo(unit) > 30f) continue;

            string name = unit.Name;
            if (string.IsNullOrEmpty(name)) continue;
            if (!nearbyNames.Contains(name))
                nearbyNames.Add(name);
            if (nearbyNames.Count >= _maxDotTargets)
                break;
        }

        if (nearbyNames.Count == 0)
            return _fullScript;

        // Считаем мобов в 10м от ТАРГЕТА (для Mind Sear)
        int unitsNearTarget = 0;
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (unit.Guid == mainTarget.Guid) continue;
            if (mainTarget.DistanceTo(unit) <= 10f)
                unitsNearTarget++;
        }

        // Формируем Lua-массив имён мобов
        var luaNames = string.Join(",", nearbyNames.Select(n => "'" + n.Replace("'", "\\'") + "'"));
        int mindSearCount = _useMindSear ? _mindSearTargets : 999;
        float dispThreshold = DispManaThreshold / 100f;
        float sfThreshold = SFManaThreshold / 100f;

        // Lua скрипт: полная SP ротация + мультидот
        return @"
local function WB_AoE()
    if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
    -- GCD check (SP-only AoE script, VT всегда в спеллбуке)
    local gS,gD = GetSpellCooldown('Прикосновение вампира')
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
    if not UnitAffectingCombat('player') then return end
    if not UnitExists('target') or UnitIsDeadOrGhost('target') then return end
    if not UnitCanAttack('player','target') then return end

    local function IsReady(name)
        local s,d=GetSpellCooldown(name)
        return s~=nil and s==0
    end
    local function HasBuff(name)
        for i=1,40 do
            local n=UnitBuff('player',i)
            if not n then return false end
            if n==name then return true end
        end
        return false
    end
    local function HasDebuff(unit,name)
        for i=1,40 do
            local n=UnitDebuff(unit,i)
            if not n then return false end
            if n==name then return true end
        end
        return false
    end
    local function MP()
        local m,mm=UnitMana('player'),UnitManaMax('player')
        if mm==0 then return 1 end
        return m/mm
    end

    -- Shadowform
    if not HasBuff('Облик Тьмы') then CastSpellByName('Облик Тьмы') return end

    -- Dispersion (по порогу маны)
    if MP() < " + dispThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" and IsReady('Слияние с Тьмой') then CastSpellByName('Слияние с Тьмой') return end

    -- Shadowfiend (по порогу маны)
    if MP() < " + sfThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" and IsReady('Исчадие Тьмы') then CastSpellByName('Исчадие Тьмы') return end

    -- Доты на основной таргет (защита от double-cast: пропускаем если кастили < 2 сек назад)
    if not HasDebuff('target','Прикосновение вампира') then if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') return end end
    if not HasDebuff('target','Всепожирающая чума') then if not WB_DP or GetTime()-WB_DP>2 then WB_DP=GetTime() CastSpellByName('Всепожирающая чума') return end end
    if not HasDebuff('target','Слово Тьмы: Боль') then if not WB_SWP or GetTime()-WB_SWP>2 then WB_SWP=GetTime() CastSpellByName('Слово Тьмы: Боль') return end end

    -- Основной задотан — мультидот VT на других
    local mainGUID=UnitGUID('target')
    local casted=false
    local mobs={" + luaNames + @"}
    for _,name in ipairs(mobs) do
        TargetUnit(name)
        if UnitGUID('target')~=mainGUID and UnitExists('target') and not UnitIsDeadOrGhost('target') and UnitCanAttack('player','target') and not HasDebuff('target','Прикосновение вампира') then
            if not WB_VT or GetTime()-WB_VT>2 then WB_VT=GetTime() CastSpellByName('Прикосновение вампира') casted=true end
        end
        -- Всегда возвращаем таргет
        if UnitGUID('target')~=mainGUID then TargetLastTarget() end
        if casted then return end
    end
    -- Финальная проверка
    if UnitGUID('target')~=mainGUID then TargetLastTarget() end

    -- Все задотаны — Mind Blast / Mind Sear / Mind Flay
    local _,_,_,_,mbPts = GetTalentInfo(3,8)
    if mbPts and mbPts > 0 and IsReady('Взрыв разума') then CastSpellByName('Взрыв разума') return end
    if " + unitsNearTarget + @" >= " + mindSearCount + @" then CastSpellByName('Иссушение разума') return end
    CastSpellByName('Пытка разума')
end
WB_AoE()
";
    }

    // --- Buff system (делегирован в BuffScriptBuilder) ---

    public void Dispose()
    {
        StopAll();
        _afkTimer?.Dispose();
        _afkTimer = null;
    }
}
