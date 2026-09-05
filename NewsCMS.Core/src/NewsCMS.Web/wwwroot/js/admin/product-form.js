/**
 * Product form — slug tự sinh, repeater ảnh và biến thể.
 * File ngoài (không inline) để form vẫn chạy khi bật CSP script-src 'self'.
 */
(function () {
    'use strict';

    const form = document.getElementById('product-form');
    if (!form) return;

    const $ = (sel, root) => (root || document).querySelector(sel);
    const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

    function parseJson(raw, fallback) {
        if (!raw) return fallback;
        try { return JSON.parse(raw); } catch { return fallback; }
    }

    function escapeAttr(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // --- Slug ---------------------------------------------------------------
    const nameInput = $('#product-name');
    const slugInput = $('#product-slug');
    const slugify = (s) => (s || '').toLowerCase()
        .normalize('NFD').replace(/[̀-ͯ]/g, '')
        .replace(/đ/g, 'd')
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '');

    if (nameInput && slugInput) {
        let lastGenerated = slugInput.value;
        nameInput.addEventListener('input', () => {
            if (!slugInput.value || slugInput.value === lastGenerated) {
                lastGenerated = slugify(nameInput.value);
                slugInput.value = lastGenerated;
            }
        });
        const regen = $('#product-slug-regen');
        if (regen) {
            regen.addEventListener('click', () => {
                lastGenerated = slugify(nameInput.value);
                slugInput.value = lastGenerated;
            });
        }
    }

    // --- Tồn kho chỉ nhập được khi bật theo dõi ------------------------------
    const trackStock = $('#product-track-stock');
    const stockQty = $('#product-stock-qty');
    if (trackStock && stockQty) {
        // readOnly chứ không disabled: input disabled không được submit, tắt theo dõi
        // tồn kho một lần là số tồn đang có bị ghi về 0.
        const syncStock = () => {
            stockQty.readOnly = !trackStock.checked;
            stockQty.classList.toggle('bg-slate-100', !trackStock.checked);
            stockQty.classList.toggle('text-slate-400', !trackStock.checked);
        };
        trackStock.addEventListener('change', syncStock);
        syncStock();
    }

    // --- Repeater ảnh -------------------------------------------------------
    const imageList = $('#product-images-list');
    const imageEmpty = $('#product-images-empty');
    const imagesJson = $('#product-images-json');
    const imageStage = $('#product-image-stage');
    const imageFile = $('#product-image-file');

    function imageRowMarkup(item) {
        const url = escapeAttr(item.url || item.Url || '');
        const alt = escapeAttr(item.altText || item.AltText || '');
        return '<div class="flex items-start gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3" data-image-row>'
            + '<img src="' + url + '" alt="" class="h-16 w-16 shrink-0 rounded-lg border border-slate-200 bg-white object-cover" />'
            + '<div class="min-w-0 flex-1 space-y-2">'
            + '<input type="text" class="admin-input font-mono text-xs" data-image-url value="' + url + '" placeholder="/uploads/images/..." />'
            + '<input type="text" class="admin-input text-xs" data-image-alt value="' + alt + '" placeholder="Mô tả ảnh (alt) — tốt cho SEO" />'
            + '</div>'
            + '<div class="flex shrink-0 flex-col gap-1">'
            + '<button type="button" class="admin-btn-secondary px-2 py-1 text-xs" data-image-up title="Lên trên">&uarr;</button>'
            + '<button type="button" class="admin-btn-secondary px-2 py-1 text-xs" data-image-down title="Xuống dưới">&darr;</button>'
            + '<button type="button" class="admin-btn-secondary px-2 py-1 text-xs text-red-600" data-image-remove title="Xoá ảnh">&times;</button>'
            + '</div></div>';
    }

    function syncImageEmpty() {
        if (!imageEmpty || !imageList) return;
        imageEmpty.classList.toggle('hidden', imageList.children.length > 0);
    }

    function addImage(item) {
        if (!imageList || !item) return;
        const url = item.url || item.Url;
        if (!url) return;
        imageList.insertAdjacentHTML('beforeend', imageRowMarkup(item));
        syncImageEmpty();
    }

    if (imageList) {
        parseJson(imageList.dataset.initial, []).forEach(addImage);
        syncImageEmpty();

        imageList.addEventListener('click', (e) => {
            const row = e.target.closest('[data-image-row]');
            if (!row) return;
            if (e.target.closest('[data-image-remove]')) {
                row.remove();
                syncImageEmpty();
            } else if (e.target.closest('[data-image-up]') && row.previousElementSibling) {
                row.parentNode.insertBefore(row, row.previousElementSibling);
            } else if (e.target.closest('[data-image-down]') && row.nextElementSibling) {
                row.parentNode.insertBefore(row.nextElementSibling, row);
            }
        });

        imageList.addEventListener('input', (e) => {
            if (e.target.matches('[data-image-url]')) {
                const img = $('img', e.target.closest('[data-image-row]'));
                if (img) img.src = e.target.value;
            }
        });
    }

    const btnLibrary = $('#product-images-library');
    if (btnLibrary && imageStage) {
        btnLibrary.addEventListener('click', () => {
            imageStage.value = '';
            if (window.mediaLibraryModal) window.mediaLibraryModal.open('product-image-stage', '', '', '');
        });
        imageStage.addEventListener('media-library:selected', (e) => {
            addImage({ url: e.detail.url, altText: '' });
        });
    }

    const btnUpload = $('#product-images-upload');
    if (btnUpload && imageFile) {
        btnUpload.addEventListener('click', () => imageFile.click());
        imageFile.addEventListener('change', (e) => {
            Array.from(e.target.files || []).forEach((file) => {
                if (!window.uploadManager) return;
                window.uploadManager.enqueue(file, {
                    onDone: (result) => {
                        if (result && result.location) addImage({ url: result.location, altText: '' });
                    }
                });
            });
            imageFile.value = '';
        });
    }

    // --- Repeater biến thể --------------------------------------------------
    const variantList = $('#product-variants-list');
    const variantEmpty = $('#product-variants-empty');
    const variantsJson = $('#product-variants-json');

    function variantRowMarkup(v) {
        const val = (x) => (x === null || x === undefined ? '' : escapeAttr(x));
        const checked = v.isActive === false ? '' : 'checked';
        return '<div class="grid gap-2 rounded-xl border border-slate-200 bg-slate-50 p-3 md:grid-cols-[1.1fr_1.4fr_1fr_1fr_.8fr_auto]" data-variant-row>'
            + '<input type="text" class="admin-input text-sm" data-variant-sku value="' + val(v.sku) + '" placeholder="SKU" />'
            + '<input type="text" class="admin-input text-sm" data-variant-name value="' + val(v.name) + '" placeholder="Tên biến thể" />'
            + '<input type="number" step="1000" min="0" class="admin-input text-sm" data-variant-price value="' + val(v.price) + '" placeholder="Giá" />'
            + '<input type="number" step="1000" min="0" class="admin-input text-sm" data-variant-sale value="' + val(v.salePrice) + '" placeholder="Giá KM" />'
            + '<input type="number" min="0" class="admin-input text-sm" data-variant-stock value="' + (val(v.stockQuantity) || 0) + '" placeholder="Tồn" />'
            + '<div class="flex items-center gap-2">'
            + '<label class="flex items-center gap-1 text-xs font-semibold text-slate-600" title="Đang bán">'
            + '<input type="checkbox" class="h-4 w-4" data-variant-active ' + checked + ' /> Bật</label>'
            + '<button type="button" class="admin-btn-secondary px-2 py-1 text-xs text-red-600" data-variant-remove title="Xoá biến thể">&times;</button>'
            + '</div></div>';
    }

    function syncVariantEmpty() {
        if (!variantEmpty || !variantList) return;
        variantEmpty.classList.toggle('hidden', variantList.children.length > 0);
    }

    function addVariant(v) {
        if (!variantList) return;
        variantList.insertAdjacentHTML('beforeend', variantRowMarkup(v || {}));
        syncVariantEmpty();
    }

    if (variantList) {
        parseJson(variantList.dataset.initial, []).forEach(addVariant);
        syncVariantEmpty();

        variantList.addEventListener('click', (e) => {
            if (e.target.closest('[data-variant-remove]')) {
                e.target.closest('[data-variant-row]').remove();
                syncVariantEmpty();
            }
        });

        const btnAddVariant = $('#product-variants-add');
        if (btnAddVariant) btnAddVariant.addEventListener('click', () => addVariant({ isActive: true }));
    }

    // --- Serialize khi submit -----------------------------------------------
    form.addEventListener('submit', () => {
        if (imagesJson && imageList) {
            const rows = $$('[data-image-row]', imageList)
                .map((row, i) => ({
                    Url: $('[data-image-url]', row).value.trim(),
                    AltText: $('[data-image-alt]', row).value.trim(),
                    SortOrder: i
                }))
                .filter((r) => r.Url);
            imagesJson.value = JSON.stringify(rows);
        }

        if (variantsJson && variantList) {
            const num = (v) => (v === '' || v === null ? null : Number(v));
            const rows = $$('[data-variant-row]', variantList)
                .map((row, i) => ({
                    Sku: $('[data-variant-sku]', row).value.trim(),
                    Name: $('[data-variant-name]', row).value.trim(),
                    Price: num($('[data-variant-price]', row).value),
                    SalePrice: num($('[data-variant-sale]', row).value),
                    StockQuantity: Number($('[data-variant-stock]', row).value || 0),
                    IsActive: $('[data-variant-active]', row).checked,
                    SortOrder: i
                }))
                .filter((r) => r.Sku || r.Name);
            variantsJson.value = JSON.stringify(rows);
        }
    });
})();
