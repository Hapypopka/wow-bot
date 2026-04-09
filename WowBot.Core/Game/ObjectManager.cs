using WowBot.Core.Game.Entities;
using WowBot.Core.Memory;

namespace WowBot.Core.Game;

public class ObjectManager
{
    private readonly MemoryReader _memory;
    public MemoryReader Memory => _memory;

    public ObjectManager(MemoryReader memory)
    {
        _memory = memory;
    }

    public ulong LocalPlayerGuid { get; private set; }
    public WowPlayer? LocalPlayer { get; private set; }
    public List<WowUnit> Units { get; private set; } = new();
    public List<WowPlayer> Players { get; private set; } = new();
    public List<WowDynObject> DynObjects { get; private set; } = new();
    public List<WowObject> Objects { get; private set; } = new();

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
        var units = new List<WowUnit>();
        var players = new List<WowPlayer>();
        var dynObjects = new List<WowDynObject>();
        var objects = new List<WowObject>();
        WowPlayer? localPlayer = null;

        uint clientConnection = _memory.ReadUInt32(Offsets.ClientConnection);
        if (clientConnection == 0) { Units = units; Players = players; Objects = objects; LocalPlayer = null; return; }

        uint objectManagerBase = _memory.ReadUInt32(clientConnection + Offsets.ObjectManagerOffset);
        if (objectManagerBase == 0) { Units = units; Players = players; Objects = objects; LocalPlayer = null; return; }

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
                    units.Add(unit);
                    break;
                }
                case WowObjectType.Player:
                {
                    var player = new WowPlayer(_memory, currentObject);
                    players.Add(player);

                    if (player.Guid == LocalPlayerGuid)
                        localPlayer = player;
                    break;
                }
                case WowObjectType.DynamicObject:
                {
                    try
                    {
                        var dynObj = new WowDynObject(_memory, currentObject);
                        dynObjects.Add(dynObj);
                    }
                    catch (Exception ex) { Logger.Log(LogCat.Error, $"DynObject parse failed at 0x{currentObject:X}: {ex.Message}", "ERR"); }
                    break;
                }
                default:
                {
                    var obj = new WowObject(_memory, currentObject);
                    objects.Add(obj);
                    break;
                }
            }

            currentObject = _memory.ReadUInt32(currentObject + Offsets.NextObject);
        }

        Units = units;
        Players = players;
        DynObjects = dynObjects;
        Objects = objects;
        LocalPlayer = localPlayer;

        // Раз в 33 вызова (~5с) логируем итог
        _updateCount++;
        if (_updateCount >= 33)
        {
            _updateCount = 0;
            Logger.Log(LogCat.AoE, $"ObjectManager: units={units.Count} players={players.Count} dyn={dynObjects.Count} obj={objects.Count} total={safetyCounter}");
        }
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
