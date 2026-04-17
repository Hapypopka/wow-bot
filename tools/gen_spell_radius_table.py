"""
Генератор SpellRadiusTable.g.cs из Spell.dbc + SpellRadius.dbc (WoW 3.3.5a).

Фильтр: только спеллы с Effect == 27 (SPELL_EFFECT_PERSISTENT_AREA_AURA).
Именно они создают DynObjects на земле — которые мы детектим и избегаем.

Запуск:
    python tools/gen_spell_radius_table.py

Вход: D:/SPP/SPP_Classics_V2/SPP_Server/Modules/wotlk/dbc/{Spell,SpellRadius}.dbc
Выход: WowBot.Core/Game/Generated/SpellRadiusTable.g.cs
"""
import struct
import os

DBC_DIR = 'D:/SPP/SPP_Classics_V2/SPP_Server/Modules/wotlk/dbc'
SPELL_DBC = os.path.join(DBC_DIR, 'Spell.dbc')
RADIUS_DBC = os.path.join(DBC_DIR, 'SpellRadius.dbc')

# Русские имена берём из клиентского MPQ (распаковать через mpyq).
# Порядок приоритета: patch-4 > patch-3 > patch-2 > patch > locale.
CLIENT_MPQ_DIR = 'D:/Games/Вов/Data/ruRU'
CLIENT_MPQS_PRIORITY = [
    'patch-ruRU-4.MPQ', 'patch-ruRU-3.MPQ', 'patch-ruRU-2.MPQ',
    'patch-ruRU.MPQ', 'locale-ruRU.MPQ',
]

OUT_FILE = 'WowBot.Core/Game/Generated/SpellRadiusTable.g.cs'
OUT_DOC = 'docs/spell_radius_table.md'

SPELL_EFFECT_PERSISTENT_AREA_AURA = 27

# Оффсеты в Spell.dbc (verified для этого DBC, 234 fields × 4 = 936 bytes):
# Name[locale=8 ruRU] = field 136 + 8
FIELD_ID = 0
FIELD_EFFECT = 71            # Effect[0..2] = fields 71, 72, 73
FIELD_RADIUS_IDX = 92        # EffectRadiusIndex[0..2] = fields 92, 93, 94
FIELD_APPLY_AURA = 95        # EffectApplyAuraName[0..2] = fields 95, 96, 97
FIELD_AMPLITUDE = 98         # EffectAmplitude[0..2] (period в мс) = fields 98, 99, 100
FIELD_NAME_RU = 136 + 8      # ruRU локаль


def load_spell_radius(path):
    with open(path, 'rb') as f:
        data = f.read()
    sig, nrec, nfield, recsize, strsize = struct.unpack_from('<4s4i', data, 0)
    assert sig == b'WDBC', f'Bad signature: {sig}'
    radius_map = {}
    for i in range(nrec):
        off = 20 + i * recsize
        rid = struct.unpack_from('<I', data, off)[0]
        radius = struct.unpack_from('<f', data, off + 4)[0]
        radius_map[rid] = radius
    return radius_map


def load_spell_dbc(path):
    with open(path, 'rb') as f:
        data = f.read()
    sig, nrec, nfield, recsize, strsize = struct.unpack_from('<4s4i', data, 0)
    assert sig == b'WDBC'
    assert nfield == 234 and recsize == 936, f'Unexpected layout: {nfield} fields, {recsize} bytes'
    data_start = 20
    str_start = data_start + nrec * recsize
    strings = data[str_start:str_start + strsize]
    return data, nrec, recsize, strings


def load_ruru_name_map():
    """Строит {spell_id: russian_name} из клиентских MPQ (locale=8 ruRU)."""
    try:
        import mpyq
    except ImportError:
        print('WARN: mpyq не установлен — русских имён не будет. pip install mpyq')
        return {}

    name_map = {}
    # Порядок: сначала самые новые patch, потом старые — позже найденные не перезаписывают
    for mpq_name in CLIENT_MPQS_PRIORITY:
        mpq_path = os.path.join(CLIENT_MPQ_DIR, mpq_name)
        if not os.path.exists(mpq_path):
            continue
        try:
            archive = mpyq.MPQArchive(mpq_path)
            dbc_data = archive.read_file('DBFilesClient\\Spell.dbc')
            if not dbc_data:
                continue
        except Exception as e:
            print(f'  {mpq_name}: skip ({e})')
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
                continue  # уже есть из более приоритетного patch
            ru_offset = fields[136 + 8]  # locale=8 ruRU
            name = read_string(strings, ru_offset)
            if name:
                name_map[sid] = name
        print(f'  {mpq_name}: {len(name_map)} total names after merge')
    return name_map


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


def generate():
    print(f'Reading {RADIUS_DBC}...')
    radius_map = load_spell_radius(RADIUS_DBC)
    print(f'  {len(radius_map)} radius entries')

    print(f'Reading {SPELL_DBC}...')
    data, nrec, recsize, strings = load_spell_dbc(SPELL_DBC)
    print(f'  {nrec} spells')

    print(f'Loading russian names from {CLIENT_MPQ_DIR}...')
    ru_names = load_ruru_name_map()
    print(f'  {len(ru_names)} russian names loaded')

    # spell_id → (radius, name)
    found = {}
    skipped_no_radius = 0

    for i in range(nrec):
        off = 20 + i * recsize
        fields = struct.unpack_from('<234I', data, off)

        spell_id = fields[FIELD_ID]
        effects = fields[FIELD_EFFECT:FIELD_EFFECT + 3]
        radius_indices = fields[FIELD_RADIUS_IDX:FIELD_RADIUS_IDX + 3]
        auras = fields[FIELD_APPLY_AURA:FIELD_APPLY_AURA + 3]
        periods = fields[FIELD_AMPLITUDE:FIELD_AMPLITUDE + 3]

        # Ищем слот с PERSISTENT_AREA_AURA который реально что-то делает.
        # Фильтр: aura!=0 OR period>0 — иначе это косметика (сигналки, декорации).
        best_radius = 0.0
        for slot in range(3):
            if effects[slot] != SPELL_EFFECT_PERSISTENT_AREA_AURA:
                continue
            # Пустой слот: ни ауры не накладывает, ни тиков — визуал/декор
            if auras[slot] == 0 and periods[slot] == 0:
                continue
            ridx = radius_indices[slot]
            if ridx == 0:
                continue
            r = radius_map.get(ridx)
            if r is None or r <= 0:
                continue
            # Игнорируем явные "весь уровень" радиусы (>= 100y)
            if r >= 100.0:
                continue
            if r > best_radius:
                best_radius = r

        if best_radius <= 0:
            if any(e == SPELL_EFFECT_PERSISTENT_AREA_AURA for e in effects):
                skipped_no_radius += 1
            continue

        # Приоритет: русское имя из клиентского MPQ → fallback enUS из SPP DBC
        name = ru_names.get(spell_id, '')
        if not name:
            name = read_string(strings, fields[136])  # enUS
        found[spell_id] = (best_radius, name)

    print(f'  {len(found)} AoE spells with radius')
    print(f'  {skipped_no_radius} AoE spells skipped (no valid radius / >=100y)')

    # Генерим C# файл
    lines = [
        '// <auto-generated>',
        '// Сгенерировано tools/gen_spell_radius_table.py из Spell.dbc + SpellRadius.dbc',
        '// НЕ РЕДАКТИРОВАТЬ ВРУЧНУЮ — изменения будут потеряны.',
        '// </auto-generated>',
        '',
        'using System.Collections.Generic;',
        '',
        'namespace WowBot.Core.Game.Generated;',
        '',
        '/// <summary>',
        f'/// Радиусы AoE спеллов из DBC (фильтр: Effect=PERSISTENT_AREA_AURA). Всего: {len(found)}.',
        '/// Используется WowDynObject для определения радиуса AoE зоны.',
        '/// </summary>',
        'public static class SpellRadiusTable',
        '{',
        f'    public static readonly IReadOnlyDictionary<int, float> Radius = new Dictionary<int, float>({len(found)})',
        '    {',
    ]

    # Сортируем по id для стабильности
    for spell_id in sorted(found.keys()):
        radius, name = found[spell_id]
        # Экранируем комментарий
        safe_name = name.replace('*/', '*_/').replace('\n', ' ').strip()
        if safe_name:
            lines.append(f'        {{ {spell_id}, {radius:g}f }}, // {safe_name}')
        else:
            lines.append(f'        {{ {spell_id}, {radius:g}f }},')

    lines.append('    };')
    lines.append('')
    lines.append('    /// <summary>Получить радиус по spell id. Возвращает 0 если нет в таблице.</summary>')
    lines.append('    public static float Get(int spellId) => Radius.TryGetValue(spellId, out var r) ? r : 0f;')
    lines.append('}')
    lines.append('')

    out_path = OUT_FILE
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))

    size_kb = os.path.getsize(out_path) / 1024
    print(f'Written {out_path} ({size_kb:.1f} KB)')

    # Markdown-документ со списком (для удобного просмотра)
    write_markdown(found, ru_names)

    # Проверочные спеллы
    print('\nVerification:')
    for sid, expected_name in [
        (16914, 'Hurricane'),
        (48467, 'Hurricane r4'),
        (42940, 'Blizzard max'),
        (42926, 'Flamestrike max'),
        (43265, 'Death and Decay'),
        (48819, 'Consecration max'),
        (5740, 'Rain of Fire r1'),
        (26573, 'Consecration r1'),
    ]:
        if sid in found:
            r, n = found[sid]
            print(f'  {sid} ({expected_name}): {r}y [{n}]')
        else:
            print(f'  {sid} ({expected_name}): NOT FOUND')


def write_markdown(found, ru_names):
    """Генерит markdown-табличку с группировкой по имени спелла."""
    # Группируем по русскому имени (или англ fallback): все ранги вместе
    groups = {}  # name → list of (id, radius)
    for sid, (r, name) in found.items():
        key = name or f'Unknown {sid}'
        groups.setdefault(key, []).append((sid, r))
    for k in groups:
        groups[k].sort()

    ru_count = sum(1 for _, (_, n) in found.items() if any(ord(c) > 127 for c in n))

    lines = [
        '# Таблица AoE радиусов (генерируется автоматически)',
        '',
        f'**Всего спеллов:** {len(found)}  ',
        f'**С русскими именами:** {ru_count}  ',
        f'**Источник:** `Spell.dbc` + `SpellRadius.dbc` (SPP) + русские локали из `patch-ruRU*.MPQ`  ',
        f'**Фильтр:** `Effect == PERSISTENT_AREA_AURA (27)` — спеллы создающие DynObject на земле  ',
        '',
        'Регенерация: `python tools/gen_spell_radius_table.py`',
        '',
        '## Как использовать',
        '',
        'Таблица сама попадает в `WowBot.Core/Game/Generated/SpellRadiusTable.g.cs`.',
        'Бот читает её в `WowDynObject.GetDefaultRadius()` когда определяет радиус AoE лужи.',
        '',
        '## Все спеллы (по имени)',
        '',
        '| Имя | Ранги (ID) | Радиус |',
        '|-----|------------|--------|',
    ]

    # Сортировка: сначала русские имена (по алфавиту), потом англ
    def sort_key(name):
        is_rus = any(ord(c) > 127 for c in name)
        return (0 if is_rus else 1, name.lower())

    for name in sorted(groups.keys(), key=sort_key):
        ranks = groups[name]
        # Если все ранги с одним радиусом — показываем один
        radii = sorted(set(r for _, r in ranks))
        ids_str = ', '.join(str(sid) for sid, _ in ranks)
        if len(radii) == 1:
            r_str = f'{radii[0]:g}y'
        else:
            r_str = ', '.join(f'{r:g}y' for r in radii)
        lines.append(f'| {name} | {ids_str} | {r_str} |')

    out_path = OUT_DOC
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    size_kb = os.path.getsize(out_path) / 1024
    print(f'Written {out_path} ({size_kb:.1f} KB)')


if __name__ == '__main__':
    os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    generate()
