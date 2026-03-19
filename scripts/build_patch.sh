#!/bin/bash
# Сборка патча: собирает проект, пакует только изменённые файлы, загружает на VPS
# Запускать из корня проекта: bash scripts/build_patch.sh 1.5

VERSION=${1:-"dev"}
VPS="root@45.131.187.128"
SSH_KEY="~/.ssh/id_ed25519_vmmo"
REMOTE_DIR="/var/www/wowbot"
PUBLISH_DIR="publish"
PATCH_DIR="_patch_temp"

echo "=== WowBot Patch Builder v${VERSION} ==="

# 1. Убиваем бота если запущен
taskkill /f /im WowBot.Injector.exe 2>/dev/null

# 2. Собираем
echo "[1/5] Сборка..."
dotnet publish WowBot.Injector -c Release -r win-x86 --self-contained -o "$PUBLISH_DIR" -v quiet
if [ $? -ne 0 ]; then echo "ОШИБКА сборки!"; exit 1; fi

# 3. Собираем updater
echo "[2/5] Сборка updater..."
dotnet publish WowBot.Updater -c Release -r win-x86 -o "$PUBLISH_DIR" -v quiet 2>/dev/null

# 4. Пакуем только нужные файлы в patch.zip
echo "[3/5] Пакуем патч..."
rm -rf "$PATCH_DIR"
mkdir -p "$PATCH_DIR/Icons"

# Основные DLL
cp "$PUBLISH_DIR/WowBot.Core.dll" "$PATCH_DIR/"
cp "$PUBLISH_DIR/WowBot.Injector.dll" "$PATCH_DIR/"

# Иконки
cp "$PUBLISH_DIR/Icons/"*.jpg "$PATCH_DIR/Icons/" 2>/dev/null

# Версия
echo "$VERSION" > "$PATCH_DIR/version.txt"

# Пакуем
cd "$PATCH_DIR"
zip -r ../patch.zip . -q
cd ..
rm -rf "$PATCH_DIR"

PATCH_SIZE=$(wc -c < patch.zip)
echo "   patch.zip: $((PATCH_SIZE / 1024)) KB"

# 5. Загружаем на VPS
echo "[4/5] Загрузка на VPS..."
# Текущий патч → previous
ssh -i "$SSH_KEY" $VPS "cd $REMOTE_DIR && [ -f patch.zip ] && mv patch.zip previous_patch.zip"
# Загружаем новый
scp -i "$SSH_KEY" patch.zip $VPS:$REMOTE_DIR/
# Обновляем версию
ssh -i "$SSH_KEY" $VPS "echo '$VERSION' > $REMOTE_DIR/version.txt"

echo "[5/5] Уведомление через бота..."
ssh -i "$SSH_KEY" $VPS "cd /opt/wowbot-telegram && python3 -c \"
import json
from pathlib import Path
p = Path('/var/www/wowbot/bugs')
# Помечаем все баги как fixed
for f in p.glob('*.json'):
    d = json.loads(f.read_text())
    if d.get('status') == 'new':
        d['status'] = 'fixed'
        f.write_text(json.dumps(d, ensure_ascii=False, indent=2))
print('Bugs marked as fixed')
\"" 2>/dev/null

rm -f patch.zip

echo ""
echo "=== Готово! ==="
echo "Версия $VERSION загружена на VPS"
echo "Тестировщики могут запустить update.exe"
