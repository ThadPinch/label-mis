// Live QuickBooks status badges on the invoice pages.
//
// A page opts in with one element carrying data-qb-status-url (the Invoices page's
// QuickBooksStates handler). Every badge rendered by QuickBooksUi.Badge carries
// data-qb-badge, data-invoice-id and data-busy. While any badge is busy (queued or sending),
// the script asks for those invoices' states every few seconds and updates the badges in
// place; it stops asking once nothing is busy.
//
// On the Invoices Ready tab (data-qb-tab="ready") a row that lands in QuickBooks, or that
// QuickBooks turns down, leaves the Ready count and its tick box is cleared. With
// data-qb-reload-on-settle="true" (the invoice and preview pages) the page reloads when an
// invoice settles, so its QuickBooks card shows the outcome; if the user is typing in a form,
// a "refresh" note appears instead.
(function () {
    const root = document.querySelector('[data-qb-status-url]');
    if (!root) return;

    const url = root.dataset.qbStatusUrl;
    const tab = root.dataset.qbTab || '';
    const reloadOnSettle = root.dataset.qbReloadOnSettle === 'true';
    const intervalMs = parseInt(root.dataset.qbInterval || '4000', 10);
    let timer = null;

    const allBadges = () => Array.from(document.querySelectorAll('[data-qb-badge][data-invoice-id]'));
    const busyBadges = () => allBadges().filter(b => b.dataset.busy === 'true');

    function schedule(delay) {
        if (timer === null) timer = window.setTimeout(poll, delay);
    }

    function adjustCount(id, delta) {
        const el = document.getElementById(id);
        if (!el) return;
        const n = parseInt(el.textContent, 10);
        if (!isNaN(n)) el.textContent = String(Math.max(0, n + delta));
    }

    function apply(badge, state) {
        badge.className = 'badge qb-badge ' + state.badgeClass;
        badge.textContent = state.label || '';
        badge.dataset.busy = state.busy ? 'true' : 'false';
        if (state.needsAttention && state.lastError) {
            badge.title = state.lastError;
        } else {
            badge.removeAttribute('title');
        }
    }

    function leaveReadyList(row) {
        const box = row && row.querySelector('.invoice-select');
        if (!box) return;
        box.checked = false;
        box.disabled = true;
        box.dispatchEvent(new Event('change'));
    }

    function settled(badge, state) {
        const row = badge.closest('tr[data-invoice-id]');
        if (tab !== 'ready' || !row) return;
        if (state.inQuickBooks && !state.needsAttention) {
            row.classList.add('table-success');
            adjustCount('tab-ready-count', -1);
            adjustCount('tab-exported-count', 1);
            leaveReadyList(row);
        } else if (state.needsAttention) {
            row.classList.add('table-danger');
            adjustCount('tab-ready-count', -1);
            adjustCount('tab-attention-count', 1);
            leaveReadyList(row);
        }
    }

    function userIsTyping() {
        const el = document.activeElement;
        return !!el && ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName) && el.type !== 'checkbox';
    }

    function showSettledNotice() {
        const notice = document.querySelector('[data-qb-settled-notice]');
        if (notice) notice.classList.remove('d-none');
    }

    async function poll() {
        timer = null;
        const busy = busyBadges();
        if (busy.length === 0) return;
        if (document.hidden) {
            schedule(intervalMs);
            return;
        }

        const ids = Array.from(new Set(busy.map(b => b.dataset.invoiceId))).slice(0, 100);
        const query = new URLSearchParams();
        ids.forEach(id => query.append('ids', id));

        let states;
        try {
            const response = await fetch(url + (url.includes('?') ? '&' : '?') + query.toString(), {
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
            if (!response.ok) throw new Error('HTTP ' + response.status);
            states = await response.json();
        } catch (e) {
            // A failed poll (signed out, server restarting) waits longer and tries again.
            schedule(intervalMs * 3);
            return;
        }

        const byId = new Map(states.map(s => [String(s.id).toLowerCase(), s]));
        let anySettled = false;
        ids.forEach(id => {
            const state = byId.get(id.toLowerCase());
            document.querySelectorAll(`[data-qb-badge][data-invoice-id="${id}"]`).forEach(badge => {
                if (!state) {
                    // The invoice is gone; stop asking about it.
                    badge.dataset.busy = 'false';
                    return;
                }
                apply(badge, state);
                if (!state.busy) {
                    anySettled = true;
                    settled(badge, state);
                }
            });
        });

        if (anySettled && reloadOnSettle) {
            if (userIsTyping()) {
                showSettledNotice();
            } else {
                window.location.reload();
                return;
            }
        }

        if (busyBadges().length > 0) schedule(intervalMs);
    }

    if (busyBadges().length > 0) schedule(intervalMs);
})();
