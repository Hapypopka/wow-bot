using WowBot.Abstractions;
using WowBot.Abstractions.Entities;
using WowBot.Adapter.Headless.Entities;
using WowBot.HeadlessPoc;

namespace WowBot.Adapter.Headless;

/// <summary>
/// IObjectManager поверх WorldState из HeadlessPoc.
/// Snapshot-стиль: при каждом Update() формирует новые wrapper'ы поверх живых WorldEntity.
/// Это безопасно для concurrent чтения из тика бота — entries не меняются между Update().
/// </summary>
public sealed class HeadlessObjectManager : IObjectManager
{
    private readonly WorldState _world;
    private readonly Func<ulong> _localPlayerGuidProvider;

    private List<IWowUnit> _units = new();
    private List<IWowPlayer> _players = new();
    private List<IWowDynObject> _dynObjects = new();
    private IWowPlayer? _localPlayer;

    public HeadlessObjectManager(WorldState world, Func<ulong> localPlayerGuidProvider)
    {
        _world = world;
        _localPlayerGuidProvider = localPlayerGuidProvider;
    }

    public ulong LocalPlayerGuid => _localPlayerGuidProvider();
    public IWowPlayer? LocalPlayer => _localPlayer;

    public IReadOnlyList<IWowUnit> Units => _units;
    public IReadOnlyList<IWowPlayer> Players => _players;
    public IReadOnlyList<IWowDynObject> DynObjects => _dynObjects;

    public bool IsValid() => _world.Count > 0 && LocalPlayerGuid != 0;

    public void Update()
    {
        var snapshot = _world.Snapshot();
        var localGuid = _localPlayerGuidProvider();

        WorldEntity? localEntity = null;
        var units = new List<IWowUnit>(snapshot.Count);
        var players = new List<IWowPlayer>(snapshot.Count);
        var dyns = new List<IWowDynObject>(snapshot.Count);

        foreach (var e in snapshot)
        {
            if (e.Guid == localGuid) localEntity = e;

            switch (e.Type)
            {
                case WowObjectType.Unit:
                    units.Add(new HeadlessWowUnit(e, () => localEntity));
                    break;
                case WowObjectType.Player:
                    var player = new HeadlessWowPlayer(e, () => localEntity);
                    players.Add(player);
                    units.Add(player);  // игроки тоже юниты
                    break;
                case WowObjectType.DynamicObject:
                    dyns.Add(new HeadlessWowDynObject(e));
                    break;
            }
        }

        _units = units;
        _players = players;
        _dynObjects = dyns;
        _localPlayer = localEntity != null
            ? new HeadlessWowPlayer(localEntity, () => localEntity)
            : null;
    }
}
