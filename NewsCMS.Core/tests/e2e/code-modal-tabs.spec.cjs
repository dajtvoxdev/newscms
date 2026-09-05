// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

/**
 * Chống tái phát lỗi "bấm sang tab khác thì pane không hiện".
 *
 * Gốc lỗi: CSS hiện pane BẰNG `.active` (`.nc-tab-pane{display:none}` / `.nc-tab-pane.active{display:block}`),
 * nhưng modal code của trang tự viết lại phần chuyển tab và chỉ gỡ/thêm class `hidden` — pane đích
 * không bao giờ được `.active` nên không bao giờ hiện. Không có lỗi console, không có gì đỏ: bấm
 * "Sửa" ra một modal trông như không phản ứng. Tab JavaScript và Library dính cùng lỗi từ trước.
 *
 * Hai chốt chặn ở đây, vì mỗi cái bắt một nửa vấn đề:
 *   1) hợp đồng CSS — `.active` là thứ quyết định hiện/ẩn, `hidden` KHÔNG đủ;
 *   2) hợp đồng mã — cả hai modal phải dùng chung wireTabs, không ai chép tay lại logic chuyển tab.
 */

const CSS = fs.readFileSync(
  path.resolve(__dirname, '../../src/NewsCMS.Web/wwwroot/css/nc-builder.css'),
  'utf8'
);
const JS = fs.readFileSync(
  path.resolve(__dirname, '../../src/NewsCMS.Web/wwwroot/js/admin/nc-builder-extensions.js'),
  'utf8'
);

test('pane chỉ hiện khi có .active — gỡ mỗi class hidden là không đủ', async ({ page }) => {
  await page.setContent(
    `<!doctype html><html><head><meta charset="utf-8"><style>${CSS}</style></head><body>` +
    '<div class="nc-tab-pane active" data-pane="css">CSS</div>' +
    '<div class="nc-tab-pane" data-pane="js">JS</div>' +
    '<div class="nc-tab-pane hidden" data-pane="lib">LIB</div>' +
    '</body></html>',
    { waitUntil: 'load' }
  );

  await expect(page.locator('[data-pane="css"]')).toBeVisible();
  // Đây chính là ca gây lỗi: đã gỡ `hidden` nhưng thiếu `.active` → vẫn ẩn.
  await expect(page.locator('[data-pane="js"]')).toBeHidden();
  await expect(page.locator('[data-pane="lib"]')).toBeHidden();

  await page.evaluate(() => {
    document.querySelector('[data-pane="css"]').classList.remove('active');
    document.querySelector('[data-pane="js"]').classList.add('active');
  });

  await expect(page.locator('[data-pane="css"]')).toBeHidden();
  await expect(page.locator('[data-pane="js"]')).toBeVisible();
});

test('cả hai modal dùng chung wireTabs, không chép tay logic chuyển tab', () => {
  // wireTabs là bản DUY NHẤT xử lý đúng cả .active lẫn .hidden.
  expect(JS).toContain('function wireTabs(');

  // Đếm NƠI GỌI, không tính dòng `function wireTabs(modal, onSwitch)`.
  const calls = JS.match(/(?<!function\s)wireTabs\s*\(\s*modal/g) || [];
  expect(calls.length).toBe(2); // đúng hai modal: code khối + code trang

  // Bản chép tay cũ nhận diện qua việc tự gắn listener lên '.nc-tab' ngoài wireTabs.
  const wireTabsBody = JS.slice(JS.indexOf('function wireTabs('));
  const handlersOutsideWireTabs = (JS.match(/querySelectorAll\('\.nc-tab'\)/g) || []).length -
                                  (wireTabsBody.slice(0, 400).match(/querySelectorAll\('\.nc-tab'\)/g) || []).length;
  expect(handlersOutsideWireTabs).toBe(0);
});
