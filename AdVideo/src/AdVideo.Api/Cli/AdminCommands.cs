using System.Text.Json;
using AdVideo.Api.Admin;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Core.Security;
using AdVideo.Infrastructure.Persistence;
using AdVideo.Infrastructure.Persistence.Seeding;
using AdVideo.Infrastructure.Providers;
using AdVideo.Infrastructure.Providers.Declarative;
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
        [
            "create-tenant", "rotate-key", "set-credential", "list-settings", "set-setting", "seed", "migrate",
            "set-descriptor", "list-descriptors", "activate-descriptor", "deactivate-descriptor", "test-descriptor",
            "create-operator-key", "list-operator-keys", "revoke-operator-key",
        ];

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
                "set-descriptor" => await SetDescriptorAsync(args, sp),
                "list-descriptors" => await ListDescriptorsAsync(args, sp),
                "activate-descriptor" => await ActivateDescriptorAsync(args, sp),
                "deactivate-descriptor" => await DeactivateDescriptorAsync(args, sp),
                "test-descriptor" => await TestDescriptorAsync(args, sp),
                "create-operator-key" => await CreateOperatorKeyAsync(args, sp),
                "list-operator-keys" => await ListOperatorKeysAsync(sp),
                "revoke-operator-key" => await RevokeOperatorKeyAsync(args, sp),
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

        IssuedTenantKey issued = await sp.GetRequiredService<TenantAdmin>().CreateAsync(name, Arg(args, "--note"));

        Console.WriteLine($"Đã tạo tenant {issued.Tenant.Name}");
        Console.WriteLine($"  id      : {issued.Tenant.Id}");
        Console.WriteLine($"  api key : {issued.ApiKey}");
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

        IssuedTenantKey? issued = await sp.GetRequiredService<TenantAdmin>().RotateKeyAsync(tenantId);

        if (issued is null)
        {
            Console.Error.WriteLine($"Không có tenant {tenantId}.");

            return 1;
        }

        // Key cũ mất hiệu lực NGAY, không có thời gian chuyển tiếp. Nếu sau này cần chuyển tiếp
        // êm thì phải là hai dòng key cùng sống, không phải một cột mới cho "key cũ".
        Console.WriteLine($"Đã cấp key mới cho {issued.Tenant.Name}. Key cũ đã hết hiệu lực ngay lập tức.");
        Console.WriteLine($"  api key : {issued.ApiKey}");

        return 0;
    }

    /// <summary>
    /// Sinh operator key — gốc của mọi quyền quản trị qua API.
    /// </summary>
    /// <remarks>
    /// Không có endpoint nào làm được việc này, cố ý: xem remarks của <see cref="OperatorKey"/>.
    /// </remarks>
    private static async Task<int> CreateOperatorKeyAsync(string[] args, IServiceProvider sp)
    {
        string? name = Arg(args, "--name");

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Thiếu --name. Ví dụ: create-operator-key --name \"NewsCMS admin\"");

            return 1;
        }

        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();

        string key = ApiKeyHasher.Generate(ApiKeyHasher.OperatorKeyPrefix);

        var row = new OperatorKey
        {
            Name = name.Trim(),
            ApiKeyPrefix = ApiKeyHasher.LookupPrefix(key),
            ApiKeyHash = ApiKeyHasher.Hash(key),
            Note = Arg(args, "--note"),
        };

        db.OperatorKeys.Add(row);
        await db.SaveChangesAsync();

        Console.WriteLine($"Đã tạo operator key \"{row.Name}\"");
        Console.WriteLine($"  id           : {row.Id}");
        Console.WriteLine($"  operator key : {key}");
        Console.WriteLine();
        Console.WriteLine("Key này mở toàn bộ /v1/admin: nạp key provider, đổi trần chi tiêu, tạo tenant.");
        Console.WriteLine("Chép ngay vào nơi app lưu secret — hệ thống chỉ giữ bản băm, không in lại được.");

        return 0;
    }

    private static async Task<int> ListOperatorKeysAsync(IServiceProvider sp)
    {
        List<OperatorKey> rows = await sp.GetRequiredService<AdVideoDbContext>().OperatorKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

        if (rows.Count == 0)
        {
            Console.WriteLine("Chưa có operator key nào. Tạo bằng create-operator-key --name \"...\".");

            return 0;
        }

        foreach (OperatorKey row in rows)
        {
            string state = row.IsActive ? "đang dùng" : $"thu hồi {row.RevokedAt:yyyy-MM-dd HH:mm}";
            string lastUsed = row.LastUsedAt is { } at ? at.ToString("yyyy-MM-dd HH:mm") : "chưa dùng";

            Console.WriteLine($"{row.Id}  {row.ApiKeyPrefix}…  {row.Name,-30} {state,-24} lần cuối: {lastUsed}");
        }

        return 0;
    }

    private static async Task<int> RevokeOperatorKeyAsync(string[] args, IServiceProvider sp)
    {
        if (!Guid.TryParse(Arg(args, "--id"), out Guid id))
        {
            Console.Error.WriteLine("Thiếu hoặc sai --id <guid>. Xem list-operator-keys.");

            return 1;
        }

        AdVideoDbContext db = sp.GetRequiredService<AdVideoDbContext>();
        OperatorKey? row = await db.OperatorKeys.FirstOrDefaultAsync(k => k.Id == id);

        if (row is null)
        {
            Console.Error.WriteLine($"Không có operator key {id}.");

            return 1;
        }

        row.IsActive = false;
        row.RevokedAt ??= DateTime.UtcNow;
        row.Note = string.IsNullOrWhiteSpace(Arg(args, "--reason")) ? row.Note : $"{row.Note} | thu hồi: {Arg(args, "--reason")}".Trim(' ', '|');

        await db.SaveChangesAsync();

        Console.WriteLine($"Đã thu hồi operator key \"{row.Name}\" ({row.ApiKeyPrefix}…). Có hiệu lực ngay ở request kế tiếp.");

        return 0;
    }

    private static async Task<int> SetCredentialAsync(string[] args, IServiceProvider sp)
    {
        string? provider = Arg(args, "--provider");

        if (string.IsNullOrWhiteSpace(provider))
        {
            Console.Error.WriteLine("Thiếu --provider. Ví dụ: set-credential --provider kling --key \"$FAL_KEY\"");

            return 1;
        }

        string? capabilityFile = Arg(args, "--capability-file");

        var input = new CredentialInput
        {
            Provider = provider,
            ApiKey = Arg(args, "--key"),
            ModelId = Arg(args, "--model"),
            EndpointUrl = Arg(args, "--endpoint"),
            CapabilityJson = capabilityFile is null ? null : await File.ReadAllTextAsync(capabilityFile),
            Priority = int.TryParse(Arg(args, "--priority"), out int priority) ? priority : null,

            // Chạy lại set-credential mà không có --disabled là BẬT lại — đúng thói quen khi nạp key
            // mới cho một credential vừa bị tắt tự động vì hết tiền (402).
            IsActive = !args.Contains("--disabled", StringComparer.Ordinal),
            Note = Arg(args, "--note"),
        };

        CredentialAdminResult result = await sp.GetRequiredService<CredentialAdmin>().UpsertAsync(input);

        if (result.Error is not null)
        {
            Console.Error.WriteLine(result.Error);

            return 1;
        }

        ProviderCredential saved = result.Credential!;

        Console.WriteLine($"Đã lưu credential {saved.Provider}/{saved.ModelId} ({saved.Category}).");

        Console.WriteLine(result.KeyChanged
            ? $"  key: {CredentialAdmin.Mask(input.ApiKey)} — đã mã hoá bằng DataProtection trước khi ghi."
            : "Không truyền --key nên giữ nguyên key cũ (nếu đã có).");

        foreach (string notice in result.Notices)
        {
            Console.WriteLine();
            Console.WriteLine(notice);
        }

        return 0;
    }

    private static async Task<int> SetDescriptorAsync(string[] args, IServiceProvider sp)
    {
        string? file = Arg(args, "--file");

        if (string.IsNullOrWhiteSpace(file))
        {
            Console.Error.WriteLine("Thiếu --file. Ví dụ: set-descriptor --file samples/providers/nova-grok-video-15.json --note \"thử NOVA\"");

            return 1;
        }

        if (!File.Exists(file))
        {
            // dotnet run chạy app với thư mục làm việc là thư mục project, nên đường dẫn tương đối
            // gõ từ thư mục AdVideo/ bị lệch. In đường dẫn tuyệt đối đã thử để người gõ thấy ngay.
            Console.Error.WriteLine($"Không thấy file {Path.GetFullPath(file)}. Dùng đường dẫn tuyệt đối, ví dụ --file \"$PWD/samples/providers/...\".");

            return 1;
        }

        string json = await File.ReadAllTextAsync(file);

        try
        {
            ProviderDescriptorRow row = await sp.GetRequiredService<IDescriptorStore>().AddVersionAsync(json, Arg(args, "--note"));

            Console.WriteLine($"Đã lưu descriptor {row.Code} bản {row.Version} ({row.Kind}) — đang TẮT.");
            Console.WriteLine($"  sha256: {row.Sha256}");
            Console.WriteLine();
            Console.WriteLine("Bước tiếp:");
            Console.WriteLine($"  1. test-descriptor --provider {row.Code} --version {row.Version}   (chạy khô, không gọi mạng)");
            Console.WriteLine($"  2. set-setting --key {SettingKeys.ProviderHostAllowlist} ...         (thêm host của provider nếu chưa có)");
            Console.WriteLine($"  3. set-credential --provider {row.Code} --key <api key>");
            Console.WriteLine($"  4. activate-descriptor --provider {row.Code} --version {row.Version}");

            return 0;
        }
        catch (DescriptorInvalidException ex)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }
    }

    private static async Task<int> ListDescriptorsAsync(string[] args, IServiceProvider sp)
    {
        IReadOnlyList<ProviderDescriptorRow> rows =
            await sp.GetRequiredService<IDescriptorStore>().ListAsync(Arg(args, "--provider"));

        if (rows.Count == 0)
        {
            Console.WriteLine("Chưa có descriptor nào.");

            return 0;
        }

        foreach (ProviderDescriptorRow row in rows)
        {
            Console.WriteLine(
                $"{(row.IsActive ? "*" : " ")} {row.Code,-28} v{row.Version,-3} {row.Kind,-5} {row.CreatedAt:yyyy-MM-dd HH:mm}  {row.Sha256[..12]}  {row.ChangeNote}");
        }

        Console.WriteLine();
        Console.WriteLine("* = đang bật.");

        return 0;
    }

    private static async Task<int> ActivateDescriptorAsync(string[] args, IServiceProvider sp)
    {
        string? provider = Arg(args, "--provider");

        if (string.IsNullOrWhiteSpace(provider) || !int.TryParse(Arg(args, "--version"), out int version))
        {
            Console.Error.WriteLine("Cần --provider và --version. Xem list-descriptors.");

            return 1;
        }

        try
        {
            await sp.GetRequiredService<IDescriptorStore>().ActivateVersionAsync(provider, version);
        }
        catch (DescriptorInvalidException ex)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }

        Console.WriteLine($"Đã bật descriptor {provider} bản {version}. Capability đã ghi vào credential cùng tên.");
        Console.WriteLine("Có hiệu lực trong vòng 30 giây trên mọi tiến trình (cache ngắn), không cần restart.");

        return 0;
    }

    private static async Task<int> DeactivateDescriptorAsync(string[] args, IServiceProvider sp)
    {
        string? provider = Arg(args, "--provider");

        if (string.IsNullOrWhiteSpace(provider))
        {
            Console.Error.WriteLine("Cần --provider.");

            return 1;
        }

        await sp.GetRequiredService<IDescriptorStore>().DeactivateAsync(provider);

        Console.WriteLine($"Đã tắt descriptor của {provider}. Provider quay về adapter viết tay nếu có, không thì biến khỏi danh sách chọn.");

        return 0;
    }

    private static async Task<int> TestDescriptorAsync(string[] args, IServiceProvider sp)
    {
        string? json = null;

        if (Arg(args, "--file") is { } file)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Không thấy file {Path.GetFullPath(file)}. Dùng đường dẫn tuyệt đối.");

                return 1;
            }

            json = await File.ReadAllTextAsync(file);
        }
        else if (Arg(args, "--provider") is { } provider)
        {
            IReadOnlyList<ProviderDescriptorRow> rows = await sp.GetRequiredService<IDescriptorStore>().ListAsync(provider);
            ProviderDescriptorRow? row = int.TryParse(Arg(args, "--version"), out int version)
                ? rows.FirstOrDefault(r => r.Version == version)
                : rows.FirstOrDefault();

            json = row?.Json;
        }

        if (json is null)
        {
            Console.Error.WriteLine("Cần --file <đường dẫn> hoặc --provider <tên> [--version <số>].");

            return 1;
        }

        DescriptorParseResult parsed = ProviderDescriptorParser.Parse(json);

        if (!parsed.IsValid)
        {
            Console.Error.WriteLine("Descriptor KHÔNG hợp lệ:");

            foreach (string error in parsed.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }

            return 1;
        }

        string? allowlist = await sp.GetRequiredService<ISettingsStore>().GetStringAsync(SettingKeys.ProviderHostAllowlist);

        DescriptorPreviewResult preview = DescriptorPreview.Render(parsed.Descriptor!, ProviderHostAllowlist.Parse(allowlist));

        Console.WriteLine($"Descriptor {parsed.Descriptor!.Name} hợp lệ. Request sẽ gửi cho một request mẫu (KHÔNG gọi mạng):");
        Console.WriteLine();
        Console.WriteLine($"{preview.Method} {preview.Url}");

        foreach ((string name, string value) in preview.Headers)
        {
            Console.WriteLine($"{name}: {value}");
        }

        if (preview.Body is not null)
        {
            Console.WriteLine();
            Console.WriteLine(preview.Body);
        }

        foreach (string warning in preview.Warnings)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ {warning}");
        }

        return preview.Warnings.Count == 0 ? 0 : 2;
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

        // Kiểm kiểu + khoảng hợp lệ ở SettingAdmin chứ không để người gõ tự nhớ: min/max nằm sẵn
        // trong DB, và một MaxConcurrentShots = 64 gõ nhầm sẽ đốt hạn mức provider trong vài phút.
        SettingUpdateResult result = await sp.GetRequiredService<SettingAdmin>().UpdateAsync(key, value, isProvisional: false);

        if (result.Error is not null)
        {
            Console.Error.WriteLine(result.NotFound ? $"{result.Error} Xem danh sách bằng list-settings." : result.Error);

            return 1;
        }

        Console.WriteLine($"{key}: {result.PreviousValue} → {result.Setting!.Value}");
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

              create-operator-key --name "Tên" [--note "..."]
                  Sinh operator key cho API quản trị /v1/admin. In MỘT LẦN DUY NHẤT.

              list-operator-keys
                  Liệt kê operator key, lần dùng cuối, key nào đã thu hồi.

              revoke-operator-key --id <guid> [--reason "..."]
                  Thu hồi operator key. Có hiệu lực ngay.

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

              set-descriptor --file <json> [--note "..."]
                  Kiểm rồi lưu một bản descriptor mới ở trạng thái TẮT.

              list-descriptors [--provider <tên>]
                  Liệt kê mọi bản descriptor, * = đang bật.

              test-descriptor (--file <json> | --provider <tên> [--version <số>])
                  Chạy khô: kiểm, dựng request mẫu, soát allowlist. Không gọi mạng.

              activate-descriptor --provider <tên> --version <số>
                  Bật một bản (tắt bản đang bật), ghi capability vào credential cùng tên.

              deactivate-descriptor --provider <tên>
                  Tắt descriptor; provider quay về adapter viết tay nếu có.
            """);

        return 1;
    }
}
