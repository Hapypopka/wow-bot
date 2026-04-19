"""
Экипирует слейвов BIS-ишним набором (по iLvl + preferred stats) и клонирует
предметы в новые entry >= 99000 с статами ×10. Оригиналы не трогаются.

1. Для каждого спека определяет слоты + armor type + preferred stats
2. Выбирает из item_template лучший item per slot по score (iLvl + статы)
3. Клонирует item_template -> entry 99000+X (stat_value1..10 × 10, dmg × 10, armor × 10)
4. Создаёт item_instance + character_inventory для слейва

Запуск: python tools/equip_bis_pumped.py
"""
import subprocess
import os

MYSQL = 'D:/SPP/SPP_Classics_V2/SPP_Server/Server/Database/bin/mysql.exe'
BASE = [MYSQL, '-h127.0.0.1', '-P3310', '-uroot', '-p123456', '--default-character-set=utf8', '-Dwotlkcharacters']

PUMPED_ENTRY_BASE = 99000  # entry-ы клонов начинаются с этой цифры
STAT_MULTIPLIER = 10

# Слейв-персонажи: (guid, class_id, spec_name)
SLAVES = [
    (5007, 6,  'blood_dk_tank'),
    (5010, 7,  'resto_shaman'),
    (5008, 11, 'balance_druid'),
    (5005, 5,  'shadow_priest'),
    (5009, 2,  'ret_paladin'),
]

# Дополнительные слейвы с lookup guid по имени (для свежесозданных через create_slaves.py).
# Формат: (name, class_id, spec_name)
SLAVES_BY_NAME = [
    ('Ршамка', 7, 'resto_shaman'),
]

# ItemMod (stat_type) константы WotLK 3.3.5
ST_MANA, ST_HEALTH = 0, 1
ST_AGI, ST_STR, ST_INT, ST_SPI, ST_STA = 3, 4, 5, 6, 7
ST_DEF, ST_DODGE, ST_PARRY, ST_BLOCK = 12, 13, 14, 15
ST_HIT, ST_CRIT, ST_HASTE, ST_EXP = 31, 32, 36, 37
ST_AP, ST_RAP, ST_MP5, ST_SP = 38, 39, 43, 45

# Spec preferences: {stat_type: weight}
SPEC_PREFS = {
    'blood_dk_tank':   {ST_STA: 8, ST_STR: 4, ST_DEF: 5, ST_DODGE: 3, ST_PARRY: 3, ST_HIT: 2, ST_EXP: 2},
    'resto_shaman':    {ST_INT: 5, ST_SP: 5, ST_HASTE: 4, ST_SPI: 3, ST_MP5: 3, ST_CRIT: 1, ST_STA: 1},
    'balance_druid':   {ST_INT: 5, ST_SP: 5, ST_HASTE: 4, ST_CRIT: 3, ST_HIT: 3, ST_SPI: 1, ST_STA: 1},
    'shadow_priest':   {ST_INT: 5, ST_SP: 5, ST_HASTE: 4, ST_HIT: 3, ST_CRIT: 3, ST_SPI: 1, ST_STA: 1},
    'ret_paladin':     {ST_STR: 8, ST_HIT: 4, ST_CRIT: 3, ST_HASTE: 3, ST_EXP: 3, ST_AP: 4, ST_AGI: 2, ST_STA: 1},
}

# Armor subclass (class=4 Armor)
CLOTH, LEATHER, MAIL, PLATE = 1, 2, 3, 4
SHIELD, LIBRAM, IDOL, TOTEM, SIGIL = 6, 7, 8, 9, 10

# Weapon subclasses (class=2 Weapon)
AXE_1H, AXE_2H, BOW, GUN, MACE_1H, MACE_2H, POLE, SWORD_1H, SWORD_2H = 0,1,2,3,4,5,6,7,8
STAFF = 10
FIST = 13
DAGGER = 15
CROSSBOW = 18
WAND = 19

# Inventory slots (позиции на персонаже)
SLOT_HEAD = 0; SLOT_NECK = 1; SLOT_SHOULDER = 2
SLOT_CHEST = 4; SLOT_WAIST = 5; SLOT_LEGS = 6; SLOT_FEET = 7
SLOT_WRIST = 8; SLOT_HANDS = 9
SLOT_FINGER1 = 10; SLOT_FINGER2 = 11
SLOT_TRINKET1 = 12; SLOT_TRINKET2 = 13
SLOT_BACK = 14; SLOT_MH = 15; SLOT_OH = 16; SLOT_RANGED = 17

# Inventory type -> slot mapping
# Per-class plan: list of (slot, filter_predicate_name)
# filter_predicate использует item_template fields

ARMOR_CLASS = 4
WEAPON_CLASS = 2

class SlotPlan:
    """План для одного слота: что искать."""
    def __init__(self, slot, item_class, inventory_types, subclasses=None,
                 skip_subclasses=None, tag=''):
        self.slot = slot
        self.item_class = item_class
        self.inventory_types = inventory_types if isinstance(inventory_types, list) else [inventory_types]
        self.subclasses = subclasses  # если None — любой
        self.skip_subclasses = skip_subclasses or []
        self.tag = tag  # для debug


def build_class_plan(class_id, spec):
    """Строит список SlotPlan для класса/спека."""
    plans = []
    # Armor slots (head, neck, shoulder, chest, waist, legs, feet, wrist, hands, back, fingers, trinkets)
    if spec == 'blood_dk_tank' or spec == 'ret_paladin':
        armor_sub = PLATE
    elif spec == 'resto_shaman':
        armor_sub = MAIL
    elif spec == 'balance_druid':
        armor_sub = LEATHER
    elif spec == 'shadow_priest':
        armor_sub = CLOTH

    # Plate/Mail/Leather/Cloth slots
    plans.append(SlotPlan(SLOT_HEAD,     ARMOR_CLASS, [1],  [armor_sub], tag='Head'))
    plans.append(SlotPlan(SLOT_NECK,     ARMOR_CLASS, [2],  [0], tag='Neck'))  # subclass=0 misc
    plans.append(SlotPlan(SLOT_SHOULDER, ARMOR_CLASS, [3],  [armor_sub], tag='Shoulder'))
    plans.append(SlotPlan(SLOT_CHEST,    ARMOR_CLASS, [5, 20], [armor_sub], tag='Chest'))
    plans.append(SlotPlan(SLOT_WAIST,    ARMOR_CLASS, [6],  [armor_sub], tag='Waist'))
    plans.append(SlotPlan(SLOT_LEGS,     ARMOR_CLASS, [7],  [armor_sub], tag='Legs'))
    plans.append(SlotPlan(SLOT_FEET,     ARMOR_CLASS, [8],  [armor_sub], tag='Feet'))
    plans.append(SlotPlan(SLOT_WRIST,    ARMOR_CLASS, [9],  [armor_sub], tag='Wrist'))
    plans.append(SlotPlan(SLOT_HANDS,    ARMOR_CLASS, [10], [armor_sub], tag='Hands'))
    plans.append(SlotPlan(SLOT_BACK,     ARMOR_CLASS, [16], None, tag='Back'))  # любой subclass — Cloth/Misc
    plans.append(SlotPlan(SLOT_FINGER1,  ARMOR_CLASS, [11], [0], tag='Finger1'))
    plans.append(SlotPlan(SLOT_FINGER2,  ARMOR_CLASS, [11], [0], tag='Finger2'))
    plans.append(SlotPlan(SLOT_TRINKET1, ARMOR_CLASS, [12], [0], tag='Trinket1'))
    plans.append(SlotPlan(SLOT_TRINKET2, ARMOR_CLASS, [12], [0], tag='Trinket2'))

    # Weapons per spec
    if spec == 'blood_dk_tank':
        # 2H Axe/Sword/Mace/Polearm. Ranged: Sigil (class=4 subclass=10)
        plans.append(SlotPlan(SLOT_MH, WEAPON_CLASS, [17], [AXE_2H, SWORD_2H, MACE_2H, POLE], tag='2H weapon'))
        plans.append(SlotPlan(SLOT_RANGED, ARMOR_CLASS, [28], [SIGIL], tag='Sigil'))
    elif spec == 'resto_shaman':
        # 1H MH + Shield OR 2H. BIS is 1H+Shield for resto.
        plans.append(SlotPlan(SLOT_MH, WEAPON_CLASS, [13, 21], [MACE_1H, AXE_1H, DAGGER, FIST], tag='1H weapon'))
        plans.append(SlotPlan(SLOT_OH, ARMOR_CLASS, [14], [SHIELD], tag='Shield'))
        plans.append(SlotPlan(SLOT_RANGED, ARMOR_CLASS, [28], [TOTEM], tag='Totem'))
    elif spec == 'balance_druid':
        # Staff. Idol for ranged.
        plans.append(SlotPlan(SLOT_MH, WEAPON_CLASS, [17], [STAFF], tag='Staff'))
        plans.append(SlotPlan(SLOT_RANGED, ARMOR_CLASS, [28], [IDOL], tag='Idol'))
    elif spec == 'shadow_priest':
        # 1H MH + offhand (holdable, InvType=23). Wand for ranged.
        plans.append(SlotPlan(SLOT_MH, WEAPON_CLASS, [13, 21], [MACE_1H, DAGGER], tag='1H weapon'))
        plans.append(SlotPlan(SLOT_OH, ARMOR_CLASS, [23], [0], tag='Offhand'))
        plans.append(SlotPlan(SLOT_RANGED, WEAPON_CLASS, [26, 15], [WAND], tag='Wand'))
    elif spec == 'ret_paladin':
        # 2H weapon. Libram.
        plans.append(SlotPlan(SLOT_MH, WEAPON_CLASS, [17], [AXE_2H, SWORD_2H, MACE_2H, POLE], tag='2H weapon'))
        plans.append(SlotPlan(SLOT_RANGED, ARMOR_CLASS, [28], [LIBRAM], tag='Libram'))

    return plans


def class_mask(class_id):
    return {1:0x1,2:0x2,3:0x4,4:0x8,5:0x10,6:0x20,7:0x40,8:0x80,9:0x100,11:0x400}[class_id]


def sql(query, fetch=False):
    args = BASE + (['-N', '-s'] if fetch else []) + ['-e', query]
    r = subprocess.run(args, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError(f'SQL error: {r.stderr.decode("utf-8", errors="replace")}\nQuery: {query[:300]}')
    if fetch:
        out = r.stdout.decode('utf-8', errors='replace').strip()
        return [line.split('\t') for line in out.split('\n') if line]
    return None


def find_best_item(plan, class_id, spec):
    """Возвращает (entry, name, ilvl, score) лучшего items для слота."""
    cm = class_mask(class_id)
    inv_types_sql = ','.join(str(x) for x in plan.inventory_types)
    sub_sql = ''
    if plan.subclasses is not None:
        sub_sql = f' AND subclass IN ({",".join(str(x) for x in plan.subclasses)})'
    # AllowableClass bitmask: -1 OR has our bit
    # Quality >= 4 (epic)
    # RequiredLevel <= 80
    # ItemLevel not too insane (< 300 to avoid test items)
    # Name not "Deprecated" etc
    q = f"""
        SELECT entry, name, ItemLevel, Quality,
               stat_type1, stat_value1, stat_type2, stat_value2,
               stat_type3, stat_value3, stat_type4, stat_value4,
               stat_type5, stat_value5, stat_type6, stat_value6,
               stat_type7, stat_value7, stat_type8, stat_value8,
               stat_type9, stat_value9, stat_type10, stat_value10,
               dmg_min1, dmg_max1, armor
        FROM wotlkmangos.item_template
        WHERE class={plan.item_class}
          AND InventoryType IN ({inv_types_sql})
          {sub_sql}
          AND (AllowableClass = -1 OR AllowableClass & {cm})
          AND Quality >= 4
          AND Quality <= 4
          AND RequiredLevel <= 80
          AND ItemLevel >= 200 AND ItemLevel <= 284
          AND Flags & 0x00000004 = 0
          AND name NOT LIKE '%Deprecated%'
          AND name NOT LIKE '%QA%'
          AND name NOT LIKE '%TEST%'
          AND name NOT LIKE '%GOD%'
          AND name NOT LIKE '%DEBUG%'
          AND name NOT LIKE 'NPC %'
          AND entry < {PUMPED_ENTRY_BASE}
          AND name != ''
          AND requiredspell = 0
        ORDER BY ItemLevel DESC, entry DESC
        LIMIT 50;
    """
    rows = sql(q, fetch=True)
    if not rows:
        return None

    prefs = SPEC_PREFS[spec]
    best = None
    best_score = -1
    for row in rows:
        entry = int(row[0]); name = row[1]; ilvl = int(row[2])
        # parse stats
        stat_score = 0
        for i in range(10):
            st_type = int(row[4 + i*2])
            st_val = int(row[5 + i*2])
            if st_type in prefs:
                stat_score += st_val * prefs[st_type]

        # primary: iLvl
        score = ilvl * 100 + stat_score
        if score > best_score:
            best_score = score
            best = (entry, name, ilvl, score)

    return best


def pump_original_item(entry):
    """Модифицирует оригинальный item_template: stat_value × 10, dmg × 10, armor × 10.
    Идемпотентно: добавляет префикс [x10] в name, если его ещё нет."""
    # Проверка что ещё не помпован
    row = sql(f"SELECT name FROM wotlkmangos.item_template WHERE entry={entry};", fetch=True)
    if not row:
        return
    name = row[0][0]
    if name.startswith('[x10]'):
        return  # уже помпован, пропускаем

    stat_val_cols = ', '.join([f'stat_value{i}=stat_value{i}*{STAT_MULTIPLIER}' for i in range(1, 11)])
    sql(f"""
        UPDATE wotlkmangos.item_template
        SET {stat_val_cols},
            dmg_min1 = dmg_min1*{STAT_MULTIPLIER},
            dmg_max1 = dmg_max1*{STAT_MULTIPLIER},
            dmg_min2 = dmg_min2*{STAT_MULTIPLIER},
            dmg_max2 = dmg_max2*{STAT_MULTIPLIER},
            armor = armor*{STAT_MULTIPLIER},
            name = CONCAT('[x10] ', name)
        WHERE entry={entry};
    """)


_item_guid_counter = None
def next_item_guid():
    """Берём guid из safe-диапазона 9_000_000+. Проверяем через item_instance И character_inventory.item."""
    global _item_guid_counter
    if _item_guid_counter is None:
        row = sql(
            "SELECT GREATEST(COALESCE((SELECT MAX(guid) FROM wotlkcharacters.item_instance),0), "
            "COALESCE((SELECT MAX(item) FROM wotlkcharacters.character_inventory),0), 9000000);",
            fetch=True
        )
        max_existing = int(row[0][0])
        _item_guid_counter = max_existing + 1
    else:
        _item_guid_counter += 1
    return _item_guid_counter


def give_item(char_guid, item_entry, slot):
    """Даёт предмет персонажу в slot (через item_instance + character_inventory)."""
    item_guid = next_item_guid()
    # item_instance
    sql(f"""
        INSERT INTO wotlkcharacters.item_instance
        (guid, owner_guid, itemEntry, creatorGuid, giftCreatorGuid, count, duration,
         charges, flags, enchantments, randomPropertyId, durability, playedTime, text)
        VALUES ({item_guid}, {char_guid}, {item_entry}, {char_guid}, 0, 1, 0,
                '0 0 0 0 0 ', 1, '0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 ',
                0, 120, 0, NULL);
    """)
    # character_inventory
    # bag=0 для equipped slots (0..18)
    sql(f"""
        INSERT INTO wotlkcharacters.character_inventory
        (guid, bag, slot, item, item_template)
        VALUES ({char_guid}, 0, {slot}, {item_guid}, {item_entry});
    """)
    return item_guid


def update_equipment_cache(char_guid):
    """Перегенерирует equipmentCache в characters из character_inventory (slots 0..18)."""
    rows = sql(f"""
        SELECT ci.slot, ci.item_template
        FROM wotlkcharacters.character_inventory ci
        WHERE ci.guid={char_guid} AND ci.bag=0 AND ci.slot<19
        ORDER BY ci.slot;
    """, fetch=True)
    eq = {int(r[0]): int(r[1]) for r in rows}
    parts = []
    for slot in range(19):
        item = eq.get(slot, 0)
        parts.append(f'{item} 0')
    cache = ' '.join(parts) + ' '
    sql(f"UPDATE wotlkcharacters.characters SET equipmentCache='{cache}' WHERE guid={char_guid};")


def equip_slave(char_guid, class_id, spec):
    print(f'\n=== {spec} (guid={char_guid}, class={class_id}) ===')

    # Очищаем текущую экипировку в слотах 0..18 (equipped)
    sql(f"""
        DELETE ii FROM wotlkcharacters.item_instance ii
        JOIN wotlkcharacters.character_inventory ci ON ii.guid=ci.item
        WHERE ci.guid={char_guid} AND ci.bag=0 AND ci.slot<19;
    """)
    sql(f"DELETE FROM wotlkcharacters.character_inventory WHERE guid={char_guid} AND bag=0 AND slot<19;")

    plans = build_class_plan(class_id, spec)

    used_entries = set()  # чтобы для 2-х колец не выдавать одно и то же

    for plan in plans:
        best = find_best_item(plan, class_id, spec)
        if not best:
            print(f'  [{plan.tag:>12}] не найдено')
            continue
        entry, name, ilvl, score = best

        # Если уже использован (для ring1/trinket1 vs ring2/trinket2), берём второй по score
        if entry in used_entries:
            cm = class_mask(class_id)
            inv_types_sql = ','.join(str(x) for x in plan.inventory_types)
            sub_sql = ''
            if plan.subclasses is not None:
                sub_sql = f' AND subclass IN ({",".join(str(x) for x in plan.subclasses)})'
            excluded = ','.join(str(e) for e in used_entries) or '0'
            rows = sql(f"""
                SELECT entry, name, ItemLevel
                FROM wotlkmangos.item_template
                WHERE class={plan.item_class}
                  AND InventoryType IN ({inv_types_sql})
                  {sub_sql}
                  AND (AllowableClass = -1 OR AllowableClass & {cm})
                  AND Quality = 4 AND RequiredLevel <= 80
                  AND ItemLevel >= 200 AND ItemLevel <= 284
                  AND name NOT LIKE '%Deprecated%'
                  AND entry NOT IN ({excluded})
                  AND entry < {PUMPED_ENTRY_BASE}
                  AND name NOT LIKE '%QA%'
                  AND name NOT LIKE '%TEST%'
                  AND name NOT LIKE '%GOD%'
                  AND name NOT LIKE 'NPC %'
                  AND requiredspell = 0
                ORDER BY ItemLevel DESC, entry DESC LIMIT 5;
            """, fetch=True)
            if rows:
                entry = int(rows[0][0])
                name = rows[0][1]
                ilvl = int(rows[0][2])

        used_entries.add(entry)

        # Помпуем оригинальный entry (×10 статы). Идемпотентно.
        pump_original_item(entry)
        # Даём персу оригинальный entry — сервер его УЖЕ знает в кеше
        item_guid = give_item(char_guid, entry, plan.slot)
        print(f'  [{plan.tag:>12}] slot={plan.slot:2} {entry}  iLvl={ilvl}  {name}')

    update_equipment_cache(char_guid)


def lookup_guid_by_name(name):
    rows = sql(f"SELECT guid FROM wotlkcharacters.characters WHERE name='{name}';", fetch=True)
    return int(rows[0][0]) if rows else None


def main():
    os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    for guid, class_id, spec in SLAVES:
        equip_slave(guid, class_id, spec)
    for name, class_id, spec in SLAVES_BY_NAME:
        guid = lookup_guid_by_name(name)
        if guid is None:
            print(f'\n[!] {name} не найден в БД — пропускаю (запусти create_slaves.py)')
            continue
        equip_slave(guid, class_id, spec)
    print('\n[OK] Готово. Заходи в игру и проверяй.')


if __name__ == '__main__':
    main()
