// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

/**
 * Test cho nc-code-format.js — beautify khi mở / minify khi lưu của mọi panel code.
 *
 * Chạy trong trình duyệt thật và nạp ĐÚNG các bundle vendor qua <script src> (route giả lập
 * static server): chỗ dễ hỏng âm thầm nhất là "bundle minified có thật sự gán global
 * csso/Terser/beautifier khi nạp bằng script tag hay không" — test kiểu node require bỏ lỡ lớp đó.
 *
 * Các tính chất được chốt (hỏng là hỏng âm thầm trên trang public):
 *   - CSS/JS sai cú pháp phải giữ NGUYÊN VĂN kèm error, không được lưu thành mảnh vỡ;
 *   - DSL CSS khối (&, ^, khai báo trần) sống sót qua cả beautify lẫn minify;
 *   - [href^=...] và content:"&" KHÔNG bị nhận nhầm là DSL;
 *   - HTML không bao giờ bị minify (khoảng trắng giữa thẻ inline là ký tự có nghĩa).
 */

const WEBROOT = path.resolve(__dirname, '../../src/NewsCMS.Web/wwwroot');

test.beforeEach(async ({ page }) => {
  await page.route('**/*', (route) => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname === '/test.html') {
      return route.fulfill({
        contentType: 'text/html',
        body: '<!doctype html><html><head><meta charset="utf-8"></head><body>' +
              '<script src="/js/admin/nc-code-format.js"></script></body></html>'
      });
    }
    const file = path.join(WEBROOT, pathname);
    if (fs.existsSync(file) && fs.statSync(file).isFile()) {
      return route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync(file) });
    }
    return route.fulfill({ status: 404, body: 'not found' });
  });
  await page.goto('https://nc.local/test.html');
  await expect.poll(() => page.evaluate(() => typeof window.ncCodeFormat)).toBe('object');
});

test('beautify mở CSS minify một dòng thành nhiều dòng, minify lại vẫn đủ rule', async ({ page }) => {
  const out = await page.evaluate(async () => {
    const f = window.ncCodeFormat;
    await f.ensure(['css']);
    const min = '.a{color:red}.b{padding:1px 2px}';
    const pretty = f.beautify(min, 'css');
    const back = f.minify(pretty, 'css');
    return { pretty, back };
  });
  expect(out.pretty.split('\n').length).toBeGreaterThan(2);
  expect(out.back.code).toContain('.a{color:red}');
  expect(out.back.code).toContain('.b{padding:1px 2px}');
  expect(out.back.error).toBeNull();
});

test('CSS khối giữ &, ^ và khai báo trần qua beautify lẫn minify', async ({ page }) => {
  const out = await page.evaluate(async () => {
    const f = window.ncCodeFormat;
    await f.ensure(['blockcss']);
    const block = '&:hover { color: red }\n^#top { margin: 0 }\npadding: 32px';
    const pretty = f.beautify(block, 'blockcss');
    const back = f.minify(pretty, 'blockcss');
    return { pretty, code: back.code, error: back.error };
  });
  expect(out.pretty).not.toContain('ncfmt-');
  expect(out.code).toContain('&:hover');
  expect(out.code).toContain('^#top');
  expect(out.code).toContain('padding:32px');
  expect(out.code).not.toContain('ncfmt-');
  expect(out.error).toBeNull();
});

test('[href^=] và content:"&" không bị nhận nhầm là DSL', async ({ page }) => {
  const out = await page.evaluate(async () => {
    const f = window.ncCodeFormat;
    await f.ensure(['blockcss']);
    return f.minify('a[href^="/ban"] { content: "&" }', 'blockcss').code;
  });
  expect(out).toContain('[href^="/ban"]');
  expect(out).toContain('"&"');
  expect(out).not.toContain('ncfmt-');
});

test('JS sai cú pháp giữ nguyên văn kèm error; HTML không minify', async ({ page }) => {
  const out = await page.evaluate(async () => {
    const f = window.ncCodeFormat;
    await f.ensure(['js', 'html']);
    return {
      js: f.minify('function oops( {', 'js'),
      html: f.minify('<a>x</a> <a>y</a>', 'html')
    };
  });
  expect(out.js.code).toBe('function oops( {');
  expect(out.js.error).not.toBeNull();
  expect(out.html.skipped).toBe(true);
  expect(out.html.code).toBe('<a>x</a> <a>y</a>');
});
