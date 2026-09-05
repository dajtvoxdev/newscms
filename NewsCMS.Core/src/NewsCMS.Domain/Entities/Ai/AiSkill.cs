using NewsCMS.Domain.Common;

namespace NewsCMS.Domain.Entities.Ai;

public class AiSkill : AuditableEntity, ISoftDelete
{
    // Chung
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public AiSkillKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    // Prompt skill (Kind = Prompt)
    public string? SystemPrompt { get; set; }
    public string? UserPromptTemplate { get; set; }
    public bool AllowStyled { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    /// <summary>CSV target positions: post.title, post.body, post.excerpt, product.name, product.description, product.short</summary>
    public string? Targets { get; set; }
    public bool UseTools { get; set; }

    // Tool skill (Kind = Tool)
    public string? ToolType { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKeyEncrypted { get; set; }
    public string? ConfigJson { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
