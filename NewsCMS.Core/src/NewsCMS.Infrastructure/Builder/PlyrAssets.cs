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

        return "<style>.nc-post-body video,.nc-post-body audio{width:100%;height:auto;max-width:100%;border-radius:12px}</style>"
             + $"<link rel=\"stylesheet\" href=\"/lib/plyr/plyr.css?v={Version}\">"
             + $"<script src=\"/lib/plyr/plyr.polyfilled.min.js?v={Version}\"></script>"
             + "<script>(function(){"
             + $"var SPRITE='/lib/plyr/plyr.svg?v={Version}';"
             // KHÔNG truyền ratio (kể cả ratio: 0). Plyr coi 0 là giá trị tỉ lệ; khi metadata
             // video chưa load (videoWidth=videoHeight=0) nó tính ra [NaN,NaN], hàm de() của Plyr
             // loại NaN nên trả null, rồi pe() destructure null → THROW. Control bar không được
             // dựng nên TOÀN BỘ icon mất — đúng triệu chứng "Ctrl+F5 thì được, F5 lại mất icon"
             // (F5 rơi đúng nhánh metadata chưa sẵn). Bỏ ratio để Plyr tự xử lý.
             + "function boot(){"
             + "document.querySelectorAll('.nc-post-body video,.nc-post-body audio').forEach(function(el){"
             + "if(el.closest('.plyr'))return;"
             + "try{new Plyr(el,{iconUrl:SPRITE,captions:{active:true,language:'vi',update:true},"
             + "controls:['play-large','play','progress','current-time','mute','volume','captions','settings','fullscreen']});}catch(e){}"
             + "});"
             + "}"
             + "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',boot);}else{boot();}"
             + "})();</script>";
    }
}
