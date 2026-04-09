// Парсит SpellIcon.dbc → скачивает правильные иконки для всех spell ID из ротаций
// Запуск: dotnet run -- <SpellIcon.dbc path> <Icons output dir>

using System.Text;

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <SpellIcon.dbc> <Icons output dir>");
    Console.WriteLine("Example: dotnet run -- C:\\Проекты\\WoW-MMaps-Data\\dbc\\SpellIcon.dbc C:\\Проекты\\wow-bot\\WowBot.Injector\\Icons");
    return;
}

string spellIconDbcPath = args[0];
string outputDir = args[1];

// 1. Parse SpellIcon.dbc → Dictionary<uint iconId, string iconName>
Console.WriteLine("Parsing SpellIcon.dbc...");
var iconNames = new Dictionary<uint, string>();
using (var fs = new FileStream(spellIconDbcPath, FileMode.Open, FileAccess.Read))
using (var reader = new BinaryReader(fs, Encoding.UTF8))
{
    int sig = reader.ReadInt32(); // WDBC
    int recordCount = reader.ReadInt32();
    int fieldCount = reader.ReadInt32();
    int recordSize = reader.ReadInt32();
    int stringTableSize = reader.ReadInt32();

    long dataStart = fs.Position;
    long stringTableStart = dataStart + (long)recordCount * recordSize;

    // Read string table
    byte[] stringTable;
    long saved = fs.Position;
    fs.Position = stringTableStart;
    stringTable = reader.ReadBytes(stringTableSize);
    fs.Position = saved;

    string GetString(uint offset)
    {
        if (offset >= stringTable.Length) return "";
        int end = (int)offset;
        while (end < stringTable.Length && stringTable[end] != 0) end++;
        return Encoding.UTF8.GetString(stringTable, (int)offset, end - (int)offset);
    }

    for (int i = 0; i < recordCount; i++)
    {
        uint id = reader.ReadUInt32();
        uint nameOffset = reader.ReadUInt32();
        string fullPath = GetString(nameOffset); // e.g. "Interface\\Icons\\Spell_Nature_Lightning"
        string iconName = fullPath.Replace("Interface\\Icons\\", "").Replace("Interface/Icons/", "");
        iconNames[id] = iconName;
    }
    Console.WriteLine($"  {iconNames.Count} icons in SpellIcon.dbc");
}

// 2. Parse SpellDatabase.json → get spellId → spellIconId mapping
Console.WriteLine("Parsing SpellDatabase.json...");

// Re-parse Spell.dbc to get SpellIconID (field 133)
// SpellDatabase.json doesn't have iconId, so we need to read Spell.dbc again
string spellDbcPath = Path.Combine(Path.GetDirectoryName(spellIconDbcPath)!, "Spell.dbc");
var spellIcons = new Dictionary<uint, uint>(); // spellId → iconId

using (var fs = new FileStream(spellDbcPath, FileMode.Open, FileAccess.Read))
using (var reader = new BinaryReader(fs, Encoding.UTF8))
{
    int sig = reader.ReadInt32();
    int recordCount = reader.ReadInt32();
    int fieldCount = reader.ReadInt32();
    int recordSize = reader.ReadInt32();
    int stringTableSize = reader.ReadInt32();
    long dataStart = fs.Position;

    for (int i = 0; i < recordCount; i++)
    {
        long recordStart = dataStart + (long)i * recordSize;
        fs.Position = recordStart;
        uint[] fields = new uint[recordSize / 4];
        for (int f = 0; f < fields.Length; f++)
            fields[f] = reader.ReadUInt32();

        uint spellId = fields[0];
        uint iconId = fields[133]; // SpellIconID
        spellIcons[spellId] = iconId;
    }
    Console.WriteLine($"  {spellIcons.Count} spells with icon IDs");
}

// 3. Our spell IDs (ALL from rotations, buffs, UI toggles)
var ourSpellIds = new uint[] {
    // Warrior
    12294, 772, 7384, 5308, 1464, 46916, 845, 47449, // Arms: MS, Rend, OP, Execute, Slam, Intercept, Cleave, Heroic Strike
    23881, 1680, 2687, // Fury: Bloodthirst, Whirlwind, Blood Fury
    23922, 6343, 6572, 871, 2565, 46968, 1719, 18499, // Prot: Shield Slam, TC, Revenge, Shield Wall, Last Stand, Shockwave, Recklessness, Berserker Rage
    355, 20243, 12975, // Taunt, Devastate, Last Stand
    71, 2457, 2458, // Stances: Battle, Defensive, Berserker
    47440, 47436, // Shouts: Commanding, Battle

    // Paladin - Ret
    35395, 53385, 53408, 20271, 26573, 48801, 48806, 31884, 53601, 879, // CS, DS, Judge, Cons, Exo, HoW, AW, SS
    // Paladin - Prot
    20925, 31789, 31935, 24275, 2812, 53595, 53600, 1022, // HolyShield, RighteousDef, Avenger, HW, Cons, Hammer, Shield, BoP
    // Paladin - Holy
    48782, 48785, 48825, 53563, 54428, 20216, 48788, 4987, 1044, 1038, 20473, // HL, FL, HS, Beacon, Plea, DF, LoH, Cleanse, BoF, HoS, Exo
    633, 642, 19750, // LoH, DivineShield, FoL
    // Paladin - Seals
    31801, 20375, 20166, 20165, // SoV, SoC, SoW, SoL
    // Paladin - Blessings
    48932, 20217, 48936, 20911, // BoM, BoK, BoW, BoS
    // Paladin - Auras
    54043, 48942, 48945, 48947, 48943, 19746, 31821, 32223, // Auras: Ret, Dev, Frost, Fire, Shadow, Conc, AuraMastery, Crusader
    25780, // RighteousFury

    // Hunter
    56641, 53209, 19434, 53351, 3044, 1978, 53301, 3674, 2643, // Steady, Chimera, Aimed, Kill, Arcane, Serpent, Explosive, Black Arrow, Multi
    34026, 3045, 19574, 34471, 781, 34477, 1130, 136, // Kill Cmd, Rapid, Bestial, BeastWithin, Disengage, CallWild, Marks, Mend
    23989, 13813, 34490, 19801, 34074, 5384, // Readiness, Silence, Volley, Tranq, Aspect Hawk/Viper/Dragonhawk
    61846, 61847, 13165, // Aspects

    // Rogue
    1329, 32645, 1943, 5171, 2098, 51690, 51662, // Mutilate, Envenom, Rupture, SnD, Evis, KillingSpree, FanKnives
    1856, 31224, 13750, 13877, // Vanish, CloS, Adrenaline, BladeFlurry
    53, 16511, 1752, // Backstab, Hemorrhage, SinisterStrike
    51723, // HungerForBlood

    // Priest - Shadow
    15473, 47585, 34914, 2944, 589, 8092, 34433, 15407, 48045, // Shadowform, Dispersion, VT, DP, SWP, MB, SF, MF, MindSear
    // Priest - Disc
    17, 33206, 33076, 47540, 14751, 527, 552, 528, 2006, 139, 2061, 32546, // PW:S, PS, Penance, PoM, DivineSpirit, Renew, Abolish, DispelMagic, Res, Renew, Flash, Binding
    // Priest - Holy
    34861, 47788, 2060, // CoH, Guardian, GHeal
    // Priest Buffs
    48162, 48074, 48170, 15286, 48168, 6346, // Fort, Spirit, ShadowProt, VE, InnerFire, FearWard

    // DK - Blood
    49998, 49143, 55233, 43265, 45477, 45462, 50842, 55050, 56815, 48792, 56222, // DS, IT, PS, DnD, IT, PS, Pest, BloodBoil, RuneStrike, IB, VB
    // DK - Frost
    49020, 49184, 55095, 55078, 51271, 59052, 49143, 45529, 47568, 57330, // Oblit, HowlingBlast, FrostStrike, BloodStrike, UA, Rime, IT, BT, ERW, HoW
    // DK - Unholy
    55090, 49206, 49194, 47541, // ScourgeStrike, Gargoyle, UnholyBlight, DC
    // DK Presences
    48263, 48266, 48265, // Blood, Frost, Unholy
    57623, // HornOfWinter
    49222, // Bone Shield
    46584, // Raise Dead (ghoul)

    // Shaman - Enhancement
    324, 403, 421, 8042, 8050, 17364, 51505, 60103, 61657, 30823, 51533, 32182, 2894, 58734, 53817,
    // Shaman - Elemental
    51490, // Thunderstorm
    // Shaman - Resto
    974, 61295, 1064, 16188, 331, 55198, 8004, 51886, 526, // EarthShield, Riptide, ChainHeal, NatSwift, HW, LHW
    // Shaman - Totems
    8075, 8071, 8166, 8227, 5394, 5675, 8170, 3738, 2062, // SoE, Stoneskin, Tremor, Flametongue, HealStream, ManaSpring, Cleansing, FireRes, FrostRes
    57721, 3599, 8177, // Totem of Wrath, WindfuryTotem, WrathOfAir
    // Shaman - Shields & Weapons
    49281, 57960, // LightningShield, WaterShield
    58790, 58789, 8232, // WeaponEnch: FT, EL, WF

    // Mage - Arcane
    30451, 44425, 12042, 55342, 12051, 44401, // ArcBlast, ArcMissiles, ArcPower, MirrorImage, Evocation
    // Mage - Fire
    133, 11366, 44457, 11129, 2948, 48108, // Fireball, Pyro, LivingBomb, Combustion, Scorch, HotStreak
    // Mage - Frost
    44572, 44544, 116, 30455, 57761, 44614, // DeepFreeze, FrostfireBolt, Frostbolt, IceLance, BrainFreeze, FFB
    // Mage Buffs
    43002, 43024, 43020, 6117, // ArcBrilliance, MoltenArmor, FrostArmor, MageArmor

    // Warlock - Affliction
    686, 348, 172, 980, 1120, 30108, 48181, 30283, 63321, // SBolt, Immolate, Corruption, CoA, Drain, UA, Haunt, DarkPact
    // Warlock - Demo
    17962, 29722, 50796, 47193, 18220, 29858, 6229, 6789, 50589, 47241, 27243, 603, 1490, 63167, 71165, // Conflag, ChaosBolt, CBoil, DemonEmpower, DarkPact, Meta, ShadowWard, DeathCoil, ImmoAura
    // Warlock - Destro
    // Warlock Buffs
    47893, 47888, // FelArmor, Spellstone
    // Warlock - Curses
    47864, 47867, 47865, // CoA, CoD, CoE
    // Warlock Pets
    688, 691, 712, 697, 30146, // Imp, Felhunter, Succubus, Voidwalker, Felguard

    // Druid - Balance
    24858, 29166, 48505, 33831, 770, 5570, 8921, 2912, 5176, 48467, // Moonkin, Innervate, Starfall, Treants, FF, IS, MF, Starfire, Wrath, Hurricane
    // Druid - Feral Cat
    5217, 50334, 33876, 1822, 52610, 1079, 22568, 62078, 5221, 16857, 1082, // TigersFury, Berserk, Mangle, Rake, SavageRoar, Rip, FB, Shred, Swipe, FF, Claw
    // Druid - Feral Bear
    6795, 779, 6807, 33878, 33745, 22842, 22812, 61336, // Taunt, Tranq, Maul, MangleBear, Lacerate, Swipe, StarfireBear, Berserk
    // Druid - Resto
    33891, 50769, 774, 48438, 18562, 33763, 8936, 50464, 17116, 2782, 2893, 20484, // ToL, Nourish, Rejuv, WildGrowth, Swiftmend, Lifebloom, Regrowth, NatSwift, Remove, Innervate, Rebirth
    // Druid Buffs
    48470, 53307, // GiftWild, Thorns
    // Druid Forms
    768, 5487, 9634, // Cat, Bear, DireBear
};

// 4. Download icons
Console.WriteLine($"Downloading icons for {ourSpellIds.Length} spells...");
using var http = new HttpClient();
http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

int downloaded = 0, skipped = 0, failed = 0;
var iconMapping = new Dictionary<uint, string>(); // spellId → filename.jpg

foreach (uint spellId in ourSpellIds.Distinct())
{
    if (!spellIcons.TryGetValue(spellId, out uint iconId)) { Console.WriteLine($"  {spellId}: no icon ID"); failed++; continue; }
    if (!iconNames.TryGetValue(iconId, out string? iconName) || string.IsNullOrEmpty(iconName)) { Console.WriteLine($"  {spellId}: icon ID {iconId} not in SpellIcon.dbc"); failed++; continue; }

    string filename = iconName.ToLower() + ".jpg";
    string outputPath = Path.Combine(outputDir, filename);
    iconMapping[spellId] = filename;

    if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 500)
    {
        skipped++;
        continue;
    }

    string url = $"https://wow.zamimg.com/images/wow/icons/medium/{iconName.ToLower()}.jpg";
    try
    {
        var data = await http.GetByteArrayAsync(url);
        File.WriteAllBytes(outputPath, data);
        Console.WriteLine($"  {spellId} → {filename} ({data.Length} bytes)");
        downloaded++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {spellId} → FAILED {url}: {ex.Message}");
        failed++;
    }
}

Console.WriteLine($"\nDone: {downloaded} downloaded, {skipped} already exist, {failed} failed");

// 5. Output mapping
Console.WriteLine("\n=== SPELL ID → ICON FILE MAPPING ===");
foreach (var (id, file) in iconMapping.OrderBy(x => x.Key))
    Console.WriteLine($"  {id} → {file}");
