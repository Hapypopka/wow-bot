using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class RogueRotation : ICombatRotation
{
    public string Name => "Rogue (Assassination/Combat/Subtlety)";
    public string WowClass => "ROGUE";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "ROGUE", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("ROGUE");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("ROGUE");
}
