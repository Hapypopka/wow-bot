#!/bin/bash
# Деплой Telegram бота на VPS
# Запускать локально: bash WowBot.TelegramBot/deploy.sh

VPS="root@45.131.187.128"
SSH_KEY="~/.ssh/id_ed25519_vmmo"
REMOTE_BOT="/opt/wowbot-telegram"
REMOTE_WEB="/var/www/wowbot"
REMOTE_REPO="/home/claude/wow-bot"

echo "=== Деплой WowBot Telegram Bot ==="

# 1. Директории
echo "[1/6] Создаю директории..."
ssh -i $SSH_KEY $VPS "mkdir -p $REMOTE_BOT $REMOTE_WEB/bugs $REMOTE_REPO"

# 2. Клонируем/обновляем репо (для Claude Code)
echo "[2/6] Синхронизация репо..."
scp -i $SSH_KEY -r WowBot.Core WowBot.Injector WowBot.sln CLAUDE.md SpellDatabase.md RotationDatabase.md $VPS:$REMOTE_REPO/

# 3. Копируем бота
echo "[3/6] Копирую бота..."
scp -i $SSH_KEY WowBot.TelegramBot/bot.py WowBot.TelegramBot/requirements.txt $VPS:$REMOTE_BOT/

# 4. Зависимости
echo "[4/6] Устанавливаю зависимости..."
ssh -i $SSH_KEY $VPS "pip3 install -r $REMOTE_BOT/requirements.txt -q"

# 5. Systemd сервис
echo "[5/6] Настраиваю сервис..."
ssh -i $SSH_KEY $VPS "cat > /etc/systemd/system/wowbot-telegram.service << 'EOF'
[Unit]
Description=WowBot Telegram Bot
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/wowbot-telegram
ExecStart=/usr/bin/python3 /opt/wowbot-telegram/bot.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF"

# 6. Nginx для раздачи патчей
echo "[6/6] Настраиваю nginx..."
ssh -i $SSH_KEY $VPS "cat > /etc/nginx/sites-available/wowbot << 'EOF'
server {
    listen 8099;
    server_name _;
    root /var/www/wowbot;
    autoindex off;

    location /patch.zip { }
    location /version.txt { }
    location /previous_patch.zip { }
}
EOF
ln -sf /etc/nginx/sites-available/wowbot /etc/nginx/sites-enabled/ 2>/dev/null
nginx -t && systemctl reload nginx"

# Запуск
ssh -i $SSH_KEY $VPS "systemctl daemon-reload && systemctl enable wowbot-telegram && systemctl restart wowbot-telegram"

echo ""
echo "=== Готово! ==="
echo "Бот: systemctl status wowbot-telegram"
echo "Патчи: http://45.131.187.128:8099/"
echo "Репо для Claude Code: $REMOTE_REPO"
echo ""
echo "ВАЖНО: Установи Claude Code CLI на VPS:"
echo "  ssh -i $SSH_KEY $VPS"
echo "  npm install -g @anthropic-ai/claude-code"
echo "  claude auth login"
