// Sales order form product picker: the "Pull products from: <customer>" filter above the line
// items (Create and Edit). Fetches the picker list for the chosen customer (or every customer)
// and refills each line's .product-select with it, keeping whatever product the line already
// has — a saved line may point at another customer's product, and it must stay selected (and
// selectable) after a refill rather than silently dropping to "Select product".
//
// Wiring, read off #product-filter-customer (all optional):
//   data-products-url    endpoint returning [{id, internalSku, description}] (default "?handler=Products")
//   data-customer-param  query-string name for the customer filter (default "customerId")
//
// The filter follows #customer-select when the order customer changes, and rows added later
// (the form's "order-line-added" event) are filled from the last fetched list. Page-specific
// behaviour (suggested prices on Create) stays in the page's own script.
(function () {
    'use strict';

    const productFilterSelect = document.getElementById('product-filter-customer');
    if (!productFilterSelect) return;

    const customerSelect = document.getElementById('customer-select');
    const productsUrl = productFilterSelect.dataset.productsUrl || '?handler=Products';
    const customerParam = productFilterSelect.dataset.customerParam || 'customerId';

    let productsCacheKey = null; // filter key productsList was fetched for; null = nothing fetched yet
    let productsList = [];
    let requestSeq = 0;          // drops out-of-order responses when the filter changes quickly

    const getProductFilterKey = () => productFilterSelect.value || 'all';

    const buildProductsUrl = () => {
        const key = getProductFilterKey();
        if (key === 'all') return productsUrl;
        const sep = productsUrl.includes('?') ? '&' : '?';
        return `${productsUrl}${sep}${encodeURIComponent(customerParam)}=${encodeURIComponent(key)}`;
    };

    const productLabel = p => `${p.internalSku} — ${p.description}`;

    // Refill one select from productsList. The current pick survives even when the fetched list
    // doesn't contain it (its existing label is reused); disabled selects (the line has a job, so
    // the product is pinned) are left untouched so they never lose their value.
    const populateProductSelect = sel => {
        if (sel.disabled) return;
        const currentVal = sel.value;
        const currentLabel = sel.selectedOptions[0]?.textContent?.trim() ?? '';

        sel.innerHTML = '';
        const placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = 'Select product';
        sel.appendChild(placeholder);

        let found = false;
        productsList.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.id;
            opt.textContent = productLabel(p);
            if (p.id === currentVal) {
                opt.selected = true;
                found = true;
            }
            sel.appendChild(opt);
        });

        if (currentVal && !found) {
            const keep = document.createElement('option');
            keep.value = currentVal;
            keep.textContent = currentLabel || currentVal;
            keep.selected = true;
            placeholder.after(keep);
        }
    };

    const populateProductSelects = () => {
        // Until a list has been fetched, leave the server-rendered options in place.
        if (productsCacheKey === null) return;
        document.querySelectorAll('.product-select').forEach(populateProductSelect);
    };

    const loadProducts = async () => {
        const cacheKey = getProductFilterKey();
        if (productsCacheKey === cacheKey) {
            populateProductSelects();
            return;
        }

        const seq = ++requestSeq;
        let list;
        try {
            const response = await fetch(buildProductsUrl());
            if (!response.ok) return;
            list = await response.json();
        } catch {
            return;
        }
        if (seq !== requestSeq) return;

        productsList = Array.isArray(list) ? list : [];
        productsCacheKey = cacheKey;
        populateProductSelects();
    };

    const syncProductFilterToOrderCustomer = () => {
        if (!customerSelect?.value) return;
        const matchingOption = [...productFilterSelect.options].some(o => o.value === customerSelect.value);
        if (matchingOption) {
            productFilterSelect.value = customerSelect.value;
        }
    };

    customerSelect?.addEventListener('change', () => {
        syncProductFilterToOrderCustomer();
        loadProducts();
    });

    productFilterSelect.addEventListener('change', () => loadProducts());

    // Newly added line rows need their product dropdown filled from the loaded list.
    document.addEventListener('order-line-added', () => populateProductSelects());

    syncProductFilterToOrderCustomer();
    loadProducts();
})();
