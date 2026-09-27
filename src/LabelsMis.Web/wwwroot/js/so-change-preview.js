// Confirm-before-save for an unlocked (in-production) sales order. The edit form's Save submit is
// intercepted, the same form data is posted to ?handler=PreviewChanges (a read-only dry run of
// the save), and the resulting change list — quantity/spec edits per line, the job each new line
// will get, warnings for jobs already on the floor, and the rules the save would refuse — is shown
// in #so-change-preview-modal. "Save changes" then submits the original form with its original
// Save handler; the data-confirmed flag lets that second submit through the interceptor.
//
// Only active when the modal is on the page (Edit renders it for unlocked orders only). With no
// changes detected the form posts straight through; a preview failure is shown in the modal and
// the user can still cancel — the real save runs its own validation, so nothing is lost.
(function () {
    'use strict';

    const FORM_ID = 'sales-order-form';
    const MODAL_ID = 'so-change-preview-modal';
    const PREVIEW_HANDLER = 'PreviewChanges';

    function previewUrl(form) {
        const url = new URL(form.getAttribute('action') || window.location.href, window.location.href);
        url.searchParams.set('handler', PREVIEW_HANDLER);
        return url.toString();
    }

    function iconFor(item) {
        if (item.error) return '⛔';
        if (item.warning) return '⚠';
        if (item.kind === 'NewJob') return '＋';
        if (item.kind === 'RemovedLine') return '−';
        return '•';
    }

    function classFor(item) {
        if (item.error) return 'text-danger fw-semibold';
        if (item.warning) return 'text-warning fw-semibold';
        return '';
    }

    function renderItems(list, items) {
        list.replaceChildren();
        let currentGroup = null;
        let groupList = null;
        for (const item of items) {
            const groupKey = item.label || '';
            if (groupKey !== currentGroup) {
                currentGroup = groupKey;
                const heading = document.createElement('div');
                heading.className = 'fw-semibold small text-muted mt-2';
                heading.textContent = groupKey;
                list.appendChild(heading);
                groupList = document.createElement('ul');
                groupList.className = 'list-unstyled mb-0 ps-2';
                list.appendChild(groupList);
            }
            const row = document.createElement('li');
            row.className = classFor(item);
            row.dataset.kind = item.kind;
            const icon = document.createElement('span');
            icon.className = 'me-1';
            icon.setAttribute('aria-hidden', 'true');
            icon.textContent = iconFor(item);
            row.appendChild(icon);
            row.appendChild(document.createTextNode(item.text));
            groupList.appendChild(row);
        }
    }

    function showError(modalElement, message) {
        const error = modalElement.querySelector('[data-preview-error]');
        error.textContent = message;
        error.classList.remove('d-none');
    }

    function resetModal(modalElement) {
        modalElement.querySelector('[data-preview-error]').classList.add('d-none');
        modalElement.querySelector('[data-preview-items]').replaceChildren();
        modalElement.querySelector('[data-preview-blocked]').classList.add('d-none');
        const confirmButton = modalElement.querySelector('[data-preview-confirm]');
        confirmButton.disabled = false;
    }

    function submitForReal(form) {
        form.dataset.confirmed = 'true';
        form.dataset.submitting = 'true';
        // requestSubmit keeps the submit event (validation, other listeners); the interceptor
        // sees the flag and stands aside.
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            form.submit();
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById(FORM_ID);
        const modalElement = document.getElementById(MODAL_ID);
        if (!form || !modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
        const confirmButton = modalElement.querySelector('[data-preview-confirm]');
        const submitButtons = Array.from(document.querySelectorAll(`button[type="submit"][form="${FORM_ID}"], #${FORM_ID} button[type="submit"]`));

        function setBusy(busy) {
            submitButtons.forEach(button => { button.disabled = busy; });
        }

        confirmButton.addEventListener('click', () => {
            confirmButton.disabled = true;
            modal.hide();
            submitForReal(form);
        });

        modalElement.addEventListener('hidden.bs.modal', () => {
            if (!form.dataset.submitting) {
                setBusy(false);
            }
        });

        form.addEventListener('submit', async event => {
            if (form.dataset.confirmed) {
                delete form.dataset.confirmed;
                return; // the user confirmed in the modal — let the real Save post go
            }

            event.preventDefault();
            if (form.dataset.previewing) {
                return;
            }

            form.dataset.previewing = 'true';
            setBusy(true);
            // Carries __RequestVerificationToken and every field; artwork files are dropped so the
            // dry run doesn't upload them (the real save still does).
            const body = new FormData(form);
            for (const [name, value] of Array.from(body.entries())) {
                if (value instanceof File) {
                    body.delete(name);
                }
            }

            try {
                const response = await fetch(previewUrl(form), {
                    method: 'POST',
                    body,
                    headers: { 'X-Requested-With': 'fetch' },
                    credentials: 'same-origin'
                });

                if (response.redirected) {
                    window.location.assign(response.url); // signed out mid-edit
                    return;
                }

                let payload = null;
                try {
                    payload = await response.json();
                } catch {
                    payload = null;
                }

                resetModal(modalElement);
                if (!response.ok || !payload) {
                    showError(modalElement, (payload && payload.error) || `Could not preview the changes (${response.status}). You can cancel and try again.`);
                    confirmButton.disabled = true;
                    modal.show();
                    return;
                }

                if (!payload.hasChanges) {
                    submitForReal(form);
                    return;
                }

                const items = Array.isArray(payload.items) ? payload.items : [];
                renderItems(modalElement.querySelector('[data-preview-items]'), items);
                const blocked = items.some(item => item.error);
                if (blocked) {
                    modalElement.querySelector('[data-preview-blocked]').classList.remove('d-none');
                    confirmButton.disabled = true;
                }
                modal.show();
            } catch (error) {
                console.error('Change preview failed', error);
                resetModal(modalElement);
                showError(modalElement, 'Could not preview the changes. Check your connection; you can cancel and try again.');
                confirmButton.disabled = true;
                modal.show();
            } finally {
                delete form.dataset.previewing;
            }
        });
    });
})();
