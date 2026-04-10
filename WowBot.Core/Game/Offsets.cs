namespace WowBot.Core.Game;

/// <summary>
/// Оффсеты для WoW 3.3.5a Build 12340
/// </summary>
public static class Offsets
{
    // --- Глобальные указатели ---
    public const uint ClientConnection = 0x00C79CE0;
    public const uint ObjectManagerOffset = 0x2ED0;
    public const uint FirstObject = 0xAC;
    public const uint NextObject = 0x3C;
    public const uint LocalPlayerGuid = 0xC0;
    public const uint PlayerName = 0x00C79D18;

    // --- Объект ---
    public const uint ObjectType = 0x14;
    public const uint ObjectGuid = 0x30;
    public const uint ObjectDescriptors = 0x08;

    // --- Позиция юнита ---
    public const uint UnitPositionX = 0x798;
    public const uint UnitPositionY = 0x79C;
    public const uint UnitPositionZ = 0x7A0;
    public const uint UnitRotation = 0x7A8;

    // --- Дескрипторы юнита (индекс, НЕ умноженный на 4) ---
    // WoWCircle 3.3.5a - оффсеты отличаются от стандартного 12340
    public const uint UnitFieldTarget = 0x12;   // idx=18,19 (GUID = 2 слота)
    public const uint UnitFieldHealth = 0x18;    // idx=24  = 30414
    public const uint UnitFieldPower1 = 0x19;    // idx=25  = 6664 (Mana)
    public const uint UnitFieldPower2 = 0x1A;    // idx=26  (Rage)
    public const uint UnitFieldPower3 = 0x1B;    // idx=27  (Focus)
    public const uint UnitFieldPower4 = 0x1C;    // idx=28  (Energy)
    public const uint UnitFieldPower5 = 0x1D;    // idx=29  (Runic Power)
    public const uint UnitFieldMaxHealth = 0x20;  // idx=32  = 30414
    public const uint UnitFieldMaxPower1 = 0x21;  // idx=33  = 6664
    public const uint UnitFieldLevel = 0x36;      // idx=54  = 80
    public const uint UnitFieldFactionTemplate = 0x37; // idx=55 (рядом с level)
    public const uint UnitFieldDisplayId = 0x3E;  // idx=62
    public const uint UnitFieldFlags = 0x3B;      // idx=59
    public const uint UnitFieldBoundingRadius = 0x41; // idx=65, float — хитбокс модели
    public const uint UnitFieldCombatReach = 0x42;    // idx=66, float — дистанция мили-удара

    // --- Каст ---
    public const uint CurrentCastId = 0xA60;
    public const uint CurrentChannelId = 0xA6C;

    // --- Ауры/баффы ---
    public const uint AuraCount = 0xDD0;
    public const uint AuraTableBase = 0xC50;
    public const uint AuraTableAlt = 0xC58;
    public const uint AuraTableCountAlt = 0xC54;
    public const uint AuraSize = 0x18;
    public const uint AuraSpellId = 0x08;

    // --- DirectX / Lua ---
    public const uint DevicePtr1 = 0x00C5DF88;
    public const uint DevicePtr2Offset = 0x397C;
    public const uint EndSceneOffset = 0xA8;
    public const uint LuaDoString = 0x00819210;
    public const uint LuaGetLocalizedText = 0x007225E0;
    // Lua C API — найдены через дизассемблинг WoWCircle бинарника
    public const uint LuaState = 0x00D3F78C;
    public const uint LuaGetField = 0x0084E590;  // index2adr + luaS_new + luaV_gettable + top++ — настоящий lua_getfield
    public const uint LuaToLString = 0x0084E0E0;
    public const uint LuaSetTop = 0x0084DBF0;

    // --- Terrain Click (ground-targeted AoE) ---
    public const uint HandleTerrainClick = 0x00527830; // стандарт 12340, может быть другой на WoWCircle

    // --- Имена игроков (Name Store / Name Cache) ---
    public const uint NameStoreBase = 0x00C5D938;
    public const uint NameStoreBaseOffset = 0x08;
    public const uint NameStoreMask = 0x24;
    public const uint NameStoreBasePtr = 0x1C;
    public const uint NameStoreString = 0x20;

    // --- Имена NPC/мобов (Creature Name) ---
    public const uint UnitNamePtr1 = 0x964;
    public const uint UnitNamePtr2 = 0x05C;
}

public enum WowObjectType : int
{
    Object = 0,
    Item = 1,
    Container = 2,
    Unit = 3,
    Player = 4,
    GameObject = 5,
    DynamicObject = 6,
    Corpse = 7
}
