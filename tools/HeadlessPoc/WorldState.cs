// Локальная модель «что происходит в мире» вокруг нашего перса.
// Заполняется парсером SMSG_UPDATE_OBJECT и обновляется по приходящим патчам.
// Это аналог ObjectManager в основном боте, только данные из сети а не из памяти клиента.

namespace WowBot.HeadlessPoc;

/// <summary>Тип объекта (TypeId) в WoW 3.3.5.</summary>
internal enum WowObjectType : byte
{
    Object = 0,
    Item = 1,
    Container = 2,
    Unit = 3,
    Player = 4,
    GameObject = 5,
    DynamicObject = 6,
    Corpse = 7,
}

/// <summary>Сущность в мире — юнит, игрок, моб, объект.</summary>
internal sealed class WorldEntity
{
    public ulong Guid { get; init; }
    public WowObjectType Type { get; set; }
    public uint Entry { get; set; }   // creature template id (для крип) или item id

    // Позиция (WoW coords, Z вверх)
    public float X, Y, Z, Orientation;

    // Боевые поля (только для Unit/Player)
    public int Health;
    public int MaxHealth;
    public byte Level;
    public uint DisplayId;
    public uint FactionTemplate;
    public uint UnitFlags;
    public ulong Target;       // GUID цели (на кого смотрит)
    public ulong CharmedBy;
    public ulong SummonedBy;

    // Кэш resolved'ного имени
    public string? Name;

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>Снэпшот окружающего мира — все известные сущности.</summary>
internal sealed class WorldState
{
    private readonly Dictionary<ulong, WorldEntity> _entities = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _entities.Count; } }

    public WorldEntity GetOrCreate(ulong guid, WowObjectType type)
    {
        lock (_lock)
        {
            if (!_entities.TryGetValue(guid, out var e))
            {
                e = new WorldEntity { Guid = guid, Type = type };
                _entities[guid] = e;
            }
            else
            {
                e.Type = type;
            }
            return e;
        }
    }

    public WorldEntity? Get(ulong guid)
    {
        lock (_lock) return _entities.TryGetValue(guid, out var e) ? e : null;
    }

    public bool Remove(ulong guid)
    {
        lock (_lock) return _entities.Remove(guid);
    }

    public List<WorldEntity> Snapshot()
    {
        lock (_lock) return new List<WorldEntity>(_entities.Values);
    }

    /// <summary>Все живые юниты/игроки в радиусе от точки.</summary>
    public List<WorldEntity> NearbyUnits(float x, float y, float z, float radius)
    {
        var r2 = radius * radius;
        lock (_lock)
        {
            return _entities.Values
                .Where(e => e.Type == WowObjectType.Unit || e.Type == WowObjectType.Player)
                .Where(e => {
                    var dx = e.X - x; var dy = e.Y - y; var dz = e.Z - z;
                    return dx*dx + dy*dy + dz*dz <= r2;
                })
                .ToList();
        }
    }
}
