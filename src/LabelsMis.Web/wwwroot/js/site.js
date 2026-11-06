// Shared page bootstrapping.

// Remember the browser's time zone so the server can tell "today" the way the shop does (due-date
// flags on the jobs floor board). Read by Services/ClientClock.cs.
(function () {
    'use strict';
    try {
        const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        if (zone && !document.cookie.split('; ').includes('ls_tz=' + encodeURIComponent(zone))) {
            document.cookie = 'ls_tz=' + encodeURIComponent(zone) + '; path=/; max-age=31536000; SameSite=Lax';
        }
    } catch (e) {
        // Old browser without Intl time zones: the server falls back to its own clock.
    }
})();
