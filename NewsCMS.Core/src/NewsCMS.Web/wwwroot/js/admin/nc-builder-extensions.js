/*
 * nc-builder-extensions.js — phần mở rộng Site Builder cho GrapesJS (NewsCMS).
 *
 * Nạp SAU grapes.min.js, nc-tailwind.js và nc-css-inspector.js, TRƯỚC script init editor trong
 * Edit.cshtml.
 * API công khai: window.ncBuilder.register(editor, opts)
 *   opts: {
 *     token:        anti-forgery token (header RequestVerificationToken),
 *     blocks:       catalog khối động từ GET /admin/api/builder/blocks — mỗi khối C# tự mô tả
 *                   mình (label/category/icon/presets/props kèm options đã giải quyết),
 *     siteCss:      CSS chung của site đang chạy trong canvas — css-inspector cần văn bản gốc để
 *                   chỉ ra rule nằm ở dòng nào,
 *     hasCodeScope: bool                     — user có quyền Builder.Code.Manage,
 *     onChange:     fn()                     — gọi mỗi khi canvas đổi (để đánh dấu "chưa lưu").
 *   }
 *
 * Gồm 5 phần:
 *   1. Block "Dữ liệu" — dựng TỪ catalog server trả về, kèm panel cấu hình props (data-nc-props)
 *      xếp theo nhóm + preset kiểu hiển thị + preview nội dung thật.
 *   2. Kiểu hiển thị (style variant) — chọn class variant trên section: mặc định/trung tâm/thẻ tối/
 *      dải màu thương hiệu; CSS của variant nằm trong CompiledCss nên trang public render y hệt.
 *   2c. Truy vết CSS — chọn một DOM là thấy mọi rule đang nhắm vào nó, ở nguồn nào, dòng nào, và
 *      mở thẳng editor tại đó. Không còn phải mò CSS nằm ở đâu.
 *   3. Custom Code per page — nút "Code" mở modal CodeMirror cho CSS/JS riêng trang, cộng tab
 *      "CSS site" sửa CSS dùng chung ngay trong builder.
 *      JS cần quyền Builder.Code.Manage (server chặn ở BuilderPageApiController qua service? —
 *      không: API PUT nhận customJs không kiểm quyền chi tiết; UI ẩn field khi thiếu scope).
 *   4. Library code — snippet CSS/JS dùng sẵn chèn một click vào editor đang mở.
 */
(function (global) {
  'use strict';

  // ── Trạng thái module ────────────────────────────────────────────────────────
  var OPTS = { token: '', blocks: [], sitePresets: [], siteCss: '', hasCodeScope: false, onChange: null, sampleEntityId: null, sampleType: null, pageKind: '' };

  // ── Tiện ích ────────────────────────────────────────────────────────────────
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  /** Đọc data-nc-props hiện có của component (attribute lưu dạng HTML-escaped JSON). */
  function readProps(comp) {
    try {
      var raw = comp.getAttributes()['data-nc-props'];
      if (!raw) return {};
      return JSON.parse(raw);
    } catch (e) { return {}; }
  }

  function writeProps(comp, props) {
    var cur = comp.getAttributes() || {};
    cur['data-nc-props'] = JSON.stringify(props || {});
    comp.setAttributes(cur);
    notifyChange();
  }

  function notifyChange() {
    if (OPTS.onChange) OPTS.onChange();
  }

  // ── Beautify/minify (nc-code-format.js) ────────────────────────────────────
  // Beautify bản đồng bộ: lib chưa nạp thì trả nguyên văn — editor và css-inspector
  // gọi CÙNG hàm này nên số dòng jump-to-line không bao giờ lệch nhau.
  function fmtBeautify(text, lang) {
    var src = text == null ? '' : String(text);
    return global.ncCodeFormat ? global.ncCodeFormat.beautify(src, lang) : src;
  }
  /** Minify khi lưu nếu người dùng đang bật; không bao giờ throw (lỗi parse giữ nguyên văn). */
  function fmtForSave(text, lang, label) {
    var src = text == null ? '' : String(text);
    return global.ncCodeFormat ? global.ncCodeFormat.forSave(src, lang, label) : src;
  }

  /**
   * Gọi API preview để lấy HTML thật của block theo props hiện tại.
   * Trả { html, matchedCount } — matchedCount là số bản ghi KHỚP bộ lọc (bỏ qua giới hạn hiển thị),
   * null khi khối không đếm được. Lỗi mạng trả HTML thông báo để canvas không im lặng.
   */
  function fetchPreview(key, props) {
    var payload = {
      propsJson: JSON.stringify(props || {})
    };
    if (OPTS.sampleEntityId) {
      payload.sampleEntityId = OPTS.sampleEntityId;
      payload.sampleType = OPTS.sampleType || null;
    }
    return fetch('/admin/api/builder/block-preview/' + encodeURIComponent(key), {
      method: 'POST',
      headers: {
        'RequestVerificationToken': OPTS.token,
        'Content-Type': 'application/json'
      },
      credentials: 'include',
      body: JSON.stringify(payload)
    }).then(function (res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    }).then(function (d) {
      return { html: d.html || '', matchedCount: (typeof d.matchedCount === 'number' ? d.matchedCount : null) };
    }).catch(function () {
      return {
        html: '<div style="padding:16px;color:#94a3b8;font-family:system-ui">Không tải được preview.</div>',
        matchedCount: null
      };
    });
  }

  /** Chờ GrapesJS render xong frame rồi chạy callback trên document của canvas. */
  function onCanvasReady(editor, fn) {
    var doc = editor.Canvas.getDocument();
    if (doc && doc.body) { fn(doc); return; }
    editor.on('canvas:frame:load', function () { fn(editor.Canvas.getDocument()); });
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 1) KHỐI DỮ LIỆU ĐỘNG
  // ═════════════════════════════════════════════════════════════════════════════

  /**
   * Catalog khối động, do server trả về từ GET /admin/api/builder/blocks — mỗi khối C# tự mô tả
   * mình qua BlockDescriptor. Trước đây danh mục này khai báo lặp lại ở đây, nên thêm khối mà
   * quên sửa file JS là khối biến mất khỏi builder mà không có lỗi nào báo.
   *
   * Chuẩn hoá về hình dạng nội bộ: { key, label, desc, icon, defaults, presets, props }.
   * Prop giữ nguyên nhóm (group) để panel xếp theo trình tự người dùng thật sự đi:
   * kiểu hiển thị → nguồn dữ liệu → hiện những gì → nâng cao.
   */
  function normalizeCatalog(raw) {
    return (raw || []).map(function (b) {
      return {
        key: b.key,
        label: b.label || b.key,
        category: b.category || 'Dữ liệu',
        desc: b.description || '',
        icon: b.icon || '',
        // Loại nội dung khối dùng được; mảng rỗng = hợp mọi loại (xem ncFilterBlocksByType).
        supportedTypes: Array.isArray(b.supportedTypes) ? b.supportedTypes : [],
        defaults: parseJsonObject(b.defaults),
        presets: (b.presets || []).map(function (p) {
          return { key: p.key, label: p.label, thumb: p.thumb || '', props: parseJsonObject(p.props) };
        }),
        props: (b.props || []).map(function (p) {
          return {
            name: p.name,
            type: p.type,
            label: p.label,
            group: p.group || 'data',
            default: coerceDefault(p.type, p['default']),
            options: p.options || [],
            optionsSource: p.optionsSource || null,
            itemSource: p.itemSource || null,
            min: p.min == null ? undefined : p.min,
            max: p.max == null ? undefined : p.max,
            placeholder: p.placeholder || '',
            hint: p.hint || ''
          };
        })
      };
    });
  }

  function parseJsonObject(text) {
    if (!text) return {};
    if (typeof text === 'object') return text;
    try {
      var v = JSON.parse(text);
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  /**
   * Server gửi mặc định dạng CHUỖI (một attribute JSON phải giữ nguyên văn cho mọi kiểu prop);
   * client đổi về kiểu thật để so sánh "bằng mặc định thì lược khỏi data-nc-props" cho đúng.
   */
  function coerceDefault(type, raw) {
    if (raw === undefined || raw === null) {
      return type === 'checkbox' ? false : (type === 'multi-select' || type === 'item-picker' ? [] : undefined);
    }
    if (type === 'number') { var n = Number(raw); return isNaN(n) ? undefined : n; }
    if (type === 'checkbox') return raw === true || raw === 'true' || raw === 1 || raw === '1';
    return raw;
  }

  /** Thứ tự nhóm trong panel + nhãn tiêu đề chèn giữa các nhóm. */
  var PROP_GROUPS = [
    { id: 'display', label: 'Kiểu hiển thị' },
    { id: 'data', label: 'Nguồn dữ liệu' },
    { id: 'content', label: 'Hiện những gì' },
    { id: 'advanced', label: 'Nâng cao' }
  ];

  /**
   * Loại nội dung mà trang đang mở phục vụ, suy từ PageKind. Trang thường (Landing/Static) trả ''
   * — khi đó không lọc gì, vì khối entity-* vẫn dùng được nếu người dùng biết mình đang làm gì
   * (chúng tự hiện placeholder khi không có RouteContext).
   */
  function currentContentType() {
    switch ((OPTS.pageKind || '').toLowerCase()) {
      case 'posttemplate': return 'post';
      case 'producttemplate': return 'product';
      case 'categorytemplate': return 'category';
      default: return '';
    }
  }

  /**
   * Khối có dùng được trong ngữ cảnh trang hiện tại không.
   *
   * Vì sao cần: khối chuyên biệt như product-price kiểm RouteType rồi trả CHUỖI RỖNG khi sai loại
   * — không lỗi, không cảnh báo, chỉ là một khoảng trống. Người dùng kéo vào template bài viết
   * rồi ngồi đoán vì sao không thấy gì. Chặn ngay ở palette rẻ hơn nhiều so với việc đi tìm sau.
   */
  function blockFitsPage(def) {
    var types = def.supportedTypes || [];
    if (!types.length) return true;            // không khai = hợp mọi loại

    var current = currentContentType();
    if (!current) return true;                 // trang thường: không lọc

    return types.some(function (t) {
      return String(t).toLowerCase() === current;
    });
  }

  function registerDynamicComponents(editor) {
    // Component type phải đăng ký cho MỌI khối, kể cả khối bị lọc khỏi palette: BuilderJson của
    // trang có thể đã chứa placeholder đó từ trước (hoặc do đổi Kind sau khi dựng), thiếu type là
    // GrapesJS parse nó thành div trơ, mất trait và mất preview.
    var defs = normalizeCatalog(OPTS.blocks);
    if (!defs.length) return;

    defs.forEach(function (def) {
      // Component type: nhận diện placeholder data-nc-block, khoá không cho thả nội dung vào.
      editor.DomComponents.addType('nc-block-' + def.key, {
        isComponent: function (el) {
          if (el.tagName === 'DIV' && el.hasAttribute &&
              el.getAttribute('data-nc-block') === def.key) {
            return { type: 'nc-block-' + def.key };
          }
          return false;
        },
        model: {
          defaults: {
            tagName: 'div',
            attributes: { 'data-nc-block': def.key, 'data-nc-props': JSON.stringify(def.defaults || {}) },
            droppable: false,
            editable: false,
            highlightable: true,
            name: def.label,
            traits: buildPropTraits(def)
          },
          init() {
            this.listenTo(this, 'change:attributes:data-nc-props', this.refreshPreview);
          },
          refreshPreview() {
            var el = this.view && this.view.el;
            if (el) schedulePreview(editor, this, el, def.key, readProps(this));
          }
        },
        view: {
          onRender() {
            schedulePreview(editor, this.model, this.el, def.key, readProps(this.model));
          }
        }
      });

      // Khối sai loại nội dung KHÔNG vào palette (type ở trên vẫn đăng ký).
      if (!blockFitsPage(def)) return;

      // Block kéo-thả: content là placeholder HTML — renderer server thay ruột lúc render.
      editor.BlockManager.add('nc-dyn-' + def.key, {
        label: '<div class="ncblk"><span class="ncblk-icon">' + def.icon + '</span>' +
               '<span class="ncblk-label">' + esc(def.label) + '</span></div>',
        category: def.category || 'Dữ liệu',
        media: '',
        attributes: { title: def.desc || def.label },
        content: {
          type: 'nc-block-' + def.key,
          components: previewPlaceholderHtml(def),
          attributes: { 'data-nc-block': def.key, 'data-nc-props': JSON.stringify(def.defaults || {}) }
        }
      });
    });

    registerSitePresets(editor, defs);

    // Sau khi load project từ server, mọi node data-nc-block trong BuilderJson phải được
    // gán đúng type (isComponent chỉ chạy khi parse HTML string). LoadProjectData đi đường
    // khác nên tự quán tay.
    editor.on('load', function () {
      onCanvasReady(editor, function () { /* nothing extra — types attach khi parse */ });
    });
  }

  /**
   * Preset RIÊNG CỦA SITE (Phase 6.4): thêm ô kéo-thả có props gắn sẵn. KHÔNG tạo component type
   * mới — placeholder mang data-nc-block = handler của khối gốc nên dùng lại type đã đăng ký ở
   * registerDynamicComponents (preview, panel props, đếm khớp… đều theo khối gốc). Xếp riêng
   * category "Preset của site" để tách khỏi khối gốc.
   */
  function registerSitePresets(editor, defs) {
    var byKey = {};
    defs.forEach(function (d) { byKey[d.key] = d; });

    (OPTS.sitePresets || []).forEach(function (p) {
      var base = byKey[p.handler];
      if (!base) return; // handler không khớp khối động nào đang có — bỏ qua.
      // Preset dùng lại khối gốc, nên preset của product-price cũng vô dụng trên template bài viết.
      if (!blockFitsPage(base)) return;
      var icon = p.icon || base.icon || '';
      editor.BlockManager.add('nc-preset-' + p.key, {
        label: '<div class="ncblk"><span class="ncblk-icon">' + icon + '</span>' +
               '<span class="ncblk-label">' + esc(p.label) + '</span></div>',
        category: 'Preset của site',
        media: '',
        attributes: { title: 'Preset: ' + p.label },
        content: {
          type: 'nc-block-' + p.handler,
          components: previewPlaceholderHtml(base),
          attributes: { 'data-nc-block': p.handler, 'data-nc-props': p.props || '{}' }
        }
      });
    });
  }

  function previewPlaceholderHtml(def) {
    return '<div style="padding:28px;text-align:center;color:#64748b;font-family:system-ui,sans-serif;border:2px dashed #cbd5e1;border-radius:12px;background:#f8fafc">' +
           '<div style="font-weight:700;margin-bottom:4px">' + esc(def.label) + '</div>' +
           '<div style="font-size:.85rem">Đang tải nội dung mẫu…</div></div>';
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 1b) KHỐI BỐ CỤC CƠ BẢN (Phase 2.6)
  // ═════════════════════════════════════════════════════════════════════════════

  /*
   * Khối bố cục tĩnh: section/container/grid/heading/text/nút/ảnh/khoảng trống/divider.
   * Dựng bằng class Tailwind + biến token của site (var(--color-brand-500), var(--radius-card))
   * để bám design system, và nằm ngay trong CompiledHtml nên trang public render y hệt canvas.
   * Đặt category "Bố cục" tách khỏi "Dữ liệu" cho dễ tìm.
   */
  function layoutBlockDefs() {
    var ic = function (svg) {
      return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + svg + '</svg>';
    };
    var gridCols = function (n) {
      var cells = '';
      for (var i = 0; i < n; i++) {
        cells += '<div style="min-height:80px;background:var(--color-subtle,#f1f5f9);border-radius:var(--radius-card,12px);padding:16px"></div>';
      }
      return '<div style="display:grid;grid-template-columns:repeat(' + n + ',minmax(0,1fr));gap:20px">' + cells + '</div>';
    };
    return [
      {
        key: 'section',
        label: 'Section',
        icon: ic('<rect x="3" y="4" width="18" height="16" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/>'),
        content: '<section style="padding:64px 24px"><div style="max-width:1120px;margin:0 auto"></div></section>'
      },
      {
        key: 'container',
        label: 'Container',
        icon: ic('<rect x="5" y="4" width="14" height="16" rx="2"/>'),
        content: '<div style="max-width:1120px;margin:0 auto;padding:0 24px"></div>'
      },
      {
        key: 'grid-2',
        label: 'Grid 2 cột',
        icon: ic('<rect x="3" y="4" width="7.5" height="16" rx="1"/><rect x="13.5" y="4" width="7.5" height="16" rx="1"/>'),
        content: gridCols(2)
      },
      {
        key: 'grid-3',
        label: 'Grid 3 cột',
        icon: ic('<rect x="3" y="4" width="5" height="16" rx="1"/><rect x="9.5" y="4" width="5" height="16" rx="1"/><rect x="16" y="4" width="5" height="16" rx="1"/>'),
        content: gridCols(3)
      },
      {
        key: 'grid-4',
        label: 'Grid 4 cột',
        icon: ic('<rect x="3" y="5" width="3.6" height="14" rx="1"/><rect x="7.8" y="5" width="3.6" height="14" rx="1"/><rect x="12.6" y="5" width="3.6" height="14" rx="1"/><rect x="17.4" y="5" width="3.6" height="14" rx="1"/>'),
        content: gridCols(4)
      },
      {
        key: 'heading',
        label: 'Tiêu đề',
        icon: ic('<path d="M6 4v16M18 4v16M6 12h12"/>'),
        content: '<h2 style="font-family:var(--font-display,inherit);font-size:2rem;color:var(--color-brand-500,#0f172a);margin:0 0 12px">Tiêu đề mới</h2>'
      },
      {
        key: 'text',
        label: 'Đoạn văn',
        icon: ic('<line x1="4" y1="6" x2="20" y2="6"/><line x1="4" y1="12" x2="20" y2="12"/><line x1="4" y1="18" x2="14" y2="18"/>'),
        content: '<p style="color:var(--color-muted,#475569);line-height:1.7;margin:0 0 16px">Nhập nội dung đoạn văn tại đây. Nhấp đúp để sửa trực tiếp.</p>'
      },
      {
        key: 'button',
        label: 'Nút',
        icon: ic('<rect x="3" y="8" width="18" height="8" rx="4"/>'),
        content: '<a href="#" class="chu-cta" style="display:inline-block;padding:14px 32px;border-radius:999px;background:var(--color-brand-500,#0d7c66);color:#fff;font-weight:700;text-decoration:none">Nút bấm</a>'
      },
      {
        key: 'image',
        label: 'Ảnh',
        icon: ic('<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.5"/><path d="m21 16-5-5L5 20"/>'),
        content: { type: 'image', style: { 'max-width': '100%', 'border-radius': 'var(--radius-card,12px)', display: 'block' } }
      },
      {
        key: 'spacer',
        label: 'Khoảng trống',
        icon: ic('<line x1="12" y1="4" x2="12" y2="20"/><polyline points="7 8 12 4 17 8"/><polyline points="7 16 12 20 17 16"/>'),
        content: '<div style="height:48px" data-nc-spacer="1"></div>'
      },
      {
        key: 'divider',
        label: 'Đường kẻ',
        icon: ic('<line x1="3" y1="12" x2="21" y2="12"/>'),
        content: '<hr style="border:0;border-top:1px solid rgba(15,23,42,.12);margin:32px 0">'
      }
    ];
  }

  function registerLayoutBlocks(editor) {
    layoutBlockDefs().forEach(function (def) {
      editor.BlockManager.add('nc-layout-' + def.key, {
        label: '<div class="ncblk"><span class="ncblk-icon">' + def.icon + '</span>' +
               '<span class="ncblk-label">' + esc(def.label) + '</span></div>',
        category: 'Bố cục',
        media: '',
        content: def.content
      });
    });
  }


  // Hàng đợi preview: gom nhiều request cùng frame để không spam API.
  var previewTimers = new WeakMap();

  /**
   * Số bản ghi khớp bộ lọc của từng khối, do endpoint preview trả về.
   * WeakMap theo component (không phải thuộc tính model) để con số này KHÔNG bị ghi vào
   * BuilderJson — nó là thông tin phái sinh, lưu vào trang thì lần mở sau sẽ hiện số cũ đã sai.
   */
  var matchInfo = new WeakMap();

  function schedulePreview(editor, component, el, key, props) {
    if (previewTimers.has(el)) clearTimeout(previewTimers.get(el));
    // Đánh dấu đang tải NGAY, không chờ hết debounce: 250ms im lặng khiến người dùng
    // tưởng thao tác vừa rồi không ăn.
    markLoading(editor, el);
    var t = setTimeout(function () {
      previewTimers.delete(el);
      fetchPreview(key, props).then(function (res) {
        setMatchInfo(component, res.matchedCount, props);
        injectPreview(editor, el, res.html);
      });
    }, 250);
    previewTimers.set(el, t);
  }

  /**
   * Trạng thái đang tải: khối đã có nội dung thì chỉ làm mờ (giữ bố cục, không nhá trắng);
   * khối còn trống/placeholder thì hiện skeleton để chỗ đó không phải một vùng trắng vô nghĩa.
   */
  function markLoading(editor, el) {
    if (!el) return;
    el.setAttribute('data-nc-loading', '1');
    var hasContent = el.querySelector('*') && !el.querySelector('[data-nc-skeleton]');
    if (hasContent) { el.style.opacity = '.5'; return; }
    ensureSkeletonStyle(editor);
    el.innerHTML = skeletonHtml();
  }

  function clearLoading(el) {
    if (!el) return;
    el.removeAttribute('data-nc-loading');
    el.style.opacity = '';
  }

  function skeletonHtml() {
    var tile = '<div class="nc-sk-tile"><div class="nc-sk-img"></div>' +
               '<div class="nc-sk-line" style="width:80%"></div>' +
               '<div class="nc-sk-line" style="width:55%"></div></div>';
    return '<div data-nc-skeleton="1" class="nc-sk">' + tile + tile + tile + '</div>';
  }

  /** Keyframes cho skeleton phải nằm TRONG document của canvas — iframe không thấy CSS trang admin. */
  function ensureSkeletonStyle(editor) {
    var doc = editor.Canvas.getDocument();
    if (!doc || doc.getElementById('nc-sk-style')) return;
    var st = doc.createElement('style');
    st.id = 'nc-sk-style';
    st.textContent =
      '.nc-sk{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:16px;padding:8px 0}' +
      '.nc-sk-tile{border:1px solid #e2e8f0;border-radius:12px;padding:12px;background:#fff}' +
      '.nc-sk-img{height:150px;border-radius:8px;margin-bottom:10px}' +
      '.nc-sk-line{height:12px;border-radius:6px;margin-bottom:8px}' +
      '.nc-sk-img,.nc-sk-line{background:linear-gradient(90deg,#eef2f6 25%,#e2e8f0 37%,#eef2f6 63%);' +
      'background-size:400% 100%;animation:nc-sk-shine 1.3s ease-in-out infinite}' +
      '@keyframes nc-sk-shine{0%{background-position:100% 50%}100%{background-position:0 50%}}';
    doc.head.appendChild(st);
  }

  // ── "Khớp 12 bài, hiển thị 6" ────────────────────────────────────────────────

  /**
   * Ghi lại số khớp rồi vẽ lại dòng thông tin trên panel. Số "hiển thị" tính từ props
   * (count/skip) chứ không đếm DOM: khối có thể render kèm caption/script nên đếm phần tử
   * không đáng tin, còn count/skip chính là điều server áp lên truy vấn.
   */
  function setMatchInfo(component, matched, props) {
    if (!component) return;
    if (matched == null) { matchInfo.delete(component); }
    else {
      var count = Number((props || {}).count);
      var skip = Number((props || {}).skip) || 0;
      matchInfo.set(component, {
        matched: matched,
        shown: Math.max(0, Math.min(isNaN(count) ? matched : count, matched - skip))
      });
    }
    renderMatchInfo(component);
  }

  function renderMatchInfo(component) {
    if (!component || !component.getId) return;
    var host = document.querySelector('.nc-match[data-nc-for="' + component.getId() + '"]');
    if (!host) return; // panel của khối này không đang mở — lần mở sau trait tự đọc lại.
    var info = matchInfo.get(component);
    if (!info) { host.style.display = 'none'; host.textContent = ''; return; }

    var unit = host.getAttribute('data-nc-unit') || 'bản ghi';
    host.style.display = '';
    host.className = 'nc-match' + (info.matched === 0 ? ' nc-match-empty' : '');
    host.textContent = info.matched === 0
      ? 'Không có ' + unit + ' nào khớp bộ lọc — nới bộ lọc hoặc bỏ "chỉ nổi bật".'
      : 'Khớp ' + info.matched + ' ' + unit + ', hiển thị ' + info.shown + '.';
  }

  /**
   * Dòng "Khớp N …, hiển thị M". Không phải ô nhập nên không ghi props; nó tồn tại vì lọc ra 0
   * kết quả mà panel im lặng thì người dùng không biết do bộ lọc sai hay site chưa có dữ liệu.
   */
  function makeMatchCountTrait(editor) {
    editor.TraitManager.addType('nc-match-count', {
      noLabel: true,
      createInput({ component, trait }) {
        var el = document.createElement('div');
        el.className = 'nc-match';
        el.setAttribute('data-nc-for', component.getId());
        el.setAttribute('data-nc-unit', trait.get('unit') || 'bản ghi');
        el.style.display = 'none';
        // Vẽ ngay ở tick sau: element phải vào DOM trước khi renderMatchInfo querySelector được.
        setTimeout(function () { renderMatchInfo(component); }, 0);
        return el;
      },
      onEvent() {}
    });
  }

  /**
   * Đơn vị đếm lấy từ nhãn prop "count" của chính khối ("Số bài hiển thị" → "bài") thay vì thêm
   * một field vào BlockDescriptor: nhãn đó vốn đã nói khối này đếm cái gì.
   */
  function unitOf(def) {
    var c = (def.props || []).filter(function (p) { return p.name === 'count'; })[0];
    if (!c || !c.label) return 'bản ghi';
    return c.label.replace(/^Số\s+/i, '').replace(/\s*hiển thị\s*$/i, '').trim().toLowerCase() || 'bản ghi';
  }

  function injectPreview(editor, hostEl, html) {
    onCanvasReady(editor, function () {
      clearLoading(hostEl);
      // Server trả comment (vd "<!-- news-grid: no posts -->") hoặc rỗng khi không có dữ liệu và
      // người dùng chưa đặt emptyText → hiện trạng thái rỗng rõ ràng thay vì khối trắng, vì khối
      // trắng khiến người dùng tưởng builder hỏng (Phase 3.4 của plan).
      var visible = html.replace(/<!--[\s\S]*?-->/g, '').trim();
      if (!visible) {
        hostEl.innerHTML =
          '<div style="padding:24px;text-align:center;color:#94a3b8;font-family:system-ui,sans-serif;' +
          'border:2px dashed #cbd5e1;border-radius:12px;background:#f8fafc;font-size:.9rem">' +
          'Chưa có dữ liệu khớp cấu hình. Đổi chuyên mục/bộ lọc, hoặc đặt “Chữ khi không có dữ liệu”.' +
          '</div>';
        return;
      }
      hostEl.innerHTML = html;
      // Preview khối vừa đổi ruột: innerHTML loại bỏ mọi <script> khối tự nhúng (flipbook) và
      // trạng thái khối "ẩn tới khi JS bật" (masonry) chưa được kích. Nhờ Edit.cshtml chạy lại
      // <script> khối + JS site để preview trung thực với trang public. Không có hàm này (vd mở
      // builder độc lập) thì chỉ mất phần hydrate, không lỗi.
      if (typeof window.ncHydrateCanvas === 'function') window.ncHydrateCanvas();
    });
  }

  /** Trait "nút Làm mới preview" dùng chung. */
  function makeRefreshTrait(editor) {
    editor.TraitManager.addType('nc-refresh-btn', {
      noLabel: true,
      createInput({ component }) {
        var wrap = document.createElement('div');
        wrap.style.paddingTop = '4px';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = '↻ Làm mới preview';
        btn.className = 'nc-trait-btn';
        btn.addEventListener('click', function () {
          var hostEl = component && component.view && component.view.el;
          if (!hostEl) return;
          var props = readProps(component);
          markLoading(editor, hostEl);
          fetchPreview(component.getAttributes()['data-nc-block'], props)
            .then(function (res) {
              setMatchInfo(component, res.matchedCount, props);
              injectPreview(editor, hostEl, res.html);
            });
        });
        wrap.appendChild(btn);
        return wrap;
      },
      onEvent() { /* nút bấm, không ghi props */ }
    });
  }

  // ── Trait types dựng từ catalog: tiêu đề nhóm, preset, multi-select, item picker ──────

  /** Tiêu đề chia nhóm trong panel. Không phải ô nhập nên không ghi gì vào props. */
  function makeGroupLabelTrait(editor) {
    editor.TraitManager.addType('nc-group-label', {
      noLabel: true,
      createInput({ trait }) {
        var el = document.createElement('div');
        el.className = 'nc-grp-label';
        el.textContent = trait.get('text') || '';
        return el;
      },
      onEvent() {}
    });
  }

  /**
   * Preset = đặt MỘT LƯỢT nhiều prop. Trả lời câu "muốn nhìn như thế nào" mà không buộc người dùng
   * tự mường tượng kết quả từ layout + columns thô. Chọn preset xong vẫn chỉnh tay từng prop được.
   *
   * Preset chỉ ghi props, KHÔNG sinh class Tailwind mới — bố cục vẫn do server render bằng inline
   * style, nên trang public không phụ thuộc file CSS nào phát sinh thêm.
   */
  function makePresetPickerTrait(editor) {
    editor.TraitManager.addType('nc-preset-picker', {
      createInput({ component, trait }) {
        var presets = trait.get('presets') || [];
        var wrap = document.createElement('div');
        wrap.className = 'nc-variant-wrap nc-preset-wrap';

        presets.forEach(function (p) {
          var opt = document.createElement('button');
          opt.type = 'button';
          opt.className = 'nc-variant-opt nc-preset-opt' + (matchesPreset(component, p) ? ' active' : '');
          opt.title = p.label;
          opt.innerHTML = '<span class="nc-preset-thumb">' + (p.thumb || '') + '</span>' +
                          '<span class="nc-preset-name">' + esc(p.label) + '</span>';
          opt.addEventListener('click', function () {
            applyPreset(component, p);
            wrap.querySelectorAll('.nc-preset-opt').forEach(function (b) { b.classList.remove('active'); });
            opt.classList.add('active');
          });
          wrap.appendChild(opt);
        });
        return wrap;
      },
      onEvent() {}
    });
  }

  /** Preset đang được chọn = mọi prop của preset đều khớp props hiện tại. */
  function matchesPreset(comp, preset) {
    var props = readProps(comp);
    var keys = Object.keys(preset.props || {});
    if (!keys.length) return false;
    return keys.every(function (k) { return String(props[k]) === String(preset.props[k]); });
  }

  function applyPreset(comp, preset) {
    var props = readProps(comp);
    Object.keys(preset.props || {}).forEach(function (k) { props[k] = preset.props[k]; });
    writeProps(comp, props);
    // Traits đang mở đọc lại giá trị mới (số cột, bố cục… vừa bị preset ghi đè).
    if (comp.trigger) comp.trigger('rerender:layer');
  }

  /**
   * Chọn nhiều giá trị dạng chip (chuyên mục, thẻ). Lưu là MẢNG chuỗi trong data-nc-props.
   * Không dùng <select multiple>: trên mobile/trackpad thao tác ctrl-click rất dễ mất lựa chọn cũ.
   */
  function makeMultiSelectTrait(editor) {
    editor.TraitManager.addType('nc-multi-select', {
      createInput({ component, trait }) {
        var f = trait.get('field') || {};
        var wrap = document.createElement('div');
        wrap.className = 'nc-chips';

        var selected = asArray(readProps(component)[f.name]);
        (f.options || []).forEach(function (o) {
          var chip = document.createElement('button');
          chip.type = 'button';
          chip.className = 'nc-chip' + (selected.indexOf(o.value) >= 0 ? ' active' : '');
          chip.textContent = o.name;
          chip.addEventListener('click', function () {
            var props = readProps(component);
            var cur = asArray(props[f.name]);
            var at = cur.indexOf(o.value);
            if (at >= 0) cur.splice(at, 1); else cur.push(o.value);
            writeProp(props, f, cur);
            writeProps(component, props);
            chip.classList.toggle('active', at < 0);
          });
          wrap.appendChild(chip);
        });

        if (!(f.options || []).length) {
          var empty = document.createElement('div');
          empty.className = 'nc-hint';
          empty.textContent = 'Chưa có dữ liệu để chọn.';
          wrap.appendChild(empty);
        }
        return wrap;
      },
      onEvent() {}
    });
  }

  /**
   * Chọn bản ghi cụ thể (bài viết/sản phẩm/ảnh) và KÉO SẮP THỨ TỰ. Thứ tự này có nghĩa thật:
   * server sắp lại theo đúng danh sách (SQL IN không bảo toàn thứ tự).
   */
  function makeItemPickerTrait(editor) {
    editor.TraitManager.addType('nc-item-picker', {
      createInput({ component, trait }) {
        var f = trait.get('field') || {};
        var wrap = document.createElement('div');
        wrap.className = 'nc-picker';

        var list = document.createElement('div');
        list.className = 'nc-picker-list';

        var search = document.createElement('input');
        search.type = 'search';
        search.className = 'nc-picker-search';
        search.placeholder = 'Tìm rồi bấm để thêm…';

        var results = document.createElement('div');
        results.className = 'nc-picker-results';

        wrap.appendChild(list);
        wrap.appendChild(search);
        wrap.appendChild(results);

        function ids() { return asArray(readProps(component)[f.name]); }

        function save(next) {
          var props = readProps(component);
          writeProp(props, f, next);
          writeProps(component, props);
        }

        function renderSelected() {
          var cur = ids();
          list.innerHTML = '';
          if (!cur.length) {
            list.innerHTML = '<div class="nc-hint">Chưa chọn — khối sẽ tự lấy theo bộ lọc bên dưới.</div>';
            return;
          }
          fetchItems(f.itemSource, null, cur).then(function (items) {
            list.innerHTML = '';
            items.forEach(function (it, idx) {
              var row = document.createElement('div');
              row.className = 'nc-picker-row';
              row.draggable = true;
              row.innerHTML =
                (it.thumb ? '<img src="' + esc(it.thumb) + '" alt="">' : '<span class="nc-picker-noimg"></span>') +
                '<span class="nc-picker-label">' + esc(it.label) +
                (it.sub ? '<em>' + esc(it.sub) + '</em>' : '') + '</span>' +
                '<button type="button" class="nc-picker-del" title="Bỏ chọn">×</button>';

              row.querySelector('.nc-picker-del').addEventListener('click', function () {
                var next = ids();
                next.splice(idx, 1);
                save(next);
                renderSelected();
              });

              // Kéo sắp thứ tự: chỉ số nguồn đi kèm để thả biết chèn vào đâu.
              row.addEventListener('dragstart', function (e) {
                e.dataTransfer.setData('text/plain', String(idx));
                row.classList.add('dragging');
              });
              row.addEventListener('dragend', function () { row.classList.remove('dragging'); });
              row.addEventListener('dragover', function (e) { e.preventDefault(); });
              row.addEventListener('drop', function (e) {
                e.preventDefault();
                var from = parseInt(e.dataTransfer.getData('text/plain'), 10);
                if (isNaN(from) || from === idx) return;
                var next = ids();
                next.splice(idx, 0, next.splice(from, 1)[0]);
                save(next);
                renderSelected();
              });

              list.appendChild(row);
            });
          });
        }

        var timer = null;
        search.addEventListener('input', function () {
          if (timer) clearTimeout(timer);
          var term = search.value.trim();
          if (!term) { results.innerHTML = ''; return; }
          timer = setTimeout(function () {
            fetchItems(f.itemSource, term, null).then(function (items) {
              results.innerHTML = '';
              var cur = ids();
              items.filter(function (it) { return cur.indexOf(it.id) < 0; }).forEach(function (it) {
                var row = document.createElement('button');
                row.type = 'button';
                row.className = 'nc-picker-hit';
                row.innerHTML =
                  (it.thumb ? '<img src="' + esc(it.thumb) + '" alt="">' : '<span class="nc-picker-noimg"></span>') +
                  '<span class="nc-picker-label">' + esc(it.label) +
                  (it.sub ? '<em>' + esc(it.sub) + '</em>' : '') + '</span>';
                row.addEventListener('click', function () {
                  var next = ids();
                  next.push(it.id);
                  save(next);
                  search.value = '';
                  results.innerHTML = '';
                  renderSelected();
                });
                results.appendChild(row);
              });
              if (!results.children.length)
                results.innerHTML = '<div class="nc-hint">Không tìm thấy.</div>';
            });
          }, 280);
        });

        renderSelected();
        return wrap;
      },
      onEvent() {}
    });
  }

  /** Vừa dùng để tìm (q), vừa để tra lại nhãn của id đã lưu — nếu không thì panel chỉ có Guid trần. */
  function fetchItems(type, q, ids) {
    var url = '/admin/api/builder/pickers/items?type=' + encodeURIComponent(type || 'post');
    if (q) url += '&q=' + encodeURIComponent(q);
    if (ids && ids.length) url += '&ids=' + encodeURIComponent(ids.join(','));
    return fetch(url, { headers: { 'RequestVerificationToken': OPTS.token }, credentials: 'include' })
      .then(function (r) { return r.ok ? r.json() : []; })
      .catch(function () { return []; });
  }

  function asArray(v) {
    if (Array.isArray(v)) return v.slice();
    if (v === undefined || v === null || v === '') return [];
    return [v];
  }

  // ── Hàng nút cuối panel: làm mới / nhân bản / lưu thành preset của site (Phase 6.4) ──

  /**
   * Ba hành động trên cấu hình HIỆN TẠI của khối:
   *  • Làm mới preview — render lại từ props hiện có (dữ liệu site có thể vừa đổi).
   *  • Nhân bản khối — chèn bản sao ngay dưới, giữ nguyên props; nhanh hơn kéo mới rồi cấu hình lại.
   *  • Lưu thành preset của site — POST props hiện tại lên /admin/api/builder/block-presets để lần
   *    sau chọn lại một phát. Ghi vào BlockDefinitions với SiteId của site hiện tại (không phải
   *    global) — preset "của site" theo đúng chữ trên nút.
   */
  function makeBlockActionsTrait(editor) {
    editor.TraitManager.addType('nc-block-actions', {
      noLabel: true,
      createInput({ component, trait }) {
        var def = trait.get('blockDef') || {};
        var wrap = document.createElement('div');
        wrap.className = 'nc-blk-actions';

        var msg = document.createElement('div');
        msg.className = 'nc-act-msg';
        msg.style.display = 'none';

        function flash(text, isErr) {
          msg.textContent = text;
          msg.className = 'nc-act-msg' + (isErr ? ' nc-act-err' : '');
          msg.style.display = '';
        }

        wrap.appendChild(btn('↻ Làm mới', function () {
          var hostEl = component.view && component.view.el;
          if (!hostEl) return;
          var props = readProps(component);
          markLoading(editor, hostEl);
          fetchPreview(component.getAttributes()['data-nc-block'], props).then(function (res) {
            setMatchInfo(component, res.matchedCount, props);
            injectPreview(editor, hostEl, res.html);
          });
        }));

        wrap.appendChild(btn('⧉ Nhân bản', function () {
          // Chèn bản sao ngay sau khối này trong cùng cha; GrapesJS tự gán lại id mới.
          var parent = component.parent();
          if (!parent) { flash('Không nhân bản được ở gốc trang.', true); return; }
          var at = parent.components().indexOf(component) + 1;
          var clone = component.clone();
          parent.append(clone, { at: at });
          editor.select(clone);
          notifyChange();
          flash('Đã tạo bản sao ngay dưới.');
        }));

        wrap.appendChild(btn('★ Lưu preset', function () {
          var name = (window.prompt('Tên preset (hiện trong bảng chọn của site):', def.label || '') || '').trim();
          if (!name) return;
          savePreset(def.key, name, readProps(component)).then(function (ok) {
            flash(ok ? 'Đã lưu "' + name + '" vào preset của site.'
                     : 'Lưu preset thất bại — thử lại.', !ok);
          });
        }));

        wrap.appendChild(msg);
        return wrap;
      },
      onEvent() {}
    });

    function btn(text, onClick) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'nc-trait-btn';
      b.textContent = text;
      b.addEventListener('click', onClick);
      return b;
    }
  }

  /** Lưu props hiện tại thành BlockDefinition của site. Trả Promise<bool> thành/bại. */
  function savePreset(key, name, props) {
    return fetch('/admin/api/builder/block-presets', {
      method: 'POST',
      headers: { 'RequestVerificationToken': OPTS.token, 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ key: key, name: name, propsJson: JSON.stringify(props || {}) })
    }).then(function (r) { return r.ok; }).catch(function () { return false; });
  }

  /**
   * Sinh traits cấu hình props cho component block động, xếp theo nhóm PROP_GROUPS.
   *
   * Mỗi field đọc/ghi THẲNG vào data-nc-props qua getValue/setValue riêng. Trait mặc định của
   * GrapesJS ghi ra attribute rời (count="6"): vừa rác HTML, vừa bị ContentSanitizer xoá lúc
   * lưu (không thuộc data-*) nên giá trị người dùng chọn không sống sót qua lần mở sau.
   *
   * Cố tình KHÔNG đặt 'default'/'value' cho trait: Component.initTraits lấy getInitValue()
   * rồi ghi thành attribute. Mặc định của khối nằm ở def.defaults, đã nằm sẵn trong
   * data-nc-props từ lúc kéo thả.
   */
  function buildPropTraits(def) {
    var traits = [];

    // Dòng "Khớp N …, hiển thị M" đứng trên cùng: nó là câu trả lời cho "sao khối trống?",
    // phải thấy được ngay chứ không phải cuộn xuống mới gặp. Khối không đếm được thì trait
    // tự ẩn (matchedCount = null).
    traits.push({ type: 'nc-match-count', name: 'nc_match', label: '', unit: unitOf(def) });

    // Preset đứng đầu: người dùng gần như luôn chọn "trông như thế nào" trước khi tinh chỉnh.
    if (def.presets && def.presets.length) {
      traits.push({ type: 'nc-preset-picker', name: 'nc_preset', label: 'Kiểu hiển thị',
                    presets: def.presets, blockKey: def.key });
    }

    PROP_GROUPS.forEach(function (group) {
      var inGroup = (def.props || []).filter(function (p) { return p.group === group.id; });
      if (!inGroup.length) return;
      // Nhóm "display" đã có tiêu đề riêng từ preset picker ở trên nếu khối có preset.
      if (!(group.id === 'display' && def.presets && def.presets.length)) {
        traits.push({ type: 'nc-group-label', name: 'nc_grp_' + group.id, label: '', text: group.label });
      }
      inGroup.forEach(function (p) { traits.push(propTrait(p, def)); });
    });

    traits.push({ type: 'nc-block-actions', name: 'nc_actions', label: '', blockDef: def });
    return traits;
  }

  function propTrait(f, def) {
    var t = {
      type: traitTypeFor(f.type),
      name: f.name,
      label: f.label,
      getValue: function (o) {
        var v = readProps(o.component)[f.name];
        if (v === undefined || v === null) return f.default !== undefined ? f.default : '';
        return v;
      },
      setValue: function (o) {
        var props = readProps(o.component);
        writeProp(props, f, o.value);
        writeProps(o.component, props);
        if (o.emitUpdate) o.emitUpdate();
      }
    };
    if (f.hint) t.title = f.hint;
    if (f.placeholder) t.placeholder = f.placeholder;

    if (f.type === 'select') {
      // Prop select KHÔNG bắt buộc luôn có giá trị: chuyên mục để trống = tất cả. Thêm lựa chọn
      // rỗng khi server không khai báo mặc định, để người dùng bỏ chọn lại được.
      var opts = (f.options || []).map(function (o) { return { id: o.value, value: o.value, name: o.name }; });
      if (f.default === undefined && !opts.some(function (o) { return o.value === ''; })) {
        opts.unshift({ id: '', value: '', name: '— Tất cả —' });
      }
      t.options = opts;
    }
    if (f.type === 'multi-select' || f.type === 'item-picker') {
      t.field = f;
      t.blockKey = def.key;
    }
    if (f.type === 'number') {
      if (f.min !== undefined) t.min = f.min;
      if (f.max !== undefined) t.max = f.max;
    }
    return t;
  }

  /** Kiểu prop của server → trait type GrapesJS (kiểu lạ rơi về 'text' thay vì mất ô nhập). */
  function traitTypeFor(type) {
    if (type === 'multi-select') return 'nc-multi-select';
    if (type === 'item-picker') return 'nc-item-picker';
    if (['text', 'number', 'checkbox', 'select'].indexOf(type) >= 0) return type;
    return 'text';
  }

  /** Ghi một prop, lược khỏi data-nc-props khi trùng mặc định của server để HTML không phình. */
  function writeProp(props, f, v) {
    if (f.type === 'checkbox') {
      var b = (v === true || v === 'true' || v === 1 || v === '1');
      if (b === (f.default === true)) delete props[f.name]; else props[f.name] = b;
      return;
    }
    if (f.type === 'multi-select' || f.type === 'item-picker') {
      var arr = Array.isArray(v) ? v : (v ? [v] : []);
      if (!arr.length) delete props[f.name]; else props[f.name] = arr;
      return;
    }
    if (v === '' || v === undefined || v === null) { delete props[f.name]; return; }
    if (f.type === 'number') {
      var n = Number(v);
      if (isNaN(n) || n === f.default) delete props[f.name]; else props[f.name] = n;
      return;
    }
    if (v === f.default) delete props[f.name]; else props[f.name] = v;
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 2) KIỂU HIỂN THỊ (STYLE VARIANTS)
  // ═════════════════════════════════════════════════════════════════════════════

  /**
   * Variant là các class CSS đặt lên SECTION được chọn. CSS định nghĩa nằm trong
   * nc-builder.css nhúng vào canvas + được nối vào compiledCss lúc save (xem Edit.cshtml),
   * nên trang public hiển thị giống hệt canvas mà không phụ thuộc file nào khác.
   */
  var STYLE_VARIANTS = [
    { id: '',                label: 'Mặc định' },
    { id: 'ncv-center',      label: 'Nội dung căn giữa' },
    { id: 'ncv-dark',        label: 'Thẻ nền tối' },
    { id: 'ncv-brand',       label: 'Dải màu thương hiệu' },
    { id: 'ncv-card',        label: 'Khối thẻ bo góc' },
    { id: 'ncv-tight',       label: 'Giảm khoảng cách' }
  ];

  function registerStyleVariantTrait(editor) {
    editor.TraitManager.addType('nc-style-variant', {
      createInput({ component }) {
        var wrap = document.createElement('div');
        wrap.className = 'nc-variant-wrap';
        var current = currentVariantClass(component);

        STYLE_VARIANTS.forEach(function (v) {
          var opt = document.createElement('button');
          opt.type = 'button';
          opt.className = 'nc-variant-opt' + (v.id === current ? ' active' : '');
          opt.textContent = v.label;
          if (v.id === 'ncv-brand') opt.classList.add('demo-brand');
          if (v.id === 'ncv-dark') opt.classList.add('demo-dark');
          opt.addEventListener('click', function () {
            applyVariant(component, v.id);
            wrap.querySelectorAll('.nc-variant-opt').forEach(function (b) { b.classList.remove('active'); });
            opt.classList.add('active');
          });
          wrap.appendChild(opt);
        });
        return wrap;
      },
      onEvent() {}
    });
  }

  function currentVariantClass(comp) {
    if (!comp) return '';
    var classes = comp.getClasses() || [];
    for (var i = 0; i < classes.length; i++) {
      for (var j = 0; j < STYLE_VARIANTS.length; j++) {
        if (classes[i] === STYLE_VARIANTS[j].id) return classes[i];
      }
    }
    return '';
  }

  function applyVariant(comp, variantId) {
    if (!comp) return;
    var classes = (comp.getClasses() || []).filter(function (c) {
      return !STYLE_VARIANTS.some(function (v) { return v.id === c && v.id !== ''; });
    });
    if (variantId) classes.push(variantId);
    comp.setClasses(classes);
    notifyChange();
  }

  /**
   * Gắn trait dùng chung mỗi khi chọn một khối:
   *   - "Kiểu hiển thị" (variant) cho các khối bao ngoài (section/div/header/footer);
   *   - "CSS / JS của khối" cho mọi khối, kể cả heading/ảnh/nút.
   * Chỉ thêm một lần cho mỗi component — traits là collection sống theo component.
   */
  function wireSelectionTraits(editor) {
    editor.on('component:selected', function (comp) {
      if (!comp || comp.is('wrapper')) return;
      var coll = comp.get('traits');
      var has = function (name) {
        return coll.some(function (t) { return t.get('name') === name; });
      };

      var tag = (comp.get('tagName') || '').toLowerCase();
      if (['section', 'div', 'footer', 'header', 'main', 'article', 'aside'].indexOf(tag) >= 0
          && !has('nc_variant')) {
        // Thêm vào đầu danh sách trait để dễ thấy.
        var variant = { type: 'nc-style-variant', name: 'nc_variant', label: 'Kiểu hiển thị' };
        try { coll.unshift(variant); } catch (e) { coll.add(variant); }
      }

      if (!has('nc_code')) coll.add({ type: 'nc-block-code', name: 'nc_code', label: '' });

      // Truy vết CSS: thêm cho MỌI component, kể cả thẻ text — chỗ hay phải dò nhất lại chính là
      // một cái <h2> bị CSS viết tay ở đâu đó đổi font.
      if (!has('nc_css_trace')) coll.add({ type: 'nc-css-trace', name: 'nc_css_trace', label: '' });
    });
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 2b) CSS / JS RIÊNG TỪNG KHỐI
  // ═════════════════════════════════════════════════════════════════════════════

  /*
   * Quy ước: code lẻ nằm ngay trên khối dưới dạng attribute, KHÔNG dồn vào
   * Page.CustomCss/CustomJs (dồn vào đó thì mỗi lần lưu lại nối thêm một bản nữa,
   * đúng lỗi phình vô hạn đã sửa). BlockCodeExtractor phía server bóc các attribute
   * này lúc render, tự thêm scope [data-nc-sid="…"] cho CSS và bọc JS trong IIFE
   * có sẵn biến root. Ở đây chỉ dựng UI soạn thảo + xem trước trong canvas.
   */
  var SID_ATTR = 'data-nc-sid';
  var BCSS_ATTR = 'data-nc-css';
  var BJS_ATTR = 'data-nc-js';
  var BLOCK_CSS_STYLE_ID = 'nc-block-css-preview';

  var blockCode = { comp: null, btn: null, editors: {} };

  function newSid() {
    return 'b' + Date.now().toString(36).slice(-5) + Math.random().toString(36).slice(2, 6);
  }

  /**
   * Bản sao phía client của BlockCodeExtractor.ScopeCss — chỉ để xem trước.
   * Phải khớp 3 quy tắc của server, lệch là canvas nói dối:
   *   '&sel' → gắn thẳng vào khối;  '^sel' → giữ nguyên, không scope (nhắm phần tử cha);
   *   selector đơn → thêm cả dạng '[sid]:is(sel)' vì id GrapesJS nằm trên CHÍNH khối.
   */
  function blockScopeCss(css, sid) {
    if (!css) return '';
    var scope = '[' + SID_ATTR + '="' + sid + '"]';
    if (css.indexOf('{') < 0) return scope + '{' + css + '}';
    return css.replace(/(^|[}{])([^{}@]+)\{/g, function (_m, open, sel) {
      var scoped = scopeSelectorList(sel, sid);
      return scoped.length ? open + scoped.join(',') + '{' : _m;
    });
  }

  /**
   * Scope một danh sách selector (chuỗi ngăn bởi dấu phẩy) về đúng dạng chạy trong canvas.
   * Tách riêng khỏi blockScopeCss vì css-inspector cần đúng phép biến đổi này để trả lời "rule
   * `.title` trong CSS khối có nhắm vào phần tử đang chọn không" — mà nó chỉ có selector, không
   * có cả file CSS. Một nguồn sự thật, không có bản chép tay lệch pha.
   */
  function scopeSelectorList(selText, sid) {
    var scope = '[' + SID_ATTR + '="' + sid + '"]';
    var scoped = [];
    String(selText || '').split(',').forEach(function (s) {
      s = s.trim();
      if (!s) return;
      if (s.charAt(0) === '^') { if (s.slice(1).trim()) scoped.push(s.slice(1).trim()); return; }
      if (s.charAt(0) === '&') { scoped.push(scope + s.slice(1)); return; }
      scoped.push(scope + ' ' + s);
      if (canTargetSelf(s)) scoped.push(scope + ':is(' + s + ')');
    });
    return scoped;
  }

  /** :is() không nhận pseudo-element, và có dấu tổ hợp thì ý người dùng đã rõ là con cháu. */
  function canTargetSelf(sel) {
    return sel.indexOf('::') < 0
      && !/:(before|after|first-line|first-letter)\b/i.test(sel)
      && !/[\s>+~]/.test(sel);
  }

  /** Gom CSS lẻ của mọi khối đang có trên trang rồi bơm vào canvas. */
  function refreshBlockCssPreview(editor) {
    var wrapper = editor.getWrapper();
    if (!wrapper) return;
    var parts = [];
    wrapper.find('[' + BCSS_ATTR + ']').forEach(function (c) {
      var attrs = c.getAttributes() || {};
      var sid = attrs[SID_ATTR];
      if (sid && attrs[BCSS_ATTR]) parts.push(blockScopeCss(attrs[BCSS_ATTR], sid));
    });
    onCanvasReady(editor, function (doc) {
      var host = doc.head || doc.body;
      if (!host) return;
      var style = doc.getElementById(BLOCK_CSS_STYLE_ID);
      if (!style) {
        style = doc.createElement('style');
        style.id = BLOCK_CSS_STYLE_ID;
        host.appendChild(style);
      }
      style.textContent = parts.join('\n\n');
    });
  }

  function blockCodeLabel(comp) {
    var attrs = (comp && comp.getAttributes()) || {};
    var marks = [];
    if (attrs[BCSS_ATTR]) marks.push('CSS');
    if (attrs[BJS_ATTR]) marks.push('JS');
    return marks.length ? '✎ Code khối (' + marks.join(' + ') + ')' : '✎ CSS / JS của khối';
  }

  function registerBlockCodeTrait(editor) {
    editor.TraitManager.addType('nc-block-code', {
      noLabel: true,
      createInput({ component }) {
        var wrap = document.createElement('div');
        wrap.className = 'nc-blockcode-wrap';
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'nc-trait-btn nc-blockcode-btn';
        btn.textContent = blockCodeLabel(component);
        btn.addEventListener('click', function () {
          openBlockCodeModal(editor, component, btn);
        });
        wrap.appendChild(btn);
        return wrap;
      },
      onEvent() { /* nút bấm, không ghi attribute */ }
    });
  }

  function ensureBlockCodeModal(editor) {
    if (document.getElementById('nc-blockcode-modal')) return;

    var canJs = !!OPTS.hasCodeScope;
    var modal = document.createElement('div');
    modal.id = 'nc-blockcode-modal';
    modal.className = 'nc-modal-root';
    modal.innerHTML =
      '<div class="nc-modal-backdrop"></div>' +
      '<div class="nc-modal">' +
      '  <div class="nc-modal-head"><span>Code riêng của khối <span id="nc-bc-name"></span></span>' +
      '    <button type="button" class="nc-modal-close" title="Đóng">×</button></div>' +
      '  <div class="nc-tabs">' +
      '    <button type="button" class="nc-tab active" data-tab="bcss">CSS khối</button>' +
      '    <button type="button" class="nc-tab" data-tab="bjs">JavaScript khối</button>' +
      '  </div>' +
      '  <div class="nc-tab-pane active" data-pane="bcss"><textarea id="nc-bc-css"></textarea>' +
      '    <div class="nc-hint">Selector tự động bị giới hạn trong khối này. Viết <code>.title{…}</code> ' +
      'cho phần tử con, <code>&amp;:hover{…}</code> hoặc <code>&amp;.mo-rong{…}</code> cho chính khối, ' +
      'hoặc chỉ viết khai báo trần <code>padding:32px</code>. ' +
      'Cần sửa phần tử NGOÀI khối (vd section bọc ngoài)? Thêm <code>^</code> đằng trước: ' +
      '<code>^#iqlk{height:auto}</code>. Xoá trắng để gỡ CSS của khối.</div></div>' +
      (canJs ?
      '  <div class="nc-tab-pane hidden" data-pane="bjs"><textarea id="nc-bc-js"></textarea>' +
      '    <div class="nc-hint">Đoạn mã được bọc sẵn trong IIFE với biến <code>root</code> trỏ tới phần tử khối. ' +
      'JS chỉ chạy trên trang public, không chạy trong canvas. Lưu trang có JS lẻ cần quyền Builder.Code.Manage.</div></div>' :
      '  <div class="nc-tab-pane hidden" data-pane="bjs"><div class="nc-hint" style="padding:16px">' +
      'Tài khoản của bạn không có quyền sửa JavaScript (Builder.Code.Manage).</div></div>') +
      '  <div class="nc-modal-foot">' +
      '    <span class="nc-lib-check" id="nc-bc-sid"></span>' +
      '    <div class="nc-spacer"></div>' +
      '    <button type="button" class="nc-btn" id="nc-bc-cancel">Huỷ</button>' +
      '    <button type="button" class="nc-btn-primary" id="nc-bc-save">Áp dụng</button>' +
      '  </div>' +
      '</div>';
    document.body.appendChild(modal);

    modal.querySelector('.nc-modal-close').addEventListener('click', closeBlockCodeModal);
    modal.querySelector('.nc-modal-backdrop').addEventListener('click', closeBlockCodeModal);
    modal.querySelector('#nc-bc-cancel').addEventListener('click', closeBlockCodeModal);
    modal.querySelector('#nc-bc-save').addEventListener('click', function () { saveBlockCode(editor); });
    wireTabs(modal, function (name) {
      var ed = blockCode.editors[name];
      if (ed && ed.refresh) setTimeout(function () { ed.refresh(); }, 30);
    });
  }

  function openBlockCodeModal(editor, comp, btn) {
    ensureBlockCodeModal(editor);
    blockCode.comp = comp;
    blockCode.btn = btn || null;

    var modal = document.getElementById('nc-blockcode-modal');
    modal.classList.add('open');

    if (!blockCode.editors.bcss) {
      blockCode.editors.bcss = global.CodeMirror.fromTextArea(document.getElementById('nc-bc-css'), {
        mode: 'css', theme: 'material-darker', lineNumbers: true, autoRefresh: true,
        matchBrackets: true, autoCloseBrackets: true, lineWrapping: true
      });
    }
    if (OPTS.hasCodeScope && !blockCode.editors.bjs) {
      blockCode.editors.bjs = global.CodeMirror.fromTextArea(document.getElementById('nc-bc-js'), {
        mode: 'javascript', theme: 'material-darker', lineNumbers: true, autoRefresh: true,
        matchBrackets: true, autoCloseBrackets: true, lineWrapping: true
      });
    }

    var attrs = comp.getAttributes() || {};
    // Beautify khi mở. Bản đồng bộ trả nguyên văn nếu lib chưa nạp xong — inspector
    // (collectCssSources) dùng đúng hàm đó nên số dòng không lệch giữa hai bên.
    var rawCss = attrs[BCSS_ATTR] || '';
    var rawJs = attrs[BJS_ATTR] || '';
    var shownCss = fmtBeautify(rawCss, 'blockcss');
    var shownJs = fmtBeautify(rawJs, 'js');
    blockCode.editors.bcss.setValue(shownCss);
    if (blockCode.editors.bjs) blockCode.editors.bjs.setValue(shownJs);
    // Lib chưa nạp kịp thì format lại khi xong — chỉ khi người dùng chưa gõ đè lên.
    if (global.ncCodeFormat) {
      global.ncCodeFormat.ensure(['blockcss', 'js']).then(function () {
        var f = global.ncCodeFormat;
        var bCss = f.beautify(rawCss, 'blockcss');
        var bJs = f.beautify(rawJs, 'js');
        if (blockCode.editors.bcss && blockCode.editors.bcss.getValue() === shownCss) {
          blockCode.editors.bcss.setValue(bCss);
        }
        if (blockCode.editors.bjs && blockCode.editors.bjs.getValue() === shownJs) {
          blockCode.editors.bjs.setValue(bJs);
        }
      });
    }

    var name = comp.getName ? comp.getName() : (comp.get('name') || comp.get('tagName'));
    modal.querySelector('#nc-bc-name').textContent = '— ' + (name || 'khối');
    modal.querySelector('#nc-bc-sid').textContent = attrs[SID_ATTR]
      ? 'Mã khối: ' + attrs[SID_ATTR]
      : 'Mã khối sẽ được cấp khi bạn áp dụng.';

    setTimeout(function () {
      ['bcss', 'bjs'].forEach(function (k) { blockCode.editors[k] && blockCode.editors[k].refresh(); });
    }, 40);
  }

  function closeBlockCodeModal() {
    var modal = document.getElementById('nc-blockcode-modal');
    if (modal) modal.classList.remove('open');
    blockCode.comp = null;
    blockCode.btn = null;
  }

  function saveBlockCode(editor) {
    var comp = blockCode.comp;
    if (!comp) { closeBlockCodeModal(); return; }

    var attrs = Object.assign({}, comp.getAttributes() || {});
    var css = fmtForSave((blockCode.editors.bcss ? blockCode.editors.bcss.getValue() : '').trim(),
                         'blockcss', 'CSS khối');
    // Không có quyền sửa JS thì giữ nguyên JS cũ, đừng xoá của người khác.
    var js = blockCode.editors.bjs
      ? fmtForSave(blockCode.editors.bjs.getValue().trim(), 'js', 'JS khối')
      : (attrs[BJS_ATTR] || '');

    if (css) attrs[BCSS_ATTR] = css; else delete attrs[BCSS_ATTR];
    if (js) attrs[BJS_ATTR] = js; else delete attrs[BJS_ATTR];
    if (css || js) attrs[SID_ATTR] = attrs[SID_ATTR] || newSid();
    else delete attrs[SID_ATTR];

    comp.setAttributes(attrs);
    if (blockCode.btn) blockCode.btn.textContent = blockCodeLabel(comp);
    refreshBlockCssPreview(editor);
    notifyChange();
    closeBlockCodeModal();
  }

  /** Chuyển tab trong modal: bật .active cho tab + pane tương ứng. */
  function wireTabs(modal, onSwitch) {
    modal.querySelectorAll('.nc-tab').forEach(function (tab) {
      tab.addEventListener('click', function () {
        modal.querySelectorAll('.nc-tab').forEach(function (t) { t.classList.remove('active'); });
        tab.classList.add('active');
        modal.querySelectorAll('.nc-tab-pane').forEach(function (p) {
          p.classList.remove('active');
          p.classList.add('hidden');
        });
        var pane = modal.querySelector('[data-pane="' + tab.dataset.tab + '"]');
        pane.classList.remove('hidden');
        pane.classList.add('active');
        if (onSwitch) onSwitch(tab.dataset.tab, pane);
      });
    });
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 2c) TRUY VẾT CSS ĐANG ÁP LÊN PHẦN TỬ ĐANG CHỌN
  // ═════════════════════════════════════════════════════════════════════════════

  /*
   * CSS của một khối có thể nằm ở 5 chỗ: CSS riêng khối, CSS riêng trang, CSS chung site, Style
   * Manager, file theme đã build. Chọn một DOM trong canvas rồi muốn sửa màu/khoảng cách thì không
   * có cách nào biết rule đang áp nằm đâu ngoài mở từng chỗ ra dò — nhất là với khối dựng sẵn có
   * CSS viết tay để chung ở CSS site.
   *
   * Trait này liệt kê mọi rule đang nhắm vào phần tử, xếp từ ƯU TIÊN CAO xuống, và nút "Sửa" mở
   * thẳng editor đúng chỗ, cuộn tới đúng dòng. Rule của theme không sửa được thì cho nút "Ghi đè"
   * chép khai báo sang CSS riêng của khối.
   *
   * Logic khớp/parse nằm ở ~/js/admin/nc-css-inspector.js (thuần, test được); ở đây chỉ dựng UI.
   */

  /** Thẻ style do builder tự quản — đã có nguồn văn bản riêng, đọc lại qua CSSOM sẽ hiện trùng. */
  var OWN_STYLE_IDS = ['nc-site-custom-css', 'nc-custom-page-css', 'nc-block-css-preview',
                       'nc-sk-style'];

  /** Thứ tự cascade của từng nhóm nguồn, khớp thứ tự chèn thật vào <head> canvas trong Edit.cshtml. */
  var ORDER = { readonly: 0, sm: 100, site: 200, page: 300, block: 400 };

  /**
   * Gom mọi nguồn CSS đang sống trong canvas về dạng css-inspector hiểu được.
   * CSS riêng khối lấy từ MỌI khối trên trang, không chỉ khối đang chọn: CSS của một section cha
   * hoàn toàn có thể nhắm vào phần tử con nằm sâu bên trong.
   */
  function collectCssSources(editor) {
    var doc = editor.Canvas.getDocument();
    var sources = ncCssInspector.readOnlySheets(doc, OWN_STYLE_IDS);

    // Style Manager: đọc từ CssComposer, không đọc CSSOM — cần giữ tham chiếu tới rule model để
    // nút "Sửa" chọn đúng selector trong Style Manager.
    var smRules = [];
    try {
      editor.Css.getRules().forEach(function (rule) {
        var sel = rule.selectorsToString ? rule.selectorsToString() : '';
        var style = rule.getStyle ? rule.getStyle() : null;
        if (!sel || !style) return;
        var body = Object.keys(style)
          .filter(function (k) { return style[k] !== '' && k.charAt(0) !== '_'; })
          .map(function (k) { return k + ': ' + style[k]; })
          .join('; ');
        if (!body) return;
        var at = rule.get && rule.get('mediaText') ? '@media ' + rule.get('mediaText') : '';
        smRules.push({ selector: sel, body: body, line: 0, at: at });
      });
    } catch (e) { /* CssComposer đổi API — mất một nguồn, không sập panel */ }

    if (smRules.length) {
      sources.push({ id: 'sm', kind: 'sm', label: 'Style Manager', editable: true,
                     order: ORDER.sm, rules: smRules });
    }

    if (OPTS.siteCss) {
      sources.push({ id: 'site', kind: 'site', label: 'CSS chung của site', editable: !!OPTS.hasCodeScope,
                     order: ORDER.site, text: OPTS.siteCss });
    }

    if (codeState.css) {
      sources.push({ id: 'page', kind: 'page', label: 'CSS riêng của trang', editable: true,
                     order: ORDER.page, text: codeState.css });
    }

    var wrapper = editor.getWrapper();
    if (wrapper) {
      wrapper.find('[' + BCSS_ATTR + ']').forEach(function (c, i) {
        var attrs = c.getAttributes() || {};
        var sid = attrs[SID_ATTR];
        var css = attrs[BCSS_ATTR];
        if (!sid || !css) return;
        sources.push({
          id: 'block:' + sid,
          kind: 'block',
          label: 'CSS của khối ' + blockShortName(c),
          editable: true,
          order: ORDER.block + i,
          // Bản beautify giống hệt editor khối đang hiển thị — nếu không, số dòng của
          // css-inspector tính trên bản minify trong DB còn con trỏ nhảy vào bản đã format.
          text: fmtBeautify(css, 'blockcss'),
          meta: { comp: c, sid: sid },
          // Văn bản nguồn viết `.title`, trong canvas là `[data-nc-sid="x"] .title` — phải scope
          // trước khi thử khớp, nhưng vẫn hiện selector gốc cho người dùng.
          scope: function (sel) { return scopeSelectorList(sel, sid); }
        });
      });
    }

    return sources;
  }

  /** Tên ngắn để phân biệt các khối trong danh sách nguồn. */
  function blockShortName(comp) {
    var attrs = comp.getAttributes() || {};
    if (attrs['data-nc-block']) return '"' + attrs['data-nc-block'] + '"';
    if (attrs.id) return '#' + attrs.id;
    var cls = (comp.getClasses() || [])[0];
    if (cls) return '.' + cls;
    return '<' + (comp.get('tagName') || 'div') + '>';
  }

  /** Mô tả phần tử đang chọn đúng như CSS nhìn thấy nó — để biết #ic5kz thật ra là cái gì. */
  function elementSignature(el) {
    if (!el) return '';
    var s = el.tagName ? el.tagName.toLowerCase() : '?';
    if (el.id) s += '#' + el.id;
    var cls = (el.getAttribute && el.getAttribute('class') || '').trim();
    if (cls) s += '.' + cls.split(/\s+/).slice(0, 6).join('.');
    return s;
  }

  function makeCssTraceTrait(editor) {
    editor.TraitManager.addType('nc-css-trace', {
      noLabel: true,
      createInput({ component }) {
        var wrap = document.createElement('div');
        wrap.className = 'nc-csstrace';

        var head = document.createElement('div');
        head.className = 'nc-csstrace-head';
        var title = document.createElement('span');
        title.textContent = 'CSS đang áp dụng';
        var refresh = document.createElement('button');
        refresh.type = 'button';
        refresh.className = 'nc-csstrace-refresh';
        refresh.textContent = '↻';
        refresh.title = 'Quét lại';
        head.appendChild(title);
        head.appendChild(refresh);
        wrap.appendChild(head);

        var sig = document.createElement('div');
        sig.className = 'nc-csstrace-sig';
        wrap.appendChild(sig);

        var list = document.createElement('div');
        list.className = 'nc-csstrace-list';
        wrap.appendChild(list);

        refresh.addEventListener('click', render);
        render();
        return wrap;

        function render() {
          var el = component.view && component.view.el;
          list.innerHTML = '';
          if (!el) {
            sig.textContent = '';
            list.appendChild(hint('Khối chưa render xong — bấm ↻ để quét lại.'));
            return;
          }
          sig.textContent = elementSignature(el);

          var matches;
          try {
            matches = ncCssInspector.collect(el, collectCssSources(editor));
          } catch (e) {
            list.appendChild(hint('Không quét được CSS: ' + (e && e.message ? e.message : e)));
            return;
          }

          if (!matches.length) {
            list.appendChild(hint('Không có rule CSS nào nhắm riêng vào phần tử này. Style có thể đến từ class utility của Tailwind, thuộc tính style="" trên chính thẻ, hoặc kế thừa từ thẻ cha.'));
            return;
          }

          // Ưu tiên CAO lên trước: thứ người dùng đang thấy trên màn hình nằm ở đầu danh sách.
          matches.slice().reverse().forEach(function (rule) {
            list.appendChild(ruleRow(editor, component, rule));
          });
        }

        function hint(text) {
          var d = document.createElement('div');
          d.className = 'nc-hint';
          d.textContent = text;
          return d;
        }
      }
    });
  }

  /** Một dòng rule: nguồn + selector + các khai báo + nút hành động. */
  function ruleRow(editor, component, rule) {
    var row = document.createElement('div');
    row.className = 'nc-csstrace-rule' + (rule.editable ? '' : ' nc-csstrace-ro');

    var top = document.createElement('div');
    top.className = 'nc-csstrace-top';

    var badge = document.createElement('span');
    badge.className = 'nc-csstrace-badge nc-csstrace-' + rule.kind;
    badge.textContent = rule.sourceLabel;
    top.appendChild(badge);

    if (rule.line) {
      var ln = document.createElement('span');
      ln.className = 'nc-csstrace-line';
      ln.textContent = 'dòng ' + rule.line;
      top.appendChild(ln);
    }
    rule.states.forEach(function (st) {
      var chip = document.createElement('span');
      chip.className = 'nc-csstrace-chip';
      chip.textContent = ':' + st;
      top.appendChild(chip);
    });
    if (rule.at) {
      var media = document.createElement('span');
      media.className = 'nc-csstrace-chip';
      media.textContent = rule.at;
      top.appendChild(media);
    }
    row.appendChild(top);

    var sel = document.createElement('code');
    sel.className = 'nc-csstrace-sel';
    sel.textContent = rule.selector;
    row.appendChild(sel);

    var decls = document.createElement('div');
    decls.className = 'nc-csstrace-decls';
    rule.decls.slice(0, 12).forEach(function (d) {
      var line = document.createElement('div');
      // winning === null: rule chỉ chạy trong @media/trạng thái riêng, không kết luận thắng-thua.
      line.className = 'nc-csstrace-decl' +
        (d.winning === false ? ' nc-csstrace-dead' : d.winning === true ? ' nc-csstrace-win' : '');
      line.textContent = d.prop + ': ' + d.value;
      if (d.winning === false) line.title = 'Bị rule ưu tiên cao hơn ghi đè';
      decls.appendChild(line);
    });
    if (rule.decls.length > 12) {
      var more = document.createElement('div');
      more.className = 'nc-csstrace-decl';
      more.textContent = '… còn ' + (rule.decls.length - 12) + ' khai báo';
      decls.appendChild(more);
    }
    row.appendChild(decls);

    var act = document.createElement('div');
    act.className = 'nc-csstrace-act';
    if (rule.editable) {
      act.appendChild(actBtn('Sửa', function () { openRuleSource(editor, component, rule); }));
    } else {
      act.appendChild(actBtn('Ghi đè trong khối', function () { overrideInBlock(editor, component, rule); }));
    }
    row.appendChild(act);

    return row;

    function actBtn(text, fn) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'nc-trait-btn';
      b.textContent = text;
      b.addEventListener('click', fn);
      return b;
    }
  }

  /** Mở đúng editor chứa rule và cuộn tới đúng dòng. */
  function openRuleSource(editor, component, rule) {
    if (rule.kind === 'block' && rule.meta && rule.meta.comp) {
      openBlockCodeModal(editor, rule.meta.comp, null);
      jumpToLine(blockCode.editors.bcss, rule.line);
      return;
    }
    if (rule.kind === 'page') {
      openCodeModalAt(editor, 'css');
      jumpToLine(codeState.editors.css, rule.line);
      return;
    }
    if (rule.kind === 'site') {
      openCodeModalAt(editor, 'sitecss');
      // Lần đầu phải đợi API trả về rồi mới có nội dung để cuộn tới.
      loadSiteCss().then(function () { jumpToLine(codeState.editors.sitecss, rule.line); });
      return;
    }
    if (rule.kind === 'sm') {
      // Style Manager sửa được ngay trên panel — chỉ cần đưa người dùng tới đó.
      editor.select(component);
      try { editor.Panels.getButton('views', 'open-sm').set('active', true); } catch (e) { /* layout panel khác */ }
    }
  }

  /**
   * Đặt con trỏ vào dòng chứa rule và nháy sáng một nhịp. CodeMirror đánh số dòng từ 0, parser trả
   * số dòng 1-based như người đọc file vẫn đếm.
   */
  function jumpToLine(cm, line) {
    if (!cm || !line) return;
    var idx = Math.max(0, line - 1);
    setTimeout(function () {
      cm.refresh();
      cm.setCursor({ line: idx, ch: 0 });
      cm.scrollIntoView({ line: idx, ch: 0 }, 120);
      cm.focus();
      cm.addLineClass(idx, 'background', 'nc-cm-hit');
      setTimeout(function () { cm.removeLineClass(idx, 'background', 'nc-cm-hit'); }, 2200);
    }, 80);
  }

  /**
   * Chép khai báo của một rule chỉ-đọc (theme/Tailwind) vào CSS riêng của khối, dưới dạng `&` để
   * nó bám đúng khối này. Đây là lối thoát khi rule nằm trong file build không sửa được từ admin.
   */
  function overrideInBlock(editor, component, rule) {
    var target = nearestBlockHost(component);
    if (!target) return;

    var decls = rule.decls.map(function (d) { return '  ' + d.prop + ': ' + d.value + ';'; }).join('\n');
    var snippet = '\n/* ghi đè ' + rule.selector + ' (' + rule.sourceLabel + ') */\n' +
                  '&' + (rule.states.length ? ':' + rule.states[0] : '') + ' {\n' + decls + '\n}\n';

    openBlockCodeModal(editor, target, null);
    var cm = blockCode.editors.bcss;
    if (!cm) return;
    setTimeout(function () {
      cm.refresh();
      var cur = cm.getValue();
      cm.setValue((cur ? cur.replace(/\s+$/, '') + '\n' : '') + snippet);
      var last = cm.lineCount() - 1;
      cm.setCursor({ line: last, ch: 0 });
      cm.scrollIntoView({ line: last, ch: 0 }, 120);
      cm.focus();
    }, 80);
  }

  /**
   * Component sẽ giữ CSS ghi đè. Ưu tiên CHÍNH phần tử đang chọn để selector `&` nhắm đúng nó;
   * chỉ khi nó là text node/không có tagName mới leo lên cha.
   */
  function nearestBlockHost(component) {
    var c = component;
    while (c && !c.get('tagName')) c = c.parent();
    return c && !c.is('wrapper') ? c : component;
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // 3) CUSTOM CODE PER PAGE (modal CodeMirror)
  // ═════════════════════════════════════════════════════════════════════════════

  var codeState = { css: '', js: '', editors: {} };

  function ensureCodeModal(editor, opts) {
    if (document.getElementById('nc-code-modal')) return;

    var canJs = !!opts.hasCodeScope;
    var modal = document.createElement('div');
    modal.id = 'nc-code-modal';
    modal.innerHTML =
      '<div class="nc-modal-backdrop"></div>' +
      '<div class="nc-modal">' +
      '  <div class="nc-modal-head"><span>Code riêng của trang</span><button type="button" class="nc-modal-close" title="Đóng">×</button></div>' +
      '  <div class="nc-tabs">' +
      '    <button type="button" class="nc-tab active" data-tab="css">CSS</button>' +
      (canJs ? '<button type="button" class="nc-tab" data-tab="js">JavaScript</button>' : '') +
      (canJs ? '<button type="button" class="nc-tab" data-tab="sitecss">CSS site</button>' : '') +
      '    <button type="button" class="nc-tab" data-tab="lib">Library</button>' +
      '  </div>' +
      '  <div class="nc-tab-pane active" data-pane="css"><textarea id="nc-code-css"></textarea>' +
      '    <div class="nc-hint">CSS này nối SAU CSS do builder biên dịch — selector trùng sẽ thắng. Dùng var(--color-*), var(--radius-card)… để bám design token của site.</div></div>' +
      (canJs ?
      '  <div class="nc-tab-pane hidden" data-pane="js"><textarea id="nc-code-js"></textarea>' +
      '    <div class="nc-hint">JS chạy cuối &lt;/body&gt; trên trang public. Mã có thể thực thi trong trình duyệt khách — hãy chắc chắn bạn hiểu nó làm gì.</div></div>' :
      '  <div class="nc-tab-pane hidden" data-pane="js"><div class="nc-hint" style="padding:16px">Tài khoản của bạn không có quyền sửa JavaScript (Builder.Code.Manage).</div></div>') +
      (canJs ?
      '  <div class="nc-tab-pane hidden" data-pane="sitecss"><textarea id="nc-code-sitecss"></textarea>' +
      '    <div class="nc-hint nc-sitecss-hint">CSS dùng chung MỌI trang của site. Sửa ở đây ảnh hưởng cả site, không riêng trang đang mở — lưu ngay khi bấm Áp dụng.</div></div>' : '') +
      '  <div class="nc-tab-pane hidden" data-pane="lib"><div id="nc-lib-list"></div></div>' +
      '  <div class="nc-modal-foot">' +
      '    <label class="nc-lib-check"><input type="checkbox" id="nc-live-check" checked /> Xem trước trực tiếp trong canvas</label>' +
      '    <div class="nc-spacer"></div>' +
      '    <button type="button" class="nc-btn" id="nc-code-cancel">Huỷ</button>' +
      '    <button type="button" class="nc-btn-primary" id="nc-code-save">Áp dụng</button>' +
      '  </div>' +
      '</div>';
    document.body.appendChild(modal);

    modal.querySelector('.nc-modal-close').addEventListener('click', closeCodeModal);
    modal.querySelector('.nc-modal-backdrop').addEventListener('click', closeCodeModal);
    modal.querySelector('#nc-code-cancel').addEventListener('click', closeCodeModal);
    modal.querySelector('#nc-code-save').addEventListener('click', function () {
      codeState.css = cmGet('css').trim();
      codeState.js = canJs ? cmGet('js').trim() : codeState.js;
      applyLivePreview(editor, opts);
      // CSS site lưu qua API riêng (không nằm trong bản ghi trang) và chỉ khi thật sự đổi — tránh
      // ghi đè trắng lúc người dùng chỉ mở tab ra xem.
      saveSiteCssIfDirty(editor);
      closeCodeModal();
      notifyChange();
    });

    // Dùng chung wireTabs với modal code khối. Bản chép tay trước đây ở đây chỉ gỡ/thêm class
    // `hidden` mà quên `.active` — trong khi CSS hiện pane BẰNG `.active` (.nc-tab-pane{display:none}),
    // nên bấm sang tab khác là pane đích không bao giờ hiện ra.
    wireTabs(modal, function (name, pane) {
      if (name === 'lib') renderLibList(pane, editor);
      if (name === 'sitecss') loadSiteCss();
      var ed = codeState.editors[name === 'js' ? 'js' : name === 'sitecss' ? 'sitecss' : 'css'];
      if (ed && ed.refresh) setTimeout(function () { ed.refresh(); }, 30);
    });
  }

  // ── CSS chung của site (sửa tại chỗ trong builder) ──────────────────────────
  // Trước đây muốn sửa CSS site phải rời builder sang trang Layouts → tab Custom Code, tự dò
  // selector, rồi quay lại xem kết quả. Có css-inspector chỉ đúng dòng rồi thì nút "Sửa" phải mở
  // được editor ngay tại chỗ, nếu không thì vẫn là đi mò.
  var siteCssState = { loaded: false, loading: null, saved: '', shown: '', dto: null };

  /** Nạp CSS site một lần cho mỗi phiên builder. Trả promise để nút "Sửa" đợi trước khi nhảy dòng. */
  function loadSiteCss() {
    if (siteCssState.loaded) return Promise.resolve(siteCssState.saved);
    if (siteCssState.loading) return siteCssState.loading;

    siteCssState.loading = fetch('/admin/api/builder/site-code', {
      headers: { 'RequestVerificationToken': OPTS.token },
      credentials: 'include'
    }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (dto) {
      siteCssState.dto = dto || {};
      siteCssState.saved = siteCssState.dto.customCss || '';
      siteCssState.loaded = true;
      // Editor + inspector thấy bản beautify; saved giữ bản server để so dirty khi lưu.
      siteCssState.shown = fmtBeautify(siteCssState.saved, 'css');
      cmSet('sitecss', siteCssState.shown);
      OPTS.siteCss = siteCssState.shown;
      if (global.ncCodeFormat) {
        global.ncCodeFormat.ensure(['css']).then(function () {
          var ed = codeState.editors.sitecss;
          if (ed && ed.getValue() === siteCssState.shown) {
            siteCssState.shown = global.ncCodeFormat.beautify(siteCssState.saved, 'css');
            cmSet('sitecss', siteCssState.shown);
            OPTS.siteCss = siteCssState.shown;
          }
        });
      }
      return siteCssState.saved;
    }).catch(function () {
      // Không nạp được thì để editor trống nhưng KHÔNG đánh dấu loaded — nếu người dùng bấm Áp
      // dụng, saveSiteCssIfDirty sẽ bỏ qua thay vì PUT chuỗi rỗng đè lên CSS site đang chạy.
      var hint = document.querySelector('#nc-code-modal .nc-sitecss-hint');
      if (hint) hint.textContent = 'Không tải được CSS site — hãy thử lại hoặc sửa ở trang Layouts → Custom Code.';
      siteCssState.loading = null;
      return '';
    });

    return siteCssState.loading;
  }

  /** PUT lại đủ 4 trường: API nhận cả cụm, gửi thiếu là xoá trắng head/body/JS của site. */
  function saveSiteCssIfDirty(editor) {
    if (!siteCssState.loaded || !codeState.editors.sitecss) return;
    var next = codeState.editors.sitecss.getValue();
    if (next === siteCssState.shown) return;

    var dto = siteCssState.dto || {};
    var out = fmtForSave(next, 'css', 'CSS site');
    fetch('/admin/api/builder/site-code', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': OPTS.token },
      credentials: 'include',
      body: JSON.stringify({
        customCss: out,
        customJs: dto.customJs || '',
        headHtml: dto.headHtml || '',
        bodyEndHtml: dto.bodyEndHtml || ''
      })
    }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      siteCssState.saved = out;
      siteCssState.shown = next;
      OPTS.siteCss = next;
      applySiteCssPreview(editor, next);
    }).catch(function () {
      alert('Không lưu được CSS chung của site. Thay đổi CSS của trang vẫn được giữ.');
    });
  }

  /** Cập nhật thẻ style CSS site trong canvas để thấy kết quả ngay, không phải tải lại builder. */
  function applySiteCssPreview(editor, css) {
    var doc = editor.Canvas.getDocument();
    if (!doc) return;
    var el = doc.getElementById('nc-site-custom-css');
    if (!el) {
      el = doc.createElement('style');
      el.id = 'nc-site-custom-css';
      doc.head.appendChild(el);
    }
    el.textContent = css || '';
  }

  function cmGet(which) {
    var ed = codeState.editors[which];
    return ed ? ed.getValue() : codeState[which] || '';
  }
  function cmSet(which, value) {
    codeState[which] = value || '';
    var ed = codeState.editors[which];
    if (ed) ed.setValue(value || '');
  }

  function openCodeModal(editor, opts) {
    ensureCodeModal(editor, opts);
    var modal = document.getElementById('nc-code-modal');
    modal.classList.add('open');

    if (!codeState.editors.css) {
      codeState.editors.css = global.CodeMirror.fromTextArea(document.getElementById('nc-code-css'), {
        mode: 'css', theme: 'material-darker', lineNumbers: true, autoRefresh: true,
        matchBrackets: true, autoCloseBrackets: true, lineWrapping: true
      });
    }
    if (opts.hasCodeScope && !codeState.editors.js) {
      codeState.editors.js = global.CodeMirror.fromTextArea(document.getElementById('nc-code-js'), {
        mode: 'javascript', theme: 'material-darker', lineNumbers: true, autoRefresh: true,
        matchBrackets: true, autoCloseBrackets: true, lineWrapping: true
      });
    }
    if (opts.hasCodeScope && !codeState.editors.sitecss) {
      codeState.editors.sitecss = global.CodeMirror.fromTextArea(document.getElementById('nc-code-sitecss'), {
        mode: 'css', theme: 'material-darker', lineNumbers: true, autoRefresh: true,
        matchBrackets: true, autoCloseBrackets: true, lineWrapping: true
      });
    }
    // Beautify khi mở: DB có thể đang giữ bản minify một dòng. cmSet ghi cùng giá trị vào
    // codeState để số dòng của css-inspector khớp đúng bản editor hiển thị.
    var shownCss = fmtBeautify(codeState.css, 'css');
    var shownJs = fmtBeautify(codeState.js, 'js');
    cmSet('css', shownCss);
    if (opts.hasCodeScope) cmSet('js', shownJs);
    // Beautify đồng bộ trả nguyên văn nếu lib chưa nạp xong — format lại khi xong,
    // chỉ khi người dùng chưa gõ đè lên nội dung vừa hiện.
    if (global.ncCodeFormat) {
      global.ncCodeFormat.ensure(['css', 'js']).then(function () {
        var f = global.ncCodeFormat;
        if (codeState.editors.css && codeState.editors.css.getValue() === shownCss) {
          cmSet('css', f.beautify(shownCss, 'css'));
        }
        if (opts.hasCodeScope && codeState.editors.js && codeState.editors.js.getValue() === shownJs) {
          cmSet('js', f.beautify(shownJs, 'js'));
        }
      });
    }
    setTimeout(function () {
      ['css', 'js', 'sitecss'].forEach(function (k) { codeState.editors[k] && codeState.editors[k].refresh(); });
    }, 40);
  }

  /** Mở modal code ở đúng tab. Dùng khi css-inspector điều hướng tới nơi chứa rule. */
  function openCodeModalAt(editor, tabName) {
    openCodeModal(editor, OPTS);
    var modal = document.getElementById('nc-code-modal');
    var tab = modal && modal.querySelector('.nc-tab[data-tab="' + tabName + '"]');
    if (tab) tab.click();
    return modal;
  }

  function closeCodeModal() {
    var modal = document.getElementById('nc-code-modal');
    if (modal) modal.classList.remove('open');
  }

  // ── Live preview custom CSS/JS trong canvas ─────────────────────────────────
  var LIVE_STYLE_ID = 'nc-custom-page-css';

  function applyLivePreview(editor, opts) {
    onCanvasReady(editor, function (doc) {
      var head = doc.head || doc.getElementsByTagName('head')[0];
      if (!head) return;
      var styleEl = doc.getElementById(LIVE_STYLE_ID);
      if (!styleEl) {
        styleEl = doc.createElement('style');
        styleEl.id = LIVE_STYLE_ID;
        head.appendChild(styleEl);
      }
      styleEl.textContent = codeState.css || '';

      // Custom JS KHÔNG chạy trong canvas builder (tránh hiệu ứng lạ khi chỉnh sửa);
      // chỉ chạy trên trang public qua Page.CustomJs.
    });
  }

  // ── Snippet library ─────────────────────────────────────────────────────────
  var CODE_LIBRARY = [
    {
      group: 'CSS',
      items: [
        {
          name: 'Nút gradient nổi bật',
          desc: '.btn-gradient — nút chuyển màu brand, hover sáng lên.',
          lang: 'css',
          code: '.btn-gradient{display:inline-block;padding:14px 36px;border-radius:999px;\n' +
                '  background:linear-gradient(135deg,var(--color-brand-500),var(--color-brand-700));\n' +
                '  color:#fff;font-weight:700;text-decoration:none;\n' +
                '  box-shadow:0 10px 30px -10px var(--color-brand-500);transition:.25s}\n' +
                '.btn-gradient:hover{transform:translateY(-2px);filter:brightness(1.1)}'
        },
        {
          name: 'Thẻ glass mờ',
          desc: '.glass-card — thẻ nền mờ viền sáng (backdrop-filter).',
          lang: 'css',
          code: '.glass-card{background:rgba(255,255,255,.65);backdrop-filter:blur(12px);\n' +
                '  border:1px solid rgba(255,255,255,.5);border-radius:var(--radius-card,16px);\n' +
                '  box-shadow:0 8px 32px rgba(15,23,42,.08);padding:24px}'
        },
        {
          name: 'Tiêu đề gạch chân nhấn',
          desc: '.heading-accent — chữ display với gạch dưới màu brand.',
          lang: 'css',
          code: '.heading-accent{position:relative;display:inline-block;padding-bottom:8px}\n' +
                '.heading-accent::after{content:"";position:absolute;left:0;bottom:0;width:56px;height:4px;\n' +
                '  border-radius:99px;background:var(--color-brand-500)}'
        },
        {
          name: 'Hover nâng ảnh',
          desc: '.img-lift — ảnh phẳng, hover nhấc nhẹ + bóng.',
          lang: 'css',
          code: '.img-lift{border-radius:var(--radius-card,14px);transition:transform .3s,box-shadow .3s}\n' +
                '.img-lift:hover{transform:translateY(-6px);box-shadow:0 24px 48px -16px rgba(15,23,42,.25)}'
        },
        {
          name: 'Badge trạng thái',
          desc: '.pill-badge — nhãn nhỏ nền brand nhạt.',
          lang: 'css',
          code: '.pill-badge{display:inline-flex;align-items:center;gap:6px;padding:4px 14px;\n' +
                '  border-radius:999px;background:color-mix(in srgb,var(--color-brand-500) 12%,white);\n' +
                '  color:var(--color-brand-600,#0a6352);font-size:.78rem;font-weight:700;\n' +
                '  letter-spacing:.06em;text-transform:uppercase}'
        },
        {
          name: 'Section sọc kẻ zebra',
          desc: '.zebra — nền sọc kẻ mảnh cho section.',
          lang: 'css',
          code: '.zebra{background-image:repeating-linear-gradient(45deg,\n' +
                '  transparent 0 12px,color-mix(in srgb,var(--color-brand-500) 5%,transparent) 12px 24px)}'
        },
        {
          name: 'Scroll reveal mượt',
          desc: '.reveal + .reveal.in — fade-up khi JS thêm class .in.',
          lang: 'css',
          code: '.reveal{opacity:0;transform:translateY(24px);transition:opacity .6s ease,transform .6s ease}\n' +
                '.reveal.in{opacity:1;transform:none}'
        }
      ]
    },
    {
      group: 'JS',
      items: [
        {
          name: 'Scroll reveal tự động',
          desc: 'Thêm .in cho mọi .reveal khi cuộn tới (IntersectionObserver).',
          lang: 'javascript',
          needsCode: true,
          code: '// Reveal on scroll\nvar els=document.querySelectorAll(".reveal");\nif("IntersectionObserver" in window){\n  var io=new IntersectionObserver(function(entries){\n    entries.forEach(function(e){ if(e.isIntersecting){ e.target.classList.add("in"); io.unobserve(e.target);} });\n  },{threshold:.15});\n  els.forEach(function(el){ io.observe(el); });\n}else{ els.forEach(function(el){ el.classList.add("in"); }); }'
        },
        {
          name: 'Smooth scroll cho anchor',
          desc: 'Cuộn mượt tới #anchor thay vì nhảy trang.',
          lang: 'javascript',
          needsCode: true,
          code: 'document.querySelectorAll(\'a[href^="#"]\').forEach(function(a){\n  a.addEventListener("click",function(ev){\n    var id=a.getAttribute("href").slice(1);\n    var el=id&&document.getElementById(id);\n    if(el){ ev.preventDefault(); el.scrollIntoView({behavior:"smooth"}); }\n  });\n});'
        },
        {
          name: 'Đánh dấu menu đang mở',
          desc: 'Thêm class .active cho link menu khớp path hiện tại.',
          lang: 'javascript',
          needsCode: true,
          code: '(function(){\n  var path=location.pathname.replace(/\\/$/,"");\n  document.querySelectorAll(\'header a[href]\').forEach(function(a){\n    var href=a.getAttribute("href").replace(/\\/$/,"");\n    if(href&&href!="/"&&path.indexOf(href)===0)a.classList.add("active");\n  });\n})();'
        },
        {
          name: 'Nút back-to-top',
          desc: 'Tạo nút ↑ cuộn về đầu, hiện sau khi cuộn 400px.',
          lang: 'javascript',
          needsCode: true,
          code: '(function(){\n  var b=document.createElement("button");\n  b.textContent="↑";b.setAttribute("aria-label","Về đầu trang");\n  b.style.cssText="position:fixed;right:20px;bottom:20px;width:44px;height:44px;border-radius:50%;"+\n    "border:none;background:var(--color-brand-500,#0d7c66);color:#fff;font-size:20px;cursor:pointer;"+\n    "opacity:0;pointer-events:none;transition:.3s;z-index:99";\n  document.body.appendChild(b);\n  addEventListener("scroll",function(){\n    var show=scrollY>400;b.style.opacity=show?1:0;b.style.pointerEvents=show?"auto":"none";\n  });\n  b.addEventListener("click",function(){scrollTo({top:0,behavior:"smooth"});});\n})();'
        }
      ]
    }
  ];

  function renderLibList(pane, editor) {
    var list = pane.querySelector('#nc-lib-list');
    if (!list) return;
    list.innerHTML = CODE_LIBRARY.map(function (group) {
      return '<div class="nc-lib-group"><div class="nc-lib-group-title">' + group.group + '</div>' +
        group.items.map(function (item, idx) {
          var disabled = item.needsCode && !OPTS.hasCodeScope;
          return '<div class="nc-lib-item' + (disabled ? ' disabled' : '') + '">' +
            '<div class="nc-lib-info"><div class="nc-lib-name">' + esc(item.name) + '</div>' +
            '<div class="nc-lib-desc">' + esc(item.desc) + '</div></div>' +
            '<button type="button" class="nc-btn" data-lang="' + item.lang + '" ' +
            'data-g="' + CODE_LIBRARY.indexOf(group) + '" data-i="' + idx + '"' +
            (disabled ? ' disabled title="Cần quyền Builder.Code.Manage"' : '') + '>Xem + chèn</button>' +
            '</div>';
        }).join('') + '</div>';
    }).join('');

    list.querySelectorAll('button[data-lang]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var g = CODE_LIBRARY[+btn.dataset.g];
        var item = g.items[+btn.dataset.i];
        insertLibraryItem(editor, item);
      });
    });
  }

  /** Chèn snippet: CSS → nối vào customCss; JS → nối vào customJs (nếu đủ quyền). */
  function insertLibraryItem(editor, item) {
    var which = item.lang === 'css' ? 'css' : 'js';
    if (which === 'js' && !OPTS.hasCodeScope) return;
    openCodeModal(editor, OPTS);
    // Đảm bảo tab đúng loại đang mở
    var modal = document.getElementById('nc-code-modal');
    var tabBtn = modal.querySelector('.nc-tab[data-tab="' + (which === 'css' ? 'css' : 'js') + '"]');
    if (tabBtn) tabBtn.click();
    var cur = cmGet(which);
    cmSet(which, (cur ? cur.trimEnd() + '\n\n' : '') + '/* ' + item.name + ' */\n' + item.code);
    notifyChange();
  }

  // ═════════════════════════════════════════════════════════════════════════════
  // Khớp nối ra ngoài
  // ═════════════════════════════════════════════════════════════════════════════

  function register(editor, opts) {
    OPTS = Object.assign(OPTS, opts || {});
    OPTS.hasCodeScope = !!OPTS.hasCodeScope;
    // Nạp lib beautify/minify sớm để lần mở modal đầu tiên đã beautify đồng bộ được.
    if (global.ncCodeFormat) global.ncCodeFormat.ensure(['css', 'js', 'blockcss']);
    // OPTS.siteCss nuôi số dòng của css-inspector — phải là bản beautify giống editor thấy.
    OPTS.siteCss = fmtBeautify(OPTS.siteCss, 'css');

    // Trait types trước component types: buildPropTraits tham chiếu tên type, TraitManager phải
    // biết chúng trước khi một khối được chọn lần đầu.
    registerStyleVariantTrait(editor);
    registerBlockCodeTrait(editor);
    makeRefreshTrait(editor);
    makeMatchCountTrait(editor);
    makeGroupLabelTrait(editor);
    makePresetPickerTrait(editor);
    makeMultiSelectTrait(editor);
    makeItemPickerTrait(editor);
    makeBlockActionsTrait(editor);
    makeCssTraceTrait(editor);

    registerDynamicComponents(editor);
    registerLayoutBlocks(editor);
    wireSelectionTraits(editor);

    // Nút "Code" trên topbar do Edit.cshtml tạo — bắt sự kiện tại đây.
    var btnCode = document.getElementById('btn-code');
    if (btnCode) {
      btnCode.style.display = ''; // ẩn ban đầu trong cshtml nếu muốn
      btnCode.addEventListener('click', function () { openCodeModal(editor, OPTS); });
    }

    // Áp live-preview cho CSS đã lưu khi mở trang (Edit.cshtml set window.ncPageCustomCss)
    // và cho CSS lẻ của từng khối đã có sẵn trong HTML.
    // nc-builder.css đã nằm trong canvas.styles của Edit.cshtml nên không nhúng lại ở đây.
    editor.on('load', function () {
      onCanvasReady(editor, function () {
        applyLivePreview(editor, OPTS);
        refreshBlockCssPreview(editor);
      });
    });
  }

  function refreshAllDynamicBlocks(editor) {
    if (!editor) return;
    var wrapper = editor.getWrapper();
    if (!wrapper) return;
    wrapper.find('[data-nc-block]').forEach(function (comp) {
      var el = comp.view && comp.view.el;
      var key = comp.getAttributes()['data-nc-block'];
      if (el && key) {
        schedulePreview(editor, comp, el, key, readProps(comp));
      }
    });
  }

  global.ncBuilder = {
    register: register,
    /** Được Edit.cshtml gọi trước khi PUT: trả { customCss, customJs } hiện hành.
        Minify tại đây (= lúc lưu); codeState vẫn giữ bản beautify khớp editor để
        css-inspector báo dòng đúng. */
    getCode: function () {
      return {
        customCss: fmtForSave(codeState.css || '', 'css', 'CSS trang'),
        customJs: fmtForSave(codeState.js || '', 'js', 'JS trang')
      };
    },
    /** Edit.cshtml gọi sau khi load project: khởi tạo trạng thái code từ DB. */
    setCode: function (customCss, customJs) {
      codeState.css = customCss || '';
      codeState.js = customJs || '';
    },
    /** Đặt entity mẫu và làm mới toàn bộ khối động trên canvas. */
    setSampleEntity: function (editor, sampleId, sampleType) {
      OPTS.sampleEntityId = sampleId || null;
      OPTS.sampleType = sampleType || null;
      refreshAllDynamicBlocks(editor);
    },
    refreshAll: refreshAllDynamicBlocks
  };
})(window);
