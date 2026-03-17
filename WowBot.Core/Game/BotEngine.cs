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

    // Mana thresholds (из оверлея, в процентах 0-100)
    public int DispManaThreshold { get; set; } = 15;
    public int SFManaThreshold { get; set; } = 50;

    public event Action<string>? OnStatusChanged;

    public BotEngine(EndSceneHook hook, ObjectManager objectManager, Navigation navigation, ClickToMove ctm)
    {
        _hook = hook;
        _objectManager = objectManager;
        _navigation = navigation;
        _ctm = ctm;
    }

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

    // --- Toggle ---

    public void ToggleFollow()
    {
        _followEnabled = !_followEnabled;
        if (_followEnabled) EnsureRunning();
        else if (!_rotationEnabled) StopTimer();
        if (!_followEnabled)
            _hook.ExecuteLua("MoveForwardStop()", 100);
        OnStatusChanged?.Invoke(GetStatusText());
    }

    public void ToggleRotation()
    {
        _rotationEnabled = !_rotationEnabled;
        if (_rotationEnabled) EnsureRunning();
        else if (!_followEnabled) StopTimer();
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

    private void EnsureRunning()
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

    private void Tick(object? state)
    {
        if (!_followEnabled && !_rotationEnabled) return;
        if (!_hook.IsHooked) return;

        try
        {
            _objectManager.Update();
            var player = _objectManager.LocalPlayer;
            if (player == null) return;

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

            // === ТОЛЬКО ROTATION ===
            if (!_followEnabled && _rotationEnabled)
            {
                if (hasTarget)
                {
                    if (_autoFace) _navigation.FaceUnit(player, target!);
                    string script = GetRotationScript(player);
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
                    _hook.ExecuteLua(_instantScript, 300);
            }
            else
            {
                // СТОИМ — полная ротация (CTM сам остановился)

                if (hasTarget)
                {
                    if (_autoFace) _navigation.FaceUnit(player, target!);
                    string script = GetRotationScript(player);
                    _hook.ExecuteLua(script, 500);
                }
            }
        }
        catch { }
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
    local gS,gD = GetSpellCooldown(61304)
    if gS and gS > 0 and gD and gD <= 1.5 then return end
    if UnitIsDeadOrGhost('player') then return end
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

    -- Доты на основной таргет
    if not HasDebuff('target','Прикосновение вампира') then CastSpellByName('Прикосновение вампира') return end
    if not HasDebuff('target','Всепожирающая чума') then CastSpellByName('Всепожирающая чума') return end
    if not HasDebuff('target','Слово Тьмы: Боль') then CastSpellByName('Слово Тьмы: Боль') return end

    -- Основной задотан — мультидот VT на других
    local mainGUID=UnitGUID('target')
    local casted=false
    local mobs={" + luaNames + @"}
    for _,name in ipairs(mobs) do
        TargetUnit(name)
        if UnitGUID('target')~=mainGUID and not UnitIsDeadOrGhost('target') and not HasDebuff('target','Прикосновение вампира') then
            CastSpellByName('Прикосновение вампира')
            casted=true
        end
        TargetLastTarget()
        if casted then return end
    end
    -- Восстанавливаем таргет если потерялся
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

    public void Dispose()
    {
        StopAll();
    }
}
