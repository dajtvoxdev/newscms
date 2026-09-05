/**
 * AI Assist — Dynamic AI buttons for admin editors.
 * Loads prompt skills from /Admin/Ai/Skills?target=... and renders buttons.
 * Each button opens a preview modal, calls /Admin/Ai/Generate, and applies result.
 */
(function () {
    'use strict';

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function loadSkills(target) {
        try {
            const resp = await fetch('/Admin/Ai/Skills?target=' + encodeURIComponent(target), {
                headers: { 'X-CSRF-TOKEN': getToken() }
            });
            if (!resp.ok) return [];
            return await resp.json();
        } catch { return []; }
    }

    async function generate(skillKey, style, context) {
        const resp = await fetch('/Admin/Ai/Generate', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': getToken()
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

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({ error: 'Lỗi không xác định' }));
            throw new Error(err.error || 'HTTP ' + resp.status);
        }

        return await resp.json();
    }

    function createModal() {
        const existing = document.getElementById('ai-assist-modal');
        if (existing) existing.remove();

        const modal = document.createElement('div');
        modal.id = 'ai-assist-modal';
        modal.className = 'fixed inset-0 z-50 flex items-center justify-center bg-black/50';
        modal.innerHTML = `
            <div class="bg-white rounded-2xl shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col">
                <div class="flex items-center justify-between px-6 py-4 border-b">
                    <h3 class="text-lg font-bold" id="ai-modal-title">AI Assist</h3>
                    <button onclick="document.getElementById('ai-assist-modal').remove()" class="text-gray-400 hover:text-gray-600 text-2xl">&times;</button>
                </div>
                <div class="flex-1 overflow-auto p-6">
                    <div id="ai-modal-loading" class="text-center py-8 text-gray-500">
                        <div class="inline-block animate-spin rounded-full h-8 w-8 border-4 border-blue-500 border-t-transparent mb-3"></div>
                        <p>Đang tạo nội dung...</p>
                    </div>
                    <div id="ai-modal-preview" style="display:none">
                        <div id="ai-modal-render" class="prose max-w-none border rounded-lg p-4 mb-4 min-h-[200px]"></div>
                        <details class="mb-4">
                            <summary class="text-sm text-gray-500 cursor-pointer">Xem HTML thô</summary>
                            <textarea id="ai-modal-raw" class="w-full mt-2 border rounded-lg p-3 font-mono text-sm" rows="6"></textarea>
                        </details>
                    </div>
                    <div id="ai-modal-error" style="display:none" class="text-red-600 text-center py-8"></div>
                </div>
                <div class="flex items-center justify-between px-6 py-4 border-t bg-gray-50 rounded-b-2xl">
                    <div class="flex items-center gap-3" id="ai-modal-style-toggle" style="display:none">
                        <label class="text-sm font-medium">Kiểu:</label>
                        <select id="ai-modal-style" class="border rounded px-2 py-1 text-sm">
                            <option value="0">Plain</option>
                            <option value="1">Styled</option>
                        </select>
                    </div>
                    <div class="flex gap-3">
                        <button id="ai-btn-regenerate" onclick="window._aiRegenerate()" class="px-4 py-2 border rounded-lg hover:bg-gray-100 text-sm" style="display:none">Tạo lại</button>
                        <button id="ai-btn-apply" onclick="window._aiApply()" class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 text-sm" style="display:none">Áp dụng</button>
                        <button onclick="document.getElementById('ai-assist-modal').remove()" class="px-4 py-2 border rounded-lg hover:bg-gray-100 text-sm">Huỷ</button>
                    </div>
                </div>
            </div>
        `;
        document.body.appendChild(modal);
        return modal;
    }

    async function runAiAssist(skillKey, allowStyled, context, onApply) {
        const modal = createModal();
        const loading = modal.querySelector('#ai-modal-loading');
        const preview = modal.querySelector('#ai-modal-preview');
        const errorDiv = modal.querySelector('#ai-modal-error');
        const renderDiv = modal.querySelector('#ai-modal-render');
        const rawTextarea = modal.querySelector('#ai-modal-raw');
        const styleToggle = modal.querySelector('#ai-modal-style-toggle');
        const styleSelect = modal.querySelector('#ai-modal-style');
        const btnRegenerate = modal.querySelector('#ai-btn-regenerate');
        const btnApply = modal.querySelector('#ai-btn-apply');
        const title = modal.querySelector('#ai-modal-title');

        title.textContent = 'AI: ' + skillKey;
        let currentContent = '';

        if (allowStyled) {
            styleToggle.style.display = 'flex';
        }

        async function doGenerate(style) {
            loading.style.display = 'block';
            preview.style.display = 'none';
            errorDiv.style.display = 'none';
            btnRegenerate.style.display = 'none';
            btnApply.style.display = 'none';

            try {
                const result = await generate(skillKey, style, context);
                // content is server-sanitized (safe for innerHTML), raw is original AI output
                currentContent = result.raw || result.content;
                renderDiv.innerHTML = result.content || currentContent;
                rawTextarea.value = currentContent;
                loading.style.display = 'none';
                preview.style.display = 'block';
                btnRegenerate.style.display = 'inline-block';
                btnApply.style.display = 'inline-block';
            } catch (err) {
                loading.style.display = 'none';
                errorDiv.textContent = err.message;
                errorDiv.style.display = 'block';
                btnRegenerate.style.display = 'inline-block';
                window.toast?.error(err.message);
            }
        }

        window._aiRegenerate = () => {
            const style = allowStyled ? parseInt(styleSelect.value) : 0;
            doGenerate(style);
        };

        window._aiApply = () => {
            const raw = rawTextarea.value;
            onApply(raw);
            modal.remove();
        };

        doGenerate(0);
    }

    // --- TinyMCE integration ---
    function setupTinyMceAiButton(editor, target) {
        loadSkills(target).then(skills => {
            if (!skills.length) return;

            skills.forEach(skill => {
                const buttonName = 'ai_' + skill.key;
                editor.ui.registry.addButton(buttonName, {
                    text: '🤖 ' + skill.name,
                    tooltip: skill.name,
                    onAction: function () {
                        const selection = editor.selection.getContent({ format: 'text' });
                        const fullContent = editor.getContent({ format: 'text' });
                        const titleEl = document.querySelector('input[name*="Title"], input[name*="Name"], input[id*="title"], input[id*="name"]');
                        const title = titleEl ? titleEl.value : '';

                        runAiAssist(skill.key, skill.allowStyled, {
                            title: title,
                            content: fullContent,
                            selection: selection,
                            language: 'vi'
                        }, function (result) {
                            editor.insertContent(result);
                        });
                    }
                });
            });

            // Add AI dropdown to toolbar
            const aiButtons = skills.map(s => 'ai_' + s.key);
            // Register as menu items too
            skills.forEach(skill => {
                editor.ui.registry.addMenuItem('ai_' + skill.key, {
                    text: '🤖 ' + skill.name,
                    onAction: function () {
                        const selection = editor.selection.getContent({ format: 'text' });
                        const fullContent = editor.getContent({ format: 'text' });
                        const titleEl = document.querySelector('input[name*="Title"], input[name*="Name"], input[id*="title"], input[id*="name"]');
                        const title = titleEl ? titleEl.value : '';

                        runAiAssist(skill.key, skill.allowStyled, {
                            title: title,
                            content: fullContent,
                            selection: selection,
                            language: 'vi'
                        }, function (result) {
                            editor.insertContent(result);
                        });
                    }
                });
            });
        });
    }

    // --- Field-level AI buttons ---
    function addFieldAiButton(inputEl, target) {
        loadSkills(target).then(skills => {
            if (!skills.length) return;

            // Wrapper phai la block full-width: 'inline-block' lam o input co width:100%
            // bi co lai bang kich thuoc mac dinh (~20 ky tu) va vo layout form.
            const isMultiline = inputEl.tagName === 'TEXTAREA';
            const wrapper = document.createElement('div');
            wrapper.className = 'relative block w-full';
            inputEl.parentNode.insertBefore(wrapper, inputEl);
            wrapper.appendChild(inputEl);

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = isMultiline
                ? 'absolute right-2 top-2 text-xs bg-purple-100 text-purple-700 px-2 py-1 rounded hover:bg-purple-200'
                : 'absolute right-2 top-1/2 -translate-y-1/2 text-xs bg-purple-100 text-purple-700 px-2 py-1 rounded hover:bg-purple-200';
            btn.textContent = '🤖 AI';
            btn.title = 'Sinh noi dung bang AI';
            wrapper.appendChild(btn);

            // Chua cho nut AI de chu khong chui xuong duoi nut.
            inputEl.style.paddingRight = '4rem';

            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                // Create dropdown
                const existing = document.querySelector('.ai-field-dropdown');
                if (existing) existing.remove();

                const dropdown = document.createElement('div');
                dropdown.className = 'ai-field-dropdown absolute right-0 top-full mt-1 bg-white border rounded-lg shadow-lg z-50 min-w-[200px]';
                dropdown.innerHTML = skills.map(s =>
                    `<button class="block w-full text-left px-4 py-2 hover:bg-gray-100 text-sm" data-key="${s.key}" data-styled="${s.allowStyled}">🤖 ${s.name}</button>`
                ).join('');
                wrapper.appendChild(dropdown);

                dropdown.querySelectorAll('button').forEach(b => {
                    b.addEventListener('click', function () {
                        dropdown.remove();
                        const skillKey = this.dataset.key;
                        const allowStyled = this.dataset.styled === 'true';
                        const titleEl = document.querySelector('input[name*="Title"], input[name*="Name"], input[id*="title"], input[id*="name"]');
                        const title = titleEl ? titleEl.value : '';

                        runAiAssist(skillKey, allowStyled, {
                            title: title,
                            content: inputEl.value || '',
                            selection: '',
                            language: 'vi'
                        }, function (result) {
                            inputEl.value = result;
                            inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                            inputEl.dispatchEvent(new Event('change', { bubbles: true }));
                        });
                    });
                });

                // Close on outside click
                setTimeout(() => {
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

    // --- Auto-initialize ---
    function initAiAssist() {
        // TinyMCE editors: look for setup callbacks or existing editors
        if (typeof tinymce !== 'undefined') {
            // For each editor, add AI buttons based on the textarea's data-target
            document.querySelectorAll('textarea[data-ai-target]').forEach(ta => {
                const target = ta.dataset.aiTarget;
                const editorId = ta.id;
                const checkEditor = setInterval(() => {
                    const editor = tinymce.get(editorId);
                    if (editor) {
                        clearInterval(checkEditor);
                        setupTinyMceAiButton(editor, target);
                    }
                }, 500);
            });
        }

        // Field-level buttons
        document.querySelectorAll('[data-ai-field]').forEach(el => {
            const target = el.dataset.aiField;
            addFieldAiButton(el, target);
        });
    }

    // Run on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAiAssist);
    } else {
        initAiAssist();
    }

    // Expose for manual init
    window.AiAssist = { init: initAiAssist, run: runAiAssist, loadSkills: loadSkills };
})();
