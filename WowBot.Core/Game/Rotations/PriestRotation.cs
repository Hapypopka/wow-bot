using WowBot.Abstractions;

namespace WowBot.Core.Game.Rotations;

/// <summary>Priest — Shadow DPS + Disc/Holy хилеры. Полное тело из AllRotations, обёрнутое LuaHelpers.</summary>
public class PriestRotation : ICombatRotation
{
    public string Name => "Priest (Shadow/Disc/Holy)";
    public string WowClass => "PRIEST";
    public bool IsMatch(string playerClass, string? specName) => playerClass == "PRIEST";
    public string GetFullScript() => AllRotations.GetBuiltInScript("PRIEST");
    public string GetInstantScript() => AllRotations.GetInstantScriptForClass("PRIEST");
}
