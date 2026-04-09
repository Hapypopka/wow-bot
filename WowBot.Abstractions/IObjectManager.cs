using WowBot.Abstractions.Entities;

namespace WowBot.Abstractions;

/// <summary>
/// Доступ к игровым объектам WoW (юниты, игроки, DynObject).
/// Реализация: WowBot.Core.Game.ObjectManager
/// </summary>
public interface IObjectManager
{
    ulong LocalPlayerGuid { get; }
    IWowPlayer? LocalPlayer { get; }

    IReadOnlyList<IWowUnit> Units { get; }
    IReadOnlyList<IWowPlayer> Players { get; }
    IReadOnlyList<IWowDynObject> DynObjects { get; }

    bool IsValid();
    void Update();
}
