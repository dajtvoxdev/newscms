// Vendor GrapesJS + @tailwindcss/browser dist files vào wwwroot/lib/grapesjs/.
// Chạy: npm run vendor:builder  (sau khi npm install / bump phiên bản trong package.json).
// Lý do tự vendor thay vì CDN: CSP và chạy offline — xem plan Site Builder, mục 2.
import { copyFileSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(here, '..');
const nodeModules = join(webRoot, 'node_modules');
const target = join(webRoot, 'wwwroot', 'lib', 'grapesjs');

// Phiên bản đang vendor — đọc từ package.json của từng package để ghi log,
// còn việc copy luôn lấy bản trong node_modules (đã được npm pin theo package.json).
function readVersion(pkg) {
  try {
    return JSON.parse(readFileSync(join(nodeModules, pkg, 'package.json'), 'utf8')).version;
  } catch {
    return '?';
  }
}

const files = [
  // [source (relative to node_modules), dest (relative to wwwroot/lib/grapesjs)]
  ['grapesjs/dist/grapes.min.js', 'grapes.min.js'],
  ['grapesjs/dist/css/grapes.min.css', 'grapes.min.css'],
  ['grapesjs/LICENSE', 'LICENSE-grapesjs.txt'],
  ['@tailwindcss/browser/dist/index.global.js', 'tailwind-browser.js'],
];

// CodeMirror 5 (dùng bởi panel Code editor của builder) → wwwroot/lib/codemirror/.
// GrapesJS nhúng codemirror nội bộ nhưng không expose, nên phải vendor bản riêng.
const cmTarget = join(webRoot, 'wwwroot', 'lib', 'codemirror');
const cmFiles = [
  ['codemirror/lib/codemirror.js', 'codemirror.min.js'],
  ['codemirror/lib/codemirror.css', 'codemirror.min.css'],
  ['codemirror/LICENSE', 'LICENSE-codemirror.txt'],
  ['codemirror/mode/javascript/javascript.js', 'mode/javascript/javascript.min.js'],
  ['codemirror/mode/css/css.js', 'mode/css/css.min.js'],
  ['codemirror/mode/xml/xml.js', 'mode/xml/xml.min.js'],
  ['codemirror/mode/htmlmixed/htmlmixed.js', 'mode/htmlmixed/htmlmixed.min.js'],
  ['codemirror/addon/edit/matchbrackets.js', 'addon/edit/matchbrackets.min.js'],
  ['codemirror/addon/edit/closebrackets.js', 'addon/edit/closebrackets.min.js'],
  ['codemirror/addon/display/autorefresh.js', 'addon/display/autorefresh.min.js'],
  ['codemirror/theme/material-darker.css', 'theme/material-darker.min.css'],
];

// Beautify + minify cho panel Code (nc-code-format.js) → wwwroot/lib/codeformat/.
// Tự viết bộ format là sai chỗ: minify CSS/JS đúng cần AST (regex vs phép chia, ASI, at-rule),
// làm tay thì lỗi âm thầm phá CSS/JS đang chạy trên trang public.
// terser nặng ~1MB nên nc-code-format.js nạp LAZY, chỉ khi thật sự bấm lưu/format.
const fmtTarget = join(webRoot, 'wwwroot', 'lib', 'codeformat');
const fmtFiles = [
  // beautifier.min.js gộp cả 3 (js/css/html) và expose global `beautifier`.
  ['js-beautify/js/lib/beautifier.min.js', 'beautifier.min.js'],
  ['js-beautify/LICENSE', 'LICENSE-js-beautify.txt'],
  // csso: bundle IIFE gán `var csso` ở top-level → thành window.csso khi nạp bằng <script>.
  ['csso/dist/csso.js', 'csso.js'],
  ['csso/LICENSE', 'LICENSE-csso.txt'],
  // terser: UMD gán global.Terser. Có minify_sync nên không cần Promise ở chỗ lưu.
  ['terser/dist/bundle.min.js', 'terser.min.js'],
  ['terser/LICENSE', 'LICENSE-terser.txt'],
];

mkdirSync(target, { recursive: true });

for (const [src, dest] of files) {
  copyFileSync(join(nodeModules, src), join(target, dest));
  console.log(`  vendored ${src} -> wwwroot/lib/grapesjs/${dest}`);
}

mkdirSync(cmTarget, { recursive: true });
for (const [src, dest] of cmFiles) {
  const destPath = join(cmTarget, dest);
  mkdirSync(dirname(destPath), { recursive: true });
  copyFileSync(join(nodeModules, src), destPath);
  console.log(`  vendored ${src} -> wwwroot/lib/codemirror/${dest}`);
}

console.log(`\ngrapesjs ${readVersion('grapesjs')}, @tailwindcss/browser ${readVersion('@tailwindcss/browser')}, codemirror ${readVersion('codemirror')}`);

mkdirSync(fmtTarget, { recursive: true });
for (const [src, dest] of fmtFiles) {
  copyFileSync(join(nodeModules, src), join(fmtTarget, dest));
  console.log(`  vendored ${src} -> wwwroot/lib/codeformat/${dest}`);
}

console.log(`js-beautify ${readVersion('js-beautify')}, csso ${readVersion('csso')}, terser ${readVersion('terser')}`);
console.log('Done. KHÔNG copy file .map để giữ wwwroot gọn.');
