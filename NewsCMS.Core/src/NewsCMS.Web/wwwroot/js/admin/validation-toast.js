// validation-toast.js — Cầu nối asp-validation-summary → toast error.
//
// Razor render validation summary thành <ul> bên trong div có class
// "validation-summary-errors" (khi có lỗi) — script này quét các div đó sau
// khi trang tải, đẩy từng lỗi vào toast và ẩn khối banner gốc.
(function () {
    'use strict';

    function bridgeValidationSummaries() {
        document.querySelectorAll('.validation-summary-errors, [data-valmsg-summary="true"]').forEach(function (box) {
            const items = box.querySelectorAll('ul li');
            if (items.length === 0) return;
            // Chỉ xử lý khi khối này thực sự đang hiển thị nội dung lỗi server-side.
            items.forEach(function (li) {
                const msg = li.textContent && li.textContent.trim();
                if (msg) window.toast?.error(msg);
            });
            box.classList.add('hidden');
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bridgeValidationSummaries);
    } else {
        bridgeValidationSummaries();
    }
})();
