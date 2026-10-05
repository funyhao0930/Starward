// 鸣潮游戏内公告板。数据由 Starward（KuroNoticeWindow）通过 WebView2 的消息送进来：
//   → {action: 'ready'}                       页面就绪，请求公告板
//   ← {type: 'init', tabs, text}              分页与公告
//   → {action: 'content', id}                 要一则公告的正文
//   ← {type: 'content', id, banner, html}     正文；取不到时 html 为 null
//   → {action: 'read', id} / {action: 'url', url} / {action: 'close'}
(() => {
    'use strict';

    const host = window.chrome && window.chrome.webview;
    const post = (message) => host && host.postMessage(message);

    const $ = (id) => document.getElementById(id);
    const board = $('board');

    // ---------- 图标 ----------
    // 坐标系 0..100 就是整块图标方块（分页牌上的也照这个比例），形状照游戏截图放大后的格线量。
    // 白色部分用 currentColor，镂空部分用底色 --icon-cut。

    // 四角星，四个方向的长度可以不同；k 越小星芒越细
    const STAR = (cx, cy, top, right, bottom, left, k = 0.16) =>
        `M${cx} ${cy - top} Q${cx + right * k} ${cy - top * k} ${cx + right} ${cy}`
        + ` Q${cx + right * k} ${cy + bottom * k} ${cx} ${cy + bottom}`
        + ` Q${cx - left * k} ${cy + bottom * k} ${cx - left} ${cy}`
        + ` Q${cx - left * k} ${cy - top * k} ${cx} ${cy - top} Z`;

    const ICONS = {
        // tag 1 公告：挂起来的告示板，里面一支喇叭和一颗星
        announce: `
            <polyline points="36,25.5 48.5,12 61,25.5" fill="none" stroke="currentColor" stroke-width="4" stroke-linejoin="round"/>
            <circle cx="36" cy="25.5" r="2.6" fill="currentColor"/>
            <circle cx="61" cy="25.5" r="2.6" fill="currentColor"/>
            <rect x="15" y="24" width="69" height="40" rx="3.5" fill="currentColor"/>
            <path d="M26.5 43 H29.5 V47 H37 V51.5 H29.5 V60 H26.5 Z" fill="var(--icon-cut)"/>
            <path d="M37 45.5 L55 35 Q59.5 49 55 63 L37 52.5 Z" fill="var(--icon-cut)"/>
            <path d="${STAR(67, 49, 14, 6, 14, 6, 0.14)}" fill="var(--icon-cut)"/>
            <rect x="15" y="74" width="69" height="6.5" rx="3.2" fill="currentColor"/>`,
        // tag 10 资讯：卷起边的报纸，上面印着 NEWS
        info: `
            <rect x="17.5" y="22.5" width="67.5" height="56.5" rx="6" fill="currentColor"/>
            <rect x="22.8" y="27" width="3" height="46" rx="1.5" fill="var(--icon-cut)"/>
            <text x="57.5" y="47.5" text-anchor="middle" font-family="Arial Black, Arial, sans-serif" font-weight="900" font-size="13" fill="var(--icon-cut)">NEWS</text>
            <rect x="38" y="54" width="17" height="3" rx="1.5" fill="var(--icon-cut)"/>
            <rect x="38" y="61" width="17" height="3" rx="1.5" fill="var(--icon-cut)"/>
            <rect x="38" y="68" width="17" height="3" rx="1.5" fill="var(--icon-cut)"/>
            <path d="${STAR(70, 61, 10, 6, 10, 6, 0.14)}" fill="var(--icon-cut)"/>`,
        // tag 4 商城、活动说明：购物车，车斗上缘两道缺口，中间一颗星
        cart: `
            <path d="M13 27 H64 L60 58 Q59 62 55 62 H24 Q20 62 19.5 58 Z" fill="currentColor"/>
            <path d="M19.5 27 H27.5 L23.5 41 Z" fill="var(--icon-cut)"/>
            <path d="M54 27 H62 L56.5 41 Z" fill="var(--icon-cut)"/>
            <path d="${STAR(41, 45, 15, 9, 15, 9, 0.14)}" fill="var(--icon-cut)"/>
            <path d="M79 21 H91 L89.5 24.5 H84 L66.5 67 H61 Z" fill="currentColor"/>
            <rect x="18" y="63.5" width="48" height="6" rx="3" fill="currentColor"/>
            <path d="M22 69 A5 5 0 0 0 32 69 Z" fill="currentColor"/>
            <path d="M53 69 A5 5 0 0 0 63 69 Z" fill="currentColor"/>`,
        // tag 3 签到、收集、先行体验：一大两小的星芒，右下几道斜笔
        sparkles: `
            <path d="${STAR(40.5, 43, 38, 18, 14, 16, 0.1)}" fill="currentColor"/>
            <path d="${STAR(56, 22, 9, 6, 9, 6, 0.14)}" fill="currentColor"/>
            <path d="${STAR(26, 33, 8, 6, 8, 6, 0.14)}" fill="currentColor"/>
            <path d="M17 55 L38 54 V60.5 L20 64 Z" fill="currentColor"/>
            <path d="M61 25 L76 31 L66 44 L63 57 L58 55 Z" fill="currentColor"/>
            <path d="M64 46 L70 40 L81 85 L70 83 Z" fill="currentColor"/>
            <path d="M45 61 L53 58 L59 79 L51 81 Z" fill="currentColor"/>
            <path d="M41 63 L46 62 L44 76 L41 77 Z" fill="currentColor"/>`,
        // tag 5 挑战：码表，表面一颗星
        stopwatch: `
            <circle cx="48" cy="54" r="29.5" fill="none" stroke="currentColor" stroke-width="3.5"/>
            <circle cx="48" cy="54" r="23.5" fill="currentColor"/>
            <rect x="44" y="13" width="8" height="10" fill="currentColor"/>
            <rect x="35" y="10" width="26" height="4" rx="1" fill="currentColor"/>
            <rect x="68" y="24" width="7" height="4" rx="1" transform="rotate(45 71.5 26)" fill="currentColor"/>
            <circle cx="21" cy="38" r="4.5" fill="currentColor"/>
            <path d="${STAR(48, 54, 17, 10, 17, 10, 0.12)}" fill="var(--icon-cut)"/>`,
        // tag 6 新玩法：叠在一起的两张折纸三角，实心那张里有一颗星
        origami: `
            <path d="M22 13 L86 37 L35 79 Z" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linejoin="miter"/>
            <path d="M13 24 H82 L47.5 83 Z" fill="currentColor"/>
            <path d="${STAR(47.5, 47, 21, 14, 22, 14, 0.14)}" fill="var(--icon-cut)"/>`,
        // 关闭：四片刃，中间留一道十字缝
        close: `<path d="M6 6 L47 31 L47 47 L31 47 Z M94 6 L69 47 L53 47 L53 31 Z M6 94 L31 53 L47 53 L47 69 Z M94 94 L53 69 L53 53 L69 53 Z" fill="currentColor"/>`,
        // 返回
        back: `<path d="M82 10 L14 50 L82 90 L72 67 L44 50 L72 33 Z" fill="currentColor"/>`,
    };

    // 鸣潮公告的 tag 对应的图标，没见过的 tag 用告示板
    const TAG_ICONS = { 1: 'announce', 3: 'sparkles', 4: 'cart', 5: 'stopwatch', 6: 'origami', 10: 'info' };

    const svg = (name) => `<svg viewBox="0 0 100 100" aria-hidden="true">${ICONS[name] || ICONS.announce}</svg>`;

    $('close').innerHTML = svg('close');
    $('back').innerHTML = svg('back');

    // 标题列右边的同心弧线
    document.querySelector('.header-deco').innerHTML = [0, 1, 2, 3, 4].map(i => {
        const x = 40 + i * 13, y = 98 - i * 13;
        return `<path d="M${x} 0 C${x} ${y * 0.62} ${x + 46} ${y} ${x + 132} ${y} H440" fill="none" stroke="#000" stroke-opacity="0.07" stroke-width="1.4"/>`;
    }).join('');

    // 卡片墙左边那条竖线，上中下三个节点
    document.querySelector('.grid-deco').innerHTML =
        `<line x1="7" y1="0" x2="7" y2="670" stroke="#D3D3D3" stroke-width="1.2"/>` +
        [12, 335, 658].map(y => [-8, -4, 0, 4, 8].map(d =>
            `<line x1="${7 - (6 - Math.abs(d) * 0.6)}" y1="${y + d}" x2="${7 + (6 - Math.abs(d) * 0.6)}" y2="${y + d}" stroke="#CFCFCF" stroke-width="1.4"/>`).join('')).join('');

    // ---------- 状态 ----------

    const state = {
        tabs: [],
        tab: 0,
        mode: 'grid',
        selected: null,
        text: {},
    };

    // ---------- 自绘卷轴：游戏里是一条细轨道配黑色滑块 ----------

    class ScrollBar {
        constructor(scroller, bar) {
            this.scroller = scroller;
            this.bar = bar;
            this.thumb = bar.querySelector('.thumb');
            scroller.addEventListener('scroll', () => this.update(), { passive: true });
            new ResizeObserver(() => this.update()).observe(scroller);
            this.thumb.addEventListener('pointerdown', (e) => this.drag(e));
            bar.addEventListener('pointerdown', (e) => {
                if (e.target !== this.thumb) {
                    const rect = this.bar.getBoundingClientRect();
                    const ratio = (e.clientY - rect.top) / rect.height;
                    this.scroller.scrollTop = ratio * this.scroller.scrollHeight - this.scroller.clientHeight / 2;
                }
            });
        }

        update() {
            const { scrollTop, scrollHeight, clientHeight } = this.scroller;
            const overflow = scrollHeight - clientHeight;
            this.bar.classList.toggle('hidden', overflow <= 1);
            if (overflow <= 1) return;
            const track = this.bar.clientHeight;
            const size = Math.max(track * clientHeight / scrollHeight, track * 0.08);
            this.thumb.style.height = `${size}px`;
            this.thumb.style.top = `${(track - size) * scrollTop / overflow}px`;
        }

        drag(e) {
            e.preventDefault();
            const startY = e.clientY;
            const startTop = this.scroller.scrollTop;
            const { scrollHeight, clientHeight } = this.scroller;
            const ratio = (scrollHeight - clientHeight) / (this.bar.clientHeight - this.thumb.clientHeight);
            const move = (ev) => { this.scroller.scrollTop = startTop + (ev.clientY - startY) * ratio; };
            const up = () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
            window.addEventListener('pointermove', move);
            window.addEventListener('pointerup', up);
        }
    }

    const gridBar = new ScrollBar($('grid-scroll'), $('grid-bar'));
    const contentBar = new ScrollBar($('content-scroll'), $('content-bar'));

    // ---------- 公告正文：远端来的 HTML，插入前清掉会执行的东西 ----------

    const DROP_TAGS = new Set(['SCRIPT', 'STYLE', 'LINK', 'META', 'BASE', 'IFRAME', 'FRAME', 'OBJECT', 'EMBED', 'FORM', 'INPUT', 'BUTTON', 'SELECT', 'TEXTAREA', 'TEMPLATE', 'SVG', 'MATH']);

    function sanitize(html) {
        const doc = new DOMParser().parseFromString(`<body>${html || ''}</body>`, 'text/html');
        const walk = (node) => {
            for (const child of [...node.children]) {
                if (DROP_TAGS.has(child.tagName)) {
                    child.remove();
                    continue;
                }
                for (const attr of [...child.attributes]) {
                    const name = attr.name.toLowerCase();
                    const value = attr.value.trim().toLowerCase();
                    if (name.startsWith('on') || name === 'srcdoc' || name === 'formaction'
                        || ((name === 'href' || name === 'src' || name === 'xlink:href') && !/^(https?:|data:image\/)/.test(value))) {
                        child.removeAttribute(attr.name);
                    }
                }
                walk(child);
            }
        };
        walk(doc.body);
        return doc.body.innerHTML;
    }

    // ---------- 渲染 ----------

    function currentTab() {
        return state.tabs[state.tab] || { items: [] };
    }

    function dot(show) {
        return show ? '<span class="dot"></span>' : '';
    }

    // 文字与属性值都会用到，引号也要转
    function escapeText(text) {
        return String(text ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    function renderTabs() {
        $('tabs').innerHTML = state.tabs.map((tab, i) => `
            <button class="tab${i === state.tab ? ' active' : ''}" data-index="${i}">
                ${svg(tab.icon)}<span class="label">${escapeText(tab.label)}</span>${dot(tab.items.some(x => x.unread))}
            </button>`).join('');
    }

    function renderGrid() {
        const items = currentTab().items;
        $('grid').innerHTML = items.map((item, i) => `
            <div class="card${i === 0 ? ' focus' : ''}" data-id="${escapeText(item.id)}">
                ${item.card ? `<img src="${escapeText(item.card)}" alt="" draggable="false">` : `<div class="fallback">${escapeText(item.title)}</div>`}
                ${dot(item.unread)}
            </div>`).join('');
        $('grid-scroll').scrollTop = 0;
        requestAnimationFrame(() => gridBar.update());
    }

    function renderList() {
        const items = currentTab().items;
        $('list').innerHTML = items.map(item => `
            <div class="item${item.id === state.selected ? ' selected' : ''}" data-id="${escapeText(item.id)}">
                <div class="tile">${svg(TAG_ICONS[item.tag] || 'announce')}</div>
                <div class="title">${escapeText(item.title)}</div>
                ${dot(item.unread)}
            </div>`).join('');
    }

    function render() {
        board.classList.toggle('detail', state.mode === 'detail');
        renderTabs();
        if (state.mode === 'grid') {
            renderGrid();
        } else {
            renderList();
        }
        const empty = currentTab().items.length === 0;
        $('message').textContent = empty ? (state.text.empty || '') : '';
        $('message').classList.toggle('show', empty);
    }

    function findItem(id) {
        return currentTab().items.find(x => x.id === id);
    }

    function select(id) {
        const item = findItem(id);
        if (!item) return;
        const fromGrid = state.mode !== 'detail';
        state.mode = 'detail';
        state.selected = id;
        if (item.unread) {
            item.unread = false;
            post({ action: 'read', id });
        }
        render();
        // 从卡片墙点进来时把这张卡放到清单中间；在清单里点的只要看得到就好
        const node = $('list').querySelector('.item.selected');
        if (node) node.scrollIntoView({ block: fromGrid ? 'center' : 'nearest' });
        showContentLoading();
        post({ action: 'content', id });
    }

    function showContentLoading() {
        $('content').innerHTML = '<div class="loading"></div>';
        $('content-scroll').scrollTop = 0;
        contentBar.update();
    }

    function showContent(message) {
        if (message.id !== state.selected || state.mode !== 'detail') return;
        const item = findItem(message.id);
        if (message.html == null) {
            $('content').innerHTML = `<div class="error">${escapeText(state.text.error || '')}</div>`;
            contentBar.update();
            return;
        }
        const banner = message.banner || (item && item.card);
        $('content').innerHTML =
            (banner ? `<img class="banner" src="${escapeText(banner)}" alt="" draggable="false">` : '') +
            `<div class="text">${sanitize(message.html)}</div>`;
        $('content-scroll').scrollTop = 0;
        // 图片陆续载入会撑高内容，卷轴跟着更新
        $('content').querySelectorAll('img').forEach(img => img.addEventListener('load', () => contentBar.update(), { once: true }));
        requestAnimationFrame(() => contentBar.update());
    }

    function switchTab(index) {
        if (index === state.tab || !state.tabs[index]) return;
        state.tab = index;
        const items = currentTab().items;
        if (state.mode === 'detail' && items.length > 0) {
            select(items[0].id);
        } else {
            state.mode = 'grid';
            state.selected = null;
            render();
        }
    }

    function back() {
        state.mode = 'grid';
        state.selected = null;
        render();
    }

    // ---------- 交互 ----------

    $('tabs').addEventListener('click', (e) => {
        const tab = e.target.closest('.tab');
        if (tab) switchTab(Number(tab.dataset.index));
    });

    $('grid').addEventListener('click', (e) => {
        const card = e.target.closest('.card');
        if (card) select(card.dataset.id);
    });

    // 游戏里金框跟着鼠标走
    $('grid').addEventListener('mouseover', (e) => {
        const card = e.target.closest('.card');
        if (!card || card.classList.contains('focus')) return;
        $('grid').querySelectorAll('.card.focus').forEach(x => x.classList.remove('focus'));
        card.classList.add('focus');
    });

    $('list').addEventListener('click', (e) => {
        const item = e.target.closest('.item');
        if (item && item.dataset.id !== state.selected) select(item.dataset.id);
    });

    $('content').addEventListener('click', (e) => {
        const link = e.target.closest('a[href]');
        if (!link) return;
        e.preventDefault();
        post({ action: 'url', url: link.href });
    });

    $('back').addEventListener('click', back);
    $('close').addEventListener('click', () => post({ action: 'close' }));

    // 点公告板外面的暗处关闭
    document.body.addEventListener('pointerdown', (e) => {
        if (e.target === document.body) post({ action: 'close' });
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            post({ action: 'close' });
        } else if (e.key === 'Backspace' && state.mode === 'detail') {
            back();
        }
    });

    document.addEventListener('contextmenu', (e) => e.preventDefault());

    // ---------- 与 Starward 通讯 ----------

    if (host) {
        host.addEventListener('message', (e) => {
            const message = e.data || {};
            if (message.type === 'init') {
                state.tabs = message.tabs || [];
                state.text = message.text || {};
                state.tab = 0;
                state.mode = 'grid';
                render();
                document.body.classList.add('ready');
            } else if (message.type === 'content') {
                showContent(message);
            }
        });
    }

    post({ action: 'ready' });
})();
