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

    private bool _followEnabled;
    private bool _rotationEnabled;
    private bool _autoFace = true;
    private bool _autoSelectTarget;
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
    public string SelectedSeal { get; set; } = "SoV";
    public string SelectedBlessing { get; set; } = "BoM";
    public string SelectedAura { get; set; } = "AuRet";
    public bool IsHealer { get; set; }
    public string PlayerClass { get; set; } = "";

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

            // === БАФФЫ (каждые ~3 сек, вне боя) ===
            if (_buffsEnabled && (_enabledBuffs.Count > 0 || !string.IsNullOrEmpty(SelectedSeal) || !string.IsNullOrEmpty(SelectedBlessing) || !string.IsNullOrEmpty(SelectedAura)))
            {
                _buffCheckTick++;
                if (_buffCheckTick >= 20)
                {
                    _buffCheckTick = 0;
                    if (!player.IsCasting)
                    {
                        string buffScript = BuildBuffScript();
                        if (!string.IsNullOrEmpty(buffScript))
                        {
                            Logger.Info($"ExecBuffs: len={buffScript.Length} seal={SelectedSeal}");
                            _hook.ExecuteLua(buffScript, 500);
                            return; // Не выполняем ротацию на этом тике
                        }
                    }
                }
            }

            // Слейв в режиме атаки — выполняем ротацию
            if (Hivemind.CurrentRole == Hivemind.Role.Slave && Hivemind.WantRotation)
            {
                var slaveTarget = _objectManager.GetTarget();
                if (slaveTarget != null && slaveTarget.IsAlive)
                {
                    if (_autoFace) _navigation.FaceUnit(player, slaveTarget);
                    string script = SpellFlagsLua + _fullScript;
                    _hook.ExecuteLua(script, 500);
                }
                return; // Слейв в атаке — не нужен обычный follow/rotation
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
                    string script = SpellFlagsLua + GetRotationScript(player);
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
                    _hook.ExecuteLua(SpellFlagsLua + _instantScript, 300);
            }
            else
            {
                // СТОИМ — полная ротация (CTM сам остановился)

                if (hasTarget)
                {
                    if (_autoFace) _navigation.FaceUnit(player, target!);
                    string script = SpellFlagsLua + GetRotationScript(player);
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

    private string BuildBuffScript()
    {
        // Аура/печать/благословение кастуются даже если нет обычных баффов
        if (_enabledBuffs.Count == 0 && string.IsNullOrEmpty(SelectedSeal) && string.IsNullOrEmpty(SelectedBlessing) && string.IsNullOrEmpty(SelectedAura)) return "";

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
                "AuDev" => "Аура воина Света",
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
                _ => ("", "")
            };
            if (!string.IsNullOrEmpty(blessSpell))
            {
                var bs = blessSpell.Replace("'", "\\'");
                var gs = greatSpell.Replace("'", "\\'");
                sb.Append($"if not HasB('player','{bs}') and not HasB('player','{gs}') then if GetItemCount('Знак королей')>0 then CastSpellByName('{gs}') else CastSpellByName('{bs}') end return end ");
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
