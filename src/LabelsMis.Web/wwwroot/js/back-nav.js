// Back-nav query memory.
//
// List (Index) pages render <body data-remember-query="true"> (see Shared/_Layout.cshtml).
// On those pages we remember the current query string (search, filters, sort, page)
// in the tab's sessionStorage, keyed by the page path. Any <a data-back-nav> link
// (Shared/_BackNav, "Cancel" links) whose target has a remembered query gets that
// query appended, so "Back" returns the user to exactly what they were looking at —
// even after the save/redirect round-trips an edit page goes through.
//
// The same remembered query is also added to the page's POST forms as a hidden
// "backNavReturnQuery" field, so handlers that leave the record (delete, deactivate)
// can redirect to the same view of the list via PageModel.RedirectToListPage()
// (see Pages/Shared/BackNavExtensions.cs).
//
// sessionStorage is per tab, so two tabs with different searches keep separate
// back targets; the plain link stays as a fallback if nothing is remembered.
(function () {
    'use strict';

    var PREFIX = 'lm.back-nav:';
    var RETURN_QUERY_FIELD = 'backNavReturnQuery';

    function normalizePath(pathname) {
        var p = (pathname || '/').toLowerCase();
        p = p.replace(/\/index$/, '');
        p = p.replace(/\/+$/, '');
        return p || '/';
    }

    function readStored(key) {
        try { return window.sessionStorage.getItem(PREFIX + key); }
        catch (e) { return null; }
    }

    function writeStored(key, value) {
        try { window.sessionStorage.setItem(PREFIX + key, value); }
        catch (e) { /* storage unavailable (privacy mode / quota) — silently degrade */ }
    }

    // Remember what this list page is currently showing.
    function remember() {
        if (!document.body || document.body.getAttribute('data-remember-query') !== 'true') return;
        writeStored(normalizePath(window.location.pathname), window.location.search || '');
    }

    // Same-origin URL for a back link, or null.
    function linkUrl(link) {
        var href = link.getAttribute('href');
        if (!href) return null;
        var url;
        try { url = new URL(href, window.location.href); }
        catch (e) { return null; }
        return url.origin === window.location.origin ? url : null;
    }

    // Point back links at the remembered view of their target page.
    function restore() {
        var links = document.querySelectorAll('a[data-back-nav]');
        for (var i = 0; i < links.length; i++) {
            var link = links[i];
            var url = linkUrl(link);
            if (!url) continue;

            var remembered = readStored(normalizePath(url.pathname));
            if (!remembered) continue;

            // Remembered params first; anything the link itself specifies wins.
            var merged = new URLSearchParams(remembered);
            var own = new URLSearchParams(url.search);
            own.forEach(function (_, name) { merged.delete(name); });
            own.forEach(function (value, name) { merged.append(name, value); });

            var query = merged.toString();
            link.setAttribute('href', url.pathname + (query ? '?' + query : '') + url.hash);
        }
    }

    // Let this page's POST forms carry the back target's remembered query, so a
    // server-side redirect back to the list can restore the same search.
    function shareWithForms() {
        var back = document.querySelector('a[data-back-nav]');
        var url = back && linkUrl(back);
        if (!url) return;

        var remembered = readStored(normalizePath(url.pathname));
        if (!remembered) return;

        var forms = document.querySelectorAll('main form');
        for (var i = 0; i < forms.length; i++) {
            var form = forms[i];
            if ((form.getAttribute('method') || 'get').toLowerCase() !== 'post') continue;
            if (form.querySelector('input[name="' + RETURN_QUERY_FIELD + '"]')) continue;

            var input = document.createElement('input');
            input.type = 'hidden';
            input.name = RETURN_QUERY_FIELD;
            input.value = remembered;
            form.appendChild(input);
        }
    }

    remember();
    restore();
    shareWithForms();
})();
