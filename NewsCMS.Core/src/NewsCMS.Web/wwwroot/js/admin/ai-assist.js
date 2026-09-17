/**
 * AI Assist — trợ lý AI cho các form trong admin.
 *
 * Một nút AI duy nhất trên thanh công cụ editor mở khung trò chuyện: người dùng
 * mô tả mong muốn, AI trả về đồng thời tiêu đề / tóm tắt / nội dung và có nút
 * apply riêng cho từng phần. Hội thoại được gửi kèm mỗi lượt nên AI nhớ ngữ
 * cảnh và sửa dần theo yêu cầu.
 *
 * File này CHỈ dựng UI + gọi API. Nút trên thanh công cụ TinyMCE do partial
 * _TinyMce.cshtml đăng ký (đăng ký ở cả hai nơi từng làm nút AI mất icon vì bản
 * đăng ký sau, không icon, ghi đè bản trước).
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

    const ICON_SEND = svg('<path d="M22 2 11 13"/><path d="M22 2 15 22l-4-9-9-4Z"/>');

    /// Icon theo AiTaskKeys (NewsCMS.Domain/Entities/Ai/AiEnums.cs).
    const SKILL_ICONS = {
        article_chat: ICON_SPARKLES,
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

    /**
     * Gửi một lượt hội thoại. Trả về { reply, title, excerpt, body }.
     * @param payload { contentType, messages, currentTitle, currentExcerpt, currentBody }
     */
    async function chat(payload) {
        let resp;
        try {
            resp = await fetch('/Admin/Ai/Generate?handler=Chat', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    // Tên header phải khớp AddAntiforgery(o => o.HeaderName) trong
                    // Program.cs. Lệch tên thì antiforgery chặn với 400 body rỗng.
                    'RequestVerificationToken': getToken()
                },
                body: JSON.stringify(payload)
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

    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"]/g, function (ch) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[ch];
        });
    }

    // ---------- Khung trò chuyện ----------
    // Nhãn đổi theo loại nội dung: form sản phẩm dùng "Tên sản phẩm / Mô tả ngắn
    // / Mô tả chi tiết" thay vì "Tiêu đề / Tóm tắt / Nội dung".
    const LABELS = {
        post: { title: 'Tiêu đề', excerpt: 'Tóm tắt', body: 'Nội dung' },
        product: { title: 'Tên sản phẩm', excerpt: 'Mô tả ngắn', body: 'Mô tả chi tiết' }
    };

    function labelsFor(contentType) {
        return LABELS[contentType] || LABELS.post;
    }

    /**
     * Mở khung trò chuyện AI.
     * @param opts.contentType 'post' | 'product'
     * @param opts.getContext  hàm trả về { title, excerpt, body } hiện tại trên form
     * @param opts.onApply     hàm nhận ({ field, value }) với field = title|excerpt|body
     * @param opts.onApplyAll  hàm nhận ({ title, excerpt, body }) khi bấm "Áp dụng tất cả"
     */
    function openChat(opts) {
        const contentType = opts.contentType || 'post';
        const labels = labelsFor(contentType);
        const getContext = opts.getContext || function () { return {}; };

        document.getElementById('ai-chat-modal')?.remove();

        const modal = document.createElement('dialog');
        modal.id = 'ai-chat-modal';
        modal.className = 'relative w-full max-w-5xl rounded-2xl border border-slate-200 bg-white p-0 shadow-2xl backdrop:bg-black/50';
        modal.innerHTML = `
            <div class="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-4">
                <div class="flex items-start gap-3">
                    <span class="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-indigo-50 text-indigo-600">${ICON_SPARKLES}</span>
                    <div>
                        <h2 class="text-lg font-bold text-slate-950">Trợ lý soạn nội dung</h2>
                        <p class="mt-0.5 text-sm text-slate-500">Mô tả điều bạn muốn, trao đổi tới khi ưng ý rồi áp dụng vào từng phần.</p>
                    </div>
                </div>
                <button type="button" data-ai-close aria-label="Đóng"
                        class="inline-flex rounded-xl p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700">
                    ${svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>')}
                </button>
            </div>

            <div class="grid max-h-[70vh] grid-cols-1 gap-0 overflow-hidden lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
                <!-- Cột trái: hội thoại -->
                <div class="flex min-h-0 flex-col border-b border-slate-200 lg:border-b-0 lg:border-r">
                    <div id="ai-chat-log" class="min-h-0 flex-1 space-y-3 overflow-y-auto bg-slate-50 px-5 py-4"></div>
                    <div class="border-t border-slate-200 bg-white px-5 py-3">
                        <div id="ai-chat-error" class="mb-2 hidden rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs font-semibold text-red-700"></div>
                        <div class="flex items-end gap-2">
                            <textarea id="ai-chat-input" rows="2" placeholder="Ví dụ: Viết bài giới thiệu cà phê rang mộc, giọng gần gũi…"
                                      class="admin-input resize-none text-sm"></textarea>
                            <button type="button" id="ai-chat-send" class="admin-btn-primary shrink-0 inline-flex items-center gap-2">
                                <span class="inline-flex h-4 w-4 items-center justify-center">${ICON_SEND}</span>
                                <span id="ai-chat-send-label">Gửi</span>
                            </button>
                        </div>
                        <p class="mt-2 text-xs text-slate-400">Enter để gửi · Shift + Enter xuống dòng</p>
                        <label class="mt-2 flex items-center gap-2 text-xs text-slate-600">
                            <input type="checkbox" id="ai-chat-use-site-context" checked
                                   class="h-4 w-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-500" />
                            <span>Dùng thông tin &amp; nội dung của site làm ngữ cảnh</span>
                        </label>
                    </div>
                </div>

                <!-- Cột phải: 3 phần kết quả -->
                <div class="flex min-h-0 flex-col">
                    <div class="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 py-4">
                        <div id="ai-result-empty" class="flex h-full flex-col items-center justify-center gap-2 py-12 text-center">
                            <span class="flex h-12 w-12 items-center justify-center rounded-full bg-slate-100 text-slate-400">${ICON_SPARKLES}</span>
                            <p class="text-sm font-semibold text-slate-600">Chưa có bản nháp nào</p>
                            <p class="max-w-xs text-xs text-slate-400">Gửi tin nhắn đầu tiên để AI soạn ${escapeHtml(labels.title.toLowerCase())}, ${escapeHtml(labels.excerpt.toLowerCase())} và ${escapeHtml(labels.body.toLowerCase())}.</p>
                        </div>

                        <div id="ai-result-fields" class="hidden space-y-4">
                            <div data-ai-field-block="title">
                                <div class="mb-1.5 flex items-center justify-between gap-2">
                                    <span class="admin-label mb-0">${escapeHtml(labels.title)}</span>
                                    <button type="button" data-ai-apply="title" class="text-xs font-bold text-indigo-600 transition hover:text-indigo-800">Áp dụng</button>
                                </div>
                                <textarea data-ai-out="title" rows="2" class="admin-input text-sm"></textarea>
                            </div>

                            <div data-ai-field-block="excerpt">
                                <div class="mb-1.5 flex items-center justify-between gap-2">
                                    <span class="admin-label mb-0">${escapeHtml(labels.excerpt)}</span>
                                    <button type="button" data-ai-apply="excerpt" class="text-xs font-bold text-indigo-600 transition hover:text-indigo-800">Áp dụng</button>
                                </div>
                                <textarea data-ai-out="excerpt" rows="3" class="admin-input text-sm"></textarea>
                            </div>

                            <div data-ai-field-block="body">
                                <div class="mb-1.5 flex items-center justify-between gap-2">
                                    <span class="admin-label mb-0">${escapeHtml(labels.body)}</span>
                                    <button type="button" data-ai-apply="body" class="text-xs font-bold text-indigo-600 transition hover:text-indigo-800">Áp dụng</button>
                                </div>
                                <div data-ai-preview="body"
                                     class="max-h-64 overflow-y-auto rounded-xl border border-slate-200 bg-slate-50 p-3 text-sm leading-relaxed text-slate-800"></div>
                                <details class="mt-2">
                                    <summary class="cursor-pointer text-xs font-semibold text-slate-500">Sửa HTML trước khi áp dụng</summary>
                                    <textarea data-ai-out="body" rows="8" spellcheck="false" class="admin-input mt-2 font-mono text-xs"></textarea>
                                </details>
                            </div>
                        </div>
                    </div>

                    <div class="flex items-center justify-end gap-2 border-t border-slate-200 bg-slate-50 px-5 py-3">
                        <button type="button" id="ai-show-context" class="mr-auto hidden text-xs font-semibold text-slate-500 underline-offset-2 transition hover:text-slate-800 hover:underline">AI đang thấy gì?</button>
                        <button type="button" data-ai-close class="admin-btn-secondary">Đóng</button>
                        <button type="button" id="ai-apply-all" class="admin-btn-primary hidden">Áp dụng tất cả</button>
                    </div>
                </div>
            </div>
        `;

        document.body.appendChild(modal);

        const log = modal.querySelector('#ai-chat-log');
        const input = modal.querySelector('#ai-chat-input');
        const sendBtn = modal.querySelector('#ai-chat-send');
        const sendLabel = modal.querySelector('#ai-chat-send-label');
        const errorBox = modal.querySelector('#ai-chat-error');
        const resultEmpty = modal.querySelector('#ai-result-empty');
        const resultFields = modal.querySelector('#ai-result-fields');
        const applyAllBtn = modal.querySelector('#ai-apply-all');
        const useContextBox = modal.querySelector('#ai-chat-use-site-context');
        const showContextBtn = modal.querySelector('#ai-show-context');

        /** Lịch sử gửi lên server: [{ role, content }] */
        const history = [];
        /** Bản nháp AI vừa trả về, người dùng có thể sửa trước khi apply. */
        let draft = null;
        /** Khối ngữ cảnh site lần gửi gần nhất, để hiện khi bấm "AI đang thấy gì?". */
        let lastSiteContext = '';

        const out = {
            title: modal.querySelector('[data-ai-out="title"]'),
            excerpt: modal.querySelector('[data-ai-out="excerpt"]'),
            body: modal.querySelector('[data-ai-out="body"]')
        };
        const preview = modal.querySelector('[data-ai-preview="body"]');

        modal.querySelectorAll('[data-ai-close]').forEach(function (btn) {
            btn.addEventListener('click', function () { modal.close(); });
        });
        modal.addEventListener('click', function (e) { if (e.target === modal) modal.close(); });
        modal.addEventListener('close', function () { modal.remove(); });

        // showModal() đẩy modal lên top layer nên vẫn hiện đúng khi TinyMCE
        // đang bật toàn màn hình (Fullscreen API).
        modal.showModal();
        input.focus();

        function addBubble(role, text) {
            const wrap = document.createElement('div');
            wrap.className = role === 'user' ? 'flex justify-end' : 'flex justify-start';

            const bubble = document.createElement('div');
            bubble.className = role === 'user'
                ? 'max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-sm bg-indigo-600 px-3.5 py-2 text-sm text-white'
                : 'max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-bl-sm border border-slate-200 bg-white px-3.5 py-2 text-sm text-slate-700';
            bubble.textContent = text;

            wrap.appendChild(bubble);
            log.appendChild(wrap);
            log.scrollTop = log.scrollHeight;
            return wrap;
        }

        function addThinking() {
            const wrap = document.createElement('div');
            wrap.className = 'flex justify-start';
            wrap.innerHTML = '<div class="flex items-center gap-2 rounded-2xl rounded-bl-sm border border-slate-200 bg-white px-3.5 py-2 text-sm text-slate-500">'
                + '<span class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-indigo-100 border-t-indigo-600"></span>'
                + '<span>Đang soạn… <span data-ai-elapsed></span></span></div>';
            log.appendChild(wrap);
            log.scrollTop = log.scrollHeight;

            const startedAt = Date.now();
            const elapsed = wrap.querySelector('[data-ai-elapsed]');
            const timer = setInterval(function () {
                elapsed.textContent = Math.round((Date.now() - startedAt) / 1000) + 's';
            }, 1000);
            return function () { clearInterval(timer); wrap.remove(); };
        }

        function setBusy(busy) {
            sendBtn.disabled = busy;
            input.disabled = busy;
            sendLabel.textContent = busy ? 'Đang soạn…' : 'Gửi';
            sendBtn.classList.toggle('opacity-60', busy);
        }

        function showError(message, hint) {
            errorBox.textContent = hint ? message + ' ' + hint : message;
            errorBox.classList.remove('hidden');
        }

        function fillResult(data) {
            draft = {
                title: data.title || '',
                excerpt: data.excerpt || '',
                body: data.body || ''
            };
            out.title.value = draft.title;
            out.excerpt.value = draft.excerpt;
            out.body.value = draft.body;
            // body đã được server sanitize nên an toàn để đổ vào innerHTML.
            preview.innerHTML = draft.body;

            resultEmpty.classList.add('hidden');
            resultFields.classList.remove('hidden');
            applyAllBtn.classList.remove('hidden');

            // Phần nào AI không trả về thì ẩn luôn nút Áp dụng để người dùng
            // không bấm nhầm và ghi đè dữ liệu hiện có bằng chuỗi rỗng.
            ['title', 'excerpt', 'body'].forEach(function (field) {
                modal.querySelector('[data-ai-field-block="' + field + '"]')
                    .classList.toggle('hidden', !draft[field]);
            });
        }

        async function send() {
            const text = input.value.trim();
            if (!text) return;

            errorBox.classList.add('hidden');
            addBubble('user', text);
            history.push({ role: 'user', content: text });
            input.value = '';

            setBusy(true);
            const stopThinking = addThinking();

            try {
                const ctx = getContext() || {};
                const data = await chat({
                    contentType: contentType,
                    messages: history,
                    currentTitle: out.title.value || ctx.title || '',
                    currentExcerpt: out.excerpt.value || ctx.excerpt || '',
                    currentBody: out.body.value || ctx.body || '',
                    includeSiteContext: useContextBox.checked
                });

                stopThinking();
                lastSiteContext = data.siteContext || '';
                // Chỉ hiện nút khi thực sự có ngữ cảnh để xem — site chưa có nội
                // dung nào thì nút này mở ra khung rỗng, gây hoang mang.
                showContextBtn.classList.toggle('hidden', !lastSiteContext);

                const reply = data.reply || 'Đã cập nhật bản nháp.';
                addBubble('assistant', reply);
                history.push({ role: 'assistant', content: reply });
                fillResult(data);
            } catch (err) {
                stopThinking();
                // Không đẩy lượt lỗi vào history: gửi lại nguyên văn câu hỏi cũ
                // sẽ khiến AI thấy hai lượt user liên tiếp.
                history.pop();
                showError(err.message, err.hint || '');
                addBubble('assistant', 'Xin lỗi, mình chưa tạo được nội dung. Bạn thử gửi lại giúp mình nhé.');
            } finally {
                setBusy(false);
                input.focus();
            }
        }

        sendBtn.addEventListener('click', send);
        input.addEventListener('keydown', function (e) {
            // Enter gửi, Shift+Enter xuống dòng — thoả thuận quen thuộc của khung chat.
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                send();
            }
        });

        modal.querySelectorAll('[data-ai-apply]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const field = btn.dataset.aiApply;
                const value = out[field].value;
                if (!value) return;
                opts.onApply?.({ field: field, value: value });
                btn.textContent = 'Đã áp dụng ✓';
                setTimeout(function () { btn.textContent = 'Áp dụng'; }, 1500);
            });
        });

        showContextBtn.addEventListener('click', function () {
            const existing = modal.querySelector('#ai-context-panel');
            if (existing) { existing.remove(); return; }

            const panel = document.createElement('div');
            panel.id = 'ai-context-panel';
            panel.className = 'absolute inset-x-6 bottom-20 top-24 z-10 overflow-y-auto rounded-2xl border border-slate-300 bg-white p-4 shadow-2xl';
            panel.innerHTML = '<div class="mb-2 flex items-center justify-between gap-3">'
                + '<span class="text-sm font-bold text-slate-800">Ngữ cảnh đã gửi kèm cho AI</span>'
                + '<button type="button" class="rounded-lg px-2 py-1 text-xs font-semibold text-slate-500 transition hover:bg-slate-100">Đóng</button>'
                + '</div>'
                + '<pre class="whitespace-pre-wrap font-mono text-xs leading-relaxed text-slate-700"></pre>';
            // textContent chứ không innerHTML: đây là nội dung bài viết của người
            // dùng, đổ vào innerHTML là tự mở đường XSS trong trang admin.
            panel.querySelector('pre').textContent = lastSiteContext;
            panel.querySelector('button').addEventListener('click', function () { panel.remove(); });

            modal.appendChild(panel);
        });

        applyAllBtn.addEventListener('click', function () {
            if (!draft) return;
            // Đọc lại từ textarea: người dùng có thể đã sửa tay trong lúc xem trước.
            const payload = {
                title: out.title.value,
                excerpt: out.excerpt.value,
                body: out.body.value
            };
            if (opts.onApplyAll) opts.onApplyAll(payload);
            else ['title', 'excerpt', 'body'].forEach(function (field) {
                if (payload[field]) opts.onApply?.({ field: field, value: payload[field] });
            });
            modal.close();
            window.toast?.success('Đã áp dụng nội dung do AI tạo.');
        });

        // Câu chào đầu tiên: nêu rõ AI cần gì để người dùng biết bắt đầu từ đâu.
        const ctx = getContext() || {};
        const hasDraft = !!(ctx.title || ctx.excerpt || ctx.body);
        addBubble('assistant', hasDraft
            ? 'Mình thấy form đang có nội dung. Bạn muốn mình viết mới, hay chỉnh sửa phần nào?'
            : 'Bạn muốn viết về chủ đề gì? Cho mình biết chủ đề, đối tượng độc giả và giọng văn mong muốn nhé.');
    }

    // ---------- Nút AI cạnh input / textarea (data-ai-field) ----------
    // Từ khi có khung trò chuyện, các nút lẻ ở từng ô nhập không còn cần thiết —
    // người dùng vào một chỗ duy nhất. Giữ hàm này lại nhưng không tự gắn nút,
    // để trang nào còn sót data-ai-field cũng không sinh UI trùng.
    function initAiAssist() { /* no-op: xem openChat */ }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAiAssist);
    } else {
        initAiAssist();
    }

    window.AiAssist = {
        init: initAiAssist,
        openChat: openChat,
        chat: chat,
        loadSkills: loadSkills,
        labelsFor: labelsFor,
        iconFor: iconFor,
        skillIcons: SKILL_ICONS,
        defaultIcon: ICON_SPARKLES
    };
})();
