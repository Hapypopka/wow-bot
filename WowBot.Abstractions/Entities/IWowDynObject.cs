namespace WowBot.Abstractions.Entities;

/// <summary>Динамический объект WoW (AoE лужи, консекрация и т.д.)</summary>
public interface IWowDynObject : IWowObject
{
    int SpellId { get; }
    float Radius { get; }
    float X { get; }
    float Y { get; }
    float Z { get; }
    ulong Caster { get; }
}
