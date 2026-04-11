# GM команды для тестирования бота (TrinityCore / SPP)

## Первый запуск
В консоли worldserver:
```
.account create test test
.account set gmlevel test 3 -1
```

## Основные команды (в чат WoW с точкой)

### Персонаж
```
.god on/off          — бессмертие
.levelup 80          — уровень 80
.modify speed 5      — скорость x5
.modify hp 100000    — установить HP
.cooldown            — сбросить все КД
.additem <id> <кол>  — дать предмет
.learn <spellId>     — выучить спелл
```

### Телепорт
```
.tele <name>         — телепорт (список: .lookup tele <name>)
.go xyz X Y Z [map]  — телепорт по координатам
.gps                 — показать текущие координаты
```

Полезные телепорты:
```
.tele dalaran
.tele icecrown_citadel
.tele ulduar
.tele naxxramas
.tele orgrimmar
```

### Спавн мобов и боссов
```
.npc add <id>        — заспавнить NPC (постоянно)
.npc add temp <id>   — заспавнить временно
.npc delete          — удалить таргетнутого NPC
.respawn             — респавнить ближайших мобов
```

Полезные ID:
```
36597  — Lich King (ICC)
33288  — Yogg-Saron (Ulduar)
15990  — Kel'Thuzad (Naxx)
31125  — Archavon (VoA)
Тренировочный манекен — .lookup creature training dummy
```

### Спеллы и эффекты
```
.cast <spellId>                  — кастануть спелл
.cast target <spellId>           — кастануть на таргет
.aura <spellId>                  — повесить ауру
.unaura <spellId>                — снять ауру
```

### Тестирование AoE Avoidance
```
.cast 72762          — Defile (лужа Лича)
.cast 69576          — Malleable Goo (ICC Festergut)
.cast 72273          — Vile Gas (ICC Festergut)
```

### Тестирование группы
```
.npc add temp <id>   — заспавнить несколько мобов
.damage <amount>     — нанести урон таргету (для теста хила)
.die                 — убить таргет
.revive              — воскресить
```

### Поиск
```
.lookup creature <name>  — найти ID моба по имени
.lookup spell <name>     — найти ID спелла
.lookup item <name>      — найти ID предмета
.lookup tele <name>      — найти телепорт
```

## Переключение серверов
- `switch_to_local.bat` — играть на локальном TC
- `switch_to_wowcircle.bat` — вернуться на WoWCircle
