// Roll picker: a search box that whittles a <select> of inventory rolls down with fuzzy matching.
//
// Markup (see Pages/Shared/_RollPicker.cshtml):
//   [data-roll-picker]
//     input[data-roll-search]     free-text filter; also the scan target
//     select[data-roll-select]    the roll dropdown, options grouped by <optgroup data-group>
//     [data-roll-count]           "N of M rolls" hint
//
// Every query token must match the option's search text (barcode, stock code/description, lot,
// width, remaining LF, location): as a substring, as a subsequence of one word ("wht" → white,
// "bpp" → bopp), or within one edit of a word (typos). Options are re-ranked by match quality.
// A scan (or exact barcode) auto-selects the roll; Enter in the search box then moves on to
// the linear-feet field instead of submitting a half-filled form.
//
// Listeners are delegated to the document so pickers inside fetched modal content just work.
(function () {
    'use strict';

    window.LabelsMis = window.LabelsMis || {};
    if (window.LabelsMis.rollPickerReady) {
        return;
    }
    window.LabelsMis.rollPickerReady = true;

    const pickers = new WeakMap();

    function normalize(text) {
        return (text || '').toString().toLowerCase().replace(/[",]/g, ' ').replace(/\s+/g, ' ').trim();
    }

    // Snapshot the full option list the first time a picker is touched — later filtering
    // rebuilds the <select> from this, never from the (already filtered) DOM.
    function getState(picker) {
        let state = pickers.get(picker);
        if (state) {
            return state;
        }

        const select = picker.querySelector('[data-roll-select]');
        const options = [];
        select.querySelectorAll('optgroup').forEach(group => {
            group.querySelectorAll('option').forEach(option => {
                const haystack = normalize(option.dataset.search || option.textContent);
                options.push({
                    value: option.value,
                    text: option.textContent,
                    group: group.dataset.group || 'other',
                    groupLabel: group.label,
                    haystack,
                    words: haystack.split(' ').filter(Boolean),
                    barcode: normalize(option.value),
                    order: options.length
                });
            });
        });

        state = { select, options, placeholder: select.options[0] ? select.options[0].textContent : '— select a roll —' };
        pickers.set(picker, state);
        return state;
    }

    // ---- matching -----------------------------------------------------------------------

    function isSubsequence(needle, word) {
        let i = 0;
        for (let j = 0; j < word.length && i < needle.length; j++) {
            if (word[j] === needle[i]) {
                i++;
            }
        }
        return i === needle.length;
    }

    // Damerau-Levenshtein distance capped at 1: one insert, delete, substitute or adjacent swap.
    function withinOneEdit(a, b) {
        if (a === b) {
            return true;
        }
        if (Math.abs(a.length - b.length) > 1) {
            return false;
        }
        let i = 0;
        while (i < a.length && i < b.length && a[i] === b[i]) {
            i++;
        }
        if (a.length === b.length) {
            return a.slice(i + 1) === b.slice(i + 1)
                || (a[i] === b[i + 1] && a[i + 1] === b[i] && a.slice(i + 2) === b.slice(i + 2));
        }
        const longer = a.length > b.length ? a : b;
        const shorter = a.length > b.length ? b : a;
        return longer.slice(i + 1) === shorter.slice(i);
    }

    // Typo tolerance is for words ("matte", "semi-gloss"), never for codes: barcodes and lot
    // numbers that differ by one digit are different rolls, not misspellings.
    const wordLike = /^[a-z][a-z-]*$/;

    // Score one query token against an option, or -1 for no match.
    function scoreToken(token, option) {
        if (option.barcode === token) {
            return 100;
        }
        const allowTypo = token.length >= 4 && wordLike.test(token);
        let best = -1;
        for (const word of option.words) {
            let score = -1;
            if (word === token) {
                score = 40;
            } else if (word.startsWith(token)) {
                score = 30;
            } else if (word.includes(token)) {
                score = 20;
            } else if (token.length >= 2 && isSubsequence(token, word)) {
                score = 8 + Math.round(6 * token.length / word.length);
            } else if (allowTypo && wordLike.test(word) && withinOneEdit(token, word)) {
                score = 6;
            }
            if (score > best) {
                best = score;
            }
        }
        if (best < 0 && option.haystack.includes(token)) {
            best = 15; // multi-word substring, e.g. "matte bopp"
        }
        return best;
    }

    function scoreOption(tokens, option) {
        let total = 0;
        for (const token of tokens) {
            const score = scoreToken(token, option);
            if (score < 0) {
                return -1;
            }
            total += score;
        }
        return total;
    }

    // ---- rendering ---------------------------------------------------------------------

    function render(picker, query) {
        const state = getState(picker);
        const select = state.select;
        const previous = select.value;
        const tokens = normalize(query).split(' ').filter(Boolean);

        const matched = state.options
            .map(option => ({ option, score: tokens.length ? scoreOption(tokens, option) : 0 }))
            .filter(item => item.score >= 0)
            .sort((a, b) => b.score - a.score || a.option.order - b.option.order);

        select.innerHTML = '';
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = matched.length ? state.placeholder : 'No rolls match';
        select.appendChild(placeholder);

        const groups = new Map();
        for (const { option } of matched) {
            let group = groups.get(option.group);
            if (!group) {
                group = document.createElement('optgroup');
                group.label = option.groupLabel;
                group.dataset.group = option.group;
                groups.set(option.group, group);
            }
            const element = document.createElement('option');
            element.value = option.value;
            element.textContent = option.text;
            group.appendChild(element);
        }
        // Job material first, then the rest of inventory, regardless of which matched first.
        ['preferred', 'other'].forEach(key => {
            if (groups.has(key)) {
                select.appendChild(groups.get(key));
            }
        });

        // Selection: exact barcode wins, then a lone match, then whatever was already picked.
        const normalizedQuery = normalize(query);
        const exact = matched.find(item => item.option.barcode === normalizedQuery);
        if (exact) {
            select.value = exact.option.value;
        } else if (matched.length === 1) {
            select.value = matched[0].option.value;
        } else if (matched.some(item => item.option.value === previous)) {
            select.value = previous;
        } else {
            select.value = '';
        }

        const count = picker.querySelector('[data-roll-count]');
        if (count) {
            const total = state.options.length;
            count.textContent = tokens.length
                ? `${matched.length} of ${total} roll${total === 1 ? '' : 's'} match`
                : (total ? `${total} roll${total === 1 ? '' : 's'} in inventory` : 'No rolls with material on them in inventory.');
        }

        return { matched, selected: select.value };
    }

    // ---- events ------------------------------------------------------------------------

    document.addEventListener('input', event => {
        const search = event.target.closest('[data-roll-search]');
        if (!search) {
            return;
        }
        const picker = search.closest('[data-roll-picker]');
        if (picker) {
            render(picker, search.value);
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key !== 'Enter') {
            return;
        }
        const search = event.target.closest('[data-roll-search]');
        if (!search) {
            return;
        }
        const picker = search.closest('[data-roll-picker]');
        if (!picker) {
            return;
        }

        // A scanner sends Enter after the barcode: don't submit — settle the selection and hop
        // to the quantity field so the operator can type the footage.
        event.preventDefault();
        const { selected } = render(picker, search.value);
        const form = picker.closest('form');
        if (selected && form) {
            const next = form.querySelector('input[name="consumedLf"], input[name="ConsumedLf"], input[type="number"]');
            if (next) {
                next.focus();
                next.select();
                return;
            }
        }
        const state = getState(picker);
        state.select.focus();
    });
})();
