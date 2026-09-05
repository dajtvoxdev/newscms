using FluentValidation;
using NewsCMS.Application.Engagement.Dtos;

namespace NewsCMS.Application.Engagement.Validators;

public sealed class CommitmentCreateValidator : AbstractValidator<CommitmentCreateDto>
{
    private const long MaxUploadBytes = 2 * 1024 * 1024;
    private const int MaxBase64Length = 4 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = new[] { "image/png", "image/jpeg", "image/jpg" };

    public CommitmentCreateValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Vui lòng nhập tên hiển thị.")
            .Length(2, 80).WithMessage("Tên cần từ 2 đến 80 ký tự.");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Vui lòng nhập nội dung cam kết.")
            .Length(4, 280).WithMessage("Cam kết cần từ 4 đến 280 ký tự.");

        RuleFor(x => x.SignatureKind)
            .NotEmpty()
            .Must(k => string.Equals(k, "Drawn", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(k, "Uploaded", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Loại chữ ký không hợp lệ.");

        When(x => string.Equals(x.SignatureKind, "Drawn", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.SignatureBase64)
                .NotEmpty().WithMessage("Vui lòng vẽ chữ ký trước khi gửi.")
                .Must(IsLikelyPngDataUrl).WithMessage("Chữ ký vẽ phải ở định dạng PNG.")
                .Must(b => b!.Length <= MaxBase64Length).WithMessage("Chữ ký vượt quá kích thước cho phép.");
        });

        When(x => string.Equals(x.SignatureKind, "Uploaded", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.UploadedImageStream)
                .NotNull().WithMessage("Vui lòng chọn ảnh chữ ký.");

            RuleFor(x => x.UploadedImageContentType)
                .NotEmpty()
                .Must(t => AllowedContentTypes.Contains(t!.ToLowerInvariant()))
                .WithMessage("Chỉ chấp nhận ảnh PNG hoặc JPEG.");

            RuleFor(x => x.UploadedImageSize)
                .NotNull()
                .Must(s => s!.Value > 0 && s.Value <= MaxUploadBytes)
                .WithMessage("Ảnh không được vượt quá 2 MB.");
        });
    }

    private static bool IsLikelyPngDataUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase);
    }
}
