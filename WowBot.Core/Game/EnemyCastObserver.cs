using System.Globalization;
using WowBot.Core.Game.Generated;

namespace WowBot.Core.Game;

/// <summary>
/// Событие начала вражеского каста — пойманное Lua combat log hook'ом.
/// </summary>
public readonly record struct HostileCast(
    ulong CasterGuid,
    int SpellId,
    ulong TargetGuid,
    DateTime DetectedAt);

/// <summary>
/// Наблюдатель за вражескими кастами через COMBAT_LOG_EVENT_UNFILTERED.
/// Работа в два шага:
///   1. Lua фрейм ловит SPELL_CAST_START от враждебных → пишет в WB_HC.
///   2. C# раз в ~200мс читает WB_HC и обновляет ActiveCasts.
///
/// Решения об уклонении делает ProactiveAoEAvoidance (в CombatHelper).
/// </summary>
public class EnemyCastObserver
{
    private readonly EndSceneHook _hook;
    private bool _setupDone;
    private DateTime _lastPoll = DateTime.MinValue;

    public IReadOnlyList<HostileCast> ActiveCasts => _active;
    private readonly List<HostileCast> _active = new();

    // Setup Lua: idempotent — регистрируем фрейм один раз.
    // 3.3.5a combat log signature OnEvent args:
    //   (self, event, timestamp, subEvent, srcGuid, srcName, srcFlags, dstGuid, dstName, dstFlags, spellId, spellName, spellSchool, ...)
    // COMBATLOG_OBJECT_REACTION_HOSTILE = 0x00000040
    private const string SetupScript = @"
if not WB_OBS then
    WB_OBS = 1
    WB_HC = {}
    WB_OBS_F = CreateFrame('Frame')
    WB_OBS_F:RegisterEvent('COMBAT_LOG_EVENT_UNFILTERED')
    WB_OBS_F:SetScript('OnEvent', function(self, ev, ts, sub, sg, sn, sf, dg, dn, df, sid, sname, sschool)
        if not sub or not sg then return end
        if sub == 'SPELL_CAST_START' then
            if not sf or bit.band(sf, 0x40) == 0 then return end
            if not sid then return end
            WB_HC[sg] = sid .. '|' .. ts .. '|' .. (dg or '0')
        elseif sub == 'SPELL_CAST_FAILED' or sub == 'SPELL_CAST_SUCCESS' or sub == 'SPELL_INTERRUPT' then
            WB_HC[sg] = nil
        elseif sub == 'UNIT_DIED' then
            WB_HC[dg] = nil
        end
    end)
end
";

    // Сериализация WB_HC в строку. Формат: каждая запись отделена \n, поля через ;
    //   casterGuid;spellId|castStartTs|targetGuid
    private const string QueryScript = @"
local r = ''
local now = GetTime()
for guid, v in pairs(WB_HC or {}) do
    -- отфильтровать старые записи (>8s — дольше самого длинного каста)
    local p = string.find(v, '|', 1, true)
    if p then
        local ts = tonumber(string.sub(v, p+1, (string.find(v, '|', p+1, true) or 0)-1))
        if ts and (now - ts) < 8 then
            r = r .. guid .. ';' .. v .. '\n'
        else
            WB_HC[guid] = nil
        end
    end
end
WB_R = r
";

    public EnemyCastObserver(EndSceneHook hook)
    {
        _hook = hook;
    }

    public void EnsureSetup()
    {
        if (_setupDone) return;
        _hook.ExecuteLua(SetupScript, timeoutMs: 300);
        _setupDone = true;
        Logger.Log(LogCat.AoE, "EnemyCastObserver: Lua hook установлен");
    }

    /// <summary>Обновить список активных кастов. Вызывать раз в тик.</summary>
    public void Tick()
    {
        EnsureSetup();

        // Throttle — полим Lua не чаще 200мс (чтобы не блокировать основной тик).
        if ((DateTime.UtcNow - _lastPoll).TotalMilliseconds < 200) return;
        _lastPoll = DateTime.UtcNow;

        string? raw = _hook.ExecuteLuaWithResult(QueryScript, timeoutMs: 300);
        _active.Clear();
        if (string.IsNullOrEmpty(raw)) return;

        foreach (var line in raw.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Формат: casterGuid;spellId|castStartTs|targetGuid
            int semi = line.IndexOf(';');
            if (semi < 0) continue;
            string casterGuidStr = line.Substring(0, semi);
            string rest = line.Substring(semi + 1);
            string[] parts = rest.Split('|');
            if (parts.Length < 3) continue;

            ulong casterGuid = ParseGuid(casterGuidStr);
            if (casterGuid == 0) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int spellId))
                continue;
            ulong targetGuid = ParseGuid(parts[2]);

            _active.Add(new HostileCast(casterGuid, spellId, targetGuid, DateTime.UtcNow));
        }
    }

    public void Clear()
    {
        _active.Clear();
    }

    /// <summary>
    /// Парсим GUID из формата Lua "0x0123456789ABCDEF" или "Creature-0-...-0x...".
    /// В 3.3.5a обычно plain hex.
    /// </summary>
    private static ulong ParseGuid(string s)
    {
        if (string.IsNullOrEmpty(s) || s == "0") return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        // fallback — иногда GUID приходит в десятичном виде
        if (ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0;
    }
}
