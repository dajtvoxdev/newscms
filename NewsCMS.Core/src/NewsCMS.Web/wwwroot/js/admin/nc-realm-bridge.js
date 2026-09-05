/*
 * nc-realm-bridge.js — sửa lệch realm giữa trang admin và iframe canvas của GrapesJS.
 *
 * GrapesJS dựng DOM canvas bằng `document` của trang admin rồi append sang iframe. Node được
 * adopt nhưng VẪN giữ prototype của realm cha, nên bên trong iframe phép thử
 * `el instanceof HTMLElement` trả về false. Thư viện layout họ fizzy-ui-utils (Masonry,
 * imagesLoaded, Isotope) lọc phần tử con bằng đúng phép thử đó → tìm thấy 0 item → Masonry
 * đặt height lưới = 0 và mọi item (position:absolute) chồng lên nhau ở góc trên trái, tràn ra
 * ngoài section. Đây là lỗi "một ảnh khổng lồ đè lên cả trang" trong canvas.
 *
 * Cách sửa: định nghĩa Symbol.hasInstance cho các constructor DOM của iframe để chúng chấp nhận
 * THÊM prototype của realm cha. KHÔNG gán đè global (win.HTMLElement = parent.HTMLElement) vì như
 * thế node do chính iframe tạo — new Image(), doc.createElement — lại hoá "không hợp lệ"; ở đây cả
 * hai realm đều được chấp nhận.
 *
 * Chỉ ảnh hưởng canvas builder; trang public chạy một realm duy nhất nên không liên quan.
 *
 * Hàm thuần, KHÔNG tự giữ cờ "đã chạy" — caller (Edit.cshtml) sở hữu once-guard, còn test gọi
 * trực tiếp trên iframe do nó tạo. `parentWin` mặc định là window hiện tại.
 */
(function (global) {
  'use strict';

  function ncBridgeRealms(win, parentWin) {
    parentWin = parentWin || global;
    if (!win || win === parentWin) return;

    var names = ['Node', 'Element', 'CharacterData', 'Text', 'DocumentFragment', 'SVGElement'];
    try {
      Object.getOwnPropertyNames(win).forEach(function (k) {
        if (/^HTML[A-Za-z]*Element$/.test(k)) names.push(k);
      });
    } catch (e) { /* window bị chặn liệt kê — dùng danh sách tối thiểu ở trên */ }

    names.forEach(function (name) {
      var Inner, Outer;
      try { Inner = win[name]; Outer = parentWin[name]; } catch (e) { return; }
      if (typeof Inner !== 'function' || typeof Outer !== 'function' || Inner === Outer) return;
      if (!Inner.prototype || !Outer.prototype) return;
      try {
        Object.defineProperty(Inner, Symbol.hasInstance, {
          configurable: true,
          value: function (v) {
            return Inner.prototype.isPrototypeOf(v) || Outer.prototype.isPrototypeOf(v);
          }
        });
      } catch (e) { /* constructor bị đóng băng — bỏ qua, chỉ mất preview của lib đó */ }
    });
  }

  global.ncBridgeRealms = ncBridgeRealms;
})(typeof window !== 'undefined' ? window : this);
