---
title: Updater
updated: 2025-04-15
tags: [architecture, updater, deployment]
---

# WowBot.Updater — Авто-обновление

Файл: `WowBot.Updater/Program.cs` (219 строк)

## Как работает

1. Запрашивает `https://api.github.com/repos/Hapypopka/wow-bot/releases/latest`
2. Сравнивает `tag_name` с локальным `version.txt`
3. Если новее:
   - Создаёт бэкап в `_backup/` (DLL, Icons/, version.txt)
   - Скачивает `patch.zip` из assets релиза
   - Распаковывает поверх текущих файлов
   - Обновляет version.txt

## Откат
`--rollback` — восстанавливает файлы из `_backup/`

## Защита
- Проверяет что WowBot.Injector не запущен перед обновлением

## Пайплайн (полный)
1. Push в GitHub → GitHub Actions собирает patch.zip → создаёт Release
2. Пользователь запускает update.exe (или автоматически)
3. Updater скачивает patch.zip, применяет
4. Nginx на VPS (порт 8099) тоже отдаёт patch.zip (для [[telegram-bot]])

## Связи
- [[telegram-bot]] — баг-фикс пайплайн заканчивается новым релизом
- [[overview]] — часть проекта
