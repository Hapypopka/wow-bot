# SlaveController — полный план

## Файлы

### 1. Navigation.cs (переписан)
Чистый поворот и движение. Без костылей.

```
Публичный API:
- bool FaceUnit(player, target) → true когда повёрнут
- void StartMoveForward()
- void StopMoveForward()
- void StopAll()
- bool IsFacing(from, to)
- bool IsMovingForward

Внутренняя логика FaceUnit:
- State machine: Idle → Turning → Done
- Тик 1: записать facing в память + TurnStart (ОДИН РАЗ)
- Тик 2-5: ждать, НЕ перезаписывать
- Когда довернулся: TurnStop → Done
- Следующий вызов: Done → Idle (готов к новому повороту)
```

### 2. SlaveController.cs (НОВЫЙ)
Единственное место где живёт слейв-логика. BotEngine просто вызывает `SlaveController.Tick()`.

```csharp
public class SlaveController
{
    // Зависимости
    private Navigation _nav;
    private EndSceneHook _hook;
    private ObjectManager _objectManager;

    // Состояние
    public enum State { Idle, Following, Attacking, Stopped }
    public State CurrentState { get; private set; }

    // Конфиг
    public string MasterName { get; set; }
    public float FollowDistance { get; set; } = 8f;
    public bool IsHealer { get; set; }
    public bool AlwaysAssist { get; set; }

    // Внутреннее
    private ulong _masterGuid;
    private int _stopTimer;        // Тиков до перехода Stopped → Idle
    private int _assistTick;       // Счётчик для периодического AssistUnit
    private int _healTick;         // Счётчик для хилера
}
```

### State Machine:

```
┌──────┐  "ко мне"   ┌───────────┐
│ Idle │────────────→│ Following │
└──┬───┘             └─────┬─────┘
   │                       │ "стоп"
   │  "бейте таргет"       ▼
   │                 ┌──────────┐
   └────────────────→│ Stopped  │──(3сек)──→ Idle
                     └──────────┘
   │                       ▲
   │  "бейте таргет"       │ "стоп"
   └────────────────→┌──────────┐
                     │Attacking │
                     └──────────┘
```

### Tick() по состояниям:

```
State.Idle:
  - Ничего не делать
  - Бот не управляет персонажем
  - Юзер может играть вручную

State.Following:
  - Найти мастера (по GUID из ObjectManager)
  - dist = расстояние до мастера

  ЕСЛИ dist > followDistance:
    faced = _nav.FaceUnit(player, master)   // поворот
    ЕСЛИ faced:
      _nav.StartMoveForward()               // бежать
    ИНАЧЕ:
      _nav.StopMoveForward()                // ждём поворот

  ЕСЛИ dist <= followDistance:
    _nav.StopMoveForward()                  // дошли, стоим
    _nav.FaceUnit(player, master)           // смотрим на мастера

  ЕСЛИ AlwaysAssist И мастер в бою:
    AssistUnit(мастер) каждые ~1.5 сек
    → переход в Attacking

State.Attacking:
  - AssistUnit(мастер) каждые ~1.5 сек (или чаще если AutoSwitch)
  - target = текущий таргет (из ObjectManager)

  ЕСЛИ target == null или мёртв:
    AssistUnit(мастер)  // новый таргет
    return

  ЕСЛИ target это Player (мастер при "ко мне"):
    → логика Following (подбег к мастеру)
    return

  dist = расстояние до target
  ЕСЛИ dist > castRange:
    faced = _nav.FaceUnit(player, target)
    ЕСЛИ faced:
      _nav.StartMoveForward()
    ИНАЧЕ:
      _nav.StopMoveForward()
  ИНАЧЕ:
    _nav.StopMoveForward()
    _nav.FaceUnit(player, target)           // всегда лицом к цели
    ЕСЛИ IsHealer:
      execHealRotation()
    ИНАЧЕ:
      execDPSRotation()

  ЕСЛИ IsHealer:
    execHealRotation() каждые ~500мс (независимо от подбега)

State.Stopped:
  _nav.StopAll()
  _stopTimer--
  ЕСЛИ _stopTimer <= 0:
    → State.Idle
```

### Команды мастера → переходы:

```csharp
void OnCommand(Command cmd, string arg)
{
    switch (cmd)
    {
        case Follow:
            _nav.StopAll();
            _nav.ResetTurn();
            MasterName = arg;
            FindMasterGuid(arg);
            // Таргетим мастера (для подбега через бота)
            _hook.ExecuteLua($"TargetUnit('{arg}')", 200);
            CurrentState = State.Following;
            AlwaysAssist = true;   // Авто-атака при бое мастера
            break;

        case Attack:
            _nav.StopAll();
            _nav.ResetTurn();
            MasterName = arg;
            // Берём таргет мастера
            _hook.ExecuteLua($"AssistUnit('{arg}') StartAttack()", 200);
            CurrentState = State.Attacking;
            break;

        case Stop:
            _nav.StopAll();
            CurrentState = State.Stopped;
            _stopTimer = 20;  // 20 тиков = 3 сек → Idle
            break;
    }
}
```

### FindMasterGuid:
```csharp
void FindMasterGuid(string name)
{
    // Через Lua: TargetUnit → читаем GUID из ObjectManager
    _hook.ExecuteLua($"TargetUnit('{name}')", 200);
    Thread.Sleep(150);
    var target = _objectManager.GetTarget();
    if (target != null)
        _masterGuid = target.Guid;
}
```

### 3. Hivemind.cs — упрощение
Hivemind становится тонким:
- Мастер: отправляет команды через SendAddonMessage
- Слейв: принимает команды через listener → передаёт в SlaveController
- Вся логика слейва в SlaveController

### 4. BotEngine.cs — упрощение
```csharp
// В Tick():
if (Hivemind.CurrentRole == Role.Slave && _slaveController != null)
{
    _slaveController.Tick(player, enemyCountLua, SpellFlagsLua);
    return;  // Слейв-контроллер управляет всем
}
// ... обычная логика (ротация/follow/баффы для не-слейва)
```

## Что убирается из текущего кода:
- BotEngine: весь блок `if (Hivemind.Role.Slave && ...)` (подбег, хилер, isFollowing)
- Hivemind: `SlaveTickFollow()`, `_followMaster`, `_followAttack`, `_wantRotation`
- Hivemind: `ExecuteSlaveCommand` логика follow/attack (переносится в SlaveController)
- BotEngine: `_isMovingForward`, `_isApproaching`, `_lastApproachX/Y` для слейва
- Navigation: старые `MoveToward`, `StrafeToward` (не используются)

## Что остаётся:
- Hivemind: SendAddonMessage (мастер), listener (слейв), ParseSlaveResponse
- BotEngine: обычная ротация/follow/баффы (для соло-игры)
- Navigation: новый чистый FaceUnit + MoveForward

## Порядок реализации:
1. Navigation.cs — переписать (уже готов)
2. SlaveController.cs — создать с нуля
3. BotEngine.cs — подключить SlaveController, убрать старый слейв-код
4. Hivemind.cs — упростить, команды → SlaveController
5. Тест: FaceUnit (поворот без дёрганья)
6. Тест: Following (бежит к мастеру, останавливается)
7. Тест: Attacking (бьёт цель мастера)
8. Тест: Stop (останавливается, можно играть вручную)
9. Тест: Хилер (хилит + следует)
10. Тест: Данж (следование по коридорам)
