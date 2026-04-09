using WowBot.Abstractions;
using WowBot.Abstractions.Entities;

namespace WowBot.Core.Game.Rotations;

/// <summary>
/// Retribution Paladin — C# ротация (v2).
/// Логика приоритетов на C#, проверка КД и каст через минимальный Lua.
///
/// Приоритет (FCFS):
/// 1. [SURVIVAL] Lay on Hands (HP < 20%), Divine Shield (HP < 15%), Flash of Light (AoW proc + HP < 50%)
/// 2. Divine Plea (мана < 10%)
/// 3. Avenging Wrath (бурст при 3+ стаках Holy Vengeance)
/// 4. Hammer of Wrath (добивание < 20%)
/// 5. Judgement
/// 6. Divine Storm
/// 7. Crusader Strike
/// 8. Consecration (2+ врагов, враг стоит)
/// 9. Exorcism (только с Art of War проком)
/// 10. Sacred Shield
/// </summary>
public class RetPaladinRotation : ICombatRotation
{
    public string Name => "Retribution Paladin (C#)";
    public string WowClass => "PALADIN";

    public bool IsMatch(string playerClass, string? specName) =>
        playerClass == "PALADIN" && specName?.Contains("Ret") == true;

    // Spell IDs
    private const int LayOnHands = 633;
    private const int DivineShield = 642;
    private const int ArtOfWar = 59578;     // бафф
    private const int FlashOfLight = 19750;
    private const int DivinePlea = 54428;
    private const int AvengingWrath = 31884;
    private const int HolyVengeance = 31803; // дебафф на таргете
    private const int HammerOfWrath = 24275;
    private const int JudgementOfWisdom = 53408;
    private const int JudgementOfLight = 20271;
    private const int DivineStorm = 53385;
    private const int CrusaderStrike = 35395;
    private const int Consecration = 26573;
    private const int Exorcism = 879;
    private const int SacredShield = 53601;

    public string GetFullScript() => BuildScript(false);
    public string GetInstantScript() => BuildScript(true);

    /// <summary>Генерирует Lua из C# приоритетов. Компактно и читабельно.</summary>
    private string BuildScript(bool instantOnly)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("local function WB_Ret() ");
        sb.Append(LuaHelpers.All);

        // Pre-checks
        sb.Append("if IsMounted() then return end ");
        sb.Append("if UnitIsDeadOrGhost('player') then return end ");
        sb.Append("if UnitCastingInfo('player') or UnitChannelInfo('player') then return end ");

        if (!instantOnly)
        {
            sb.Append("if not UnitAffectingCombat('target') then return end ");
            sb.Append("if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end ");
            sb.Append("if not WB_SA or GetTime()-WB_SA>1 then WB_SA=GetTime() RunMacroText('/startattack') end ");
        }

        sb.Append("if not WB_S then WB_S={} end ");

        // === ПРИОРИТЕТЫ (C# определяет порядок) ===

        // 1. Survival
        AddSpell(sb, $"if PHP()<0.2 and IR({LayOnHands}) then CastSpellByName(SN({LayOnHands}),'player') return end ");
        AddSpell(sb, $"if PHP()<0.15 and IR({DivineShield}) then Cast({DivineShield}) return end ");
        AddSpell(sb, $"if PHP()<0.5 and HB({ArtOfWar}) and IR({FlashOfLight}) then CastSpellByName(SN({FlashOfLight}),'player') return end ");

        // 2. Divine Plea
        AddToggle(sb, "Plea", $"if MP()<0.1 and not HB({DivinePlea}) and IR({DivinePlea}) then Cast({DivinePlea}) return end ");

        // 3. Бурст: Avenging Wrath при 3+ стаках
        AddToggle(sb, "AW", $"do local _,_,_,stk=UnitDebuff('target',SN({HolyVengeance}) or '') if (stk or 0)>=3 and IR({AvengingWrath}) then Cast({AvengingWrath}) return end end ");

        if (instantOnly)
        {
            // Instant-only: только HoW + Judge + DS + CS
            AddToggle(sb, "HoW", $"if THP()<0.2 and IR({HammerOfWrath}) then Cast({HammerOfWrath}) return end ");
            AddToggle(sb, "Judge", $"if IR({JudgementOfWisdom}) then Cast({JudgementOfWisdom}) return end ");
            AddToggle(sb, "DS", $"if IR({DivineStorm}) then Cast({DivineStorm}) return end ");
            AddToggle(sb, "CS", $"if IR({CrusaderStrike}) then Cast({CrusaderStrike}) return end ");
        }
        else
        {
            // 4. Hammer of Wrath (добивание)
            AddToggle(sb, "HoW", $"if THP()<0.2 and IR({HammerOfWrath}) then Cast({HammerOfWrath}) return end ");

            // 5. Judgement (JoW по дефолту, JoL по тоглу)
            AddToggle(sb, "Judge", $"if WB_S.JoL==true then if IR({JudgementOfLight}) then Cast({JudgementOfLight}) return end else if IR({JudgementOfWisdom}) then Cast({JudgementOfWisdom}) return end end ");

            // 6. Divine Storm
            AddToggle(sb, "DS", $"if IR({DivineStorm}) then Cast({DivineStorm}) return end ");

            // 7. Crusader Strike
            AddToggle(sb, "CS", $"if IR({CrusaderStrike}) then Cast({CrusaderStrike}) return end ");

            // 8. Consecration (враг стоит на месте — не тратим на убегающего)
            AddToggle(sb, "Cons", $"if (GetUnitSpeed('target') or 0)==0 and IR({Consecration}) then Cast({Consecration}) return end ");

            // 9. Exorcism (только с AoW проком)
            AddToggle(sb, "Exo", $"if HB({ArtOfWar}) and IR({Exorcism}) then Cast({Exorcism}) return end ");

            // 10. Sacred Shield
            AddToggle(sb, "SS", $"if not HB({SacredShield}) and IR({SacredShield}) then Cast({SacredShield}) return end ");
        }

        sb.Append("end local ok,err=pcall(WB_Ret) if not ok then WB_ERR=err end ");
        return sb.ToString();
    }

    /// <summary>Добавить спелл (всегда активен)</summary>
    private static void AddSpell(System.Text.StringBuilder sb, string lua) => sb.Append(lua);

    /// <summary>Добавить спелл с тоглом (WB_S.Key~=false)</summary>
    private static void AddToggle(System.Text.StringBuilder sb, string key, string lua) =>
        sb.Append($"if WB_S.{key}~=false then {lua} end ");
}
