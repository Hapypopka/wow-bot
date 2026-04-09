namespace WowBot.Abstractions.Entities;

/// <summary>Базовый игровой объект WoW</summary>
public interface IWowObject
{
    uint BaseAddress { get; }
    ulong Guid { get; }
}
