/*
 * nc-code-format.js — beautify khi MỞ, minify khi LƯU cho mọi panel code của builder.
 *
 * Vì sao dùng thư viện thay vì tự viết: minify đúng cần AST. Regex-vs-phép-chia trong JS, ASI,
 * chuỗi/template literal, at-rule lồng nhau trong CSS — làm tay thì lỗi âm thầm và cái vỡ là
 * CSS/JS đang chạy trên trang public. Ở đây: js-beautify (format), csso (CSS), terser (JS).
 *
 * NGUYÊN TẮC AN TOÀN, quan trọng hơn cả việc nén được bao nhiêu:
 *   1. Mọi hàm không bao giờ throw. Lỗi parse -> trả lại NGUYÊN VĂN bản gốc kèm .error.
 *      Không có đường nào để một đoạn code hợp lệ bị lưu thành rỗng hay thành mảnh vỡ.
 *   2. JS minify chạy với compress:false, mangle:false — chỉ bỏ comment/khoảng trắng rồi in lại
 *      AST. Mangle sẽ đổi tên hàm top-level, mà snippet trong CMS hay được gọi từ
 *      onclick="doThing()" trong HTML — đổi tên là trang vỡ mà không báo gì.
 *   3. CSS minify chạy restructure:false. Restructure gộp/đổi thứ tự rule, trong khi CSS của site
 *      này cố tình dựa vào thứ tự nối (custom.css thắng compiled, layout CSS thắng site CSS).
 *   4. HTML KHÔNG minify, chỉ beautify — xem MINIFIABLE bên dưới.
 *   5. Comment bị xoá khi minify là mất hẳn. Muốn giữ thì viết /*! ... *\/ (CSS) hoặc //! (JS).
 *
 * Vì sao beautify khi mở lại quan trọng: css-inspector báo "rule này ở dòng N" rồi nút Sửa nhảy
 * tới dòng N trong CodeMirror. Số dòng đó tính trên CHÍNH văn bản đang giữ trong bộ nhớ. Nên bên
 * gọi phải beautify TRƯỚC khi đưa vào codeState/OPTS.siteCss, không phải chỉ beautify để hiển thị
 * — nếu không thì DB minify một dòng, inspector báo dòng 1 cho mọi rule.
 */
(function (global) {
  'use strict';

  var LIB_BASE = '/lib/codeformat/';

  /** Ngôn ngữ hỗ trợ. 'blockcss' là CSS DSL của khối (&, ^, khai báo trần) — xem dslEncode. */
  var LANGS = ['css', 'js', 'html', 'blockcss'];

  /**
   * HTML cố tình không nằm ở đây. Khoảng trắng giữa hai thẻ inline là ký tự có nghĩa
   * (`<a>x</a> <a>y</a>` khác `<a>x</a><a>y</a>`), và CompiledHtml của layout còn bị parse lại
   * thành cây component GrapesJS. Nén nó đổi cách trang hiển thị mà không ai thấy lúc lưu.
   */
  var MINIFIABLE = { css: true, blockcss: true, js: true, html: false };

  var LIBS = {
    css: [{ global: 'csso', src: 'csso.js' }],
    blockcss: [{ global: 'csso', src: 'csso.js' }],
    js: [{ global: 'Terser', src: 'terser.min.js' }],
    html: []
  };
  var BEAUTIFIER = { global: 'beautifier', src: 'beautifier.min.js' };

  var BEAUTIFY_CSS = { indent_size: 2, selector_separator_newline: true, newline_between_rules: true, preserve_newlines: true };
  var BEAUTIFY_JS = { indent_size: 2, preserve_newlines: true, max_preserve_newlines: 2, brace_style: 'collapse', end_with_newline: false };
  var BEAUTIFY_HTML = { indent_size: 2, preserve_newlines: true, max_preserve_newlines: 2, wrap_line_length: 0, unformatted: ['code', 'pre', 'em', 'strong', 'span', 'a', 'b', 'i', 'sub', 'sup', 'small' ] };

  // ── Nạp lười ────────────────────────────────────────────────────────────────
  // terser ~1MB: nạp sẵn ở mọi trang admin là trả giá cho thứ hầu hết phiên không dùng tới.
  var loading = {};

  function loadScript(src) {
    if (loading[src]) return loading[src];
    loading[src] = new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = (global.ncCodeFormatBase || LIB_BASE) + src;
      s.onload = function () { resolve(); };
      s.onerror = function () { loading[src] = null; reject(new Error('Không tải được ' + src)); };
      document.head.appendChild(s);
    });
    return loading[src];
  }

  function need(entry) {
    return global[entry.global] ? Promise.resolve() : loadScript(entry.src);
  }

  /**
   * Bảo đảm thư viện cho các lang đã cho đã nạp. Luôn resolve — hỏng mạng thì các hàm bên dưới
   * tự rơi về chế độ "trả nguyên văn" chứ không để bên gọi phải try/catch.
   */
  function ensure(langs, opts) {
    var want = normalizeLangs(langs);
    var jobs = [need(BEAUTIFIER)];
    if (!opts || opts.minify !== false) {
      want.forEach(function (l) {
        if (MINIFIABLE[l]) (LIBS[l] || []).forEach(function (e) { jobs.push(need(e)); });
      });
    }
    return Promise.all(jobs.map(function (p) { return p.catch(function () { return null; }); }))
      .then(function () { return true; });
  }

  function normalizeLangs(langs) {
    var arr = Array.isArray(langs) ? langs : [langs];
    return arr.filter(function (l) { return LANGS.indexOf(l) >= 0; });
  }

  // ── DSL CSS của khối ────────────────────────────────────────────────────────
  // CSS khối không phải CSS hợp lệ: `&:hover{}` gắn vào chính khối, `^#id{}` thoát ra ngoài khối,
  // và cả file có thể chỉ là khai báo trần `padding:32px`. csso/js-beautify sẽ bỏ hoặc làm hỏng
  // các dạng đó, nên phải mã hoá thành CSS hợp lệ trước rồi giải mã sau.
  var AMP_TOKEN = '.ncfmt-amp';
  var CARET_TOKEN = '.ncfmt-caret';
  var BARE_WRAP = '.ncfmt-bare';

  /**
   * Đổi & / ^ ở ĐẦU selector thành class giả. Chỉ ở đầu selector: `[href^="/a"]` cũng có dấu ^
   * và `content:"&"` cũng có dấu &, thay bừa cả file là phá luôn hai dạng đó. Nên phải quét có
   * trạng thái (chuỗi, comment, ngoặc vuông/tròn, độ sâu {}) thay vì replace toàn cục.
   */
  function dslEncode(css) {
    var out = '';
    var i = 0;
    var n = css.length;
    var depth = 0;          // độ sâu {} — chỉ ở ngoài khai báo mới là vùng selector
    var bracket = 0;        // trong [] hoặc ()
    var atSelectorStart = true;

    while (i < n) {
      var c = css.charAt(i);

      // comment
      if (c === '/' && css.charAt(i + 1) === '*') {
        var end = css.indexOf('*/', i + 2);
        if (end < 0) end = n; else end += 2;
        out += css.slice(i, end);
        i = end;
        continue;
      }
      // chuỗi
      if (c === '"' || c === "'") {
        var q = c;
        var j = i + 1;
        while (j < n) {
          if (css.charAt(j) === '\\') { j += 2; continue; }
          if (css.charAt(j) === q) { j++; break; }
          j++;
        }
        out += css.slice(i, j);
        i = j;
        atSelectorStart = false;
        continue;
      }

      if (c === '[' || c === '(') bracket++;
      else if (c === ']' || c === ')') bracket = Math.max(0, bracket - 1);
      else if (c === '{') { depth++; atSelectorStart = false; out += c; i++; continue; }
      else if (c === '}') { depth = Math.max(0, depth - 1); atSelectorStart = true; out += c; i++; continue; }
      else if (c === ',' && bracket === 0) { atSelectorStart = true; out += c; i++; continue; }

      if (atSelectorStart && bracket === 0 && /\s/.test(c)) { out += c; i++; continue; }

      // depth 0 = vùng selector top-level; depth 1 khi rule nằm trong @media thì selector cũng
      // đứng ngay sau '{' của at-rule — atSelectorStart đã bật ở nhánh '{' phía trên? Không:
      // '{' tắt cờ. Nên chỉ nhận & / ^ khi cờ đang bật (đầu file, sau '}' hoặc sau ',').
      if (atSelectorStart && bracket === 0 && (c === '&' || c === '^')) {
        out += (c === '&' ? AMP_TOKEN : CARET_TOKEN);
        i++;
        atSelectorStart = false;
        continue;
      }

      if (bracket === 0 && !/\s/.test(c)) atSelectorStart = false;
      out += c;
      i++;
    }
    return out;
  }

  function dslDecode(css) {
    return css
      .split(AMP_TOKEN).join('&')
      .split(CARET_TOKEN).join('^');
  }

  /** Khai báo trần (không có '{') phải bọc tạm để công cụ CSS chịu parse. */
  function isBareDecls(css) {
    return css.indexOf('{') < 0 && css.trim().length > 0;
  }

  function unwrapBare(css) {
    var open = css.indexOf('{');
    var close = css.lastIndexOf('}');
    if (open < 0 || close < open) return css;
    return css.slice(open + 1, close)
      .split('\n')
      .map(function (l) { return l.replace(/^\s{1,2}/, ''); })
      .join('\n')
      .trim();
  }

  /**
   * Khối CSS có thể TRỘN rule với khai báo trần (`padding:32px` không thuộc rule nào).
   * Cả file trần thì isBareDecls bắt được, nhưng khai báo trần nằm XEN KẼ các rule thì
   * csso/js-beautify bỏ luôn (e2e bắt đúng ca này) — nên bọc từng run khai báo trần ở
   * depth 0 vào rule giả, thay vì chỉ bọc khi cả file trần.
   *
   * Nhận diện run: đoạn ở depth 0 kết thúc bằng ';' / EOF ('{' đứng trước là selector của
   * rule kế tiếp, '}' là CSS lệch ngoặc — cả hai cứ để nguyên). Có ':' ngoài chuỗi/ngoặc
   * và không bắt đầu bằng '@': @import url("https://…") cũng chứa ':' nhưng là at-rule,
   * bọc vào rule giả là hỏng.
   */
  function wrapBareRuns(css) {
    var out = '';
    var i = 0;
    var n = css.length;
    var depth = 0;
    var bracket = 0;
    var start = -1; // đầu run đang gom — đệm trong bản gốc, closeRun cắt một lần

    function emitRun(end, inclusive) {
      var run = css.slice(start, inclusive ? end + 1 : end);
      start = -1;
      out += isBareRun(run) ? BARE_WRAP + '{' + run + '}' : run;
    }

    while (i < n) {
      var c = css.charAt(i);

      if (c === '/' && css.charAt(i + 1) === '*') {
        var ce = css.indexOf('*/', i + 2);
        ce = ce < 0 ? n : ce + 2;
        if (depth === 0) { if (start < 0) start = i; }
        else out += css.slice(i, ce);
        i = ce;
        continue;
      }
      if (c === '"' || c === "'") {
        var q = c;
        var j = i + 1;
        while (j < n) {
          if (css.charAt(j) === '\\') { j += 2; continue; }
          if (css.charAt(j) === q) { j++; break; }
          j++;
        }
        if (depth === 0) { if (start < 0) start = i; }
        else out += css.slice(i, j);
        i = j;
        continue;
      }

      if (c === '[' || c === '(') {
        bracket++;
        if (depth > 0) out += c;
        else if (start < 0) start = i;
        i++;
        continue;
      }
      if (c === ']' || c === ')') {
        bracket = Math.max(0, bracket - 1);
        if (depth > 0) out += c;
        else if (start < 0) start = i;
        i++;
        continue;
      }

      if (depth === 0 && bracket === 0) {
        if (c === '{') {
          // Đoạn trước '{' là selector của rule kế tiếp — giữ nguyên, đừng bọc.
          if (start >= 0) { out += css.slice(start, i); start = -1; }
          out += c; depth++; i++; continue;
        }
        if (c === '}') {
          if (start >= 0) { out += css.slice(start, i); start = -1; }
          out += c; i++; continue;
        }
        if (c === ';') {
          if (start >= 0) emitRun(i, true);
          else out += c;
          i++;
          continue;
        }
      }

      if (depth > 0) {
        if (c === '{') depth++;
        else if (c === '}') depth--;
        out += c;
        i++;
        continue;
      }

      // depth 0: whitespace ngoài run thì giữ nguyên, ký tự khác mở/nuôi run
      if (start >= 0) { i++; continue; }
      if (/\s/.test(c)) out += c;
      else start = i;
      i++;
    }
    if (start >= 0) emitRun(n, false);
    return out;
  }

  /** Run có phải khai báo trần thật không: có ':' ở top-level và không phải at-rule. */
  function isBareRun(text) {
    var t = text.trim();
    if (!t || t.charAt(0) === '@') return false;
    var i = 0;
    var n = t.length;
    var bracket = 0;
    while (i < n) {
      var c = t.charAt(i);
      if (c === '/' && t.charAt(i + 1) === '*') {
        var e = t.indexOf('*/', i + 2);
        i = e < 0 ? n : e + 2;
        continue;
      }
      if (c === '"' || c === "'") {
        var q = c;
        var j = i + 1;
        while (j < n) {
          if (t.charAt(j) === '\\') { j += 2; continue; }
          if (t.charAt(j) === q) { j++; break; }
          j++;
        }
        i = j;
        continue;
      }
      if (c === '[' || c === '(') { bracket++; i++; continue; }
      if (c === ']' || c === ')') { bracket = Math.max(0, bracket - 1); i++; continue; }
      if (c === ':' && bracket === 0) return true;
      i++;
    }
    return false;
  }

  /** Gỡ các rule giả mà wrapBareRuns bọc vào; chịu cả bản minify lẫn bản beautify có thụt lề. */
  function unwrapBareRuns(css) {
    return css.replace(/\.ncfmt-bare\s*\{([^{}]*)\}/g, function (m, body) {
      return body.split('\n').map(function (l) { return l.replace(/^\s{1,2}/, ''); }).join('\n').trim();
    });
  }

  // ── Beautify ────────────────────────────────────────────────────────────────

  function beautify(text, lang) {
    var src = text == null ? '' : String(text);
    if (!src.trim()) return src;
    var b = global.beautifier;
    if (!b) return src;                       // chưa nạp được lib -> để nguyên, không phá gì

    try {
      if (lang === 'html') return b.html(src, BEAUTIFY_HTML);
      if (lang === 'js') return b.js(src, BEAUTIFY_JS);
      if (lang === 'css') return b.css(src, BEAUTIFY_CSS);
      if (lang === 'blockcss') {
        var work = dslEncode(wrapBareRuns(src));
        var done = b.css(work, BEAUTIFY_CSS);
        return unwrapBareRuns(dslDecode(done));
      }
      return src;
    } catch (e) {
      return src;
    }
  }

  // ── Minify ──────────────────────────────────────────────────────────────────

  /**
   * @returns {{code:string, error:(string|null), skipped:boolean}}
   *   code  luôn dùng được: hoặc bản đã nén, hoặc nguyên văn bản gốc.
   *   error khác null = KHÔNG nén được (thường do cú pháp sai) — bên gọi nên cảnh báo người dùng.
   */
  function minify(text, lang) {
    var src = text == null ? '' : String(text);
    if (!src.trim()) return { code: src, error: null, skipped: true };
    if (!MINIFIABLE[lang]) return { code: src, error: null, skipped: true };

    try {
      if (lang === 'js') return minifyJs(src);
      return minifyCss(src, lang === 'blockcss');
    } catch (e) {
      return { code: src, error: e && e.message ? e.message : 'Lỗi không xác định', skipped: false };
    }
  }

  function minifyCss(src, isBlock) {
    if (!global.csso) return { code: src, error: null, skipped: true };
    var work = isBlock ? dslEncode(wrapBareRuns(src)) : src;

    var res = global.csso.minify(work, { restructure: false, comments: 'exclamation' });
    var out = res && typeof res.css === 'string' ? res.css : '';
    // csso không throw với CSS sai cú pháp, nó bỏ đoạn không parse được. Ra rỗng từ đầu vào
    // không rỗng nghĩa là nó bỏ sạch -> giữ bản gốc.
    if (!out.trim()) return { code: src, error: 'CSS không nén được (cú pháp không hợp lệ?)', skipped: false };
    if (isBlock) out = unwrapBareRuns(dslDecode(out));
    return { code: out, error: null, skipped: false };
  }

  function minifyJs(src) {
    var T = global.Terser;
    if (!T || typeof T.minify_sync !== 'function') return { code: src, error: null, skipped: true };
    var res = T.minify_sync(src, {
      compress: false,          // không biến đổi logic, chỉ in lại gọn
      mangle: false,            // giữ tên hàm/biến top-level: HTML có thể gọi tới
      sourceMap: false,
      format: { comments: /^!/ }
    });
    if (res && res.error) return { code: src, error: String(res.error.message || res.error), skipped: false };
    var out = res && typeof res.code === 'string' ? res.code : '';
    if (!out.trim()) return { code: src, error: 'JS không nén được', skipped: false };
    return { code: out, error: null, skipped: false };
  }

  // ── Bật/tắt minify khi lưu ──────────────────────────────────────────────────
  var STORE_KEY = 'nc.code.minifyOnSave';

  function minifyEnabled() {
    try {
      var v = global.localStorage && global.localStorage.getItem(STORE_KEY);
      return v === null || v === undefined ? true : v === '1';
    } catch (e) { return true; }
  }

  function setMinifyEnabled(on) {
    try { global.localStorage.setItem(STORE_KEY, on ? '1' : '0'); } catch (e) { /* private mode */ }
  }

  /**
   * Đường dùng thường ngày: nén nếu người dùng đang bật, còn lại trả nguyên văn.
   * Có lỗi thì báo qua toast nhưng VẪN trả code dùng được (bản gốc) để việc lưu không bị chặn.
   */
  function forSave(text, lang, label) {
    if (!minifyEnabled()) return text == null ? '' : String(text);
    var r = minify(text, lang);
    if (r.error && global.toast && global.toast.warning) {
      global.toast.warning((label ? label + ': ' : '') + 'giữ nguyên bản chưa nén — ' + r.error);
    }
    return r.code;
  }

  global.ncCodeFormat = {
    LANGS: LANGS,
    MINIFIABLE: MINIFIABLE,
    ensure: ensure,
    beautify: beautify,
    minify: minify,
    forSave: forSave,
    minifyEnabled: minifyEnabled,
    setMinifyEnabled: setMinifyEnabled,
    // export cho unit test
    _dslEncode: dslEncode,
    _dslDecode: dslDecode,
    _isBareDecls: isBareDecls,
    _unwrapBare: unwrapBare,
    _wrapBareRuns: wrapBareRuns,
    _unwrapBareRuns: unwrapBareRuns,
    _isBareRun: isBareRun
  };

  if (typeof module === 'object' && module.exports) module.exports = global.ncCodeFormat;
})(typeof globalThis !== 'undefined' ? globalThis : window);
