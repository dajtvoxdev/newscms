using Microsoft.EntityFrameworkCore;
using NewsCMS.Application.Common;
using NewsCMS.Application.Engagement;
using NewsCMS.Application.Engagement.Dtos;
using NewsCMS.Domain.Entities.Engagement;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace NewsCMS.Infrastructure.Engagement;

public sealed class CommitmentService : ICommitmentService
{
    private const int MaxSignatureWidth = 800;
    private const int MaxSignatureHeight = 400;
    private const string SubFolder = "commitments";

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;

    public CommitmentService(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Result<Guid>> CreateAsync(
        CommitmentCreateDto dto,
        string signerKey,
        string ipHash,
        string userAgent,
        CancellationToken ct = default)
    {
        var alreadySigned = await _db.Commitments
            .AnyAsync(x => x.SignerKey == signerKey || x.IpHash == ipHash, ct);
        if (alreadySigned)
        {
            return Result<Guid>.Failure("Bạn đã ký cam kết rồi. Mỗi người chỉ ký một lần.");
        }

        var kind = string.Equals(dto.SignatureKind, "Uploaded", StringComparison.OrdinalIgnoreCase)
            ? CommitmentSignatureKind.Uploaded
            : CommitmentSignatureKind.Drawn;

        string signatureUrl;
        try
        {
            signatureUrl = await PersistSignatureAsync(kind, dto, ct);
        }
        catch (UnknownImageFormatException)
        {
            return Result<Guid>.Failure("Ảnh chữ ký không hợp lệ.");
        }
        catch (InvalidImageContentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        var entity = new Commitment
        {
            DisplayName = dto.DisplayName.Trim(),
            Message = dto.Message.Trim(),
            SignatureKind = kind,
            SignatureUrl = signatureUrl,
            SignerKey = signerKey,
            IpHash = ipHash,
            UserAgent = Truncate(userAgent, 256),
            IsHidden = false
        };

        _db.Commitments.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index race on SignerKey
            return Result<Guid>.Failure("Bạn đã ký cam kết rồi.");
        }

        return Result<Guid>.Success(entity.Id);
    }

    public async Task<bool> HasSignedAsync(string signerKey, string ipHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signerKey) && string.IsNullOrWhiteSpace(ipHash)) return false;
        return await _db.Commitments
            .AnyAsync(x => x.SignerKey == signerKey || x.IpHash == ipHash, ct);
    }

    public async Task<IReadOnlyList<CommitmentWallItemDto>> GetWallAsync(int take = 200, CancellationToken ct = default)
    {
        var items = await _db.Commitments
            .AsNoTracking()
            .Where(x => !x.IsHidden)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new CommitmentWallItemDto(x.Id, x.DisplayName, x.Message, x.SignatureUrl, x.CreatedAt))
            .ToListAsync(ct);

        return items
            .Select(x => x with { SignatureUrl = NormalizeSignatureUrl(x.SignatureUrl) })
            .ToList();
    }

    public async Task<IReadOnlyList<CommitmentWallItemDto>> GetWallSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var items = await _db.Commitments
            .AsNoTracking()
            .Where(x => !x.IsHidden && x.CreatedAt > sinceUtc)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new CommitmentWallItemDto(x.Id, x.DisplayName, x.Message, x.SignatureUrl, x.CreatedAt))
            .ToListAsync(ct);

        return items
            .Select(x => x with { SignatureUrl = NormalizeSignatureUrl(x.SignatureUrl) })
            .ToList();
    }

    public async Task<PagedList<CommitmentAdminItemDto>> GetForAdminAsync(string? keyword, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var query = _db.Commitments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => EF.Functions.Like(x.DisplayName, $"%{term}%")
                || EF.Functions.Like(x.Message, $"%{term}%"));
        }
        query = query.OrderByDescending(x => x.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CommitmentAdminItemDto(
                x.Id,
                x.DisplayName,
                x.Message,
                x.SignatureUrl,
                x.SignatureKind.ToString(),
                x.IpHash,
                x.IsHidden,
                x.CreatedAt))
            .ToListAsync(ct);

        return new PagedList<CommitmentAdminItemDto>
        {
            Items = items
                .Select(x => x with { SignatureUrl = NormalizeSignatureUrl(x.SignatureUrl) })
                .ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total
        };
    }

    public async Task<Result> ToggleHiddenAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Commitments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Result.Failure("Không tìm thấy cam kết.");
        entity.IsHidden = !entity.IsHidden;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<int> GetTotalCountAsync(CancellationToken ct = default) =>
        _db.Commitments.CountAsync(x => !x.IsHidden, ct);

    private async Task<string> PersistSignatureAsync(
        CommitmentSignatureKind kind,
        CommitmentCreateDto dto,
        CancellationToken ct)
    {
        byte[] rawBytes;
        if (kind == CommitmentSignatureKind.Drawn)
        {
            if (string.IsNullOrWhiteSpace(dto.SignatureBase64))
                throw new InvalidImageContentException("Thiếu chữ ký vẽ.");
            const string prefix = "data:image/png;base64,";
            var payload = dto.SignatureBase64.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? dto.SignatureBase64[prefix.Length..]
                : dto.SignatureBase64;
            try
            {
                rawBytes = Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                throw new InvalidImageContentException("Chữ ký không phải base64 hợp lệ.");
            }
        }
        else
        {
            if (dto.UploadedImageStream is null)
                throw new InvalidImageContentException("Thiếu ảnh chữ ký.");

            using var ms = new MemoryStream();
            await dto.UploadedImageStream.CopyToAsync(ms, ct);
            rawBytes = ms.ToArray();
        }

        if (rawBytes.Length == 0)
            throw new InvalidImageContentException("Ảnh chữ ký rỗng.");

        // Re-encode to PNG to strip EXIF and enforce dimensions. Throws UnknownImageFormatException on bad input.
        using var input = new MemoryStream(rawBytes);
        using var image = await Image.LoadAsync(input, ct);

        if (image.Width > MaxSignatureWidth || image.Height > MaxSignatureHeight)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxSignatureWidth, MaxSignatureHeight),
                Mode = ResizeMode.Max
            }));
        }

        using var output = new MemoryStream();
        await image.SaveAsync(output, new PngEncoder(), ct);
        output.Position = 0;

        var fileName = $"{Guid.NewGuid():N}.png";
        var key = await _storage.SaveAsync(output, fileName, SubFolder, ct);
        return _storage.GetPublicUrl(key);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private string NormalizeSignatureUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            return "/" + trimmed;

        return _storage.GetPublicUrl(trimmed);
    }
}

internal sealed class InvalidImageContentException : Exception
{
    public InvalidImageContentException(string message) : base(message) { }
}
