/*
 * nc-design-tokens.js — sổ tra design token của site, nguồn cho swatch trong Style Manager.
 *
 * VÌ SAO PHẢI ĐỌC LÚC CHẠY
 *   Token màu / bo góc / bóng KHÔNG nằm trong site-utilities.css — file đó chỉ có palette
 *   Tailwind (--color-{hue}-{50..950}) và font stack. Token của site do
 *   DesignTokenCssBuilder sinh ra theo từng site và phục vụ ở /_nc/site/{slug}-{hash}.css,
 *   gồm đúng hai khối :root và @theme với các khai báo `--<nhóm>-<key>: <giá trị>;`.
 *   Muốn Style Manager đưa ra lựa chọn `var(--color-brand-500)` thay vì bắt người biên tập
 *   gõ hex thì phải có danh sách đó — mà danh sách đổi theo site nên không hardcode được.
 *
 *   Nhóm suy ra từ tiền tố biến, đúng bảng DesignTokenCssBuilder.TokenName:
 *     Color→--color-, Font→--font-, Space→--space-, Radius→--radius-, Shadow→--shadow-.
 *
 *   Giá trị token có thể là `var(--color-emerald-700)` trỏ ngược về palette Tailwind, nên
 *   khi thấy có tham chiếu chưa giải được thì mới nạp thêm site-utilities.css (file này
 *   canvas đã nạp nên gần như luôn trúng cache) — không phải trả 114KB cho mọi site.
 *
 * API: window.ncTokens.{ load, ready, onReady, colors, radii, shadows, spaces, resolve }
 */
(function (global) {
  'use strict';

  var UTIL_CSS = '/css/site-utilities.css';

  var state = {
    /** Token của site, theo thứ tự file CSS (= Group, SortOrder, Key do server sắp). */
    site: {},
    /** Palette Tailwind trong site-utilities.css — chỉ nạp khi token có tham chiếu var(). */
    utility: {},
    ready: false,
    loading: null,
    cbs: []
  };

  /** Bóc mọi khai báo `--x: y;` trong một đoạn CSS, giữ thứ tự xuất hiện. */
  function parseVars(css) {
    var map = {};
    if (!css) return map;
    var re = /(--[\w-]+)\s*:\s*([^;}]+)/g;
    var m;
    while ((m = re.exec(css)) !== null) {
      // Khai báo đầu tiên thắng: :root đứng trước @theme trong file sinh ra, và hai khối
      // đó là bản sao của cùng bộ token.
      if (!(m[1] in map)) map[m[1]] = m[2].trim();
    }
    return map;
  }

  function loadText(url) {
    if (!url) return Promise.resolve('');
    return fetch(url)
      .then(function (r) { return r.ok ? r.text() : ''; })
      .catch(function () { return ''; });
  }

  function hasVarRef(map) {
    for (var k in map) {
      if (map[k].indexOf('var(') !== -1) return true;
    }
    return false;
  }

  /**
   * Nạp token cho site hiện tại. Gọi nhiều lần chỉ nạp một lần (Edit.cshtml và
   * nc-builder-extensions.js đều gọi để không phụ thuộc thứ tự khởi động).
   */
  function load(tokenCssUrl) {
    if (state.loading) return state.loading;

    state.loading = loadText(tokenCssUrl)
      .then(function (css) {
        state.site = parseVars(css);
        return hasVarRef(state.site) ? loadText(UTIL_CSS) : '';
      })
      .then(function (utilCss) {
        state.utility = parseVars(utilCss);
        state.ready = true;
        var cbs = state.cbs;
        state.cbs = [];
        cbs.forEach(function (fn) {
          try { fn(); } catch (e) { /* widget đã bị huỷ trước khi token về */ }
        });
      });

    return state.loading;
  }

  /**
   * Đăng ký callback chạy khi token sẵn sàng (chạy ngay nếu đã sẵn sàng).
   * Trả về hàm huỷ đăng ký — widget của Style Manager bị render/huỷ liên tục nên phải
   * cho phép rút callback ra, không thì mỗi lần đổi selection là rò một listener.
   */
  function onReady(fn) {
    if (state.ready) { fn(); return function () {}; }
    state.cbs.push(fn);
    return function () {
      var i = state.cbs.indexOf(fn);
      if (i >= 0) state.cbs.splice(i, 1);
    };
  }

  /**
   * Giải một giá trị token về dạng trình duyệt hiểu được (để tô màu swatch).
   * Bám var() tới 6 cấp rồi dừng — vòng lặp token là lỗi dữ liệu, không đáng treo tab.
   */
  function resolve(raw, depth) {
    var v = (raw || '').trim();
    if (v.indexOf('var(') !== 0) return v;

    var m = v.match(/^var\(\s*(--[\w-]+)\s*(?:,([\s\S]*))?\)$/);
    if (!m) return v;

    var next = state.site[m[1]] !== undefined ? state.site[m[1]] : state.utility[m[1]];
    if (next === undefined) return m[2] !== undefined ? m[2].trim() : v;
    if ((depth || 0) >= 6) return v;
    return resolve(next, (depth || 0) + 1);
  }

  /** Danh sách token theo nhóm, giữ nguyên thứ tự server sinh ra. */
  function list(prefix) {
    var out = [];
    for (var name in state.site) {
      if (!Object.prototype.hasOwnProperty.call(state.site, name)) continue;
      if (name.indexOf(prefix) !== 0) continue;
      var key = name.slice(prefix.length);
      if (!key) continue;
      var value = state.site[name];
      out.push({
        name: name,
        key: key,
        value: value,
        css: 'var(' + name + ')',
        resolved: resolve(value)
      });
    }
    return out;
  }

  global.ncTokens = {
    load: load,
    ready: function () { return state.ready; },
    onReady: onReady,
    resolve: resolve,
    colors: function () { return list('--color-'); },
    radii: function () { return list('--radius-'); },
    shadows: function () { return list('--shadow-'); },
    spaces: function () { return list('--space-'); },
    /** Token thô theo tên biến — css-inspector / nơi khác có thể cần. */
    get: function (name) { return state.site[name]; }
  };
})(window);
