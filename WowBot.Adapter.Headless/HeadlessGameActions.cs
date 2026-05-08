using System.Buffers.Binary;
using System.Text;
using WowBot.Abstractions;
using WowBot.Abstractions.Actions;
using WowBot.HeadlessPoc;

namespace WowBot.Adapter.Headless;

/// <summary>
/// IGameActions реализация для headless через CMSG-пакеты к серверу.
/// Все методы async — будут ждать ack/fail от сервера в Phase C (сейчас fire-and-forget).
/// </summary>
public sealed class HeadlessGameActions : IGameActions
{
    // CMSG opcodes 3.3.5a (TC: Common.h, Opcodes.h)
    private const uint CMSG_CAST_SPELL          = 0x12E;
    private const uint CMSG_CANCEL_CAST         = 0x12F;
    private const uint CMSG_SET_SELECTION       = 0x13D;
    private const uint CMSG_USE_ITEM            = 0xAB;
    private const uint CMSG_GAMEOBJ_USE         = 0xB1;   // INTERACT с GameObject
    private const uint CMSG_LOOT                = 0x15D;
    private const uint CMSG_LOOT_RELEASE        = 0x15F;
    private const uint CMSG_ATTACKSWING         = 0x141;
    private const uint CMSG_ATTACKSTOP          = 0x142;
    private const uint CMSG_PET_ACTION          = 0x175;

    // SpellCastTargets target mask flags
    private const uint TARGET_FLAG_SELF       = 0x00000000;
    private const uint TARGET_FLAG_UNIT       = 0x00000002;
    private const uint TARGET_FLAG_ITEM       = 0x00000010;
    private const uint TARGET_FLAG_SOURCE_LOC = 0x00000020;
    private const uint TARGET_FLAG_DEST_LOC   = 0x00000040;
    private const uint TARGET_FLAG_OBJECT     = 0x00004000;

    private readonly WorldClient _world;

    public HeadlessGameActions(WorldClient world) { _world = world; }

    public Task CastSpell(int spellId, ulong? target = null, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0);            // castCount
        w.Write((uint)spellId);      // spellId
        w.Write((byte)0);            // castFlags

        // SpellCastTargets
        if (target.HasValue && target.Value != 0 && target.Value != _world.LocalPlayerGuid)
        {
            w.Write(TARGET_FLAG_UNIT);
            WorldClient.WritePackedGuidPublic(w, target.Value);
        }
        else
        {
            w.Write(TARGET_FLAG_SELF);
        }
        return _world.SendCmsgAsync(CMSG_CAST_SPELL, ms.ToArray());
    }

    public Task CastSpellOnGround(int spellId, float x, float y, float z, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0);
        w.Write((uint)spellId);
        w.Write((byte)0);
        w.Write(TARGET_FLAG_DEST_LOC);
        WorldClient.WritePackedGuidPublic(w, _world.LocalPlayerGuid);   // src GUID — caster
        w.Write(x); w.Write(y); w.Write(z);
        return _world.SendCmsgAsync(CMSG_CAST_SPELL, ms.ToArray());
    }

    public Task UseItem(int itemId, CancellationToken ct = default)
    {
        // TODO Phase B+: для USE_ITEM нужен item GUID и bag/slot. Пока заглушка.
        // Требует парсинг inventory через SMSG_ITEM_PUSH_RESULT и/или CMSG_REQUEST_ACCOUNT_DATA.
        Console.WriteLine($"[HeadlessActions] UseItem({itemId}) — TODO: нужен item GUID lookup");
        return Task.CompletedTask;
    }

    public Task SetTarget(ulong guid, CancellationToken ct = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, guid);
        return _world.SendCmsgAsync(CMSG_SET_SELECTION, payload);
    }

    public Task ClearTarget(CancellationToken ct = default) => SetTarget(0, ct);

    public Task AttackTarget(ulong guid, CancellationToken ct = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, guid);
        return _world.SendCmsgAsync(CMSG_ATTACKSWING, payload);
    }

    public Task StopAttack(CancellationToken ct = default)
        => _world.SendCmsgAsync(CMSG_ATTACKSTOP, Array.Empty<byte>());

    public Task Interact(ulong guid, CancellationToken ct = default)
    {
        // CMSG_GAMEOBJ_USE для GameObject. Для Unit/Player другая логика — TODO Phase C.
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, guid);
        return _world.SendCmsgAsync(CMSG_GAMEOBJ_USE, payload);
    }

    public Task Loot(ulong corpseGuid, CancellationToken ct = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, corpseGuid);
        return _world.SendCmsgAsync(CMSG_LOOT, payload);
    }

    public Task SendChat(ChatType type, string message, string? whisperTo = null, CancellationToken ct = default)
    {
        // Делегируем в WorldClient — там уже реализованы все варианты с правильным форматом.
        return type switch
        {
            ChatType.Say     => _world.SayAsync(message),
            ChatType.Yell    => _world.YellAsync(message),
            ChatType.Guild   => _world.GuildAsync(message),
            ChatType.Whisper => _world.WhisperAsync(whisperTo ?? string.Empty, message),
            _ => Task.CompletedTask,    // TODO: Party/Officer/Channel
        };
    }

    public Task MoveTo(float x, float y, float z, CancellationToken ct = default)
    {
        // TODO Phase D: pathfinding через NavQuery + spline movement.
        // Сейчас заглушка — WorldClient умеет MoveForwardAsync(TimeSpan), но не goto-point с pathing.
        Console.WriteLine($"[HeadlessActions] MoveTo({x:F1},{y:F1},{z:F1}) — TODO Phase D (pathfinding)");
        return Task.CompletedTask;
    }

    public Task StopMovement(CancellationToken ct = default)
    {
        // TODO Phase B+: CMSG_MOVE_STOP (0xB7) с MovementInfo.
        Console.WriteLine($"[HeadlessActions] StopMovement() — TODO");
        return Task.CompletedTask;
    }

    public Task SendPetCommand(PetCommand command, ulong? target = null, CancellationToken ct = default)
    {
        // TODO Phase C: CMSG_PET_ACTION формат:
        //   uint64 petGuid
        //   uint32 actionData (action+slot encoding)
        //   uint64 targetGuid (если есть)
        Console.WriteLine($"[HeadlessActions] SendPetCommand({command}) — TODO Phase C");
        return Task.CompletedTask;
    }
}
