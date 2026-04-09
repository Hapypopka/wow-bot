using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class DeathKnightRotation : ICombatRotation
{
    public string Name => "Death Knight (Blood/Frost/Unholy)";
    public string WowClass => "DEATHKNIGHT";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "DEATHKNIGHT";
    public string GetFullScript() => AllRotations.GetBuiltInScript("DEATHKNIGHT");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("DEATHKNIGHT");
}
