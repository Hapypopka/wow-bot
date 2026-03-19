---
name: project_architecture
description: WowBot architecture overview — components, data flow, key mechanics
type: project
---

## Компоненты

### WowBot.Core (net8.0-windows, x86)
- **Memory/WinApi.cs** — P/Invoke (OpenProcess, Read/WriteProcessMemory, VirtualAllocEx, D3D9 COM)
- **Memory/MemoryReader.cs** — обёртка чтения/записи (uint, float, string, bytes)
- **Game/Offsets.cs** — кастомные WoWCircle оффсеты (HEALTH=0x18 вместо 0x58 и т.д.)
- **Game/ObjectManager.cs** — обход объектов WoW, LocalPlayer, GetTarget(), GetUnitsInRange()
- **Game/Entities/** — WowObject → WowUnit → WowPlayer (HP, Mana, Position, CastId, Name)
- **Game/EndSceneHook.cs** — хук D3D9 EndScene: codecave 512+16384+128 байт, x86 asm, Lua_DoString
- **Game/D3D9Helper.cs** — фейковое D3D9 устройство для поиска EndScene (fallback при оверлеях)
- **Game/Navigation.cs** — FaceUnit (запись float в +0x7A8), GetAngleTo, IsFacing
- **Game/ClickToMove.cs** — CTM через запись координат в память (0x00CA11D8)
- **Game/LuaReader.cs** — двухпроходный скан макроса для чтения Lua→C#
- **Game/BotEngine.cs** — главный координатор: таймер 150мс, Follow/Rotation/Buffs/AoE
- **Game/Rotations/AllRotations.cs** — все ротации в одном Lua-скрипте
- **Logger.cs** — wowbot.log рядом с exe

### WowBot.Injector (WPF, net8.0-windows, x86)
- **MainWindow** — Attach/Detach, Lua консоль, Dump, update loop 200мс
- **OverlayWindow** — прозрачный оверлей поверх WoW, меню с секциями

## Data Flow
```
MainWindow.Attach → MemoryReader → ObjectManager → EndSceneHook.Install
                                                  → LuaReader.Init
BotEngine (150ms tick):
  → ObjectManager.Update (read objects)
  → Buffs (каждые ~3с): BuildBuffScript → ExecuteLua
  → Follow: CTM.MoveTo
  → Rotation: SpellFlagsLua + AllRotations script → ExecuteLua
OverlayWindow → settings → BotEngine (через MainWindow update loop)
```

## Реализованные спеки (5 шт)
1. Balance Druid — Eclipse, DoTs, Starfall, Treants
2. Shadow Priest — VT/DP/SWP (2-sec guard), MB, MF, Dispersion
3. Demo Warlock — Meta, Life Tap, DoTs, Curse radio, Decimation/Molten Core procs
4. Ret Paladin — FCFS (Judge→DS→CS→Cons→Exo по проку→HoW)
5. Holy Paladin — хилер, поиск дохлого, Beacon/SS на фокус, правосудие

## Buff System
BuildBuffScript() генерирует однострочный Lua. Порядок: Аура → Печать → Благословение → Камень чар → Self-баффы → Рейд-баффы (с реагентами)

## Ключевые механики
- SpellFlagsLua: `WB_S={VT=true,CoA=false,...}` — тоглы из UI передаются в Lua
- Double-cast guard: WB_VT/WB_DP/WB_SWP глобальные таймеры 2сек
- Eclipse: HasBuffById(48518/48517) → WB_ECL state
- Healer: WB_HEALER=true → пропуск DPS prechecks
- Буфер Lua: 16384 байт (было 8192, увеличено т.к. скрипт ~8KB)

**Why:** Документация архитектуры для быстрого вхождения в контекст
**How to apply:** Перед работой над ботом — перечитать для понимания связей
