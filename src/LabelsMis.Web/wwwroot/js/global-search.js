// Global search bar (Pages/Shared/_Layout.cshtml → .app-search).
//
// Type to get grouped suggestions (customers, sales orders, jobs, estimates, invoices, shipments,
// products, POs, rolls, dies, stocks, suppliers) from /search?handler=Suggest. ↑/↓ move through
// them, Enter opens the highlighted one; with nothing highlighted Enter opens the record a full
// number names ("JOB-2026-00052", "job 52") or the full results page. "/" or Ctrl/⌘+K focuses the
// box from anywhere. Records opened from here are remembered (localStorage) and offered as
// "Recent" when the box is focused empty.
(function () {
    'use strict';

    const form = document.querySelector('form.app-search');
    if (!form) {
        return;
    }

    const input = form.querySelector('.app-search-input');
    const panel = form.querySelector('.app-search-panel');
    const suggestUrl = form.dataset.suggestUrl;
    const searchPage = form.getAttribute('action') || '/search';

    const RECENT_KEY = 'ls.search.recent.v1';
    const MAX_RECENT = 8;
    const DEBOUNCE_MS = 140;
    const MIN_CHARS = 2;

    let options = [];      // [{ el, url, item }] in display order
    let active = -1;
    let timer = 0;
    let controller = null;
    let requestSeq = 0;
    let last = null;       // last suggest response, keyed by its q

    // ---- helpers ------------------------------------------------------------------------

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"']/g, ch => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        })[ch]);
    }

    function escapeRegex(value) {
        return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    function termsOf(query) {
        const doc = /^(JOB|SO|INV|EST|PO|SHP|R)[\s#:\-]*(?:\d{4}[\s\-]+)?(\d{1,6})$/i.exec(query.trim());
        if (doc) {
            return [doc[2], query.trim()];
        }
        return query.trim().split(/\s+/).filter(Boolean).slice(0, 6);
    }

    // Encode the text and wrap every occurrence of a search term in <mark>.
    function highlight(text, terms) {
        if (!text) {
            return '';
        }
        const wanted = terms.filter(t => t.length > 0).sort((a, b) => b.length - a.length);
        if (wanted.length === 0) {
            return escapeHtml(text);
        }
        const pattern = new RegExp('(' + wanted.map(escapeRegex).join('|') + ')', 'ig');
        return String(text).split(pattern)
            .map((part, i) => (i % 2 === 1 ? '<mark>' + escapeHtml(part) + '</mark>' : escapeHtml(part)))
            .join('');
    }

    function icon(key) {
        return '<span class="as-icon as-icon-' + escapeHtml(key) + '" aria-hidden="true">' +
            '<svg><use href="#si-' + escapeHtml(key) + '"></use></svg></span>';
    }

    function resultsUrl(query) {
        return searchPage + '?q=' + encodeURIComponent(query) + '&all=true';
    }

    // ---- recent picks -------------------------------------------------------------------

    function readRecent() {
        try {
            const list = JSON.parse(localStorage.getItem(RECENT_KEY) || '[]');
            return Array.isArray(list) ? list.filter(r => r && r.url && r.title) : [];
        } catch (e) {
            return [];
        }
    }

    function remember(item) {
        if (!item || !item.url || !item.title || item.kind === 'all') {
            return;
        }
        try {
            const recent = readRecent().filter(r => r.url !== item.url);
            recent.unshift({ title: item.title, subtitle: item.subtitle || '', url: item.url, key: item.key || '' });
            localStorage.setItem(RECENT_KEY, JSON.stringify(recent.slice(0, MAX_RECENT)));
        } catch (e) {
            // Private mode / storage full: recents are a convenience only.
        }
    }

    // ---- panel --------------------------------------------------------------------------

    function openPanel() {
        panel.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        form.classList.add('is-open');
    }

    function closePanel() {
        panel.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        form.classList.remove('is-open');
        setActive(-1);
    }

    function setActive(index) {
        active = index;
        options.forEach((option, i) => {
            const on = i === index;
            option.el.classList.toggle('is-active', on);
            option.el.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        if (index >= 0 && options[index]) {
            input.setAttribute('aria-activedescendant', options[index].el.id);
            options[index].el.scrollIntoView({ block: 'nearest' });
        } else {
            input.removeAttribute('aria-activedescendant');
        }
    }

    function optionHtml(item, index, terms) {
        const badge = item.badge
            ? '<span class="as-badge tone-' + escapeHtml(item.tone || 'neutral') + '">' + escapeHtml(item.badge) + '</span>'
            : '';
        const sub = item.subtitle ? '<span class="as-sub">' + highlight(item.subtitle, terms) + '</span>' : '';
        return '<a class="as-item" role="option" aria-selected="false" id="as-opt-' + index + '" href="' + escapeHtml(item.url) + '" data-index="' + index + '">' +
            icon(item.key) +
            '<span class="as-text"><span class="as-title">' + highlight(item.title, terms) + '</span>' + sub + '</span>' +
            badge + '</a>';
    }

    function keyHints() {
        return '<span class="as-keys"><kbd>↑</kbd><kbd>↓</kbd> move <kbd>Enter</kbd> open <kbd>Esc</kbd> close</span>';
    }

    function show(html, list) {
        panel.innerHTML = html;
        options = list.map((item, i) => ({ item, el: panel.querySelector('#as-opt-' + i) })).filter(o => o.el);
        active = -1;
        openPanel();
    }

    function renderResults(data) {
        const query = data.q || input.value.trim();
        const terms = termsOf(query);
        const list = [];
        let html = '';

        (data.groups || []).forEach(group => {
            html += '<div class="as-group" role="presentation">' +
                '<div class="as-group-head"><span>' + escapeHtml(group.label) + '</span>' +
                (group.seeAllUrl ? '<a class="as-more" href="' + escapeHtml(group.seeAllUrl) + '" tabindex="-1">See all</a>' : '') +
                '</div>';
            group.items.forEach(item => {
                const entry = Object.assign({ key: group.key }, item);
                html += optionHtml(entry, list.length, terms);
                list.push(entry);
            });
            html += '</div>';
        });

        if (list.length === 0) {
            show('<div class="as-message"><strong>No matches for “' + escapeHtml(query) + '”.</strong>' +
                '<span>Try a number (job, order, PO, invoice, tracking), a customer or contact, a SKU, or a size like 2x3.</span></div>', []);
            return;
        }

        const all = { kind: 'all', key: 'search', title: 'All results for “' + query + '”', url: data.searchUrl || resultsUrl(query) };
        html += '<div class="as-foot">' + keyHints() +
            '<a class="as-item as-all" role="option" aria-selected="false" id="as-opt-' + list.length + '" href="' + escapeHtml(all.url) + '" data-index="' + list.length + '">' +
            'All results for “' + escapeHtml(query) + '” →</a></div>';
        list.push(all);
        show(html, list);
    }

    function renderRecent() {
        const recent = readRecent();
        if (recent.length === 0) {
            show('<div class="as-message"><strong>Search everything from here.</strong>' +
                '<span>Job, sales order, estimate, invoice, shipment or PO number · customer or contact · customer PO · SKU · tracking # · roll barcode · a label size like 2x3.</span>' +
                '<span class="as-tip">Press <kbd>/</kbd> or <kbd>Ctrl</kbd>+<kbd>K</kbd> on any page to jump here.</span></div>', []);
            return;
        }

        let html = '<div class="as-group" role="presentation"><div class="as-group-head"><span>Recent</span>' +
            '<button type="button" class="as-clear" tabindex="-1">Clear</button></div>';
        recent.forEach((item, i) => { html += optionHtml(Object.assign({ key: item.key || 'recent' }, item), i, []); });
        html += '</div><div class="as-foot">' + keyHints() + '</div>';
        show(html, recent.map(item => Object.assign({ key: item.key || 'recent' }, item)));
    }

    function renderMessage(html) {
        show('<div class="as-message">' + html + '</div>', []);
    }

    // ---- fetching -----------------------------------------------------------------------

    async function suggest(query) {
        if (controller) {
            controller.abort();
        }
        controller = new AbortController();
        const seq = ++requestSeq;
        form.classList.add('is-loading');

        try {
            const response = await fetch(suggestUrl + '&q=' + encodeURIComponent(query), {
                headers: { 'Accept': 'application/json', 'X-Requested-With': 'fetch' },
                credentials: 'same-origin',
                signal: controller.signal
            });
            const type = response.headers.get('content-type') || '';
            if (response.redirected || !type.includes('json')) {
                if (seq === requestSeq) {
                    renderMessage('<strong>Your session has ended.</strong><span><a href="/Account/Login">Sign in again</a> to search.</span>');
                }
                return;
            }
            if (!response.ok) {
                throw new Error('Search failed (' + response.status + ')');
            }

            const data = await response.json();
            if (seq !== requestSeq || input.value.trim() !== query) {
                return; // a newer keystroke owns the panel
            }
            last = data;
            renderResults(data);
        } catch (error) {
            if (error.name !== 'AbortError' && seq === requestSeq) {
                console.error(error);
                renderMessage('<strong>Search is unavailable right now.</strong><span>Press Enter to open the results page.</span>');
            }
        } finally {
            if (seq === requestSeq) {
                form.classList.remove('is-loading');
            }
        }
    }

    function onQueryChanged() {
        const query = input.value.trim();
        window.clearTimeout(timer);
        if (query.length === 0) {
            if (controller) {
                controller.abort();
            }
            form.classList.remove('is-loading');
            renderRecent();
            return;
        }
        if (query.length < MIN_CHARS) {
            renderMessage('<span>Keep typing…</span>');
            return;
        }
        if (last && last.q === query) {
            renderResults(last);
            return;
        }
        timer = window.setTimeout(() => suggest(query), DEBOUNCE_MS);
    }

    function go(option, newTab) {
        if (!option) {
            return;
        }
        remember(option.item);
        if (newTab) {
            window.open(option.item.url, '_blank', 'noopener');
            return;
        }
        window.location.assign(option.item.url);
    }

    // ---- events -------------------------------------------------------------------------

    input.addEventListener('input', onQueryChanged);
    input.addEventListener('focus', () => {
        form.classList.add('is-focused');
        onQueryChanged();
    });
    input.addEventListener('blur', () => form.classList.remove('is-focused'));

    input.addEventListener('keydown', event => {
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                if (panel.hidden) {
                    onQueryChanged();
                    return;
                }
                if (options.length) {
                    setActive(active < options.length - 1 ? active + 1 : 0);
                }
                return;
            case 'ArrowUp':
                event.preventDefault();
                if (options.length) {
                    setActive(active > 0 ? active - 1 : options.length - 1);
                }
                return;
            case 'Escape':
                if (!panel.hidden) {
                    event.preventDefault();
                    closePanel();
                } else if (input.value) {
                    input.value = '';
                } else {
                    input.blur();
                }
                return;
            case 'Enter': {
                event.preventDefault();
                const query = input.value.trim();
                if (active >= 0 && options[active]) {
                    go(options[active], event.ctrlKey || event.metaKey);
                    return;
                }
                if (!query) {
                    return;
                }
                if (last && last.q === query && last.jumpUrl) {
                    window.location.assign(last.jumpUrl);
                    return;
                }
                // The results page itself opens a record the query names exactly.
                window.location.assign(searchPage + '?q=' + encodeURIComponent(query));
                return;
            }
            default:
                return;
        }
    });

    panel.addEventListener('mousemove', event => {
        const el = event.target.closest('.as-item');
        if (el && el.dataset.index !== undefined && Number(el.dataset.index) !== active) {
            setActive(Number(el.dataset.index));
        }
    });

    // Keep focus in the box while clicking inside the panel (so blur doesn't close it first).
    panel.addEventListener('mousedown', event => {
        if (event.target.closest('.as-item, .as-more, .as-clear')) {
            event.preventDefault();
        }
    });

    panel.addEventListener('click', event => {
        if (event.target.closest('.as-clear')) {
            event.preventDefault();
            try { localStorage.removeItem(RECENT_KEY); } catch (e) { /* ignore */ }
            renderRecent();
            input.focus();
            return;
        }
        const el = event.target.closest('.as-item');
        if (el && el.dataset.index !== undefined) {
            remember(options[Number(el.dataset.index)]?.item);
        }
    });

    document.addEventListener('mousedown', event => {
        if (!form.contains(event.target)) {
            closePanel();
        }
    });

    form.addEventListener('focusout', event => {
        if (!form.contains(event.relatedTarget)) {
            window.setTimeout(() => {
                if (!form.contains(document.activeElement)) {
                    closePanel();
                }
            }, 0);
        }
    });

    // "/" from anywhere that isn't a text field, or Ctrl/⌘+K from anywhere.
    document.addEventListener('keydown', event => {
        const target = event.target;
        const typing = target instanceof HTMLElement &&
            (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
        const slash = event.key === '/' && !typing && !event.ctrlKey && !event.metaKey && !event.altKey;
        const ctrlK = (event.key === 'k' || event.key === 'K') && (event.ctrlKey || event.metaKey) && !event.altKey;
        if (!slash && !ctrlK) {
            return;
        }
        // The collapsed (mobile) menu hides the box; open it first.
        const collapse = form.closest('.navbar-collapse');
        if (collapse && !collapse.classList.contains('show') && getComputedStyle(collapse).display === 'none' && window.bootstrap) {
            window.bootstrap.Collapse.getOrCreateInstance(collapse).show();
        }
        event.preventDefault();
        input.focus();
        input.select();
    });
})();
