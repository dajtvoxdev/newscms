/*!
 * chu-flipbook.js — khởi tạo flipbook (StPageFlip) cho block "pdf-flipbook".
 *
 * Trang sách là ảnh WebP đã rasterise sẵn từ PDF (thư mục full/ small/ thumb/).
 * Ảnh chỉ được nạp cho cửa sổ quanh trang đang xem (mặc định -2 → +3) nên trang chủ
 * chỉ tải vài trăm KB thay vì cả file PDF; srcset để trình duyệt tự chọn bản 620w/1240w.
 */
(function () {
    'use strict';

    if (window.__chuFlipbookInit) return;
    window.__chuFlipbookInit = true;

    var PRELOAD_BACK = 2;
    var PRELOAD_AHEAD = 3;

    function pad(n) {
        return n < 10 ? '0' + n : '' + n;
    }

    function init(root) {
        if (root.dataset.flipReady === '1') return;
        if (typeof window.St === 'undefined' || !window.St.PageFlip) return;
        root.dataset.flipReady = '1';

        var bookEl = root.querySelector('[data-flip-book]');
        var viewport = root.querySelector('[data-flip-viewport]');
        var pageEls = Array.prototype.slice.call(root.querySelectorAll('.chu-flip__page'));
        if (!bookEl || !pageEls.length) return;

        var src = (root.dataset.src || '').replace(/\/+$/, '');
        var count = pageEls.length;
        var ratio = parseFloat(root.dataset.ratio) || 0.707;
        var smallW = parseInt(root.dataset.small, 10) || 620;
        var fullW = parseInt(root.dataset.full, 10) || 1240;

        var btnPrev = root.querySelector('[data-flip-prev]');
        var btnNext = root.querySelector('[data-flip-next]');
        var btnFull = root.querySelector('[data-flip-fullscreen]');
        var counter = root.querySelector('[data-flip-counter]');
        var nudge = root.querySelector('[data-flip-nudge]');

        function hideNudge() {
            if (!nudge) return;
            var el = nudge;
            nudge = null;
            el.classList.add('is-gone');
            setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 500);
        }

        // ---- nạp ảnh theo cửa sổ quanh trang hiện tại -------------------------
        function pageUrl(dir, i) {
            return src + '/' + dir + '/' + pad(i + 1) + '.webp';
        }

        function loadPage(i) {
            if (i < 0 || i >= count) return;
            var el = pageEls[i];
            var img = el.querySelector('img');
            if (!img || img.dataset.loaded === '1') return;
            img.dataset.loaded = '1';

            // thumb 180w (~5KB) làm nền mờ trong lúc chờ bản nét.
            if (!el.style.backgroundImage) el.style.backgroundImage = 'url("' + pageUrl('thumb', i) + '")';

            img.addEventListener('load', function () {
                img.classList.add('is-loaded');
                el.classList.add('is-ready');
                el.style.backgroundImage = '';
            }, { once: true });

            img.sizes = img.sizes || '(max-width: 767px) 92vw, 46vw';
            img.srcset = pageUrl('small', i) + ' ' + smallW + 'w, ' + pageUrl('full', i) + ' ' + fullW + 'w';
            img.src = pageUrl('small', i);
        }

        function loadWindow(index) {
            var from = Math.max(0, index - PRELOAD_BACK);
            var to = Math.min(count - 1, index + PRELOAD_AHEAD);
            for (var i = from; i <= to; i++) loadPage(i);
        }

        // ---- kích thước: sách luôn vừa khung nhìn, không phải cuộn ------------
        function applySize() {
            if (!viewport) return;
            var portrait = flip && flip.getOrientation && flip.getOrientation() === 'portrait';
            var pages = portrait ? 1 : 2;
            var avail = (document.fullscreenElement === root ? window.innerHeight - 120 : window.innerHeight * 0.82);
            var byHeight = Math.round(avail * ratio * pages);
            var cap = portrait ? 560 : 1120;
            viewport.style.maxWidth = Math.max(280, Math.min(cap, byHeight)) + 'px';
        }

        var baseW = 550;
        var flip = new window.St.PageFlip(bookEl, {
            width: baseW,
            height: Math.round(baseW / ratio),
            size: 'stretch',
            minWidth: 260,
            maxWidth: 760,
            minHeight: Math.round(260 / ratio),
            maxHeight: Math.round(760 / ratio),
            showCover: true,
            usePortrait: true,
            mobileScrollSupport: false,
            maxShadowOpacity: 0.5,
            drawShadow: true,
            flippingTime: 700,
            autoSize: true
        });

        // Sách đóng (bìa trước / bìa sau) chỉ chiếm nửa khung → canh giữa bằng transform.
        function updateShift() {
            var portrait = flip.getOrientation() === 'portrait';
            var index = flip.getCurrentPageIndex();
            root.classList.toggle('chu-flip--portrait', portrait);
            root.classList.toggle('chu-flip--closed', !portrait && index <= 0);
            root.classList.toggle('chu-flip--closed-end', !portrait && index >= count - 1);
        }

        function sync() {
            var index = flip.getCurrentPageIndex();
            loadWindow(index);
            updateShift();
            if (counter) {
                var portrait = flip.getOrientation() === 'portrait';
                var label;
                if (portrait || index === 0 || index === count - 1) label = (index + 1) + ' / ' + count;
                else label = (index + 1) + '–' + Math.min(count, index + 2) + ' / ' + count;
                counter.textContent = label;
            }
            if (btnPrev) btnPrev.disabled = index <= 0;
            if (btnNext) btnNext.disabled = index >= count - 1;
        }

        loadWindow(0);
        bookEl.classList.add('is-ready');
        flip.loadFromHTML(pageEls);
        applySize();
        sync();

        flip.on('flip', sync);
        flip.on('changeOrientation', function () { applySize(); sync(); });
        flip.on('changeState', function (e) {
            if (e.data === 'flipping' || e.data === 'user_fold') {
                // Bắt đầu lật → nạp trước trang đích để không thấy ô trắng giữa chừng,
                // đồng thời bỏ canh giữa ngay để sách "mở ra" mượt cùng nhịp lật.
                loadWindow(flip.getCurrentPageIndex());
                hideNudge();
                root.classList.remove('chu-flip--closed', 'chu-flip--closed-end');
            } else if (e.data === 'read') {
                updateShift();
            }
        });

        if (nudge) root.addEventListener('pointerdown', hideNudge, { once: true });

        if (btnPrev) btnPrev.addEventListener('click', function () { flip.flipPrev(); });
        if (btnNext) btnNext.addEventListener('click', function () { flip.flipNext(); });

        if (btnFull) {
            if (!document.documentElement.requestFullscreen) {
                btnFull.hidden = true;
            } else {
                btnFull.addEventListener('click', function () {
                    if (document.fullscreenElement === root) document.exitFullscreen();
                    else root.requestFullscreen().catch(function () { /* người dùng từ chối */ });
                });
                document.addEventListener('fullscreenchange', function () {
                    var on = document.fullscreenElement === root;
                    btnFull.setAttribute('aria-pressed', on ? 'true' : 'false');
                    setTimeout(function () { applySize(); flip.update(); }, 80);
                });
            }
        }

        root.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowLeft') { flip.flipPrev(); e.preventDefault(); }
            else if (e.key === 'ArrowRight') { flip.flipNext(); e.preventDefault(); }
        });

        var resizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(function () { applySize(); sync(); }, 150);
        });
    }

    function boot() {
        var nodes = document.querySelectorAll('[data-chu-flipbook]');
        for (var i = 0; i < nodes.length; i++) init(nodes[i]);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
