using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

public class ShamanRotation : ICombatRotation
{
    public string Name => "Shaman (Ele/Enh/Resto)";
    public string WowClass => "SHAMAN";

    public bool IsMatch(string playerClass, string? specName) =>
        string.Equals(playerClass, "SHAMAN", StringComparison.OrdinalIgnoreCase);

    public string GetFullScript() => AllRotations.GetBuiltInScript("SHAMAN");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("SHAMAN");
}
