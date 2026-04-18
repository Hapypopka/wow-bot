"""
Генерирует docs/gm_commands.html — интерактивный русскоязычный справочник
по всем GM командам SPP WotLK.

Данные из:
- MySQL wotlkmangos.command (имена + уровни доступа)
- tools/gm_commands_ru.json (русские переводы групп и команд)
"""
import subprocess
import json
import os

MYSQL = 'D:/SPP/SPP_Classics_V2/SPP_Server/Server/Database/bin/mysql.exe'
MYSQL_ARGS = [MYSQL, '-h127.0.0.1', '-P3310', '-uroot', '-p123456', '-N', '-s',
              '--default-character-set=utf8', '-e',
              'SELECT name, security, COALESCE(help, "") FROM wotlkmangos.command ORDER BY name;']


def fetch_commands():
    r = subprocess.run(MYSQL_ARGS, capture_output=True)
    raw = r.stdout.decode('utf-8', errors='replace')
    rows = []
    for line in raw.strip().split('\n'):
        parts = line.split('\t', 2)
        if len(parts) == 3:
            rows.append({'name': parts[0], 'sec': int(parts[1]), 'help_en': parts[2]})
    return rows


def load_translations():
    path = 'tools/gm_commands_ru.json'
    if not os.path.exists(path):
        return {'groups': {}, 'commands': {}}
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)


SEC_LABEL = {0: 'Игрок', 1: 'Модер', 2: 'ГМ', 3: 'Админ', 4: 'Рут'}
SEC_COLOR = {0: '#64748b', 1: '#3b82f6', 2: '#10b981', 3: '#f59e0b', 4: '#ef4444'}


def generate_html(commands, translations):
    groups_tr = translations.get('groups', {})
    cmds_tr = translations.get('commands', {})

    # Добавляем русские тексты и группируем
    groups = {}
    translated_count = 0
    for cmd in commands:
        top = cmd['name'].split()[0]
        ru = cmds_tr.get(cmd['name'])
        if ru:
            translated_count += 1
        cmd['help_ru'] = ru or ''
        cmd['group'] = top
        cmd['group_ru'] = groups_tr.get(top, top.capitalize())
        groups.setdefault(top, []).append(cmd)

    print(f'Переведено команд: {translated_count}/{len(commands)}')

    data_json = json.dumps([
        {
            'n': c['name'],
            's': c['sec'],
            'ru': c['help_ru'],
            'en': c['help_en'],
            'g': c['group'],
            'gru': c['group_ru']
        }
        for c in commands
    ], ensure_ascii=False)

    sec_json = json.dumps(SEC_LABEL, ensure_ascii=False)
    sec_color_json = json.dumps(SEC_COLOR, ensure_ascii=False)

    html_tpl = r"""<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<title>SPP WotLK — GM команды</title>
<style>
:root {
    --bg: #0f172a;
    --panel: #1e293b;
    --border: #334155;
    --text: #e2e8f0;
    --muted: #94a3b8;
    --accent: #60a5fa;
    --hover: #2d3a54;
}
* { box-sizing: border-box; }
body {
    margin: 0;
    font-family: -apple-system, "Segoe UI", Roboto, sans-serif;
    background: var(--bg);
    color: var(--text);
    line-height: 1.5;
}
header {
    background: var(--panel);
    padding: 20px 30px;
    border-bottom: 2px solid var(--border);
    position: sticky;
    top: 0;
    z-index: 10;
    box-shadow: 0 2px 10px rgba(0,0,0,0.3);
}
h1 { margin: 0 0 12px; font-size: 22px; }
.subtitle { color: var(--muted); font-size: 13px; }
.subtitle kbd {
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 3px;
    padding: 1px 6px;
    font-family: "SF Mono", Consolas, monospace;
    font-size: 12px;
}
.controls {
    display: flex;
    gap: 12px;
    margin-top: 14px;
    flex-wrap: wrap;
    align-items: center;
}
#search {
    flex: 1;
    min-width: 250px;
    padding: 10px 14px;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    color: var(--text);
    font-size: 14px;
    outline: none;
}
#search:focus { border-color: var(--accent); }
.filter-group { display: flex; gap: 6px; flex-wrap: wrap; }
.filter-btn {
    padding: 6px 12px;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    color: var(--text);
    font-size: 13px;
    cursor: pointer;
    transition: all .15s;
}
.filter-btn:hover { border-color: var(--accent); }
.filter-btn.active {
    background: var(--accent);
    color: #0f172a;
    border-color: var(--accent);
    font-weight: 600;
}
.stats { color: var(--muted); font-size: 13px; margin-left: auto; }
main { padding: 20px 30px; }
.group {
    margin-bottom: 10px;
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: 8px;
    overflow: hidden;
}
.group-header {
    padding: 14px 18px;
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 12px;
    user-select: none;
    transition: background .1s;
}
.group-header:hover { background: var(--hover); }
.group-arrow { color: var(--muted); transition: transform .2s; display: inline-block; font-size: 10px; }
.group.open .group-arrow { transform: rotate(90deg); }
.group-ru { font-size: 16px; font-weight: 700; color: var(--text); }
.group-en {
    font-family: "SF Mono", Consolas, monospace;
    font-size: 13px;
    color: var(--accent);
}
.group-count { color: var(--muted); font-size: 12px; margin-left: auto; }
.group-body { display: none; padding: 0 14px 14px; }
.group.open .group-body { display: block; }
.cmd {
    padding: 12px 14px;
    margin: 6px 0;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    transition: border-color .1s;
}
.cmd:hover { border-color: var(--accent); }
.cmd-header { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; flex-wrap: wrap; }
.cmd-name {
    font-family: "SF Mono", Consolas, monospace;
    font-size: 14px;
    color: #f8fafc;
    font-weight: 600;
    background: #0f172a;
    padding: 3px 10px;
    border-radius: 4px;
    border: 1px solid var(--border);
}
.sec-badge {
    padding: 3px 9px;
    border-radius: 4px;
    font-size: 11px;
    font-weight: 700;
    color: white;
    text-transform: uppercase;
    letter-spacing: .5px;
}
.cmd-help-ru {
    color: var(--text);
    font-size: 13px;
    margin: 4px 0;
    line-height: 1.55;
}
.cmd-help-en {
    color: var(--muted);
    font-size: 11.5px;
    margin-top: 6px;
    padding-top: 6px;
    border-top: 1px dashed var(--border);
    white-space: pre-wrap;
    font-family: "SF Mono", Consolas, monospace;
    max-height: 80px;
    overflow: hidden;
    opacity: 0.7;
}
.cmd-help-en.expanded { max-height: 1000px; }
.show-en {
    background: transparent;
    border: none;
    color: var(--muted);
    font-size: 11px;
    cursor: pointer;
    padding: 2px 0;
    margin-top: 4px;
}
.show-en:hover { color: var(--accent); }
.copy-btn {
    margin-left: auto;
    padding: 4px 10px;
    background: transparent;
    border: 1px solid var(--border);
    border-radius: 4px;
    color: var(--muted);
    font-size: 11px;
    cursor: pointer;
    transition: all .1s;
}
.copy-btn:hover { color: var(--accent); border-color: var(--accent); }
.copy-btn.copied { color: #10b981; border-color: #10b981; }
.untranslated {
    display: inline-block;
    background: #7c2d12;
    color: #fed7aa;
    padding: 1px 6px;
    border-radius: 3px;
    font-size: 10px;
    font-weight: 600;
    margin-left: 6px;
}
.hidden { display: none !important; }
.no-match { text-align: center; padding: 40px; color: var(--muted); }
</style>
</head>
<body>
<header>
    <h1>📖 SPP WotLK — справочник GM команд</h1>
    <div class="subtitle">
        Всего команд: <span id="total">__TOTAL__</span> ·
        Нажми <kbd>/</kbd> для поиска, <kbd>Esc</kbd> для сброса.
        Клик по команде копирует её в буфер.
    </div>
    <div class="controls">
        <input id="search" type="text" placeholder="🔍 Поиск по имени или описанию..." autocomplete="off">
        <div class="filter-group" id="filters">
            <button class="filter-btn active" data-sec="all">Все</button>
            <button class="filter-btn" data-sec="0">0 Игрок</button>
            <button class="filter-btn" data-sec="1">1 Модер</button>
            <button class="filter-btn" data-sec="2">2 ГМ</button>
            <button class="filter-btn" data-sec="3">3 Админ</button>
            <button class="filter-btn" data-sec="4">4 Рут</button>
        </div>
        <div class="stats">Показано: <span id="visible-count">0</span></div>
    </div>
</header>
<main id="main"></main>

<script>
const DATA = __DATA__;
const SEC_LABEL = __SEC__;
const SEC_COLOR = __SEC_COLOR__;

const groups = {};
DATA.forEach(c => {
    (groups[c.g] = groups[c.g] || { ru: c.gru, items: [] }).items.push(c);
});

const main = document.getElementById('main');
const searchInput = document.getElementById('search');
const filters = document.getElementById('filters');
const visibleCount = document.getElementById('visible-count');

function copyToClipboard(text, btn) {
    navigator.clipboard.writeText(text).then(() => {
        const orig = btn.textContent;
        btn.textContent = '✓ скопировано';
        btn.classList.add('copied');
        setTimeout(() => {
            btn.textContent = orig;
            btn.classList.remove('copied');
        }, 1500);
    });
}

function render() {
    main.innerHTML = '';
    Object.keys(groups).sort().forEach(top => {
        const g = groups[top];
        const groupEl = document.createElement('div');
        groupEl.className = 'group';

        const hdr = document.createElement('div');
        hdr.className = 'group-header';
        hdr.innerHTML =
            '<span class="group-arrow">▶</span>' +
            '<span class="group-ru">' + g.ru + '</span>' +
            '<span class="group-en">.' + top + '</span>' +
            '<span class="group-count" data-count>' + g.items.length + '</span>';
        hdr.addEventListener('click', () => groupEl.classList.toggle('open'));
        groupEl.appendChild(hdr);

        const body = document.createElement('div');
        body.className = 'group-body';
        g.items.forEach(c => {
            const cmd = document.createElement('div');
            cmd.className = 'cmd';
            cmd.dataset.name = c.n;
            cmd.dataset.sec = c.s;
            cmd.dataset.search = (c.n + ' ' + (c.ru || '') + ' ' + c.en).toLowerCase();

            const header = document.createElement('div');
            header.className = 'cmd-header';

            const nameEl = document.createElement('span');
            nameEl.className = 'cmd-name';
            nameEl.textContent = '.' + c.n;
            header.appendChild(nameEl);

            const secEl = document.createElement('span');
            secEl.className = 'sec-badge';
            secEl.textContent = c.s + ' ' + SEC_LABEL[c.s];
            secEl.style.background = SEC_COLOR[c.s];
            header.appendChild(secEl);

            if (!c.ru) {
                const untr = document.createElement('span');
                untr.className = 'untranslated';
                untr.textContent = 'EN';
                untr.title = 'Нет русского перевода — смотри оригинал ниже';
                header.appendChild(untr);
            }

            const copyBtn = document.createElement('button');
            copyBtn.className = 'copy-btn';
            copyBtn.textContent = 'скопировать';
            copyBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                copyToClipboard('.' + c.n, copyBtn);
            });
            header.appendChild(copyBtn);
            cmd.appendChild(header);

            if (c.ru) {
                const helpRu = document.createElement('div');
                helpRu.className = 'cmd-help-ru';
                helpRu.textContent = c.ru;
                cmd.appendChild(helpRu);
            }

            if (c.en && c.en.trim()) {
                const toggleBtn = document.createElement('button');
                toggleBtn.className = 'show-en';
                toggleBtn.textContent = c.ru ? '▼ оригинал (EN)' : '▼ описание (EN)';
                const helpEn = document.createElement('div');
                helpEn.className = 'cmd-help-en';
                helpEn.textContent = c.en;
                helpEn.style.display = 'none';
                toggleBtn.addEventListener('click', () => {
                    const open = helpEn.style.display === 'none';
                    helpEn.style.display = open ? 'block' : 'none';
                    toggleBtn.textContent = (open ? '▲' : '▼') + ' ' + (c.ru ? 'оригинал (EN)' : 'описание (EN)');
                });
                // Если нет RU — сразу показываем EN
                if (!c.ru) {
                    helpEn.style.display = 'block';
                    toggleBtn.textContent = '▲ описание (EN)';
                }
                cmd.appendChild(toggleBtn);
                cmd.appendChild(helpEn);
            }

            body.appendChild(cmd);
        });
        groupEl.appendChild(body);
        main.appendChild(groupEl);
    });
}

function applyFilter() {
    const q = searchInput.value.trim().toLowerCase();
    const activeBtn = filters.querySelector('.filter-btn.active');
    const secFilter = activeBtn.dataset.sec;
    let visible = 0;
    document.querySelectorAll('.group').forEach(g => {
        let groupVisible = 0;
        g.querySelectorAll('.cmd').forEach(cmd => {
            const matchQ = !q || cmd.dataset.search.includes(q);
            const matchSec = secFilter === 'all' || cmd.dataset.sec === secFilter;
            const show = matchQ && matchSec;
            cmd.classList.toggle('hidden', !show);
            if (show) { groupVisible++; visible++; }
        });
        g.classList.toggle('hidden', groupVisible === 0);
        const count = g.querySelector('[data-count]');
        if (count) count.textContent = groupVisible;
        if (q && groupVisible > 0) g.classList.add('open');
        else if (!q) g.classList.remove('open');
    });
    visibleCount.textContent = visible;
}

searchInput.addEventListener('input', applyFilter);
filters.addEventListener('click', e => {
    if (!e.target.classList.contains('filter-btn')) return;
    filters.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
    e.target.classList.add('active');
    applyFilter();
});

document.addEventListener('keydown', e => {
    if (e.key === '/' && document.activeElement !== searchInput) {
        e.preventDefault();
        searchInput.focus();
    } else if (e.key === 'Escape') {
        searchInput.value = '';
        applyFilter();
        searchInput.blur();
    }
});

render();
applyFilter();
</script>
</body>
</html>
"""

    out = (html_tpl
           .replace('__TOTAL__', str(len(commands)))
           .replace('__DATA__', data_json)
           .replace('__SEC__', sec_json)
           .replace('__SEC_COLOR__', sec_color_json))

    os.makedirs('docs', exist_ok=True)
    with open('docs/gm_commands.html', 'w', encoding='utf-8') as f:
        f.write(out)
    size_kb = os.path.getsize('docs/gm_commands.html') / 1024
    print(f'Written docs/gm_commands.html ({size_kb:.1f} KB)')


if __name__ == '__main__':
    os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    cmds = fetch_commands()
    tr = load_translations()
    generate_html(cmds, tr)
