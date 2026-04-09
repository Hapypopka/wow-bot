# SimCraft WotLK 3.3.5a — Ротации ВСЕХ классов

**Источник**: `/c/Проекты/SimCraft_WotLK_Real/engine/` (SC_MAJOR_VERSION "335")
**Репозиторий**: github.com/Magmonix/SIMC_WotLK_3.3.5a

---

## Shadow Priest (sc_priest.cpp)

1. Shadow Fiend (по кулдауну)
2. **Слово Тени: Боль** — если не тикает (shadow_weaving_wait=1 — первый для набора стаков)
3. Берсерк (тролль)
4. **Прикосновение вампира** — если не тикает ИЛИ осталось < cast_time
5. **Пожирающая чума** — если не тикает
6. **Взрыв разума** — если use_mind_blast=1 и haste > 0.60
7. **Слово Тени: Смерть** — mb_min_wait=0.3, mb_max_wait=1.2
8. Внутренний огонь + **Пытка разума** (филлер)
9. Слово Тени: Смерть (при движении)
10. Рассеивание (крайний случай)

**Ключевое**: SWP кастуется ПЕРВЫМ для набора Shadow Weaving (5 стаков = +10% теневого урона). VT refresh при remains < cast_time.

---

## Balance Druid / Сова (sc_druid.cpp)

1. Волшебный огонь (если есть Improved Faerie Fire)
2. Лунный огонь (при движении, если не тикает)
3. Рой насекомых (при движении, если не тикает)
4. Тайфун (при движении)
5. Озарение (при низкой мане, trigger=-2000)
6. Деревья (time >= 5)
7. **Звездопад** (если НЕТ Eclipse — между проками!)
8. Звездный огонь (если T8 4PC бафф)
9. Лунный огонь (если не тикает И нет Eclipse)
10. Рой насекомых (если не тикает И нет Eclipse И глиф)
11. **Гнев** (если trigger_lunar — кастуем для прока Lunar Eclipse)
12. **Звездный огонь** (если горит Lunar Eclipse И remains > cast_time)
13. **Гнев** (если горит Solar Eclipse И remains > cast_time)
14. Звездный огонь (дефолтный филлер)

**Ключевое**: Starfall между Eclipse проками. Wrath для прока Lunar, Starfire при Lunar Eclipse. DoTs обновлять вне Eclipse.

---

## Ret Paladin (sc_paladin.cpp)

1. **Правосудие** (Judgement)
2. **Удар воина Света** (Crusader Strike)
3. **Божественная буря** (Divine Storm)
4. **Освящение** (Consecration) — если таргет проживёт >= 6с
5. **Молот Гнева** (Hammer of Wrath) — execute < 20%
6. **Экзорцизм** (Exorcism) — если прокнул Art of War
7. Священный удар (если есть талант)
8. Щит праведности (если одноручное оружие)

**Ключевое**: FCFS (First Come First Serve) — что готово, то и кастуй. Порядок: Judge → CS → DS → Cons → HoW → Exo(AoW).

---

## Demo Warlock / Демо Лок (sc_warlock.cpp)

1. Демоническое подчинение (Demonic Empowerment) — усиление пета
2. **Метаморфоза** (Metamorphosis)
3. **Жертвенный огонь** (Immolate) — если осталось < cast_time
4. **Аура Жертвенного огня** (Immolation) — если T10 4PC прок ИЛИ мета < 15с
5. Проклятие Рока (Curse of Doom) — если таргет проживёт >= 70с
6. **Огонь Души** (Soul Fire) — если прок Decimation + Molten Core
7. **Порча** (Corruption) — если не тикает, таргет >= 8с
8. Огонь Души — если прок Decimation (без Molten Core)
9. **Испепеление** (Incinerate) — если прок Molten Core
10. Life Tap — если мана < 30% И мета не горит
11. Проклятие Агонии (при движении)
12. Испепеление (филлер) / Стрела Тьмы (если нет Emberstorm)

**Ключевое**: Мета по кулдауну. Soul Fire приоритетнее при Decimation (<35% HP). Life Tap только вне Мета.

---

## Affliction Warlock / Аффлик Лок (sc_warlock.cpp)

1. **Haunt** — если бафф < 3с ИЛИ Corruption < 4с
2. **Порча** (Corruption) — если не тикает
3. **Нестабильная порча** (Unstable Affliction) — если remains < cast_time, таргет >= 5с
4. **Проклятие Агонии** (Curse of Agony) — если не тикает, таргет >= 20с
5. **Вытягивание души** (Drain Soul) — если HP босса <= 25% (execute!)
6. Стрела Тьмы (филлер)

**Ключевое**: Haunt обновлять по кулдауну (поддерживает бафф на DoTs). Drain Soul на execute. 

---

## Destruction Warlock / Дестро Лок (sc_warlock.cpp)

1. **Конфлаграция** (Conflagrate) — если есть Immolate
2. **Жертвенный огонь** (Immolate) — если remains < cast_time
3. **Хаос Болт** (Chaos Bolt)
4. Проклятие Рока (если таргет >= 70с)
5. Проклятие Агонии (при движении)
6. Испепеление (Incinerate) — филлер

---

## Arms Warrior / Оружейник (sc_warrior.cpp)

1. Ярость крови (Bloodrage) — если rage <= 65
2. **Героический удар** (Heroic Strike) — если rage >= 50
3. **Рассечение** (Rend)
4. **Превосходство** (Overpower) — если бафф Taste for Blood < 1.5с
5. **Вихрь клинков** (Bladestorm)
6. **Смертельный удар** (Mortal Strike)
7. Превосходство — если прок Taste for Blood
8. **Казнь** (Execute) — если HP >= 20% И прок Sudden Death
9. Казнь — если HP <= 20%
10. Удар (Slam)

---

## Fury Warrior / Неистовство (sc_warrior.cpp)

1. Ярость крови — rage <= 65
2. Безрассудство (Recklessness)
3. Жажда смерти (Death Wish)
4. **Героический удар** — rage >= 25
5. **Вихрь** (Whirlwind)
6. **Кровожадность** (Bloodthirst)
7. **Удар** (Slam) — если прок Bloodsurge
8. **Казнь** (Execute)
9. Ярость берсерка (Berserker Rage)

---

## Assassination Rogue / Ликвидация (sc_rogue.cpp)

1. Голод крови (Hunger for Blood) — если бафф < 2
2. Серия ударов (Slice and Dice) — если < 1с
3. **Рваная рана** (Rupture) — если CP >= 4, таргет > 15с, SnD > 11с
4. Хладнокровие + **Отравление** (Envenom) — CP >= 4, бафф Envenom не горит
5. Отравление — CP >= 4, энергия > 90
6. Отравление — CP >= 2, SnD < 2с
7. Обманный путь (Tricks of the Trade)
8. **Расправа** (Mutilate) — CP < 4
9. Исчезновение (Vanish) — time > 30, energy > 50

---

## Combat Rogue / Бой (sc_rogue.cpp)

1. Серия ударов (если down И time < 4)
2. Серия ударов (если < 2с И CP >= 3)
3. Обманный путь
4. **Кровавая баня** (Killing Spree) — энергия < 20, SnD > 5
5. **Веерная атака** (Blade Flurry)
6. **Рваная рана** (Rupture) — CP = 5, таргет > 10с
7. **Удар сзади** (Backstab) / **Зловещий удар** (Sinister Strike)
8. **Потрошение** (Eviscerate)
9. Адреналин (Adrenaline Rush) — энергия < 20

---

## Arcane Mage / Аркан Маг (sc_mage.cpp)

1. Камень маны (Mana Gem)
2. Воскрешение (Evocation) — если нет стаков Arcane Blast
3. **Чародейские стрелы** (Arcane Missiles) — если прок Missile Barrage
4. Присутствие разума + **Чародейский взрыв** (Arcane Blast)
5. Чародейский взрыв (dps режим)
6. Чародейские стрелы (филлер)
7. Чародейский снаряд (при движении)
8. Огненный шар (при движении)

---

## Fire Mage / Огонь Маг (sc_mage.cpp)

1. Камень маны
2. **Пиро** (Pyroblast) — если прок Hot Streak
3. **Живая бомба** (Living Bomb)
4. **Огненный шар** (Fireball) / Шар ледяного пламени (Frostfire Bolt)
5. Воскрешение
6. Огненный шар (при движении)
7. Ледяное копье (при движении)

---

## Frost Mage / Фрост Маг (sc_mage.cpp)

1. Камень маны
2. **Глубокая заморозка** (Deep Freeze)
3. Ледяная стрела (Frostbolt) — если frozen=1
4. Cold Snap — если CD Deep Freeze > 15с
5. Шар ледяного пламени — если прок Brain Freeze
6. **Ледяная стрела** (Frostbolt) — филлер
7. Воскрешение
8. Ледяное копье (при движении, frozen)
9. Огненный шар (при движении)

---

## Enhancement Shaman / Энх Шаман (sc_shaman.cpp)

1. Обрыв колдовства (Wind Shear)
2. Героизм (если таргет <= 60с)
3. Тотем огненного элементаля
4. Дух волков (Feral Spirit)
5. **Молния** (Lightning Bolt) — если Maelstrom Weapon = 5 стаков!
6. **Удар бури** (Stormstrike)
7. **Огненный шок** (Flame Shock) — если не тикает
8. **Шок земли** (Earth Shock)
9. Тотем магмы
10. Новая огненная
11. Щит молний (Lightning Shield)
12. **Удар лавы** (Lava Lash)
13. Шаманская ярость

**Ключевое**: Молния ТОЛЬКО при 5 стаках Maelstrom Weapon. Stormstrike > Shocks > Lava Lash.

---

## Elemental Shaman / Элем Шаман (sc_shaman.cpp)

1. Обрыв колдовства
2. Героизм (<= 59с)
3. Мастерство стихий (Elemental Mastery) — бурст КД
4. **Огненный шок** (Flame Shock) — если не тикает
5. **Выброс лавы** (Lava Burst) — если Flame Shock remains >= cast_time
6. Тотем огненного элементаля / Опаляющий тотем
7. Цепная молния (если адды > 1)
8. **Молния** (Lightning Bolt) — филлер
9. Новая огненная (при движении)
10. Громовой удар (при движении)

**Ключевое**: Lava Burst ТОЛЬКО при активном Flame Shock. FS > LvB > LB.

---

## Blood DK / Кровь ДК (sc_death_knight.cpp)

1. Истерия (Hysteria) — time 5-60
2. Армия мёртвых (Raise Dead) — time >= 10
3. **Ледяное прикосновение** — если болезни не тикают
4. **Удар чумы** — если болезни не тикают
5. **Удар сердца** (Heart Strike) — основной урон
6. **Удар смерти** (Death Strike)
7. Танцующее руническое оружие (Dancing Rune Weapon)
8. Укрепление рунического оружия (Empower Rune Weapon) — если все руны down
9. **Спираль смерти** (Death Coil) — дамп руник пауэра

---

## Frost DK / Фрост ДК (sc_death_knight.cpp)

1. Неразрушимая броня (Unbreakable Armor)
2. Армия мёртвых — time >= 5
3. Ледяное прикосновение — болезни
4. Удар чумы — болезни
5. **Вой Взрыва** (Howling Blast) — если прок Rime + Killing Machine
6. Смертельный холод (Deathchill)
7. **Уничтожение** (Obliterate) — основной урон
8. Кровавое касание (Blood Tap) — руны
9. Удар крови (Blood Strike) — руны
10. Укрепление рунического оружия
11. **Ледяной удар** (Frost Strike) — дамп RP
12. Вой Взрыва — если прок Rime

**Ключевое**: Obliterate основной, Frost Strike дамп RP, Howling Blast на проки.

---

## Unholy DK / Анхоли ДК (sc_death_knight.cpp)

1. Костяной щит (Bone Shield) — если не горит
2. Армия мёртвых
3. Ледяное прикосновение — болезни
4. Удар чумы — болезни
5. Кровавое касание — руны (если Reaping)
6. Удар крови — конверсия рун
7. **Удар Скверны** (Scourge Strike) — основной урон
8. Призыв горгульи (Summon Gargoyle) — бурст КД
9. Укрепление рунического оружия
10. **Спираль смерти** (Death Coil) — дамп RP

**Ключевое**: Scourge Strike основной, Death Coil дамп, болезни поддерживать.

---

## Beast Mastery Hunter / БМ Хантер (sc_hunter.cpp)

1. Kill Command (синк с Bestial Wrath)
2. **Звериная ярость** (Bestial Wrath)
3. Быстрая стрельба (Rapid Fire)
4. **Убийственный выстрел** (Kill Shot) — execute
5. **Жало змеи** (Serpent Sting)
6. Прицельный выстрел / Мультишот
7. Чародейский выстрел (Arcane Shot)
8. **Меткий выстрел** (Steady Shot) — филлер

---

## Marksmanship Hunter / ММ Хантер (sc_hunter.cpp)

1. Жало змеи (Serpent Sting)
2. Быстрая стрельба
3. Kill Command
4. Усмиряющий выстрел (Silencing Shot)
5. Убийственный выстрел
6. **Выстрел Химеры** (Chimera Shot) — основной
7. Прицельный / Мультишот
8. Чародейский выстрел
9. Готовность (Readiness) — ресет КД
10. Меткий выстрел — филлер

---

## Survival Hunter / Сурв Хантер (sc_hunter.cpp)

1. Быстрая стрельба
2. Kill Command
3. Убийственный выстрел
4. **Взрывной выстрел** (Explosive Shot) — на проки Lock and Load
5. **Чёрная стрела** (Black Arrow) — DoT
6. Жало змеи
7. Прицельный / Мультишот
8. Чародейский выстрел (на проки Lock and Load)
9. Меткий выстрел — филлер

**Ключевое**: Explosive Shot основной, Black Arrow для Lock and Load проков.
