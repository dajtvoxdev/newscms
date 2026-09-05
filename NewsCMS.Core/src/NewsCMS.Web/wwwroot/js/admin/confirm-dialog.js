/**
 * confirm-dialog.js — Dialog xác nhận dùng chung thay thế window.confirm() mặc định.
 *
 * Cơ chế: mọi form có thuộc tính [data-confirm] (nội dung là câu hỏi xác nhận,
 * có thể chứa HTML escape) sẽ được chặn submit và hiện dialog xác nhận Yes/No.
 * Chỉ khi người dùng chọn "Đồng ý" form mới được submit tiếp.
 *
 * Nút xác nhận nhận nhãn qua data-confirm-yes, nút huỷ qua data-confirm-no
 * (mặc định "Đồng ý" / "Huỷ"). Nút submit gốc vẫn giữ nguyên để trình duyệt
 * gửi đúng name/value (ví dụ asp-page-handler với button name="starType").
 *
 * API lập trình: window.confirmDialog(message, options) → Promise<boolean>,
 * với options = { title, yesLabel, noLabel, danger }.
 */
(function () {
    'use strict';

    let dialog = null;
    let messageEl = null;
    let titleEl = null;
    let yesBtn = null;
    let noBtn = null;

    function ensureDialog() {
        if (dialog && document.body.contains(dialog)) return;

        dialog = document.createElement('dialog');
        dialog.id = 'admin-confirm-dialog';
        dialog.innerHTML =
            '<div class="acd-card">' +
            '<h2 class="acd-title">Xác nhận</h2>' +
            '<p class="acd-message"></p>' +
            '<div class="acd-actions">' +
            '<button type="button" class="acd-btn acd-no">Huỷ</button>' +
            '<button type="button" class="acd-btn acd-yes">Đồng ý</button>' +
            '</div></div>';
        document.body.appendChild(dialog);

        titleEl = dialog.querySelector('.acd-title');
        messageEl = dialog.querySelector('.acd-message');
        yesBtn = dialog.querySelector('.acd-yes');
        noBtn = dialog.querySelector('.acd-no');

        // Click ngoài card hoặc Esc/Cancel → coi như từ chối.
        dialog.addEventListener('click', function (e) {
            if (e.target === dialog) dialog.close('cancel');
        });
        dialog.addEventListener('cancel', function () { /* close event xử lý */ });

        const style = document.createElement('style');
        style.textContent = `
            #admin-confirm-dialog {
                border: none; padding: 0; background: transparent; max-width: none;
            }
            #admin-confirm-dialog::backdrop { background: rgba(15, 23, 42, .45); }
            .acd-card {
                width: min(400px, calc(100vw - 48px));
                background: #fff; border-radius: 16px;
                box-shadow: 0 20px 50px rgba(15, 23, 42, .3);
                padding: 22px 24px 18px;
                font-family: inherit;
            }
            .acd-title {
                margin: 0 0 8px; font-size: 15px; font-weight: 700; color: #0f172a;
            }
            #admin-confirm-dialog.acd-danger .acd-title { color: #b91c1c; }
            .acd-message {
                margin: 0; font-size: 13.5px; line-height: 1.55; color: #475569;
                white-space: pre-wrap; word-break: break-word;
            }
            .acd-actions {
                margin-top: 18px; display: flex; justify-content: flex-end; gap: 10px;
            }
            .acd-btn {
                min-width: 92px; padding: 8px 16px; border-radius: 10px;
                font-size: 13px; font-weight: 600; cursor: pointer;
                transition: background-color .15s ease, border-color .15s ease;
                display: inline-flex; align-items: center; justify-content: center;
            }
            .acd-no {
                background: #fff; color: #334155; border: 1px solid #e2e8f0;
            }
            .acd-no:hover { background: #f8fafc; border-color: #cbd5e1; }
            .acd-yes {
                background: #4f46e5; color: #fff; border: 1px solid transparent;
            }
            .acd-yes:hover { background: #4338ca; }
            #admin-confirm-dialog.acd-danger .acd-yes {
                background: #dc2626;
            }
            #admin-confirm-dialog.acd-danger .acd-yes:hover { background: #b91c1c; }
        `;
        document.head.appendChild(style);
    }

    /**
     * Mở dialog xác nhận. Trả về Promise<boolean> — true nếu người dùng đồng ý.
     * options: { title?, yesLabel?, noLabel?, danger? }
     */
    function confirmDialog(message, options) {
        ensureDialog();
        const opts = options || {};

        return new Promise(function (resolve) {
            const onDone = function () {
                cleanup();
                resolve(dialog.returnValue === 'ok');
            };
            const cleanup = function () {
                dialog.removeEventListener('close', onDone);
            };

            cleanup();
            dialog.addEventListener('close', onDone);

            dialog.classList.toggle('acd-danger', !!opts.danger);
            titleEl.textContent = opts.title || 'Xác nhận';
            // Nội dung đến từ Razor (đã HTML-encode) hoặc chuỗi JS — luôn render dạng text.
            messageEl.textContent = message || 'Bạn có chắc chắn?';
            yesBtn.textContent = opts.yesLabel || 'Đồng ý';
            noBtn.textContent = opts.noLabel || 'Huỷ';

            dialog.returnValue = '';
            if (!dialog.open) dialog.showModal();
            yesBtn.focus();
        });
    }

    // ── Bridge cho form [data-confirm] ────────────────────────────────────
    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        const msg = form.getAttribute('data-confirm');
        if (!msg || form.dataset.confirmBound === 'pending') return;

        e.preventDefault();
        form.dataset.confirmBound = 'pending';

        const danger = form.hasAttribute('data-confirm-danger');
        confirmDialog(msg, {
            danger: danger,
            yesLabel: form.getAttribute('data-confirm-yes') || undefined,
            noLabel: form.getAttribute('data-confirm-no') || undefined
        }).then(function (ok) {
            delete form.dataset.confirmBound;
            if (!ok) return;
            form.dispatchEvent(new CustomEvent('nc-confirm-accepted'));
            // Submit lại nhưng bỏ qua bridge: gọi submit() không kích hoạt sự kiện submit.
            if (typeof form.requestSubmit === 'function') {
                form.requestSubmit();
            } else {
                form.submit();
            }
        });
    }, true);

    // Xác nhận gắn trên từng nút ([data-confirm-button]) — dùng khi cùng một form
    // có nhiều nút submit và chỉ một nhánh cần hỏi (vd. Khoá / Mở khoá người chơi).
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('[data-confirm-button]');
        if (!btn) return;
        const form = btn.closest('form');
        if (!form) return;
        e.preventDefault();
        e.stopPropagation();
        confirmDialog(btn.getAttribute('data-confirm-button'), { danger: true })
            .then(function (ok) {
                if (!ok) return;
                form.dispatchEvent(new CustomEvent('nc-confirm-accepted'));
                // requestSubmit(btn) giữ nguyên name/value của nút bấm.
                if (typeof form.requestSubmit === 'function') form.requestSubmit(btn);
                else form.submit();
            });
    }, true);

    window.confirmDialog = confirmDialog;
})();
