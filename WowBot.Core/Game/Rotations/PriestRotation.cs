using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class PriestRotation : ICombatRotation
{
    public string Name => "Priest (Shadow/Disc/Holy)";
    public string WowClass => "PRIEST";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "PRIEST", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("PRIEST");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("PRIEST");
}
