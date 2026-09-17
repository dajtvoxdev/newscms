/**
 * AI Assist — trợ lý AI cho các form trong admin.
 *
 * Nạp danh sách kỹ năng từ /Admin/Ai/Skills?target=... rồi mở modal xem trước,
 * gọi /Admin/Ai/Generate và chèn kết quả vào ô nhập hoặc editor.
 *
 * File này CHỈ dựng nút cho input/textarea thường (data-ai-field).
 * Với TinyMCE, partial _TinyMce.cshtml tự đăng ký menubutton "aiAssist" ngay
 * trong setup() — đăng ký ở cả hai nơi từng làm nút AI mất icon vì bản đăng ký
 * sau (không icon) ghi đè bản trước.
 */
(function () {
    'use strict';

    // ---------- Icon (lucide, stroke = currentColor) ----------
    function svg(paths) {
        return '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"'
            + ' fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"'
            + ' stroke-linejoin="round">' + paths + '</svg>';
    }

    const ICON_SPARKLES = svg('<path d="m12 3-1.9 5.8L4.3 10.7l5.8 1.9L12 18.4l1.9-5.8 5.8-1.9-5.8-1.9Z"/>'
        + '<path d="M5 3v3"/><path d="M3.5 4.5h3"/><path d="M18 16v3"/><path d="M16.5 17.5h3"/>');

    const ICON_DOC_PEN = svg('<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h6"/><path d="M14 2v6h6"/>'
        + '<path d="M8 12h5"/><path d="M8 16h3"/><path d="m18.4 13.6 2 2L16 20l-2.6.6.6-2.6Z"/>');

    const ICON_TEXT = svg('<path d="M4 6h16"/><path d="M4 11h16"/><path d="M4 16h9"/>');

    const ICON_BULB = svg('<path d="M9 18h6"/><path d="M10 22h4"/>'
        + '<path d="M15.1 14a5 5 0 1 0-6.2 0c.6.5 1.1 1.2 1.1 2h4c0-.8.5-1.5 1.1-2Z"/>');

    const ICON_PEN = svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>');

    const ICON_CHART = svg('<path d="M3 3v18h18"/><path d="m7 15 3-4 3 3 4-6"/>');

    /// Icon theo AiTaskKeys (NewsCMS.Domain/Entities/Ai/AiEnums.cs).
    const SKILL_ICONS = {
        generate_body: ICON_DOC_PEN,
        summarize: ICON_TEXT,
        suggest_title: ICON_BULB,
        rewrite: ICON_PEN,
        keobia_expert_analysis: ICON_CHART
    };

    function iconFor(skillKey) {
        return SKILL_ICONS[skillKey] || ICON_SPARKLES;
    }

    // ---------- Gọi API ----------
    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function httpMessage(status) {
        if (status === 400) return 'Yêu cầu không hợp lệ hoặc phiên làm việc đã hết hạn.';
        if (status === 401) return 'Phiên đăng nhập đã hết hạn.';
        if (status === 403) return 'Tài khoản của bạn chưa có quyền dùng trợ lý AI.';
        if (status === 404) return 'Không tìm thấy dịch vụ AI trên máy chủ.';
        if (status === 429) return 'Nhà cung cấp AI đang giới hạn tốc độ.';
        if (status >= 500) return 'Máy chủ gặp lỗi khi tạo nội dung.';
        return 'Lỗi không xác định (HTTP ' + status + ').';
    }

    function hintFor(status) {
        if (status === 400 || status === 401) return 'Hãy tải lại trang rồi thử lại.';
        if (status === 403) return 'Liên hệ quản trị viên để được cấp quyền "Ai.Assist.Use".';
        if (status === 404) return 'Có thể tính năng AI chưa được bật cho site này.';
        if (status === 429) return 'Thử lại sau vài phút.';
        if (status >= 500) return 'Kiểm tra cấu hình kết nối AI trong mục Cấu hình AI.';
        return '';
    }

    const skillCache = new Map();

    function loadSkills(target) {
        if (skillCache.has(target)) return skillCache.get(target);

        const promise = fetch('/Admin/Ai/Skills?target=' + encodeURIComponent(target), {
            credentials: 'same-origin'
        }).then(function (resp) {
            return resp.ok ? resp.json() : [];
        }).then(function (data) {
            return Array.isArray(data) ? data : [];
        }).catch(function () {
            return [];
        });

        skillCache.set(target, promise);
        return promise;
    }

    async function generate(skillKey, style, context) {
        let resp;
        try {
            resp = await fetch('/Admin/Ai/Generate', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    // Tên header phải khớp AddAntiforgery(o => o.HeaderName) trong
                    // Program.cs. Lệch tên thì antiforgery chặn với 400 body rỗng —
                    // đúng triệu chứng "lỗi 400 không xác định" trước đây.
                    'RequestVerificationToken': getToken()
                },
                body: JSON.stringify({
                    skillKey: skillKey,
                    style: style,
                    title: context.title || '',
                    content: context.content || '',
                    selection: context.selection || '',
                    language: context.language || 'vi'
                })
            });
        } catch {
            const err = new Error('Không kết nối được máy chủ.');
            err.hint = 'Kiểm tra kết nối mạng rồi thử lại.';
            throw err;
        }

        if (resp.ok) return await resp.json();

        // Lỗi nghiệp vụ trả JSON { error }; lỗi hạ tầng (antiforgery, 401) body rỗng.
        const data = await resp.json().catch(() => null);
        const err = new Error(data?.error || httpMessage(resp.status));
        err.hint = data?.error ? '' : hintFor(resp.status);
        throw err;
    }

    // ---------- Modal xem trước ----------
    function buildModal(skill) {
        document.getElementById('ai-assist-modal')?.remove();

        const modal = document.createElement('dialog');
        modal.id = 'ai-assist-modal';
        modal.className = 'w-full max-w-3xl rounded-2xl border border-slate-200 bg-white p-0 shadow-2xl backdrop:bg-black/50';
        modal.innerHTML = `
            <div class="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-4">
                <div class="flex items-start gap-3">
                    <span class="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-indigo-50 text-indigo-600">${iconFor(skill.key)}</span>
                    <div>
                        <h2 class="text-lg font-bold text-slate-950">${escapeHtml(skill.name || 'Trợ lý AI')}</h2>
                        <p class="mt-0.5 text-sm text-slate-500">${escapeHtml(skill.description || 'Nội dung do AI tạo, hãy đọc lại trước khi dùng.')}</p>
                    </div>
                </div>
                <button type="button" data-ai-close aria-label="Đóng"
                        class="inline-flex rounded-xl p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700">
                    ${svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>')}
                </button>
            </div>

            <div class="max-h-[60vh] overflow-y-auto px-6 py-5">
                <div id="ai-modal-loading" class="flex flex-col items-center justify-center gap-3 py-12 text-center">
                    <span class="h-10 w-10 animate-spin rounded-full border-4 border-indigo-100 border-t-indigo-600"></span>
                    <p class="text-sm font-semibold text-slate-700">AI đang viết nội dung…</p>
                    <p class="text-xs text-slate-400">Thường mất 5–30 giây <span id="ai-modal-elapsed"></span></p>
                </div>

                <div id="ai-modal-preview" class="hidden">
                    <p class="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Xem trước</p>
                    <div id="ai-modal-render"
                         class="min-h-[160px] rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm leading-relaxed text-slate-800"></div>
                    <details class="mt-4 rounded-xl border border-slate-200 px-4 py-3">
                        <summary class="cursor-pointer text-sm font-semibold text-slate-600">Chỉnh sửa HTML trước khi chèn</summary>
                        <textarea id="ai-modal-raw" rows="8" spellcheck="false"
                                  class="admin-input mt-3 font-mono text-xs"></textarea>
                    </details>
                </div>

                <div id="ai-modal-error" class="hidden py-10 text-center">
                    <span class="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-red-50 text-red-500">
                        ${svg('<circle cx="12" cy="12" r="10"/><path d="M12 8v5"/><path d="M12 16h.01"/>')}
                    </span>
                    <p id="ai-modal-error-text" class="text-sm font-semibold text-slate-800"></p>
                    <p id="ai-modal-error-hint" class="mt-1 text-xs text-slate-500"></p>
                </div>
            </div>

            <div class="flex flex-wrap items-center gap-3 border-t border-slate-200 bg-slate-50 px-6 py-4">
                <label id="ai-modal-style-wrap" class="hidden items-center gap-2 text-sm text-slate-600">
                    <span class="font-semibold">Kiểu trình bày</span>
                    <select id="ai-modal-style" class="admin-select w-56">
                        <option value="0">Văn bản thuần</option>
                        <option value="1">Trình bày đẹp (có định dạng)</option>
                    </select>
                </label>
                <div class="ml-auto flex items-center gap-2">
                    <button type="button" data-ai-close class="admin-btn-secondary">Huỷ</button>
                    <button type="button" id="ai-btn-regenerate" class="admin-btn-secondary hidden">Tạo lại</button>
                    <button type="button" id="ai-btn-apply" class="admin-btn-primary hidden">Chèn vào nội dung</button>
                </div>
            </div>
        `;

        document.body.appendChild(modal);
        modal.querySelectorAll('[data-ai-close]').forEach(function (btn) {
            btn.addEventListener('click', function () { modal.close(); });
        });
        // Click ra nền ngoài cũng đóng — <dialog> không tự làm việc này.
        modal.addEventListener('click', function (e) { if (e.target === modal) modal.close(); });
        modal.addEventListener('close', function () { modal.remove(); });

        // showModal() đẩy modal lên top layer nên vẫn hiện đúng khi TinyMCE
        // đang bật toàn màn hình (Fullscreen API).
        modal.showModal();
        return modal;
    }

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"]/g, function (ch) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[ch];
        });
    }

    /**
     * Mở modal, gọi AI rồi trả nội dung đã duyệt qua onApply.
     * @param skill {key, name, description, allowStyled} — chấp nhận cả chữ ký cũ
     *        run(skillKey, allowStyled, context, onApply).
     */
    function runAiAssist(skill, context, onApply) {
        if (typeof skill === 'string') {
            const args = Array.prototype.slice.call(arguments);
            skill = { key: args[0], name: args[0], allowStyled: !!args[1] };
            context = args[2];
            onApply = args[3];
        }

        const modal = buildModal(skill);
        const loading = modal.querySelector('#ai-modal-loading');
        const elapsedEl = modal.querySelector('#ai-modal-elapsed');
        const preview = modal.querySelector('#ai-modal-preview');
        const errorBox = modal.querySelector('#ai-modal-error');
        const errorText = modal.querySelector('#ai-modal-error-text');
        const errorHint = modal.querySelector('#ai-modal-error-hint');
        const renderDiv = modal.querySelector('#ai-modal-render');
        const rawTextarea = modal.querySelector('#ai-modal-raw');
        const styleWrap = modal.querySelector('#ai-modal-style-wrap');
        const styleSelect = modal.querySelector('#ai-modal-style');
        const btnRegenerate = modal.querySelector('#ai-btn-regenerate');
        const btnApply = modal.querySelector('#ai-btn-apply');

        if (skill.allowStyled) {
            styleWrap.classList.remove('hidden');
            styleWrap.classList.add('flex');
            styleSelect.addEventListener('change', function () { doGenerate(); });
        }

        let timer = null;
        modal.addEventListener('close', function () { clearInterval(timer); });

        function show(el) { el.classList.remove('hidden'); }
        function hide(el) { el.classList.add('hidden'); }

        async function doGenerate() {
            show(loading);
            hide(preview);
            hide(errorBox);
            hide(btnRegenerate);
            hide(btnApply);

            const startedAt = Date.now();
            elapsedEl.textContent = '';
            clearInterval(timer);
            timer = setInterval(function () {
                elapsedEl.textContent = '· đã chờ ' + Math.round((Date.now() - startedAt) / 1000) + 's';
            }, 1000);

            const style = skill.allowStyled ? parseInt(styleSelect.value, 10) : 0;

            try {
                const result = await generate(skill.key, style, context);
                // content đã được server làm sạch (an toàn cho innerHTML);
                // raw là đầu ra gốc của AI để người dùng chỉnh tay.
                const raw = result.raw || result.content || '';
                renderDiv.innerHTML = result.content || '';
                rawTextarea.value = raw;
                hide(loading);
                show(preview);
                show(btnRegenerate);
                show(btnApply);
            } catch (err) {
                hide(loading);
                errorText.textContent = err.message;
                errorHint.textContent = err.hint || '';
                show(errorBox);
                show(btnRegenerate);
            } finally {
                clearInterval(timer);
            }
        }

        btnRegenerate.addEventListener('click', doGenerate);
        btnApply.addEventListener('click', function () {
            const raw = rawTextarea.value;
            modal.close();
            onApply(raw);
            window.toast?.success('Đã chèn nội dung do AI tạo.');
        });

        doGenerate();
    }

    // ---------- Nút AI cạnh input / textarea (data-ai-field) ----------
    function addFieldAiButton(inputEl, target) {
        loadSkills(target).then(function (skills) {
            if (!skills.length) return;

            // Wrapper phải là block full-width: 'inline-block' làm ô input có
            // width:100% bị co lại bằng kích thước mặc định (~20 ký tự) và vỡ layout.
            const isMultiline = inputEl.tagName === 'TEXTAREA';
            const wrapper = document.createElement('div');
            wrapper.className = 'relative block w-full';
            inputEl.parentNode.insertBefore(wrapper, inputEl);
            wrapper.appendChild(inputEl);

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.title = 'Tạo nội dung bằng AI';
            btn.setAttribute('aria-label', 'Tạo nội dung bằng AI');
            btn.className = (isMultiline ? 'absolute right-2 top-2' : 'absolute right-2 top-1/2 -translate-y-1/2')
                + ' inline-flex items-center gap-1 rounded-lg bg-indigo-50 px-2 py-1 text-xs font-semibold text-indigo-700 transition hover:bg-indigo-100';
            btn.innerHTML = '<span class="inline-flex h-4 w-4 items-center justify-center">' + ICON_SPARKLES + '</span><span>AI</span>';
            wrapper.appendChild(btn);

            // Chừa chỗ cho nút AI để chữ không chui xuống dưới nút.
            inputEl.style.paddingRight = '4.5rem';

            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                document.querySelector('.ai-field-dropdown')?.remove();

                const dropdown = document.createElement('div');
                dropdown.className = 'ai-field-dropdown absolute right-0 top-full z-50 mt-1 w-64 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-xl';
                skills.forEach(function (s) {
                    const item = document.createElement('button');
                    item.type = 'button';
                    item.className = 'flex w-full items-start gap-2 px-3 py-2 text-left transition hover:bg-slate-50';
                    item.innerHTML = '<span class="mt-0.5 inline-flex h-4 w-4 shrink-0 items-center justify-center text-indigo-600">'
                        + iconFor(s.key) + '</span>'
                        + '<span class="min-w-0"><span class="block text-sm font-semibold text-slate-800">' + escapeHtml(s.name) + '</span>'
                        + (s.description ? '<span class="block text-xs text-slate-500">' + escapeHtml(s.description) + '</span>' : '')
                        + '</span>';
                    item.addEventListener('click', function () {
                        dropdown.remove();
                        const titleEl = document.querySelector('input[name*="Title"], input[name*="Name"], input[id*="title"], input[id*="name"]');
                        runAiAssist(s, {
                            title: titleEl ? titleEl.value : '',
                            content: inputEl.value || '',
                            selection: '',
                            language: 'vi'
                        }, function (result) {
                            inputEl.value = result;
                            inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                            inputEl.dispatchEvent(new Event('change', { bubbles: true }));
                        });
                    });
                    dropdown.appendChild(item);
                });
                wrapper.appendChild(dropdown);

                setTimeout(function () {
                    document.addEventListener('click', function closeDropdown(ev) {
                        if (!wrapper.contains(ev.target)) {
                            dropdown.remove();
                            document.removeEventListener('click', closeDropdown);
                        }
                    });
                }, 10);
            });
        });
    }

    function initAiAssist() {
        document.querySelectorAll('[data-ai-field]').forEach(function (el) {
            addFieldAiButton(el, el.dataset.aiField);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAiAssist);
    } else {
        initAiAssist();
    }

    window.AiAssist = {
        init: initAiAssist,
        run: runAiAssist,
        loadSkills: loadSkills,
        iconFor: iconFor,
        skillIcons: SKILL_ICONS,
        defaultIcon: ICON_SPARKLES
    };
})();
