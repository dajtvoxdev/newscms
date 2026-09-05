/*
 * nc-tailwind.js — cầu nối Tailwind 4 cho GrapesJS trong NewsCMS Site Builder.
 *
 * Vì sao tự viết thay vì dùng grapesjs-tailwindcss-plugin: package đó MIT nhưng
 * 1 contributor / 22 star / push cuối 08-2025 — bus factor quá thấp cho một thành phần lõi.
 * Xem plan Site Builder, mục 2.
 *
 * CÁCH @tailwindcss/browser HOẠT ĐỘNG (đã đọc dist, không phải suy đoán):
 *   - Là IIFE, KHÔNG export gì cả — không có hàm compile() để gọi.
 *   - Khi nạp, nó quét `style[type="text/tailwindcss"]` trong CHÍNH document chứa nó,
 *     lấy nội dung làm CSS nguồn.
 *   - Nó MutationObserver document.documentElement (class + childList + subtree) và
 *     build lại mỗi khi markup đổi.
 *   - Kết quả được ghi vào một <style> do nó tự append vào document.head.
 *
 * Hệ quả: muốn Tailwind chạy cho canvas thì phải nạp script vào BÊN TRONG iframe canvas,
 * không phải document của trang admin. Và muốn lấy CSS đã build thì đọc lại <style> đó.
 *
 * API công khai: window.ncTailwind.{ attach, getCompiledCss, isReady }.
 */
(function (global) {
  'use strict';

  var SOURCE_ID = 'nc-tw-source';
  var RUNTIME_SRC = '/lib/grapesjs/tailwind-browser.js';

  function log(msg, extra) {
    if (extra !== undefined) console.debug('[nc-tailwind] ' + msg, extra);
    else console.debug('[nc-tailwind] ' + msg);
  }

  var state = {
    themeCss: '',
    ready: false,
    /** <style> do Tailwind runtime tạo trong iframe — nguồn để lấy CSS đã build. */
    outputEl: null
  };

  /**
   * Nạp CSS design token của site và bóc riêng khối @theme.
   * Phần :root trong file đó là bản sao của cùng bộ token; đưa cả hai vào sẽ nhân đôi biến.
   */
  async function loadThemeCss(url) {
    if (!url) return '';
    try {
      var res = await fetch(url, { credentials: 'include' });
      if (!res.ok) return '';
      var text = await res.text();
      var match = text.match(/@theme\s*\{[\s\S]*?\n\}/);
      return match ? match[0] : '';
    } catch (e) {
      log('không nạp được token CSS', e);
      return '';
    }
  }

  /** Chờ tới khi điều kiện đúng hoặc hết giờ. Trả true nếu điều kiện thành. */
  function waitFor(check, timeoutMs) {
    return new Promise(function (resolve) {
      var deadline = Date.now() + (timeoutMs || 5000);
      (function poll() {
        if (check()) return resolve(true);
        if (Date.now() > deadline) return resolve(false);
        setTimeout(poll, 60);
      })();
    });
  }

  /**
   * Bơm Tailwind runtime vào iframe canvas: đặt CSS nguồn trước, rồi mới nạp script
   * (runtime đọc nguồn ngay lúc khởi động).
   */
  async function bootstrapCanvas(editor) {
    var doc = editor.Canvas.getDocument();
    if (!doc || !doc.head) return false;

    // Đã bơm rồi thì thôi — editor.on('load') có thể bắn lại khi đổi device.
    if (doc.getElementById(SOURCE_ID)) return true;

    var source = doc.createElement('style');
    source.id = SOURCE_ID;
    source.setAttribute('type', 'text/tailwindcss');
    source.textContent = '@import "tailwindcss";\n' + state.themeCss;
    doc.head.appendChild(source);

    // Chụp danh sách <style> trước khi nạp runtime; cái xuất hiện thêm sau đó là output.
    var before = new Set(Array.prototype.slice.call(doc.head.querySelectorAll('style')));

    await new Promise(function (resolve) {
      var script = doc.createElement('script');
      script.src = RUNTIME_SRC;
      script.onload = resolve;
      script.onerror = function () { log('không nạp được ' + RUNTIME_SRC); resolve(); };
      doc.head.appendChild(script);
    });

    // Runtime build bất đồng bộ — chờ nó append <style> kết quả.
    var found = await waitFor(function () {
      var styles = Array.prototype.slice.call(doc.head.querySelectorAll('style'));
      for (var i = 0; i < styles.length; i++) {
        if (!before.has(styles[i]) && styles[i].id !== SOURCE_ID) {
          state.outputEl = styles[i];
          return true;
        }
      }
      return false;
    }, 8000);

    if (!found) log('runtime không tạo ra style output trong 8s');
    state.ready = found;
    return found;
  }

  /**
   * Gắn Tailwind vào một editor GrapesJS.
   * @param {object} editor  instance GrapesJS
   * @param {object} opts    { tokenCssUrl: string }
   */
  async function attach(editor, opts) {
    opts = opts || {};
    state.themeCss = await loadThemeCss(opts.tokenCssUrl);

    editor.on('load', function () { bootstrapCanvas(editor); });

    // Editor có thể đã load xong trước khi attach được gọi.
    if (editor.Canvas && editor.Canvas.getDocument()) bootstrapCanvas(editor);

    log('đã gắn', { hasTheme: !!state.themeCss });
  }

  /**
   * CSS để LƯU XUỐNG SERVER: utility Tailwind đã build + CSS thủ công của Style Manager.
   * Nhờ đó trang phục vụ khách có sẵn CSS, không phụ thuộc compile ở runtime.
   *
   * Runtime build theo debounce nội bộ nên chờ một nhịp ngắn cho class vừa thêm kịp vào.
   */
  async function getCompiledCss(editor) {
    var manual = '';
    try { manual = editor.getCss() || ''; } catch (e) { /* editor chưa sẵn sàng */ }

    if (!state.outputEl) return manual;

    await new Promise(function (r) { setTimeout(r, 350); });

    var utility = state.outputEl.textContent || '';
    if (!utility) return manual;

    return utility + (manual ? '\n\n/* Style Manager */\n' + manual : '');
  }

  global.ncTailwind = {
    attach: attach,
    getCompiledCss: getCompiledCss,
    isReady: function () { return state.ready; }
  };
})(window);
