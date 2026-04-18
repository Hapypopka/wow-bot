"""
Создаёт 5 тестовых аккаунтов для слейвов на локальном SPP сервере.
Все с gmlevel=3 как у TEST.

Пароли: одинаковые для всех = 'test123'
Юзернеймы: SLAVE1..SLAVE5
"""
import hashlib
import os
import subprocess

MYSQL = 'D:/SPP/SPP_Classics_V2/SPP_Server/Server/Database/bin/mysql.exe'
MYSQL_ARGS = ['-h127.0.0.1', '-P3310', '-uroot', '-p123456']

# MaNGOS/cmangos SRP6 константы
SRP6_N = int('894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7', 16)
SRP6_g = 7


def compute_srp6(username: str, password: str):
    """Возвращает (salt_hex, verifier_hex) для MaNGOS SRP6.

    В MaNGOS BigNumber хранит байты в LE, но AsHexStr() выдаёт их как BE hex.
    Поэтому для хранения нужно:
      - salt: 32 случайных байта, в БД записать BE-hex (реверснуть байты)
      - v:    g^x mod N как bigint → BE-hex
    При вычислении x используется salt в LE порядке (как в MaNGOS AsByteArray).
    """
    auth = f'{username.upper()}:{password.upper()}'.encode('utf-8')
    h1 = hashlib.sha1(auth).digest()

    # 32 случайных байта — считаем их LE-представлением bigint salt
    salt_le = os.urandom(32)

    # x = H(salt_le || h1) → int LE (как в MaNGOS BigNumber::SetBinary)
    x_bytes = hashlib.sha1(salt_le + h1).digest()
    x = int.from_bytes(x_bytes, 'little')

    # v = g^x mod N
    v = pow(SRP6_g, x, SRP6_N)

    # Хранение: оба как BE-hex (стандарт MaNGOS AsHexStr)
    s_hex = salt_le[::-1].hex().upper()          # BE of the same bigint
    v_hex = v.to_bytes(32, 'big').hex().upper()  # BE hex

    return s_hex, v_hex


def run_sql(sql: str):
    result = subprocess.run(
        [MYSQL] + MYSQL_ARGS + ['-e', sql],
        capture_output=True, text=True
    )
    if result.returncode != 0:
        print(f'ERROR: {result.stderr}')
        return False
    if result.stdout.strip():
        print(result.stdout.strip())
    return True


def main():
    accounts = [('SLAVE1', 'test123'), ('SLAVE2', 'test123'), ('SLAVE3', 'test123'),
                ('SLAVE4', 'test123'), ('SLAVE5', 'test123')]

    for username, password in accounts:
        # Проверка существования
        check = subprocess.run(
            [MYSQL] + MYSQL_ARGS + ['-N', '-s', '-e',
             f"SELECT id FROM wotlkrealmd.account WHERE username='{username}';"],
            capture_output=True, text=True
        )
        if check.stdout.strip():
            existing_id = check.stdout.strip()
            print(f'Аккаунт {username} уже существует (id={existing_id}), обновляю пароль и gmlevel...')
            s_hex, v_hex = compute_srp6(username, password)
            sql = (
                f"UPDATE wotlkrealmd.account SET "
                f"s='{s_hex}', v='{v_hex}', gmlevel=3 "
                f"WHERE username='{username}';"
            )
            run_sql(sql)
        else:
            s_hex, v_hex = compute_srp6(username, password)
            sql = (
                f"INSERT INTO wotlkrealmd.account "
                f"(username, gmlevel, s, v, email, joindate, expansion, locale) VALUES "
                f"('{username}', 3, '{s_hex}', '{v_hex}', NULL, NOW(), 2, 'ruRU');"
            )
            if run_sql(sql):
                print(f'Создан {username} (пароль: {password}, gmlevel=3)')

    # Проверка
    print('\n=== Все не-RNDBOT аккаунты ===')
    run_sql("SELECT id, username, gmlevel FROM wotlkrealmd.account WHERE username NOT LIKE 'RNDBOT%' ORDER BY id;")


if __name__ == '__main__':
    main()
