using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Admin;

public sealed record VoicePresetView(
    Guid Id, string Name, string? Description, string Provider, string ProviderVoiceId,
    string? PreviewUrl, bool IsActive, int SortOrder, DateTime CreatedAt)
{
    public static VoicePresetView From(VoiceProfile v) =>
        new(v.Id, v.Name, v.Description, v.Provider, v.ProviderVoiceId, v.PreviewUrl, v.IsActive, v.SortOrder, v.CreatedAt);
}

public sealed record UpsertVoicePresetRequest(
    string? Name, string? Description, string? Provider, string? ProviderVoiceId, string? PreviewUrl, int? SortOrder, bool? IsActive);

/// <param name="AlreadyAdded">Giọng này đã là giọng có sẵn — màn hình hiện "đã thêm" thay vì nút thêm.</param>
public sealed record ProviderVoiceView(
    string VoiceId, string Name, string? Category, string? Description, string? PreviewUrl,
    IReadOnlyDictionary<string, string> Labels, bool AlreadyAdded);

/// <summary>Số giọng clone theo tenant — người vận hành canh số slot giọng của gói engine.</summary>
public sealed record ClonedVoiceStats(Guid TenantId, string TenantName, int Count);

/// <summary>
/// Giọng có sẵn (preset) cho mọi tenant: thêm tay, hoặc nhập từ thư viện giọng của engine.
/// </summary>
/// <remarks>
/// Thứ tự (<c>sort_order</c>) có nghĩa: giọng có sẵn đầu tiên của một engine là giọng mặc định khi
/// khách không chọn giọng nào.
/// </remarks>
public static class AdminVoiceEndpoints
{
    public static RouteGroupBuilder MapAdminVoiceEndpoints(this RouteGroupBuilder admin)
    {
        RouteGroupBuilder group = admin.MapGroup("/voices");

        group.MapGet("/", async (AdVideoDbContext db, CancellationToken ct) =>
        {
            // Operator không mang tenant: filter chỉ để lộ preset (TenantId null) — đúng thứ cần ở đây.
            List<VoiceProfile> presets = await db.VoiceProfiles.AsNoTracking()
                .Where(v => v.TenantId == null)
                .OrderBy(v => v.Provider).ThenBy(v => v.SortOrder).ThenBy(v => v.Name)
                .ToListAsync(ct);

            return Results.Ok(presets.Select(VoicePresetView.From));
        })
        .WithSummary("Giọng có sẵn, theo engine rồi theo thứ tự hiển thị.");

        group.MapGet("/cloned-stats", async (AdVideoDbContext db, CancellationToken ct) =>
        {
            var stats = await db.VoiceProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.Kind == VoiceProfileKind.Cloned && !v.IsDeleted && v.TenantId != null)
                .GroupBy(v => v.TenantId!.Value)
                .Select(g => new { TenantId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            Dictionary<Guid, string> names = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            return Results.Ok(stats.Select(s => new ClonedVoiceStats(s.TenantId, names.GetValueOrDefault(s.TenantId, "?"), s.Count)));
        })
        .WithSummary("Số giọng clone theo tenant (không lộ mẫu ghi âm hay voice id).");

        group.MapGet("/library", async (HttpContext http, string provider, IProviderRegistry registry, AdVideoDbContext db, CancellationToken ct) =>
        {
            IProviderVoices? voices = await registry.FindVoicesAsync(provider, ct);

            if (voices is null)
            {
                return ApiProblem.Unprocessable(http, "Không đọc được thư viện giọng",
                    [$"Engine \"{provider}\" đang tắt, chưa có key, hoặc không có thư viện giọng qua API."]);
            }

            IReadOnlyList<ProviderVoice> list;

            try
            {
                list = await voices.ListAsync(ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                return ApiProblem.Unprocessable(http, "Engine từ chối liệt kê giọng", [ex.Message]);
            }

            HashSet<string> added = (await db.VoiceProfiles.AsNoTracking()
                    .Where(v => v.TenantId == null && v.Provider == voices.Name)
                    .Select(v => v.ProviderVoiceId)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            // Giọng clone của các tenant cũng nằm trong thư viện tài khoản engine — không bao giờ đưa
            // chúng ra làm giọng có sẵn cho mọi người.
            HashSet<string> cloned = (await db.VoiceProfiles.IgnoreQueryFilters().AsNoTracking()
                    .Where(v => v.Kind == VoiceProfileKind.Cloned && v.Provider == voices.Name)
                    .Select(v => v.ProviderVoiceId)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            return Results.Ok(list
                .Where(v => !cloned.Contains(v.VoiceId))
                .Select(v => new ProviderVoiceView(v.VoiceId, v.Name, v.Category, v.Description, v.PreviewUrl, v.Labels, added.Contains(v.VoiceId))));
        })
        .WithSummary("Thư viện giọng của tài khoản engine (?provider=elevenlabs), trừ giọng clone của khách.");

        group.MapPost("/", async (HttpContext http, [FromBody] UpsertVoicePresetRequest? body, AdVideoDbContext db, CancellationToken ct) =>
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(body?.Name)) errors.Add("Thiếu name.");
            if (string.IsNullOrWhiteSpace(body?.Provider)) errors.Add("Thiếu provider (engine TTS, ví dụ elevenlabs).");
            if (string.IsNullOrWhiteSpace(body?.ProviderVoiceId)) errors.Add("Thiếu provider_voice_id.");

            if (errors.Count > 0)
            {
                return ApiProblem.Unprocessable(http, "Không thêm được giọng", errors);
            }

            string provider = body!.Provider!.Trim().ToLowerInvariant();
            string voiceId = body.ProviderVoiceId!.Trim();

            if (await db.VoiceProfiles.AnyAsync(v => v.TenantId == null && v.Provider == provider && v.ProviderVoiceId == voiceId, ct))
            {
                return ApiProblem.Unprocessable(http, "Giọng đã có", [$"{provider}/{voiceId} đã là giọng có sẵn."]);
            }

            int nextOrder = (await db.VoiceProfiles.Where(v => v.TenantId == null).Select(v => (int?)v.SortOrder).MaxAsync(ct) ?? 0) + 10;

            var voice = new VoiceProfile
            {
                Name = body.Name!.Trim(),
                Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
                Provider = provider,
                ProviderVoiceId = voiceId,
                Kind = VoiceProfileKind.Preset,
                PreviewUrl = string.IsNullOrWhiteSpace(body.PreviewUrl) ? null : body.PreviewUrl.Trim(),
                SortOrder = body.SortOrder ?? nextOrder,
                IsActive = body.IsActive ?? true,
            };

            db.VoiceProfiles.Add(voice);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/v1/admin/voices/{voice.Id}", VoicePresetView.From(voice));
        })
        .WithSummary("Thêm giọng có sẵn.");

        group.MapPut("/{id:guid}", async (HttpContext http, Guid id, [FromBody] UpsertVoicePresetRequest? body, AdVideoDbContext db, CancellationToken ct) =>
        {
            VoiceProfile? voice = await db.VoiceProfiles.FirstOrDefaultAsync(v => v.Id == id && v.TenantId == null, ct);

            if (voice is null)
            {
                return ApiProblem.NotFound(http, $"Không có giọng có sẵn {id}.");
            }

            if (!string.IsNullOrWhiteSpace(body?.Name)) voice.Name = body.Name.Trim();
            if (body?.Description is not null) voice.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
            if (body?.PreviewUrl is not null) voice.PreviewUrl = string.IsNullOrWhiteSpace(body.PreviewUrl) ? null : body.PreviewUrl.Trim();
            if (body?.SortOrder is { } order) voice.SortOrder = order;
            if (body?.IsActive is { } active) voice.IsActive = active;

            await db.SaveChangesAsync(ct);

            return Results.Ok(VoicePresetView.From(voice));
        })
        .WithSummary("Sửa tên, mô tả, thứ tự, bật/tắt một giọng có sẵn. Không đổi engine/voice id — thêm giọng mới.");

        group.MapDelete("/{id:guid}", async (HttpContext http, Guid id, AdVideoDbContext db, CancellationToken ct) =>
        {
            VoiceProfile? voice = await db.VoiceProfiles.FirstOrDefaultAsync(v => v.Id == id && v.TenantId == null, ct);

            if (voice is null)
            {
                return ApiProblem.NotFound(http, $"Không có giọng có sẵn {id}.");
            }

            // Chỉ gỡ khỏi danh sách; KHÔNG xoá bên engine — giọng có sẵn thuộc thư viện tài khoản.
            voice.IsDeleted = true;
            voice.IsActive = false;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        })
        .WithSummary("Gỡ giọng có sẵn khỏi danh sách chọn. Video cũ không bị ảnh hưởng.");

        return group;
    }
}
