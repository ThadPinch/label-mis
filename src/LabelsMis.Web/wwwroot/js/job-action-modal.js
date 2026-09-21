// Job action popup: any [data-action-panel] button fetches its panel partial (the stage's
// counts/roll/finishing-task form) into the shared #job-action-modal shell and opens it.
// returnUrl inputs are filled with the caller's current URL so the post lands back on the
// same filtered/sorted/paged list.
//
// Forms inside the panel marked [data-modal-inline] (the per-task "Complete" forms on the
// finishing stage) are posted with fetch instead of a page-level submit: the handler answers
// a fetch (X-Requested-With: fetch) with the refreshed panel, which replaces the popup body,
// so the operator can complete every task in one visit. Anything recorded that way marks the
// popup dirty and the list reloads when it closes, so the row/counts catch up.
(function () {
    'use strict';

    const fetchHeaders = { 'X-Requested-With': 'fetch' };

    function fillReturnUrls(content) {
        content.querySelectorAll('input[name="returnUrl"]').forEach(input => {
            input.value = window.location.pathname + window.location.search;
        });
    }

    function focusFirstField(modalElement, content, immediate) {
        const focusTarget = content.querySelector('input[type="number"], input[type="text"], input:not([type="hidden"]), .modal-footer .btn-primary');
        if (!focusTarget) {
            return;
        }
        if (immediate) {
            focusTarget.focus();
        } else {
            modalElement.addEventListener('shown.bs.modal', () => focusTarget.focus(), { once: true });
        }
    }

    // Surface a transport-level failure inside the popup (service errors come back rendered
    // in the panel itself), replacing any earlier one.
    function showInlineError(content, message) {
        const body = content.querySelector('.modal-body') || content;
        let alert = body.querySelector('[data-inline-error]');
        if (!alert) {
            alert = document.createElement('div');
            alert.className = 'alert alert-danger py-2 mb-2';
            alert.setAttribute('role', 'alert');
            alert.setAttribute('data-inline-error', '');
            body.prepend(alert);
        }
        alert.textContent = message;
    }

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
            const response = await fetch(trigger.dataset.actionPanel, { headers: fetchHeaders });
            if (!response.ok) {
                throw new Error(`Panel request failed (${response.status})`);
            }

            content.innerHTML = await response.text();
            fillReturnUrls(content);
            delete content.dataset.dirty;

            const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
            modal.show();
            focusFirstField(modalElement, content, false);
        } catch (error) {
            console.error('Job action panel failed to load', error);
            window.alert('Could not load the action form. Refresh the page and try again.');
        } finally {
            trigger.disabled = false;
        }
    });

    document.addEventListener('submit', async event => {
        const form = event.target.closest('form[data-modal-inline]');
        if (!form) {
            return;
        }

        event.preventDefault();
        if (form.dataset.busy) {
            return; // already in flight (Enter mashed, double click)
        }

        const modalElement = document.getElementById('job-action-modal');
        const content = document.getElementById('job-action-modal-content');
        if (!modalElement || !content) {
            return;
        }

        const buttons = form.querySelectorAll('button[type="submit"]');
        const body = new FormData(form); // snapshot before disabling anything
        form.dataset.busy = 'true';
        buttons.forEach(button => { button.disabled = true; });

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body,
                headers: fetchHeaders,
                credentials: 'same-origin'
            });

            if (response.redirected) {
                // The server sent us somewhere else (e.g. the sign-in page) — follow it for real.
                window.location.assign(response.url);
                return;
            }
            if (!response.ok) {
                throw new Error(`Request failed (${response.status})`);
            }

            content.innerHTML = await response.text();
            fillReturnUrls(content);
            content.dataset.dirty = 'true';
            focusFirstField(modalElement, content, true);
        } catch (error) {
            console.error('Job action submit failed', error);
            showInlineError(content, 'Could not save. Check your connection and try again.');
            delete form.dataset.busy;
            buttons.forEach(button => { button.disabled = false; });
        }
    });

    // Something was recorded in place: the list behind the popup is stale, so reload it once
    // the popup is gone (the job may have left this stage entirely).
    document.addEventListener('hidden.bs.modal', event => {
        if (event.target.id !== 'job-action-modal') {
            return;
        }
        const content = document.getElementById('job-action-modal-content');
        if (content && content.dataset.dirty) {
            delete content.dataset.dirty;
            window.location.reload();
        }
    });
})();
