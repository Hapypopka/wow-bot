using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class MageRotation : ICombatRotation
{
    public string Name => "Mage (Arcane/Fire/Frost)";
    public string WowClass => "MAGE";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "MAGE", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("MAGE");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("MAGE");
}
