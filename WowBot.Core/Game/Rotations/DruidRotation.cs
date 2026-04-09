using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Druid (Balance/Feral/Resto) — Resto хилер, Feral формы. Делегирует в AllRotations.</summary>
public class DruidRotation : ICombatRotation
{
    public string Name => "Druid (Balance/Feral/Resto)";
    public string WowClass => "DRUID";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "DRUID";
    public string GetFullScript() => AllRotations.GetBuiltInScript("DRUID");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("DRUID");
}
