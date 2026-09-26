using System.Text.Json.Nodes;
using AdVideo.Core.Providers.Descriptors;

namespace AdVideo.Core.Tests.Providers.Descriptors;

/// <summary>Đọc descriptor mẫu trong <c>samples/providers</c> và dựng biến thể hỏng từ chúng.</summary>
internal static class Samples
{
    public const string Nova = "nova-grok-video-15.json";
    public const string FalKling = "fal-kling.json";
    public const string ElevenLabs = "elevenlabs-flash.json";

    public static string Read(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "providers", file));

    /// <summary>Nạp mẫu thành cây JSON (đã bỏ comment), sửa, rồi parse lại.</summary>
    public static DescriptorParseResult Mutate(string file, Action<JsonObject> mutate)
    {
        JsonObject root = (JsonObject)JsonNode.Parse(
            Read(file),
            documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })!;

        mutate(root);

        return ProviderDescriptorParser.Parse(root.ToJsonString());
    }

    public static JsonObject Obj(this JsonObject root, string path)
    {
        JsonObject current = root;

        foreach (string part in path.Split('.'))
        {
            current = (JsonObject)current[part]!;
        }

        return current;
    }
}
