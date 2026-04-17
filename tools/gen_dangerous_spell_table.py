"""
Генератор DangerousSpellTable.g.cs — полная БД вражеских AoE для proactive avoidance.

Фильтр: спеллы с damage school (SchoolMask != 0) и хотя бы одним радиусом > 0
ИЛИ с эффектом PERSISTENT_AREA_AURA (создают зону). Плюс классификация TargetMode
по EffectImplicitTargetA — чтобы бот знал как именно уклоняться.

Запуск:
    python tools/gen_dangerous_spell_table.py

Вход:  D:/SPP/SPP_Classics_V2/SPP_Server/Modules/wotlk/dbc/{Spell,SpellRadius}.dbc
       D:/Games/Вов/Data/ruRU/patch-ruRU-*.MPQ (русские имена)
Выход: WowBot.Core/Game/Generated/DangerousSpellTable.g.cs
       docs/dangerous_spell_table.md
"""
import struct
import os

DBC_DIR = 'D:/SPP/SPP_Classics_V2/SPP_Server/Modules/wotlk/dbc'
SPELL_DBC = os.path.join(DBC_DIR, 'Spell.dbc')
RADIUS_DBC = os.path.join(DBC_DIR, 'SpellRadius.dbc')
CLIENT_MPQ_DIR = 'D:/Games/Вов/Data/ruRU'
CLIENT_MPQS_PRIORITY = [
    'patch-ruRU-4.MPQ', 'patch-ruRU-3.MPQ', 'patch-ruRU-2.MPQ',
    'patch-ruRU.MPQ', 'locale-ruRU.MPQ',
]
OUT_CS = 'WowBot.Core/Game/Generated/DangerousSpellTable.g.cs'
OUT_DOC = 'docs/dangerous_spell_table.md'

# Spell.dbc field offsets (verified для 234 fields × 4 = 936 bytes)
FIELD_ID = 0
FIELD_EFFECT = 71            # Effect[0..2]
FIELD_IMPLICIT_TARGET_A = 86 # EffectImplicitTargetA[0..2]
FIELD_RADIUS_IDX = 92        # EffectRadiusIndex[0..2]
FIELD_APPLY_AURA = 95        # EffectApplyAuraName[0..2]
FIELD_AMPLITUDE = 98         # EffectAmplitude[0..2] (period мс)
FIELD_NAME_RU = 136 + 8      # ruRU локаль
FIELD_NAME_EN = 136 + 0
FIELD_SCHOOL_MASK = 225

# SpellEffect типы которые могут навредить в AoE
EFFECT_SCHOOL_DAMAGE = 2
EFFECT_DUMMY = 3
EFFECT_APPLY_AURA = 6
EFFECT_PERSISTENT_AREA_AURA = 27
EFFECT_SUMMON = 28
EFFECT_WEAPON_DAMAGE = 58
EFFECT_WEAPON_DAMAGE_NOSCHOOL = 2
EFFECT_APPLY_AREA_AURA_ENEMY = 129
EFFECT_APPLY_AREA_AURA_FRIEND = 53
EFFECT_APPLY_AREA_AURA_PARTY = 35

HARMFUL_EFFECTS = {
    EFFECT_SCHOOL_DAMAGE,
    EFFECT_APPLY_AURA,
    EFFECT_PERSISTENT_AREA_AURA,
    EFFECT_APPLY_AREA_AURA_ENEMY,
    58, 59, 62, 64,  # Weapon damage variants, health leech
}

# Aura типы — positive (хилы, бафы) исключаем
POSITIVE_AURAS = {
    8,    # PERIODIC_HEAL
    10,   # MOD_THREAT (может быть good или bad)
    21,   # OBS_MOD_POWER
    62,   # POWER_BURN (опасно!)  — НЕ исключать
    85,   # MOD_POWER_REGEN
    84,   # MOD_INCREASE_ENERGY
}

# Таргет-типы WotLK (EffectImplicitTargetA). Маппим в TargetMode.
# Enum TargetMode: Unknown, GroundTargeted, AroundCaster, AroundTarget, Cone, Frontal
TARGET_MODE = {
    # Около кастера — бежать от кастера
    15: 'AroundCaster',   # TARGET_UNIT_SRC_AREA_ENEMY
    22: 'AroundCaster',   # TARGET_UNIT_CASTER_AREA_*
    37: 'AroundCaster',   # TARGET_UNIT_CASTER_AREA_PARTY
    53: 'AroundCaster',   # TARGET_DEST_CASTER_BACK
    56: 'AroundCaster',   # TARGET_UNIT_RAID_CASTER

    # Ground-targeted — отойти с текущей позиции (лужа падает на точку)
    28: 'GroundTargeted', # TARGET_UNIT_DEST_AREA_ENEMY
    30: 'GroundTargeted', # TARGET_UNIT_DEST_AREA_ALLY
    31: 'GroundTargeted', # TARGET_UNIT_DEST_AREA_ENTRY
    45: 'GroundTargeted', # TARGET_UNIT_TARGET_CHAINHEAL_ALLY — chain
    87: 'GroundTargeted', # TARGET_DEST_DEST (ground cursor)
    88: 'GroundTargeted', # TARGET_DEST_DEST_BACK / similar
    104: 'GroundTargeted',# TARGET_DEST_DEST_SIDE
    107: 'GroundTargeted',# TARGET_DEST_DEST_RADIUS

    # Вокруг цели — если я цель, бежать от источника
    8:  'AroundTarget',   # TARGET_UNIT_DEST_AREA_ENEMY (?)
    25: 'AroundTarget',   # TARGET_UNIT_TARGET_ANY
    77: 'AroundTarget',   # TARGET_UNIT_TARGET_AREA
    78: 'AroundTarget',

    # Конус перед кастером
    54: 'Cone',           # TARGET_UNIT_CONE_ENEMY_54
    60: 'Cone',           # TARGET_UNIT_CONE_ENEMY_104
    65: 'Cone',           # CASTER_ORIENTATION / cone
    104: 'Cone' if False else 'GroundTargeted',  # fallback

    # Фронт (180°)
    50: 'Frontal',

    # Single-target — не AoE
    1:  'SingleTarget',   # SELF
    6:  'SingleTarget',   # TARGET_UNIT_TARGET_ENEMY
    7:  'SingleTarget',   # TARGET_UNIT_TARGET_ALLY
    24: 'SingleTarget',
}


def load_spell_radius(path):
    with open(path, 'rb') as f:
        data = f.read()
    sig, nrec, _, recsize, _ = struct.unpack_from('<4s4i', data, 0)
    assert sig == b'WDBC'
    radius_map = {}
    for i in range(nrec):
        off = 20 + i * recsize
        rid = struct.unpack_from('<I', data, off)[0]
        r = struct.unpack_from('<f', data, off + 4)[0]
        radius_map[rid] = r
    return radius_map


def read_string(strings, offset):
    if offset >= len(strings):
        return ''
    end = offset
    while end < len(strings) and strings[end] != 0:
        end += 1
    try:
        return strings[offset:end].decode('utf-8', errors='replace')
    except Exception:
        return ''


def load_ruru_name_map():
    try:
        import mpyq
    except ImportError:
        print('WARN: mpyq не установлен')
        return {}
    name_map = {}
    for mpq_name in CLIENT_MPQS_PRIORITY:
        mpq_path = os.path.join(CLIENT_MPQ_DIR, mpq_name)
        if not os.path.exists(mpq_path):
            continue
        try:
            archive = mpyq.MPQArchive(mpq_path)
            dbc_data = archive.read_file('DBFilesClient\\Spell.dbc')
            if not dbc_data:
                continue
        except Exception:
            continue
        sig, nrec, nfield, recsize, strsize = struct.unpack_from('<4s4i', dbc_data, 0)
        if sig != b'WDBC':
            continue
        str_start = 20 + nrec * recsize
        strings = dbc_data[str_start:str_start + strsize]
        for i in range(nrec):
            off = 20 + i * recsize
            fields = struct.unpack_from(f'<{nfield}I', dbc_data, off)
            sid = fields[0]
            if sid in name_map:
                continue
            ru = read_string(strings, fields[136 + 8])
            if ru:
                name_map[sid] = ru
    return name_map


def classify_target_mode(implicit_targets, effects):
    """Выбираем доминирующий TargetMode по эффектам с радиусом."""
    modes = []
    for slot in range(3):
        if effects[slot] == 0:
            continue
        t = implicit_targets[slot]
        mode = TARGET_MODE.get(t, 'Unknown')
        if mode != 'SingleTarget':
            modes.append(mode)
    if not modes:
        return 'Unknown'
    # Приоритет: GroundTargeted > AroundCaster > AroundTarget > Cone > Frontal > Unknown
    priority = ['GroundTargeted', 'AroundCaster', 'AroundTarget', 'Cone', 'Frontal', 'Unknown']
    for p in priority:
        if p in modes:
            return p
    return 'Unknown'


def generate():
    radius_map = load_spell_radius(RADIUS_DBC)
    print(f'SpellRadius: {len(radius_map)} entries')

    with open(SPELL_DBC, 'rb') as f:
        data = f.read()
    _, nrec, nfield, recsize, strsize = struct.unpack_from('<4s4i', data, 0)
    str_start = 20 + nrec * recsize
    strings = data[str_start:str_start + strsize]
    print(f'Spell.dbc: {nrec} spells')

    ru_names = load_ruru_name_map()
    print(f'Russian names: {len(ru_names)}')

    # spell_id → {radius, mode, name, school}
    found = {}
    for i in range(nrec):
        off = 20 + i * recsize
        fields = struct.unpack_from('<234I', data, off)

        spell_id = fields[FIELD_ID]
        school_mask = fields[FIELD_SCHOOL_MASK]

        effects = fields[FIELD_EFFECT:FIELD_EFFECT + 3]
        implicit_a = fields[FIELD_IMPLICIT_TARGET_A:FIELD_IMPLICIT_TARGET_A + 3]
        radius_idx = fields[FIELD_RADIUS_IDX:FIELD_RADIUS_IDX + 3]
        auras = fields[FIELD_APPLY_AURA:FIELD_APPLY_AURA + 3]
        periods = fields[FIELD_AMPLITUDE:FIELD_AMPLITUDE + 3]

        # Нужен хотя бы один эффект который может быть опасен
        harmful_slots = [slot for slot in range(3) if effects[slot] in HARMFUL_EFFECTS]
        if not harmful_slots:
            continue

        # Хотя бы один positional effect (не single-target)
        has_area = False
        max_radius = 0.0
        for slot in harmful_slots:
            t = implicit_a[slot]
            if TARGET_MODE.get(t) == 'SingleTarget':
                continue
            ridx = radius_idx[slot]
            if ridx == 0:
                # PERSISTENT_AREA_AURA без radius_idx в самом слоте — используем любой другой
                continue
            r = radius_map.get(ridx, 0)
            if r <= 0 or r >= 100.0:
                continue
            # PERSISTENT_AREA_AURA должна иметь aura или period
            if effects[slot] == EFFECT_PERSISTENT_AREA_AURA:
                if auras[slot] == 0 and periods[slot] == 0:
                    continue
            has_area = True
            if r > max_radius:
                max_radius = r

        # Для spells без radius_idx но с PERSISTENT_AREA_AURA — берём из любого другого эффекта
        if not has_area:
            for slot in range(3):
                if effects[slot] != EFFECT_PERSISTENT_AREA_AURA:
                    continue
                if auras[slot] == 0 and periods[slot] == 0:
                    continue
                for any_slot in range(3):
                    if radius_idx[any_slot] == 0:
                        continue
                    r = radius_map.get(radius_idx[any_slot], 0)
                    if 0 < r < 100:
                        has_area = True
                        max_radius = max(max_radius, r)
                        break
                if has_area:
                    break

        if not has_area:
            continue

        # Фильтр: нужен damage school (иначе бафф) — НО: PERSISTENT_AREA_AURA без school
        # тоже включаем, т.к. может быть вредный (Void Zone и т.п.)
        is_persistent = any(effects[s] == EFFECT_PERSISTENT_AREA_AURA for s in harmful_slots)
        if school_mask == 0 and not is_persistent:
            continue

        mode = classify_target_mode(implicit_a, effects)
        if mode == 'SingleTarget':
            continue

        name = ru_names.get(spell_id) or read_string(strings, fields[FIELD_NAME_EN])
        found[spell_id] = {
            'radius': max_radius,
            'mode': mode,
            'name': name,
            'school': school_mask,
            'effects': effects,
        }

    print(f'Dangerous AoE spells: {len(found)}')

    # Статистика по mode
    by_mode = {}
    for sid, info in found.items():
        by_mode.setdefault(info['mode'], 0)
        by_mode[info['mode']] += 1
    print('\nПо TargetMode:')
    for m, cnt in sorted(by_mode.items(), key=lambda x: -x[1]):
        print(f'  {m}: {cnt}')

    write_cs(found)
    write_markdown(found)

    # Verify key ICC/raid spells
    print('\nВерификация ICC/raid:')
    for sid, label in [(72754, 'Defile'), (69076, 'Bone Storm'), (72299, 'Malleable Goo'),
                       (70447, 'Volatile Ooze'), (71837, 'Vile Gas'), (73655, 'Harvest Soul'),
                       (70541, 'Infest'), (16914, 'Hurricane'), (42940, 'Blizzard')]:
        info = found.get(sid)
        if info:
            print(f'  {sid} {label}: r={info["radius"]}y mode={info["mode"]} [{info["name"]}]')
        else:
            print(f'  {sid} {label}: НЕТ')


def write_cs(found):
    lines = [
        '// <auto-generated>',
        '// tools/gen_dangerous_spell_table.py → Spell.dbc + SpellRadius.dbc + patch-ruRU.MPQ',
        '// </auto-generated>',
        '',
        'using System.Collections.Generic;',
        '',
        'namespace WowBot.Core.Game.Generated;',
        '',
        'public enum AoETargetMode : byte',
        '{',
        '    Unknown = 0,',
        '    GroundTargeted = 1,  // лужа падает в точку, отойти с позиции',
        '    AroundCaster = 2,    // вокруг кастера, убежать от кастера на >radius',
        '    AroundTarget = 3,    // вокруг цели каста (часто = игрок), убежать',
        '    Cone = 4,            // конус перед кастером, отойти вбок',
        '    Frontal = 5,         // фронтальная полоса, отойти в сторону',
        '}',
        '',
        'public readonly record struct DangerousSpell(',
        '    int Id,',
        '    float Radius,',
        '    AoETargetMode Mode,',
        '    string Name);',
        '',
        'public static class DangerousSpellTable',
        '{',
        f'    public static readonly IReadOnlyDictionary<int, DangerousSpell> All = new Dictionary<int, DangerousSpell>({len(found)})',
        '    {',
    ]
    for sid in sorted(found.keys()):
        info = found[sid]
        # C# verbatim string (@"...") — только "" экранировать, всё остальное as-is
        name_escaped = info['name'].replace('"', '""').replace('\n', ' ').replace('\r', '')
        lines.append(
            f'        {{ {sid}, new DangerousSpell({sid}, {info["radius"]:g}f, '
            f'AoETargetMode.{info["mode"]}, @"{name_escaped}") }},'
        )
    lines.append('    };')
    lines.append('')
    lines.append('    public static bool TryGet(int spellId, out DangerousSpell spell) => All.TryGetValue(spellId, out spell);')
    lines.append('}')
    lines.append('')

    os.makedirs(os.path.dirname(OUT_CS), exist_ok=True)
    with open(OUT_CS, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    size_kb = os.path.getsize(OUT_CS) / 1024
    print(f'\nWritten {OUT_CS} ({size_kb:.1f} KB)')


def write_markdown(found):
    # Группы по базе имени
    by_mode = {}
    for sid, info in found.items():
        by_mode.setdefault(info['mode'], []).append((sid, info))

    lines = [
        '# DangerousSpellTable — proactive AoE avoidance',
        '',
        f'**Всего спеллов:** {len(found)}  ',
        f'**Источник:** `Spell.dbc` + `SpellRadius.dbc` + `patch-ruRU-4.MPQ`  ',
        f'**Фильтр:** harmful effects + area radius + не single-target  ',
        '',
        'Регенерация: `python tools/gen_dangerous_spell_table.py`',
        '',
        '## Использование',
        '',
        'Бот наблюдает за кастами врагов. При начале каста lookup в этой таблице:',
        'если спелл опасный — запустить логику уклонения согласно `Mode`.',
        '',
        '## Распределение по TargetMode',
        '',
    ]
    for mode in ['GroundTargeted', 'AroundCaster', 'AroundTarget', 'Cone', 'Frontal', 'Unknown']:
        if mode in by_mode:
            lines.append(f'- **{mode}:** {len(by_mode[mode])} спеллов')
    lines.append('')

    for mode in ['GroundTargeted', 'AroundCaster', 'AroundTarget', 'Cone', 'Frontal', 'Unknown']:
        if mode not in by_mode:
            continue
        lines.append(f'## {mode}')
        lines.append('')
        lines.append('| ID | Имя | Радиус |')
        lines.append('|----|-----|--------|')
        items = sorted(by_mode[mode], key=lambda x: (x[1]['name'].lower(), x[0]))
        for sid, info in items:
            lines.append(f'| {sid} | {info["name"]} | {info["radius"]:g}y |')
        lines.append('')

    os.makedirs(os.path.dirname(OUT_DOC), exist_ok=True)
    with open(OUT_DOC, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    size_kb = os.path.getsize(OUT_DOC) / 1024
    print(f'Written {OUT_DOC} ({size_kb:.1f} KB)')


if __name__ == '__main__':
    os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    generate()
