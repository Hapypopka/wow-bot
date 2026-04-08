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
        // GUID'ы союзников чтобы определить враждебность
        var friendlyGuids = new HashSet<ulong>();
        friendlyGuids.Add(_objectManager.LocalPlayerGuid);
        foreach (var p in _objectManager.Players)
            friendlyGuids.Add(p.Guid);

        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (!unit.InCombat) continue;
            if (player.DistanceTo(unit) > range) continue;
            // Враждебный = его таргет кто-то из нашей группы (бьёт нас/союзника)
            if (unit.TargetGuid == 0) continue;
            if (!friendlyGuids.Contains(unit.TargetGuid)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Пробуем кастовать ground AoE на таргет (Гроза, Метель и т.д.)</summary>
    /// <returns>true если начали AoE каст</returns>
    private bool TryGroundAoE(Entities.WowPlayer player, Entities.WowUnit target)
    {
        if (!_aoeEnabled) return false;
        if (player.IsCasting) return false;
        // Проверяем channeling — если уже ченнелим (Гроза), не прерываем
        if (player.ChannelingSpellId != 0) return false;

        int nearTarget = CountEnemiesNearTarget(target, 10f);
        if (nearTarget < AoeMinEnemies) return false;

        bool hurricaneEnabled = IsSpellEnabled("Hurricane");
        Logger.Log(LogCat.AoE, $"GroundAoE: class={PlayerClass} spec={_specName} Hurricane={hurricaneEnabled} flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 100))}\"");

        // Определяем AoE спелл по классу
        string? aoeSpell = PlayerClass switch
        {
            "DRUID" when _specName?.Contains("Balance") == true => hurricaneEnabled ? "Гроза" : null,
            "HUNTER" => IsSpellEnabled("Volley") ? "WB_VOLLEY" : null,
            _ => null
        };

        if (aoeSpell == null) { Logger.Log(LogCat.AoE, $"GroundAoE: skip — no spell for class={PlayerClass} spec={_specName}"); return false; }

        // Каст спелла → terrain click на позицию таргета
        if (aoeSpell == "WB_VOLLEY")
            _hook.ExecuteLua("local n=GetSpellInfo(1510) if n then CastSpellByName(n) end", 200);
        else
            _hook.ExecuteLua($"CastSpellByName('{aoeSpell}')", 200);
        System.Threading.Thread.Sleep(100);
        bool ok = _hook.CastTerrainClick(target.X, target.Y, target.Z);
        Logger.Log(LogCat.AoE, $"GroundAoE: {aoeSpell} → ({target.X:F0},{target.Y:F0},{target.Z:F0}) enemies={nearTarget} ok={ok}");
        return ok;
    }

    private bool IsSpellEnabled(string key) => SpellFlagsLua?.Contains($"{key}=true") == true;

    /// <summary>Считаем живых врагов в бою рядом с таргетом (для AoE решений)</summary>
    private int CountEnemiesNearTarget(Entities.WowUnit target, float range = 10f)
    {
        int count = 0;
        var friendlyGuids = new HashSet<ulong>();
        friendlyGuids.Add(_objectManager.LocalPlayerGuid);
        foreach (var p in _objectManager.Players)
            friendlyGuids.Add(p.Guid);
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive) continue;
            if (friendlyGuids.Contains(unit.Guid)) continue;
            // Враждебный: таргетит кого-то из нашей группы
            if (unit.TargetGuid != 0 && !friendlyGuids.Contains(unit.TargetGuid)) continue;
            float dx = unit.X - target.X;
            float dy = unit.Y - target.Y;
            float dz = unit.Z - target.Z;
            if (dx * dx + dy * dy + dz * dz <= range * range)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Умный таунт: ищет моба в ObjectManager который бьёт союзника (не нас).
    /// Переключает таргет на моба и кастует таунт через Lua.
    /// </summary>
    private int _smartTauntTick;
    private bool TrySmartTaunt(Entities.WowPlayer player)
    {
        if (!IsSpellEnabled("AutoTaunt")) return false;
        _smartTauntTick++;
        if (_smartTauntTick < 5) return false; // каждые ~750мс
        _smartTauntTick = 0;

        ulong myGuid = _objectManager.LocalPlayerGuid;

        // Ищем моба который бьёт НЕ нас и в радиусе 30м
        foreach (var unit in _objectManager.Units)
        {
            if (!unit.IsAlive || !unit.InCombat) continue;
            if (unit.TargetGuid == 0 || unit.TargetGuid == myGuid) continue; // бьёт нас или никого
            if (player.DistanceTo(unit) > 30f) continue;

            // Проверяем: бьёт ли моб кого-то из нашей группы?
            bool hitsAlly = false;
            foreach (var p in _objectManager.Players)
            {
                if (p.Guid == myGuid) continue;
                if (p.Guid == unit.TargetGuid) { hitsAlly = true; break; }
            }
            if (!hitsAlly) continue;

            // Нашли моба бьющего союзника — таргетим + таунт
            string mobName = unit.Name.Replace("'", "\\'");
            if (string.IsNullOrEmpty(mobName)) continue;

            // Определяем таунт по классу
            int tauntId = PlayerClass switch
            {
                "WARRIOR" => 355,      // Taunt
                "PALADIN" => 62124,    // Hand of Reckoning
                "DEATHKNIGHT" => 56222, // Dark Command
                "DRUID" => 6795,       // Growl
                _ => 0
            };
            if (tauntId == 0) return false;

            string lua = $"TargetUnit('{mobName}') local n=GetSpellInfo({tauntId}) if n then CastSpellByName(n) end";
            _hook.ExecuteLua(lua, 300);
            Logger.Log(LogCat.Tank, $"SmartTaunt: {mobName} → {tauntId}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// AoE Avoidance через DynObject (вдохновлено AmeisenBotX CombatMovementLeaf).
    /// Сканирует DynObjects из ObjectManager — находит вражеские AoE зоны.
    /// Если стоим в AoE → бежим в противоположную сторону от центра AoE.
    /// </summary>
    private int _aoeTick;
    private DateTime _aoeFleeUntil = DateTime.MinValue;
    public bool IsAoeFleeing => _aoeFleeUntil > DateTime.UtcNow;

    // Безопасные AoE дебафы (слоу/баффы без урона) — не бежим
    private static readonly HashSet<int> SafeAoeDebuffs = new()
    {
        68766, // Осквернение (Desecration) ДК — только слоу 50%, без урона
    };

    private bool TryAoEAvoidance(Entities.WowPlayer player)
    {
        _aoeTick++;
        if (_aoeTick < 3) return false; // каждые ~450мс
        _aoeTick = 0;

        int dynCount = _objectManager.DynObjects.Count;
        if (dynCount == 0) return false;

        // Дебафы на игроке — если SpellId дебафа совпадает с DynObject SpellId → лужа вражеская
        var playerDebuffs = new HashSet<int>(player.GetAuraSpellIds());

        // Ищем DynObject рядом, чей SpellId совпадает с дебафом на нас
        float sumX = 0, sumY = 0;
        int count = 0;
        float maxRadius = 0;

        foreach (var dyn in _objectManager.DynObjects)
        {
            // Ключевая проверка: есть ли дебаф от этого спелла на нас?
            if (!playerDebuffs.Contains(dyn.SpellId)) continue;
            // Белый список: безопасные AoE (слоу без урона)
            if (SafeAoeDebuffs.Contains(dyn.SpellId)) continue;

            // Проверяем: стоим ли внутри AoE (distance < radius + 2)
            float dx = player.X - dyn.X;
            float dy = player.Y - dyn.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist < dyn.Radius + 2f)
            {
                sumX += dyn.X;
                sumY += dyn.Y;
                count++;
                if (dyn.Radius > maxRadius) maxRadius = dyn.Radius;
                Logger.Log(LogCat.AoE, $"AoE HOSTILE: spell={dyn.SpellId} r={dyn.Radius:F1} dist={dist:F1} pos=({dyn.X:F0},{dyn.Y:F0})");
            }
        }

        if (count == 0) return false;

        // Бежим ОТ ЦЕНТРА лужи на radius + 5 ярдов
        float meanX = sumX / count;
        float meanY = sumY / count;
        float escapeAngle = MathF.Atan2(player.Y - meanY, player.X - meanX);
        float escapeDist = maxRadius + 5f;
        float fleeX = meanX + escapeDist * MathF.Cos(escapeAngle);
        float fleeY = meanY + escapeDist * MathF.Sin(escapeAngle);

        // Останавливаем approach перед flee
        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
        _ctm.MoveTo(fleeX, fleeY, player.Z, 1.0f);
        _aoeFleeUntil = DateTime.UtcNow.AddSeconds(2);
        Logger.Log(LogCat.AoE, $"AoE Avoidance: {count} hostile DynObj, maxR={maxRadius:F1} flee=({fleeX:F0},{fleeY:F0}) escapeDist={escapeDist:F1}");
        Logger.Log(LogCat.AoE, $"AoE Avoidance: {count} DynObj, maxR={maxRadius:F1} flee=({fleeX:F0},{fleeY:F0}) escapeDist={escapeDist:F1}");
        return true;
    }

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
    private int _autoPveTick;
    public string SelectedTotemEarth { get; set; } = "";
    public string SelectedTotemFire { get; set; } = "";
    public string SelectedTotemWater { get; set; } = "";
    public string SelectedTotemAir { get; set; } = "";
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
        _combatPositioning = new CombatPositioning(ctm);
    }

    private readonly CombatPositioning _combatPositioning;

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

    /// <summary>Слейв: подбег к таргету + ротация. Используется в Attacking и Auto.</summary>
    private bool _slaveApproaching;

    private void SlaveAttackTick(Entities.WowPlayer player, string enemyCountLua)
    {
        // AoE Avoidance (DynObject — стандартные серверы)
        if (AoeAvoidEnabled && IsAoeFleeing) return;
        if (AoeAvoidEnabled && TryAoEAvoidance(player)) return;

        var slaveTarget = _objectManager.GetTarget();
        if (slaveTarget == null || !slaveTarget.IsAlive || !slaveTarget.InCombat)
        {
            // Нет таргета — остановить подход
            if (_slaveApproaching) { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); _slaveApproaching = false; }
            return;
        }

        // Поворот к таргету (C# запись facing — не конфликтует с Lua MoveBackward)
        _navigation.FaceInstant(player, slaveTarget);

        if (player.IsCasting) return;

        bool isMelee = PlayerClass == "WARRIOR" || PlayerClass == "ROGUE" ||
                       PlayerClass == "DEATHKNIGHT" ||
                       (PlayerClass == "PALADIN" && !IsHealer) ||
                       (PlayerClass == "DRUID" && _specName?.Contains("Feral") == true) ||
                       (PlayerClass == "SHAMAN" && _specName?.Contains("Enhancement") == true);

        // Approach через Lua (НЕ C# CTM!) — так AoE flee из PreChecksDPS может заблокировать.
        // CTM перезаписывал MoveBackward каждые 150мс, теперь движение полностью в Lua.
        // CheckInteractDistance: 3 = ~10yd (melee), 1 = ~28yd (ranged)
        int distId = isMelee ? 3 : 1;
        string approachLua =
            $"if not CheckInteractDistance('target',{distId}) then MoveForwardStart() WB_FWD=1 " +
            $"elseif WB_FWD then MoveForwardStop() WB_FWD=nil end ";

        _slaveApproaching = true;
        string script = approachLua + enemyCountLua + SpellFlagsLua + _fullScriptNoCombatCheck;
        _hook.ExecuteLua(script, 500);
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
        _aoeFleeUntil = DateTime.MinValue;
        _slaveApproaching = false;
        try { _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50); } catch { }
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

            // === HIVEMIND: слушаем addon messages (и мастер, и слейв) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave || Hivemind.CurrentRole == Hivemind.Role.Master)
            {
                // Устанавливаем слушатель (один раз)
                if (!Hivemind._slaveListenerInstalled)
                {
                    _hook.ExecuteLua(Game.Hivemind.GetSlaveListenerScript(), 500);
                    Hivemind._slaveListenerInstalled = true;
                    Logger.Info("Hivemind: slave listener installed");
                    // Найти адрес макроса #2 для прямого чтения
                    if (_hiveMacroAddr == 0 && LuaReader != null && LuaReader.IsInitialized)
                    {
                        System.Threading.Thread.Sleep(300);
                        _hiveMacroAddr = FindHiveMacroAddr();
                        Logger.Log(LogCat.Hivemind, $"Hivemind: macro#2 addr=0x{_hiveMacroAddr:X8}");
                    }
                }

                // Читаем команды через Lua C API (без макросов!)
                _hiveCheckTick++;
                if (_hiveCheckTick >= 3 && !Hivemind.GossipReading)
                {
                    _hiveCheckTick = 0;

                    // Команды мастера (для слейвов)
                    string checkLua = Hivemind.GetSlaveReadScript();
                    string? response = _hook.ExecuteLuaWithResult(checkLua);
                    if (_logTick == 0) Logger.Log(LogCat.Hivemind, $"Hivemind: raw response=[{response ?? "NULL"}]");
                    if (response != null)
                    {
                        var (cmd, arg, sender, time) = Hivemind.ParseSlaveResponse(response);
                        // Выполнять если: новый таймстамп ИЛИ другая команда с тем же таймстампом
                        bool isNew = time > LastHiveCheck || (time == LastHiveCheck && cmd != _lastHiveCmd);
                        if (cmd != null && isNew)
                        {
                            LastHiveCheck = time;
                            _lastHiveCmd = cmd;
                            // Слейв: игнорировать команды от чужих мастеров
                            if (Hivemind.CurrentRole == Hivemind.Role.Slave &&
                                !string.IsNullOrEmpty(Hivemind.MasterName) &&
                                !string.IsNullOrEmpty(sender) &&
                                sender != Hivemind.MasterName)
                            {
                                if (_logTick == 0) Logger.Log(LogCat.Hivemind, $"Hivemind: IGNORED {cmd} from {sender} (my master={Hivemind.MasterName})");
                                // не выполняем
                            }
                            else
                            {
                                Logger.Log(LogCat.Hivemind, $"Hivemind: received {cmd} from {sender} arg={arg} time={time}");
                                Hivemind.ExecuteSlaveCommand(cmd.Value, arg);
                            }
                        }
                    }

                    // Register + ACK от слейвов (для мастера)
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

                        // ACK чтение
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
                                    // Парсим "5~Забудь"
                                    var ap = ackParts[0].Split('~', 2);
                                    if (ap.Length == 2 && int.TryParse(ap[0], out int ackSeq))
                                        Hivemind.ReceiveAck(ackSeq, ap[1]);
                                }
                            }
                        }

                        // ACK retry тик
                        Hivemind.TickAck();
                    }
                }
            }

            // === HIVEMIND SLAVE: периодический Register (каждые ~10 сек) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave)
            {
                _registerTick++;
                if (_registerTick >= 66) // 66 * 150мс ≈ 10 сек
                {
                    _registerTick = 0;
                    Hivemind.SendRegister(PlayerClass);
                }
            }

            // === HIVEMIND MASTER: Ctrl+ПКМ → направить слейвов ===
            if (Hivemind.CurrentRole == Hivemind.Role.Master)
            {
                Hivemind.MasterTick();
            }

            // === БАФФЫ ДЛЯ СЛЕЙВОВ (до return из слейв-блоков) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && _buffsEnabled &&
                (_enabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) || !string.IsNullOrEmpty(SelectedBlessing) ||
                 !string.IsNullOrEmpty(SelectedAura) || !string.IsNullOrEmpty(SelectedShout) || !string.IsNullOrEmpty(SelectedStance) ||
                 !string.IsNullOrEmpty(SelectedPresence) || !string.IsNullOrEmpty(SelectedFeralForm) ||
                 !string.IsNullOrEmpty(SelectedTotemEarth) || !string.IsNullOrEmpty(SelectedTotemFire) ||
                 !string.IsNullOrEmpty(SelectedTotemWater) || !string.IsNullOrEmpty(SelectedTotemAir)))
            {
                _buffCheckTick++;
                bool classBuffCheck = _buffCheckTick % 3 == 0;
                bool fullBuffCheck = _buffCheckTick >= 20;
                if (fullBuffCheck) _buffCheckTick = 0;
                if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
                {
                    string buffScript = fullBuffCheck ? BuildBuffScript() : BuildClassBuffScript();
                    if (!string.IsNullOrEmpty(buffScript))
                    {
                        _hook.ExecuteLua(buffScript, 500);
                    }
                }
            }

            // === HIVEMIND SLAVE: хилер всегда хилит (если не Wipe) ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && IsHealer && !Hivemind.WipeMode)
            {
                // Follow к мастеру если есть команда follow/auto (и follow не на паузе)
                bool healerFollow = Hivemind.Mode == Hivemind.SlaveMode.Following ||
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

                // Если вышли из Attacking — остановить Lua approach
                if (Hivemind.Mode != Hivemind.SlaveMode.Attacking && _slaveApproaching)
                {
                    _hook.ExecuteLua("MoveForwardStop() WB_FWD=nil", 50);
                    _slaveApproaching = false;
                }

                switch (Hivemind.Mode)
                {
                    case Hivemind.SlaveMode.Following:
                        // Ко мне — SlaveCtrl рулит движением
                        SlaveCtrl.Tick();
                        var fTarget = _objectManager.GetTarget();
                        bool fHasTarget = fTarget != null && fTarget.IsAlive && fTarget.Type != WowObjectType.Player && fTarget.InCombat;
                        bool standing = _navigation.IsPlayerStanding(player);
                        if (!standing)
                        {
                            // Бежим — только instants, без поворота
                            if (fHasTarget)
                                _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
                        }
                        else
                        {
                            // Добежали — поворот + полная ротация
                            Logger.Info($"HiveFollow ARRIVED: fHasTarget={fHasTarget} target={fTarget?.Name} alive={fTarget?.IsAlive} combat={fTarget?.InCombat}");
                            if (fHasTarget)
                            {
                                if (TryGroundAoE(player, fTarget!)) { }
                                else if (_autoFace && !_navigation.FaceInstant(player, fTarget!)) { /* поворачиваемся */ }
                                else _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _fullScript, 500);
                            }
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
                            // Авто-ассист (если атака не на паузе)
                            if (!Hivemind.AutoPauseAttack)
                                Hivemind.SlaveAutoTick();

                            // Приоритет: сначала follow, потом атака
                            if (!Hivemind.AutoPauseFollow)
                                SlaveCtrl.Tick(); // follow мастера

                            var autoTarget = _objectManager.GetTarget();
                            bool hasAutoTarget = !Hivemind.AutoPauseAttack &&
                                autoTarget != null && autoTarget.IsAlive &&
                                autoTarget.Type != WowObjectType.Player && autoTarget.InCombat;

                            if (hasAutoTarget)
                            {
                                bool autoStanding = _navigation.IsPlayerStanding(player);
                                if (!autoStanding)
                                {
                                    // Бежим за мастером — только инстанты
                                    _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
                                }
                                else
                                {
                                    // Стоим рядом с мастером — полная ротация + поворот к мобу
                                    if (TryGroundAoE(player, autoTarget!)) { }
                                    else if (_autoFace && !_navigation.FaceInstant(player, autoTarget!)) { }
                                    else _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _fullScript, 500);
                                }
                            }
                        }
                        break;

                    case Hivemind.SlaveMode.Idle:
                        // ForceIdle (полный стоп) или ротация выключена — ничего не делать
                        if (Hivemind.ForceIdle || !_rotationEnabled) break;
                        // Обычный Idle — бьём таргет если есть
                        var idleTarget = _objectManager.GetTarget();
                        if (idleTarget != null && idleTarget.IsAlive && idleTarget.Type != WowObjectType.Player && idleTarget.InCombat)
                        {
                            if (TryGroundAoE(player, idleTarget)) { }
                            else if (_navigation.FaceInstant(player, idleTarget))
                            {
                                string idleScript = enemyCountLua + SpellFlagsLua + _fullScript;
                                _hook.ExecuteLua(idleScript, 500);
                            }
                        }
                        break;
                }
                return;
            }


            // Лог каждые ~5 сек (33 тиков по 150мс)
            _logTick++;
            if (_logTick >= 33) { _logTick = 0; var t = _objectManager.GetTarget(); Logger.Info($"Tick: rot={_rotationEnabled} follow={_followEnabled} buffs={_buffsEnabled} target={t?.Name ?? "none"} alive={t?.IsAlive} NCE={CountNearbyCombatEnemies(_objectManager.LocalPlayer!)} dyn={_objectManager.DynObjects.Count} flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 200))}\""); }

            // === БАФФЫ ===
            if (_buffsEnabled && (_enabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) || !string.IsNullOrEmpty(SelectedBlessing) || !string.IsNullOrEmpty(SelectedAura) || !string.IsNullOrEmpty(SelectedShout) || !string.IsNullOrEmpty(SelectedStance) || !string.IsNullOrEmpty(SelectedPresence) || !string.IsNullOrEmpty(SelectedFeralForm)))
            {
                _buffCheckTick++;
                bool classBuffCheck = _buffCheckTick % 3 == 0;
                bool fullBuffCheck = _buffCheckTick >= 20;
                if (fullBuffCheck) _buffCheckTick = 0;
                if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
                {
                    string buffScript = fullBuffCheck ? BuildBuffScript() : BuildClassBuffScript();
                    if (!string.IsNullOrEmpty(buffScript))
                    {
                        if (fullBuffCheck) Logger.Log(LogCat.Buffs, $"ExecBuffs: len={buffScript.Length} seal={SelectedSeal} aura={SelectedAura} bless={SelectedBlessing} script={buffScript.Substring(0, Math.Min(buffScript.Length, 500))}");
                        _hook.ExecuteLua(buffScript, 500);
                        if (fullBuffCheck) _blessingCooldown = 40;
                        // Хилеры: не прерываем — должны хилить после баффов
                        if (!IsHealer)
                            return;
                    }
                }
            }



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
                // Hivemind Follow: SlaveController рулит движением
                if (HivemindFollowing)
                {
                    bool moving = _navigation.IsPlayerMoving(player);
                    var slaveState = SlaveCtrl?.CurrentState;
                    Logger.Info($"HiveFollow: moving={moving} slaveState={slaveState} hasTarget={hasTarget} combat={targetInCombat} cast={player.IsCasting}");
                    if (moving)
                    {
                        // Бежим — только instants на ходу, без поворота
                        if (hasTarget && targetInCombat)
                        {
                            if (_logTick == 0) Logger.Info("HiveFollow: MOVING — instants only");
                            _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
                        }
                    }
                    else
                    {
                        // Стоим (добежали) — поворот + полная ротация
                        if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                        {
                            if (_logTick == 0) Logger.Info("HiveFollow: STOPPED — full rotation + face");
                            if (hasTarget && targetInCombat && _autoFace && !IsHealer && !_navigation.FaceInstant(player, target!)) { }
                            else _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + GetRotationScript(player), 500);
                        }
                    }
                    return;
                }

                // Умный таунт: C# сканирует ObjectManager на мобов бьющих союзников
                if (playerInCombat && !IsHealer)
                    TrySmartTaunt(player);

                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    // Позиционирование в бою (MoveBehind для мили, RangedPos для ранж)
                    _combatPositioning.IsMelee = IsMeleeSpec;
                    _combatPositioning.IsTank = IsTankSpec;
                    _combatPositioning.IsHealer = IsHealer;
                    _combatPositioning.PlayerClass = PlayerClass;
                    _combatPositioning.SpecName = _specName;
                    if (MoveBehindEnabled && hasTarget && targetInCombat && target != null)
                    {
                        if (_combatPositioning.TryMoveBehind(player, target)) { _navigation.FaceInstant(player, target); }
                        else if (_combatPositioning.TryRangedPosition(player, target)) { _navigation.FaceInstant(player, target); }
                    }

                    // Ground AoE (Гроза и т.д.) — если врагов >= порог
                    if (hasTarget && targetInCombat && target != null && TryGroundAoE(player, target)) { }
                    else
                    {
                        bool needFace = hasTarget && targetInCombat && _autoFace && !IsHealer;
                        if (needFace && !_navigation.FaceInstant(player, target!)) { }
                        else
                        {
                            string script = enemyCountLua + SpellFlagsLua + GetRotationScript(player);
                            if (_logTick == 0) Logger.Log(LogCat.Rotation, $"ExecRotation: scriptLen={script.Length} healer={IsHealer}");
                            _hook.ExecuteLua(script, 500);
                        }
                    }
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
                // В дистанции — полная ротация (только в бою)
                if ((hasTarget && targetInCombat) || IsHealer || playerInCombat)
                {
                    // Ground AoE (Гроза и т.д.) — если врагов >= порог
                    if (hasTarget && targetInCombat && target != null && TryGroundAoE(player, target))
                    { /* AoE каст начат, пропускаем обычную ротацию */ }
                    else if (hasTarget && targetInCombat && _autoFace && !_navigation.FaceInstant(player, target!)) { }
                    else
                    {
                        string script = enemyCountLua + SpellFlagsLua + GetRotationScript(player);
                        _hook.ExecuteLua(script, 500);
                    }
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

    // --- Buff system ---

    // Баффы которые кастуются на группу/рейд (через TargetUnit)
    private static readonly HashSet<string> RaidBuffs = new()
    {
        "Молитва стойкости", "Молитва духа", "Молитва защиты от темной магии",
        "Дар дикой природы", "Чародейская гениальность",
    };

    // Баффы-аналоги: рейдовый бафф покрывает одиночный (проверяем оба)
    private static readonly Dictionary<string, string> BuffAliases = new()
    {
        { "Молитва стойкости", "Слово силы: Стойкость" },
        { "Молитва духа", "Божественный дух" },
        { "Молитва защиты от темной магии", "Защита от темной магии" },
        { "Дар дикой природы", "Знак дикой природы" },
        { "Чародейская гениальность", "Чародейский интеллект" },
    };

    // Реагенты для рейд-баффов: prayer → (reagent, fallback single-target spell)
    private static readonly Dictionary<string, (string reagent, string fallback)> BuffReagents = new()
    {
        { "Молитва стойкости", ("Свеча благочестия", "Слово силы: Стойкость") },
        { "Молитва духа", ("Свеча благочестия", "Божественный дух") },
        { "Молитва защиты от темной магии", ("Свеча благочестия", "Защита от темной магии") },
        { "Дар дикой природы", ("Дикий шиполист", "Знак дикой природы") },
        { "Чародейская гениальность", ("Чародейский порошок", "Чародейский интеллект") },
    };

    /// <summary>Быстрая проверка только классовых баффов (стойка/форма/власть/аура/печать) — каждые 0.5 сек</summary>
    private string BuildClassBuffScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_CB() ");
        sb.Append("if IsMounted() then return end ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");

        bool hasAnything = false;

        // Стойка воина
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedStance))
        {
            var (stanceForm, stanceSpell) = SelectedStance switch
            {
                "Battle" => (1, "Боевая стойка"),
                "Defensive" => (2, "Оборонительная стойка"),
                "Berserker" => (3, "Стойка берсерка"),
                _ => (0, "")
            };
            if (stanceForm > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={stanceForm} then CastSpellByName('{stanceSpell}') return end ");
                hasAnything = true;
            }
        }

        // Власть ДК
        if (PlayerClass == "DEATHKNIGHT" && !string.IsNullOrEmpty(SelectedPresence))
        {
            var (presForm, presSpell) = SelectedPresence switch
            {
                "Blood" => (1, "Власть крови"),
                "Frost" => (2, "Власть льда"),
                "Unholy" => (3, "Власть нечестивости"),
                _ => (0, "")
            };
            if (presForm > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={presForm} then CastSpellByName('{presSpell}') return end ");
                hasAnything = true;
            }
        }

        // Форма друида
        if (PlayerClass == "DRUID" && !string.IsNullOrEmpty(SelectedFeralForm))
        {
            var (formId, formSpell) = SelectedFeralForm switch
            {
                "Cat" => (3, "Облик кошки"),
                "Bear" => (1, "Облик лютого медведя"),
                _ => (0, "")
            };
            if (formId > 0)
            {
                sb.Append($"if GetShapeshiftForm()~={formId} then CastSpellByName('{formSpell}') return end ");
                hasAnything = true;
            }
        }

        // Аура паладина
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedAura))
        {
            string auraSpell = SelectedAura switch
            {
                "AuRet" => "Аура воздаяния",
                "AuDev" => "Аура благочестия",
                "AuCru" => "Аура воина Света",
                "AuFrost" => "Аура защиты от магии льда",
                "AuFire" => "Аура защиты от огня",
                "AuShadow" => "Аура защиты от темной магии",
                "AuConc" => "Аура сосредоточенности",
                _ => ""
            };
            if (!string.IsNullOrEmpty(auraSpell))
            {
                sb.Append($"local function HasB(u,n) for i=1,40 do local b=UnitBuff(u,i) if not b then return false end if b==n then return true end end return false end ");
                sb.Append($"if not HasB('player','{auraSpell}') then CastSpellByName('{auraSpell}') return end ");
                hasAnything = true;
            }
        }

        // Печать паладина (быстрая проверка)
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedSeal))
        {
            if (!hasAnything)
                sb.Append("local function HasB(u,n) for i=1,40 do local b=UnitBuff(u,i) if not b then return false end if b==n then return true end end return false end ");
            if (AoeEnabled && AoeSealSwap && (SelectedSeal == "SoV" || SelectedSeal == "SoC"))
            {
                sb.Append("if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') end ");
                sb.Append("else if not HasB('player','Печать мщения') then CastSpellByName('Печать мщения') end end ");
            }
            else
            {
                string sealSpell = SelectedSeal switch
                {
                    "SoV" => "Печать мщения",
                    "SoC" => "Печать повиновения",
                    "SoW" => "Печать мудрости",
                    "SoL" => "Печать Света",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(sealSpell))
                    sb.Append($"if not HasB('player','{sealSpell}') then CastSpellByName('{sealSpell}') return end ");
            }
            hasAnything = true;
        }

        if (!hasAnything) return "";
        sb.Append("end WB_CB()");
        return sb.ToString();
    }

    private string BuildBuffScript()
    {
        // Аура/печать/благословение кастуются даже если нет обычных баффов
        if (_enabledBuffs.Count == 0 && string.IsNullOrEmpty(SelectedSeal) && string.IsNullOrEmpty(SelectedBlessing) && string.IsNullOrEmpty(SelectedAura) && string.IsNullOrEmpty(SelectedShout) && string.IsNullOrEmpty(SelectedStance) && string.IsNullOrEmpty(SelectedPresence) && string.IsNullOrEmpty(SelectedFeralForm) && string.IsNullOrEmpty(SelectedTotemEarth) && string.IsNullOrEmpty(SelectedTotemFire) && string.IsNullOrEmpty(SelectedTotemWater) && string.IsNullOrEmpty(SelectedTotemAir)) return "";

        var selfBuffs = new List<string>();
        var raidBuffs = new List<string>();

        foreach (var buff in _enabledBuffs)
        {
            if (RaidBuffs.Contains(buff))
                raidBuffs.Add(buff);
            else
                selfBuffs.Add(buff);
        }

        // Всё в одну строку — многострочный Lua через AppendLine триггерит taint WeakAuras
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_Buff() ");
        sb.Append("if IsMounted() then return end ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");
        sb.Append("if UnitIsDeadOrGhost('player') then return end ");
        sb.Append("local function HasB(unit,name) for i=1,40 do local n=UnitBuff(unit,i) if not n then return false end if n==name then return true end end return false end ");

        // Аура паладина (только для PALADIN)
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedAura))
        {
            string auraSpell = SelectedAura switch
            {
                "AuRet" => "Аура воздаяния",
                "AuDev" => "Аура благочестия",
                "AuCru" => "Аура воина Света",
                "AuFrost" => "Аура защиты от магии льда",
                "AuFire" => "Аура защиты от огня",
                "AuShadow" => "Аура защиты от темной магии",
                "AuConc" => "Аура сосредоточенности",
                _ => ""
            };
            if (!string.IsNullOrEmpty(auraSpell))
            {
                var au = auraSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{au}') then CastSpellByName('{au}') return end ");
            }
        }

        // Печать паладина (только для PALADIN)
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedSeal))
        {
            // AoE свап: SoV↔SoC при 2+ врагах (только для рет/прот)
            if (AoeEnabled && AoeSealSwap && (SelectedSeal == "SoV" || SelectedSeal == "SoC"))
            {
                sb.Append("if WB_NCE>=2 then if not HasB('player','Печать повиновения') then CastSpellByName('Печать повиновения') return end ");
                sb.Append("else if not HasB('player','Печать мщения') then CastSpellByName('Печать мщения') return end end ");
            }
            else
            {
                string sealSpell = SelectedSeal switch
                {
                    "SoV" => "Печать мщения",
                    "SoC" => "Печать повиновения",
                    "SoW" => "Печать мудрости",
                    "SoL" => "Печать Света",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(sealSpell))
                {
                    var ss = sealSpell.Replace("'", "\\'");
                    sb.Append($"if not HasB('player','{ss}') then CastSpellByName('{ss}') return end ");
                }
            }
        }

        // Благословение паладина (только для PALADIN) — проверяем пати/рейд
        if (PlayerClass == "PALADIN" && !string.IsNullOrEmpty(SelectedBlessing))
        {
            var (blessSpell, greatSpell) = SelectedBlessing switch
            {
                "BoM" => ("Благословение могущества", "Великое благословение могущества"),
                "BoK" => ("Благословение королей", "Великое благословение королей"),
                "BoW" => ("Благословение мудрости", "Великое благословение мудрости"),
                "BoS" => ("Благословение неприкосновенности", "Великое благословение неприкосновенности"),
                _ => ("", "")
            };
            if (!string.IsNullOrEmpty(blessSpell))
            {
                var bs = blessSpell.Replace("'", "\\'");
                var gs = greatSpell.Replace("'", "\\'");
                // Сначала себя (TargetUnit('player') чтобы Великое благословение бафнуло свой класс)
                sb.Append($"if not HasB('player','{bs}') and not HasB('player','{gs}') then TargetUnit('player') if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end ");
                // Потом пати/рейд в радиусе 30 ярдов
                sb.Append($"local nr=GetNumRaidMembers() ");
                sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end ");
                sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{bs}') and not HasB(u,'{gs}') then TargetUnit(u) if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end TargetLastTarget() return end end end ");
            }
        }

        // Крик воина (только для WARRIOR)
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedShout))
        {
            string shoutSpell = SelectedShout switch
            {
                "Battle" => "Боевой крик",
                "Commanding" => "Командирский крик",
                _ => ""
            };
            if (!string.IsNullOrEmpty(shoutSpell))
            {
                var sh = shoutSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{sh}') then CastSpellByName('{sh}') return end ");
            }
        }

        // Стойка воина (через GetShapeshiftForm: 1=боевая, 2=защитная, 3=берсерк)
        if (PlayerClass == "WARRIOR" && !string.IsNullOrEmpty(SelectedStance))
        {
            var (stanceForm, stanceSpell) = SelectedStance switch
            {
                "Battle" => (1, "Боевая стойка"),
                "Defensive" => (2, "Оборонительная стойка"),
                "Berserker" => (3, "Стойка берсерка"),
                _ => (0, "")
            };
            if (stanceForm > 0)
            {
                var st = stanceSpell.Replace("'", "\\'");
                Logger.Info($"BuildBuff: stance={SelectedStance} form={stanceForm} spell={stanceSpell}");
                sb.Append($"if GetShapeshiftForm()~={stanceForm} then CastSpellByName('{st}') return end ");
            }
        }

        // Власть ДК (через GetShapeshiftForm: 1=кровь, 2=лёд, 3=нечестивость)
        if (PlayerClass == "DEATHKNIGHT" && !string.IsNullOrEmpty(SelectedPresence))
        {
            var (presForm, presSpell) = SelectedPresence switch
            {
                "Blood" => (1, "Власть крови"),
                "Frost" => (2, "Власть льда"),
                "Unholy" => (3, "Власть нечестивости"),
                _ => (0, "")
            };
            if (presForm > 0)
            {
                var pr = presSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={presForm} then CastSpellByName('{pr}') return end ");
            }
        }

        // Форма ферал друида (через GetShapeshiftForm: 1=медведь, 3=кот)
        if (PlayerClass == "DRUID" && !string.IsNullOrEmpty(SelectedFeralForm))
        {
            var (formId, formSpell) = SelectedFeralForm switch
            {
                "Cat" => (3, "Облик кошки"),
                "Bear" => (1, "Облик лютого медведя"),
                _ => (0, "")
            };
            if (formId > 0)
            {
                var fs = formSpell.Replace("'", "\\'");
                sb.Append($"if GetShapeshiftForm()~={formId} then CastSpellByName('{fs}') return end ");
            }
        }

        // Тотемы шамана — SetMultiCastSpell + Зов Духов
        if (PlayerClass == "SHAMAN")
        {
            // Spell ID для каждого тотема (max rank 3.3.5a)
            var earthIds = new Dictionary<string, int>
            {
                ["Stoneskin"] = 58753, ["SoE"] = 58643, ["Tremor"] = 8143,
            };
            var fireIds = new Dictionary<string, int>
            {
                ["Flametongue"] = 58656, ["FrostRes"] = 58745,
            };
            var waterIds = new Dictionary<string, int>
            {
                ["ManaSpring"] = 58774, ["HealStream"] = 58757, ["Cleansing"] = 8170, ["FireRes"] = 58739,
            };
            var airIds = new Dictionary<string, int>
            {
                ["WrathOfAir"] = 3738, ["Windfury"] = 8512, ["NatureRes"] = 58749,
            };

            // Слоты Зова Духов: 141=fire, 142=earth, 143=water, 144=air
            Logger.Info($"Totems: fire={SelectedTotemFire} earth={SelectedTotemEarth} water={SelectedTotemWater} air={SelectedTotemAir}");
            bool hasAny = false;
            if (!string.IsNullOrEmpty(SelectedTotemFire) && fireIds.TryGetValue(SelectedTotemFire, out int fireId))
            { sb.Append($"SetMultiCastSpell(141,{fireId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemEarth) && earthIds.TryGetValue(SelectedTotemEarth, out int earthId))
            { sb.Append($"SetMultiCastSpell(142,{earthId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemWater) && waterIds.TryGetValue(SelectedTotemWater, out int waterId))
            { sb.Append($"SetMultiCastSpell(143,{waterId}) "); hasAny = true; }
            if (!string.IsNullOrEmpty(SelectedTotemAir) && airIds.TryGetValue(SelectedTotemAir, out int airId))
            { sb.Append($"SetMultiCastSpell(144,{airId}) "); hasAny = true; }

        }

        // Аспект охотника (авто-переключение по мане: дракондор/гадюка)
        if (PlayerClass == "HUNTER")
        {
            bool hasDragon = selfBuffs.Remove("Дух дракондора");
            bool hasViper = selfBuffs.Remove("Дух гадюки");
            if (hasDragon)
            {
                // Дракондор включён → авто-переключение на гадюку для реген маны
                // Вне боя: гадюка если мана <100%, дракондор если мана полная
                // В бою: гадюка при мане <30%, дракондор при мане >80% (гистерезис)
                sb.Append("local m=UnitMana('player')/UnitManaMax('player') ");
                sb.Append("if not UnitAffectingCombat('player') then ");
                sb.Append("if m<1 and not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
                sb.Append("if m>=1 and not HasB('player','Дух дракондора') then CastSpellByName('Дух дракондора') return end ");
                sb.Append("else ");
                sb.Append("if m<0.3 and not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
                sb.Append("if m>0.8 and not HasB('player','Дух дракондора') then CastSpellByName('Дух дракондора') return end ");
                sb.Append("end ");
                sb.Append("if not HasB('player','Дух дракондора') and not HasB('player','Дух гадюки') then CastSpellByName('Дух дракондора') return end ");
            }
            else if (hasViper)
            {
                sb.Append("if not HasB('player','Дух гадюки') then CastSpellByName('Дух гадюки') return end ");
            }
        }

        // Камень чар (проверка чары на оружии, создание + применение)
        if (selfBuffs.Remove("WB_SPELLSTONE"))
        {
            sb.Append("local hasEnch=GetWeaponEnchantInfo() ");
            sb.Append("if not hasEnch then ");
            sb.Append("if GetItemCount('Могучий камень чар')>0 then UseItemByName('Могучий камень чар') PickupInventoryItem(16) return end ");
            sb.Append("CastSpellByName('Создание камня чар') return end ");
        }

        // Призыв пета варлока (радио-выбор, только вне боя, смена пета → dismiss + призыв)
        if (PlayerClass == "WARLOCK" && !string.IsNullOrEmpty(SelectedPet))
        {
            int petSpellId = SelectedPet switch
            {
                "Felguard" => 30146,
                "Felhunter" => 691,
                "Imp" => 688,
                "Voidwalker" => 697,
                "Succubus" => 712,
                _ => 0
            };
            if (petSpellId != 0)
            {
                sb.Append($"if not UnitAffectingCombat('player') then ");
                sb.Append($"if not UnitExists('pet') then WB_PET=nil local n=GetSpellInfo({petSpellId}) if n then CastSpellByName(n) end return ");
                sb.Append($"end ");
                sb.Append($"if UnitExists('pet') and not WB_PET then WB_PET={petSpellId} end ");
                sb.Append($"if UnitExists('pet') and WB_PET and WB_PET~={petSpellId} then PetDismiss() WB_PET=nil return end ");
                sb.Append($"end ");
            }
        }


        // Призыв пета хантера (через тогл в баффах)
        if (PlayerClass == "HUNTER" && selfBuffs.Remove("WB_HUNTER_PET"))
        {
            sb.Append("if not UnitAffectingCombat('player') then ");
            sb.Append("if not UnitExists('pet') then CastSpellByName('Призыв питомца') return end ");
            sb.Append("if UnitIsDead('pet') then CastSpellByName('Воскрешение питомца') return end ");
            sb.Append("end ");
        }

        // Оружие шамана (проверка через GetWeaponEnchantInfo + смена выбора)
        string? shamanWeapon = null;
        string? shamanWeaponKey = null;
        if (selfBuffs.Remove("WB_WEAPON_FT")) { shamanWeapon = "Оружие языка пламени"; shamanWeaponKey = "FT"; }
        if (selfBuffs.Remove("WB_WEAPON_EL")) { shamanWeapon = "Оружие жизни земли"; shamanWeaponKey = "EL"; }
        if (selfBuffs.Remove("WB_WEAPON_WF")) { shamanWeapon = "Оружие неистовства ветра"; shamanWeaponKey = "WF"; }
        if (shamanWeapon != null)
        {
            sb.Append($"local hasEnch=GetWeaponEnchantInfo() ");
            sb.Append($"if not hasEnch or (WB_WEAPON_LAST and WB_WEAPON_LAST~='{shamanWeaponKey}') then WB_WEAPON_LAST='{shamanWeaponKey}' CastSpellByName('{shamanWeapon}') return end ");
            sb.Append($"if not WB_WEAPON_LAST then WB_WEAPON_LAST='{shamanWeaponKey}' end ");
        }

        // Self-баффы
        foreach (var buff in selfBuffs)
        {
            var s = buff.Replace("'", "\\'");
            sb.Append($"if not HasB('player','{s}') then CastSpellByName('{s}') return end ");
        }

        // Рейд-баффы: проверяем себя + пати/рейд
        foreach (var buff in raidBuffs)
        {
            var s = buff.Replace("'", "\\'");
            string singleBuff = s;
            if (BuffAliases.TryGetValue(buff, out var alias))
                singleBuff = alias.Replace("'", "\\'");

            string reagent = "", fallback = "";
            bool hasReagent = BuffReagents.TryGetValue(buff, out var reagentInfo);
            if (hasReagent)
            {
                reagent = reagentInfo.reagent.Replace("'", "\\'");
                fallback = reagentInfo.fallback.Replace("'", "\\'");
            }

            // Проверить: нужен ли кому бафф? Ищем первого без бафа
            // Себя
            sb.Append($"if not HasB('player','{s}') and not HasB('player','{singleBuff}') then ");
            if (hasReagent)
                sb.Append($"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else CastSpellByName('{fallback}') end ");
            else
                sb.Append($"CastSpellByName('{s}') ");
            sb.Append("return end ");

            // Пати/рейд
            string castLogic = hasReagent
                ? $"if GetItemCount('{reagent}')>0 then CastSpellByName('{s}') else TargetUnit(u) CastSpellByName('{fallback}') TargetLastTarget() end"
                : $"TargetUnit(u) CastSpellByName('{s}') TargetLastTarget()";

            sb.Append($"local nr=GetNumRaidMembers() ");
            sb.Append($"if nr>0 then for i=1,nr do local u='raid'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end ");
            sb.Append($"else for i=1,4 do local u='party'..i if UnitExists(u) and not UnitIsDeadOrGhost(u) and UnitIsConnected(u) and CheckInteractDistance(u,4) and not HasB(u,'{s}') and not HasB(u,'{singleBuff}') then {castLogic} return end end end ");
        }

        sb.Append("end WB_Buff()");
        return sb.ToString();
    }

    public void Dispose()
    {
        StopAll();
    }
}
