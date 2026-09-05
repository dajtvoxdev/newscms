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
    public const string KeoBiaExpertAnalysis = "keobia_expert_analysis";
    public const string KeoBiaCorrectScoreOdds = "keobia_correct_score_odds";
}
