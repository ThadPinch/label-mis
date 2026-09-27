// Fuzzy filter box for long <select> lists (dies, etc.).
//
// Opt in with `data-filter` on a <select>; a small search input is inserted directly above it.
// Typing hides every option whose text doesn't fuzzily match the query (subsequence match with
// preference for word-start/contiguous hits), and keeps the current selection visible so a saved
// value is never silently hidden. Enter picks the top visible option; Escape clears the box.
//
// Selects added later (e.g. estimate die-cut rows built in JS) are picked up by a MutationObserver,
// so callers don't need to wire anything after inserting markup.
(function () {
    'use strict';

    const MIN_OPTIONS = 8; // don't bother filtering tiny lists
    const RESERVED = new Set(['placeholder']);

    function fuzzyScore(query, text) {
        // Returns -Infinity when no match; higher is better.
        const q = query.toLowerCase();
        const t = text.toLowerCase();
        if (!q) return 0;
        const sub = t.indexOf(q);
        if (sub >= 0) {
            // Contiguous hit: strongly preferred; earlier/word-start positions score higher.
            const wordStart = sub === 0 || /[\s\-_/(),.x×"']/.test(t[sub - 1]);
            return 1000 + (wordStart ? 200 : 0) - sub;
        }
        // Subsequence match: every query char in order, gaps allowed. Per-token so the user can
        // type "3 rnd" and hit "3.000 Round" — all tokens must match.
        const tokens = q.split(/\s+/).filter(Boolean);
        let score = 0;
        for (const tok of tokens) {
            let ti = 0, s = 0, prev = -2;
            for (const ch of tok) {
                const idx = t.indexOf(ch, ti);
                if (idx < 0) return -Infinity;
                s += idx === prev + 1 ? 15 : 1;                                  // contiguous runs
                if (idx === 0 || /[\s\-_/(),.x×"']/.test(t[idx - 1])) s += 10; // word starts
                prev = idx;
                ti = idx + 1;
            }
            score += s - Math.floor(t.length / 20);
        }
        return score;
    }

    function optionsOf(select) {
        return Array.from(select.options).filter(o => !RESERVED.has(o.dataset.role || '') && o.value !== '');
    }

    function apply(select, input) {
        const query = input.value.trim();
        const opts = optionsOf(select);
        let visible = 0;
        const scored = [];
        for (const o of opts) {
            const score = query ? fuzzyScore(query, o.textContent || '') : 0;
            const keep = score > -Infinity || o.selected;
            o.hidden = !keep;
            o.disabled = !keep; // Firefox/Safari ignore `hidden` on <option>
            if (keep) visible++;
            if (keep && score > -Infinity) scored.push({ o, score });
        }
        input.dataset.top = '';
        if (query && scored.length) {
            scored.sort((a, b) => b.score - a.score);
            input.dataset.top = scored[0].o.value;
            // Show the best match as a hint so Enter is predictable.
            input.title = 'Enter selects: ' + (scored[0].o.textContent || '').trim();
        } else {
            input.title = '';
        }
        input.classList.toggle('is-invalid', Boolean(query) && visible === 0);
        input.setAttribute('aria-description', query ? `${visible} of ${opts.length} shown` : '');
    }

    const ready = new WeakSet(); // not a data- attribute, so cloneNode'd rows get re-enhanced

    function enhance(select) {
        if (ready.has(select)) return;
        if (optionsOf(select).length < MIN_OPTIONS) return;
        ready.add(select);

        // A cloned row may carry a dead copy of the input (no listeners) — drop it.
        const stale = select.previousElementSibling;
        if (stale && stale.classList.contains('select-filter-input')) stale.remove();

        const input = document.createElement('input');
        input.type = 'search';
        input.className = 'form-control form-control-sm select-filter-input mb-1';
        input.placeholder = select.dataset.filterPlaceholder || 'Type to filter…';
        input.autocomplete = 'off';
        input.spellcheck = false;
        input.setAttribute('aria-label', 'Filter options');
        if (select.disabled) input.disabled = true;

        select.parentNode.insertBefore(input, select);

        input.addEventListener('input', () => apply(select, input));
        input.addEventListener('keydown', e => {
            if (e.key === 'Enter') {
                e.preventDefault(); // don't submit the form
                const top = input.dataset.top;
                if (top && select.value !== top) {
                    select.value = top;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                select.focus();
            } else if (e.key === 'Escape') {
                input.value = '';
                apply(select, input);
            } else if (e.key === 'ArrowDown') {
                e.preventDefault();
                select.focus();
            }
        });
        // Keep the disabled state in sync (SO lines toggle `disabled` on lock/unlock).
        new MutationObserver(() => { input.disabled = select.disabled; })
            .observe(select, { attributes: true, attributeFilter: ['disabled'] });
    }

    function scan(root) {
        if (!(root instanceof Element) && root !== document) return;
        if (root instanceof Element && root.matches('select[data-filter]')) enhance(root);
        root.querySelectorAll('select[data-filter]').forEach(enhance);
    }

    document.addEventListener('DOMContentLoaded', () => {
        scan(document);
        new MutationObserver(records => {
            for (const r of records) r.addedNodes.forEach(n => { if (n.nodeType === 1) scan(n); });
        }).observe(document.body, { childList: true, subtree: true });
    });
})();
