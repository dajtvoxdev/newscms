using System.Text.Json.Nodes;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Persistence.Stores;
using System.Text.Json;

namespace AdVideo.Tests.Providers.Declarative;

/// <summary>Nạp descriptor mẫu, dựng biến thể, và credential giả cho test engine.</summary>
internal static class DescriptorTestKit
{
    public const string Nova = "nova-grok-video-15.json";
    public const string FalKling = "fal-kling.json";
    public const string ElevenLabs = "elevenlabs-flash.json";

    public static string Read(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "providers", file));

    public static ActiveDescriptor Load(string file, Action<JsonObject>? mutate = null)
    {
        string json = Read(file);

        if (mutate is not null)
        {
            var root = (JsonObject)JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })!;

            mutate(root);
            json = root.ToJsonString();
        }

        DescriptorParseResult parsed = ProviderDescriptorParser.Parse(json);

        if (!parsed.IsValid)
        {
            throw new InvalidOperationException(string.Join(" | ", parsed.Errors));
        }

        return new ActiveDescriptor(parsed.Descriptor!, 1, DbDescriptorStore.Sha256Of(json));
    }

    public static T Capability<T>(ActiveDescriptor descriptor) =>
        descriptor.Descriptor.Capability!.Deserialize<T>(ProviderDescriptorParser.JsonOptions)!;

    public static ResolvedCredential Credential(ActiveDescriptor descriptor, string apiKey, string? modelId = null) => new(
        descriptor.Descriptor.Name,
        modelId ?? descriptor.Descriptor.Capability!["modelId"]!.GetValue<string>(),
        descriptor.Descriptor.Kind == DescriptorKind.Video ? ProviderCategory.Video : ProviderCategory.TextToSpeech,
        descriptor.Descriptor.Transport.BaseUrl,
        apiKey,
        CredentialScope.System,
        null,
        0);

    /// <summary>Delay không chờ thật, đếm số lần được gọi.</summary>
    public sealed class CountingDelay
    {
        public int Calls { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.CompletedTask;
        }
    }
}

/// <summary>Chỉ ghi lại lệnh tắt credential; mọi hàm khác không được gọi trong test engine.</summary>
internal sealed class RecordingCredentialStore : ICredentialStore
{
    public List<(string Provider, string Reason)> Deactivated { get; } = [];

    public Task DeactivateAsync(string provider, string reason, CancellationToken cancellationToken = default)
    {
        Deactivated.Add((provider, reason));

        return Task.CompletedTask;
    }

    public Task<ResolvedCredential?> GetAsync(string provider, ProviderCategory category, Guid? tenantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ResolvedCredential>> ListActiveAsync(ProviderCategory category, Guid? tenantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<VideoProviderCapability?> GetVideoCapabilityAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TtsProviderCapability?> GetTtsCapabilityAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VideoProviderCapability>> GetAllVideoCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpsertAsync(ProviderCredential credential, string plainApiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void Invalidate() => throw new NotSupportedException();
}
