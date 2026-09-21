// Sales Orders list expander: each [data-lines-toggle] chevron shows/hides the detail row it
// controls (aria-controls) and, on the first open, fetches the order's lines partial
// (?handler=Lines&id=…) into it. The fetched markup stays in the DOM so later toggles are
// instant; several rows can be open at once. Nothing here navigates or touches the filters.
(function () {
    'use strict';

    const LOADING = '<span class="text-muted small">Loading…</span>';
    const FAILED = '<span class="text-danger small">Could not load items.</span> '
        + '<button type="button" class="btn btn-link btn-sm p-0 align-baseline" data-lines-retry>Retry</button>';

    document.addEventListener('click', event => {
        const retry = event.target.closest('[data-lines-retry]');
        if (retry) {
            event.preventDefault();
            const detail = retry.closest('tr');
            const toggle = detail && document.querySelector(`[data-lines-toggle][aria-controls="${detail.id}"]`);
            if (toggle) {
                load(toggle, detail.querySelector('[data-lines-body]'));
            }
            return;
        }

        const toggle = event.target.closest('[data-lines-toggle]');
        if (!toggle) {
            return;
        }

        event.preventDefault();
        const detail = document.getElementById(toggle.getAttribute('aria-controls'));
        const body = detail && detail.querySelector('[data-lines-body]');
        if (!body) {
            return;
        }

        const open = toggle.getAttribute('aria-expanded') !== 'true';
        toggle.setAttribute('aria-expanded', String(open));
        toggle.title = open ? 'Hide items' : 'Show items';
        detail.hidden = !open;
        if (open && body.dataset.state !== 'loaded' && body.dataset.state !== 'loading') {
            load(toggle, body);
        }
    });

    async function load(toggle, body) {
        body.dataset.state = 'loading';
        body.innerHTML = LOADING;
        try {
            const response = await fetch(toggle.dataset.linesToggle, { headers: { 'X-Requested-With': 'fetch' } });
            if (!response.ok) {
                throw new Error(`Lines request failed (${response.status})`);
            }

            body.innerHTML = await response.text();
            body.dataset.state = 'loaded';
        } catch (error) {
            console.error('Sales order lines failed to load', error);
            body.innerHTML = FAILED;
            body.dataset.state = 'failed';
        }
    }
})();
