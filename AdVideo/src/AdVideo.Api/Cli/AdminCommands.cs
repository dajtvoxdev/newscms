using System.Text.Json;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Cli;

/// <summary>
/// Lệnh quản trị chạy từ dòng lệnh, dùng chung host với API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao là CLI chứ không phải endpoint quản trị.</b> Những lệnh này nạp API key và tạo
/// tenant — tức là chúng cấp quyền. Một endpoint làm việc đó cần chính nó được bảo vệ bằng một
/// cơ chế xác thực khác, và cơ chế đó lại cần một cách khởi tạo. CLI cắt vòng luẩn quẩn: ai chạy
/// được lệnh trên máy chủ thì đã có quyền cao nhất rồi.
/// </para>
/// <para>
/// <b>Dùng chung host với API</b> để đọc đúng một bộ cấu hình và đúng một key ring DataProtection.
/// Một công cụ riêng với connection string riêng là cách chắc chắn để mã hoá key bằng một khoá mà
/// API không giải mã được.
/// </para>
/// </remarks>
public static class AdminCommands
{
    /// <summary>Những từ đầu tiên được coi là lệnh CLI thay vì tham số của web host.</summary>
    private static readonly string[] Known =
        ["create-tenant", "rotate-key", "set-credential", "list-settings", "set-setting", "seed", "migrate"];

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && Known.Contains(args[0], StringComparer.Ordinal);

    /// <summary>Chạy lệnh. Trả về exit code.</summary>
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        try
        {
            return args[0] switch
            {
                "create-tenant" => await CreateTenantAsync(args, sp),
                "rotate-key" => await RotateKeyAsync(args, sp),
                "set-credential" => await SetCredentialAsync(args, sp),
                "list-settings" => await ListSettingsAsync(sp),
                "set-setting" => await SetSettingAsync(args, sp),
                "seed" => await SeedAsync(sp),
                "migrate" => await MigrateAsync(sp),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            // Lỗi ở đây là lỗi của người vận hành gõ lệnh, không phải của một request. In gọn
            // một dòng thay vì stack trace — trừ khi bật chi tiết.
            Console.Error.WriteLine($"Lỗi: {ex.Message}");

            if (args.Contains("--verbose", StringComparer.Ordinal))
            {
                Console.Error.WriteLine(ex);
            }

            return 1;
        }
    }

    private static async Task<int> CreateTenantAsync(string[] args, IServiceProvider sp)
    {
        string? name = Arg(args, "--name");

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Thiếu --name. Ví dụ: create-tenant --name \"CHU Kafe\"");

            return 1;
        }

        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

        string apiKey = ApiKeyHasher.Generate();

        var tenant = new Tenant
        {
            Name = name.Trim(),
            ApiKeyPrefix = ApiKeyHasher.LookupPrefix(apiKey),
            ApiKeyHash = ApiKeyHasher.Hash(apiKey),
            Note = Arg(args, "--note"),
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        Console.WriteLine($"Đã tạo tenant {tenant.Name}");
        Console.WriteLine($"  id      : {tenant.Id}");
        Console.WriteLine($"  api key : {apiKey}");
        Console.WriteLine();
        Console.WriteLine("Chép key ngay. Hệ thống chỉ lưu bản băm nên không in lại được lần thứ hai;");
        Console.WriteLine("mất thì dùng lệnh rotate-key để cấp key mới.");

        return 0;
    }

    private static async Task<int> RotateKeyAsync(string[] args, IServiceProvider sp)
    {
        string? id = Arg(args, "--tenant");

        if (!Guid.TryParse(id, out Guid tenantId))
        {
            Console.Error.WriteLine("Thiếu hoặc sai --tenant <guid>.");

            return 1;
        }

        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
        Tenant? tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null)
        {
            Console.Error.WriteLine($"Không có tenant {tenantId}.");

            return 1;
        }

        string apiKey = ApiKeyHasher.Generate();

        tenant.ApiKeyPrefix = ApiKeyHasher.LookupPrefix(apiKey);
        tenant.ApiKeyHash = ApiKeyHasher.Hash(apiKey);
        tenant.ApiKeyRotatedAt = DateTime.UtcNow;
        tenant.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Key cũ mất hiệu lực NGAY, không có thời gian chuyển tiếp. Nếu sau này cần chuyển tiếp
        // êm thì phải là hai dòng key cùng sống, không phải một cột mới cho "key cũ".
        Console.WriteLine($"Đã cấp key mới cho {tenant.Name}. Key cũ đã hết hiệu lực ngay lập tức.");
        Console.WriteLine($"  api key : {apiKey}");

        return 0;
    }

    private static async Task<int> SetCredentialAsync(string[] args, IServiceProvider sp)
    {
        string? provider = Arg(args, "--provider")?.Trim().ToLowerInvariant();
        string key = Arg(args, "--key") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(provider))
        {
            Console.Error.WriteLine("Thiếu --provider. Ví dụ: set-credential --provider veo --key \"$GEMINI_API_KEY\"");

            return 1;
        }

        ProviderCategory? category = ProviderCapabilityCatalog.CategoryOf(provider);

        if (category is null)
        {
            Console.Error.WriteLine(
                $"Không nhận ra provider \"{provider}\". Các tên đã biết: veo, kling, seedance, vidu, elevenlabs, vieneu.");

            return 1;
        }

        string? capabilityJson = null;
        string? modelId = Arg(args, "--model");

        string? capabilityFile = Arg(args, "--capability-file");

        if (capabilityFile is not null)
        {
            capabilityJson = await File.ReadAllTextAsync(capabilityFile);
        }
        else if (category == ProviderCategory.Video)
        {
            VideoProviderCapability? template = ProviderCapabilityCatalog.Video(provider);

            if (template is not null)
            {
                if (modelId is not null)
                {
                    template = template with { ModelId = modelId };
                }

                capabilityJson = JsonSerializer.Serialize(template, AdVideoJson.Indented);
            }
        }
        else
        {
            TtsProviderCapability? template = ProviderCapabilityCatalog.Tts(provider);

            if (template is not null)
            {
                if (modelId is not null)
                {
                    template = template with { ModelId = modelId };
                }

                capabilityJson = JsonSerializer.Serialize(template, AdVideoJson.Indented);
            }
        }

        if (capabilityJson is null)
        {
            Console.Error.WriteLine(
                $"Chưa có manifest mẫu cho \"{provider}\". Truyền --capability-file <đường dẫn json>.");

            return 1;
        }

        string resolvedModelId = modelId ?? ReadModelId(capabilityJson) ?? provider;

        ICredentialStore store = sp.GetRequiredService<ICredentialStore>();

        await store.UpsertAsync(
            new ProviderCredential
            {
                Provider = provider,
                ModelId = resolvedModelId,
                Category = category.Value,
                Scope = CredentialScope.System,
                TenantId = null,
                EndpointUrl = Arg(args, "--endpoint") ?? ProviderCapabilityCatalog.DefaultEndpoint(provider),
                EncryptedApiKey = string.Empty,
                CapabilityJson = capabilityJson,
                IsActive = !args.Contains("--disabled", StringComparer.Ordinal),
                Priority = int.TryParse(Arg(args, "--priority"), out int p) ? p : 0,
                Notes = Arg(args, "--note"),
            },
            key);

        Console.WriteLine($"Đã lưu credential {provider}/{resolvedModelId} ({category}).");

        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine("Không truyền --key nên giữ nguyên key cũ (nếu đã có).");
        }
        else
        {
            Console.WriteLine($"  key: {ApiKeyHasher.Mask(key)} — đã mã hoá bằng DataProtection trước khi ghi.");
        }

        VideoProviderCapability? check = ProviderCapabilityCatalog.Video(provider);

        if (check is not null && capabilityFile is null)
        {
            Console.WriteLine();
            Console.WriteLine($"Đơn giá đang dùng: {check.CostPerSecondUsd} USD/giây — số tham khảo từ bảng so sánh,");
            Console.WriteLine("KHÔNG phải số đo. Kiểm lại bảng giá của nhà cung cấp trước khi bật provider này.");
        }

        return 0;
    }

    private static async Task<int> ListSettingsAsync(IServiceProvider sp)
    {
        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

        List<SystemSetting> settings = await db.SystemSettings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync();

        if (settings.Count == 0)
        {
            Console.WriteLine("Bảng SystemSettings rỗng. Chạy lệnh seed trước.");

            return 0;
        }

        int width = settings.Max(s => s.Key.Length);

        foreach (SystemSetting setting in settings)
        {
            string flag = setting.IsProvisional ? " (tạm)" : string.Empty;

            Console.WriteLine($"{setting.Key.PadRight(width)} = {setting.Value}{flag}");
        }

        int provisional = settings.Count(s => s.IsProvisional);

        if (provisional > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{provisional} giá trị còn là phỏng đoán (tạm) — phải đo lại bằng key thật trước khi chạy thật.");
        }

        return 0;
    }

    private static async Task<int> SetSettingAsync(string[] args, IServiceProvider sp)
    {
        string? key = Arg(args, "--key");
        string? value = Arg(args, "--value");

        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            Console.Error.WriteLine("Cú pháp: set-setting --key <tên> --value <giá trị>");

            return 1;
        }

        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
        SystemSetting? setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);

        if (setting is null)
        {
            Console.Error.WriteLine($"Không có setting \"{key}\". Xem danh sách bằng list-settings.");

            return 1;
        }

        // Kiểm khoảng hợp lệ ở đây chứ không để người gõ tự nhớ: min/max nằm sẵn trong DB, và
        // một MaxConcurrentShots = 64 gõ nhầm sẽ đốt hạn mức provider trong vài phút.
        if (!IsWithinRange(setting, value, out string? problem))
        {
            Console.Error.WriteLine(problem);

            return 1;
        }

        ISettingsStore store = sp.GetRequiredService<ISettingsStore>();

        await store.SetAsync(key, value, setting.ValueType, setting.Description, isProvisional: false);

        Console.WriteLine($"{key}: {setting.Value} → {value}");
        Console.WriteLine("Cache đã được làm mới; API và Worker nhận giá trị mới mà không cần khởi động lại.");

        return 0;
    }

    private static async Task<int> SeedAsync(IServiceProvider sp)
    {
        int settings = await sp.GetRequiredService<SystemSettingSeeder>().SeedAsync();
        int prompts = await sp.GetRequiredService<PromptTemplateSeeder>().SeedAsync();

        Console.WriteLine($"Đã nạp {settings} setting và {prompts} prompt còn thiếu.");
        Console.WriteLine("Seeder chỉ THÊM khoá thiếu, không ghi đè giá trị người vận hành đã sửa.");

        return 0;
    }

    private static async Task<int> MigrateAsync(IServiceProvider sp)
    {
        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

        Console.WriteLine("Đang áp migration…");
        await db.Database.MigrateAsync();
        Console.WriteLine("Xong.");

        return 0;
    }

    private static bool IsWithinRange(SystemSetting setting, string value, out string? problem)
    {
        problem = null;

        if (setting.ValueType is not (SettingValueType.Int or SettingValueType.Decimal))
        {
            return true;
        }

        if (!decimal.TryParse(value, out decimal parsed))
        {
            problem = $"\"{value}\" không phải số, trong khi {setting.Key} có kiểu {setting.ValueType}.";

            return false;
        }

        if (decimal.TryParse(setting.MinValue, out decimal min) && parsed < min)
        {
            problem = $"{setting.Key} phải ≥ {min}. {setting.Description}";

            return false;
        }

        if (decimal.TryParse(setting.MaxValue, out decimal max) && parsed > max)
        {
            problem = $"{setting.Key} phải ≤ {max}. {setting.Description}";

            return false;
        }

        return true;
    }

    private static string? ReadModelId(string capabilityJson)
    {
        using JsonDocument doc = JsonDocument.Parse(capabilityJson);

        return doc.RootElement.TryGetProperty("modelId", out JsonElement element)
            ? element.GetString()
            : null;
    }

    private static string? Arg(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Usage()
    {
        Console.WriteLine("""
            Lệnh quản trị AdVideo:

              migrate
                  Áp migration lên DB.

              seed
                  Nạp setting và prompt còn thiếu. Không ghi đè giá trị đã sửa.

              create-tenant --name "Tên khách" [--note "..."]
                  Tạo tenant và in API key MỘT LẦN DUY NHẤT.

              rotate-key --tenant <guid>
                  Cấp key mới. Key cũ mất hiệu lực ngay.

              set-credential --provider <tên> [--key <api key>] [--model <id>]
                            [--endpoint <url>] [--capability-file <json>]
                            [--priority <số>] [--disabled] [--note "..."]
                  Nạp hoặc sửa credential provider. Bỏ --key thì giữ key cũ.

              list-settings
                  In mọi setting kèm cờ (tạm) cho giá trị chưa đo.

              set-setting --key <tên> --value <giá trị>
                  Đổi một setting, có kiểm khoảng hợp lệ.
            """);

        return 1;
    }
}
