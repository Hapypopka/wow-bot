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
    private bool _rotationEnabled;
    private bool _autoFace = true;
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
            else if (!_followEnabled && !_rotationEnabled) StopTimer();
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
    public string SelectedSeal { get; set; } = "";
    public string SelectedBlessing { get; set; } = "BoM";
    public string SelectedAura { get; set; } = "AuRet";
    public string SelectedShout { get; set; } = "";
    public string SelectedStance { get; set; } = "";
    public string SelectedPresence { get; set; } = "";
    public string SelectedFeralForm { get; set; } = "";
    public bool IsHealer { get; set; }
    public string PlayerClass { get; set; } = "";
    private bool _isApproaching;
    private float _lastApproachX, _lastApproachY;
    private int _approachRetryTick;
    private string? _specName;
    public string? SpecName { get => _specName; set => _specName = value; }

    // Mana thresholds (из оверлея, в процентах 0-100)
    public int DispManaThreshold { get; set; } = 15;
    public int SFManaThreshold { get; set; } = 50;

    public event Action<string>? OnStatusChanged;

    // Hivemind (мультибоксинг)
    public Hivemind Hivemind { get; private set; }
    public double LastHiveCheck { get; set; }
    // _slaveListenerInstalled хранится в Hivemind

    public BotEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation, ClickToMove ctm)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
        _ctm = ctm;
        Hivemind = new Hivemind(hook, objectManager, navigation, ctm);
        Hivemind.SetBotEngine(this);
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
        _hook.ExecuteLua("MoveForwardStop()", 100);
    }

    // --- Toggle ---

    public void ToggleFollow()
    {
        _followEnabled = !_followEnabled;
        if (_followEnabled) EnsureRunning();
        else if (!_rotationEnabled && !_buffsEnabled) StopTimer();
        if (!_followEnabled)
            _hook.ExecuteLua("MoveForwardStop()", 100);
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void ToggleRotation()
    {
        _rotationEnabled = !_rotationEnabled;
        Logger.Info($"ToggleRotation: {_rotationEnabled}, follow={_followEnabled}, buffs={_buffsEnabled}");
        if (_rotationEnabled) EnsureRunning();
        else if (!_followEnabled && !_buffsEnabled) StopTimer();
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void StopAll()
    {
        _followEnabled = false;
        _rotationEnabled = false;
        StopTimer();
        _ctm.Stop();
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
    private int _hiveCheckTick;

    private void Tick(object? state)
    {
        if (!_followEnabled && !_rotationEnabled && !_buffsEnabled && !Hivemind.IsActive) return;
        if (!_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

            // Считаем мобов рядом для AoE (Залп охотника и т.п.)
            int nearbyEnemies = CountNearbyEnemies(player);
            string enemyCountLua = $"WB_NE={nearbyEnemies} ";

            // === HIVEMIND SLAVE: слушаем команды мастера ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave)
            {
                // Устанавливаем слушатель (один раз)
                if (!Hivemind._slaveListenerInstalled)
                {
                    _hook.ExecuteLua(Game.Hivemind.GetSlaveListenerScript(), 500);
                    Hivemind._slaveListenerInstalled = true;
                    Logger.Info("Hivemind: slave listener installed");
                }

                // Проверяем новые команды каждые 500мс
                _hiveCheckTick++;
                if (_hiveCheckTick >= 3 && LuaReader != null && LuaReader.IsInitialized)
                {
                    _hiveCheckTick = 0;
                    string checkLua = Hivemind.GetSlaveReadScript();
                    string? response = LuaReader.Execute(checkLua);
                    if (response != null)
                    {
                        var (cmd, arg, sender, time) = Hivemind.ParseSlaveResponse(response);
                        if (cmd != null && time > LastHiveCheck)
                        {
                            LastHiveCheck = time;
                            Logger.Info($"Hivemind: received {cmd} from {sender} arg={arg}");
                            Hivemind.ExecuteSlaveCommand(cmd.Value, arg);
                        }
                    }
                }
            }

            // === HIVEMIND: постоянный follow за мастером ===
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && Hivemind.IsFollowing)
            {
                Hivemind.SlaveTickFollow();
            }

            // Лог каждые ~5 сек (33 тиков по 150мс)
            _logTick++;
            if (_logTick >= 33) { _logTick = 0; var t = _objectManager.GetTarget(); Logger.Info($"Tick: rot={_rotationEnabled} follow={_followEnabled} buffs={_buffsEnabled} target={t?.Name ?? "none"} alive={t?.IsAlive} flags=\"{SpellFlagsLua?.Substring(0, Math.Min(SpellFlagsLua?.Length ?? 0, 80))}\""); }

            // === БАФФЫ ===
            if (_buffsEnabled && (_enabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) || !string.IsNullOrEmpty(SelectedBlessing) || !string.IsNullOrEmpty(SelectedAura) || !string.IsNullOrEmpty(SelectedShout) || !string.IsNullOrEmpty(SelectedStance) || !string.IsNullOrEmpty(SelectedPresence) || !string.IsNullOrEmpty(SelectedFeralForm)))
            {
                _buffCheckTick++;
                // Классовые баффы (стойка/форма/власть/аура/печать) — каждые 3 тика (~0.5 сек)
                // Обычные баффы (self-buffs) — каждые 20 тиков (~3 сек)
                bool classBuffCheck = _buffCheckTick % 3 == 0;
                bool fullBuffCheck = _buffCheckTick >= 20;
                if (fullBuffCheck) _buffCheckTick = 0;
                if ((classBuffCheck || fullBuffCheck) && !player.IsCasting)
                {
                    string buffScript = fullBuffCheck ? BuildBuffScript() : BuildClassBuffScript();
                    if (!string.IsNullOrEmpty(buffScript))
                    {
                        if (fullBuffCheck) Logger.Info($"ExecBuffs: len={buffScript.Length} seal={SelectedSeal}");
                        _hook.ExecuteLua(buffScript, 500);
                        return; // Не выполняем ротацию на этом тике
                    }
                }
            }

            // Слейв в режиме атаки — выполняем ротацию
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && Hivemind.WantRotation)
            {
                var slaveTarget = _objectManager.GetTarget();
                if (slaveTarget != null && slaveTarget.IsAlive)
                {
                    float distToTarget = player.DistanceTo2D(slaveTarget); // 2D — WoW проверяет спеллы без учёта высоты
                    // Дистанция каста: мили ~5 ярдов, рейндж ~30 ярдов
                    bool isMelee = PlayerClass == "WARRIOR" || PlayerClass == "ROGUE" ||
                                   PlayerClass == "DEATHKNIGHT" ||
                                   (PlayerClass == "PALADIN" && !IsHealer) ||
                                   (PlayerClass == "DRUID" && _specName?.Contains("Feral") == true) ||
                                   (PlayerClass == "SHAMAN" && _specName?.Contains("Enhancement") == true);
                    float castRange = isMelee ? 8f : 30f; // WoW мили спеллы работают ~5-8 ярдов с учётом hitbox

                    float stopDist = isMelee ? 2f : 23f; // Мили — вплотную, рейндж — на 23 ярда

                    if (distToTarget > castRange)
                    {
                        // Далеко — повернуться и дать CTM
                        if (_autoFace) _navigation.FaceUnit(player, slaveTarget);
                        // Повторяем CTM каждые ~10 тиков (1.5 сек) пока далеко, или если таргет сдвинулся
                        _approachRetryTick++;
                        float dx = slaveTarget.X - _lastApproachX;
                        float dy = slaveTarget.Y - _lastApproachY;
                        float targetMoved = MathF.Sqrt(dx * dx + dy * dy);
                        if (!_isApproaching || targetMoved > 5f || _approachRetryTick >= 10)
                        {
                            _approachRetryTick = 0;
                            // Бежим прямо к таргету (stopDist от цели)
                            float dirX = player.X - slaveTarget.X;
                            float dirY = player.Y - slaveTarget.Y;
                            float dirLen = MathF.Sqrt(dirX * dirX + dirY * dirY);
                            if (dirLen > 0.1f)
                            {
                                dirX /= dirLen;
                                dirY /= dirLen;
                            }
                            float goalX = slaveTarget.X + dirX * stopDist;
                            float goalY = slaveTarget.Y + dirY * stopDist;
                            float goalZ = slaveTarget.Z;
                            _ctm.MoveTo(goalX, goalY, goalZ, 0.5f);
                            _lastApproachX = slaveTarget.X;
                            _lastApproachY = slaveTarget.Y;
                            _isApproaching = true;
                            Logger.Info($"Hivemind: CTM approach dist={distToTarget:F1} goal=({goalX:F1},{goalY:F1})");
                        }
                    }
                    else
                    {
                        // В дистанции — бьём без проверки комбата
                        _isApproaching = false;
                        if (_autoFace) _navigation.FaceUnit(player, slaveTarget);
                        // Слейв: пропускаем PreChecks (комбат-проверку) — кастуем сразу
                        string script = enemyCountLua + SpellFlagsLua + _fullScriptNoCombatCheck;
                        _hook.ExecuteLua(script, 500);
                    }
                }
                return;
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
                if (needsToMove)
                    _ctm.MoveTo(followTarget!.X, followTarget.Y, followTarget.Z, 0.5f);
                // Не вызываем Stop — CTM сам остановится когда дойдёт
                return;
            }

            // === Автовыбор таргета ===
            bool targetTooFar = hasTarget && player.DistanceTo(target!) > _maxTargetRange;
            if (_autoSelectTarget && (!hasTarget || targetTooFar))
            {
                _hook.ExecuteLua("TargetNearestEnemy()", 200);
                return;
            }

            // === ТОЛЬКО ROTATION ===
            if (!_followEnabled && _rotationEnabled)
            {
                if (hasTarget || IsHealer)
                {
                    if (hasTarget && _autoFace && !IsHealer) _navigation.FaceUnit(player, target!);
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
                // КАСТУЕМ — не двигаемся, не трогаем facing, ждём
                _ctm.Stop();
            }
            else if (needsToMove)
            {
                // БЕЖИМ к follow — CTM плавно рулит
                _ctm.MoveTo(followTarget!.X, followTarget.Y, followTarget.Z, 0.5f);

                // Instants на бегу БЕЗ поворота
                if (hasTarget)
                    _hook.ExecuteLua(enemyCountLua + SpellFlagsLua + _instantScript, 300);
            }
            else
            {
                // СТОИМ — полная ротация (CTM сам остановился)

                if (hasTarget)
                {
                    if (_autoFace) _navigation.FaceUnit(player, target!);
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
        { "Дар дикой природы", ("Дикий шиповник", "Знак дикой природы") },
        { "Чародейская гениальность", ("Чародейский порошок", "Чародейский интеллект") },
    };

    /// <summary>Быстрая проверка только классовых баффов (стойка/форма/власть/аура/печать) — каждые 0.5 сек</summary>
    private string BuildClassBuffScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_CB() ");
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

        if (!hasAnything) return "";
        sb.Append("end WB_CB()");
        return sb.ToString();
    }

    private string BuildBuffScript()
    {
        // Аура/печать/благословение кастуются даже если нет обычных баффов
        if (_enabledBuffs.Count == 0 && string.IsNullOrEmpty(SelectedSeal) && string.IsNullOrEmpty(SelectedBlessing) && string.IsNullOrEmpty(SelectedAura) && string.IsNullOrEmpty(SelectedShout) && string.IsNullOrEmpty(SelectedStance) && string.IsNullOrEmpty(SelectedPresence) && string.IsNullOrEmpty(SelectedFeralForm)) return "";

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

        // Благословение паладина (только для PALADIN)
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
                sb.Append($"if not HasB('player','{bs}') and not HasB('player','{gs}') then if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end return end ");
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


        // Self-баффы
        foreach (var buff in selfBuffs)
        {
            var s = buff.Replace("'", "\\'");
            sb.Append($"if not HasB('player','{s}') then CastSpellByName('{s}') return end ");
        }

        // Рейд-баффы: если есть реагент → Prayer, иначе → одиночная версия
        foreach (var buff in raidBuffs)
        {
            var s = buff.Replace("'", "\\'");
            string aliasP = "";
            if (BuffAliases.TryGetValue(buff, out var alias))
            {
                var a = alias.Replace("'", "\\'");
                aliasP = $" and not HasB('player','{a}')";
            }

            if (BuffReagents.TryGetValue(buff, out var reagentInfo))
            {
                var r = reagentInfo.reagent.Replace("'", "\\'");
                var fb = reagentInfo.fallback.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{s}'){aliasP} then if GetItemCount('{r}')>0 then CastSpellByName('{s}') else CastSpellByName('{fb}') end return end ");
            }
            else
            {
                sb.Append($"if not HasB('player','{s}'){aliasP} then CastSpellByName('{s}') return end ");
            }
        }

        sb.Append("end WB_Buff()");
        return sb.ToString();
    }

    public void Dispose()
    {
        StopAll();
    }
}
