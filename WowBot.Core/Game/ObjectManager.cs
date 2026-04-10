using WowBot.Abstractions;
using WowBot.Abstractions.Entities;
using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class ObjectManager : IObjectManager
{
    private readonly MemoryReader _memory;
    public MemoryReader Memory => _memory;

    public ObjectManager(MemoryReader memory)
    {
        _memory = memory;
    }

    public ulong LocalPlayerGuid { get; private set; }
    public WowPlayer? LocalPlayer { get; private set; }
    public List<WowUnit> Units { get; private set; } = new(128);
    public List<WowPlayer> Players { get; private set; } = new(32);
    public List<WowDynObject> DynObjects { get; private set; } = new(16);
    public List<WowObject> Objects { get; private set; } = new(64);

    // IObjectManager — через интерфейс возвращаем readonly списки
    IWowPlayer? IObjectManager.LocalPlayer => LocalPlayer;
    IReadOnlyList<IWowUnit> IObjectManager.Units => Units;
    IReadOnlyList<IWowPlayer> IObjectManager.Players => Players;
    IReadOnlyList<IWowDynObject> IObjectManager.DynObjects => DynObjects;

    /// <summary>
    /// Проверяет валидность базовых указателей ObjectManager
    /// </summary>
    public bool IsValid()
    {
        uint clientConnection = _memory.ReadUInt32(Offsets.ClientConnection);
        return clientConnection > 0x10000 && clientConnection < 0x7FFFFFFF;
    }

    /// <summary>
    /// Обновляет список всех объектов из памяти WoW
    /// </summary>
    public void Update()
    {
        Units.Clear();
        Players.Clear();
        DynObjects.Clear();
        Objects.Clear();
        WowPlayer? localPlayer = null;

        uint clientConnection = _memory.ReadUInt32(Offsets.ClientConnection);
        if (clientConnection == 0) { LocalPlayer = null; return; }

        uint objectManagerBase = _memory.ReadUInt32(clientConnection + Offsets.ObjectManagerOffset);
        if (objectManagerBase == 0) { LocalPlayer = null; return; }

        LocalPlayerGuid = _memory.ReadUInt64(objectManagerBase + Offsets.LocalPlayerGuid);

        uint currentObject = _memory.ReadUInt32(objectManagerBase + Offsets.FirstObject);
        int safetyCounter = 0;

        while (currentObject != 0 && (currentObject & 1) == 0 && safetyCounter < 5000)
        {
            safetyCounter++;

            var objectType = (WowObjectType)_memory.ReadInt32(currentObject + Offsets.ObjectType);

            if ((int)objectType > 7 || (int)objectType < 0)
                Logger.Log(LogCat.Error, $"UNKNOWN ObjType={((int)objectType)} at 0x{currentObject:X8}", "ERR");

            switch (objectType)
            {
                case WowObjectType.Unit:
                {
                    var unit = new WowUnit(_memory, currentObject);
                    Units.Add(unit);
                    break;
                }
                case WowObjectType.Player:
                {
                    var player = new WowPlayer(_memory, currentObject);
                    Players.Add(player);

                    if (player.Guid == LocalPlayerGuid)
                        localPlayer = player;
                    break;
                }
                case WowObjectType.DynamicObject:
                {
                    try
                    {
                        var dynObj = new WowDynObject(_memory, currentObject);
                        DynObjects.Add(dynObj);
                    }
                    catch (Exception ex) { Logger.Log(LogCat.Error, $"DynObject parse failed at 0x{currentObject:X}: {ex.Message}", "ERR"); }
                    break;
                }
                default:
                {
                    var obj = new WowObject(_memory, currentObject);
                    Objects.Add(obj);
                    break;
                }
            }

            currentObject = _memory.ReadUInt32(currentObject + Offsets.NextObject);
        }

        LocalPlayer = localPlayer;

        _updateCount++;
    }
    private int _updateCount;

    /// <summary>
    /// Получить имя персонажа (локального игрока)
    /// </summary>
    public string GetPlayerName()
    {
        return _memory.ReadString(Offsets.PlayerName);
    }

    /// <summary>
    /// Найти юнита по GUID
    /// </summary>
    public WowUnit? GetUnitByGuid(ulong guid)
    {
        foreach (var unit in Units)
            if (unit.Guid == guid) return unit;
        foreach (var player in Players)
            if (player.Guid == guid) return player;
        return null;
    }

    /// <summary>
    /// Получить текущий таргет локального игрока
    /// </summary>
    public WowUnit? GetTarget()
    {
        if (LocalPlayer == null) return null;
        return GetUnitByGuid(LocalPlayer.TargetGuid);
    }

    /// <summary>
    /// Юниты в радиусе от локального игрока
    /// </summary>
    public List<WowUnit> GetUnitsInRange(float range)
    {
        if (LocalPlayer == null) return new();
        return Units.Where(u => u.IsAlive && LocalPlayer.DistanceTo(u) <= range).ToList();
    }
}
