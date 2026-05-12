// Парсер SMSG_UPDATE_OBJECT для WoW 3.3.5.
//
// Формат пакета:
//   uint32 numUpdates
//   for each:
//     uint8 updateType (0=VALUES, 1=MOVEMENT, 2=CREATE, 3=CREATE2, 4=OUT_OF_RANGE, 5=NEAR)
//     packed_guid (для 0/1/2/3) или uint32 count + packed_guid[count] (для 4/5)
//     если CREATE: uint8 typeId + Movement/Position блок
//     для 0/1/2/3: ValuesBlock (если есть данные)
//
// MovementBlock зависит от UPDATEFLAG_* — обрабатываем основные ветки,
// остальное (transports, splines) — best-effort скип, чтобы не сломать парсинг.
//
// ValuesBlock:
//   uint8 blockCount
//   uint32 mask[blockCount]  — каждый бит = поле UNIT_FIELD_*
//   uint32 values[N]         — N = популяция битов; в порядке возрастания индекса

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace WowBot.HeadlessPoc;

public static class UpdateObjectParser
{
    // updateType
    private const byte UPDATETYPE_VALUES         = 0;
    private const byte UPDATETYPE_MOVEMENT       = 1;
    private const byte UPDATETYPE_CREATE_OBJECT  = 2;
    private const byte UPDATETYPE_CREATE_OBJECT2 = 3;
    private const byte UPDATETYPE_OUT_OF_RANGE   = 4;
    private const byte UPDATETYPE_NEAR_OBJECTS   = 5;

    // updateFlags (uint16)
    private const uint UPDATEFLAG_NONE                = 0x0000;
    private const uint UPDATEFLAG_SELF                = 0x0001;
    private const uint UPDATEFLAG_TRANSPORT           = 0x0002;
    private const uint UPDATEFLAG_HAS_TARGET          = 0x0004;
    private const uint UPDATEFLAG_UNK                 = 0x0008;
    private const uint UPDATEFLAG_LOWGUID             = 0x0010;
    private const uint UPDATEFLAG_LIVING              = 0x0020;
    private const uint UPDATEFLAG_STATIONARY_POSITION = 0x0040;
    private const uint UPDATEFLAG_VEHICLE             = 0x0080;
    private const uint UPDATEFLAG_POSITION            = 0x0100;
    private const uint UPDATEFLAG_ROTATION            = 0x0200;

    // movementFlags
    private const uint MOVEMENTFLAG_ONTRANSPORT       = 0x00000200;
    private const uint MOVEMENTFLAG_SWIMMING          = 0x00200000;
    private const uint MOVEMENTFLAG_FLYING            = 0x02000000;
    private const uint MOVEMENTFLAG_FALLING           = 0x00002000;
    private const uint MOVEMENTFLAG_SPLINE_ELEVATION  = 0x04000000;
    private const uint MOVEMENTFLAG_SPLINE_ENABLED    = 0x08000000;
    private const ushort MOVEMENTFLAG2_INTERPOLATED   = 0x0010;
    private const ushort MOVEMENTFLAG2_PITCHING       = 0x0020;

    // UNIT_FIELD_* — индексы в descriptor array (значения = смещение / 4)
    private const int OBJECT_FIELD_GUID         = 0x00; // 2 ints
    private const int OBJECT_FIELD_TYPE         = 0x02;
    private const int OBJECT_FIELD_ENTRY        = 0x03;
    private const int OBJECT_FIELD_SCALE_X      = 0x04;
    private const int UNIT_FIELD_CHARMEDBY      = 0x08; // 2 ints
    private const int UNIT_FIELD_SUMMONEDBY     = 0x0A; // 2 ints
    private const int UNIT_FIELD_TARGET         = 0x12; // 2 ints
    private const int UNIT_FIELD_HEALTH         = 0x18;
    private const int UNIT_FIELD_MAXHEALTH      = 0x20;
    private const int UNIT_FIELD_LEVEL          = 0x36;
    private const int UNIT_FIELD_FACTIONTEMPLATE= 0x37;
    private const int UNIT_FIELD_FLAGS          = 0x3B;
    private const int UNIT_FIELD_DISPLAYID      = 0x3E;

    public sealed class ParseStats
    {
        public int Created;
        public int Updated;
        public int Removed;
        public int Errors;
    }

    public static ParseStats Parse(byte[] body, WorldState state)
    {
        var stats = new ParseStats();
        try
        {
            var br = new BinaryReader(new MemoryStream(body));
            var numUpdates = br.ReadUInt32();
            if (numUpdates > 10000) return stats; // санити

            for (var i = 0; i < numUpdates; i++)
            {
                if (br.BaseStream.Position >= br.BaseStream.Length) break;
                try
                {
                    var updateType = br.ReadByte();
                    switch (updateType)
                    {
                        case UPDATETYPE_VALUES:
                        {
                            var guid = ReadPackedGuid(br);
                            var entity = state.Get(guid) ?? state.GetOrCreate(guid, WowObjectType.Object);
                            ReadValuesBlock(br, entity);
                            entity.LastSeen = DateTime.UtcNow;
                            stats.Updated++;
                            break;
                        }
                        case UPDATETYPE_MOVEMENT:
                        {
                            var guid = ReadPackedGuid(br);
                            var entity = state.Get(guid) ?? state.GetOrCreate(guid, WowObjectType.Object);
                            ReadMovementBlock(br, entity, isCreate: false);
                            entity.LastSeen = DateTime.UtcNow;
                            stats.Updated++;
                            break;
                        }
                        case UPDATETYPE_CREATE_OBJECT:
                        case UPDATETYPE_CREATE_OBJECT2:
                        {
                            var guid = ReadPackedGuid(br);
                            var typeId = br.ReadByte();
                            var entity = state.GetOrCreate(guid, (WowObjectType)typeId);
                            ReadMovementBlock(br, entity, isCreate: true);
                            ReadValuesBlock(br, entity);
                            entity.LastSeen = DateTime.UtcNow;
                            stats.Created++;
                            break;
                        }
                        case UPDATETYPE_OUT_OF_RANGE:
                        {
                            var n = br.ReadUInt32();
                            for (var k = 0; k < n; k++)
                            {
                                var g = ReadPackedGuid(br);
                                if (state.Remove(g)) stats.Removed++;
                            }
                            break;
                        }
                        case UPDATETYPE_NEAR_OBJECTS:
                        {
                            var n = br.ReadUInt32();
                            for (var k = 0; k < n; k++) ReadPackedGuid(br);
                            break;
                        }
                        default:
                            // неизвестный updateType — стрим скорее всего desync, выходим
                            stats.Errors++;
                            return stats;
                    }
                }
                catch (Exception)
                {
                    stats.Errors++;
                    return stats; // на любой ошибке прекращаем — стрим уже не валидный
                }
            }
        }
        catch (Exception)
        {
            stats.Errors++;
        }
        return stats;
    }

    // ---------- Movement Block ----------
    // Формат зависит от updateFlags (uint16), читаемых первыми.
    // Для нашего use-case: интересны позиция и (опционально) скорости.
    // Спайны и transport пропускаем максимально аккуратно.
    private static void ReadMovementBlock(BinaryReader br, WorldEntity entity, bool isCreate)
    {
        if (!isCreate)
        {
            // Для UPDATETYPE_MOVEMENT (без CREATE) сразу идёт MovementInfo без updateFlags.
            // Но это редкий случай — обычно сервер шлёт CREATE→VALUES→VALUES, не отдельный MOVEMENT.
            // Безопасно прочитаем минимально.
            ReadFullMovementInfo(br, entity);
            return;
        }

        var updateFlags = br.ReadUInt16();

        if ((updateFlags & UPDATEFLAG_LIVING) != 0)
        {
            ReadFullMovementInfo(br, entity);
        }
        else if ((updateFlags & UPDATEFLAG_POSITION) != 0)
        {
            // Vehicle/transport partial movement
            ReadPackedGuid(br); // transport guid
            entity.X = br.ReadSingle();
            entity.Y = br.ReadSingle();
            entity.Z = br.ReadSingle();
            br.ReadSingle(); // t_x
            br.ReadSingle(); // t_y
            br.ReadSingle(); // t_z
            entity.Orientation = br.ReadSingle();
            br.ReadSingle(); // t_ori
            if ((updateFlags & UPDATEFLAG_TRANSPORT) != 0)
                br.ReadUInt32(); // path timer
        }
        else if ((updateFlags & UPDATEFLAG_STATIONARY_POSITION) != 0)
        {
            entity.X = br.ReadSingle();
            entity.Y = br.ReadSingle();
            entity.Z = br.ReadSingle();
            entity.Orientation = br.ReadSingle();
        }

        // optional fields после movement
        if ((updateFlags & UPDATEFLAG_UNK) != 0) br.ReadUInt32();
        if ((updateFlags & UPDATEFLAG_LOWGUID) != 0) br.ReadUInt32();
        if ((updateFlags & UPDATEFLAG_HAS_TARGET) != 0) ReadPackedGuid(br);
        if ((updateFlags & UPDATEFLAG_TRANSPORT) != 0)
            br.ReadUInt32(); // path timer (если не было выше)
        if ((updateFlags & UPDATEFLAG_VEHICLE) != 0)
        {
            br.ReadUInt32(); // vehicle id
            br.ReadSingle(); // facing
        }
        if ((updateFlags & UPDATEFLAG_ROTATION) != 0)
            br.ReadInt64(); // rotation
    }

    private static void ReadFullMovementInfo(BinaryReader br, WorldEntity entity)
    {
        var movementFlags = br.ReadUInt32();
        var movementFlags2 = br.ReadUInt16();
        br.ReadUInt32(); // timestamp
        entity.X = br.ReadSingle();
        entity.Y = br.ReadSingle();
        entity.Z = br.ReadSingle();
        entity.Orientation = br.ReadSingle();

        if ((movementFlags & MOVEMENTFLAG_ONTRANSPORT) != 0)
        {
            ReadPackedGuid(br); // transport guid
            br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); // t pos+ori
            br.ReadUInt32(); // t_time
            br.ReadByte();   // t_seat
            if ((movementFlags2 & MOVEMENTFLAG2_INTERPOLATED) != 0) br.ReadUInt32();
        }

        if ((movementFlags & (MOVEMENTFLAG_SWIMMING | MOVEMENTFLAG_FLYING)) != 0
            || (movementFlags2 & MOVEMENTFLAG2_PITCHING) != 0)
            br.ReadSingle(); // pitch

        br.ReadUInt32(); // fall_time

        if ((movementFlags & MOVEMENTFLAG_FALLING) != 0)
        {
            br.ReadSingle(); // jump velocity
            br.ReadSingle(); // jump sin
            br.ReadSingle(); // jump cos
            br.ReadSingle(); // jump xy speed
        }
        if ((movementFlags & MOVEMENTFLAG_SPLINE_ELEVATION) != 0)
            br.ReadSingle();

        // 9 скоростей — всегда после остального
        for (var s = 0; s < 9; s++) br.ReadSingle();

        if ((movementFlags & MOVEMENTFLAG_SPLINE_ENABLED) != 0)
        {
            // Спайн: переменной длины, но аккуратно скипнем
            ReadSplineBlock(br);
        }
    }

    private static void ReadSplineBlock(BinaryReader br)
    {
        // SplineFlags + (опц.) точка/угол/таргет + длительности + точки
        var splineFlags = br.ReadUInt32();
        const uint SPLINEFLAG_FINAL_POINT = 0x20000;
        const uint SPLINEFLAG_FINAL_TARGET = 0x40000;
        const uint SPLINEFLAG_FINAL_ANGLE = 0x80000;

        if ((splineFlags & SPLINEFLAG_FINAL_ANGLE) != 0) br.ReadSingle();
        else if ((splineFlags & SPLINEFLAG_FINAL_TARGET) != 0) br.ReadUInt64();
        else if ((splineFlags & SPLINEFLAG_FINAL_POINT) != 0)
        { br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); }

        br.ReadUInt32(); // time passed
        br.ReadUInt32(); // duration
        br.ReadUInt32(); // splineId
        br.ReadSingle(); br.ReadSingle();           // unk float pair
        br.ReadSingle(); br.ReadSingle();           // unk float pair
        br.ReadUInt32(); // splineMode
        var pointsCount = br.ReadUInt32();
        if (pointsCount > 1024) throw new IOException("spline points too big — desync");
        for (var i = 0; i < pointsCount; i++)
        { br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); }
        br.ReadByte();   // splineMode2
        br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); // final dest
    }

    // ---------- Values Block ----------
    private static void ReadValuesBlock(BinaryReader br, WorldEntity entity)
    {
        var blockCount = br.ReadByte();
        if (blockCount == 0) return;
        if (blockCount > 60) throw new IOException("blockCount > 60 — desync");

        var mask = new uint[blockCount];
        for (var i = 0; i < blockCount; i++) mask[i] = br.ReadUInt32();

        for (var b = 0; b < blockCount; b++)
        {
            if (mask[b] == 0) continue;
            for (var bit = 0; bit < 32; bit++)
            {
                if ((mask[b] & (1u << bit)) == 0) continue;
                var fieldIdx = b * 32 + bit;
                var val = br.ReadUInt32();
                ApplyField(entity, fieldIdx, val);
            }
        }
    }

    private static uint? _pendingTargetLow, _pendingCharmLow, _pendingSummonLow;

    private static void ApplyField(WorldEntity e, int idx, uint val)
    {
        switch (idx)
        {
            case OBJECT_FIELD_TYPE:           /* TypeMask, не TypeId */ break;
            case OBJECT_FIELD_ENTRY:          e.Entry = val; break;
            case UNIT_FIELD_HEALTH:           e.Health = (int)val; break;
            case UNIT_FIELD_MAXHEALTH:        e.MaxHealth = (int)val; break;
            case UNIT_FIELD_LEVEL:            e.Level = (byte)val; break;
            case UNIT_FIELD_FACTIONTEMPLATE:  e.FactionTemplate = val; break;
            case UNIT_FIELD_FLAGS:            e.UnitFlags = val; break;
            case UNIT_FIELD_DISPLAYID:        e.DisplayId = val; break;

            // GUID-поля — пара uint32 (low + high)
            case UNIT_FIELD_TARGET:           _pendingTargetLow = val; break;
            case UNIT_FIELD_TARGET + 1:
                if (_pendingTargetLow.HasValue) {
                    e.Target = ((ulong)val << 32) | _pendingTargetLow.Value;
                    _pendingTargetLow = null;
                }
                break;
            case UNIT_FIELD_CHARMEDBY:        _pendingCharmLow = val; break;
            case UNIT_FIELD_CHARMEDBY + 1:
                if (_pendingCharmLow.HasValue) {
                    e.CharmedBy = ((ulong)val << 32) | _pendingCharmLow.Value;
                    _pendingCharmLow = null;
                }
                break;
            case UNIT_FIELD_SUMMONEDBY:       _pendingSummonLow = val; break;
            case UNIT_FIELD_SUMMONEDBY + 1:
                if (_pendingSummonLow.HasValue) {
                    e.SummonedBy = ((ulong)val << 32) | _pendingSummonLow.Value;
                    _pendingSummonLow = null;
                }
                break;
        }
    }

    // ---------- helpers ----------
    private static ulong ReadPackedGuid(BinaryReader br)
    {
        var mask = br.ReadByte();
        ulong guid = 0;
        for (var i = 0; i < 8; i++)
            if ((mask & (1 << i)) != 0)
                guid |= (ulong)br.ReadByte() << (i * 8);
        return guid;
    }
}
