// Job page imposition card: live frame summary while the template is edited, and the
// expand/collapse preview of the imposed PDF (fetched on first expand, like the artwork card).
(() => {
    const form = document.querySelector('[data-imposition-form]');
    if (!form) return;

    const num = (key) => {
        const el = form.querySelector(`[data-imp="${key}"]`);
        const value = parseFloat(el?.value);
        return Number.isFinite(value) ? value : 0;
    };
    const fmt = (v) => (Math.round(v * 1000) / 1000).toString();

    const live = form.querySelector('[data-imposition-live]');
    const summary = document.querySelector('[data-imposition-summary]');

    const update = () => {
        const rotated = form.querySelector('[data-imp="orientation"]')?.value === '1';
        const labelAcross = num('labelAcross');
        const labelAround = num('labelAround');
        const placedAcross = rotated ? labelAround : labelAcross;
        const placedAround = rotated ? labelAcross : labelAround;
        const across = Math.max(1, Math.round(num('across')));
        const around = Math.max(1, Math.round(num('around')));
        const gutterAcross = num('gutterAcross');
        const gutterAround = num('gutterAround');
        const web = num('webWidth');
        const offset = num('offset');
        const block = across * placedAcross + (across - 1) * gutterAcross;
        const repeat = around * (placedAround + gutterAround);
        const left = (web - block) / 2 + offset;
        const right = (web - block) / 2 - offset;
        const overflow = left < 0 || right < 0;

        if (live) {
            live.textContent = `Block ${fmt(block)}" wide · margins ${fmt(left)}" / ${fmt(right)}" · repeat ${fmt(repeat)}"`;
            live.classList.toggle('text-danger', overflow);
            if (overflow) live.textContent += ' — wider than the web';
        }
        if (summary) {
            summary.textContent = `${across} × ${around} = ${across * around} per frame · ${fmt(web)}" × ${fmt(repeat)}" repeat`;
        }
    };

    form.querySelectorAll('[data-imp]').forEach(el => {
        el.addEventListener('input', update);
        el.addEventListener('change', update);
    });

    const expandButton = document.getElementById('imposition-expand');
    const viewer = document.getElementById('imposition-viewer');
    if (expandButton && viewer) {
        expandButton.addEventListener('click', () => {
            const expanded = !viewer.classList.toggle('d-none');
            expandButton.textContent = expanded ? 'Collapse' : 'Expand';
            if (expanded) {
                const media = viewer.querySelector('[data-src]');
                if (media && !media.src) media.src = media.dataset.src;
                viewer.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
        });
    }

    // Original artwork is always the default tab on load. Only a fresh imposition ACTION
    // (Run / Save / Reset / Upload leaves a success/warning/error alert) switches to the
    // Imposition tab so its result is visible — a plain visit or an #imposition link does not.
    const impositionTabButton = document.getElementById('artwork-tab-imposition-btn');
    const actionAlert = document.querySelector(
        '#imposition .alert-success, #imposition .alert-warning, #imposition .alert-danger');
    if (actionAlert && impositionTabButton && window.bootstrap?.Tab) {
        bootstrap.Tab.getOrCreateInstance(impositionTabButton).show();
        document.getElementById('artwork')?.scrollIntoView({ block: 'start' });
        if (expandButton && viewer && viewer.classList.contains('d-none')) {
            expandButton.click();
        }
    }
})();
