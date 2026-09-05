/**
 * toast.js — Hệ thống thông báo toast dùng chung cho toàn bộ khu vực Admin.
 *
 * API: window.toast.success(message) / .error / .warning / .info
 * Tuỳ chọn tham số thứ hai: { duration } (ms) để ghi đè thời gian tự tắt.
 *
 * Tự chứa CSS (cùng pattern với upload-manager.js) nên không phụ thuộc chuỗi
 * build Tailwind. Được nạp một lần trong _AdminLayout; các TempData
 * Success/Error/Warning/Info được cầu nối qua partial _Toasts.cshtml.
 */
(function () {
    'use strict';

    const CONTAINER_ID = 'admin-toast-root';
    const MAX_VISIBLE = 6;

    const ICON_CHECK = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>';
    const ICON_X_CIRCLE = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>';
    const ICON_WARNING = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>';
    const ICON_INFO = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>';
    const ICON_CLOSE = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';

    // Bảng màu bám theo palette emerald/red/amber/sky đã dùng khắp admin.
    const TYPES = {
        success: { icon: ICON_CHECK, accent: '#10b981', bg: '#ecfdf5', border: '#a7f3d0', text: '#065f46', timeout: 4000, role: 'status', label: 'Thành công' },
        error:   { icon: ICON_X_CIRCLE, accent: '#ef4444', bg: '#fef2f2', border: '#fecaca', text: '#991b1b', timeout: 8000, role: 'alert', label: 'Lỗi' },
        warning: { icon: ICON_WARNING, accent: '#f59e0b', bg: '#fffbeb', border: '#fde68a', text: '#92400e', timeout: 6000, role: 'alert', label: 'Cảnh báo' },
        info:    { icon: ICON_INFO, accent: '#0ea5e9', bg: '#f0f9ff', border: '#bae6fd', text: '#075985', timeout: 4500, role: 'status', label: 'Thông tin' }
    };

    let container = null;

    function getContainer() {
        if (container && document.body.contains(container)) return container;
        container = document.getElementById(CONTAINER_ID);
        if (!container) {
            container = document.createElement('div');
            container.id = CONTAINER_ID;
            document.body.appendChild(container);
        }
        return container;
    }

    function show(type, message, opts) {
        const cfg = TYPES[type] || TYPES.info;
        if (!message) return;

        const box = getContainer();

        // Giới hạn số toast hiển thị cùng lúc — bỏ toast cũ nhất.
        while (box.children.length >= MAX_VISIBLE) {
            box.firstElementChild.remove();
        }

        const el = document.createElement('div');
        el.className = 'at-toast';
        el.setAttribute('role', cfg.role);
        el.setAttribute('aria-label', cfg.label);
        el.style.setProperty('--at-accent', cfg.accent);
        el.style.background = cfg.bg;
        el.style.borderColor = cfg.border;

        el.innerHTML =
            '<span class="at-icon" aria-hidden="true">' + cfg.icon + '</span>' +
            '<div class="at-body"><p class="at-msg"></p></div>' +
            '<button type="button" class="at-close" aria-label="Đóng">' + ICON_CLOSE + '</button>';

        el.querySelector('.at-msg').textContent = String(message);

        let timer = null;
        const dismiss = function () {
            if (!el.isConnected) return;
            clearTimeout(timer);
            el.classList.remove('at-show');
            setTimeout(function () { el.remove(); }, 220);
        };
        const armTimer = function (ms) {
            clearTimeout(timer);
            timer = setTimeout(dismiss, ms);
        };

        el.querySelector('.at-close').addEventListener('click', dismiss);
        // Hover thì tạm dừng đếm ngược, rời chuột chạy lại đủ thời gian.
        el.addEventListener('mouseenter', function () { clearTimeout(timer); });
        el.addEventListener('mouseleave', function () {
            armTimer((opts && opts.duration) || cfg.timeout);
        });

        box.appendChild(el);
        // Nhường 1 frame để trạng thái ban đầu kịp paint rồi mới chuyển sang .at-show.
        requestAnimationFrame(function () { el.classList.add('at-show'); });
        armTimer((opts && opts.duration) || cfg.timeout);
    }

    window.toast = {
        success: function (msg, opts) { show('success', msg, opts); },
        error: function (msg, opts) { show('error', msg, opts); },
        warning: function (msg, opts) { show('warning', msg, opts); },
        info: function (msg, opts) { show('info', msg, opts); }
    };

    const style = document.createElement('style');
    style.textContent = `
        #admin-toast-root {
            position: fixed; top: 16px; right: 16px; z-index: 10000;
            display: flex; flex-direction: column; gap: 10px;
            width: min(380px, calc(100vw - 32px));
            pointer-events: none;
        }
        .at-toast {
            display: flex; align-items: flex-start; gap: 10px;
            padding: 12px 14px; border: 1px solid; border-left: 4px solid var(--at-accent, #64748b);
            border-radius: 14px; box-shadow: 0 8px 24px rgba(15, 23, 42, .12);
            font-family: inherit; font-size: 13px; line-height: 1.45;
            pointer-events: all; cursor: default;
            opacity: 0; transform: translateX(16px);
            transition: opacity .2s ease, transform .2s ease;
        }
        .at-toast.at-show { opacity: 1; transform: translateX(0); }
        .at-icon { flex-shrink: 0; width: 18px; height: 18px; margin-top: 1px; color: var(--at-accent, #64748b); }
        .at-icon svg { width: 100%; height: 100%; display: block; }
        .at-body { min-width: 0; flex: 1; }
        .at-msg { margin: 0; white-space: pre-wrap; word-break: break-word; color: #1e293b; font-weight: 500; }
        .at-close {
            flex-shrink: 0; display: inline-flex; align-items: center; justify-content: center;
            width: 22px; height: 22px; margin-top: -1px; margin-right: -4px;
            background: none; border: none; border-radius: 6px; cursor: pointer;
            color: #94a3b8; padding: 0;
        }
        .at-close:hover { color: #475569; background: rgba(148, 163, 184, .18); }
        .at-close svg { width: 14px; height: 14px; }
    `;
    document.head.appendChild(style);
})();
