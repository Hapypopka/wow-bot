using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Warlock (Affli/Demo/Destro) — сложная Demo логика. Делегирует в AllRotations.</summary>
public class WarlockRotation : ICombatRotation
{
    public string Name => "Warlock (Affli/Demo/Destro)";
    public string WowClass => "WARLOCK";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "WARLOCK";
    public string GetFullScript() => AllRotations.GetBuiltInScript("WARLOCK");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("WARLOCK");
}
