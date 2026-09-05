using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Products;

[Authorize(Permissions.Catalog.CreateProduct)]
public class CreateModel : PageModel
{
    private readonly IProductService _svc;
    private readonly IProductCategoryService _categories;
    private readonly IMediaService _media;

    public CreateModel(IProductService svc, IProductCategoryService categories, IMediaService media)
    {
        _svc = svc;
        _categories = categories;
        _media = media;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Categories { get; private set; } = new();

    /// <summary>Dữ liệu khởi tạo cho repeater ảnh/biến thể — giữ lại khi form quay về vì lỗi validate.</summary>
    public string ImagesJson { get; private set; } = "[]";
    public string VariantsJson { get; private set; } = "[]";

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string? ShortDescription { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public decimal? SalePrice { get; set; }
        public int StockQuantity { get; set; }
        public bool IsTrackingStock { get; set; } = true;
        public ProductStatus Status { get; set; } = ProductStatus.Draft;
        public bool IsFeatured { get; set; }
        public int SortOrder { get; set; }
        public Guid ProductCategoryId { get; set; }
        public Guid? ThumbnailMediaId { get; set; }

        /// <summary>URL ảnh đại diện do media picker bind; POST xong mới map ngược về media id.</summary>
        public string? ThumbnailUrl { get; set; }

        public string? ImagesJson { get; set; }
        public string? VariantsJson { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadCategoriesAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadCategoriesAsync(ct);
        ImagesJson = Input.ImagesJson ?? "[]";
        VariantsJson = Input.VariantsJson ?? "[]";
        if (!ModelState.IsValid) return Page();

        var images = ParseImages(Input.ImagesJson);
        var variants = ParseVariants(Input.VariantsJson);
        var thumbnailId = await ResolveThumbnailAsync(_media, Input.ThumbnailUrl, Input.ThumbnailMediaId, ct);

        var dto = new ProductUpsertDto(
            null, Input.Name, Input.Slug, Input.Sku,
            Input.ShortDescription, Input.Description,
            Input.Price, Input.SalePrice, Input.StockQuantity, Input.IsTrackingStock,
            Input.Status, Input.Status == ProductStatus.Published ? DateTime.UtcNow : null,
            Input.IsFeatured, Input.SortOrder, Input.ProductCategoryId, thumbnailId,
            images, variants);

        var result = await _svc.CreateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Lỗi không xác định.");
            return Page();
        }
        TempData["Success"] = $"Đã tạo sản phẩm \"{Input.Name}\".";
        return RedirectToPage("/Products/Index");
    }

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        var cats = await _categories.GetAllAsync(ct);
        Categories = cats.Where(c => c.IsActive)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }

    /// <summary>
    /// Media picker chỉ bind URL nên phải map ngược về media id. URL rỗng = bỏ ảnh đại diện;
    /// URL không tìm thấy trong thư viện thì giữ nguyên id cũ thay vì xoá mất ảnh đang dùng.
    /// </summary>
    public static async Task<Guid?> ResolveThumbnailAsync(
        IMediaService media, string? url, Guid? currentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var found = await media.GetByPathAsync(url, ct);
        return found?.Id ?? currentId;
    }

    public static IReadOnlyList<ProductImageUpsertDto> ParseImagesPublic(string? json) => ParseImages(json);
    public static IReadOnlyList<ProductVariantUpsertDto> ParseVariantsPublic(string? json) => ParseVariants(json);

    private static IReadOnlyList<ProductImageUpsertDto> ParseImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ProductImageUpsertDto>();
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<ImageRow>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items?.Select((r, i) => new ProductImageUpsertDto(null, r.Url, r.AltText, r.SortOrder == 0 ? i : r.SortOrder)).ToList()
                ?? new List<ProductImageUpsertDto>();
        }
        catch { return Array.Empty<ProductImageUpsertDto>(); }
    }

    private static IReadOnlyList<ProductVariantUpsertDto> ParseVariants(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ProductVariantUpsertDto>();
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<VariantRow>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items?.Select((r, i) => new ProductVariantUpsertDto(
                null, r.Sku ?? "", r.Name ?? "", r.Price, r.SalePrice,
                r.StockQuantity, r.IsActive, r.SortOrder == 0 ? i : r.SortOrder)).ToList()
                ?? new List<ProductVariantUpsertDto>();
        }
        catch { return Array.Empty<ProductVariantUpsertDto>(); }
    }

    public class ImageRow { public string Url { get; set; } = ""; public string? AltText { get; set; } public int SortOrder { get; set; } }
    public class VariantRow { public string? Sku { get; set; } public string? Name { get; set; } public decimal? Price { get; set; } public decimal? SalePrice { get; set; } public int StockQuantity { get; set; } public bool IsActive { get; set; } = true; public int SortOrder { get; set; } }
}
