using Microsoft.AspNetCore.Mvc.Rendering;

namespace NewsCMS.Web.Areas.Admin.Pages.Products;

/// <summary>
/// Model cho partial _ProductForm — dùng chung giữa trang Tạo và trang Sửa sản phẩm.
/// Ảnh/biến thể được truyền xuống dạng JSON để repeater phía client dựng lại danh sách.
/// </summary>
public class ProductFormViewModel
{
    public required CreateModel.InputModel Input { get; init; }
    public required List<SelectListItem> Categories { get; init; }

    /// <summary>Nhãn nút submit, ví dụ "Tạo sản phẩm" hoặc "Lưu thay đổi".</summary>
    public required string SubmitLabel { get; init; }

    /// <summary>JSON [{ url, altText }] để dựng lại repeater ảnh.</summary>
    public string ImagesJson { get; init; } = "[]";

    /// <summary>JSON [{ sku, name, price, salePrice, stockQuantity, isActive }] cho repeater biến thể.</summary>
    public string VariantsJson { get; init; } = "[]";
}
