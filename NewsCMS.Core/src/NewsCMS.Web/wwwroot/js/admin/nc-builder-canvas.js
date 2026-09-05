/*
 * nc-builder-canvas.js — hạ tầng canvas dùng chung cho mọi màn GrapesJS của Site Builder
 * (Edit.cshtml — sửa trang, EditShell.cshtml — sửa shell header/footer).
 *
 * Gồm bốn việc mà canvas nào cũng cần, trước đây chép trong Edit.cshtml:
 *   1. Hydrate canvas — dựng lại đúng pipeline render của trang public trong iframe
 *      (CSS site → HeadHtml + vendor script → script của khối → JS site).
 *   2. Font Google theo nhu cầu — canvas là iframe riêng nên phải tự nạp webfont.
 *   3. Asset Manager nối thẳng vào thư viện Media + upload qua uploadManager.
 *   4. Danh sách "Font family" cho Style Manager (fontFamilyOptions — gọi được trước attach).
 *
 * Dùng:
 *   styleManager: { … options: ncCanvas.fontFamilyOptions(GOOGLE_FONTS) … }   // trong grapesjs.init
 *   ncCanvas.attach(editor, { token, siteCss, siteHeadHtml, siteJs, googleFonts });
 * Sau attach() mới có ncCanvas.assets / ncCanvas.uploadFile — assetManager của GrapesJS chỉ
 * gọi chúng lúc người dùng thực sự upload nên tham chiếu muộn là đủ.
 *
 * Nạp SAU grapes.min.js và nc-realm-bridge.js.
 */
(function (global) {
    'use strict';

    var editor = null;
    var OPTS = { token: '', siteCss: '', siteHeadHtml: '', siteJs: '', googleFonts: [] };

    function apiHeaders(json) {
        var h = { 'RequestVerificationToken': OPTS.token };
        if (json) h['Content-Type'] = 'application/json';
        return h;
    }

    /**
     * Dropdown "Font family": font hệ thống trước (tải sẵn, không tốn request), rồi tới font
     * Google. Giá trị phải TRÙNG chuỗi GoogleFontCatalog.ToCssValue sinh ra, vì PageRenderer
     * dò font đang dùng bằng cách đọc lại chính giá trị font-family này trong CSS.
     *
     * Gọi TRƯỚC attach() (lúc dựng styleManager trong grapesjs.init) nên nhận danh sách
     * font qua tham số thay vì đọc OPTS.
     */
    function fontFamilyOptions(GOOGLE_FONTS) {
        var opts = [
            { id: '', label: '— Kế thừa —' },
            { id: 'Arial, Helvetica, sans-serif', label: 'Arial' },
            { id: 'Georgia, serif', label: 'Georgia' },
            { id: '"Times New Roman", Times, serif', label: 'Times New Roman' },
            { id: '"Courier New", Courier, monospace', label: 'Courier New' },
            { id: 'Tahoma, Geneva, sans-serif', label: 'Tahoma' },
            { id: 'Verdana, Geneva, sans-serif', label: 'Verdana' },
            { id: 'system-ui, sans-serif', label: 'Mặc định hệ thống' }
        ];
        GOOGLE_FONTS.forEach(function (f) {
            opts.push({ id: '"' + f.name + '", ' + f.category, label: f.name + ' (Google)' });
        });
        return opts;
    }

    /**
     * Gắn hạ tầng vào một editor. Gọi MỘT lần, sau grapesjs.init().
     * Thân hàm giữ nguyên văn code cũ của Edit.cshtml — các biến SITE_* / GOOGLE_FONTS
     * cố tình đặt lại ở đây để không phải sửa từng dòng khi tách file.
     */
    function attach(ed, opts) {
        editor = ed;
        OPTS = Object.assign(OPTS, opts || {});

        var SITE_CUSTOM_CSS = OPTS.siteCss || '';
        var SITE_HEAD_HTML = OPTS.siteHeadHtml || '';
        var SITE_CUSTOM_JS = OPTS.siteJs || '';
        var GOOGLE_FONTS = OPTS.googleFonts || [];

        // ══ Preview trung thực: tái dựng pipeline render của trang public trong canvas ═══════════
        // PageRenderer dựng trang public gồm: <head> (CSS component site + vendor <link>/<script src>
        // như swiper/masonry/imagesloaded), nội dung khối (một số khối TỰ nhúng <script src> — vd
        // flipbook nhúng page-flip + chu-flipbook.js), rồi JS site cuối <body> (init swiper coverflow,
        // masonry .chu-pin-grid, nav toggle). Trong canvas GrapesJS chèn mọi thứ bằng innerHTML nên
        // TẤT CẢ <script> bị loại bỏ, không chạy — vì thế khối "ẩn tới khi JS bật" (masonry) sập còn
        // tiêu đề, và flipbook thành hộp rỗng. Hàm dưới chạy lại đúng chuỗi đó trong iframe canvas,
        // TỔNG QUÁT cho mọi theme (không hardcode class): bơm CSS site + HeadHtml, chạy lại <script>
        // khối nhúng vào (theo thứ tự phụ thuộc), rồi chạy JS site sau khi nội dung đã có mặt.

        /** Tạo <script> mới từ node đã bị innerHTML vô hiệu hoá rồi append để nó THỰC SỰ chạy.
         *  Bỏ defer/async để tự xếp thứ tự; src → resolve khi tải xong, inline → chạy ngay. */
        function ncExecScript(doc, node) {
            return new Promise(function (resolve) {
                var s = doc.createElement('script');
                for (var i = 0; i < node.attributes.length; i++) {
                    var a = node.attributes[i];
                    if (a.name === 'defer' || a.name === 'async') continue;
                    s.setAttribute(a.name, a.value);
                }
                var src = node.getAttribute('src');
                if (src) {
                    s.onload = s.onerror = function () { resolve(); };
                    s.src = src;
                    doc.head.appendChild(s);
                } else {
                    s.textContent = node.textContent;
                    doc.head.appendChild(s);
                    resolve();
                }
            });
        }

        /** Chạy tuần tự để giữ phụ thuộc (vd page-flip.browser.js phải xong trước chu-flipbook.js). */
        function ncExecScriptsSeq(doc, nodes) {
            return nodes.reduce(function (p, n) {
                return p.then(function () { return ncExecScript(doc, n); });
            }, Promise.resolve());
        }

        /**
         * Nối realm giữa trang admin và iframe canvas (once-guard ở đây; logic ở
         * ~/js/admin/nc-realm-bridge.js để test hồi quy gọi được trực tiếp trên iframe thật).
         * Xem file đó để biết vì sao cần và vì sao không gán đè global.
         */
        var ncRealmBridged = false;
        function ncBridgeRealms(win) {
            if (ncRealmBridged || !win || win === window) return;
            ncRealmBridged = true;
            if (typeof window.ncBridgeRealms === 'function') window.ncBridgeRealms(win, window);
        }

        var ncHeadInjected = false, ncSiteJsInjected = false;
        function ncHydrateCanvas() {
            var doc = editor.Canvas.getDocument();
            if (!doc || !doc.head || !doc.body) return;

            // 0) Nối realm TRƯỚC khi nạp vendor — Masonry/imagesLoaded kiểm tra instanceof lúc chạy.
            ncBridgeRealms(editor.Canvas.getWindow());

            // 1) CSS component tầng site (1 lần) — icon .chu-cup 17px thay vì SVG phình, swiper/pin CSS.
            if (SITE_CUSTOM_CSS && !doc.getElementById('nc-site-custom-css')) {
                var st = doc.createElement('style');
                st.id = 'nc-site-custom-css';
                st.textContent = SITE_CUSTOM_CSS;
                doc.head.appendChild(st);
            }

            var chain = Promise.resolve();

            // 2) HeadHtml (1 lần): font + <link> CSS vendor chèn thẳng; <script src> vendor chạy tuần tự.
            if (SITE_HEAD_HTML && !ncHeadInjected) {
                ncHeadInjected = true;
                var tpl = doc.createElement('template');
                tpl.innerHTML = SITE_HEAD_HTML;
                var headScripts = [];
                Array.prototype.forEach.call(tpl.content.querySelectorAll('script'), function (n) {
                    headScripts.push(n); n.parentNode.removeChild(n);
                });
                doc.head.appendChild(doc.importNode(tpl.content, true));
                chain = chain.then(function () { return ncExecScriptsSeq(doc, headScripts); });
            }

            // 3) <script> khối tự nhúng vào nội dung (flipbook…) — innerHTML đã vô hiệu hoá; gom lại
            //    chạy tuần tự, đánh dấu data-nc-ran để không chạy trùng khi hydrate lại.
            chain = chain.then(function () {
                var pending = Array.prototype.slice.call(doc.body.querySelectorAll('script:not([data-nc-ran])'));
                pending.forEach(function (n) { n.setAttribute('data-nc-ran', '1'); });
                return ncExecScriptsSeq(doc, pending);
            });

            // 4) JS site (1 lần) — chạy SAU khi nội dung + vendor có mặt để init tìm thấy .chu-pin-grid,
            //    .chu-space-swiper… JS site tự poll chờ lib nên không kẹt thứ tự với bước 2.
            if (SITE_CUSTOM_JS && !ncSiteJsInjected) {
                chain = chain.then(function () {
                    ncSiteJsInjected = true;
                    var s = doc.createElement('script');
                    s.id = 'nc-site-custom-js';
                    s.textContent = SITE_CUSTOM_JS;
                    doc.body.appendChild(s);
                });
            }
        }

        // Debounce: preview khối động nạp bất đồng bộ; đợi lắng rồi mới chạy JS site để nó thấy đủ
        // khối. nc-builder-extensions.js gọi window.ncHydrateCanvas sau mỗi lần chèn preview.
        var ncHydrateTimer = null;
        function ncScheduleHydrate(delay) {
            if (ncHydrateTimer) clearTimeout(ncHydrateTimer);
            ncHydrateTimer = setTimeout(function () {
                ncHydrateTimer = null;
                ncHydrateCanvas();
                ncEnsureCanvasFonts();
            }, delay || 500);
        }
        window.ncHydrateCanvas = function () { ncScheduleHydrate(500); };

        // Lần nạp đầu: đợi lâu hơn cho các preview khối kịp về (kể cả trang không có khối động).
        editor.on('load', function () { ncScheduleHydrate(1000); });

        // ── Font Google trong canvas ──────────────────────────────────────────────
        // Trang public nạp font qua thẻ <link> PageRenderer sinh ra; canvas là iframe riêng nên
        // phải tự nạp, không thì chọn font xong vẫn thấy chữ y hệt cũ và tưởng tính năng hỏng.
        // Nạp THEO NHU CẦU (font nào đang được dùng) chứ không gánh cả danh mục — 27 họ font là
        // vài MB webfont cho mỗi lần mở builder.
        var GOOGLE_FONT_BY_NAME = {};
        GOOGLE_FONTS.forEach(function (f) { GOOGLE_FONT_BY_NAME[f.name.toLowerCase()] = f; });

        function ncEnsureCanvasFonts() {
            var doc = editor.Canvas.getDocument();
            if (!doc || !doc.head) return;

            // Quét cả CSS của trang lẫn CSS site: font có thể do người dùng gõ tay trong tab Code.
            var css = '';
            try { css = editor.getCss() || ''; } catch (e) { /* canvas chưa sẵn sàng */ }
            css += '\n' + (SITE_CUSTOM_CSS || '');

            var re = /font-family\s*:\s*([^;}]+)/gi;
            var m;
            while ((m = re.exec(css)) !== null) {
                m[1].split(',').forEach(function (part) {
                    var name = part.trim().replace(/^["']/, '').replace(/["']$/, '');
                    var font = GOOGLE_FONT_BY_NAME[name.toLowerCase()];
                    if (!font) return;
                    var id = 'nc-gfont-' + name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
                    if (doc.getElementById(id)) return;
                    var link = doc.createElement('link');
                    link.id = id;
                    link.rel = 'stylesheet';
                    link.href = 'https://fonts.googleapis.com/css2?family=' +
                        encodeURIComponent(name).replace(/%20/g, '+') +
                        // Razor rút gọn hai ký tự at thành một, nên JS nhận đúng dạng URL css2 cần.
                        ':wght@' + font.weights + '&display=swap';
                    doc.head.appendChild(link);
                });
            }
        }

        // Chọn font trong Style Manager phải thấy ngay. Debounce vì 'update' bắn liên tục lúc gõ.
        var ncFontScanTimer = null;
        editor.on('update', function () {
            if (ncFontScanTimer) clearTimeout(ncFontScanTimer);
            ncFontScanTimer = setTimeout(function () { ncFontScanTimer = null; ncEnsureCanvasFonts(); }, 300);
        });

        // ── Thư viện ảnh: Asset Manager đọc thẳng từ Media ────────────────────────
        // GrapesJS khởi tạo assetManager rỗng. Thay vì nạp sẵn vài trăm ảnh lúc mở builder,
        // ta gắn thanh tìm kiếm + "Tải thêm" vào modal "Chọn ảnh" và phân trang qua
        // /Admin/Media?handler=List (camelCase {items:[{filePath,fileName,kind,width,height}]},
        // đúng nguồn dữ liệu của trang Media trong admin).
        //
        // Cơ chế render của GrapesJS: all (getAll) là kho, assetsVis là danh sách đang hiển thị.
        // all.reset() KHÔNG tự đồng bộ sang assetsVis, nên sau khi reset phải gọi am.render(models)
        // — hàm này reset assetsVis và vẽ lại đúng thứ tự mảng (am.add() chèn ngược nên không dùng).
        var ncAssets = (function () {
            var state = { q: '', page: 0, hasNext: false, items: [], loading: false, loaded: false };
            var els = null;
            var searchTimer = null;

            function fromMedia(m) {
                return {
                    type: 'image',
                    src: m.filePath,
                    name: m.fileName,
                    height: m.height || undefined,
                    width: m.width || undefined
                };
            }

            function fromUrl(url) {
                return { type: 'image', src: url, name: String(url).split('/').pop() };
            }

            /** Đẩy state.items vào Asset Manager và vẽ lại nếu modal đang mở. */
            function sync() {
                var am = editor.AssetManager;
                am.getAll().reset(state.items);
                if (am.isOpen && am.isOpen()) am.render(am.getAll().models);
            }

            function paint() {
                if (!els) return;
                els.more.disabled = state.loading || !state.hasNext;
                els.more.textContent = state.loading ? 'Đang tải…' : 'Tải thêm';
                els.info.textContent = state.loaded ? state.items.length + ' ảnh' : '';
            }

            async function fetchPage(page) {
                if (state.loading) return;
                state.loading = true;
                paint();
                try {
                    var url = '/Admin/Media?handler=List&kind=Image&p=' + page +
                        '&q=' + encodeURIComponent(state.q);
                    var res = await fetch(url, { headers: apiHeaders(false), credentials: 'include' });
                    if (!res.ok) return;
                    var data = await res.json();
                    var mapped = ((data && data.items) || []).map(fromMedia);
                    state.items = page <= 1 ? mapped : state.items.concat(mapped);
                    state.page = page;
                    state.hasNext = !!(data && data.hasNext);
                    state.loaded = true;
                    sync();
                } catch (e) {
                    // Thư viện lỗi vẫn cho dán URL / upload trực tiếp trong modal.
                } finally {
                    state.loading = false;
                    paint();
                }
            }

            /** Chèn ảnh mới (vừa upload hoặc vừa dán URL) lên đầu danh sách. */
            function prepend(assets) {
                if (!assets || !assets.length) return;
                // Không bật state.loaded: lần mở modal sau vẫn nạp trang 1 từ Media,
                // ảnh vừa upload đã là Media row nên sẽ quay lại ở đầu danh sách.
                state.items = assets.concat(state.items);
                sync();
                paint();
            }

            /**
             * Gắn thanh công cụ vào khung Asset Manager. Chỉ chạy được sau lần render đầu
             * của modal; các lần vẽ lại sau chỉ đụng vào [data-el=assets] nên thanh này còn nguyên.
             */
            function mount() {
                if (els) return true;
                var root = editor.AssetManager.getContainer();
                if (!root) return false;
                var cont = root.querySelector('[class*="assets-cont"]') || root;
                var list = cont.querySelector('[data-el=assets]');
                if (!list) return false;

                var bar = document.createElement('div');
                bar.className = 'nc-am-bar';
                var input = document.createElement('input');
                input.type = 'search';
                input.placeholder = 'Tìm ảnh trong thư viện…';
                var more = document.createElement('button');
                more.type = 'button';
                more.textContent = 'Tải thêm';
                var info = document.createElement('span');
                info.className = 'nc-am-info';
                bar.appendChild(input);
                bar.appendChild(more);
                bar.appendChild(info);
                cont.insertBefore(bar, list);

                input.addEventListener('input', function () {
                    clearTimeout(searchTimer);
                    searchTimer = setTimeout(function () {
                        state.q = input.value.trim();
                        fetchPage(1);
                    }, 350);
                });
                more.addEventListener('click', function () { fetchPage(state.page + 1); });

                els = { bar: bar, input: input, more: more, info: info };
                paint();
                return true;
            }

            return {
                fromUrl: fromUrl,
                prepend: prepend,
                mount: mount,
                ensureLoaded: function () { if (!state.loaded && !state.loading) fetchPage(1); }
            };
        })();

        // Nạp lười: chỉ gọi Media khi người dùng thực sự mở modal chọn ảnh.
        editor.on('asset:open', function () {
            setTimeout(function () {
                ncAssets.mount();
                ncAssets.ensureLoaded();
            }, 0);
        });

        /**
         * uploadFile của Asset Manager: đẩy file qua window.uploadManager (XHR cho file nhỏ,
         * tus cho file lớn) để tạo Media row rồi mới thêm vào thư viện. `clb` chỉ có khi
         * GrapesJS thả ảnh thẳng lên canvas — gọi lại với {data:[…]} để nó gán src.
         */
        function ncUploadToMedia(e, clb, fileUploader) {
            var input = e && e.target;
            var files = (e && e.dataTransfer) ? e.dataTransfer.files : (input && input.files);
            var list = Array.prototype.slice.call(files || []);
            if (!list.length) return;

            if (fileUploader && typeof fileUploader.onUploadStart === 'function') fileUploader.onUploadStart();

            var uploaded = [];
            var pending = list.length;

            function finish() {
                if (--pending > 0) return;
                ncAssets.prepend(uploaded);
                if (fileUploader && typeof fileUploader.onUploadEnd === 'function') {
                    fileUploader.onUploadEnd({ data: uploaded });
                } else if (input) {
                    input.value = '';
                }
                if (clb) clb({ data: uploaded });
            }

            list.forEach(function (file) {
                ncUploadOne(file, function (res) {
                    // Media trả kind cho mọi loại file; chỉ ảnh mới có chỗ trong Asset Manager.
                    if (res && res.location && (!res.kind || res.kind === 'image')) {
                        uploaded.push({
                            type: 'image',
                            src: res.location,
                            name: res.fileName || file.name,
                            width: res.width || undefined,
                            height: res.height || undefined
                        });
                    }
                    finish();
                }, finish);
            });
        }

        /** Một file → Media row. Ưu tiên uploadManager; thiếu nó thì POST thẳng. */
        function ncUploadOne(file, onDone, onError) {
            if (window.uploadManager && document.getElementById('upload-tracker')) {
                window.uploadManager.enqueue(file, { onDone: onDone, onError: onError });
                return;
            }
            var fd = new FormData();
            fd.append('file', file);
            fetch('/admin/Media/Upload', {
                method: 'POST', body: fd, headers: apiHeaders(false), credentials: 'include'
            })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('upload')); })
                .then(onDone)
                .catch(function () { onError(); });
        }

        // ncAssets / ncUploadToMedia là biến cục bộ của attach(); đưa ra ngoài cho cấu hình
        // assetManager (đã truyền vào grapesjs.init trước đó, nhưng chỉ chạy khi người dùng upload).
        global.ncCanvas.assets = ncAssets;
        global.ncCanvas.uploadFile = ncUploadToMedia;
    }

    global.ncCanvas = { attach: attach, fontFamilyOptions: fontFamilyOptions };
})(window);
