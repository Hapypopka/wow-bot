using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Hunter — сложная ротация с петом, трекингом, Misdirection. Пока делегирует в AllRotations.</summary>
public class HunterRotation : ICombatRotation
{
    public string Name => "Hunter (BM/MM/Survival)";
    public string WowClass => "HUNTER";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "HUNTER";
    public string GetFullScript() => AllRotations.GetBuiltInScript("HUNTER");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("HUNTER");
}
