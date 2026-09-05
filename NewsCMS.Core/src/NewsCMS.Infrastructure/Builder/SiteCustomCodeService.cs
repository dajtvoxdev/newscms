using NewsCMS.Application.Builder;
using NewsCMS.Application.Common;
using NewsCMS.Application.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Custom CSS/JS/HTML ở tầng site — lưu qua ISiteSettingService (group "builder.code").
/// Khoá sau quyền Builder.Code.Manage ở controller. HeadHtml/BodyEndHtml cho phép chèn snippet
/// nhỏ (analytics, pixel...) mà không cần sửa layout.
/// </summary>
public sealed class SiteCustomCodeService : ISiteCustomCodeService
{
    private const string Group = "builder.code";
    private const string KeyCss = "custom.css";
    private const string KeyJs = "custom.js";
    private const string KeyHead = "head.html";
    private const string KeyBodyEnd = "body.end.html";

    private readonly ISiteSettingService _settings;

    public SiteCustomCodeService(ISiteSettingService settings) => _settings = settings;

    public async Task<SiteCustomCodeDto> GetAsync(CancellationToken ct = default)
    {
        var group = await _settings.GetGroupAsync(Group, ct);
        return new SiteCustomCodeDto(
            CustomCss: group.GetValueOrDefault(KeyCss),
            CustomJs: group.GetValueOrDefault(KeyJs),
            HeadHtml: group.GetValueOrDefault(KeyHead),
            BodyEndHtml: group.GetValueOrDefault(KeyBodyEnd));
    }

    public async Task<Result<SiteCustomCodeDto>> SaveAsync(SiteCustomCodeSaveRequest request, CancellationToken ct = default)
    {
        await _settings.SetAsync(KeyCss, request.CustomCss ?? string.Empty, Group, ct);
        await _settings.SetAsync(KeyJs, request.CustomJs ?? string.Empty, Group, ct);
        await _settings.SetAsync(KeyHead, request.HeadHtml ?? string.Empty, Group, ct);
        await _settings.SetAsync(KeyBodyEnd, request.BodyEndHtml ?? string.Empty, Group, ct);

        return Result<SiteCustomCodeDto>.Success(await GetAsync(ct));
    }
}
