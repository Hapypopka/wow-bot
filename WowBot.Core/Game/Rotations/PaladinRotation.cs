using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class PaladinRotation : ICombatRotation
{
    public string Name => "Paladin (Ret/Prot/Holy)";
    public string WowClass => "PALADIN";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "PALADIN", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("PALADIN");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("PALADIN");
}
