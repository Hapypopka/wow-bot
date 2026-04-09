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

// 3. Our spell IDs (all from rotations)
var ourSpellIds = new uint[] {
    // Shaman
    324, 403, 421, 8042, 8050, 17364, 51505, 60103, 61657, 30823, 51533, 32182, 2894, 58734, 53817,
    // Paladin
    35395, 53385, 20271, 26573, 48806, 48801, 31884, 20925, 19750, 642, 633, 54428, 31789, 31935, 24275,
    // Priest
    8092, 589, 2944, 34914, 15407, 15473, 47585, 33206, 33076, 34861, 47788, 47540, 47541, 14751, 29166,
    // Druid
    8921, 5176, 2912, 48505, 33831, 24858, 16857, 33878, 33876, 1822, 22568, 1079, 5221, 48438, 18562,
    // DK
    49998, 49143, 49020, 55090, 47541, 45462, 45477, 49184, 43265, 51271, 47568, 49206, 46584,
    // Hunter
    56641, 53209, 19434, 53351, 3044, 1978, 53301, 3674, 2643, 34026, 3045, 19574, 34471, 781, 34477, 1130, 136,
    // Warrior
    23881, 12294, 1680, 5308, 7384, 46968, 23922, 6343, 6572, 1464, 871, 2565, 1719, 18499, 2687,
    // Rogue
    1329, 32645, 1943, 5171, 2098, 51690, 51662, 1856, 31224, 13750, 13877,
    // Mage
    30451, 44425, 133, 11366, 44457, 44572, 116, 12042, 55342, 12051, 11129, 2948,
    // Warlock
    686, 348, 172, 980, 1120, 30108, 17962, 29722, 50796, 47193, 18220, 29858, 6229, 6789,
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
