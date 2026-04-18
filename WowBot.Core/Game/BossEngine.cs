namespace WowBot.Core.Game;

using WowBot.Core.Game.Entities;

/// <summary>
/// BossEngine v2 — event-driven система босс-тактик по образцу DBM.
/// Заменяет старый BossTactics.cs.
///
/// 1. Lua Combat Log Listener ловит CLEU события по spell ID
/// 2. C# читает WB_BOSS_EVT каждый тик, парсит
/// 3. Активная тактика (IBossTactic) реагирует на события
/// </summary>
public class BossEngine
{
    private readonly EndSceneHook _hook;
    private readonly ObjectManager _objectManager;
    private readonly ClickToMove _ctm;
    private readonly Navigation _navigation;

    private bool _listenerInstalled;
    private int _tick;
    private int _bossNpcId;

    // Детект ещё-не-в-бою босса: SAY один раз за сессию на каждого NPC ID
    private readonly HashSet<int> _announcedBosses = new();

    // Активная тактика
    private IBossTactic? _activeTactic;

    // Все зарегистрированные тактики
    private readonly Dictionary<int, Func<IBossTactic>> _tacticFactory = new();

    // Контекст боя (передаётся в тактику)
    public BossContext Context { get; }

    public bool IsActive => _activeTactic != null;
    public string? ActiveBossName => _activeTactic?.BossName;

    public bool IsMelee { get; set; }
    public bool IsHealer { get; set; }
    public bool IsTank { get; set; }

    public BossEngine(EndSceneHook hook, ObjectManager objectManager, ClickToMove ctm, Navigation navigation)
    {
        _hook = hook;
        _objectManager = objectManager;
        _ctm = ctm;
        _navigation = navigation;
        Context = new BossContext(hook, objectManager, ctm, navigation);

        // Регистрируем тактики — Marrowgar во всех вариантах (нормал/героик/image)
        foreach (int id in MarrowgarTactic.NPC_IDS)
            _tacticFactory[id] = () => new MarrowgarTactic();
    }

    /// <summary>Установить Lua Combat Log Listener</summary>
    public void InstallListener()
    {
        if (_listenerInstalled) return;

        // Собираем все spell ID из всех тактик для фильтрации
        var allSpellIds = new HashSet<int>();
        var allNpcIds = new HashSet<int>();
        foreach (var (npcId, factory) in _tacticFactory)
        {
            var tactic = factory();
            allSpellIds.UnionWith(tactic.WatchSpellIds);
            allNpcIds.Add(npcId);
        }

        // Disrupting Shout (71022) — всегда мониторим в ICC
        allSpellIds.Add(71022);

        string spellIdSet = string.Join(",", allSpellIds.Select(id => $"[{id}]=1"));
        string lua = @$"
if not WB_BOSS_FRAME then
  WB_BOSS_EVT=''
  WB_STOP_CAST=0
  WB_BOSS_IDS={{{spellIdSet}}}
  WB_BOSS_FRAME=CreateFrame('Frame')
  WB_BOSS_FRAME:RegisterEvent('COMBAT_LOG_EVENT_UNFILTERED')
  WB_BOSS_FRAME:SetScript('OnEvent',function(self,event,_,evt,srcG,srcN,_,dstG,dstN,_,spId)
    if not spId or not WB_BOSS_IDS[spId] then return end
    if spId==71022 and evt=='SPELL_CAST_START' then WB_STOP_CAST=GetTime()+3 end
    local isMe=(dstG==UnitGUID('player')) and 1 or 0
    WB_BOSS_EVT=evt..'|'..spId..'|'..(srcN or '')..'|'..(dstN or '')..'|'..isMe
  end)
end";

        _hook.ExecuteLua(lua.Replace("\n", " ").Replace("\r", ""), 500);
        _listenerInstalled = true;
        Logger.Info($"BossEngine: listener installed, watching {allSpellIds.Count} spell IDs");
    }

    /// <summary>Удалить Lua listener</summary>
    public void RemoveListener()
    {
        if (!_listenerInstalled) return;
        try
        {
            _hook.ExecuteLua("if WB_BOSS_FRAME then WB_BOSS_FRAME:UnregisterAllEvents() WB_BOSS_FRAME=nil end WB_BOSS_EVT=nil WB_BOSS_IDS=nil", 200);
        }
        catch { }
        _listenerInstalled = false;
        _activeTactic = null;
    }

    /// <summary>
    /// Главный тик. Вызывается из BotEngine каждые ~150мс.
    /// Возвращает true если тактика активна и управляет поведением.
    /// </summary>
    public bool Tick(WowPlayer player, string enemyCountLua, string spellFlagsLua, string fullScript)
    {
        _tick++;
        if (!_listenerInstalled) return false;

        Context.Player = player;
        Context.IsMelee = IsMelee;
        Context.IsHealer = IsHealer;
        Context.IsTank = IsTank;

        // Каждые ~1с: ищем босса если нет активной тактики
        if (_tick % 7 == 0 && _activeTactic == null)
        {
            DetectBoss();
        }

        if (_activeTactic == null) return false;

        // Обновляем контекст босса
        var boss = FindBossUnit(_bossNpcId);
        if (boss == null || !boss.IsAlive)
        {
            // Босс мёртв или не найден
            Logger.Info($"BossEngine: {_activeTactic.BossName} dead or not found, deactivating");
            _activeTactic.OnCombatEnd(Context);
            _activeTactic = null;
            return false;
        }
        Context.Boss = boss;

        // Читаем последнее событие из Lua
        if (_tick % 2 == 0) // каждые ~300мс
        {
            ReadAndDispatchEvent();
        }

        // Тик тактики
        var action = _activeTactic.Tick(Context);
        return ExecuteAction(action, player, enemyCountLua, spellFlagsLua, fullScript);
    }

    private void DetectBoss()
    {
        // 1) Быстрый путь: смотрим на таргет игрока. Если таргет — босс из списка, announce.
        var target = _objectManager.GetTarget();
        if (target != null)
            Logger.Info($"BossEngine.Detect: target='{target.Name}' NpcId={target.NpcId} alive={target.IsAlive} inCombat={target.InCombat}");
        else
            Logger.Info("BossEngine.Detect: no target");

        if (target != null && target.IsAlive && _tacticFactory.ContainsKey(target.NpcId))
        {
            TryAnnounceAndActivate(target);
            if (_activeTactic != null) return;
        }

        // 2) Медленный путь: сканируем все видимые юниты (на случай когда таргет не босс).
        int unitCount = 0;
        foreach (var unit in _objectManager.Units)
        {
            unitCount++;
            if (!unit.IsAlive) continue;
            try
            {
                if (!_tacticFactory.ContainsKey(unit.NpcId)) continue;
                Logger.Info($"BossEngine.Detect: found boss in units — '{unit.Name}' NpcId={unit.NpcId}");
                TryAnnounceAndActivate(unit);
                if (_activeTactic != null) return;
            }
            catch { }
        }
        if (target == null && unitCount < 5)
            Logger.Info($"BossEngine.Detect: unitCount={unitCount}");
    }

    private void TryAnnounceAndActivate(WowUnit unit)
    {
        // Announcement один раз за сессию при первом появлении
        if (!_announcedBosses.Contains(unit.NpcId))
        {
            _announcedBosses.Add(unit.NpcId);
            var factoryBoss = _tacticFactory[unit.NpcId]();
            string announce = $"Вижу {factoryBoss.BossName}!";
            _hook.ExecuteLua($"SendChatMessage('{announce}','SAY')", 150);
            Logger.Info($"BossEngine: {factoryBoss.BossName} detected (target/visible)");
        }

        // Активация тактики только когда босс в бою
        if (!unit.InCombat) return;

        _bossNpcId = unit.NpcId;
        _activeTactic = _tacticFactory[unit.NpcId]();
        Context.Boss = unit;
        _activeTactic.OnCombatStart(Context);
        _hook.ExecuteLua($"SendChatMessage('Бой на {_activeTactic.BossName}!','SAY')", 150);
        Logger.Info($"BossEngine: {_activeTactic.BossName} combat started! NPC={unit.NpcId}");
    }

    private WowUnit? FindBossUnit(int npcId)
    {
        foreach (var unit in _objectManager.Units)
        {
            try { if (unit.NpcId == npcId && unit.IsAlive) return unit; } catch { }
        }
        return null;
    }

    private void ReadAndDispatchEvent()
    {
        try
        {
            string? raw = _hook.ExecuteLuaWithResult("WB_R=WB_BOSS_EVT or '' WB_BOSS_EVT=''");
            if (string.IsNullOrEmpty(raw)) return;

            // Формат: "EVENT|spellId|srcName|dstName|isMe"
            var parts = raw.Split('|');
            if (parts.Length < 5) return;

            var evt = new BossEvent
            {
                EventType = parts[0],
                SpellId = int.TryParse(parts[1], out int sid) ? sid : 0,
                SourceName = parts[2],
                DestName = parts[3],
                IsPlayer = parts[4] == "1"
            };

            if (evt.SpellId != 0)
            {
                _activeTactic?.OnEvent(Context, evt);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"BossEngine: event read error: {ex.Message}");
        }
    }

    private bool ExecuteAction(TacticAction action, WowPlayer player, string enemyCountLua, string spellFlagsLua, string fullScript)
    {
        switch (action.Type)
        {
            case ActionType.None:
                return false; // тактика не управляет — обычная ротация

            case ActionType.RotateAndAttack:
                // Face target + полная ротация
                var target = _objectManager.GetTarget();
                if (target != null && target.IsAlive && target.InCombat)
                {
                    if (!_navigation.FaceInstant(player, target)) { }
                    else _hook.ExecuteLua(enemyCountLua + spellFlagsLua + fullScript, 500);
                }
                return true;

            case ActionType.MoveTo:
                // Двигаться к точке
                _ctm.MoveTo(action.X, action.Y, action.Z, 0.5f);
                return true;

            case ActionType.MoveAndAttack:
                // Двигаться + instants
                _ctm.MoveTo(action.X, action.Y, action.Z, 0.5f);
                // TODO: instants на ходу
                return true;

            case ActionType.TargetSwitch:
                // Сменить таргет на NPC
                if (action.TargetNpcId > 0)
                {
                    foreach (var u in _objectManager.Units)
                    {
                        try
                        {
                            if (u.IsAlive && u.NpcId == action.TargetNpcId)
                            {
                                // Таргет через GUID
                                _hook.ExecuteLua($"WB_R='' for i=1,40 do local u='nameplate'..i if UnitExists(u) then local g=UnitGUID(u) if g then local _,_,_,_,_,npcId=strsplit('-',g) if tonumber(npcId)=={action.TargetNpcId} then TargetUnit(u) break end end end end", 300);
                                Logger.Info($"BossEngine: target switch → NPC {action.TargetNpcId}");
                                break;
                            }
                        }
                        catch { }
                    }
                }
                return true;

            case ActionType.Flee:
                // Бежать от точки
                float dx = player.X - action.X;
                float dy = player.Y - action.Y;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 0.1f) { dx = 1; dy = 0; len = 1; }
                float fleeX = player.X + (dx / len) * action.FleeDistance;
                float fleeY = player.Y + (dy / len) * action.FleeDistance;
                _ctm.MoveTo(fleeX, fleeY, player.Z, 0.5f);
                return true;

            default:
                return false;
        }
    }
}

// === Data Types ===

public class BossEvent
{
    public string EventType { get; set; } = "";
    public int SpellId { get; set; }
    public string SourceName { get; set; } = "";
    public string DestName { get; set; } = "";
    public bool IsPlayer { get; set; }
}

public class BossContext
{
    public EndSceneHook Hook { get; }
    public ObjectManager ObjectManager { get; }
    public ClickToMove Ctm { get; }
    public Navigation Navigation { get; }
    public WowPlayer? Player { get; set; }
    public WowUnit? Boss { get; set; }
    public bool IsMelee { get; set; }
    public bool IsHealer { get; set; }
    public bool IsTank { get; set; }

    public BossContext(EndSceneHook hook, ObjectManager objectManager, ClickToMove ctm, Navigation navigation)
    {
        Hook = hook;
        ObjectManager = objectManager;
        Ctm = ctm;
        Navigation = navigation;
    }
}

public enum ActionType
{
    None,           // тактика не управляет — обычная ротация
    RotateAndAttack,// face + ротация
    MoveTo,         // двигаться к точке
    MoveAndAttack,  // двигаться + instants
    TargetSwitch,   // сменить таргет
    Flee,           // бежать от точки
}

public struct TacticAction
{
    public ActionType Type;
    public float X, Y, Z;
    public float FleeDistance;
    public int TargetNpcId;

    public static TacticAction DoNothing => new() { Type = ActionType.None };
    public static TacticAction Attack => new() { Type = ActionType.RotateAndAttack };
    public static TacticAction GoTo(float x, float y, float z) => new() { Type = ActionType.MoveTo, X = x, Y = y, Z = z };
    public static TacticAction FleeFrom(float x, float y, float z, float dist = 15f) => new() { Type = ActionType.Flee, X = x, Y = y, Z = z, FleeDistance = dist };
    public static TacticAction SwitchTarget(int npcId) => new() { Type = ActionType.TargetSwitch, TargetNpcId = npcId };
}

/// <summary>Интерфейс тактики босса</summary>
public interface IBossTactic
{
    string BossName { get; }
    int[] WatchSpellIds { get; }
    void OnCombatStart(BossContext ctx);
    void OnCombatEnd(BossContext ctx);
    void OnEvent(BossContext ctx, BossEvent evt);
    TacticAction Tick(BossContext ctx);
}
