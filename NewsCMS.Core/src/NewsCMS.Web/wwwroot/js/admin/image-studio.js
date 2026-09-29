/**
 * Xưởng ảnh AI — modal tạo ảnh dùng chung cho mọi trang admin.
 *
 *   window.imageStudio.open({
 *     purpose: 'post-cover' | 'post-inline' | 'product-main' | 'product-gallery' | 'banner' | 'social' | 'free',
 *     aspect: '16:9',                  // bỏ trống = theo mục đích / mặc định của site
 *     prompt: 'mô tả điền sẵn',
 *     context: { type: 'post' | 'product', id: '…',
 *                title, excerpt,         // bài viết → điền {chu_de}, {mo_ta}; bật "Gợi ý từ nội dung"
 *                name, short },          // sản phẩm → điền {san_pham}, {mo_ta}
 *     pickLabel: 'Dùng ảnh này',       // chữ trên nút chọn ảnh
 *     onPick: (media) => {},          // media = { id, url, alt, width, height } — ảnh đã vào thư viện
 *     onClose: (state) => {}          // state.created = số job đã tạo trong lần mở này
 *   });
 *
 * File này chỉ dựng UI + gọi /Admin/ImageStudio/Api. Mọi luật (quyền, hạn mức, model hợp lệ)
 * nằm ở server — client chỉ hiện lại lỗi server trả về.
 */
(function () {
    'use strict';

    const modal = document.getElementById('image-studio-modal');
    if (!modal) return;

    const API = '/Admin/ImageStudio/Api';
    const POLL_FAST_MS = 2000;
    const POLL_SLOW_MS = 5000;
    const SLOW_AFTER_MS = 30000;

    const $ = (id) => document.getElementById(id);
    const els = {
        form: $('isd-form'),
        prompt: $('isd-prompt'),
        promptCount: $('isd-prompt-count'),
        models: $('isd-models'),
        aspects: $('isd-aspects'),
        count: $('isd-count'),
        estimate: $('isd-estimate'),
        quota: $('isd-quota'),
        submit: $('isd-submit'),
        disabled: $('isd-disabled'),
        results: $('isd-results'),
        empty: $('isd-empty'),
        close: $('isd-close'),
        templatesWrap: $('isd-templates-wrap'),
        templates: $('isd-templates'),
        templateSearch: $('isd-template-search'),
        templateFilter: $('isd-template-filter'),
        templateClear: $('isd-template-clear'),
        placeholders: $('isd-placeholders'),
        placeholderFields: $('isd-placeholder-fields'),
        enhance: $('isd-enhance'),
        suggest: $('isd-suggest'),
        undo: $('isd-undo'),
    };

    const PURPOSES = {
        'free': 'Free',
        'post-cover': 'PostCover',
        'post-inline': 'PostInline',
        'product-main': 'ProductMain',
        'product-gallery': 'ProductGallery',
        'banner': 'Banner',
        'social': 'Social',
    };

    const STATUS = {
        queued: { label: 'Đang chờ', cls: 'bg-slate-100 text-slate-600' },
        running: { label: 'Đang tạo…', cls: 'bg-indigo-50 text-indigo-700' },
        succeeded: { label: 'Xong', cls: 'bg-emerald-50 text-emerald-700' },
        failed: { label: 'Lỗi', cls: 'bg-red-50 text-red-700' },
        canceled: { label: 'Đã huỷ', cls: 'bg-slate-100 text-slate-500' },
    };

    let options = {};
    let form = null;          // dữ liệu ?handler=Form
    let modelId = null;
    let aspect = '1:1';
    let idempotencyKey = null;
    let jobs = new Map();     // id → job json
    let order = [];           // id mới nhất đứng đầu
    let pollTimer = null;
    let pollStartedAt = 0;
    let created = 0;
    let busy = false;

    // Kho mẫu
    let templates = [];
    let template = null;          // mẫu đang chọn
    let placeholderValues = {};   // '{chu_de}' → 'Phở Hà Nội'
    let promptDirty = false;      // người dùng đã tự sửa mô tả sau khi chọn mẫu → ô chỗ giữ thôi ghi đè
    let undoPrompt = null;        // mô tả trước lần "Cải thiện"/"Gợi ý" gần nhất
    let suggestedAlt = null;      // alt AI gợi ý — dùng khi đưa ảnh vào thư viện

    // ---------- HTTP ----------
    function token() {
        return modal.querySelector('input[name="__RequestVerificationToken"]')?.value
            || document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function call(handler, method, body, query) {
        const url = API + '?handler=' + handler + (query ? '&' + query : '');
        let resp;
        try {
            resp = await fetch(url, {
                method,
                credentials: 'same-origin',
                headers: method === 'GET' ? {} : { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: method === 'GET' ? undefined : JSON.stringify(body || {}),
            });
        } catch {
            throw new Error('Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.');
        }
        const data = await resp.json().catch(() => null);
        if (!resp.ok) {
            if (data && data.error) throw new Error(data.error);
            if (resp.status === 400) throw new Error('Phiên làm việc đã hết hạn. Tải lại trang rồi thử lại.');
            if (resp.status === 401) throw new Error('Phiên đăng nhập đã hết hạn.');
            if (resp.status === 403) throw new Error('Tài khoản của bạn chưa có quyền tạo ảnh AI.');
            throw new Error('Máy chủ gặp lỗi (HTTP ' + resp.status + ').');
        }
        return data;
    }

    function newKey() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID().replace(/-/g, '');
        return Date.now().toString(36) + Math.random().toString(36).slice(2, 12);
    }

    function toast(type, message) {
        if (window.toast && window.toast[type]) window.toast[type](message);
    }

    // ---------- Form ----------
    function money(usd) {
        const n = Number(usd) || 0;
        return '$' + (n < 0.01 && n > 0 ? n.toFixed(4) : n.toFixed(2));
    }

    function selectedModel() {
        return form?.models.find((m) => m.id === modelId) || null;
    }

    function defaultAspect() {
        if (options.aspect) return options.aspect;
        switch (options.purpose) {
            case 'post-cover': return form?.coverAspect || '16:9';
            case 'product-main':
            case 'product-gallery': return form?.productAspect || '1:1';
            case 'banner': return '16:9';
            case 'social': return '1:1';
            default: return '1:1';
        }
    }

    function renderModels() {
        els.models.replaceChildren();
        if (!form || form.models.length === 0) return;

        form.models.forEach((m) => {
            const label = document.createElement('label');
            label.className = 'flex cursor-pointer gap-3 rounded-xl border border-slate-200 bg-white p-3 text-sm transition hover:border-indigo-300 has-[:checked]:border-indigo-500 has-[:checked]:ring-4 has-[:checked]:ring-indigo-100';

            const radio = document.createElement('input');
            radio.type = 'radio';
            radio.name = 'isd-model';
            radio.value = m.id;
            radio.className = 'mt-1 h-4 w-4 shrink-0 text-indigo-600';
            radio.checked = m.id === modelId;
            radio.addEventListener('change', () => { modelId = m.id; renderCounts(); renderEstimate(); });

            const text = document.createElement('span');
            text.className = 'min-w-0 space-y-0.5';
            const name = document.createElement('span');
            name.className = 'block font-semibold text-slate-900';
            name.textContent = m.name;
            const desc = document.createElement('span');
            desc.className = 'block text-xs text-slate-500';
            desc.textContent = (m.description ? m.description + ' · ' : '') + '~' + money(m.pricePerImageUsd) + '/ảnh';

            text.append(name, desc);
            label.append(radio, text);
            els.models.append(label);
        });
    }

    function renderAspects() {
        els.aspects.replaceChildren();
        (form?.aspectRatios || []).forEach((a) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = a;
            btn.className = 'rounded-lg px-3 py-1.5 text-xs font-semibold ring-1 transition '
                + (a === aspect ? 'bg-indigo-600 text-white ring-indigo-600' : 'bg-white text-slate-600 ring-slate-200 hover:ring-slate-300');
            btn.setAttribute('aria-pressed', a === aspect ? 'true' : 'false');
            btn.addEventListener('click', () => { aspect = a; renderAspects(); });
            els.aspects.append(btn);
        });
    }

    function renderCounts() {
        const max = selectedModel()?.maxVariants || 1;
        const current = Math.min(Number(els.count.value) || 2, max);
        els.count.replaceChildren();
        for (let i = 1; i <= max; i++) {
            const opt = document.createElement('option');
            opt.value = String(i);
            opt.textContent = i + ' ảnh';
            opt.selected = i === current;
            els.count.append(opt);
        }
    }

    function renderEstimate() {
        const model = selectedModel();
        const count = Number(els.count.value) || 1;
        els.estimate.textContent = model
            ? 'Chi phí ước tính: ' + count + ' × ' + money(model.pricePerImageUsd) + ' ≈ ' + money(model.pricePerImageUsd * count)
            : 'Chi phí ước tính: —';

        const parts = [];
        if (form?.monthlyRemaining != null) parts.push('Site còn ' + form.monthlyRemaining + ' lượt tháng này');
        if (form?.dailyRemaining != null) parts.push('bạn còn ' + form.dailyRemaining + ' lượt hôm nay');
        els.quota.textContent = parts.join(' · ');
    }

    function renderDisabled() {
        let reason = form?.enabled ? null : (form?.disabledReason || 'Chưa dùng được Xưởng ảnh AI.');
        if (!reason && form?.monthlyRemaining === 0) reason = 'Site đã dùng hết lượt tạo ảnh của tháng này.';
        if (!reason && form?.dailyRemaining === 0) reason = 'Bạn đã dùng hết lượt tạo ảnh hôm nay.';
        els.disabled.textContent = reason || '';
        els.disabled.classList.toggle('hidden', !reason);
        els.submit.disabled = !!reason || busy;
        els.submit.classList.toggle('opacity-60', els.submit.disabled);
    }

    async function loadForm(keepSelection) {
        form = await call('Form', 'GET');
        if (!keepSelection || !selectedModel()) {
            modelId = (form.models.find((m) => m.isDefault) || form.models[0] || {}).id || null;
            aspect = defaultAspect();
        }
        if (!idempotencyKey) idempotencyKey = form.idempotencyKey;
        renderModels();
        renderAspects();
        renderCounts();
        renderEstimate();
        renderDisabled();
    }

    // ---------- Kho mẫu ----------
    function fold(text) {
        return (text || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');
    }

    function contextValue(token) {
        const c = options.context || {};
        switch (token) {
            case '{chu_de}': return c.title || '';
            case '{san_pham}': return c.name || '';
            case '{mo_ta}': return c.excerpt || c.short || '';
            default: return '';
        }
    }

    function fill(text, values) {
        let result = text;
        Object.keys(values).forEach((token) => {
            const v = (values[token] || '').trim();
            if (v) result = result.split(token).join(v);
        });
        return result;
    }

    function setPrompt(text) {
        els.prompt.value = text;
        els.promptCount.textContent = text.length + '/4000';
    }

    async function loadTemplates() {
        try {
            const data = await call('Templates', 'GET', null, 'purpose=' + encodeURIComponent(options.purpose || 'free'));
            templates = data.templates || [];
        } catch {
            templates = [];
        }

        // Nhóm có trong kho → thêm vào bộ lọc (giữ hai lựa chọn đầu "Tất cả" / "Đang trend").
        while (els.templateFilter.options.length > 2) els.templateFilter.remove(2);
        [...new Set(templates.map((t) => t.category))].sort().forEach((c) => els.templateFilter.add(new Option(c, 'cat:' + c)));

        els.templatesWrap.classList.toggle('hidden', templates.length === 0);
        renderTemplates();
    }

    function renderTemplates() {
        const q = fold(els.templateSearch.value.trim());
        const filter = els.templateFilter.value;
        els.templates.replaceChildren();

        const visible = templates.filter((t) => {
            if (filter === 'trend' && !t.isTrend) return false;
            if (filter.startsWith('cat:') && t.category !== filter.slice(4)) return false;
            return !q || fold(t.title + ' ' + (t.description || '') + ' ' + t.category + ' ' + (t.trendName || '')).includes(q);
        });

        if (visible.length === 0) {
            const none = document.createElement('p');
            none.className = 'col-span-full py-4 text-center text-xs text-slate-500';
            none.textContent = 'Không có mẫu khớp.';
            els.templates.append(none);
            return;
        }

        visible.forEach((t) => {
            const card = document.createElement('button');
            card.type = 'button';
            card.title = t.description || t.title;
            card.className = 'group overflow-hidden rounded-xl border bg-white text-left transition hover:border-indigo-300 '
                + (template && template.id === t.id ? 'border-indigo-500 ring-4 ring-indigo-100' : 'border-slate-200');

            const media = document.createElement('div');
            media.className = 'relative aspect-[4/3] bg-gradient-to-br from-slate-100 to-slate-200';
            if (t.demoUrl) {
                const img = document.createElement('img');
                img.src = t.demoUrl;
                img.alt = '';
                img.loading = 'lazy';
                img.className = 'h-full w-full object-cover';
                media.append(img);
            } else {
                const ph = document.createElement('span');
                ph.className = 'absolute inset-0 flex items-center justify-center px-2 text-center text-[11px] text-slate-400';
                ph.textContent = 'Chưa có ảnh demo';
                media.append(ph);
            }
            if (t.isTrend) {
                const badge = document.createElement('span');
                badge.className = 'absolute left-1.5 top-1.5 rounded-full bg-rose-600 px-2 py-0.5 text-[10px] font-bold text-white';
                badge.textContent = 'Trend';
                media.append(badge);
            }

            const label = document.createElement('span');
            label.className = 'block px-2 py-1.5';
            const name = document.createElement('span');
            name.className = 'line-clamp-2 block text-xs font-semibold text-slate-900';
            name.textContent = t.title;
            const meta = document.createElement('span');
            meta.className = 'block text-[11px] text-slate-500';
            meta.textContent = t.category + ' · ' + t.aspectRatio;
            label.append(name, meta);

            card.append(media, label);
            card.addEventListener('click', () => selectTemplate(t));
            els.templates.append(card);
        });
    }

    function selectTemplate(t) {
        template = t;
        promptDirty = false;
        undoPrompt = null;
        els.undo.classList.add('hidden');
        placeholderValues = {};
        t.placeholders.forEach((p) => { placeholderValues[p.token] = contextValue(p.token); });

        if ((form?.aspectRatios || []).includes(t.aspectRatio)) {
            aspect = t.aspectRatio;
            renderAspects();
        }

        renderPlaceholderFields();
        setPrompt(fill(t.prompt, placeholderValues));
        els.templateClear.classList.remove('hidden');
        renderTemplates();

        // Còn ô trống thì đưa con trỏ vào ô đầu tiên chưa điền.
        const firstEmpty = els.placeholderFields.querySelector('input[data-empty="1"]');
        (firstEmpty || els.prompt).focus();
    }

    function renderPlaceholderFields() {
        els.placeholderFields.replaceChildren();
        const tokens = template ? template.placeholders : [];
        els.placeholders.classList.toggle('hidden', tokens.length === 0);

        tokens.forEach((p) => {
            const wrap = document.createElement('label');
            wrap.className = 'block space-y-1';
            const span = document.createElement('span');
            span.className = 'text-xs font-semibold text-slate-700';
            span.textContent = p.label;
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'admin-input py-2';
            input.maxLength = 300;
            input.value = placeholderValues[p.token] || '';
            input.dataset.empty = input.value ? '0' : '1';
            input.placeholder = p.token;
            input.addEventListener('input', () => {
                placeholderValues[p.token] = input.value;
                input.dataset.empty = input.value ? '0' : '1';
                if (!promptDirty) setPrompt(fill(template.prompt, placeholderValues));
            });
            wrap.append(span, input);
            els.placeholderFields.append(wrap);
        });
    }

    function clearTemplate() {
        template = null;
        placeholderValues = {};
        els.templateClear.classList.add('hidden');
        renderPlaceholderFields();
        renderTemplates();
        els.prompt.focus();
    }

    function contextText() {
        const c = options.context || {};
        return { title: c.title || c.name || '', excerpt: c.excerpt || c.short || '' };
    }

    async function withButton(button, busyLabel, action) {
        if (button.disabled) return;
        const original = button.innerHTML;
        button.disabled = true;
        button.textContent = busyLabel;
        try {
            await action();
        } catch (err) {
            toast('error', err.message);
        } finally {
            button.disabled = false;
            button.innerHTML = original;
        }
    }

    function enhancePrompt() {
        const current = els.prompt.value.trim();
        if (!current) {
            toast('warning', 'Hãy viết vài chữ mô tả trước, rồi mới cải thiện.');
            els.prompt.focus();
            return;
        }
        withButton(els.enhance, 'Đang viết lại…', async () => {
            const data = await call('Enhance', 'POST', { prompt: current, purpose: options.purpose || 'free' });
            undoPrompt = current;
            promptDirty = true;
            setPrompt(data.prompt);
            els.undo.classList.remove('hidden');
        });
    }

    function suggestPrompt() {
        const ctx = contextText();
        withButton(els.suggest, 'Đang đọc nội dung…', async () => {
            const data = await call('Suggest', 'POST', { purpose: options.purpose || 'free', title: ctx.title, excerpt: ctx.excerpt });
            undoPrompt = els.prompt.value;
            promptDirty = true;
            suggestedAlt = data.alt || null;
            setPrompt(data.prompt);
            els.undo.classList.remove('hidden');
        });
    }

    function undo() {
        if (undoPrompt === null) return;
        setPrompt(undoPrompt);
        undoPrompt = null;
        els.undo.classList.add('hidden');
    }

    // ---------- Kết quả ----------
    function renderResults() {
        els.results.replaceChildren();
        els.empty.classList.toggle('hidden', order.length > 0);

        order.forEach((id) => {
            const job = jobs.get(id);
            if (!job) return;

            const card = document.createElement('article');
            card.className = 'rounded-2xl border border-slate-200 bg-white p-4';

            const head = document.createElement('div');
            head.className = 'mb-3 flex items-start justify-between gap-3';
            const info = document.createElement('div');
            info.className = 'min-w-0';
            const prompt = document.createElement('p');
            prompt.className = 'line-clamp-2 text-sm font-semibold text-slate-900';
            prompt.textContent = job.prompt;
            const meta = document.createElement('p');
            meta.className = 'mt-0.5 text-xs text-slate-500';
            meta.textContent = job.modelName + ' · ' + job.aspectRatio + ' · ' + job.variantCount + ' ảnh'
                + (job.finished ? ' · ' + money(job.costUsd) : '');
            info.append(prompt, meta);

            const status = STATUS[job.status] || STATUS.queued;
            const badge = document.createElement('span');
            badge.className = 'shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold ' + status.cls;
            badge.textContent = status.label;
            head.append(info, badge);
            card.append(head);

            if (job.error) {
                const err = document.createElement('p');
                err.className = 'mb-3 rounded-xl px-3 py-2 text-xs ' + (job.status === 'failed' ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-800');
                err.textContent = job.error;
                card.append(err);
            }

            const grid = document.createElement('div');
            grid.className = 'grid grid-cols-2 gap-3';

            if (!job.finished) {
                for (let i = 0; i < job.variantCount; i++) {
                    const ph = document.createElement('div');
                    ph.className = 'aspect-square animate-pulse rounded-xl bg-slate-200';
                    grid.append(ph);
                }
                if (job.status === 'queued') {
                    const cancel = document.createElement('button');
                    cancel.type = 'button';
                    cancel.className = 'mt-3 text-xs font-semibold text-slate-500 hover:text-red-600';
                    cancel.textContent = 'Huỷ yêu cầu';
                    cancel.addEventListener('click', () => cancelJob(job.id, cancel));
                    card.append(grid, cancel);
                } else {
                    card.append(grid);
                }
            } else {
                job.outputs.forEach((o) => grid.append(outputTile(o)));
                if (job.outputs.length) card.append(grid);
            }

            els.results.append(card);
        });
    }

    function outputTile(o) {
        const tile = document.createElement('figure');
        tile.className = 'group relative overflow-hidden rounded-xl border border-slate-200 bg-slate-100';

        if (o.purged || !o.url) {
            tile.className += ' flex aspect-square items-center justify-center text-xs text-slate-400';
            tile.textContent = 'Ảnh đã được dọn';
            return tile;
        }

        const img = document.createElement('img');
        img.src = o.url;
        img.alt = '';
        img.loading = 'lazy';
        img.className = 'block h-auto w-full';
        if (o.width && o.height) { img.width = o.width; img.height = o.height; }

        const bar = document.createElement('figcaption');
        bar.className = 'flex items-center justify-between gap-2 border-t border-slate-200 bg-white px-2 py-2';

        const view = document.createElement('a');
        view.href = o.url;
        view.target = '_blank';
        view.rel = 'noopener';
        view.className = 'text-xs font-semibold text-slate-500 hover:text-slate-900';
        view.textContent = 'Xem lớn';

        const pick = document.createElement('button');
        pick.type = 'button';
        pick.className = 'rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-indigo-700';
        pick.textContent = o.promotedMediaId && !options.onPick ? 'Đã lưu vào thư viện' : (options.pickLabel || 'Dùng ảnh này');
        pick.disabled = !!o.promotedMediaId && !options.onPick;
        if (pick.disabled) pick.className = 'rounded-lg bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700';
        pick.addEventListener('click', () => pickOutput(o, pick));

        bar.append(view, pick);
        tile.append(img, bar);
        return tile;
    }

    // ---------- Hành động ----------
    async function submit(e) {
        e.preventDefault();
        if (busy || !form?.enabled) return;

        const prompt = els.prompt.value.trim();
        if (!prompt) {
            toast('warning', 'Hãy mô tả ảnh cần tạo.');
            els.prompt.focus();
            return;
        }
        if (!modelId) {
            toast('warning', 'Chưa có model nào để chọn.');
            return;
        }

        busy = true;
        renderDisabled();
        try {
            const job = await call('Create', 'POST', {
                idempotencyKey,
                modelId,
                prompt,
                aspectRatio: aspect,
                variantCount: Number(els.count.value) || 1,
                purpose: PURPOSES[options.purpose] || 'Free',
                templateId: template ? template.id : null,
                contextType: options.context?.type || null,
                contextId: options.context?.id || null,
            });
            if (!jobs.has(job.id)) {
                order.unshift(job.id);
                created++;
            }
            jobs.set(job.id, job);
            idempotencyKey = newKey(); // lần gửi sau là yêu cầu mới
            renderResults();
            startPolling();
            loadForm(true).catch(() => {});
        } catch (err) {
            // Giữ nguyên khoá chống trùng: sửa lỗi rồi gửi lại vẫn là CÙNG một yêu cầu.
            toast('error', err.message);
        } finally {
            busy = false;
            renderDisabled();
        }
    }

    async function pickOutput(output, button) {
        if (button.dataset.busy) return;
        button.dataset.busy = '1';
        const original = button.textContent;
        button.textContent = 'Đang lưu…';
        try {
            const media = await call('Promote', 'POST', { outputId: output.id, altText: suggestedAlt });
            output.promotedMediaId = media.id;
            if (typeof options.onPick === 'function') {
                options.onPick(media);
                toast('success', 'Đã thêm ảnh vào thư viện media.');
                modal.close();
            } else {
                toast('success', 'Đã lưu ảnh vào thư viện media (thư mục "Ảnh AI").');
                renderResults();
            }
        } catch (err) {
            toast('error', err.message);
            button.textContent = original;
        } finally {
            delete button.dataset.busy;
        }
    }

    async function cancelJob(id, button) {
        button.disabled = true;
        try {
            await call('Cancel', 'POST', { jobId: id });
            await poll();
            loadForm(true).catch(() => {});
        } catch (err) {
            toast('error', err.message);
            button.disabled = false;
        }
    }

    function startPolling() {
        if (pollTimer) return;
        pollStartedAt = Date.now();
        schedulePoll();
    }

    function schedulePoll() {
        const delay = Date.now() - pollStartedAt > SLOW_AFTER_MS ? POLL_SLOW_MS : POLL_FAST_MS;
        pollTimer = setTimeout(async () => {
            pollTimer = null;
            const pending = await poll();
            if (pending > 0 && modal.open) schedulePoll();
        }, delay);
    }

    async function poll() {
        const pendingIds = order.filter((id) => !jobs.get(id)?.finished);
        if (pendingIds.length === 0) return 0;
        try {
            const data = await call('Status', 'GET', null, 'ids=' + pendingIds.join(','));
            let justFinished = false;
            data.jobs.forEach((j) => {
                if (j.finished && !jobs.get(j.id)?.finished) justFinished = true;
                jobs.set(j.id, j);
            });
            renderResults();
            if (justFinished) loadForm(true).catch(() => {});
        } catch {
            // Lỗi mạng thoáng qua: lần hỏi sau thử lại.
        }
        return order.filter((id) => !jobs.get(id)?.finished).length;
    }

    // ---------- Mở / đóng ----------
    async function open(opts) {
        options = opts || {};
        jobs = new Map();
        order = [];
        created = 0;
        idempotencyKey = null;
        template = null;
        placeholderValues = {};
        promptDirty = false;
        undoPrompt = null;
        suggestedAlt = null;
        els.templateSearch.value = '';
        els.templateFilter.value = '';
        els.templateClear.classList.add('hidden');
        els.undo.classList.add('hidden');
        renderPlaceholderFields();
        setPrompt(options.prompt || '');
        const ctx = contextText();
        els.suggest.classList.toggle('hidden', !(ctx.title || ctx.excerpt));
        renderResults();

        if (!modal.open) modal.showModal();
        const templatesLoaded = loadTemplates();
        try {
            await loadForm(false);
        } catch (err) {
            form = { enabled: false, disabledReason: err.message, models: [], aspectRatios: [] };
            renderModels();
            renderAspects();
            renderDisabled();
        }
        await templatesLoaded;
        els.prompt.focus();
    }

    els.form.addEventListener('submit', submit);
    els.count.addEventListener('change', renderEstimate);
    els.prompt.addEventListener('input', () => {
        els.promptCount.textContent = els.prompt.value.length + '/4000';
        // Người dùng tự sửa mô tả: ô chỗ giữ không được ghi đè công của họ nữa.
        if (template) promptDirty = true;
    });
    els.templateSearch.addEventListener('input', renderTemplates);
    els.templateFilter.addEventListener('change', renderTemplates);
    els.templateClear.addEventListener('click', clearTemplate);
    els.enhance.addEventListener('click', enhancePrompt);
    els.suggest.addEventListener('click', suggestPrompt);
    els.undo.addEventListener('click', undo);
    els.close.addEventListener('click', () => modal.close());
    modal.addEventListener('click', (e) => { if (e.target === modal) modal.close(); });
    modal.addEventListener('close', () => {
        if (pollTimer) { clearTimeout(pollTimer); pollTimer = null; }
        if (typeof options.onClose === 'function') options.onClose({ created });
    });

    // Nút bất kỳ có data-image-studio-open mở modal với các data-* làm tuỳ chọn.
    document.addEventListener('click', (e) => {
        const trigger = e.target.closest('[data-image-studio-open]');
        if (!trigger) return;
        e.preventDefault();
        open({
            purpose: trigger.dataset.purpose || 'free',
            aspect: trigger.dataset.aspect || null,
            prompt: trigger.dataset.prompt || '',
            onClose: trigger.dataset.reloadOnClose !== undefined
                ? (state) => { if (state.created > 0) window.location.reload(); }
                : null,
        });
    });

    window.imageStudio = { open };
})();
