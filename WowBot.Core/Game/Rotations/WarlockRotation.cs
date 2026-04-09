using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class WarlockRotation : ICombatRotation
{
    public string Name => "Warlock (Affli/Demo/Destro)";
    public string WowClass => "WARLOCK";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "WARLOCK", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("WARLOCK");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("WARLOCK");
}
