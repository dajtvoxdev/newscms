namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Tài sản Plyr dùng chung cho mọi đường render nội dung Universal (EntityContentBlock,
/// PostContentType/ProductContentType fallback).
///
/// Root cause từng gây bug "trang chi tiết không thấy Plyr": mỗi đường render tự nhúng
/// Plyr riêng — EntityContentBlock có nhưng PostContentType fallback không, nên site nào
/// không có PostTemplate (vd chu-kafe) render body trần dù video vẫn nằm trong HTML.
/// Gom một chỗ để đường render mới nào cũng chỉ cần gọi <c>ForBody</c>, không lệch nhau.
///
/// Chuỗi trả về giữ byte-identical với markup EntityContentBlock từng sinh (test
/// EntityBlocksTests assert Contains các chuỗi này) — đổi format ở đây phải sửa test theo.
/// Script inline không tự gắn nonce: <c>PageRenderer.StampInlineScriptNonce</c> gắn nonce
/// CSP per-request khi ráp trang; nonce sinh per-request nên block/content-type không biết
/// được tại thời điểm render.
/// </summary>
internal static class PlyrAssets
{
    /// <summary>
    /// Version của bộ Plyr đã vendor (khớp version trong wwwroot/lib/plyr). Gắn vào query string
    /// của css/js/svg để ĐỔI URL mỗi khi cập nhật Plyr — trình duyệt buộc tải lại thay vì dùng
    /// bản cache cũ (các file này phục vụ không kèm Cache-Control nên browser tự đoán thời hạn).
    /// Cập nhật Plyr → bump số này.
    /// </summary>
    public const string Version = "384";

    /// <summary>
    /// Trả về chuỗi CSS/JS Plyr cần nhúng kèm nội dung, hoặc rỗng khi body không có media —
    /// tránh tải thừa plyr.css/js cho bài không có video/audio.
    /// Selector và rule đều scope theo <c>.nc-post-body</c> nên áp dụng được cho cả fallback
    /// lẫn entity-content (cả hai đều bọc body trong div class đó).
    /// </summary>
    public static string ForBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        if (!body.Contains("<video", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("<audio", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return "<style>.nc-post-body video,.nc-post-body audio{width:100%;height:auto;max-width:100%;border-radius:12px}"
             // object-fit:contain là mấu chốt của bug "xem trên mobile mất phần chữ dưới": Plyr ép
             // .plyr video{width:100%;height:100%} nên khi khung khác tỉ lệ gốc thì video bị cắt và
             // dải chữ cháy sẵn ở đáy khung biến mất. contain luôn thu vừa khung (viền đen thay vì
             // cắt) — đây là chế độ mặc định.
             + ".nc-post-body .plyr video{object-fit:contain;background:#000}"
             // TOÀN MÀN HÌNH: Plyr để .plyr__video-wrapper{overflow:hidden}. Ở chế độ này khung bị
             // ép đúng bằng màn hình, còn thẻ video vẫn giữ height:auto theo bề ngang nên CAO HƠN
             // khung -> phần đáy bị cắt thẳng. Đo thật trên màn hình ngang 844x390: thẻ video
             // 844x475 nằm trong khung 844x390, chỉ thấy 82% — mất đúng dải chữ ở đáy (và ở chế độ
             // Lấp đầy thì mất cả trên lẫn dưới). Ép thẻ video vừa khít khung rồi để object-fit
             // quyết định: contain = thấy đủ khung hình (viền đen hai bên), cover = phủ kín.
             // Phải khai báo cả .plyr--fullscreen-fallback: trình duyệt mobile (iOS Safari) không
             // cho fullscreen trên <div> nên Plyr tự chuyển sang chế độ position:fixed.
             + ".nc-post-body .plyr--video:fullscreen video,"
             + ".nc-post-body .plyr--video.plyr--fullscreen-fallback video{"
             + "width:100%!important;height:100%!important;max-height:100%!important;object-fit:contain!important}"
             + ".nc-post-body .plyr--video.plyr--nc-fill:fullscreen video,"
             + ".nc-post-body .plyr--video.plyr--nc-fill.plyr--fullscreen-fallback video{object-fit:cover!important}"
             // Khung 16:9 của chế độ Lấp đầy cũng phải nhường cho màn hình, nếu không khung cao hơn
             // màn hình và thanh điều khiển bị đẩy ra ngoài tầm nhìn.
             + ".nc-post-body .plyr--video.plyr--nc-fill:fullscreen .plyr__video-wrapper,"
             + ".nc-post-body .plyr--video.plyr--nc-fill.plyr--fullscreen-fallback .plyr__video-wrapper{"
             + "aspect-ratio:auto;height:100%}"

             // GHI CHÚ (2026-09-21): đã thử chừa một dải riêng dưới video cho thanh điều khiển để
             // nó không phủ lên dải chữ cháy sẵn ở đáy khung, nhưng người dùng từ chối: player cao
             // hơn video 57px trông như "cục đen lòi ở dưới" và làm player sai tỉ lệ so với video.
             // Chốt: GIỮ nguyên hành vi gốc của Plyr (thanh phủ đáy video, tự ẩn khi đang phát).
             // Đừng thêm lại padding-bottom ở đây nếu chưa hỏi người dùng.
             // "Lấp đầy": khung 16:9 + video phủ kín, CĂN GIỮA. Plyr tự đặt inline height cho video
             // (đo thật: 636px cho video dọc 9:16) và rule fixed-ratio của Plyr neo top:0 — chỉ hiện
             // phần TRÊN của video, cắt mất đúng dải chữ ở đáy. Nên phải !important để thắng inline
             // style, và translate(-50%,-50%) để cắt đều hai đầu thay vì cắt mất đáy.
             + ".nc-post-body .plyr--nc-fill .plyr__video-wrapper{aspect-ratio:16/9;height:auto;padding-bottom:0;overflow:hidden}"
             + ".nc-post-body .plyr--nc-fill .plyr__video-wrapper video{position:absolute!important;"
             + "top:50%!important;left:50%!important;transform:translate(-50%,-50%)!important;"
             + "width:100%!important;height:auto!important;min-width:100%!important;min-height:100%!important;"
             + "object-fit:cover!important}"
             // Khung 16:9 tạm lúc chưa biết tỉ lệ thật: <video> chưa có metadata chỉ cao 150px nên
             // khung sập thành dải dẹt (đúng cái hộp đen người dùng thấy). Giữ 16:9 rồi nhả ra.
             + ".nc-post-body .plyr--nc-pending .plyr__video-wrapper{aspect-ratio:16/9;height:auto;overflow:hidden}"
             + ".nc-post-body .plyr--nc-pending .plyr__video-wrapper video{position:absolute!important;"
             + "top:50%!important;left:50%!important;transform:translate(-50%,-50%)!important;"
             + "width:100%!important;height:100%!important;object-fit:contain!important}"
             + "</style>"
             + $"<link rel=\"stylesheet\" href=\"/lib/plyr/plyr.css?v={Version}\">"
             + $"<script src=\"/lib/plyr/plyr.polyfilled.min.js?v={Version}\"></script>"
             + "<script>(function(){"
             + $"var SPRITE='/lib/plyr/plyr.svg?v={Version}';"
             + "var ICON_FIT='<svg role=\"presentation\" viewBox=\"0 0 18 18\"><rect x=\"1\" y=\"2\" width=\"16\" height=\"14\" rx=\"2\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"/><rect x=\"2.8\" y=\"6\" width=\"12.4\" height=\"6\"/></svg>';"
             + "var ICON_FILL='<svg role=\"presentation\" viewBox=\"0 0 18 18\"><rect x=\"1\" y=\"2\" width=\"16\" height=\"14\" rx=\"2\"/></svg>';"
             + @"var IMG=/\.(jpe?g|png|webp|gif|avif)(\?|#|$)/i;"
             // TinyMCE từng lưu poster trỏ vào chính file .mp4. Trình duyệt tải MP4 như ảnh thì
             // hỏng, mà vì thuộc tính poster có giá trị nên nó cũng KHÔNG vẽ frame đầu — video
             // thành hộp đen dẹt 300x150. Bỏ poster sai rồi gắn #t=0.1 để browser tua tới
             // 0.1s và vẽ frame đó làm ảnh đại diện. Sửa được cả bài đã lưu, không cần đụng DB.
             + "function firstFrame(el){"
             + "var p=el.getAttribute('poster');"
             + "if(p&&!IMG.test(p)){el.removeAttribute('poster');p=null;}"
             + "if(p)return;"
             + "el.setAttribute('preload','metadata');"
             + "var src=el.querySelector('source')||el;"
             + "var u=src.getAttribute('src');"
             + "if(u&&u.indexOf('#')<0){src.setAttribute('src',u+'#t=0.1');try{el.load();}catch(e){}}"
             + "}"
             // Nút đổi tỉ lệ: Vừa khung (tỉ lệ thật, không cắt) <-> Lấp đầy (khung 16:9 + cover).
             + "function setMode(player,fill){"
             + "var box=player.elements.container;"
             + "if(!box)return;"
             // Lấp đầy thì bỏ khung chờ — hai class tranh nhau cùng set aspect-ratio.
             + "box.classList.toggle('plyr--nc-fill',fill);"
             + "if(fill)box.classList.remove('plyr--nc-pending');"
             + "var b=box.querySelector('.nc-ratio-toggle');"
             + "if(b){b.innerHTML=(fill?ICON_FILL:ICON_FIT)+'<span class=\"plyr__tooltip\">'+(fill?'Lấp đầy 16:9':'Vừa khung')+'</span>';"
             + "b.setAttribute('aria-label',fill?'Đang lấp đầy 16:9 — bấm để vừa khung':'Đang vừa khung — bấm để lấp đầy 16:9');}"
             + "}"
             + "function addRatioButton(player){"
             + "var bar=player.elements.controls;"
             + "if(!bar||bar.querySelector('.nc-ratio-toggle'))return;"
             + "var b=document.createElement('button');"
             + "b.type='button';b.className='plyr__controls__item plyr__control nc-ratio-toggle';"
             + "b.addEventListener('click',function(){setMode(player,!player.elements.container.classList.contains('plyr--nc-fill'));});"
             + "var fs=bar.querySelector('[data-plyr=\"fullscreen\"]');"
             + "if(fs)bar.insertBefore(b,fs);else bar.appendChild(b);"
             + "setMode(player,false);"
             + "}"
             // KHÔNG truyền ratio (kể cả ratio: 0). Plyr coi 0 là giá trị tỉ lệ; khi metadata
             // video chưa load (videoWidth=videoHeight=0) nó tính ra [NaN,NaN], hàm de() của Plyr
             // loại NaN nên trả null, rồi pe() destructure null → THROW. Control bar không được
             // dựng nên TOÀN BỘ icon mất — đúng triệu chứng "Ctrl+F5 thì được, F5 lại mất icon"
             // (F5 rơi đúng nhánh metadata chưa sẵn). Nên khung 16:9 đặt hoàn toàn bằng CSS của
             // mình (class --nc-fill/--nc-pending ở trên), không đi qua option ratio.
             + "function initPlayer(el){"
             + "var video=el.tagName==='VIDEO';"
             + "if(video)firstFrame(el);"
             + "var player;"
             + "try{player=new Plyr(el,{iconUrl:SPRITE,captions:{active:true,language:'vi',update:true},"
             + "controls:['play-large','play','progress','current-time','mute','volume','captions','settings','fullscreen']});}catch(e){return;}"
             + "if(!video)return;"
             + "addRatioButton(player);"
             // Chưa có metadata thì giữ khung 16:9 tạm (xem CSS --nc-pending) cho khỏi giật
             // layout; có metadata rồi nhả về đúng tỉ lệ thật của video.
             + "var box=player.elements.container;"
             + "function release(){box.classList.remove('plyr--nc-pending');}"
             + "if(el.readyState<1){box.classList.add('plyr--nc-pending');el.addEventListener('loadedmetadata',release);}"
             + "}"
             + "function boot(){"
             + "document.querySelectorAll('.nc-post-body video,.nc-post-body audio').forEach(function(el){"
             + "if(el.closest('.plyr'))return;"
             + "initPlayer(el);"
             + "});"
             + "}"
             + "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',boot);}else{boot();}"
             + "})();</script>";
    }
}
