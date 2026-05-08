using WowBot.Abstractions.Entities;
using WowBot.HeadlessPoc;

namespace WowBot.Adapter.Headless.Entities;

/// <summary>DynamicObject (AoE-лужи, консекрация). TODO Phase C: SpellId/Radius из UpdateFields.</summary>
internal sealed class HeadlessWowDynObject : IWowDynObject
{
    private readonly WorldEntity _e;

    public HeadlessWowDynObject(WorldEntity entity) { _e = entity; }

    public uint BaseAddress => 0;
    public ulong Guid => _e.Guid;

    // TODO Phase C: парсинг DYNAMICOBJECT_FIELD_SPELLID (0x6+0x4 = +0x14), RADIUS, CASTER.
    public int SpellId => 0;
    public float Radius => 0f;
    public ulong Caster => 0;

    public float X => _e.X;
    public float Y => _e.Y;
    public float Z => _e.Z;
}
