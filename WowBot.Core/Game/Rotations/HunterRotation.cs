using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class HunterRotation : ICombatRotation
{
    public string Name => "Hunter (BM/MM/Survival)";
    public string WowClass => "HUNTER";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "HUNTER", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("HUNTER");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("HUNTER");
}
