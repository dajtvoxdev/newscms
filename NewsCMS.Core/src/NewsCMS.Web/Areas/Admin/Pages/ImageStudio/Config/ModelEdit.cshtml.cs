using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsCMS.Application.Common;
using NewsCMS.Application.ImageStudio;
using NewsCMS.Domain.Entities.ImageStudio;

namespace NewsCMS.Web.Areas.Admin.Pages.ImageStudio.Config;

/// <summary>Thêm hoặc sửa một model tạo ảnh (không có id = thêm mới).</summary>
[Authorize(Roles = "SuperAdmin")]
public class ModelEditModel : PageModel
{
    private readonly IImageModelService _models;

    public ModelEditModel(IImageModelService models) => _models = models;

    public IReadOnlyList<ImageConnectionOptionDto> Connections { get; private set; } = Array.Empty<ImageConnectionOptionDto>();

    public IReadOnlyList<ImageProviderAdapter> Adapters { get; private set; } = Array.Empty<ImageProviderAdapter>();

    public bool IsNew => Input.Id is null;

    [BindProperty]
    public ModelForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (id is { } modelId)
        {
            ImageModelDto? model = await _models.GetByIdAsync(modelId);

            if (model is null)
            {
                return NotFound();
            }

            Input = ModelForm.From(model);
        }

        await LoadAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        ImageModelUpsertDto dto = Input.ToDto();
        Result result;

        if (IsNew)
        {
            Result<Guid> created = await _models.CreateAsync(dto);
            result = created;
        }
        else
        {
            result = await _models.UpdateAsync(dto);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadAsync();
            return Page();
        }

        TempData["Success"] = IsNew ? "Đã thêm model. Bấm \"Chạy thử\" để kiểm tra key và model id." : "Đã lưu model.";

        return RedirectToPage("/ImageStudio/Config/Models");
    }

    private async Task LoadAsync()
    {
        Connections = await _models.GetConnectionsAsync();
        Adapters = _models.GetAvailableAdapters();
    }

    public static string AdapterLabel(ImageProviderAdapter adapter) => adapter switch
    {
        ImageProviderAdapter.OpenAiImages => "Chuẩn OpenAI (/images/generations) — OpenAI, 9Router, gateway tương thích",
        ImageProviderAdapter.Gemini => "Google Gemini",
        ImageProviderAdapter.FalQueue => "fal.ai",
        ImageProviderAdapter.Fake => "Provider giả — chỉ để phát triển, không gọi mạng",
        _ => adapter.ToString(),
    };

    public sealed class ModelForm
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Nhập tên hiển thị.")]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public ImageProviderAdapter Adapter { get; set; } = ImageProviderAdapter.OpenAiImages;

        public Guid ConnectionId { get; set; }

        [Required(ErrorMessage = "Nhập model id.")]
        [StringLength(200)]
        public string ModelId { get; set; } = string.Empty;

        public bool CapTextToImage { get; set; } = true;

        public bool CapReferenceImages { get; set; }

        public bool CapMaskEdit { get; set; }

        public bool CapInstructionEdit { get; set; }

        [Range(0, 16)]
        public int MaxReferenceImages { get; set; }

        public MaskConvention MaskConvention { get; set; } = MaskConvention.None;

        [Required(ErrorMessage = "Khai ít nhất một kích thước.")]
        public string SupportedSizes { get; set; } = "1024x1024\n1536x1024\n1024x1536";

        [Range(1, 4)]
        public int MaxVariants { get; set; } = 4;

        [StringLength(20)]
        public string? Quality { get; set; }

        public string OutputFormat { get; set; } = "png";

        [Range(0, 100)]
        public decimal PricePerImageUsd { get; set; } = 0.04m;

        [Range(30, 900)]
        public int TimeoutSeconds { get; set; } = 180;

        public string? ExtraParamsJson { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsDefault { get; set; }

        public int SortOrder { get; set; }

        public static ModelForm From(ImageModelDto m) => new()
        {
            Id = m.Id,
            Name = m.Name,
            Description = m.Description,
            Adapter = m.Adapter,
            ConnectionId = m.ConnectionId,
            ModelId = m.ModelId,
            CapTextToImage = m.Capabilities.HasFlag(ImageCapabilities.TextToImage),
            CapReferenceImages = m.Capabilities.HasFlag(ImageCapabilities.ReferenceImages),
            CapMaskEdit = m.Capabilities.HasFlag(ImageCapabilities.MaskEdit),
            CapInstructionEdit = m.Capabilities.HasFlag(ImageCapabilities.InstructionEdit),
            MaxReferenceImages = m.MaxReferenceImages,
            MaskConvention = m.MaskConvention,
            SupportedSizes = m.SupportedSizes,
            MaxVariants = m.MaxVariants,
            Quality = m.Quality,
            OutputFormat = m.OutputFormat,
            PricePerImageUsd = m.PricePerImageUsd,
            TimeoutSeconds = m.TimeoutSeconds,
            ExtraParamsJson = m.ExtraParamsJson,
            IsActive = m.IsActive,
            IsDefault = m.IsDefault,
            SortOrder = m.SortOrder,
        };

        public ImageModelUpsertDto ToDto()
        {
            ImageCapabilities caps = ImageCapabilities.None;
            if (CapTextToImage) caps |= ImageCapabilities.TextToImage;
            if (CapReferenceImages) caps |= ImageCapabilities.ReferenceImages;
            if (CapMaskEdit) caps |= ImageCapabilities.MaskEdit;
            if (CapInstructionEdit) caps |= ImageCapabilities.InstructionEdit;

            return new ImageModelUpsertDto(
                Id, Name, Description, ConnectionId, Adapter, ModelId, caps, MaxReferenceImages, MaskConvention,
                SupportedSizes, MaxVariants, Quality, OutputFormat, PricePerImageUsd, TimeoutSeconds, ExtraParamsJson,
                IsActive, IsDefault, SortOrder);
        }
    }
}
