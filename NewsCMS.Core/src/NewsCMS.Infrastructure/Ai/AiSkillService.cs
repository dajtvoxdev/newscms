using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Ai;
using NewsCMS.Application.Ai.Dtos;
using NewsCMS.Application.Common;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ai;

public sealed class AiSkillService : IAiSkillService
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;

    public AiSkillService(AppDbContext db, IDataProtectionProvider dp)
    {
        _db = db;
        _protector = dp.CreateProtector("NewsCMS.Ai.ApiKey");
    }

    public async Task<IReadOnlyList<AiSkillDto>> GetAllAsync(CancellationToken ct = default) =>
        await _db.AiSkills.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => MapToDto(x))
            .ToListAsync(ct);

    public async Task<AiSkillDto?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        var x = await _db.AiSkills.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return x == null ? null : MapToDto(x);
    }

    public async Task<IReadOnlyList<AiSkillDto>> GetPromptSkillsForTargetAsync(string target, CancellationToken ct = default) =>
        await _db.AiSkills.AsNoTracking()
            .Where(x => x.Kind == AiSkillKind.Prompt && x.IsActive && x.Targets != null && x.Targets.Contains(target))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => MapToDto(x))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AiSkillDto>> GetActiveToolSkillsAsync(CancellationToken ct = default) =>
        await _db.AiSkills.AsNoTracking()
            .Where(x => x.Kind == AiSkillKind.Tool && x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => MapToDto(x))
            .ToListAsync(ct);

    public async Task<Result<Guid>> CreateAsync(AiSkillUpsertDto dto, CancellationToken ct = default)
    {
        if (await _db.AiSkills.AnyAsync(x => x.Key == dto.Key, ct))
            return Result<Guid>.Failure("Key đã tồn tại.");

        var entity = new AiSkill
        {
            Key = dto.Key.Trim().ToLowerInvariant(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Kind = dto.Kind,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            SystemPrompt = dto.SystemPrompt,
            UserPromptTemplate = dto.UserPromptTemplate,
            AllowStyled = dto.AllowStyled,
            Temperature = dto.Temperature,
            MaxTokens = dto.MaxTokens,
            Targets = dto.Targets,
            UseTools = dto.UseTools,
            ToolType = dto.ToolType,
            BaseUrl = dto.BaseUrl?.Trim(),
            ApiKeyEncrypted = string.IsNullOrWhiteSpace(dto.ApiKey) ? null : _protector.Protect(dto.ApiKey),
            ConfigJson = dto.ConfigJson
        };

        _db.AiSkills.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(AiSkillUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Id is null) return Result.Failure("Thiếu Id.");
        var entity = await _db.AiSkills.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy skill.");

        if (await _db.AiSkills.AnyAsync(x => x.Key == dto.Key && x.Id != dto.Id, ct))
            return Result.Failure("Key đã tồn tại.");

        entity.Key = dto.Key.Trim().ToLowerInvariant();
        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.Kind = dto.Kind;
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.SystemPrompt = dto.SystemPrompt;
        entity.UserPromptTemplate = dto.UserPromptTemplate;
        entity.AllowStyled = dto.AllowStyled;
        entity.Temperature = dto.Temperature;
        entity.MaxTokens = dto.MaxTokens;
        entity.Targets = dto.Targets;
        entity.UseTools = dto.UseTools;
        entity.ToolType = dto.ToolType;
        entity.BaseUrl = dto.BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            entity.ApiKeyEncrypted = _protector.Protect(dto.ApiKey);
        entity.ConfigJson = dto.ConfigJson;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.AiSkills.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy skill.");

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static AiSkillDto MapToDto(AiSkill x) => new(
        x.Id, x.Key, x.Name, x.Description, x.Kind, x.IsActive, x.SortOrder,
        x.SystemPrompt, x.UserPromptTemplate, x.AllowStyled, x.Temperature, x.MaxTokens,
        x.Targets, x.UseTools, x.ToolType, x.BaseUrl,
        !string.IsNullOrEmpty(x.ApiKeyEncrypted), MaskKey(x.ApiKeyEncrypted), x.ConfigJson);

    // Static check only — no decrypt at display time (was a key-ring decrypt oracle)
    private static string MaskKey(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        return "••••••••";
    }
}
