// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

/**
 * Chống tái phát bug 0.1 (canvas vỡ bố cục do lệch realm).
 *
 * Gốc bug: GrapesJS append node tạo bằng `document` của trang cha vào iframe. Node giữ prototype
 * của realm cha, nên trong iframe `node instanceof iframe.HTMLElement` === false. fizzy-ui-utils
 * (Masonry/imagesLoaded/Isotope) lọc con bằng đúng phép thử đó → 0 item → lưới cao 0 → item
 * position:absolute chồng đống, tràn khỏi section.
 *
 * Test này KHÔNG dựng GrapesJS thật (nặng, cần app chạy). Thay vào đó nó tái tạo ĐÚNG điều kiện
 * gây lỗi — một node của realm cha được adopt vào iframe — rồi khẳng định:
 *   (a) trước khi bridge: phép thử cross-realm thất bại (chứng minh bug có thật, test không rỗng);
 *   (b) sau ncBridgeRealms(iframeWin, parentWin): phép thử thành công cho cả node hai realm;
 *   (c) một layout họ fizzy (lọc con bằng instanceof) đếm đúng số item thay vì 0.
 *
 * ncBridgeRealms lấy TỪ file production nc-realm-bridge.js (không phải bản chép tay) — file đổi
 * mà mất tính chất này là test đỏ.
 */

const BRIDGE_SRC = fs.readFileSync(
  path.resolve(__dirname, '../../src/NewsCMS.Web/wwwroot/js/admin/nc-realm-bridge.js'),
  'utf8'
);

/** Trang cha rỗng có sẵn hàm bridge — iframe tạo động trong từng test. */
const PARENT_HTML =
  '<!doctype html><html><head><meta charset="utf-8"></head><body>' +
  `<script>${BRIDGE_SRC}<\/script>` +
  '</body></html>';

test.beforeEach(async ({ page }) => {
  await page.setContent(PARENT_HTML, { waitUntil: 'load' });
  await expect.poll(() => page.evaluate(() => typeof window.ncBridgeRealms)).toBe('function');
});

test('node của realm cha adopt vào iframe: instanceof hỏng trước bridge, đúng sau bridge', async ({ page }) => {
  const result = await page.evaluate(() => {
    // iframe con — realm khác trang cha.
    const iframe = document.createElement('iframe');
    document.body.appendChild(iframe);
    const iwin = /** @type {Window} */ (iframe.contentWindow);
    const idoc = iwin.document;

    // Node tạo bằng document CHA rồi adopt sang iframe — đúng cách GrapesJS dựng canvas.
    const parentNode = document.createElement('div');
    parentNode.className = 'chu-pin-item';
    const adopted = idoc.adoptNode(parentNode);
    idoc.body.appendChild(adopted);

    // Node do CHÍNH iframe tạo — phải luôn hợp lệ, kể cả sau bridge.
    const nativeNode = idoc.createElement('span');
    idoc.body.appendChild(nativeNode);

    const before = adopted instanceof iwin.HTMLElement;

    // @ts-ignore — hàm gắn lên window bởi nc-realm-bridge.js
    window.ncBridgeRealms(iwin, window);

    return {
      before,
      afterAdopted: adopted instanceof iwin.HTMLElement,
      afterNative: nativeNode instanceof iwin.HTMLElement,
    };
  });

  expect(result.before).toBe(false);      // (a) bug có thật: node cha KHÔNG là HTMLElement của iframe
  expect(result.afterAdopted).toBe(true); // (b) bridge chấp nhận realm cha
  expect(result.afterNative).toBe(true);  // (b) KHÔNG phá node do iframe tự tạo
});

test('bộ lọc kiểu fizzy-ui-utils đếm đủ item sau bridge (mô phỏng Masonry)', async ({ page }) => {
  const counts = await page.evaluate(() => {
    const iframe = document.createElement('iframe');
    document.body.appendChild(iframe);
    const iwin = /** @type {Window} */ (iframe.contentWindow);
    const idoc = iwin.document;

    const grid = idoc.adoptNode(document.createElement('div'));
    idoc.body.appendChild(grid);

    // 17 item — đúng con số trong ghi chép bug — tạo bằng document CHA rồi adopt.
    for (let i = 0; i < 17; i++) {
      const item = document.createElement('div');
      item.className = 'chu-pin-item';
      grid.appendChild(idoc.adoptNode(item));
    }

    // filterFindElements của fizzy-ui-utils rút gọn: chỉ giữ con là HTMLElement CỦA IFRAME.
    const fizzyFilter = () =>
      Array.prototype.filter.call(grid.children, (el) => el instanceof iwin.HTMLElement).length;

    const before = fizzyFilter();
    // @ts-ignore
    window.ncBridgeRealms(iwin, window);
    const after = fizzyFilter();

    return { before, after };
  });

  expect(counts.before).toBe(0);  // gốc bug: Masonry thấy 0 item → lưới cao 0
  expect(counts.after).toBe(17);  // sau bridge: thấy đủ 17 item
});
