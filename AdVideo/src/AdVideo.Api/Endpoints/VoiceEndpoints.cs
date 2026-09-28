using AdVideo.Api.Auth;
using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Core.Media;
using AdVideo.Core.Providers;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Endpoints;

/// <summary>Một giọng chọn được. Không lộ tên engine — khách chọn giọng, không chọn nhà cung cấp (Luật 3).</summary>
public sealed record VoiceResponse(Guid Id, string Name, string? Description, string Kind, string? PreviewUrl, DateTime CreatedAt);

/// <summary>
/// Giọng đọc của tenant: danh sách chọn (giọng có sẵn + giọng clone của mình), clone từ mẫu ghi âm, xoá.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clone giọng là thao tác nhạy cảm.</b> Giọng một người thật dùng được để giả mạo họ, nên: bắt
/// buộc lời xác nhận có quyền dùng giọng (lưu nguyên văn + người xác nhận + thời điểm), mẫu ghi âm
/// giữ lại trong <c>adv-voice</c> làm bằng chứng, giọng clone chỉ tenant tạo ra mới thấy.
/// </para>
/// <para>
/// Danh sách chỉ gồm giọng mà engine đọc của nó ĐANG BẬT: một giọng của engine đã tắt mà vẫn hiện ra
/// là một lựa chọn chắc chắn làm job fail ở bước 4.
/// </para>
/// </remarks>
public static class VoiceEndpoints
{
    public static IEndpointRouteBuilder MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/voices").RequireAuthorization(AuthPolicies.Tenant);

        group.MapGet("/", ListAsync).WithSummary("Giọng chọn được khi tạo video: giọng có sẵn + giọng clone của tenant.");

        group.MapPost("/", CloneAsync)
            .DisableAntiforgery()
            .WithSummary("Clone giọng từ 1–5 file ghi âm (multipart). Bắt buộc consent_confirmed=true và consent_statement.");

        group.MapDelete("/{id:guid}", DeleteAsync).WithSummary("Xoá một giọng clone của tenant (cả bên engine).");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AdVideoDbContext db, IProviderRegistry registry, IStorageService storage, ISettingsStore settings, CancellationToken ct)
    {
        HashSet<string> active = (await registry.GetTtsProvidersAsync(ct)).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<VoiceProfile> voices = await db.VoiceProfiles
            .AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.Kind == VoiceProfileKind.Cloned ? 0 : 1)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.Name)
            .ToListAsync(ct);

        int lifetime = await settings.GetIntAsync(SettingKeys.DownloadUrlLifetimeMinutes, 60, ct);
        var result = new List<VoiceResponse>();

        foreach (VoiceProfile v in voices.Where(v => active.Contains(v.Provider)))
        {
            result.Add(await ToResponseAsync(v, storage, lifetime, ct));
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CloneAsync(
        HttpContext http,
        AdVideoDbContext db,
        IProviderRegistry registry,
        IStorageService storage,
        ISettingsStore settings,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        Guid tenantId = http.User.GetTenantId();

        if (!http.Request.HasFormContentType)
        {
            return ApiProblem.Unprocessable(http, "Thiếu mẫu ghi âm", ["Gửi multipart/form-data: name, consent_statement, consent_confirmed=true, files."]);
        }

        IFormCollection form = await http.Request.ReadFormAsync(ct);

        string? name = form["name"].ToString().Trim();
        string? description = form["description"].ToString().Trim();
        string? consent = form["consent_statement"].ToString().Trim();
        string? consentedBy = form["consented_by"].ToString().Trim();
        bool confirmed = string.Equals(form["consent_confirmed"].ToString(), "true", StringComparison.OrdinalIgnoreCase);

        var errors = new List<string>();

        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            errors.Add("Tên giọng bắt buộc, tối đa 100 ký tự.");
        }

        if (!confirmed || string.IsNullOrEmpty(consent) || consent.Length < 10)
        {
            // Không có đường vòng: clone giọng người khác mà không có đồng ý là mạo danh.
            errors.Add("Phải xác nhận có quyền dùng giọng này (consent_confirmed=true) và ghi rõ giọng của ai, đồng ý thế nào (consent_statement, ít nhất 10 ký tự).");
        }

        IReadOnlyList<IFormFile> files = form.Files.GetFiles("files");

        if (files.Count == 0)
        {
            errors.Add("Cần ít nhất một file ghi âm (trường files).");
        }
        else if (files.Count > VoiceSampleFormat.MaxFiles)
        {
            errors.Add($"Tối đa {VoiceSampleFormat.MaxFiles} file ghi âm.");
        }

        var samples = new List<VoiceSample>();

        foreach (IFormFile file in files.Take(VoiceSampleFormat.MaxFiles))
        {
            if (file.Length == 0 || file.Length > VoiceSampleFormat.MaxBytes)
            {
                errors.Add($"\"{file.FileName}\": mỗi file 1 byte – {VoiceSampleFormat.MaxBytes / 1024 / 1024} MB.");
                continue;
            }

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, ct);
            byte[] bytes = buffer.ToArray();

            if (VoiceSampleFormat.Detect(bytes) is not { } format)
            {
                errors.Add($"\"{file.FileName}\" không phải file ghi âm (nhận mp3, wav, m4a, ogg, webm, flac).");
                continue;
            }

            samples.Add(new VoiceSample(Path.ChangeExtension(Path.GetFileName(file.FileName), format.Extension), format.ContentType, bytes));
        }

        if (errors.Count > 0)
        {
            return ApiProblem.Unprocessable(http, "Không clone được giọng", errors);
        }

        int max = await settings.GetIntAsync(SettingKeys.MaxClonedVoicesPerTenant, 5, ct);
        int owned = await db.VoiceProfiles.CountAsync(v => v.TenantId == tenantId && v.Kind == VoiceProfileKind.Cloned, ct);

        if (owned >= max)
        {
            return ApiProblem.Unprocessable(http, "Đã đủ số giọng clone", [$"Mỗi tài khoản tối đa {max} giọng clone. Xoá bớt giọng không dùng rồi thử lại."]);
        }

        string provider = await settings.GetStringAsync(SettingKeys.VoiceCloneProvider, ct) ?? string.Empty;
        IProviderVoices? voices = string.IsNullOrWhiteSpace(provider) ? null : await registry.FindVoicesAsync(provider, ct);

        if (voices is not { SupportsCloning: true })
        {
            return ApiProblem.Create(
                http,
                StatusCodes.Status503ServiceUnavailable,
                "Clone giọng chưa được bật",
                "Hệ thống chưa cấu hình engine clone giọng. Liên hệ quản trị nền tảng.");
        }

        var profile = new VoiceProfile
        {
            TenantId = tenantId,
            Name = name!,
            Description = string.IsNullOrEmpty(description) ? null : description,
            Provider = voices.Name,
            ProviderVoiceId = string.Empty,
            Kind = VoiceProfileKind.Cloned,
            ConsentStatement = consent,
            ConsentedBy = string.IsNullOrEmpty(consentedBy) ? null : consentedBy,
            ConsentedAt = DateTime.UtcNow,
        };

        // Mẫu vào kho TRƯỚC khi gửi đi clone: đây là bằng chứng giọng đến từ đâu, phải còn kể cả khi
        // engine xoá giọng hay ngừng phục vụ.
        var keys = new List<string>();

        for (int i = 0; i < samples.Count; i++)
        {
            string key = $"{tenantId:N}/voices/{profile.Id:N}/sample-{i + 1}{Path.GetExtension(samples[i].FileName)}";
            using var content = new MemoryStream(samples[i].Content, writable: false);
            await storage.UploadAsync(Buckets.Voice, key, content, samples[i].ContentType, ct);
            keys.Add(key);
        }

        VoiceCloneResult cloned = await voices.CloneAsync(profile.Name, profile.Description, samples, ct);

        if (!cloned.IsSuccess)
        {
            foreach (string key in keys)
            {
                await storage.DeleteAsync(Buckets.Voice, key, CancellationToken.None);
            }

            loggerFactory.CreateLogger(typeof(VoiceEndpoints)).LogWarning(
                "Clone giọng cho tenant {TenantId} thất bại: {Reason} {Raw}", tenantId, cloned.FailureReason, cloned.RawError);

            return ApiProblem.Unprocessable(http, "Engine không clone được giọng", [cloned.FailureReason ?? "Không rõ lý do."]);
        }

        profile.ProviderVoiceId = cloned.VoiceId!;
        profile.SampleObjectKey = keys[0];

        db.VoiceProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        int lifetime = await settings.GetIntAsync(SettingKeys.DownloadUrlLifetimeMinutes, 60, ct);

        return Results.Created($"/v1/voices/{profile.Id}", await ToResponseAsync(profile, storage, lifetime, ct));
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext http, Guid id, AdVideoDbContext db, IProviderRegistry registry, CancellationToken ct)
    {
        Guid tenantId = http.User.GetTenantId();

        // Chỉ giọng clone của chính mình. Giọng có sẵn thuộc người vận hành — trông như không tồn tại.
        VoiceProfile? voice = await db.VoiceProfiles
            .FirstOrDefaultAsync(v => v.Id == id && v.TenantId == tenantId && v.Kind == VoiceProfileKind.Cloned, ct);

        if (voice is null)
        {
            return ApiProblem.NotFound(http, $"Không có giọng clone {id} trong tài khoản này.");
        }

        // Xoá bên engine là cố gắng hết sức: engine đã xoá trước, hoặc key đổi — vẫn phải xoá được hồ
        // sơ, không thì khách không bao giờ dọn được danh sách.
        if (await registry.FindVoicesAsync(voice.Provider, ct) is { } voices)
        {
            await voices.DeleteAsync(voice.ProviderVoiceId, ct);
        }

        voice.IsDeleted = true;
        voice.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<VoiceResponse> ToResponseAsync(VoiceProfile v, IStorageService storage, int lifetimeMinutes, CancellationToken ct)
    {
        string? preview = v.PreviewUrl;

        if (preview is null && v.SampleObjectKey is { } key)
        {
            preview = (await storage.GetPresignedUrlAsync(Buckets.Voice, key, TimeSpan.FromMinutes(lifetimeMinutes), ct)).ToString();
        }

        return new VoiceResponse(v.Id, v.Name, v.Description, v.Kind == VoiceProfileKind.Cloned ? "cloned" : "preset", preview, v.CreatedAt);
    }
}
