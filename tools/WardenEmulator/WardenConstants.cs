// Warden anti-cheat protocol constants for WoW 3.3.5a (build 12340).
// Порт WoWee/include/game/warden_constants.hpp.

namespace WowBot.WardenEmulator;

/// <summary>Sub-opcodes внутри SMSG_WARDEN_DATA / CMSG_WARDEN_DATA.</summary>
public static class WardenSubOpcodes
{
    // Server → Client
    public const byte SMSG_MODULE_USE          = 0x00;
    public const byte SMSG_MODULE_CACHE        = 0x01;
    public const byte SMSG_CHEAT_CHECKS_REQUEST = 0x02;
    public const byte SMSG_MODULE_INITIALIZE    = 0x03;
    public const byte SMSG_HASH_REQUEST         = 0x05;

    // Client → Server
    public const byte CMSG_MODULE_MISSING       = 0x00;
    public const byte CMSG_MODULE_OK            = 0x01;
    public const byte CMSG_CHEAT_CHECKS_RESULT  = 0x02;
    public const byte CMSG_HASH_RESULT          = 0x04;
}

/// <summary>PE-секции Wow.exe 3.3.5a (12340) при стандартной базе 0x400000.</summary>
public static class PeSections
{
    public const uint TextBase     = 0x400000;
    public const uint TextEnd      = 0x800000;
    public const uint RDataBase    = 0x7FF000;
    public const uint DataRawBase  = 0x827000;
    public const uint BssBase      = 0x883000;
    public const uint BssEnd       = 0xD06000;

    /// <summary>Windows KUSER_SHARED_DATA — read-only страница, всегда mapped.</summary>
    public const uint KUserSharedDataBase = 0x7FFE0000;
    public const uint KUserSharedDataEnd  = 0x7FFF0000;

    public const uint TickCountAddr   = 0x00CF0BC8;
    public const uint WinVersionAddr  = 0x7FFE026C;
}

/// <summary>Размеры pre-defined check'ов в байтах.</summary>
public static class CheckSizes
{
    public const uint CrHeader      = 17;
    public const uint CrEntry       = 68;
    public const uint PageCheck     = 29;
    public const uint PageAShort    = 24;
    public const uint KnownCodeScanOffset = 13856;
}

/// <summary>Result codes для memory-check.</summary>
public static class MemCheckResult
{
    public const byte Success    = 0x00;
    public const byte Unmapped   = 0xE9;
    public const byte PageFound  = 0x4A;
}
