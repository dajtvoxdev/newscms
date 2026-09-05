using FluentValidation;
using NewsCMS.Application.Ai.Dtos;

namespace NewsCMS.Application.Ai.Validators;

public class AiConnectionUpsertValidator : AbstractValidator<AiConnectionUpsertDto>
{
    public AiConnectionUpsertValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Provider).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            .WithMessage("Base URL phải là URL hợp lệ (http/https).");
        RuleFor(x => x.DefaultModel).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(5, 300);

        // API key required for new connections (no Id = create)
        When(x => x.Id == null, () =>
        {
            RuleFor(x => x.ApiKey).NotEmpty().WithMessage("API key là bắt buộc khi tạo kết nối mới.");
        });
    }
}
