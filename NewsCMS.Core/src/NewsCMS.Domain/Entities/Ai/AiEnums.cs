namespace NewsCMS.Domain.Entities.Ai;

public enum AiSkillKind
{
    Prompt = 0,
    Tool = 1
}

public enum AiGenerationStyle
{
    Plain = 0,
    Styled = 1
}

public static class AiTaskKeys
{
    public const string GenerateBody = "generate_body";
    public const string Summarize = "summarize";
    public const string SuggestTitle = "suggest_title";
    public const string Rewrite = "rewrite";
    /// <summary>Trò chuyện soạn bài nhiều lượt — sinh đồng thời tiêu đề, tóm tắt, nội dung.</summary>
    public const string ArticleChat = "article_chat";
    public const string KeoBiaExpertAnalysis = "keobia_expert_analysis";
    public const string KeoBiaCorrectScoreOdds = "keobia_correct_score_odds";
    /// <summary>Sinh mẫu brief video quảng cáo theo xu hướng — tác vụ nền của kho prompt VideoStudio.</summary>
    public const string VideoStudioTrendTemplates = "videostudio_trend_templates";
    /// <summary>Sinh mẫu prompt ảnh theo xu hướng — tác vụ nền của kho mẫu Xưởng ảnh.</summary>
    public const string ImageStudioTrendTemplates = "imagestudio_trend_templates";
    /// <summary>Viết lại mô tả ảnh của người dùng cho chi tiết hơn (nút "Cải thiện prompt").</summary>
    public const string ImageStudioPromptEnhance = "imagestudio_prompt_enhance";
    /// <summary>Gợi ý prompt ảnh + alt + chú thích từ tiêu đề/tóm tắt bài hoặc tên/mô tả sản phẩm.</summary>
    public const string ImageStudioSuggestPrompt = "imagestudio_suggest_prompt";
}
