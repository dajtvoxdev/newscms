using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Application.Builder;
using NewsCMS.Application.Site;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Builder;

/// <summary>
/// Quản lý API key cho agent/MCP. Admin site chỉ thấy và tạo được key của site mình;
/// SuperAdmin thấy tất cả. Key được lưu mã hoá nên xem lại được bất cứ lúc nào qua
/// nút "Xem key" (fetch handler RevealKey); panel xanh sau khi tạo chỉ là tiện tiện tay.
/// Danh sách mặc định lọc theo trạng thái "active" (còn lưu hành); key hết hạn/thu hồi
/// chỉ hiện khi chọn filter tương ứng — xem <see cref="StatusFilter"/>.
/// </summary>
[Authorize(Permissions.Agent.ApiKeyManage)]
public class ApiKeysModel : PageModel
{
    private readonly ISiteApiKeyService _service;
    private readonly ICurrentSite _currentSite;
    private readonly AppDbContext _db;

    public ApiKeysModel(ISiteApiKeyService service, ICurrentSite currentSite, AppDbContext db)
    {
        _service = service;
        _currentSite = currentSite;
        _db = db;
    }

    public IReadOnlyList<SiteApiKeyDto> Items { get; private set; } = Array.Empty<SiteApiKeyDto>();

    /// <summary>Tên site theo Id để hiển thị cho SuperAdmin (key root có SiteId = null).</summary>
    public IReadOnlyDictionary<Guid, string> SiteNames { get; private set; } = new Dictionary<Guid, string>();

    public bool IsSuperAdmin => User.IsInRole("SuperAdmin");

    public IReadOnlyList<string> AvailableScopes => ApiKeyScopes.All;

    /// <summary>Bộ lọc trạng thái (query ?status=) — mặc định chỉ hiện key còn lưu hành.
    /// Setter phải public để BindProperty(SupportsGet) ghi được giá trị từ query string.</summary>
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? StatusFilter { get; set; }

    /// <summary>Key thô vừa tạo — set trên response tạo key và render ngay, không qua redirect.</summary>
    public string? CreatedPlainKey { get; private set; }

    public string? CreatedKeyName { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public int ExpiresInDays { get; set; } = 90;
        public int RateLimitPerMinute { get; set; } = 60;
    }

    public async Task OnGetAsync(CancellationToken ct = default)
    {
        // Giá trị lạ trên query string rơi về mặc định "active" thay vì lỗi.
        if (!ApiKeyStatusFilters.IsValid(StatusFilter)) StatusFilter = null;
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        // Không dùng RedirectToPage cho nhánh tạo key: key thô chỉ tồn tại đúng trong response
        // này, mà TempData đi qua cookie có thể bị trình duyệt/extensions/bfcache làm rơi —
        // mất cookie là mất luôn key vì DB chỉ giữ hash. Render lại chính trang.
        await LoadAsync(ct);

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            ModelState.AddModelError("Input.Name", "Tên key không được để trống.");
            return Page();
        }

        if (Input.Scopes.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Chọn ít nhất một scope.");
            return Page();
        }

        // Admin site chỉ tạo được key gắn site của mình — không cho tạo key phạm vi root.
        var request = new SiteApiKeyCreateRequest(
            SiteId: _currentSite.SiteId,
            Name: Input.Name,
            Scopes: string.Join(",", Input.Scopes),
            ExpiresAt: Input.ExpiresInDays > 0 ? DateTime.UtcNow.AddDays(Input.ExpiresInDays) : null,
            RateLimitPerMinute: Input.RateLimitPerMinute);

        var result = await _service.CreateAsync(request, ct);

        if (!result.Succeeded || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Không tạo được key.");
            return Page();
        }

        CreatedPlainKey = result.Value.PlainKey;
        CreatedKeyName = result.Value.Key.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
    {
        // Chặn admin site thu hồi key của site khác: kiểm quyền sở hữu trước khi gọi service
        // (service chạy BypassSiteScope nên tự nó không lọc theo site).
        var owned = await _service.ListAsync(IsSuperAdmin ? null : _currentSite.SiteId, ApiKeyStatusFilters.All, ct);
        if (owned.All(k => k.Id != id))
        {
            TempData["Error"] = "Key không thuộc site này.";
            return RedirectToPage();
        }

        var result = await _service.RevokeAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã thu hồi key." : result.Error;
        return RedirectToPage();
    }

    /// <summary>
    /// Trả về key thô cho nút "Xem key" (fetch). Kiểm antiforgery token tay (GET không tự
    /// validate) — cùng header RequestVerificationToken như các trang Builder khác.
    /// </summary>
    public async Task<IActionResult> OnGetRevealKeyAsync(Guid id, CancellationToken ct)
    {
        var antiforgery = HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return new JsonResult(new { key = (string?)null, error = "Phiên hết hạn — tải lại trang." })
                { StatusCode = StatusCodes.Status400BadRequest };
        }

        // Chặn admin site xem key của site khác — cùng cơ chế với handler Revoke.
        var owned = await _service.ListAsync(IsSuperAdmin ? null : _currentSite.SiteId, ApiKeyStatusFilters.All, ct);
        if (owned.All(k => k.Id != id))
        {
            return new JsonResult(new { key = (string?)null, error = "Key không thuộc site này." });
        }

        var plain = await _service.RevealAsync(id, ct);
        return new JsonResult(new { key = plain, error = plain is null ? "Không giải mã được key (key ring đã đổi?)." : (string?)null });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Items = await _service.ListAsync(IsSuperAdmin ? null : _currentSite.SiteId, StatusFilter ?? ApiKeyStatusFilters.Active, ct);

        if (IsSuperAdmin)
        {
            var previous = _db.BypassSiteScope;
            _db.BypassSiteScope = true;
            try
            {
                SiteNames = await _db.Sites.AsNoTracking()
                    .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            }
            finally
            {
                _db.BypassSiteScope = previous;
            }
        }
    }
}
