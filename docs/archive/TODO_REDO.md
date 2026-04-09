# Изменения из сессии — нужно переделать после отката

Откат к коммиту a586169. Ниже — что было сделано и нужно будет повторить правильно.

## ХОРОШИЕ изменения (переприменить):

### 1. Frost DK — ротация переписана
- Howling Blast = "Воющий ветер" (было неправильно "Ледяной удар")
- Frost Strike = "Ледяной удар" (было "Лик смерти" — путаница!)
- Мор только при болезнях <3 сек (обновление)
- Добавлены: Кровоотвод, Усиление рунического оружия, Зимний горн
- Убрана HB2 (дубль)
- Файлы: AllRotations.cs (FROST секция), OverlayWindow.xaml.cs (Frost DK toggles)

### 2. Дубли властей ДК убраны из ClassBuffs
- ["DEATHKNIGHT"] убран из ClassBuffs (власти через радио PresenceOptions)
- Файл: OverlayWindow.xaml.cs

### 3. Logger — отдельный файл для каждого персонажа
- SetCharName(name) → wowbot_{name}.log
- Вызов при коннекте в MainWindow
- Файлы: Logger.cs, MainWindow.xaml.cs
- ПРОБЛЕМА: Logger.cs static — все инстансы бота в одном процессе? Нет, каждый бот отдельный процесс — ок.

### 4. Async Detach с таймаутом
- BtnDetach_Click стал async, Dispose в Task.Run с 2с таймаутом
- Файл: MainWindow.xaml.cs

### 5. Ферал — вернута проверка комбата таргета
- `if not UnitAffectingCombat('target') then return end` — вернута в Feral секцию
- Файл: AllRotations.cs

### 6. Hivemind Follow — добавлен FollowUnit
- К CTM добавлен `FollowUnit('{arg}')` через Lua
- Файл: Hivemind.cs

### 7. CTM верификация записи
- После WriteFloat — перечитываем и проверяем
- Файл: ClickToMove.cs

## ПРОБЛЕМЫ которые нужно решить по-другому:

### A. Tick крашится после 1 тика у слейва
- Tick#1 выполняется, потом тишина. Ни Tick#2, ни error в логе.
- Возможная причина: exception в Tick вне try-catch, или ExecuteLua зависает на listener install
- РЕШЕНИЕ: обернуть ВЕСЬ Tick (включая ранние return) в try-catch с логированием

### B. CTM follow не работает на некоторых клиентах
- Координаты пишутся правильно, WoW их игнорирует
- FollowUnit через Lua работает но ненадёжно (ломается при касте/урон)
- РЕШЕНИЕ: разобраться почему CTM не работает. Может CVar autointeract нужно включить программно

### C. Второй слейв периодически перестаёт получать команды
- Листенер установлен (REG=true), CMD приходит в Lua, но C# не читает
- Связано с проблемой A — если Tick не тикает, команды не читаются

## Иконки скачанные (сохранить при откате):
- blood_tap.jpg, empower_rune.jpg, horn_winter.jpg — DK
- heroic_strike.jpg, shield_block.jpg, shield_wall.jpg, last_stand.jpg — Warrior
- berserker_rage.jpg — Warrior racial
- cat_form.jpg — Druid
- battle_shout.jpg, commanding_shout.jpg — Warrior
- battle_stance.jpg, defensive_stance.jpg, berserker_stance.jpg — Warrior
- blood_presence.jpg, frost_presence.jpg, unholy_presence.jpg — DK
- blessing_sanctuary.jpg, aura_crusader.jpg — Paladin
