---
title: Telegram Bot
updated: 2025-04-15
tags: [architecture, telegram, deployment]
---

# Telegram Bot (@clwowbot)

Файл: `WowBot.TelegramBot/bot.py` (669 строк, Python)
VPS: `45.131.187.128`, systemd service `wowbot-telegram`

## Что делает
Тестеры репортят баги через Telegram → бот вызывает Claude Code CLI → Claude фиксит код → git push → GitHub Actions собирает patch.zip → тестеры обновляются.

## Команды

| Команда | Что делает |
|---------|-----------|
| `/bug` | Начать баг-репорт (2 шага: описание → лог) |
| `/rollback` | Показать последние коммиты, откатить |
| `/escalate` | Сохранить сложный баг для разработчика |
| `/new` | Сбросить сессию разговора |
| `/status` | Текущая версия бота |
| `/bugs` | Последние 10 баг-репортов |
| `/heavy` | Список эскалированных багов (для разработчика) |
| `/notify` | Уведомить всех юзеров об обновлении (admin) |
| Free chat | Разговорный режим с Claude |

## Архитектура

### Баг-фикс пайплайн (`_do_full_fix()`)
1. `git checkout -- . && git pull` — чистый код
2. `run_claude(prompt, allow_edit=True)` — Claude диагностирует и фиксит (timeout 30 мин)
3. `git_push()` — коммит + пуш (rebase при конфликте)
4. `wait_for_actions_success()` — ждём GitHub Actions (poll каждые 15s, timeout 10 мин)
5. Уведомление всем юзерам

### Claude Code интеграция
- Через subprocess → `ask_wowbot.sh` → `claude -p "$PROMPT" --output-format json`
- Сессии сохраняются в `/opt/wowbot-telegram/sessions.json`
- При expired session — fallback на новую (теряет контекст)

### Данные
```
/opt/wowbot-telegram/
  sessions.json, allowed_users.json, bot.log
/var/www/wowbot/
  bugs/{timestamp}_{user_id}.json
  logs/{timestamp}_{username}_{file}
  version.txt, patch.zip, previous_patch.zip
/home/claude/wow-bot/          — клон репы для Claude Code
```

### Деплой
- `deploy.sh` — rsync на VPS, создание systemd service, nginx на порту 8099
- nginx отдаёт patch.zip, version.txt, previous_patch.zip

## Известные проблемы
- GitHub API без токена — лимит 60 req/hour (с токеном было бы 5000)
- Ошибки обрезаются до 500 символов — может скрыть важное
- Нет rate limiting на free chat
- Нет валидации ответа Claude перед git push

## Связи
- [[overview]] — часть проекта WowBot
- [[updater]] — клиентская часть обновлений
