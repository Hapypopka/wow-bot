using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class WarriorRotation : ICombatRotation
{
    public string Name => "Warrior (Arms/Fury/Prot)";
    public string WowClass => "WARRIOR";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "WARRIOR", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("WARRIOR");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("WARRIOR");
}
