/*
 * nc-code-editor.js — gắn CodeMirror + beautify/minify cho các textarea code nằm NGOÀI
 * builder GrapesJS (trang Layouts → Custom Code, EditLayout).
 *
 * Builder (Edit/EditShell) đã tự tạo CodeMirror trong nc-builder-extensions.js; file này
 * chỉ bọc phần lặp lại cho các trang admin thường:
 *   - attach(textarea, lang)           fromTextArea với cấu hình chuẩn (theme material-darker).
 *   - fillBeautified(ed, value, lang)  nạp lib lười rồi setValue bản đã beautify; trả Promise
 *                                      của nội dung ĐANG HIỂN THỊ — bên gọi cần chính chuỗi đó
 *                                      làm baseline "người dùng có sửa không" (so với bản đã
 *                                      beautify, không so bản raw trong DB, kẻo lần lưu nào cũng
 *                                      bị tính là có sửa).
 *   - syncForSave(ed, lang, label, ta) minify (nếu người dùng đang bật) rồi ghi ngược vào
 *                                      textarea gốc, trả chuỗi sẽ gửi server. Không bao giờ
 *                                      throw — lỗi parse thì giữ nguyên bản gốc (nc-code-format.js).
 *
 * Phụ thuộc: lib/codemirror/* và js/admin/nc-code-format.js phải nạp trước file này.
 * CodeMirror vắng mặt thì attach trả null và mọi hàm rơi về textarea thô — trang vẫn dùng được.
 */
(function (global) {
  'use strict';

  /** blockcss dùng mode css thường — phần DSL (&, ^, khai báo trần) đã có nc-code-format xử lý. */
  var MODES = { css: 'css', blockcss: 'css', js: 'javascript', html: 'htmlmixed' };

  function fmt() { return global.ncCodeFormat || null; }

  function attach(textarea, lang) {
    if (!global.CodeMirror || !textarea) return null;
    return global.CodeMirror.fromTextArea(textarea, {
      mode: MODES[lang] || 'css',
      theme: 'material-darker',
      lineNumbers: true,
      autoRefresh: true,
      matchBrackets: true,
      autoCloseBrackets: true,
      lineWrapping: true
    });
  }

  /**
   * Beautify rồi đưa vào editor. Trả Promise<string> là nội dung đang hiển thị.
   * Editor null (CodeMirror chưa nạp được) -> resolve bản gốc; bên gọi phải tự đặt
   * giá trị textarea trước khi gọi hàm này để đường thoái hoá vẫn hiển thị đúng.
   */
  function fillBeautified(ed, value, lang) {
    var src = value == null ? '' : String(value);
    if (!ed) return Promise.resolve(src);
    var f = fmt();
    if (!f) { ed.setValue(src); return Promise.resolve(src); }
    return f.ensure(lang).then(function () {
      var out = f.beautify(src, lang);
      ed.setValue(out);
      return out;
    });
  }

  /**
   * Minify nội dung editor (nếu đang bật), đồng bộ xuống textarea gốc, trả chuỗi để gửi server.
   * fallbackTextarea dùng khi CodeMirror vắng mặt để đường thoái hoá không mất dữ liệu.
   */
  function syncForSave(ed, lang, label, fallbackTextarea) {
    var raw = ed ? ed.getValue() : (fallbackTextarea ? fallbackTextarea.value : '');
    var f = fmt();
    var out = f ? f.forSave(raw, lang, label) : raw;
    var ta = (ed && ed.getTextArea) ? ed.getTextArea() : (fallbackTextarea || null);
    if (ta) ta.value = out;
    return out;
  }

  global.ncCodeEditor = {
    attach: attach,
    fillBeautified: fillBeautified,
    syncForSave: syncForSave
  };
})(window);