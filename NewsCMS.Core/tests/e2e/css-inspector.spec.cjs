// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

/**
 * Test cho nc-css-inspector.js — bộ truy vết "rule CSS này nằm ở đâu".
 *
 * Chạy trong trình duyệt thật vì phép khớp cốt lõi là `el.matches(selector)`; giả lập bằng regex
 * sẽ test một thứ khác với thứ chạy thật. Nạp THẲNG file production, không chép tay.
 *
 * Điều được bảo vệ ở đây là các tính chất dễ hỏng âm thầm:
 *   - số dòng phải đúng kể cả khi phía trên có comment/chuỗi chứa dấu { } ;
 *   - rule :hover phải hiện ra dù chuột không hover (đây là loại rule khó mò nhất);
 *   - CSS riêng khối viết `.title` nhưng chạy dưới dạng `[data-nc-sid] .title` — phải khớp theo bản
 *     đã scope mà BÁO CÁO theo bản gốc, nếu không người dùng nhảy tới dòng không đọc hiểu được;
 *   - khai báo bị ghi đè phải bị đánh dấu, vì sửa nhầm dòng đó là sửa xong không thấy gì đổi.
 */

const SRC = fs.readFileSync(
  path.resolve(__dirname, '../../src/NewsCMS.Web/wwwroot/js/admin/nc-css-inspector.js'),
  'utf8'
);

const PAGE = `<!doctype html><html><head><meta charset="utf-8"></head><body>
<section data-nc-sid="abc1" id="ic5kz" class="chu-grid">
  <h2 class="title">Tiêu đề</h2>
  <a class="btn" href="#">Nút</a>
</section>
<script>${SRC}<\/script>
</body></html>`;

test.beforeEach(async ({ page }) => {
  await page.setContent(PAGE, { waitUntil: 'load' });
  await expect.poll(() => page.evaluate(() => typeof window.ncCssInspector)).toBe('object');
});

test('số dòng đúng khi có comment và chuỗi chứa dấu ngoặc nhọn', async ({ page }) => {
  const rules = await page.evaluate(() => {
    const css = [
      '/* chu thich { gia }',
      '   nhieu dong */',
      '.title {',
      '  background: url("a{b;c");',
      '}',
      '@media (max-width: 768px) {',
      '  .title { color: red }',
      '}'
    ].join('\n');
    return window.ncCssInspector.parse(css);
  });

  expect(rules).toHaveLength(2);
  expect(rules[0].line).toBe(3);                       // không bị comment 2 dòng làm lệch
  expect(rules[0].body).toContain('url("a{b;c")');     // dấu { ; trong chuỗi không phá parser
  expect(rules[1].line).toBe(7);
  expect(rules[1].at).toBe('@media (max-width: 768px)');
});

test('rule :hover vẫn truy ra được dù chuột không hover', async ({ page }) => {
  const found = await page.evaluate(() => {
    const el = document.querySelector('.btn');
    return window.ncCssInspector.collect(el, [{
      id: 'page', kind: 'page', label: 'CSS trang', editable: true, order: 300,
      text: '.btn:hover { color: red }'
    }]);
  });

  expect(found).toHaveLength(1);
  expect(found[0].states).toEqual(['hover']);
  expect(found[0].selector).toBe('.btn:hover');  // hiện đúng thứ người dùng đã gõ
});

test('CSS riêng khối: khớp theo selector đã scope, báo cáo theo selector gốc + dòng gốc', async ({ page }) => {
  const found = await page.evaluate(() => {
    const el = document.querySelector('.title');
    return window.ncCssInspector.collect(el, [{
      id: 'block:abc1', kind: 'block', label: 'CSS của khối', editable: true, order: 400,
      text: '/* css khoi */\n.title { color: teal }',
      scope: (sel) => ['[data-nc-sid="abc1"] ' + sel]
    }]);
  });

  expect(found).toHaveLength(1);
  expect(found[0].selector).toBe('.title');   // KHÔNG lộ dạng đã scope ra UI
  expect(found[0].line).toBe(2);              // dòng trong văn bản người dùng đang sửa
  expect(found[0].kind).toBe('block');
});

test('selector đã scope không khớp thì không báo nhầm', async ({ page }) => {
  const found = await page.evaluate(() => {
    const el = document.querySelector('.title');
    return window.ncCssInspector.collect(el, [{
      id: 'block:zzz', kind: 'block', label: 'CSS khối khác', editable: true, order: 400,
      text: '.title { color: teal }',
      scope: (sel) => ['[data-nc-sid="zzz"] ' + sel]   // sid của khối KHÁC
    }]);
  });
  expect(found).toHaveLength(0);
});

test('khai báo bị nguồn ưu tiên cao hơn ghi đè thì bị đánh dấu', async ({ page }) => {
  const found = await page.evaluate(() => {
    const el = document.querySelector('.title');
    return window.ncCssInspector.collect(el, [
      { id: 'site', kind: 'site', label: 'CSS site', editable: true, order: 200,
        text: '.title { color: red; font-size: 20px }' },
      { id: 'page', kind: 'page', label: 'CSS trang', editable: true, order: 300,
        text: '.title { color: blue }' }
    ]);
  });

  // collect() trả từ ưu tiên THẤP đến CAO.
  const site = found[0], pageRule = found[1];
  expect(site.sourceId).toBe('site');
  expect(pageRule.sourceId).toBe('page');

  const siteColor = site.decls.find((d) => d.prop === 'color');
  const siteSize = site.decls.find((d) => d.prop === 'font-size');
  expect(siteColor.winning).toBe(false);  // bị CSS trang đè
  expect(siteSize.winning).toBe(true);    // không ai đè
  expect(pageRule.decls[0].winning).toBe(true);
});

test('rule trong @media không bị kết luận là "bị ghi đè"', async ({ page }) => {
  const found = await page.evaluate(() => {
    const el = document.querySelector('.title');
    return window.ncCssInspector.collect(el, [
      { id: 'page', kind: 'page', label: 'CSS trang', editable: true, order: 300,
        text: '.title { color: blue }\n@media (max-width: 480px) { .title { color: green } }' }
    ]);
  });

  const media = found.find((r) => r.at);
  expect(media).toBeTruthy();
  expect(media.conditional).toBe(true);
  // null = "không kết luận", khác hẳn false = "chắc chắn bị đè".
  expect(media.decls[0].winning).toBeNull();
});

test('readOnlySheets bỏ qua các thẻ style do chính builder quản', async ({ page }) => {
  const labels = await page.evaluate(() => {
    const own = document.createElement('style');
    own.id = 'nc-custom-page-css';
    own.textContent = '.title { color: red }';
    document.head.appendChild(own);

    const theme = document.createElement('style');
    theme.id = 'theme-sheet';
    theme.textContent = '.title { color: green }';
    document.head.appendChild(theme);

    return window.ncCssInspector
      .readOnlySheets(document, ['nc-custom-page-css'])
      .map((s) => s.label);
  });

  expect(labels).toContain('theme-sheet');
  expect(labels).not.toContain('nc-custom-page-css');  // tránh hiện trùng với nguồn văn bản
});
