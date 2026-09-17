using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ai;

public sealed class AiConnectionService : IAiConnectionService
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IAiChatClient _chatClient;

    public AiConnectionService(AppDbContext db, IDataProtectionProvider dp, IAiChatClient chatClient)
    {
        _db = db;
        _protector = dp.CreateProtector("NewsCMS.Ai.ApiKey");
        _chatClient = chatClient;
    }

    public async Task<IReadOnlyList<AiConnectionDto>> GetAllAsync(CancellationToken ct = default) =>
        await _db.AiConnections.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .Select(x => new AiConnectionDto(
                x.Id, x.Name, x.Provider, x.BaseUrl, x.DefaultModel,
                x.IsActive, x.IsDefault, x.TimeoutSeconds, x.Description,
                !string.IsNullOrEmpty(x.ApiKeyEncrypted), MaskKey(x.ApiKeyEncrypted)))
            .ToListAsync(ct);

    public async Task<AiConnectionDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var x = await _db.AiConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (x == null) return null;
        return new AiConnectionDto(
            x.Id, x.Name, x.Provider, x.BaseUrl, x.DefaultModel,
            x.IsActive, x.IsDefault, x.TimeoutSeconds, x.Description,
            !string.IsNullOrEmpty(x.ApiKeyEncrypted), MaskKey(x.ApiKeyEncrypted));
    }

    public async Task<Result<Guid>> CreateAsync(AiConnectionUpsertDto dto, CancellationToken ct = default)
    {
        var entity = new AiConnection
        {
            Name = dto.Name.Trim(),
            Provider = dto.Provider.Trim(),
            BaseUrl = dto.BaseUrl.Trim().TrimEnd('/'),
            ApiKeyEncrypted = string.IsNullOrWhiteSpace(dto.ApiKey) ? null! : _protector.Protect(dto.ApiKey),
            DefaultModel = dto.DefaultModel.Trim(),
            IsActive = dto.IsActive,
            IsDefault = dto.IsDefault,
            TimeoutSeconds = dto.TimeoutSeconds,
            Description = dto.Description?.Trim()
        };

        if (entity.IsDefault)
            await ClearDefaultAsync(ct);

        _db.AiConnections.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(AiConnectionUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is null) return Result.Failure("Thiếu Id.");
        var entity = await _db.AiConnections.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy kết nối.");

        entity.Name = dto.Name.Trim();
        entity.Provider = dto.Provider.Trim();
        entity.BaseUrl = dto.BaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            entity.ApiKeyEncrypted = _protector.Protect(dto.ApiKey);
        entity.DefaultModel = dto.DefaultModel.Trim();
        entity.IsActive = dto.IsActive;
        entity.TimeoutSeconds = dto.TimeoutSeconds;
        entity.Description = dto.Description?.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        if (dto.IsDefault && !entity.IsDefault)
        {
            await ClearDefaultAsync(ct);
            entity.IsDefault = true;
        }
        else if (!dto.IsDefault && entity.IsDefault)
        {
            entity.IsDefault = false;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.AiConnections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy kết nối.");

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.AiConnections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy kết nối.");

        await ClearDefaultAsync(ct);
        entity.IsDefault = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> TestAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.AiConnections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy kết nối.");

        string apiKey;
        try
        {
            if (string.IsNullOrEmpty(entity.ApiKeyEncrypted))
                return Result.Failure("Kết nối chưa có API key. Hãy nhập API key rồi lưu lại.");
            apiKey = _protector.Unprotect(entity.ApiKeyEncrypted);
        }
        catch (Exception)
        {
            // Key ring DataProtection đổi (restore DB từ máy khác, xoá thư mục App_Data/keys…)
            // thì bản mã cũ không giải mã lại được — nói thẳng cách xử lý thay vì ném
            // thông báo "The payload was invalid" khó hiểu ra màn hình admin.
            return Result.Failure("Không giải mã được API key (key ring đã đổi). Hãy nhập lại API key rồi lưu.");
        }

        try
        {
            var messages = new List<(string, string)> { ("user", "ping") };
            var result = await _chatClient.CompleteAsync(
                entity.BaseUrl, apiKey, entity.DefaultModel,
                messages, Array.Empty<AiToolDefinition>(),
                null, null, entity.TimeoutSeconds, ct);

            return result.Succeeded
                ? Result.Success()
                : Result.Failure($"Test thất bại: {result.Error}");
        }
        catch (Exception ex)
        {
            return Result.Failure($"Test thất bại: {ex.Message}");
        }
    }

    private async Task ClearDefaultAsync(CancellationToken ct)
    {
        var current = await _db.AiConnections.Where(x => x.IsDefault && !x.IsDeleted).ToListAsync(ct);
        foreach (var c in current) c.IsDefault = false;
    }

    private static string MaskKey(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "****";
        return "••••••••";
    }
}
