/*
 * nc-gallery.js — lightbox cho khối Thư viện ảnh (GalleryBlock).
 *
 * Là JS TĂNG CƯỜNG, không phải điều kiện để bố cục thành hình: masonry/lưới đã do CSS thuần
 * quyết định, còn mỗi ảnh đã là <a href> tới ảnh gốc. Tắt JS thì bấm ảnh mở tab mới — vẫn dùng
 * được, chỉ mất phần xem phóng to tại chỗ.
 *
 * Tự khởi tạo một lần cho mọi [data-nc-gallery] trên trang; khối chèn thêm sau (preview builder)
 * được bắt qua MutationObserver.
 */
(function () {
  'use strict';
  if (window.__ncGalleryReady) return;
  window.__ncGalleryReady = true;

  var STYLE_ID = 'nc-gallery-lightbox-css';
  var CSS =
    '.nc-lb{position:fixed;inset:0;z-index:9999;display:flex;align-items:center;justify-content:center;' +
    'background:rgba(15,23,42,.92);opacity:0;transition:opacity .18s ease}' +
    '.nc-lb.is-open{opacity:1}' +
    '.nc-lb img{max-width:92vw;max-height:86vh;object-fit:contain;border-radius:8px;display:block}' +
    '.nc-lb figcaption{margin-top:12px;color:#e2e8f0;font:400 .9rem/1.5 system-ui,sans-serif;text-align:center;max-width:80vw}' +
    '.nc-lb__inner{display:flex;flex-direction:column;align-items:center}' +
    '.nc-lb__btn{position:absolute;top:50%;transform:translateY(-50%);width:44px;height:44px;border:0;' +
    'border-radius:50%;background:rgba(255,255,255,.14);color:#fff;font-size:22px;cursor:pointer;line-height:1}' +
    '.nc-lb__btn:hover{background:rgba(255,255,255,.26)}' +
    '.nc-lb__prev{left:16px}.nc-lb__next{right:16px}' +
    '.nc-lb__close{position:absolute;top:16px;right:16px;transform:none;width:40px;height:40px;font-size:20px}' +
    '.nc-lb__count{position:absolute;top:22px;left:20px;color:#94a3b8;font:600 12px system-ui,sans-serif}';

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    var st = document.createElement('style');
    st.id = STYLE_ID;
    st.textContent = CSS;
    document.head.appendChild(st);
  }

  var state = { items: [], index: 0, root: null, lastFocus: null };

  /** Ảnh trong CÙNG một khối gallery — hai gallery trên một trang không lẫn vào nhau. */
  function collect(gallery) {
    return Array.prototype.slice.call(gallery.querySelectorAll('a[data-nc-lightbox]'));
  }

  function build() {
    ensureStyle();
    var root = document.createElement('div');
    root.className = 'nc-lb';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.innerHTML =
      '<span class="nc-lb__count" data-lb-count></span>' +
      '<button type="button" class="nc-lb__btn nc-lb__close" data-lb-close aria-label="Đóng">×</button>' +
      '<button type="button" class="nc-lb__btn nc-lb__prev" data-lb-prev aria-label="Ảnh trước">‹</button>' +
      '<button type="button" class="nc-lb__btn nc-lb__next" data-lb-next aria-label="Ảnh sau">›</button>' +
      '<figure class="nc-lb__inner" style="margin:0"><img alt=""><figcaption data-lb-cap></figcaption></figure>';

    root.addEventListener('click', function (e) {
      if (e.target.closest('[data-lb-close]') || e.target === root) close();
      else if (e.target.closest('[data-lb-prev]')) step(-1);
      else if (e.target.closest('[data-lb-next]')) step(1);
    });
    document.body.appendChild(root);
    return root;
  }

  function open(items, index) {
    state.items = items;
    state.index = index;
    state.lastFocus = document.activeElement;
    if (!state.root) state.root = build();
    paint();
    state.root.classList.add('is-open');
    document.documentElement.style.overflow = 'hidden';
    state.root.querySelector('[data-lb-close]').focus();
    document.addEventListener('keydown', onKey);
  }

  function close() {
    if (!state.root) return;
    state.root.classList.remove('is-open');
    document.documentElement.style.overflow = '';
    document.removeEventListener('keydown', onKey);
    // Trả tiêu điểm về đúng ảnh vừa xem để người dùng bàn phím không mất chỗ.
    if (state.lastFocus && state.lastFocus.focus) state.lastFocus.focus();
  }

  function step(delta) {
    if (!state.items.length) return;
    state.index = (state.index + delta + state.items.length) % state.items.length;
    paint();
  }

  function paint() {
    var link = state.items[state.index];
    if (!link) return;
    var img = link.querySelector('img');
    var target = state.root.querySelector('img');
    target.src = link.getAttribute('href');
    target.alt = (img && img.getAttribute('alt')) || '';

    var figure = link.closest('figure');
    var cap = figure && figure.querySelector('figcaption');
    var capEl = state.root.querySelector('[data-lb-cap]');
    capEl.textContent = cap ? cap.textContent : target.alt;
    capEl.style.display = capEl.textContent ? '' : 'none';

    state.root.querySelector('[data-lb-count]').textContent =
      state.items.length > 1 ? (state.index + 1) + ' / ' + state.items.length : '';
    var single = state.items.length < 2;
    state.root.querySelector('[data-lb-prev]').style.display = single ? 'none' : '';
    state.root.querySelector('[data-lb-next]').style.display = single ? 'none' : '';
  }

  function onKey(e) {
    if (e.key === 'Escape') close();
    else if (e.key === 'ArrowLeft') step(-1);
    else if (e.key === 'ArrowRight') step(1);
  }

  function wire(gallery) {
    if (gallery.__ncLbWired) return;
    gallery.__ncLbWired = true;
    gallery.addEventListener('click', function (e) {
      var link = e.target.closest('a[data-nc-lightbox]');
      if (!link || !gallery.contains(link)) return;
      // Ctrl/Cmd/giữa chuột: để trình duyệt mở tab mới như một link bình thường.
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      var items = collect(gallery);
      open(items, items.indexOf(link));
    });
  }

  function scan(scope) {
    var host = scope || document;
    Array.prototype.forEach.call(host.querySelectorAll('[data-nc-gallery]'), wire);
  }

  scan();

  // Khối chèn sau khi trang tải (preview trong builder, nội dung nạp động) vẫn được gắn.
  if (window.MutationObserver) {
    new MutationObserver(function () { scan(); })
      .observe(document.documentElement, { childList: true, subtree: true });
  }
})();
