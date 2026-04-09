// Standalone парсер Spell.dbc → JSON для WoW 3.3.5a (build 12340)
// Формат DBC: Header(20 bytes) + Records + StringTable
// SpellEntry: 234 поля × 4 байта = 936 байт (но реальный размер зависит от массивов)
//
// Запуск: dotnet run -- "C:\Проекты\WoW-MMaps-Data\dbc\Spell.dbc" "C:\Проекты\wow-bot\SpellDatabase.json"

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if (args.Length < 2)
{
    Console.WriteLine("Usage: SpellDbcParser <Spell.dbc path> <output.json path> [locale: 0=enUS 8=ruRU]");
    return;
}

string dbcPath = args[0];
string outputPath = args[1];
int locale = args.Length > 2 ? int.Parse(args[2]) : 8; // ruRU по умолчанию

Console.WriteLine($"Parsing {dbcPath} (locale={locale})...");

using var fs = new FileStream(dbcPath, FileMode.Open, FileAccess.Read);
using var reader = new BinaryReader(fs, Encoding.UTF8);

// Header
int signature = reader.ReadInt32();
if (signature != 0x43424457) // 'WDBC'
{
    Console.WriteLine("Not a DBC file!");
    return;
}
int recordCount = reader.ReadInt32();
int fieldCount = reader.ReadInt32();
int recordSize = reader.ReadInt32();
int stringTableSize = reader.ReadInt32();

Console.WriteLine($"Records: {recordCount}, Fields: {fieldCount}, RecordSize: {recordSize}, StringTable: {stringTableSize}");

// Читаем всю data секцию
long dataStart = fs.Position;
long stringTableStart = dataStart + (long)recordCount * recordSize;

// Читаем string table
byte[] stringTable;
{
    long savedPos = fs.Position;
    fs.Position = stringTableStart;
    stringTable = reader.ReadBytes(stringTableSize);
    fs.Position = savedPos;
}

string GetString(uint offset)
{
    if (offset >= stringTable.Length) return "";
    int end = (int)offset;
    while (end < stringTable.Length && stringTable[end] != 0) end++;
    return Encoding.UTF8.GetString(stringTable, (int)offset, end - (int)offset);
}

// Парсим записи — минимальный набор полей для бота
// Полная структура SpellEntry = 234 uint/int/float полей
// Нам нужны: ID, Name, Rank, CastTime, Cooldown, Range, ManaCost, Effects, School, Level

var spells = new List<Dictionary<string, object>>();
int nameFieldCount = 16; // 16 локалей

for (int i = 0; i < recordCount; i++)
{
    long recordStart = dataStart + (long)i * recordSize;
    fs.Position = recordStart;

    // Читаем весь рекорд как uint массив
    int fieldTotal = recordSize / 4;
    uint[] fields = new uint[fieldTotal];
    for (int f = 0; f < fieldTotal; f++)
        fields[f] = reader.ReadUInt32();

    uint id = fields[0];
    uint category = fields[1];
    uint dispel = fields[2];
    uint mechanic = fields[3];
    uint attributes = fields[4];

    // CastingTimeIndex (поле 28), RecoveryTime (29), CategoryRecoveryTime (30)
    uint castTimeIndex = fields[28];
    uint recoveryTime = fields[29];
    uint catRecoveryTime = fields[30];

    // SpellLevel (39), BaseLevel (38)
    uint baseLevel = fields[38];
    uint spellLevel = fields[39];

    // DurationIndex (40)
    uint durationIndex = fields[40];

    // PowerType (41), ManaCost (42)
    uint powerType = fields[41];
    uint manaCost = fields[42];

    // RangeIndex (46)
    uint rangeIndex = fields[46];

    // Speed (47) — float
    float speed = BitConverter.ToSingle(BitConverter.GetBytes(fields[47]), 0);

    // StackAmount (49)
    uint stackAmount = fields[49];

    // Effect[0..2] (71-73)
    uint effect0 = fields[71];
    uint effect1 = fields[72];
    uint effect2 = fields[73];

    // EffectBasePoints[0..2] (80-82)
    int bp0 = (int)fields[80];
    int bp1 = (int)fields[81];
    int bp2 = (int)fields[82];

    // EffectApplyAuraName[0..2] (95-97)
    uint aura0 = fields[95];
    uint aura1 = fields[96];
    uint aura2 = fields[97];

    // EffectAmplitude[0..2] (98-100) — tick period
    uint amp0 = fields[98];
    uint amp1 = fields[99];
    uint amp2 = fields[100];

    // SpellName[locale] — поля 136-151 (16 локалей)
    uint nameOffset = fields[136 + locale];
    string name = GetString(nameOffset);

    // SpellNameFlag (152)
    // Rank[locale] — поля 153-168
    uint rankOffset = fields[153 + locale];
    string rank = GetString(rankOffset);

    // SchoolMask (225)
    uint schoolMask = fields[225];

    // SpellFamilyName (208) — класс
    uint spellFamily = fields[208];

    // ManaCostPercentage (204)
    uint manaCostPct = fields[204];

    // MaxAffectedTargets (212)
    uint maxTargets = fields[212];

    // DmgClass (213)
    uint dmgClass = fields[213];

    // Пропускаем пустые/служебные спеллы
    if (string.IsNullOrEmpty(name)) continue;

    var spell = new Dictionary<string, object>
    {
        ["id"] = id,
        ["name"] = name,
        ["rank"] = rank,
        ["level"] = spellLevel,
        ["baseLevel"] = baseLevel,
        ["school"] = schoolMask,
        ["powerType"] = powerType,
        ["manaCost"] = manaCost,
        ["manaCostPct"] = manaCostPct,
        ["castTimeIndex"] = castTimeIndex,
        ["cooldown"] = recoveryTime,
        ["categoryCooldown"] = catRecoveryTime,
        ["rangeIndex"] = rangeIndex,
        ["durationIndex"] = durationIndex,
        ["stackAmount"] = stackAmount,
        ["spellFamily"] = spellFamily,
        ["maxTargets"] = maxTargets,
        ["dmgClass"] = dmgClass,
        ["attributes"] = attributes,
        ["mechanic"] = mechanic,
        ["effects"] = new[]
        {
            new { effect = effect0, basePoints = bp0, aura = aura0, amplitude = amp0 },
            new { effect = effect1, basePoints = bp1, aura = aura1, amplitude = amp1 },
            new { effect = effect2, basePoints = bp2, aura = aura2, amplitude = amp2 },
        }
    };

    spells.Add(spell);
}

Console.WriteLine($"Parsed {spells.Count} spells with names");

// Пишем JSON
var options = new JsonSerializerOptions { WriteIndented = true };
string json = JsonSerializer.Serialize(spells, options);
File.WriteAllText(outputPath, json);
Console.WriteLine($"Written to {outputPath} ({new FileInfo(outputPath).Length / 1024 / 1024}MB)");
