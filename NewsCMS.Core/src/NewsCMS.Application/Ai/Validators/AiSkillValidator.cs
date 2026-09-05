using FluentValidation;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Domain.Entities.Ai;

namespace NewsCMS.Application.Ai.Validators;

public class AiSkillUpsertValidator : AbstractValidator<AiSkillUpsertDto>
{
    public AiSkillUpsertValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100).Matches("^[a-z0-9_]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        When(x => x.Kind == AiSkillKind.Prompt, () =>
        {
            RuleFor(x => x.SystemPrompt).NotEmpty().MaximumLength(5000);
            RuleFor(x => x.UserPromptTemplate).NotEmpty().MaximumLength(5000);
            RuleFor(x => x.Temperature)
                .InclusiveBetween(0.0, 2.0)
                .When(x => x.Temperature.HasValue);
            RuleFor(x => x.MaxTokens)
                .InclusiveBetween(1, 16384)
                .When(x => x.MaxTokens.HasValue);
            RuleFor(x => x.Targets)
                .Matches(@"^[a-z._]+(,[a-z._]+)*$")
                .When(x => !string.IsNullOrEmpty(x.Targets))
                .WithMessage("Targets phải là danh sách phân cách bằng dấu phẩy (vd: post.body,post.excerpt).");
        });

        When(x => x.Kind == AiSkillKind.Tool, () =>
        {
            RuleFor(x => x.ToolType).NotEmpty();
            RuleFor(x => x.BaseUrl)
                .Must(url => string.IsNullOrEmpty(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
                .WithMessage("Base URL không hợp lệ.");
            RuleFor(x => x.ConfigJson).MaximumLength(5000);
        });
    }
}
