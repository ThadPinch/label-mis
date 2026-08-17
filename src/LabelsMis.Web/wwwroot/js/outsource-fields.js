// Outsource fields block (Shared/_OutsourceFields): the switch shows/hides the vendor fields.
// Delegated so rows added later (line/charge templates) work too. Fires "outsource:toggle" on the
// block so page scripts (estimate pricing table, order margin readouts) can react.
(function () {
    'use strict';

    document.addEventListener('change', function (event) {
        var toggle = event.target.closest('.outsource-toggle');
        if (!toggle) return;
        var block = toggle.closest('[data-outsource-block]');
        if (!block) return;
        var fields = block.querySelector('.outsource-fields');
        if (fields) fields.classList.toggle('d-none', !toggle.checked);
        block.dispatchEvent(new CustomEvent('outsource:toggle', { bubbles: true, detail: { outsourced: toggle.checked } }));
    });
})();
