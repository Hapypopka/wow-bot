---
name: code-review
description: Ревью последних изменений WowBot — баги, spell ID, Lua ошибки
---

Проведи ревью последних изменений в WowBot:

1. Посмотри `git diff HEAD~1` — что изменилось
2. Проверь по каждому файлу:

**AllRotations.cs:**
- Все spell ID правильные? (не перепутаны ранги)
- `IR()`, `HB()`, `HD()`, `Cast()` вместо `IsReadyId()`, `HasBuffById()` (устаревшие)
- Lua синтаксис: скобки закрыты, `end` на месте, строки не обрезаны
- `WB_S.Key` — ключ совпадает с SpecSpells в OverlayWindow
- Нет русских названий спеллов в новом коде (только spell ID)

**BotEngine.cs:**
- Нет `catch { }` без логирования (должно быть `catch (Exception ex) { Logger.Error(...) }`)
- `CountNearbyCombatEnemies` / `CountEnemiesNearTarget` фильтрует союзников
- AoE avoidance не конфликтует с follow/rotation

**OverlayWindow.xaml.cs:**
- Тоглы в SpecSpells совпадают с WB_S ключами в ротации
- Иконки существуют (файл в Icons/)
- SaveSettings сохраняет новые чекбоксы

**EndSceneHook.cs:**
- Lua буфер достаточный (32KB)
- Новые flag коды не конфликтуют

3. Выведи список найденных проблем с номерами строк
