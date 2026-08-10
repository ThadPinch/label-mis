window.LabelsMis = window.LabelsMis || {};

(function (LabelsMis) {
    const uploadUrl = '/artwork/upload';

    function getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function getStatusElements(container) {
        const status = container.querySelector('.artwork-upload-status');
        const bar = status?.querySelector('.progress-bar');
        const message = status?.querySelector('.artwork-upload-message');
        return { status, bar, message };
    }

    function setProgress(container, percent, message, variant) {
        const { status, bar, message: messageEl } = getStatusElements(container);
        if (!status || !bar || !messageEl) return;

        status.classList.remove('d-none');
        bar.style.width = `${Math.max(0, Math.min(100, percent))}%`;
        bar.setAttribute('aria-valuenow', String(percent));
        messageEl.textContent = message;
        messageEl.className = `small artwork-upload-message mt-1 text-${variant || 'muted'}`;
    }

    function uploadArtwork(productId, file, container, onSuccess) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            xhr.open('POST', uploadUrl);
            xhr.setRequestHeader('RequestVerificationToken', getAntiForgeryToken());

            xhr.upload.addEventListener('progress', (event) => {
                if (!event.lengthComputable) {
                    setProgress(container, 0, 'Uploading…', 'muted');
                    return;
                }

                const percent = Math.round((event.loaded / event.total) * 100);
                setProgress(container, percent, `Uploading… ${percent}%`, 'muted');
            });

            xhr.addEventListener('load', () => {
                let payload = null;
                try {
                    payload = JSON.parse(xhr.responseText || '{}');
                } catch {
                    payload = { success: false, error: 'Unexpected server response.' };
                }

                if (xhr.status >= 200 && xhr.status < 300 && payload.success) {
                    setProgress(container, 100, 'Upload complete.', 'success');
                    onSuccess?.();
                    resolve(payload);
                    return;
                }

                const error = payload.error || 'Upload failed.';
                setProgress(container, 0, error, 'danger');
                reject(new Error(error));
            });

            xhr.addEventListener('error', () => {
                const error = 'Upload failed due to a network error.';
                setProgress(container, 0, error, 'danger');
                reject(new Error(error));
            });

            const formData = new FormData();
            formData.append('productId', productId);
            formData.append('file', file);
            xhr.send(formData);
        });
    }

    LabelsMis.uploadArtwork = uploadArtwork;

    LabelsMis.initJobArtworkUpload = function initJobArtworkUpload(form) {
        if (!form) return;

        const productId = form.dataset.productId;
        const container = form.closest('.card-body') || form;
        form.addEventListener('submit', (event) => {
            event.preventDefault();
            const fileInput = form.querySelector('input[type="file"]');
            const file = fileInput?.files?.[0];
            if (!file) {
                setProgress(container, 0, 'Select a file to upload.', 'danger');
                return;
            }

            setProgress(container, 0, 'Starting upload…', 'muted');
            uploadArtwork(productId, file, container, () => {
                window.setTimeout(() => window.location.reload(), 600);
            }).catch(() => { /* message already shown */ });
        });
    };

    LabelsMis.initSalesOrderArtworkUploads = function initSalesOrderArtworkUploads(root) {
        // Delegated so line rows added after page load are covered too.
        root?.addEventListener('change', (event) => {
            const input = event.target.closest('.artwork-file-input');
            if (!input) return;

            const row = input.closest('.order-line');
            const productId = row?.querySelector('.product-select')?.value;
            const file = input.files?.[0];
            const container = row?.querySelector('.artwork-upload-cell') || row;

            if (!file) return;
            if (!productId) {
                setProgress(container, 0, 'Select a product before uploading artwork.', 'danger');
                input.value = '';
                return;
            }

            setProgress(container, 0, 'Starting upload…', 'muted');
            uploadArtwork(productId, file, container, () => {
                input.value = '';
                let link = container.querySelector('.artwork-download-link');
                if (!link) {
                    link = document.createElement('a');
                    link.className = 'small d-block mb-1 artwork-download-link';
                    link.target = '_blank';
                    link.textContent = 'Download';
                    container.insertBefore(link, container.firstChild);
                }
                link.href = `/artwork/download?productId=${encodeURIComponent(productId)}`;
            }).catch(() => { /* message already shown */ });
        });
    };
})(window.LabelsMis);
