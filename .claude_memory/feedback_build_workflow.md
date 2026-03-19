---
name: feedback_build_workflow
description: Workflow сборки — всегда собирать после изменений, убивать процесс перед сборкой
type: feedback
---

После любых изменений в коде — ОБЯЗАТЕЛЬНО собрать и перезапустить:
1. `taskkill /f /im WowBot.Injector.exe` (или попросить юзера закрыть)
2. `dotnet publish WowBot.Injector -c Release -r win-x86 --self-contained -o publish`
3. Юзер запускает `publish\WowBot.Injector.exe`

**Why:** Юзер ожидает что после изменений я сам собираю — как делал Opus. Без сборки изменения не попадут в exe.
**How to apply:** После каждого Edit в .cs файле — сразу собирать. Если exe блокирует — попросить закрыть.
