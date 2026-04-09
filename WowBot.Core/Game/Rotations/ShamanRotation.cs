using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Shaman (Ele/Enh/Resto) — тотемы, Resto хилер. Делегирует в AllRotations.</summary>
public class ShamanRotation : ICombatRotation
{
    public string Name => "Shaman (Ele/Enh/Resto)";
    public string WowClass => "SHAMAN";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "SHAMAN";
    public string GetFullScript() => AllRotations.GetBuiltInScript("SHAMAN");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("SHAMAN");
}
