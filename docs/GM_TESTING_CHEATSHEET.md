# GM команды для тестирования бота (CMaNGOS / SPP)

## Первый запуск
В консоли mangosd.exe:
```
account create test test
account set gm test 3
```

## Основные команды (в чат WoW с точкой)

### Стартовая настройка персонажа
```
.gm on                    — включить GM режим
.levelup 80               — уровень 80
.learn all_myclass         — выучить все спеллы класса
.modify money 999999999    — золото (~99999г)
.modify maxhp 99999999     — огромное HP (= бессмертие)
.modify hp 99999999        — восстановить HP
.modify speed 5            — скорость x5
.cooldown                  — сбросить все КД
```

### Телепорт
```
.tele <name>               — телепорт по имени
.lookup tele <name>        — найти телепорт
.gps                       — текущие координаты
```

Полезные телепорты:
```
.tele dalaran
.tele orgrimmar
.tele stormwind
```

### Спавн мобов
```
.npc add <id>              — заспавнить NPC
.npc delete                — удалить таргетнутого NPC
.respawn                   — респавнить ближайших мобов
.lookup creature <name>    — найти ID моба по имени
```

Полезные ID:
```
36612  — Lord Marrowgar (ICC)
36597  — Lich King (ICC)
33288  — Yogg-Saron (Ulduar)
15990  — Kel'Thuzad (Naxx)
31125  — Archavon (VoA)
2674   — Training Dummy (тренировочный манекен)
```

### Спеллы и эффекты
```
.cast <spellId>            — кастануть спелл
.aura <spellId>            — повесить ауру (бафф)
.unaura <spellId>          — снять ауру
.unaura all                — снять все ауры
```

### Полезные ауры
```
.aura 18798                — неуязвимость
```

### Управление мобами
```
.damage <amount>           — нанести урон таргету
.die                       — убить таргет
.revive                    — воскресить таргет
```

### Предметы
```
.additem <id>              — дать предмет
.additemset <id>           — дать сет целиком
.lookup item <name>        — найти ID предмета
```

### Поиск
```
.lookup creature <name>    — найти ID моба
.lookup spell <name>       — найти ID спелла
.lookup item <name>        — найти ID предмета
.lookup tele <name>        — найти телепорт
```

### Рес из консоли (если умер и не можешь писать в чат)
В окне mangosd.exe:
```
revive ИмяПерсонажа
```

## Переключение серверов
- `D:\Games\Вов\switch_to_local.bat` — играть на локальном сервере
- `D:\Games\Вов\switch_to_wowcircle.bat` — вернуться на WoWCircle
