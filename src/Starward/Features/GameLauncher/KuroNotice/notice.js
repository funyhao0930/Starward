// 鸣潮游戏内公告板。数据由 Starward（KuroNoticeWindow）通过 WebView2 的消息送进来：
//   → {action: 'ready'}                       页面就绪，请求公告板
//   ← {type: 'init', lang, tabs, calendar, text}  分页与公告；calendar 是日历（可能为 null）
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

    // 五角星，R 外径、r 内径
    const STAR5 = (cx, cy, R, r) => Array.from({ length: 10 }, (_, i) => {
        const radius = i % 2 === 0 ? R : r;
        const angle = -Math.PI / 2 + i * Math.PI / 5;
        return `${i === 0 ? 'M' : 'L'}${(cx + radius * Math.cos(angle)).toFixed(2)} ${(cy + radius * Math.sin(angle)).toFixed(2)}`;
    }).join(' ') + ' Z';

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
        // 推荐分页：书签缎带，中间一颗五角星，两旁各一颗小星芒
        recommend: `
            <path d="M14.5 18 H74.5 V80.5 L44.5 66.5 L14.5 80.5 Z" fill="currentColor"/>
            <rect x="43.7" y="18" width="1.6" height="11" fill="var(--icon-cut)"/>
            <rect x="17.6" y="18" width="1.3" height="26" fill="var(--icon-cut)"/>
            <rect x="70.1" y="18" width="1.3" height="26" fill="var(--icon-cut)"/>
            <path d="${STAR5(44.5, 46.5, 16, 6.8)}" fill="var(--icon-cut)"/>
            <path d="${STAR(26.5, 53.5, 6, 2.4, 6, 2.4, 0.18)}" fill="var(--icon-cut)"/>
            <path d="${STAR(62.5, 53.5, 6, 2.4, 6, 2.4, 0.18)}" fill="var(--icon-cut)"/>`,
        // 日历分页：挂两个环的月历，后面再叠一张，纸上三颗星芒
        calendar: `
            <rect x="20.5" y="31" width="58.5" height="49.5" rx="3" fill="currentColor" opacity="0.55"/>
            <rect x="13.5" y="25" width="62" height="51.5" rx="3" fill="currentColor"/>
            <rect x="13.5" y="38" width="62" height="1.4" fill="var(--icon-cut)"/>
            <rect x="24.2" y="19.5" width="4.6" height="11.5" rx="2.3" fill="var(--icon-cut)"/>
            <rect x="25.3" y="20.6" width="2.4" height="9.3" rx="1.2" fill="currentColor"/>
            <rect x="58.3" y="19.5" width="4.6" height="11.5" rx="2.3" fill="var(--icon-cut)"/>
            <rect x="59.4" y="20.6" width="2.4" height="9.3" rx="1.2" fill="currentColor"/>
            <path d="${STAR(43, 57, 9.5, 5.5, 9.5, 5.5, 0.16)}" fill="var(--icon-cut)"/>
            <path d="${STAR(28, 57, 4.6, 2.6, 4.6, 2.6, 0.18)}" fill="var(--icon-cut)"/>
            <path d="${STAR(59.5, 57, 4.6, 2.6, 4.6, 2.6, 0.18)}" fill="var(--icon-cut)"/>`,
        // 关闭：四片刃，中间留一道十字缝
        close: `<path d="M6 6 L47 31 L47 47 L31 47 Z M94 6 L69 47 L53 47 L53 31 Z M6 94 L31 53 L47 53 L47 69 Z M94 94 L53 69 L53 53 L69 53 Z" fill="currentColor"/>`,
        // 返回
        back: `<path d="M82 10 L14 50 L82 90 L72 67 L44 50 L72 33 Z" fill="currentColor"/>`,
    };

    // 鸣潮公告的 tag 对应的图标，没见过的 tag 用告示板。
    // 1 公告、2 维护、3 活动、4 商城、5 限时活动、6 常驻玩法、7 唤取、8 新剧情、9 新玩法、10 资讯、11 周边、12 推荐
    const TAG_ICONS = { 1: 'announce', 2: 'announce', 3: 'sparkles', 4: 'cart', 5: 'stopwatch', 6: 'origami', 7: 'recommend', 9: 'origami', 10: 'info', 11: 'cart', 12: 'recommend' };

    // 分页名与提示，照游戏公告网页（aki/announcement/locales/{lang}.json）的写法
    const LABELS = {
        'zh-Hant': { recommend: '推薦', announce: '公告', info: '資訊', calendar: '日曆', tips: '提示', notPublic: '公告暫未公開\n敬請期待', unavailable: '公告已下架' },
        'zh-Hans': { recommend: '推荐', announce: '公告', info: '资讯', calendar: '日历', tips: '提示', notPublic: '公告暂未公开\n敬请期待', unavailable: '公告已下架' },
        en: { recommend: 'Highlights', announce: 'Info', info: 'News', calendar: 'Calendar', tips: 'Note', notPublic: 'Announcement not yet released. Stay tuned.', unavailable: 'This announcement is no longer available.' },
        ja: { recommend: '注目', announce: 'お知らせ', info: 'ニュース', calendar: 'カレンダー', tips: '注意', notPublic: '未公開の内容です。\nお楽しみに。', unavailable: 'お知らせの掲載は終了しました' },
        ko: { recommend: '추천', announce: '공지', info: '정보', calendar: '일정', tips: '알림', notPublic: '공지가 아직 공개되지 않았습니다.\n많은 기대 부탁드립니다.', unavailable: '게시가 종료된 공지사항입니다' },
        fr: { recommend: 'À la une', announce: 'Informations', info: 'Actualités', calendar: 'Calendrier', tips: 'Info', notPublic: "L'annonce n'a pas encore été publiée.\nRestez à l'affût !", unavailable: "Cette annonce n'est plus disponible." },
        de: { recommend: 'Highlights', announce: 'Ankündigungen', info: 'Neuigkeiten', calendar: 'Kalender', tips: 'Hinweis', notPublic: 'Noch nicht veröffentlicht.\nBleib dran!', unavailable: 'Diese Ankündigung ist nicht mehr verfügbar.' },
        es: { recommend: 'Destacados', announce: 'Anuncios', info: 'Noticias', calendar: 'Calendario', tips: 'Aviso', notPublic: 'El anuncio aún no se ha publicado.\nMantente atento.', unavailable: 'El anuncio ya no está disponible.' },
        th: { recommend: 'ไฮไลต์', announce: 'ประกาศ', info: 'ข่าว', calendar: 'ปฏิทิน', tips: 'แจ้งเตือน', notPublic: 'ยังไม่มีการประกาศอย่างเป็นทางการ\nโปรดติดตามต่อไป', unavailable: 'ประกาศถูกนำออกแล้ว' },
        'pt-BR': { recommend: 'Destaques', announce: 'Anúncios', info: 'Notícias', calendar: 'Calendário', tips: 'Aviso', notPublic: 'O anúncio ainda não foi publicado.\nContinue acompanhando as novidades.', unavailable: 'Comunicado Removido' },
    };

    const label = (key) => (LABELS[state.lang] || LABELS.en)[key];

    const svg = (name) => `<svg viewBox="0 0 100 100" aria-hidden="true">${ICONS[name] || ICONS.announce}</svg>`;

    $('close').innerHTML = svg('close');
    $('back').innerHTML = svg('back');
    $('popup-close').innerHTML = svg('close');

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

    // mode：grid 卡片墙、detail 左清单右正文、calendar 日历（日历是最后一页，tab 等于 tabs.length）
    const state = {
        tabs: [],
        tab: 0,
        mode: 'grid',
        selected: null,
        calendar: null,
        lang: 'en',
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
    const calendarBar = new ScrollBar($('calendar-scroll'), $('calendar-bar'));

    // 日历的卷轴跟游戏一样，只在卷动时浮出来
    let calendarScrollTimer = 0;
    $('calendar-scroll').addEventListener('scroll', () => {
        $('calendar-bar').classList.add('scrolling');
        clearTimeout(calendarScrollTimer);
        calendarScrollTimer = setTimeout(() => $('calendar-bar').classList.remove('scrolling'), 600);
    }, { passive: true });

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
                    // 3.8 起链接写成 browser:https://… / webView:https://…，前缀是游戏里的开启方式，这里一律用浏览器开
                    if (name === 'href' && /^\s*(browser|webview):/i.test(attr.value)) {
                        attr.value = attr.value.trim().replace(/^(browser|webview):/i, '');
                    }
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
        const tabs = state.tabs.map((tab, i) => ({
            index: i, icon: tab.icon, label: label(tab.key) || tab.label, unread: tab.items.some(x => x.unread),
        }));
        if (state.calendar) {
            tabs.push({ index: state.tabs.length, icon: 'calendar', label: label('calendar'), unread: state.calendar.unread });
        }
        $('tabs').innerHTML = tabs.map(tab => `
            <button class="tab${tab.index === state.tab ? ' active' : ''}" data-index="${tab.index}">
                ${svg(tab.icon)}<span class="label">${escapeText(tab.label)}</span>${dot(tab.unread)}
            </button>`).join('');
    }

    function isCalendarTab(index) {
        return !!state.calendar && index === state.tabs.length;
    }

    // 日历是一张长图，图上的时间轴与卡片都是画好的；这里只叠指针与热区。坐标都是对整张图的百分比
    function renderCalendar() {
        const calendar = state.calendar;
        if (!calendar) return;
        if ($('calendar-image').getAttribute('src') !== calendar.image) {
            $('calendar-image').src = calendar.image;
            $('calendar-image').addEventListener('load', () => calendarBar.update(), { once: true });
        }
        $('calendar-nodes').innerHTML = calendar.nodes.map((node, i) => `
            <button class="node" data-index="${i}" style="left:${node.x1}%;top:${node.y1}%;width:${node.x2 - node.x1}%;height:${node.y2 - node.y1}%"></button>`).join('');
        $('calendar-pointer').style.left = `${calendar.pointer}%`;
        $('calendar-date').textContent = calendar.date;
        $('calendar-scroll').scrollTop = 0;
        requestAnimationFrame(() => calendarBar.update());
    }

    // 热区点下去：上架的打开那则公告；还没上架、已下架或找不到的跳提示框
    function openCalendarNode(node) {
        if (node.status === 2) return showPopup(label('notPublic'));
        if (node.status === 3) return showPopup(label('unavailable'));
        const index = state.tabs.findIndex(tab => tab.items.some(x => x.id === node.id));
        if (index < 0) return showPopup(label('notPublic'));
        state.tab = index;
        select(node.id);
    }

    function showPopup(text) {
        $('popup-title').textContent = label('tips');
        $('popup-text').textContent = text;
        $('popup').classList.add('show');
    }

    function hidePopup() {
        $('popup').classList.remove('show');
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
        board.classList.toggle('calendar', state.mode === 'calendar');
        // 打开日历就算看过，先记下来，分页上的红点才会跟着这次重画消失
        if (state.mode === 'calendar' && state.calendar && state.calendar.unread) {
            state.calendar.unread = false;
            post({ action: 'read', id: state.calendar.id });
        }
        renderTabs();
        if (state.mode === 'grid') {
            renderGrid();
        } else if (state.mode === 'detail') {
            renderList();
        } else {
            renderCalendar();
        }
        const empty = state.mode !== 'calendar' && currentTab().items.length === 0;
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
        if (index === state.tab || !(state.tabs[index] || isCalendarTab(index))) return;
        state.tab = index;
        if (isCalendarTab(index)) {
            state.mode = 'calendar';
            state.selected = null;
            render();
            return;
        }
        const items = currentTab().items;
        if (state.mode === 'detail' && items.length > 0) {
            select(items[0].id);
        } else {
            state.mode = 'grid';
            state.selected = null;
            render();
        }
    }

    // 日历页的返回回到第一页的卡片墙
    function back() {
        if (state.mode === 'calendar') state.tab = 0;
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

    $('calendar-nodes').addEventListener('click', (e) => {
        const node = e.target.closest('.node');
        if (node && state.calendar) openCalendarNode(state.calendar.nodes[Number(node.dataset.index)]);
    });

    $('popup').addEventListener('click', (e) => {
        if (e.target === $('popup') || e.target.closest('.popup-close')) hidePopup();
    });

    $('back').addEventListener('click', back);
    $('close').addEventListener('click', () => post({ action: 'close' }));

    // 点公告板外面的暗处关闭
    document.body.addEventListener('pointerdown', (e) => {
        if (e.target === document.body) post({ action: 'close' });
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            if ($('popup').classList.contains('show')) {
                hidePopup();
            } else {
                post({ action: 'close' });
            }
        } else if (e.key === 'Backspace' && state.mode !== 'grid') {
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
                state.lang = message.lang || 'en';
                state.calendar = message.calendar || null;
                state.tab = 0;
                state.mode = isCalendarTab(0) ? 'calendar' : 'grid';
                render();
                document.body.classList.add('ready');
            } else if (message.type === 'content') {
                showContent(message);
            }
        });
    }

    post({ action: 'ready' });
})();
