using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class DruidRotation : ICombatRotation
{
    public string Name => "Druid (Balance/Feral/Resto)";
    public string WowClass => "DRUID";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "DRUID", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("DRUID");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("DRUID");
}
