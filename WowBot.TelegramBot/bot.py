#!/usr/bin/env python3
"""
WowBot Telegram Bot — баг-репорты + автофикс через Claude Code CLI
Одношаговый флоу: описание → Claude фиксит → push → GitHub Actions → patch ready
"""

import os
import json
import logging
import datetime
import subprocess
import asyncio
import uuid
import shutil
import base64
from pathlib import Path
from telegram import Update, InlineKeyboardButton, InlineKeyboardMarkup
from telegram.ext import (
    Application, CommandHandler, MessageHandler, CallbackQueryHandler,
    ConversationHandler, filters, ContextTypes
)

# --- Config ---
BOT_TOKEN = "8188885083:AAHdEfFSf6snAJUoC4nbz1oFf8HzwLq4Y6U"
GITHUB_TOKEN = "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"
GITHUB_REPO = "Hapypopka/wow-bot"
WOWBOT_DIR = Path("/home/claude/wow-bot")
BUGS_DIR = Path("/var/www/wowbot/bugs")
PATCHES_DIR = Path("/var/www/wowbot")
LOGS_DIR = Path("/var/www/wowbot/logs")
ALLOWED_USERS_FILE = Path("/opt/wowbot-telegram/allowed_users.json")
DATA_DIR = Path("/opt/wowbot-telegram")
SESSIONS_FILE = DATA_DIR / "sessions.json"

BUG_DESCRIBE, BUG_ATTACH = range(2)

logging.basicConfig(
    format="%(asctime)s [%(levelname)s] %(message)s",
    level=logging.INFO,
    handlers=[
        logging.FileHandler("/opt/wowbot-telegram/bot.log"),
        logging.StreamHandler()
    ]
)
logger = logging.getLogger(__name__)


# --- Sessions ---
def load_sessions() -> dict:
    if SESSIONS_FILE.exists():
        return json.loads(SESSIONS_FILE.read_text())
    return {}

def save_sessions(sessions: dict):
    SESSIONS_FILE.write_text(json.dumps(sessions, ensure_ascii=False, indent=2))

def get_user_session(user_id: int) -> str | None:
    return load_sessions().get(str(user_id), {}).get("session_id")

def set_user_session(user_id: int, session_id: str, topic: str = ""):
    s = load_sessions()
    s[str(user_id)] = {"session_id": session_id, "topic": topic, "updated": datetime.datetime.now().isoformat()}
    save_sessions(s)

# --- Users ---
def load_allowed_users() -> set:
    if ALLOWED_USERS_FILE.exists():
        return set(json.loads(ALLOWED_USERS_FILE.read_text()).get("users", []))
    return set()

def save_allowed_users(users: set):
    ALLOWED_USERS_FILE.write_text(json.dumps({"users": list(users)}, indent=2))

def is_allowed(user_id: int) -> bool:
    users = load_allowed_users()
    return len(users) == 0 or user_id in users

# --- Bugs ---
def save_bug(user, description: str, result: str = ""):
    BUGS_DIR.mkdir(parents=True, exist_ok=True)
    ts = datetime.datetime.now().strftime("%Y-%m-%d_%H%M%S")
    bug = {"timestamp": ts, "user_id": user.id, "username": user.username or user.first_name,
           "description": description, "result": result, "status": "fixed" if result else "new"}
    (BUGS_DIR / f"{ts}_{user.id}.json").write_text(json.dumps(bug, ensure_ascii=False, indent=2))

# --- Log files ---
def save_log_file(file_bytes: bytes, username: str, original_name: str) -> Path:
    LOGS_DIR.mkdir(parents=True, exist_ok=True)
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    save_path = LOGS_DIR / f"{ts}_{username}_{original_name}"
    save_path.write_bytes(file_bytes)
    latest = LOGS_DIR / f"latest_{username}.log"
    shutil.copy2(str(save_path), str(latest))
    return save_path


# --- Claude Code CLI ---

async def run_claude(prompt: str, session_id: str | None = None,
                     allow_edit: bool = False, timeout: int = 600) -> tuple[str, str]:
    encoded = base64.b64encode(prompt.encode('utf-8')).decode('ascii')

    env_vars = ""
    if session_id:
        env_vars += f"export CLAUDE_RESUME='{session_id}' && "
    if allow_edit:
        env_vars += "export CLAUDE_ALLOW_EDIT=1 && "

    shell_cmd = f"{env_vars}echo '{encoded}' | base64 -d | /home/claude/ask_wowbot.sh"
    cmd = ["sudo", "-u", "claude", "bash", "-c", shell_cmd]

    try:
        proc = await asyncio.create_subprocess_exec(
            *cmd, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=timeout)
        raw = stdout.decode("utf-8", errors="replace").strip()

        result = ""
        session_id_out = session_id or ""

        # Если --resume не нашёл сессию — retry без resume
        if "No conversation found with session ID" in raw and session_id:
            return await run_claude(prompt, session_id=None, allow_edit=allow_edit, timeout=timeout)

        try:
            data = json.loads(raw)
            session_id_out = data.get("session_id", session_id_out)
            result = data.get("result") or ""
            if not result:
                for d in data.get("permission_denials", []):
                    if "plan" in d.get("tool_input", {}):
                        result = d["tool_input"]["plan"]
                        break
            errors = data.get("errors", [])
            if errors and not result:
                result = f"⚠️ {errors[0][:500]}"
        except json.JSONDecodeError:
            result = raw

        if not result:
            err = stderr.decode("utf-8", errors="replace")[:500]
            result = f"⚠️ {err}" if err else "⚠️ Claude не дал ответа."

        return result[:4000], session_id_out

    except asyncio.TimeoutError:
        return "⏰ Таймаут.", session_id or ""
    except Exception as e:
        logger.error(f"Claude error: {e}")
        return f"⚠️ Ошибка: {e}", session_id or ""


# --- Git + GitHub Actions ---

def git_push() -> tuple[bool, str]:
    """Коммит + пуш через sudo -u claude"""
    try:
        def git(cmd: str) -> subprocess.CompletedProcess:
            return subprocess.run(
                ["sudo", "-u", "claude", "bash", "-c", f"cd /home/claude/wow-bot && {cmd}"],
                capture_output=True, text=True, timeout=60
            )

        git("git add -A")
        status = git("git status --porcelain")
        if not status.stdout.strip():
            return False, "Нет изменений"

        result = git("git commit -m 'fix: автофикс через Telegram бот'")
        if result.returncode != 0:
            return False, f"Commit ошибка: {result.stderr[:200]}"

        result = git("git push origin master")
        if result.returncode != 0:
            return False, f"Push ошибка: {result.stderr[:200]}"
        return True, "OK"
    except Exception as e:
        return False, str(e)


async def wait_for_github_actions(timeout: int = 300) -> tuple[bool, str]:
    """Ждём завершения GitHub Actions"""
    import urllib.request
    headers = {"Authorization": f"token {GITHUB_TOKEN}", "Accept": "application/vnd.github+json"}
    url = f"https://api.github.com/repos/{GITHUB_REPO}/actions/runs?per_page=1&branch=master"

    await asyncio.sleep(10)  # Даём GitHub время создать run

    for _ in range(timeout // 15):
        try:
            req = urllib.request.Request(url, headers=headers)
            resp = json.loads(urllib.request.urlopen(req, timeout=10).read())
            runs = resp.get("workflow_runs", [])
            if runs:
                run = runs[0]
                status = run.get("status")
                conclusion = run.get("conclusion")
                if status == "completed":
                    if conclusion == "success":
                        return True, f"v{run.get('run_number', '?')}"
                    else:
                        return False, f"Actions failed: {conclusion}"
        except Exception as e:
            logger.error(f"GitHub API error: {e}")

        await asyncio.sleep(15)

    return False, "Таймаут ожидания сборки"


# --- Handlers ---

async def start(update: Update, context: ContextTypes.DEFAULT_TYPE):
    users = load_allowed_users()
    users.add(update.effective_user.id)
    save_allowed_users(users)
    await update.message.reply_text(
        "👋 Привет! Я бот WowBot.\n\n"
        "• /bug — нашёл баг? Жми сюда\n"
        "• /new — сбросить диалог\n"
        "• /status — версия бота\n"
        "• Или просто пиши — отвечу\n\n"
        "Поехали 🔧"
    )

async def help_cmd(update: Update, context: ContextTypes.DEFAULT_TYPE):
    await update.message.reply_text(
        "📖 /bug — баг-репорт\n/new — новый диалог\n/status — версия\n/bugs — список багов"
    )

async def new_session(update: Update, context: ContextTypes.DEFAULT_TYPE):
    s = load_sessions()
    s.pop(str(update.effective_user.id), None)
    save_sessions(s)
    context.user_data.clear()
    await update.message.reply_text("🔄 Новый диалог. Пиши!")

async def status(update: Update, context: ContextTypes.DEFAULT_TYPE):
    vf = PATCHES_DIR / "version.txt"
    v = vf.read_text().strip() if vf.exists() else "?"
    await update.message.reply_text(f"📦 Версия: v{v}\nЗапусти update.exe")

async def list_bugs(update: Update, context: ContextTypes.DEFAULT_TYPE):
    BUGS_DIR.mkdir(parents=True, exist_ok=True)
    bugs = sorted(BUGS_DIR.glob("*.json"), reverse=True)[:10]
    if not bugs:
        await update.message.reply_text("✅ Нет багов!")
        return
    text = "🐛 Последние:\n\n"
    for bf in bugs:
        b = json.loads(bf.read_text())
        e = "🔴" if b.get("status") == "new" else "🟢"
        text += f"{e} {b['timestamp']} — {b['description'][:50]}...\n"
    await update.message.reply_text(text)


# ==================== /bug ====================

async def bug_start(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if not is_allowed(update.effective_user.id):
        await update.message.reply_text("⛔ Напиши /start")
        return ConversationHandler.END
    context.user_data["bug_texts"] = []
    context.user_data["bug_log_path"] = None
    await update.message.reply_text(
        "🐛 Что случилось?\n\nНапиши:\n• На каком персонаже/спеке\n• Что должно быть\n• Что происходит"
    )
    return BUG_DESCRIBE


async def bug_describe(update: Update, context: ContextTypes.DEFAULT_TYPE):
    context.user_data.setdefault("bug_texts", []).append(update.message.text)
    keyboard = [[InlineKeyboardButton("⏩ Без логов — фиксить!", callback_data="bug_go")]]
    await update.message.reply_text(
        "📝 Принял!\n\nСкинь wowbot.log если есть.\nИли жми кнопку:",
        reply_markup=InlineKeyboardMarkup(keyboard)
    )
    return BUG_ATTACH


async def bug_attach(update: Update, context: ContextTypes.DEFAULT_TYPE):
    username = update.effective_user.username or update.effective_user.first_name
    text = update.message.text or update.message.caption
    if text:
        context.user_data.setdefault("bug_texts", []).append(text)
    doc = update.message.document
    if doc:
        file = await doc.get_file()
        file_bytes = await file.download_as_bytearray()
        log_path = save_log_file(bytes(file_bytes), username, doc.file_name)
        context.user_data["bug_log_path"] = str(log_path)

    keyboard = [[InlineKeyboardButton("✅ Готово — фиксить!", callback_data="bug_go")]]
    files = "📎 Лог подключён\n" if context.user_data.get("bug_log_path") else ""
    await update.message.reply_text(
        f"{files}Ещё что-то? Или жми:",
        reply_markup=InlineKeyboardMarkup(keyboard)
    )
    return BUG_ATTACH


async def bug_go(update: Update, context: ContextTypes.DEFAULT_TYPE):
    """Кнопка нажата — сразу отвечаем, Claude работает в фоне"""
    query = update.callback_query
    await query.answer()

    texts = context.user_data.get("bug_texts", [])
    if not texts:
        await query.edit_message_text("⚠️ Ничего не написал. /bug заново.")
        return ConversationHandler.END

    description = "\n".join(texts)
    log_path = context.user_data.get("bug_log_path")

    status_text = f"🔧 Claude фиксит...\n\n📝 {description[:200]}"
    if log_path:
        status_text += "\n📎 Лог подключён"
    status_text += "\n\nЯ напишу когда будет готово."
    await query.edit_message_text(status_text)

    bot = query.get_bot()
    chat_id = query.message.chat_id
    user = query.from_user
    username = user.username or user.first_name

    asyncio.create_task(
        _do_full_fix(bot, chat_id, user, username, description, log_path)
    )

    context.user_data.clear()
    return ConversationHandler.END


async def _do_full_fix(bot, chat_id, user, username, description, log_path):
    """Полный цикл: диагноз → фикс → коммит → push → ждём Actions → уведомляем"""
    try:
        # 1. Сбрасываем файлы + подтягиваем свежий код
        subprocess.run(["sudo", "-u", "claude", "bash", "-c",
                        "cd /home/claude/wow-bot && git checkout -- . && git pull origin master 2>/dev/null"],
                       capture_output=True, timeout=30)

        # 2. Claude диагностирует + фиксит за один запрос
        log_hint = ""
        if log_path:
            log_hint = f"\n\nЛог: {log_path}\nПрочитай последние 50 строк."

        fix_result, session_id = await run_claude(
            f"Тестировщик WowBot просит:\n\n{description}{log_hint}\n\n"
            f"1. Диагностируй проблему\n"
            f"2. Пофикси код\n"
            f"3. Коротко напиши что сделал (на русском, для не-программиста)\n\n"
            f"ВАЖНО: НЕ рефактори, фикси ТОЛЬКО то что просят. Минимальные изменения.",
            allow_edit=True,
            timeout=1800
        )

        await bot.send_message(chat_id, f"🤖 {fix_result[:2000]}")

        # 3. Коммит + пуш
        pushed, push_msg = git_push()
        if not pushed:
            save_bug(user, description, f"{fix_result}\n\nPush: {push_msg}")
            await bot.send_message(chat_id, f"⚠️ Код пофикшен, но пуш не удался: {push_msg}")
            return

        await bot.send_message(chat_id, "📦 Код отправлен. Собирается на GitHub...")

        # 4. Ждём GitHub Actions
        success, info = await wait_for_github_actions(timeout=300)

        if success:
            save_bug(user, description, fix_result)
            await bot.send_message(chat_id,
                f"✅ Готово! Обновление собрано.\n\nЗапусти update.exe прямо сейчас.")

            # Уведомляем остальных
            for uid in load_allowed_users():
                if uid != user.id:
                    try:
                        await bot.send_message(uid,
                            f"🔔 Обновление!\nЗапусти update.exe\n\nФикс: {description[:80]}")
                    except Exception:
                        pass
        else:
            save_bug(user, description, f"{fix_result}\n\nActions: {info}")
            await bot.send_message(chat_id,
                f"⚠️ Сборка на GitHub не удалась: {info}\n\nРазработчик разберётся.")

    except Exception as e:
        logger.error(f"Full fix error: {e}")
        await bot.send_message(chat_id, f"⚠️ Ошибка: {e}")


async def bug_cancel(update: Update, context: ContextTypes.DEFAULT_TYPE):
    await update.message.reply_text("❌ Отменено.")
    context.user_data.clear()
    return ConversationHandler.END


# --- Free chat ---

async def free_chat(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if not is_allowed(update.effective_user.id):
        await update.message.reply_text("⛔ Напиши /start")
        return
    uid = update.effective_user.id
    session_id = get_user_session(uid)
    await update.message.reply_text("🤔 Думаю...")
    response, new_sid = await run_claude(
        f"{update.message.text}\n\nОтвечай на русском, коротко.",
        session_id=session_id
    )
    set_user_session(uid, new_sid, update.message.text[:50])
    await update.message.reply_text(response)


# --- File outside /bug ---

async def handle_file_outside(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if not is_allowed(update.effective_user.id):
        return
    doc = update.message.document
    if not doc:
        return
    username = update.effective_user.username or update.effective_user.first_name
    file = await doc.get_file()
    file_bytes = await file.download_as_bytearray()
    save_log_file(bytes(file_bytes), username, doc.file_name)
    await update.message.reply_text(f"📎 {doc.file_name} сохранён. Напиши /bug")


# --- Admin ---

async def notify_cmd(update: Update, context: ContextTypes.DEFAULT_TYPE):
    users = load_allowed_users()
    vf = PATCHES_DIR / "version.txt"
    v = vf.read_text().strip() if vf.exists() else "?"
    msg = " ".join(context.args) if context.args else ""
    text = f"🔔 Обновление v{v}! Запусти update.exe."
    if msg: text += f"\n\n{msg}"
    count = 0
    for uid in users:
        try:
            await context.bot.send_message(chat_id=uid, text=text)
            count += 1
        except Exception: pass
    await update.message.reply_text(f"✅ Уведомлены: {count}")


# --- Main ---

def main():
    for d in [BUGS_DIR, DATA_DIR, PATCHES_DIR, LOGS_DIR]:
        d.mkdir(parents=True, exist_ok=True)
    if not ALLOWED_USERS_FILE.exists():
        save_allowed_users(set())

    app = Application.builder().token(BOT_TOKEN).build()

    bug_conv = ConversationHandler(
        entry_points=[CommandHandler("bug", bug_start)],
        states={
            BUG_DESCRIBE: [MessageHandler(filters.TEXT & ~filters.COMMAND, bug_describe)],
            BUG_ATTACH: [
                MessageHandler(filters.Document.ALL, bug_attach),
                MessageHandler(filters.TEXT & ~filters.COMMAND, bug_attach),
                CallbackQueryHandler(bug_go, pattern="^bug_go$"),
            ],
        },
        fallbacks=[CommandHandler("cancel", bug_cancel)],
    )

    app.add_handler(bug_conv)
    app.add_handler(CommandHandler("start", start))
    app.add_handler(CommandHandler("help", help_cmd))
    app.add_handler(CommandHandler("new", new_session))
    app.add_handler(CommandHandler("status", status))
    app.add_handler(CommandHandler("bugs", list_bugs))
    app.add_handler(CommandHandler("notify", notify_cmd))
    app.add_handler(MessageHandler(filters.Document.ALL, handle_file_outside))
    app.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, free_chat))

    logger.info("WowBot Telegram bot started")
    app.run_polling()


if __name__ == "__main__":
    main()
