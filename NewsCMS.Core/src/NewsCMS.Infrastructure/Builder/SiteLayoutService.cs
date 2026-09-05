using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Builder;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// CRUD layout builder. HTML sanitize bằng ContentSanitizer.SanitizeBuilder trước khi lưu
/// CompiledHtml. CustomJs giữ nguyên (đi đường riêng, không qua sanitizer). Khi đặt IsDefault=true
/// cho một shell, các shell khác tự động bị bỏ default.
/// </summary>
public sealed class SiteLayoutService : ISiteLayoutService
{
    private readonly AppDbContext _db;
    private readonly ContentSanitizer _sanitizer;

    public SiteLayoutService(AppDbContext db, ContentSanitizer sanitizer)
    {
        _db = db;
        _sanitizer = sanitizer;
    }

    public async Task<Result<SiteLayoutDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var layout = await _db.SiteLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layout is null) return Result<SiteLayoutDto>.Failure("Layout không tồn tại.");
        return Result<SiteLayoutDto>.Success(Map(layout));
    }

    public async Task<IReadOnlyList<SiteLayoutDto>> ListAsync(CancellationToken ct = default)
    {
        // CompiledHtml để null: danh sách chỉ cần metadata, kéo cả cây HTML của mọi shell về
        // cho một cái bảng là lãng phí.
        return await _db.SiteLayouts.AsNoTracking()
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Name)
            .Select(l => new SiteLayoutDto(
                l.Id, l.Key, l.Name, l.Kind.ToString(),
                l.BuilderJson, null, l.CompiledCss, l.CustomCss, l.CustomJs, l.IsDefault))
            .ToListAsync(ct);
    }

    public async Task<Result<SiteLayoutDto>> CreateAsync(SiteLayoutSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return Result<SiteLayoutDto>.Failure("Key không được để trống.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<SiteLayoutDto>.Failure("Tên không được để trống.");

        var keyExists = await _db.SiteLayouts.AnyAsync(l => l.Key == request.Key, ct);
        if (keyExists)
            return Result<SiteLayoutDto>.Failure($"Key '{request.Key}' đã tồn tại.");

        var kind = ParseKind(request.Kind);

        // Shell mới mà không kèm HTML thì seed khung mặc định: shell rỗng vẫn render được (renderer
        // có nhánh fallback) nhưng người dùng mở builder ra thấy canvas trắng, không biết phải đặt
        // vùng nội dung ở đâu. Seed đảm bảo mọi shell sinh ra đều có sẵn data-nc-body.
        var html = string.IsNullOrWhiteSpace(request.CompiledHtml) && kind == LayoutKind.Shell
            ? DefaultShellHtml
            : _sanitizer.SanitizeBuilder(request.CompiledHtml);

        if (MarkerError(kind, html) is { } markerError)
            return Result<SiteLayoutDto>.Failure(markerError);

        // Nếu đặt làm mặc định → bỏ default của các shell khác cùng loại.
        if (request.IsDefault)
            await ClearDefaultAsync(kind, ct);

        var layout = new SiteLayout
        {
            Key = request.Key.Trim(),
            Name = request.Name.Trim(),
            Kind = kind,
            BuilderJson = request.BuilderJson,
            CompiledHtml = html,
            CompiledCss = request.CompiledCss,
            CustomCss = request.CustomCss,
            CustomJs = request.CustomJs,
            IsDefault = request.IsDefault
        };

        _db.SiteLayouts.Add(layout);
        await _db.SaveChangesAsync(ct);
        return Result<SiteLayoutDto>.Success(Map(layout));
    }

    public async Task<Result<SiteLayoutDto>> UpdateAsync(Guid id, SiteLayoutSaveRequest request, CancellationToken ct = default)
    {
        var layout = await _db.SiteLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layout is null) return Result<SiteLayoutDto>.Failure("Layout không tồn tại.");

        if (string.IsNullOrWhiteSpace(request.Key))
            return Result<SiteLayoutDto>.Failure("Key không được để trống.");

        // Kiểm tra key trùng (trừ chính layout đang sửa).
        if (!string.Equals(layout.Key, request.Key, StringComparison.OrdinalIgnoreCase))
        {
            var keyExists = await _db.SiteLayouts.AnyAsync(l => l.Key == request.Key && l.Id != id, ct);
            if (keyExists)
                return Result<SiteLayoutDto>.Failure($"Key '{request.Key}' đã tồn tại.");
        }

        var kind = ParseKind(request.Kind);

        // CompiledHtml null = "không đổi" — form code-editor gửi null để không xoá nhầm HTML
        // do builder shell dựng (nó không gửi lại HTML).
        var html = request.CompiledHtml is null
            ? layout.CompiledHtml
            : _sanitizer.SanitizeBuilder(request.CompiledHtml);

        if (MarkerError(kind, html) is { } markerError)
            return Result<SiteLayoutDto>.Failure(markerError);

        if (request.IsDefault && !layout.IsDefault)
            await ClearDefaultAsync(kind, ct);

        layout.Key = request.Key.Trim();
        layout.Name = request.Name.Trim();
        layout.Kind = kind;
        // BuilderJson theo quy ước null = giữ nguyên. Form code-editor sửa CompiledHtml bằng tay và
        // không có cây component trong tay; gán thẳng sẽ xoá sạch thành quả của shell builder. Gửi
        // chuỗi rỗng để CHỦ ĐỘNG xoá — dùng khi HTML tay đã lệch khỏi cây, để lần mở builder sau
        // parse lại từ CompiledHtml thay vì dựng lại cây cũ đã lạc hậu.
        if (request.BuilderJson is not null)
            layout.BuilderJson = string.IsNullOrWhiteSpace(request.BuilderJson) ? null : request.BuilderJson;
        layout.CompiledHtml = html;
        // CompiledCss cũng theo quy ước null = giữ nguyên: nó là sản phẩm compile của shell
        // builder, form code-editor không có nó trong tay để gửi lại.
        if (request.CompiledCss is not null)
            layout.CompiledCss = request.CompiledCss;
        layout.CustomCss = request.CustomCss;
        // CustomJs cũng null = giữ nguyên: người không có Builder.Code.Manage không thấy ô JS nên
        // form của họ không gửi field này — gán thẳng sẽ xoá JS của người có quyền. Chuỗi rỗng xoá.
        if (request.CustomJs is not null)
            layout.CustomJs = request.CustomJs;
        layout.IsDefault = request.IsDefault;
        layout.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<SiteLayoutDto>.Success(Map(layout));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var layout = await _db.SiteLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layout is null) return Result.Failure("Layout không tồn tại.");

        layout.IsDeleted = true;
        layout.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Bỏ IsDefault của tất cả layout cùng Kind trong site hiện tại.</summary>
    private async Task ClearDefaultAsync(LayoutKind kind, CancellationToken ct)
    {
        var currentDefaults = await _db.SiteLayouts
            .Where(l => l.Kind == kind && l.IsDefault)
            .ToListAsync(ct);

        foreach (var d in currentDefaults)
        {
            d.IsDefault = false;
            d.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static SiteLayoutDto Map(SiteLayout l) => new(
        l.Id, l.Key, l.Name, l.Kind.ToString(),
        l.BuilderJson, l.CompiledHtml, l.CompiledCss, l.CustomCss, l.CustomJs, l.IsDefault);

    private static LayoutKind ParseKind(string? kind) =>
        Enum.TryParse<LayoutKind>(kind, ignoreCase: true, out var k) ? k : LayoutKind.Shell;

    /// <summary>Phần tử trong shell nơi nội dung page được thế vào — khớp <c>PageRenderer</c>.</summary>
    private const string BodyMarker = "data-nc-body";

    /// <summary>
    /// Shell PHẢI có <c>data-nc-body</c>. Thiếu marker thì renderer rơi về nhánh "nối nội dung
    /// trước &lt;/body&gt;": trang vẫn ra HTML, chỉ là header/footer không bọc quanh nội dung nữa —
    /// hỏng âm thầm, đúng loại lỗi khó truy. Chặn ngay lúc lưu, trả lỗi thay vì cảnh báo.
    /// Trả null nếu hợp lệ.
    /// </summary>
    private static string? MarkerError(LayoutKind kind, string? html) =>
        kind == LayoutKind.Shell
        && !string.IsNullOrWhiteSpace(html)
        && !html.Contains(BodyMarker, StringComparison.OrdinalIgnoreCase)
            ? "Shell phải có đúng một vùng nội dung (data-nc-body). Kéo khối \"Vùng nội dung trang\" "
              + "vào shell rồi lưu lại."
            : null;

    /// <summary>
    /// Khung shell tối thiểu cho layout mới: header + vùng nội dung + footer, dùng design token nên
    /// đổi màu ở admin là shell đổi theo. Cố ý mộc — người dùng sẽ dựng tiếp trong shell builder.
    /// </summary>
    private const string DefaultShellHtml =
        "<header style=\"background:var(--color-surface,#fff);border-bottom:1px solid var(--color-subtle,#e2e8f0)\">"
        + "<div style=\"max-width:1200px;margin:0 auto;padding:16px 24px;display:flex;align-items:center;justify-content:space-between;gap:24px\">"
        + "<a href=\"/\" style=\"font-family:var(--font-display);font-weight:700;font-size:1.25rem;color:var(--color-brand-500);text-decoration:none\">Logo</a>"
        + "<div data-nc-block=\"site-menu\" data-nc-props='{\"location\":\"header\"}'></div>"
        + "</div></header>"
        + "<main data-nc-body></main>"
        + "<footer style=\"background:var(--color-ink,#0f172a);color:var(--color-surface,#fff);padding:48px 24px\">"
        + "<div style=\"max-width:1200px;margin:0 auto;display:flex;flex-wrap:wrap;gap:32px;justify-content:space-between\">"
        + "<div data-nc-block=\"site-menu\" data-nc-props='{\"location\":\"footer\",\"direction\":\"vertical\",\"gap\":8}'></div>"
        + "</div></footer>";
}
