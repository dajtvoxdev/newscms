/*
 * nc-css-inspector.js — truy vết rule CSS nào đang tác động lên một phần tử trong canvas builder.
 *
 * Vấn đề: CSS của một khối có thể nằm ở 5 chỗ khác nhau (CSS riêng khối, CSS riêng trang, CSS chung
 * của site, Style Manager, file theme đã build). Khi chọn một DOM trong canvas mà muốn sửa màu/khoảng
 * cách, không có cách nào biết rule đang áp nằm ở đâu ngoài mở từng chỗ ra dò.
 *
 * Module này thuần logic, KHÔNG đụng DOM của panel: nhận phần tử + danh sách nguồn CSS, trả về các
 * rule khớp kèm ĐÚNG số dòng trong văn bản nguồn để phía UI nhảy thẳng tới đó trong editor.
 *
 * Vì sao tự parse thay vì đọc CSSOM (document.styleSheets):
 *   - CSSOM không cho biết rule nằm ở dòng nào trong file nguồn, mà "nhảy tới đúng dòng" chính là
 *     thứ cần nhất ở đây;
 *   - đọc cssRules của sheet khác origin (CDN) ném SecurityError.
 * CSSOM vẫn dùng cho nguồn chỉ-đọc (theme/Tailwind) — ở đó không cần số dòng.
 */
(function (global) {
  'use strict';

  /**
   * Pseudo-class trạng thái: phải BỎ trước khi thử khớp, nếu không rule `:hover` sẽ không bao giờ
   * hiện ra (chuột đâu có đang hover lúc bấm chọn) — mà rule hover lại đúng là loại khó mò nhất.
   * Ghi lại tên đã bỏ để UI gắn nhãn "trạng thái :hover".
   */
  var STATE_PSEUDO = /:(hover|focus|focus-within|focus-visible|active|visited|target|checked|disabled|enabled|valid|invalid|required|optional|placeholder-shown|autofill|default|indeterminate|read-only|read-write|user-invalid|user-valid)\b/gi;

  /** Pseudo-element không khớp được bằng el.matches() — bỏ để rule ::before vẫn truy ra chủ nhân. */
  var PSEUDO_EL = /::?(before|after|first-line|first-letter|marker|placeholder|selection|backdrop|file-selector-button)\b(\([^)]*\))?/gi;

  // ───────────────────────────────────────────────────────────────────────────
  // Parser
  // ───────────────────────────────────────────────────────────────────────────

  /**
   * Tách văn bản CSS thành danh sách rule kèm số dòng (1-based) của dòng mở selector.
   * Trả [{ selector, body, line, at }] — `at` là chuỗi at-rule bao ngoài ("@media (max-width:768px)")
   * để UI nói rõ rule chỉ chạy ở một breakpoint.
   *
   * Đủ dùng cho CSS phẳng + @media/@supports lồng. CSS nesting kiểu `&` trong ngoặc thì rule con
   * hiện ra như rule riêng với selector chưa nối — chấp nhận được vì vẫn chỉ đúng dòng cần sửa.
   */
  function parse(src) {
    if (!src || typeof src !== 'string') return [];

    var rules = [];
    var stack = [];
    var buf = '';
    var bufLine = 0;
    var line = 1;
    var i = 0;
    var n = src.length;

    function pushBuf(ch) {
      if (!buf && !/\s/.test(ch)) bufLine = line;
      buf += ch;
    }

    while (i < n) {
      var ch = src.charAt(i);

      // Comment: nuốt trọn, chỉ đếm xuống dòng.
      if (ch === '/' && src.charAt(i + 1) === '*') {
        var end = src.indexOf('*/', i + 2);
        if (end < 0) end = n;
        for (var c = i; c < end; c++) if (src.charAt(c) === '\n') line++;
        i = end + 2;
        continue;
      }

      // Chuỗi: giữ nguyên vào buf để dấu { } trong url("a{b") không phá cấu trúc.
      if (ch === '"' || ch === "'") {
        var q = ch;
        var j = i + 1;
        while (j < n) {
          var cj = src.charAt(j);
          if (cj === '\\') { j += 2; continue; }
          if (cj === '\n') line++;
          if (cj === q) break;
          j++;
        }
        buf += src.slice(i, Math.min(j + 1, n));
        i = j + 1;
        continue;
      }

      if (ch === '{') {
        var prelude = buf.trim();
        if (prelude.charAt(0) === '@') {
          stack.push({ at: prelude });
        } else {
          stack.push({ sel: prelude, line: bufLine || line, start: i + 1 });
        }
        buf = ''; bufLine = 0; i++;
        continue;
      }

      if (ch === '}') {
        var top = stack.pop();
        if (top && top.sel != null && top.sel !== '') {
          rules.push({
            selector: top.sel,
            body: src.slice(top.start, i).trim(),
            line: top.line,
            at: stack.filter(function (s) { return s.at; })
                     .map(function (s) { return s.at; })
                     .join(' ')
          });
        }
        buf = ''; bufLine = 0; i++;
        continue;
      }

      if (ch === '\n') { line++; buf += ch; i++; continue; }
      pushBuf(ch);
      i++;
    }

    return rules;
  }

  /** Tách thân rule thành [{prop, value}]. Bỏ qua khai báo cụt để không hiện dòng rác. */
  function declarations(body) {
    if (!body) return [];
    var out = [];
    var depth = 0;
    var cur = '';
    for (var i = 0; i < body.length; i++) {
      var ch = body.charAt(i);
      if (ch === '(') depth++;
      else if (ch === ')') depth--;
      if (ch === ';' && depth <= 0) { flush(cur); cur = ''; continue; }
      cur += ch;
    }
    flush(cur);
    return out;

    function flush(text) {
      var t = (text || '').trim();
      if (!t) return;
      var k = t.indexOf(':');
      if (k <= 0) return;
      out.push({ prop: t.slice(0, k).trim(), value: t.slice(k + 1).trim() });
    }
  }

  // ───────────────────────────────────────────────────────────────────────────
  // Khớp selector
  // ───────────────────────────────────────────────────────────────────────────

  /**
   * Bỏ pseudo-element + pseudo-class trạng thái để el.matches() trả lời được câu hỏi thật sự cần:
   * "rule này có nhắm vào phần tử đang chọn không", chứ không phải "có đang hover không".
   * Trả { test, states } — states là các trạng thái đã gỡ, để UI ghi nhãn.
   */
  function testableSelector(sel) {
    var states = [];
    var out = String(sel || '')
      .replace(PSEUDO_EL, '')
      .replace(STATE_PSEUDO, function (_m, name) {
        if (states.indexOf(name) < 0) states.push(name);
        return '';
      })
      .trim();
    return { test: out, states: states };
  }

  /** true nếu bất kỳ selector nào trong danh sách nhắm vào el. Selector hỏng thì bỏ qua, không ném. */
  function matchesAny(el, selectors) {
    for (var i = 0; i < selectors.length; i++) {
      var t = testableSelector(selectors[i]).test;
      if (!t) continue;
      try { if (el.matches(t)) return true; } catch (e) { /* selector không hợp lệ */ }
    }
    return false;
  }

  /**
   * Độ ưu tiên (a,b,c) = id / class-attr-pseudo / element-pseudoElement.
   * Xấp xỉ: nội dung trong ngoặc của :is()/:not() không tính đệ quy. Dùng để XẾP THỨ TỰ hiển thị,
   * không dùng để quyết định render, nên sai số ở ca hiếm là chấp nhận được.
   */
  function specificity(sel) {
    var s = String(sel || '').replace(/\([^()]*\)/g, '()');
    var a = (s.match(/#[\w-]+/g) || []).length;
    var b = (s.match(/\.[\w-]+|\[[^\]]*\]|:(?!:)[\w-]+/g) || []).length;
    var c = (s.match(/::[\w-]+/g) || []).length +
            (s.match(/(^|[\s>+~,])([a-z][\w-]*)/gi) || []).length;
    return [a, b, c];
  }

  function specValue(spec) { return spec[0] * 10000 + spec[1] * 100 + spec[2]; }

  // ───────────────────────────────────────────────────────────────────────────
  // Thu thập
  // ───────────────────────────────────────────────────────────────────────────

  /**
   * Tìm mọi rule đang nhắm vào `el`.
   *
   * `sources` là mảng, mỗi phần tử:
   *   { id, kind, label, editable, order, text?, rules?, scope?, meta? }
   *   - text   : văn bản CSS nguồn → parse ra số dòng (nguồn sửa được);
   *   - rules  : [{selector, body}] dựng sẵn (Style Manager, CSSOM) — không có số dòng;
   *   - scope  : hàm (selector) → mảng selector THẬT trong canvas. Dùng cho CSS riêng khối, nơi văn
   *              bản nguồn viết `.title` nhưng trong canvas là `[data-nc-sid="x"] .title`. Báo cáo
   *              vẫn giữ selector gốc để người dùng thấy đúng thứ họ đã gõ.
   *   - order  : vị trí của nguồn trong thứ tự cascade (nhỏ = nạp trước = ưu tiên thấp hơn).
   *
   * Trả về mảng đã xếp từ ưu tiên THẤP đến CAO, kèm cờ `winning` cho từng khai báo: khai báo cuối
   * cùng của một property trong danh sách đã xếp là khai báo thắng.
   */
  function collect(el, sources) {
    if (!el || !el.matches) return [];
    var found = [];

    (sources || []).forEach(function (src) {
      var list = src.rules || parse(src.text);
      list.forEach(function (rule, idx) {
        var rawSelectors = splitSelectors(rule.selector);
        if (!rawSelectors.length) return;

        // Selector dùng để THỬ khớp có thể khác selector HIỂN THỊ (trường hợp CSS riêng khối).
        var probe = [];
        rawSelectors.forEach(function (s) {
          if (src.scope) probe = probe.concat(src.scope(s) || []);
          else probe.push(s);
        });
        if (!matchesAny(el, probe)) return;

        var states = [];
        rawSelectors.forEach(function (s) {
          testableSelector(s).states.forEach(function (st) {
            if (states.indexOf(st) < 0) states.push(st);
          });
        });

        var spec = rawSelectors
          .map(specificity)
          .reduce(function (best, cur) { return specValue(cur) > specValue(best) ? cur : best; });

        found.push({
          sourceId: src.id,
          kind: src.kind,
          sourceLabel: src.label,
          editable: !!src.editable,
          meta: src.meta || null,
          selector: rule.selector,
          body: rule.body,
          decls: declarations(rule.body),
          line: rule.line || 0,
          at: rule.at || '',
          states: states,
          spec: spec,
          _order: [src.order || 0, specValue(spec), idx]
        });
      });
    });

    found.sort(function (a, b) {
      for (var i = 0; i < 3; i++) {
        if (a._order[i] !== b._order[i]) return a._order[i] - b._order[i];
      }
      return 0;
    });

    markWinners(found);
    return found;
  }

  /**
   * Đánh dấu khai báo nào thật sự thắng. Đi từ ưu tiên cao xuống thấp, property nào đã gặp rồi thì
   * lần gặp sau là bị ghi đè. Rule có @media hoặc pseudo trạng thái KHÔNG tham gia tranh chấp: nó chỉ
   * áp trong điều kiện riêng, đánh dấu "bị ghi đè" ở đây sẽ là nói sai.
   */
  function markWinners(list) {
    var seen = {};
    for (var i = list.length - 1; i >= 0; i--) {
      var r = list[i];
      var conditional = !!r.at || r.states.length > 0;
      r.conditional = conditional;
      r.winning = false;
      r.decls.forEach(function (d) {
        if (conditional) { d.winning = null; return; }
        var key = d.prop.toLowerCase();
        if (seen[key]) { d.winning = false; return; }
        seen[key] = true;
        d.winning = true;
        r.winning = true;
      });
    }
  }

  /** Tách "a, b:is(c,d)" thành ["a", "b:is(c,d)"] — không cắt nhầm dấu phẩy trong ngoặc. */
  function splitSelectors(text) {
    var out = [];
    var depth = 0;
    var cur = '';
    var s = String(text || '');
    for (var i = 0; i < s.length; i++) {
      var ch = s.charAt(i);
      if (ch === '(' || ch === '[') depth++;
      else if (ch === ')' || ch === ']') depth--;
      if (ch === ',' && depth <= 0) { if (cur.trim()) out.push(cur.trim()); cur = ''; continue; }
      cur += ch;
    }
    if (cur.trim()) out.push(cur.trim());
    return out;
  }

  /**
   * Rule của các sheet chỉ-đọc trong canvas (theme, Tailwind runtime, file .css nạp qua <link>).
   * Bỏ qua sheet ném SecurityError (khác origin) và các sheet do chính builder tạo — chúng đã có
   * nguồn văn bản riêng, đọc lại ở đây sẽ hiện trùng.
   */
  function readOnlySheets(doc, skipIds) {
    var out = [];
    if (!doc || !doc.styleSheets) return out;
    var skip = skipIds || [];

    Array.prototype.forEach.call(doc.styleSheets, function (sheet, sheetIdx) {
      var owner = sheet.ownerNode;
      if (owner && owner.id && skip.indexOf(owner.id) >= 0) return;

      var rules;
      try { rules = sheet.cssRules; } catch (e) { return; } // khác origin
      if (!rules) return;

      var flat = [];
      flatten(rules, '');
      if (!flat.length) return;

      out.push({
        id: 'sheet:' + sheetIdx,
        kind: 'readonly',
        label: sheetLabel(sheet, owner),
        editable: false,
        order: sheetIdx,
        rules: flat
      });

      function flatten(collection, at) {
        Array.prototype.forEach.call(collection, function (rule) {
          if (rule.type === 1 /* STYLE_RULE */) {
            flat.push({ selector: rule.selectorText, body: rule.style.cssText, line: 0, at: at });
          } else if (rule.cssRules) {
            var cond = rule.conditionText
              ? '@' + (rule.media ? 'media' : 'supports') + ' ' + rule.conditionText
              : at;
            flatten(rule.cssRules, cond);
          }
        });
      }
    });

    return out;
  }

  function sheetLabel(sheet, owner) {
    if (sheet.href) {
      try { return sheet.href.split('/').pop().split('?')[0]; } catch (e) { return sheet.href; }
    }
    if (owner && owner.id) return owner.id;
    return 'CSS của theme';
  }

  global.ncCssInspector = {
    parse: parse,
    declarations: declarations,
    splitSelectors: splitSelectors,
    testableSelector: testableSelector,
    specificity: specificity,
    collect: collect,
    readOnlySheets: readOnlySheets
  };
})(typeof window !== 'undefined' ? window : this);
