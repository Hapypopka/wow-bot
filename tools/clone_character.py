"""
Клонирует персонажа: source_guid → новый guid на новом аккаунте с новым именем.
Копирует ВСЁ: характеристики, инвентарь (с новыми item_instance), таланты, скиллы, спеллы, glyphs, homebind, и т.д.

Использование (внутри файла внизу):
    SOURCE_GUID = 5010      # Ршамхил (slave2)
    TARGET_ACCOUNT = 'SLAVE6'
    TARGET_NAME = 'Ресторчик'
"""
import subprocess
import os
import sys

MYSQL = 'D:/SPP/SPP_Classics_V2/SPP_Server/Server/Database/bin/mysql.exe'
MYSQL_BASE = [MYSQL, '-h127.0.0.1', '-P3310', '-uroot', '-p123456', '--default-character-set=utf8']


def run_sql(sql, fetch=False):
    args = MYSQL_BASE + (['-N', '-s'] if fetch else []) + ['-e', sql]
    r = subprocess.run(args, capture_output=True)
    if r.returncode != 0:
        err = r.stderr.decode('utf-8', errors='replace')
        raise RuntimeError(f'SQL error: {err}\nSQL: {sql[:300]}')
    if fetch:
        out = r.stdout.decode('utf-8', errors='replace').strip()
        return [line.split('\t') for line in out.split('\n') if line]
    return None


def get_account_id(username):
    rows = run_sql(f"SELECT id FROM wotlkrealmd.account WHERE username='{username}';", fetch=True)
    return int(rows[0][0]) if rows else None


def next_char_guid():
    rows = run_sql("SELECT MAX(guid) FROM wotlkcharacters.characters;", fetch=True)
    return int(rows[0][0]) + 1


def next_item_guid():
    rows = run_sql("SELECT MAX(guid) FROM wotlkcharacters.item_instance;", fetch=True)
    return int(rows[0][0]) + 1


def get_columns(table):
    rows = run_sql(f"SHOW COLUMNS FROM wotlkcharacters.{table};", fetch=True)
    return [r[0] for r in rows]


def clone_main_character(source_guid, target_account_id, target_name, new_guid):
    """Копирует основную запись characters."""
    cols = get_columns('characters')
    col_list = ', '.join(cols)
    select_parts = []
    for c in cols:
        if c == 'guid':
            select_parts.append(str(new_guid))
        elif c == 'account':
            select_parts.append(str(target_account_id))
        elif c == 'name':
            # Имя пишем через _utf8 префикс чтобы кириллица сохранилась корректно
            select_parts.append(f"_utf8'{target_name}'")
        elif c == 'online':
            select_parts.append('0')
        elif c == 'totaltime':
            select_parts.append('0')
        elif c == 'leveltime':
            select_parts.append('0')
        elif c == 'at_login':
            select_parts.append('0')
        else:
            select_parts.append(c)

    sql = (
        f"INSERT INTO wotlkcharacters.characters ({col_list}) "
        f"SELECT {', '.join(select_parts)} FROM wotlkcharacters.characters WHERE guid={source_guid};"
    )
    run_sql(sql)


def clone_item_instances(source_guid, new_char_guid):
    """Копирует item_instance с новыми itemguid и новым owner_guid.
    Возвращает map {old_item_guid: new_item_guid}."""
    rows = run_sql(
        f"SELECT guid FROM wotlkcharacters.item_instance WHERE owner_guid={source_guid};",
        fetch=True
    )
    if not rows:
        return {}

    base_new = next_item_guid()
    item_map = {}
    cols = get_columns('item_instance')
    col_list = ', '.join(cols)

    for i, row in enumerate(rows):
        old_iguid = int(row[0])
        new_iguid = base_new + i
        item_map[old_iguid] = new_iguid

        select_parts = []
        for c in cols:
            if c == 'guid':
                select_parts.append(str(new_iguid))
            elif c == 'owner_guid':
                select_parts.append(str(new_char_guid))
            else:
                select_parts.append(c)

        sql = (
            f"INSERT INTO wotlkcharacters.item_instance ({col_list}) "
            f"SELECT {', '.join(select_parts)} FROM wotlkcharacters.item_instance WHERE guid={old_iguid};"
        )
        run_sql(sql)

    return item_map


def clone_character_inventory(source_guid, new_char_guid, item_map):
    """Копирует character_inventory с заменой owner guid и item itemguid."""
    rows = run_sql(
        f"SELECT guid, bag, slot, item FROM wotlkcharacters.character_inventory WHERE guid={source_guid};",
        fetch=True
    )
    for row in rows:
        old_bag = int(row[1])
        slot = int(row[2])
        old_item = int(row[3])
        # bag тоже может быть item guid (для сумок). Если bag != 0 и есть в map — заменить.
        new_bag = item_map.get(old_bag, old_bag)
        new_item = item_map.get(old_item, old_item)
        sql = (
            f"INSERT INTO wotlkcharacters.character_inventory (guid, bag, slot, item) "
            f"VALUES ({new_char_guid}, {new_bag}, {slot}, {new_item});"
        )
        run_sql(sql)


def clone_simple_table(table, source_guid, new_char_guid):
    """Копирует таблицу где первый столбец = guid (owner)."""
    cols = get_columns(table)
    if not cols or cols[0] != 'guid':
        return False
    col_list = ', '.join(cols)
    select_parts = [str(new_char_guid) if c == 'guid' else c for c in cols]
    try:
        sql = (
            f"INSERT INTO wotlkcharacters.{table} ({col_list}) "
            f"SELECT {', '.join(select_parts)} FROM wotlkcharacters.{table} WHERE guid={source_guid};"
        )
        run_sql(sql)
        return True
    except Exception as e:
        print(f'  skip {table}: {e}')
        return False


def main():
    SOURCE_GUID = 5010      # Ршамхил (slave2)
    TARGET_ACCOUNT = 'SLAVE6'
    TARGET_NAME = 'Ресторчик'

    target_acc_id = get_account_id(TARGET_ACCOUNT)
    if not target_acc_id:
        print(f'[!] Аккаунт {TARGET_ACCOUNT} не найден')
        sys.exit(1)

    # Проверка что у целевого акка нет персонажей
    existing = run_sql(
        f"SELECT guid, name FROM wotlkcharacters.characters WHERE account={target_acc_id};",
        fetch=True
    )
    if existing:
        print(f'[!] У {TARGET_ACCOUNT} уже есть персонажи: {existing}')
        print('    Удали их сначала через GM или DELETE.')
        sys.exit(1)

    # Проверка что source существует
    src = run_sql(
        f"SELECT name, class, race, level FROM wotlkcharacters.characters WHERE guid={SOURCE_GUID};",
        fetch=True
    )
    if not src:
        print(f'[!] Source guid={SOURCE_GUID} не найден')
        sys.exit(1)

    print(f'Source: guid={SOURCE_GUID}, class={src[0][1]}, race={src[0][2]}, level={src[0][3]}')

    new_guid = next_char_guid()
    print(f'\nКлонирование → guid={new_guid}, account={TARGET_ACCOUNT} (id={target_acc_id}), name={TARGET_NAME}')

    # 1) characters
    clone_main_character(SOURCE_GUID, target_acc_id, TARGET_NAME, new_guid)
    print('  [+] characters')

    # 2) item_instance (с новыми itemguid)
    item_map = clone_item_instances(SOURCE_GUID, new_guid)
    print(f'  [+] item_instance: {len(item_map)} предметов')

    # 3) character_inventory (с заменой itemguid)
    clone_character_inventory(SOURCE_GUID, new_guid, item_map)
    print('  [+] character_inventory')

    # 4) Все остальные character_* таблицы где guid = owner
    related = [
        'character_skills', 'character_spell', 'character_talent', 'character_action',
        'character_glyphs', 'character_homebind', 'character_reputation',
        'character_queststatus', 'character_queststatus_daily', 'character_queststatus_weekly',
        'character_queststatus_monthly', 'character_achievement', 'character_achievement_progress',
        'character_stats', 'character_tutorial', 'character_battleground_data',
        'character_account_data', 'character_equipmentsets', 'character_aura',
        'character_spell_cooldown', 'character_declinedname', 'character_social',
        'character_instance', 'character_battleground_random',
    ]
    for t in related:
        ok = clone_simple_table(t, SOURCE_GUID, new_guid)
        if ok:
            print(f'  [+] {t}')

    print(f'\nГотово: guid={new_guid}, name={TARGET_NAME}, account={TARGET_ACCOUNT}')

    # Проверка
    print('\n=== Все персонажи слейв-акков ===')
    rows = run_sql("""
        SELECT c.guid, c.name, c.class, c.race, c.level, a.username
        FROM wotlkcharacters.characters c
        JOIN wotlkrealmd.account a ON c.account=a.id
        WHERE a.username IN ('TEST','SLAVE1','SLAVE2','SLAVE3','SLAVE4','SLAVE5','SLAVE6')
        ORDER BY a.id;
    """, fetch=True)
    for row in rows:
        print(f'  guid={row[0]:>5} {row[5]:>7} class={row[2]} race={row[3]} level={row[4]} name={row[1]}')


if __name__ == '__main__':
    main()
