"""
Создаёт 6 тестовых персонажей для слейвов (аккаунты SLAVE1..6),
клонируя данные из существующих RNDBOT'ов того же класса с нужной спекой.

Классы/спеки:
  SLAVE1: Blood DK tank    (Human)
  SLAVE2: Resto Shaman hil (Draenei)
  SLAVE3: Balance Druid    (Night Elf — Moonkin)
  SLAVE4: Shadow Priest    (Night Elf)
  SLAVE5: Retri Paladin    (Human)
  SLAVE6: Resto Shaman hil (Draenei)  — резерв на случай поломки SLAVE2

Экипировка RNDBOT'а УДАЛЯЕТСЯ (BIS будет отдельным шагом).

Запуск: python tools/create_slaves.py
"""
import struct
import subprocess
import os

MYSQL = 'D:/SPP/SPP_Classics_V2/SPP_Server/Server/Database/bin/mysql.exe'
MYSQL_BASE = [MYSQL, '-h127.0.0.1', '-P3310', '-uroot', '-p123456', '--default-character-set=utf8']
DBC_DIR = 'D:/SPP/SPP_Classics_V2/SPP_Server/Modules/wotlk/dbc'

# Целевые персонажи: (имя, аккаунт_username, class_id, spec_tab_order, override_race)
TARGETS = [
    ('Бдктанк', 'SLAVE1', 6, 0, 1),    # Blood DK, Human
    ('Ршамхил', 'SLAVE2', 7, 2, 11),   # Resto Shaman, Draenei
    ('Совабал', 'SLAVE3', 11, 0, 4),   # Balance Druid, Night Elf
    ('Шпшип',   'SLAVE4', 5, 2, 4),    # Shadow Priest, Night Elf
    ('Ретпал',  'SLAVE5', 2, 2, 1),    # Ret Paladin, Human
    ('Ршамка',  'SLAVE6', 7, 2, 11),   # Resto Shaman, Draenei (копия Ршамхил)
]

CLASS_MASK = {1: 0x1, 2: 0x2, 3: 0x4, 4: 0x8, 5: 0x10, 6: 0x20, 7: 0x40, 8: 0x80, 9: 0x100, 11: 0x400}


def run_sql(sql, fetch=False):
    args = MYSQL_BASE + (['-N', '-s'] if fetch else []) + ['-e', sql]
    r = subprocess.run(args, capture_output=True)
    if r.returncode != 0:
        err = r.stderr.decode('utf-8', errors='replace')
        raise RuntimeError(f'SQL error: {err}\nSQL: {sql[:200]}')
    if fetch:
        out = r.stdout.decode('utf-8', errors='replace').strip()
        return [line.split('\t') for line in out.split('\n') if line]
    return None


def parse_dbc(path):
    with open(path, 'rb') as f:
        data = f.read()
    sig, nrec, nfield, recsize, strsize = struct.unpack_from('<4s4i', data, 0)
    assert sig == b'WDBC'
    records = []
    for i in range(nrec):
        off = 20 + i * recsize
        records.append(struct.unpack_from(f'<{nfield}I', data, off))
    return records


def build_talent_tab_map():
    """Возвращает {talent_id: (class_mask, tab_order)}.

    TalentTab.dbc WotLK 3.3.5a field layout (verified):
      [0] Id
      [1..17] Name[16] + NameFlag
      [18] SpellIconID
      [19] RaceMask
      [20] ClassMask          ← фиксированное поле
      [21] PetTalentMask
      [22] OrderIndex         ← 0/1/2
      [23] InternalName string ref
    """
    talent_recs = parse_dbc(os.path.join(DBC_DIR, 'Talent.dbc'))
    tab_recs = parse_dbc(os.path.join(DBC_DIR, 'TalentTab.dbc'))

    tab_info = {}
    for rec in tab_recs:
        tab_id = rec[0]
        cmask = rec[20]
        order = rec[22]
        tab_info[tab_id] = (cmask, order)

    # Talent.dbc: [0] Id, [1] TalentTab, [2] Row, [3] Column...
    t2tab = {}
    for rec in talent_recs:
        t_id = rec[0]
        tab_id = rec[1]
        if tab_id in tab_info:
            cmask, order = tab_info[tab_id]
            t2tab[t_id] = (cmask, order)
    return t2tab


def find_best_rndbot(class_id, desired_tab_order, t2tab):
    """Находит RNDBOT'а класса X с наибольшим кол-вом очков в нужной спеке."""
    cmask = CLASS_MASK[class_id]
    rows = run_sql(f"""
        SELECT c.guid, c.name, c.race, c.level, COUNT(i.item) as items
        FROM wotlkcharacters.characters c
        LEFT JOIN wotlkcharacters.character_inventory i ON c.guid=i.guid
        WHERE c.class={class_id} AND c.level=80
          AND c.account IN (SELECT id FROM wotlkrealmd.account WHERE username LIKE 'RNDBOT%')
        GROUP BY c.guid, c.name, c.race, c.level
    """, fetch=True)

    best_guid, best_score = None, -1
    for row in rows:
        guid = int(row[0])
        # Собираем очки талантов по tab для этого перса
        talents = run_sql(f"""
            SELECT talent_id, current_rank FROM wotlkcharacters.character_talent
            WHERE guid={guid} AND spec=0
        """, fetch=True)
        tab_points = {0: 0, 1: 0, 2: 0}
        for t in talents:
            tid = int(t[0])
            rank = int(t[1]) + 1  # current_rank 0 = 1 point spent
            if tid in t2tab:
                tcmask, torder = t2tab[tid]
                if tcmask == cmask and torder is not None:
                    tab_points[torder] += rank

        score = tab_points.get(desired_tab_order, 0)
        total_items = int(row[4]) if row[4] else 0
        # Приоритет: много очков в нужной ветке + экипирован
        combined = score * 100 + total_items
        if combined > best_score:
            best_score = combined
            best_guid = guid
    return best_guid


def next_guid():
    row = run_sql("SELECT MAX(guid) FROM wotlkcharacters.characters;", fetch=True)
    return int(row[0][0]) + 1


def get_account_id(username):
    row = run_sql(f"SELECT id FROM wotlkrealmd.account WHERE username='{username}';", fetch=True)
    return int(row[0][0]) if row else None


def clone_character(source_guid, target_account_id, target_name, override_race):
    new_guid = next_guid()

    # Клонируем основную запись characters
    # Заменяем: guid, account, name, race + online=0, position_x/y/z = homebind
    run_sql(f"""
        INSERT INTO wotlkcharacters.characters
        SELECT {new_guid}, {target_account_id}, '{target_name}',
            {override_race}, class, gender, skin, face, hairStyle, hairColor, facialStyle,
            level, xp, money, playerBytes, playerBytes2, playerFlags,
            position_x, position_y, position_z, map, orientation,
            taximask, online, cinematic, totaltime, leveltime, rest_bonus,
            logout_time, is_logout_resting, resettalents_cost, resettalents_time,
            trans_x, trans_y, trans_z, trans_o, transguid, extra_flags,
            stable_slots, at_login, zone, death_expire_time, taxi_path,
            arenaPoints, totalHonorPoints, todayHonorPoints, yesterdayHonorPoints,
            totalKills, todayKills, yesterdayKills, chosenTitle, knownCurrencies,
            watchedFaction, drunk, health, power1, power2, power3, power4, power5, power6, power7,
            latency, talentGroupsCount, activeTalentGroup, exploredZones,
            equipmentCache, ammoId, knownTitles, actionBars, grantableLevels, deleteInfos_Account,
            deleteInfos_Name, deleteDate
        FROM wotlkcharacters.characters WHERE guid={source_guid};
    """)

    # Обновляем guid в связанных таблицах через клонирование
    related = [
        'character_skills', 'character_spell', 'character_talent', 'character_action',
        'character_glyphs', 'character_homebind', 'character_reputation',
        'character_queststatus', 'character_queststatus_daily', 'character_queststatus_weekly',
        'character_queststatus_monthly', 'character_achievement', 'character_achievement_progress',
        'character_stats', 'character_tutorial', 'character_battleground_data',
    ]
    for tbl in related:
        # INSERT ... SELECT с заменой guid. Требует чтобы guid был первым столбцом.
        try:
            run_sql(f"""
                INSERT INTO wotlkcharacters.{tbl}
                SELECT {new_guid}, t.* FROM wotlkcharacters.{tbl} t WHERE guid={source_guid}
                LIMIT 0;
            """)
        except:
            pass
        # Если выше сработало (dry run с LIMIT 0), делаем реально
        try:
            run_sql(f"""
                INSERT INTO wotlkcharacters.{tbl}
                SELECT * FROM wotlkcharacters.{tbl} WHERE guid={source_guid};
            """)
        except Exception:
            pass  # fallthrough — попробуем через UPDATE после INSERT
    # Перезаписываем guid в связанных таблицах на новый
    # Trick: скопировали записи двоя (source остались + новые same-guid). Проще:
    # Удаляем source-дубликаты в target не вставятся, поэтому делаем так:
    # Используем UPDATE с INSERT+SELECT только если guid не PK:
    # Но character_skills.guid это часть PK. Поэтому не дубль.
    # Проще: INSERT... SELECT + UPDATE на новый guid. Но не сработает из-за PK.
    # Правильно: делаем копию через temp table с новым guid.
    pass  # Ниже сделаем правильно

    return new_guid


def clone_all_related(source_guid, new_guid):
    """Копирует связанные таблицы, меняя guid на new_guid."""
    related = [
        'character_skills', 'character_spell', 'character_talent', 'character_action',
        'character_glyphs', 'character_reputation', 'character_stats',
    ]
    for tbl in related:
        # Узнаём колонки таблицы
        cols_rows = run_sql(f"SHOW COLUMNS FROM wotlkcharacters.{tbl};", fetch=True)
        cols = [r[0] for r in cols_rows]
        col_list = ', '.join(cols)
        # SELECT с заменой guid на new_guid
        select_cols = ', '.join(f'{new_guid} AS guid' if c == 'guid' else c for c in cols)
        try:
            run_sql(f"""
                INSERT INTO wotlkcharacters.{tbl} ({col_list})
                SELECT {select_cols} FROM wotlkcharacters.{tbl} WHERE guid={source_guid};
            """)
        except Exception as e:
            print(f'  skip {tbl}: {e}')


def clone_homebind(source_guid, new_guid):
    cols_rows = run_sql("SHOW COLUMNS FROM wotlkcharacters.character_homebind;", fetch=True)
    cols = [r[0] for r in cols_rows]
    select_cols = ', '.join(f'{new_guid}' if c == 'guid' else c for c in cols)
    try:
        run_sql(f"""
            INSERT INTO wotlkcharacters.character_homebind ({', '.join(cols)})
            SELECT {select_cols} FROM wotlkcharacters.character_homebind WHERE guid={source_guid};
        """)
    except Exception as e:
        print(f'  homebind skip: {e}')


def clear_gear(guid):
    """Удаляет экипировку персонажа (для BIS apply позже)."""
    # Сначала удаляем inventory entries
    run_sql(f"DELETE FROM wotlkcharacters.character_inventory WHERE guid={guid};")
    # item_instance orphan removal — но это у источника, у нас только ссылки. Итемы чужие, не трогаем.
    # equipmentcache в characters нужно обнулить чтобы клиент не пытался показывать несуществующие
    run_sql(f"""
        UPDATE wotlkcharacters.characters
        SET equipmentCache='0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 '
        WHERE guid={guid};
    """)


def rewrite_main_clone(source_guid, target_account_id, target_name, override_race):
    """Правильное клонирование: создаём новую запись с новым guid."""
    new_guid = next_guid()

    # Получаем все колонки characters
    cols_rows = run_sql("SHOW COLUMNS FROM wotlkcharacters.characters;", fetch=True)
    cols = [r[0] for r in cols_rows]
    col_list = ', '.join(cols)

    # Строим SELECT: переписываем guid, account, name, race, online=0
    select_parts = []
    for c in cols:
        if c == 'guid':
            select_parts.append(f'{new_guid}')
        elif c == 'account':
            select_parts.append(f'{target_account_id}')
        elif c == 'name':
            select_parts.append(f"'{target_name}'")
        elif c == 'race':
            select_parts.append(f'{override_race}')
        elif c == 'online':
            select_parts.append('0')
        elif c == 'totaltime':
            select_parts.append('0')
        elif c == 'leveltime':
            select_parts.append('0')
        elif c == 'at_login':
            select_parts.append('0')
        elif c == 'extra_flags':
            select_parts.append('0')
        else:
            select_parts.append(c)

    sql = f"""
        INSERT INTO wotlkcharacters.characters ({col_list})
        SELECT {', '.join(select_parts)}
        FROM wotlkcharacters.characters WHERE guid={source_guid};
    """
    run_sql(sql)

    # Клонируем связанные таблицы
    clone_all_related(source_guid, new_guid)
    clone_homebind(source_guid, new_guid)

    # Чистим инвентарь и экипировку
    clear_gear(new_guid)

    return new_guid


def main():
    os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

    print('Парсим Talent.dbc + TalentTab.dbc...')
    t2tab = build_talent_tab_map()
    print(f'  {len(t2tab)} талантов замаплено')

    for name, slave_user, class_id, spec_order, override_race in TARGETS:
        acc_id = get_account_id(slave_user)
        if not acc_id:
            print(f'[!] Аккаунт {slave_user} не найден, пропускаем {name}')
            continue

        # Проверяем что у слейва ещё нет персонажа
        existing = run_sql(
            f"SELECT guid, name FROM wotlkcharacters.characters WHERE account={acc_id};",
            fetch=True
        )
        if existing:
            print(f'[!] У {slave_user} уже есть перс guid={existing[0][0]} name={existing[0][1]}, пропускаем')
            continue

        print(f'\n=== {name} ({slave_user}, class={class_id}, spec_order={spec_order}) ===')
        source_guid = find_best_rndbot(class_id, spec_order, t2tab)
        if not source_guid:
            print(f'  RNDBOT класса {class_id} не найден, пропускаем')
            continue
        print(f'  Клонируем с RNDBOT guid={source_guid}')

        new_guid = rewrite_main_clone(source_guid, acc_id, name, override_race)
        print(f'  Создан: guid={new_guid} name={name} на аккаунте {slave_user}')

    print('\n=== Итог ===')
    rows = run_sql("""
        SELECT c.guid, c.name, c.class, c.race, c.level, a.username
        FROM wotlkcharacters.characters c
        JOIN wotlkrealmd.account a ON c.account=a.id
        WHERE a.username IN ('TEST','SLAVE1','SLAVE2','SLAVE3','SLAVE4','SLAVE5','SLAVE6')
        ORDER BY a.id;
    """, fetch=True)
    for row in rows:
        print(f'  guid={row[0]:>4} {row[5]:>7} class={row[2]} race={row[3]} level={row[4]} name={row[1]}')


if __name__ == '__main__':
    main()
