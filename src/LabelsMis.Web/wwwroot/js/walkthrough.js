// Guided walkthroughs: a card that points at one part of a page at a time and follows the person
// from page to page. The page underneath stays fully usable (the person types and clicks as
// usual), so a walkthrough can be done on real work. The card can be dragged by its top line when
// it is in the way; it goes back to its own place at the next step. The walkthroughs themselves are in
// walkthroughs.js; they start from buttons with data-walkthrough-start="<id>" (Setup → Documentation).
//
// A walkthrough is { name, finishUrl, lookupUrl, steps: [...] }. A step:
//   page      path the step is on: a string, or a RegExp tested against the lower-cased path
//   url       where to go to reach it, when that is always the same address
//   link      or: a selector on the page before it whose href leads there (the first match)
//   lookup    or: a key in the JSON that lookupUrl answers with (null = there is nothing to show)
//   here      for pages that are also shown as the answer to a form post: a function returning the
//             address that opens the page again
//   target    selector, or a list of them: the first one that is on the page and visible is pointed at
//   title, body         what the card says (body is HTML)
//   missing   body to use when no target is on the page
//   alt       [{ when | whenNot, target, title, body }]: the first one whose `when` selector is on the page (or whose
//             `whenNot` selector is not) replaces those parts
//   doneWhen  selector: going forward, the step is skipped when this is already on the page
//   unavailable   what to say when the step's page can't be reached; the walkthrough then moves on
//                 to the next step on another page
//   actions   [{ label, start }]: extra buttons on the card that start another walkthrough
//
// Where the person is in a walkthrough is kept in sessionStorage, so it belongs to one browser tab
// and ends with it.
(function () {
    'use strict';

    const KEY = 'ls_walkthrough';
    const registry = {};
    let state = null;
    let ui = null;
    let shown = null;
    let lookups = null;

    const path = () => (location.pathname.replace(/\/+$/, '') || '/').toLowerCase();
    const here = () => location.pathname + location.search;
    const tour = () => (state ? registry[state.id] : null);

    function read() {
        try {
            const saved = JSON.parse(sessionStorage.getItem(KEY) || 'null');
            return saved && registry[saved.id] ? saved : null;
        } catch (e) {
            return null;
        }
    }

    function save() {
        try {
            sessionStorage.setItem(KEY, JSON.stringify(state));
        } catch (e) {
            // Storage is off: the walkthrough still works on this page and stops at the next one.
        }
    }

    function forget() {
        state = null;
        try {
            sessionStorage.removeItem(KEY);
        } catch (e) {
            // Nothing was kept.
        }
    }

    function onPage(step) {
        return step.page instanceof RegExp ? step.page.test(path()) : path() === String(step.page).toLowerCase();
    }

    const samePage = (a, b) => String(a.page) === String(b.page);

    function visible(el) {
        return !!el && el.getClientRects().length > 0;
    }

    function find(selectors) {
        const list = Array.isArray(selectors) ? selectors : selectors ? [selectors] : [];
        for (const selector of list) {
            for (const el of document.querySelectorAll(selector)) {
                const closed = el.closest('details:not([open])');
                if (closed) {
                    closed.open = true;
                }

                if (visible(el)) {
                    return el;
                }
            }
        }

        return null;
    }

    /** The step with the first matching `alt` laid over it. */
    function resolved(step) {
        const alt = (step.alt || []).find(a => (a.when ? !!document.querySelector(a.when) : !document.querySelector(a.whenNot)));
        return alt ? Object.assign({}, step, alt) : step;
    }

    // ---------------------------------------------------------------------------------- moving

    async function lookup(key) {
        if (!lookups) {
            lookups = fetch(tour().lookupUrl, { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
                .then(r => (r.ok ? r.json() : {}))
                .catch(() => ({}));
        }

        return (await lookups)[key] || null;
    }

    /** The address of a step that is on another page, or null when there is no way to it from here. */
    async function addressOf(index, forward) {
        const step = tour().steps[index];
        if (!forward && state.urls[index]) {
            return state.urls[index];
        }

        if (step.link) {
            const link = find(step.link);
            if (link && link.href) {
                return link.href;
            }
        }

        if (step.lookup) {
            const found = await lookup(step.lookup);
            if (found) {
                return found;
            }
        }

        return step.url || state.urls[index] || null;
    }

    async function go(index, forward) {
        const steps = tour().steps;
        if (index < 0) {
            return;
        }

        if (forward) {
            while (index < steps.length && onPage(steps[index]) && steps[index].doneWhen
                && document.querySelector(steps[index].doneWhen)) {
                index++;
            }
        }

        if (index >= steps.length) {
            finish();
            return;
        }

        const step = steps[index];
        if (onPage(step)) {
            state.step = index;
            state.urls[index] = step.here ? step.here() || here() : here();
            save();
            show(index);
            return;
        }

        const address = await addressOf(index, forward);
        if (address) {
            state.step = index;
            state.urls[index] = address;
            save();
            location.assign(address);
            return;
        }

        if (!forward) {
            go(index - 1, false);
            return;
        }

        // Nothing leads to that page right now (no order is ready, no label has been bought): say
        // so, then carry on with the next part of the walkthrough.
        let next = index + 1;
        while (next < steps.length && samePage(steps[next], step)) {
            next++;
        }

        card({
            title: step.unavailableTitle || 'Nothing to show for this part',
            body: step.unavailable || 'That page has nothing to show right now.',
            index: state.step,
            nextLabel: next >= steps.length ? 'Finish' : 'Continue',
            onNext: () => go(next, true),
            onBack: () => show(state.step)
        }, null);
    }

    function start(id) {
        if (!registry[id]) {
            return;
        }

        remove();
        state = { id: id, step: 0, urls: {} };
        lookups = null;
        save();
        go(0, true);
    }

    function finish() {
        const done = tour();
        forget();
        remove();
        if (done.finishUrl && here() + location.hash !== done.finishUrl) {
            location.assign(done.finishUrl);
        }
    }

    function end() {
        forget();
        remove();
    }

    // --------------------------------------------------------------------------------- showing

    function build() {
        if (ui) {
            return;
        }

        const spot = document.createElement('div');
        spot.className = 'walkthrough-spot';
        spot.hidden = true;

        const box = document.createElement('section');
        box.className = 'walkthrough-card';
        box.setAttribute('role', 'dialog');
        box.setAttribute('aria-label', 'Walkthrough');
        box.innerHTML =
            '<div class="walkthrough-head">'
            + '<span class="walkthrough-name"></span>'
            + '<span class="walkthrough-count"></span>'
            + '<button type="button" class="walkthrough-end" title="End the walkthrough" aria-label="End the walkthrough">&times;</button>'
            + '</div>'
            + '<div class="walkthrough-progress"><span></span></div>'
            + '<div aria-live="polite">'
            + '<h2 class="walkthrough-title"></h2>'
            + '<div class="walkthrough-body"></div>'
            + '</div>'
            + '<div class="walkthrough-foot">'
            + '<button type="button" class="btn btn-sm btn-outline-secondary walkthrough-back">Back</button>'
            + '<span class="walkthrough-extra"></span>'
            + '<button type="button" class="btn btn-sm btn-primary walkthrough-next">Next</button>'
            + '</div>';

        document.body.append(spot, box);
        ui = { spot: spot, card: box };
        box.querySelector('.walkthrough-end').addEventListener('click', end);

        const head = box.querySelector('.walkthrough-head');
        head.title = 'Drag to move';
        head.addEventListener('pointerdown', down => {
            if (down.target.closest('button') || !shown) {
                return;
            }

            const at = box.getBoundingClientRect();
            const dx = down.clientX - at.left;
            const dy = down.clientY - at.top;
            const move = event => {
                if (shown) {
                    shown.moved = { left: event.clientX - dx, top: event.clientY - dy };
                    place();
                }
            };
            const up = () => {
                head.removeEventListener('pointermove', move);
                head.removeEventListener('pointerup', up);
                head.removeEventListener('pointercancel', up);
            };
            head.setPointerCapture(down.pointerId);
            head.addEventListener('pointermove', move);
            head.addEventListener('pointerup', up);
            head.addEventListener('pointercancel', up);
            down.preventDefault();
        });

        const again = () => window.requestAnimationFrame(place);
        window.addEventListener('scroll', again, true);
        window.addEventListener('resize', again);
        ui.timer = window.setInterval(place, 400);
        ui.stop = () => {
            window.removeEventListener('scroll', again, true);
            window.removeEventListener('resize', again);
            window.clearInterval(ui.timer);
        };
    }

    function remove() {
        if (shown && shown.target) {
            shown.target.classList.remove('walkthrough-target');
        }

        shown = null;
        if (ui) {
            ui.stop();
            ui.spot.remove();
            ui.card.remove();
            ui = null;
        }
    }

    function card(content, target) {
        build();
        if (shown && shown.target) {
            shown.target.classList.remove('walkthrough-target');
        }

        const steps = tour().steps;
        const box = ui.card;
        box.classList.remove('walkthrough-card--paused');
        box.querySelector('.walkthrough-name').textContent = tour().name;
        box.querySelector('.walkthrough-count').textContent = (content.index + 1) + ' of ' + steps.length;
        box.querySelector('.walkthrough-progress span').style.width = Math.round(((content.index + 1) / steps.length) * 100) + '%';
        box.querySelector('.walkthrough-title').textContent = content.title;
        box.querySelector('.walkthrough-body').innerHTML = content.body;

        const back = box.querySelector('.walkthrough-back');
        back.textContent = 'Back';
        back.hidden = !content.onBack;
        back.onclick = content.onBack || null;

        const next = box.querySelector('.walkthrough-next');
        next.textContent = content.nextLabel || 'Next';
        next.onclick = content.onNext;

        const extra = box.querySelector('.walkthrough-extra');
        extra.textContent = '';
        (content.actions || []).forEach(action => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = action.label;
            button.addEventListener('click', () => start(action.start));
            extra.append(button);
        });

        shown = { target: target };
        if (target) {
            target.classList.add('walkthrough-target');
            const tall = target.getBoundingClientRect().height > window.innerHeight * 0.55;
            target.scrollIntoView({ block: tall ? 'start' : 'center', behavior: 'smooth' });
        }

        place();
    }

    function show(index) {
        const steps = tour().steps;
        const step = resolved(steps[index]);
        const target = find(step.target);
        const last = index === steps.length - 1;
        card({
            title: step.title,
            body: !target && step.target && step.missing ? step.missing : step.body,
            index: index,
            nextLabel: last ? 'Finish' : 'Next',
            actions: step.actions,
            onNext: () => go(index + 1, true),
            onBack: index > 0 ? () => go(index - 1, false) : null
        }, target);
    }

    /** The current step is on another page and nothing here continues it: offer the way back. */
    function paused() {
        build();
        const steps = tour().steps;
        const box = ui.card;
        box.classList.add('walkthrough-card--paused');
        box.querySelector('.walkthrough-name').textContent = tour().name;
        box.querySelector('.walkthrough-count').textContent = (state.step + 1) + ' of ' + steps.length;
        box.querySelector('.walkthrough-progress span').style.width = Math.round(((state.step + 1) / steps.length) * 100) + '%';
        box.querySelector('.walkthrough-title').textContent = 'Walkthrough paused';
        box.querySelector('.walkthrough-body').innerHTML = 'The next step is on another page.';
        box.querySelector('.walkthrough-extra').textContent = '';

        const back = box.querySelector('.walkthrough-back');
        back.hidden = false;
        back.textContent = 'End';
        back.onclick = end;

        const next = box.querySelector('.walkthrough-next');
        next.textContent = 'Take me back';
        next.onclick = async () => {
            const address = await addressOf(state.step, false);
            if (address) {
                location.assign(address);
            } else {
                go(state.step, true);
            }
        };

        shown = { target: null };
        place();
    }

    function place() {
        if (!ui || !shown) {
            return;
        }

        const box = ui.card;
        const spot = ui.spot;
        const target = shown.target && visible(shown.target) ? shown.target : null;
        const vw = document.documentElement.clientWidth;
        const vh = window.innerHeight;
        const cw = box.offsetWidth;
        const ch = box.offsetHeight;
        const gap = 14;
        const edge = 12;
        const clamp = (value, low, high) => Math.max(low, Math.min(value, Math.max(low, high)));

        // Where the person dragged it to wins, kept inside the window.
        const put = (left, top) => {
            const at = shown.moved || { left: left, top: top };
            box.style.left = clamp(at.left, 0, vw - cw) + 'px';
            box.style.top = clamp(at.top, 0, vh - ch) + 'px';
        };

        if (box.classList.contains('walkthrough-card--paused')) {
            spot.hidden = true;
            put(vw - cw - edge, vh - ch - edge);
            return;
        }

        if (!target) {
            spot.hidden = true;
            put(clamp((vw - cw) / 2, edge, vw - cw - edge), clamp(vh * 0.28, edge, vh - ch - edge));
            return;
        }

        const r = target.getBoundingClientRect();
        const pad = 6;
        spot.hidden = false;
        spot.style.left = (r.left - pad) + 'px';
        spot.style.top = (r.top - pad) + 'px';
        spot.style.width = (r.width + pad * 2) + 'px';
        spot.style.height = (r.height + pad * 2) + 'px';

        let left;
        let top;
        if (vw < 576) {
            // A phone: the card sits along the bottom edge (the top one when the target is down there).
            left = edge;
            top = r.top + r.height / 2 > vh / 2 ? edge : vh - ch - edge;
        } else if (r.bottom + gap + ch <= vh - edge) {
            left = clamp(r.left, edge, vw - cw - edge);
            top = Math.max(r.bottom + gap, edge);
        } else if (r.top - gap - ch >= edge) {
            left = clamp(r.left, edge, vw - cw - edge);
            top = r.top - gap - ch;
        } else if (r.right + gap + cw <= vw - edge) {
            left = r.right + gap;
            top = clamp(r.top, edge, vh - ch - edge);
        } else if (r.left - gap - cw >= edge) {
            left = r.left - gap - cw;
            top = clamp(r.top, edge, vh - ch - edge);
        } else {
            // The target fills the window: the card goes in its bottom right corner.
            left = vw - cw - edge - 8;
            top = vh - ch - edge - 8;
        }

        put(left, top);
    }

    // -------------------------------------------------------------------------------- starting

    /** Picks the walkthrough up on a new page. */
    function resume() {
        state = read();
        if (!state) {
            return;
        }

        const steps = tour().steps;
        let index = Math.min(state.step, steps.length - 1);
        if (!onPage(steps[index])) {
            // The person went on by themselves (pressed Record shipment, bought the label): carry on
            // from the first later step that is on this page.
            const ahead = steps.findIndex((s, i) => i > index && onPage(s));
            if (ahead < 0) {
                paused();
                return;
            }

            index = ahead;
        }

        go(index, true);
    }

    document.addEventListener('click', event => {
        const starter = event.target.closest('[data-walkthrough-start]');
        if (starter) {
            event.preventDefault();
            start(starter.getAttribute('data-walkthrough-start'));
        }
    });

    window.LabelShopWalkthroughs = {
        register: (id, definition) => { registry[id] = definition; },
        start: start,
        end: end
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', resume);
    } else {
        window.setTimeout(resume, 0);
    }
})();
