# Phase 2 — реверс Warden модуля WoWCircle

Краткая сводка из статанализа (без IDA/Ghidra). Файлы в этой же папке:
- `warden_module.bin` (30533 b) — распакованный x86 binary
- `analyze_module.py` — структурный анализ (header, прологи, SHA1 константы, импорты)
- `disasm_regions.py` — capstone-дизасм ключевых участков

## Структура файла

| Offset | Назначение |
|---|---|
| `0x00..0x4D` | Custom header (78 байт) — runtime layout, RVAs секций |
| `0x4E..` | Начало x86 кода |
| `0x6FE7..0x7213` | Таблица строк (имена импортов: `kernel32.dll`, `IsDebuggerPresent`, ...) |
| `0x7745` | EOF (30533 b) |

**Header первые поля (LE uint32):**
```
0x00  0xB000  возможный virtual size
0x04  0x5B35  ?
0x08  0xA000  data section RVA?
0x0C  0x14F   данных 335 b на этом RVA
0x10  0x8788  ?
```

Точная семантика header'а требует посмотреть как WoW client это парсит — он внутри `wow.exe` есть Warden runtime, который интерпретирует этот блок.

## Импорты (resolved через GetProcAddress)

Найдены строки и адреса:

```
IsDebuggerPresent              @ 0x6FE7
kernel32 / kernel32.dll        @ 0x6FFB / 0x7040
AddVectoredExceptionHandler    @ 0x7050
wine_get_unix_file_name        @ 0x708C  ← детектор Wine/Linux
CreateToolhelp32Snapshot       @ 0x714C  ← перечисление процессов
GetProcAddress                 @ 0x72E9
LoadLibrary                    @ 0x7378
GetTickCount                   @ 0x7331
```

**Это типичный Warden:** anti-debug + anti-Wine + process enumeration. Не VMProtect, не Themida, реверсится прямолинейно.

## SHA1 (используется массивно)

SHA1 init constants (`H0..H4`) встречаются на 7 разных функциях:
```
0x00DF, 0x088A, 0x0EF1, 0x2FBB, 0x300C, 0x4914, 0x53A9
```

SHA1 round constants (`K0..K3`):
```
K0=0x5A827999 @ 0x1CC6
K2=0x8F1BBCDC @ 0x1BAD
K1, K3 в нескольких местах @ ~0x66xx, ~0x67xx
```

Это означает: **каждая scan-проверка хеширует свои результаты SHA1**. Один центральный SHA1Update в районе 0x1AF2 (вызывается отовсюду), seven different sites init H constants — это значит модуль делает SHA1 для:
1. Hash request response
2. Module integrity self-check
3. Каждой памятной проверки (MEM_CHECK)
4. PAGE_CHECK
5. MODULE_CHECK
6. ...

## Найденная функция scan'а (`0x4E`)

Главный кандидат на **PE-based memory check**:
```asm
push  ebp
mov   ebp, esp
and   esp, 0xfffffff8         ; align stack
sub   esp, 0x164
mov   eax, [0x9000]            ; security cookie
xor   eax, esp
mov   [esp+0x160], eax         ; stack canary
...
mov   esi, [ebx+0x44]          ; ctx->some_addr
cmp   word ptr [esi], 0x5A4D   ; 'MZ' check
jne   0x147
mov   eax, [esi+0x3C]
cmp   dword ptr [esi+eax], 0x4550  ; 'PE' check
jne   0x147
...
mov   [esp+0x34], 0x67452301   ; SHA1 H0
mov   [esp+0x38], 0xefcdab89   ; SHA1 H1
mov   [esp+0x3c], 0x98badcfe   ; SHA1 H2
mov   [esp+0x40], 0x10325476   ; SHA1 H3
mov   [esp+0x44], 0xc3d2e1f0   ; SHA1 H4
call  0x1AF2                   ; SHA1Update
...
call  0x69EE                   ; CompareHash
```

Это PAGE_CHECK или MODULE_CHECK — берёт указатель на загруженный модуль (DLL/EXE) в памяти WoW, валидирует что это действительно PE, хеширует диапазон, сверяет с серверным значением.

## Что нужно для Phase 3 (реимплементации)

Чтобы сделать headless без клиента — нужно:

1. **Извлечь хардкоженые RC4 ключи модуля** (16+16 байт, активируются после HASH_RESULT). Без них вся пост-handshake фаза в headless невозможна.
2. **Перебрать ВСЕ check handlers** и понять что каждый сканит. Их минимум 7 (по числу мест где SHA1 init).
3. **Реимплементировать** в C# каждую проверку: подделать ту память WoW которую модуль ожидает увидеть.

**Реалистичная оценка:** для опытного реверсера — 2-4 недели в IDA. Без IDA по моим скриптам — невозможно (capstone'а недостаточно для понимания control flow / xrefs).

## Вердикт

**Что мы добились в Phase 1+2:**
- ✅ Modul вытащен из трафика (working capture pipeline)
- ✅ Распакован в чистый x86 binary
- ✅ Карта структуры: где код, где данные, где импорты
- ✅ Найдено 7 SHA1-handler'ов (=7 типов проверок)
- ✅ Идентифицирован стиль анти-чита (PE checks, anti-Wine, anti-debug)

**Чего нет без IDA:**
- ❌ Полная картина control flow (cross-references)
- ❌ Точная семантика каждого SHA1-handler'а
- ❌ Извлечённые модульные RC4 ключи

## Что можно сделать без IDA дальше

Если хочешь продолжить чисто скриптами:
- Дамп ВСЕХ функций (по найденным 99 прологам) и поиск crc-подобных циклов
- Поиск 16-байтных high-entropy блоков (кандидаты на RC4 keys)
- Эмуляция модуля в Unicorn Engine с tracing'ом — без понимания вручную, просто запустить и смотреть что сканит

Эмуляция через Unicorn — это **совсем другой проект**, недели работы. Решение принимаешь ты.
