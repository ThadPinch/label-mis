// Jobs floor (Pages/Jobs/Index.cshtml).
//
// - Find box: in live views every live job is already on the page, so typing filters cards/rows
//   instantly (every word must appear in the job's data-search text) and the URL keeps the text.
//   In history views (Shipped / Closed / All) it is a normal server search: Enter submits.
// - Due chips: Overdue / Due today / Next 7 days narrow the board further (data-urgency).
// - Auto refresh: every minute the board re-fetches this same URL and swaps the results in place,
//   keeping each column's scroll position — skipped while a popup or the dates panel is open,
//   or the tab is in the background. The switch is remembered per browser.
// - ?focus=<jobId> (or #job-<jobId>) scrolls that job into view and flashes it.
// - The board fills the window below the toolbar; columns scroll on their own.
(function () {
    'use strict';

    const root = document.getElementById('floor');
    if (!root) {
        return;
    }

    const live = root.dataset.live === 'true';
    const find = document.getElementById('floor-find');
    const findCount = document.getElementById('floor-find-count');
    const filtersForm = document.getElementById('floor-filters');
    const autoSwitch = document.getElementById('floor-auto');
    const refreshButton = document.getElementById('floor-refresh');
    const updatedLabel = document.getElementById('floor-updated');

    const AUTO_KEY = 'ls.floor.auto';
    const REFRESH_MS = 60000;
    const DUE_MATCH = {
        overdue: ['overdue'],
        today: ['today'],
        week: ['today', 'tomorrow', 'thisweek']
    };

    let dueFilter = null;
    let refreshTimer = 0;
    let refreshing = false;
    let findTimer = 0;

    function jobs() {
        return Array.from(document.querySelectorAll('#floor-results [data-job-id]'));
    }

    function terms() {
        if (!live || !find) {
            return [];
        }
        return find.value.toLowerCase().trim().split(/\s+/).filter(Boolean);
    }

    // ---- filtering ----------------------------------------------------------------------

    function apply() {
        const wanted = terms();
        const all = jobs();
        const dueCounts = { overdue: 0, today: 0, week: 0 };
        let shown = 0;

        all.forEach(el => {
            const text = el.dataset.search || '';
            const textOk = wanted.every(term => text.includes(term));
            const urgency = el.dataset.urgency || 'none';
            if (textOk) {
                Object.keys(DUE_MATCH).forEach(key => {
                    if (DUE_MATCH[key].includes(urgency)) {
                        dueCounts[key]++;
                    }
                });
            }
            const visible = textOk && (!dueFilter || DUE_MATCH[dueFilter].includes(urgency));
            el.hidden = !visible;
            if (visible) {
                shown++;
            }
        });

        const filtering = wanted.length > 0 || dueFilter !== null;
        document.querySelectorAll('#floor-results .floor-col').forEach(col => {
            const cards = col.querySelectorAll('[data-job-id]');
            const visible = Array.from(cards).filter(card => !card.hidden).length;
            const counter = col.querySelector('[data-col-count]');
            if (counter) {
                counter.textContent = filtering && visible !== cards.length ? visible + ' of ' + cards.length : String(cards.length);
            }
            const empty = col.querySelector('.floor-col-empty');
            if (empty) {
                empty.hidden = visible > 0;
                empty.textContent = cards.length > 0 ? 'No matches here' : 'Nothing here';
            }
        });

        const listEmpty = document.querySelector('#floor-results .floor-filter-empty');
        if (listEmpty) {
            listEmpty.hidden = shown > 0 || all.length === 0;
        }

        document.querySelectorAll('[data-due-count]').forEach(span => {
            const n = dueCounts[span.dataset.dueCount] || 0;
            span.textContent = String(n);
            span.closest('.floor-chip')?.classList.toggle('is-zero', n === 0);
        });

        if (findCount) {
            findCount.textContent = live && filtering ? shown + ' of ' + all.length : '';
        }
    }

    function rememberSearch() {
        if (!live || !find) {
            return;
        }
        const url = new URL(window.location.href);
        const value = find.value.trim();
        if (value) {
            url.searchParams.set('Search', value);
        } else {
            url.searchParams.delete('Search');
        }
        window.history.replaceState(window.history.state, '', url.toString());
    }

    if (find) {
        find.addEventListener('input', () => {
            if (!live) {
                return;
            }
            window.clearTimeout(findTimer);
            findTimer = window.setTimeout(() => {
                apply();
                rememberSearch();
            }, 60);
        });

        find.addEventListener('keydown', event => {
            if (event.key === 'Escape' && find.value) {
                event.preventDefault();
                find.value = '';
                apply();
                rememberSearch();
            } else if (event.key === 'Enter' && live) {
                // Live views filter as you type; Enter jumps to a lone match instead of reloading.
                event.preventDefault();
                const visible = jobs().filter(el => !el.hidden);
                if (visible.length === 1) {
                    const link = visible[0].querySelector('.floor-card-number');
                    if (link) {
                        window.location.assign(link.href);
                    }
                }
            }
        });
    }

    document.addEventListener('click', event => {
        const chip = event.target.closest('[data-due-filter]');
        if (!chip || !root.contains(chip)) {
            return;
        }
        const key = chip.dataset.dueFilter;
        dueFilter = dueFilter === key ? null : key;
        root.querySelectorAll('[data-due-filter]').forEach(button => {
            const on = button.dataset.dueFilter === dueFilter;
            button.classList.toggle('active', on);
            button.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
        apply();
    });

    // Customer / press dropdowns apply straight away.
    root.querySelectorAll('select[data-autosubmit]').forEach(select => {
        select.addEventListener('change', () => filtersForm?.submit());
    });

    // Close the dates panel when clicking elsewhere.
    document.addEventListener('mousedown', event => {
        root.querySelectorAll('details.floor-more[open]').forEach(panel => {
            if (!panel.contains(event.target)) {
                panel.removeAttribute('open');
            }
        });
    });

    // ---- sizing -------------------------------------------------------------------------

    function sizeToWindow() {
        const target = document.getElementById('floor-board')
            || (live ? document.querySelector('#floor-results .floor-list-wrap') : null);
        if (!target) {
            return;
        }
        if (window.matchMedia('(max-width: 767.98px)').matches) {
            target.style.removeProperty('--floor-h');
            return;
        }
        const top = target.getBoundingClientRect().top + window.scrollY;
        const footer = document.querySelector('.app-footer');
        const footerHeight = footer ? footer.offsetHeight : 0;
        const height = Math.max(420, window.innerHeight - top - footerHeight - 20);
        target.style.setProperty('--floor-h', height + 'px');
    }

    window.addEventListener('resize', sizeToWindow);

    // ---- auto refresh -------------------------------------------------------------------

    function stamp(text) {
        if (updatedLabel) {
            updatedLabel.textContent = text || 'Updated ' + new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
        }
    }

    async function refresh(manual) {
        if (refreshing) {
            return;
        }
        if (!manual && (document.visibilityState !== 'visible'
            || document.querySelector('.modal.show')
            || root.querySelector('details.floor-more[open]'))) {
            return;
        }

        refreshing = true;
        root.classList.add('is-refreshing');
        try {
            const response = await fetch(window.location.href, {
                headers: { 'X-Requested-With': 'fetch' },
                credentials: 'same-origin',
                cache: 'no-store'
            });
            if (response.redirected && /\/Account\/Login/i.test(response.url)) {
                stamp('Signed out — reload to sign in');
                stopAuto();
                return;
            }
            if (!response.ok) {
                throw new Error('Refresh failed (' + response.status + ')');
            }

            const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
            const fresh = doc.getElementById('floor-results');
            if (!fresh) {
                throw new Error('Refresh returned no board');
            }

            const scrolls = {};
            document.querySelectorAll('#floor-results .floor-col').forEach(col => {
                scrolls[col.dataset.stage] = col.querySelector('.floor-col-body')?.scrollTop || 0;
            });
            const listWrap = document.querySelector('#floor-results .floor-list-wrap');
            const listScroll = listWrap ? listWrap.scrollTop : 0;

            document.getElementById('floor-results').innerHTML = fresh.innerHTML;
            ['floor-stages', 'floor-live-count'].forEach(id => {
                const current = document.getElementById(id);
                const next = doc.getElementById(id);
                if (current && next) {
                    current.innerHTML = next.innerHTML;
                }
            });

            document.querySelectorAll('#floor-results .floor-col').forEach(col => {
                const body = col.querySelector('.floor-col-body');
                if (body && scrolls[col.dataset.stage]) {
                    body.scrollTop = scrolls[col.dataset.stage];
                }
            });
            const newListWrap = document.querySelector('#floor-results .floor-list-wrap');
            if (newListWrap) {
                newListWrap.scrollTop = listScroll;
            }

            apply();
            sizeToWindow();
            stamp();
        } catch (error) {
            console.error(error);
            stamp('Refresh failed — will retry');
        } finally {
            refreshing = false;
            root.classList.remove('is-refreshing');
        }
    }

    function startAuto() {
        window.clearInterval(refreshTimer);
        refreshTimer = window.setInterval(() => refresh(false), REFRESH_MS);
    }

    function stopAuto() {
        window.clearInterval(refreshTimer);
        refreshTimer = 0;
        if (autoSwitch) {
            autoSwitch.checked = false;
        }
    }

    if (live && autoSwitch) {
        let enabled = true;
        try {
            enabled = localStorage.getItem(AUTO_KEY) !== 'off';
        } catch (e) {
            // storage unavailable: default on
        }
        autoSwitch.checked = enabled;
        if (enabled) {
            startAuto();
        }
        autoSwitch.addEventListener('change', () => {
            try {
                localStorage.setItem(AUTO_KEY, autoSwitch.checked ? 'on' : 'off');
            } catch (e) {
                // ignore
            }
            if (autoSwitch.checked) {
                startAuto();
                refresh(true);
            } else {
                stopAuto();
            }
        });

        // Coming back to the tab after a while: catch up straight away.
        let hiddenAt = 0;
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'hidden') {
                hiddenAt = Date.now();
            } else if (autoSwitch.checked && hiddenAt && Date.now() - hiddenAt > REFRESH_MS) {
                refresh(false);
            }
        });
    }

    refreshButton?.addEventListener('click', () => refresh(true));

    // ---- focus a job --------------------------------------------------------------------

    function focusJob() {
        const params = new URLSearchParams(window.location.search);
        const id = params.get('focus') || (window.location.hash.startsWith('#job-') ? window.location.hash.slice(5) : null);
        if (!id) {
            return;
        }
        const el = document.getElementById('job-' + id);
        if (!el) {
            return;
        }
        el.hidden = false;
        el.scrollIntoView({ block: 'center', inline: 'center' });
        el.classList.add('is-focused');
        window.setTimeout(() => el.classList.remove('is-focused'), 4000);
    }

    apply();
    sizeToWindow();
    stamp();
    focusJob();
})();
