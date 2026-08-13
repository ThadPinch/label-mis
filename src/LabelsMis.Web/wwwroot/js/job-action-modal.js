// Job action popup: any [data-action-panel] button fetches its panel partial (the stage's
// counts/roll/finishing-task form) into the shared #job-action-modal shell and opens it.
// returnUrl inputs are filled with the caller's current URL so the post lands back on the
// same filtered/sorted/paged list.
(function () {
    'use strict';

    document.addEventListener('click', async event => {
        const trigger = event.target.closest('[data-action-panel]');
        if (!trigger) {
            return;
        }

        event.preventDefault();
        const modalElement = document.getElementById('job-action-modal');
        const content = document.getElementById('job-action-modal-content');
        if (!modalElement || !content) {
            return;
        }

        trigger.disabled = true;
        try {
            const response = await fetch(trigger.dataset.actionPanel, { headers: { 'X-Requested-With': 'fetch' } });
            if (!response.ok) {
                throw new Error(`Panel request failed (${response.status})`);
            }

            content.innerHTML = await response.text();
            content.querySelectorAll('input[name="returnUrl"]').forEach(input => {
                input.value = window.location.pathname + window.location.search;
            });

            const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
            modal.show();
            const focusTarget = content.querySelector('input[type="number"], input[type="text"], input:not([type="hidden"])');
            if (focusTarget) {
                modalElement.addEventListener('shown.bs.modal', () => focusTarget.focus(), { once: true });
            }
        } catch (error) {
            console.error('Job action panel failed to load', error);
            window.alert('Could not load the action form. Refresh the page and try again.');
        } finally {
            trigger.disabled = false;
        }
    });
})();
