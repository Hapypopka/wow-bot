using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Paladin Prot/Holy — Ret покрыт RetPaladinRotation. Делегирует в AllRotations.</summary>
public class PaladinRotation : ICombatRotation
{
    public string Name => "Paladin (Prot/Holy)";
    public string WowClass => "PALADIN";

    public bool IsMatch(string playerClass, string? specName) =>
        playerClass == "PALADIN" && specName?.Contains("Ret") != true;

    public string GetFullScript() => AllRotations.GetBuiltInScript("PALADIN");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("PALADIN");
}
