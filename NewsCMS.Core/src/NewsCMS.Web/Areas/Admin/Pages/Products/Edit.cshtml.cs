using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using NewsCMS.Application.Catalog;
using NewsCMS.Application.Catalog.Dtos;
using NewsCMS.Application.Content;
using NewsCMS.Domain.Enums;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Web.Areas.Admin.Pages.Products;

[Authorize(Permissions.Catalog.EditProduct)]
public class EditModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IProductService _svc;
    private readonly IProductCategoryService _categories;
    private readonly IMediaService _media;

    public EditModel(IProductService svc, IProductCategoryService categories, IMediaService media)
    {
        _svc = svc;
        _categories = categories;
        _media = media;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public CreateModel.InputModel Input { get; set; } = new();
    public List<SelectListItem> Categories { get; private set; } = new();
    /// <summary>Dữ liệu khởi tạo cho repeater ảnh/biến thể phía client (data-initial).</summary>
    public string ImagesJson { get; private set; } = "[]";
    public string VariantsJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var item = await _svc.GetByIdAsync(Id, ct);
        if (item is null) return NotFound();

        Input = new CreateModel.InputModel
        {
            Name = item.Name,
            Slug = item.Slug,
            Sku = item.Sku,
            ShortDescription = item.ShortDescription,
            Description = item.Description,
            Price = item.Price,
            SalePrice = item.SalePrice,
            StockQuantity = item.StockQuantity,
            IsTrackingStock = item.IsTrackingStock,
            Status = item.Status,
            IsFeatured = item.IsFeatured,
            SortOrder = item.SortOrder,
            ProductCategoryId = item.ProductCategoryId,
            ThumbnailMediaId = item.ThumbnailMediaId,
            ThumbnailUrl = item.ThumbnailMediaId is Guid thumbId
                ? (await _media.GetByIdAsync(thumbId, ct))?.FilePath
                : null
        };

        ImagesJson = JsonSerializer.Serialize(
            item.Images.OrderBy(i => i.SortOrder).Select(i => new { url = i.Url, altText = i.AltText }), JsonOpts);
        VariantsJson = JsonSerializer.Serialize(
            item.Variants.OrderBy(v => v.SortOrder).Select(v => new
            {
                sku = v.Sku, name = v.Name, price = v.Price,
                salePrice = v.SalePrice, stockQuantity = v.StockQuantity, isActive = v.IsActive
            }), JsonOpts);

        await LoadCategoriesAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadCategoriesAsync(ct);
        // Giữ lại ảnh/biến thể vừa nhập để repeater dựng lại khi form quay về vì lỗi validate.
        ImagesJson = Input.ImagesJson ?? "[]";
        VariantsJson = Input.VariantsJson ?? "[]";
        if (!ModelState.IsValid) return Page();

        var images = CreateModel.ParseImagesPublic(Input.ImagesJson);
        var variants = CreateModel.ParseVariantsPublic(Input.VariantsJson);
        var thumbnailId = await CreateModel.ResolveThumbnailAsync(_media, Input.ThumbnailUrl, Input.ThumbnailMediaId, ct);

        var dto = new ProductUpsertDto(
            Id, Input.Name, Input.Slug, Input.Sku,
            Input.ShortDescription, Input.Description,
            Input.Price, Input.SalePrice, Input.StockQuantity, Input.IsTrackingStock,
            Input.Status, Input.Status == ProductStatus.Published ? DateTime.UtcNow : null,
            Input.IsFeatured, Input.SortOrder, Input.ProductCategoryId, thumbnailId,
            images, variants);

        var result = await _svc.UpdateAsync(dto, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Lỗi không xác định.");
            return Page();
        }
        TempData["Success"] = "Đã cập nhật sản phẩm.";
        // Quay lại chính trang sửa: admin thường lưu nhiều lần liên tiếp, bật về danh sách
        // sẽ mất ngữ cảnh và không thấy được kết quả vừa lưu.
        return RedirectToPage("/Products/Edit", new { id = Id });
    }

    [Authorize(Permissions.Catalog.DeleteProduct)]
    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var result = await _svc.DeleteAsync(Id, ct);
        if (!result.Succeeded) TempData["Error"] = result.Error;
        else TempData["Success"] = "Đã xoá sản phẩm.";
        return RedirectToPage("/Products/Index");
    }

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        var cats = await _categories.GetAllAsync(ct);
        Categories = cats.Where(c => c.IsActive)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
