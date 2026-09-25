using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using AdVideo.Infrastructure.Persistence.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AdVideo.Infrastructure.Persistence.Stores;

/// <summary>
/// Prompt lấy từ DB, có phiên bản (D10). Sửa prompt là thao tác nội dung, không phải deploy.
/// </summary>
/// <remarks>
/// <b>Bản cũ không bao giờ bị sửa đè.</b> Khi một video ra kết quả lạ, câu hỏi đầu tiên luôn là
/// "lúc đó prompt nào đang chạy". Sửa đè là mất câu trả lời đó vĩnh viễn, nên mọi thay đổi đều
/// tạo bản mới và việc bật bản nào là một hành động riêng.
/// </remarks>
public sealed class DbPromptStore : IPromptStore
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly AdVideoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly StoreCacheSignal<DbPromptStore> _signal;
    private readonly ILogger<DbPromptStore> _logger;

    public DbPromptStore(
        AdVideoDbContext db,
        IMemoryCache cache,
        StoreCacheSignal<DbPromptStore> signal,
        ILogger<DbPromptStore> logger)
    {
        _db = db;
        _cache = cache;
        _signal = signal;
        _logger = logger;
    }

    /// <remarks>
    /// Không cache: trả về entity thì bên gọi có thể sửa, mà một instance nằm trong cache singleton
    /// bị sửa từ một request là dữ liệu bẩn cho mọi request sau. Đường nóng của pipeline dùng
    /// <see cref="GetActiveContentAsync"/> (chỉ trả chuỗi) nên mất cache ở đây không ảnh hưởng.
    /// </remarks>
    public async Task<PromptTemplate?> GetActiveAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _db.PromptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code && x.IsActive, cancellationToken);
    }

    public async Task<string> GetActiveContentAsync(
        string code,
        string fallback,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"advideo:prompt:{code}";

        string? content = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.AddExpirationToken(_signal.Token);

            return await _db.PromptTemplates
                .AsNoTracking()
                .Where(x => x.Code == code && x.IsActive)
                .Select(x => x.Content)
                .FirstOrDefaultAsync(cancellationToken);
        });

        if (content is not null)
        {
            return content;
        }

        // Chạy bằng fallback nghĩa là seed chưa chạy hoặc ai đó vừa tắt bản cuối cùng của code này.
        // Hệ thống vẫn chạy được, nhưng chạy bằng prompt trong mã nguồn thay vì prompt đã duyệt —
        // im lặng ở đây là để người vận hành sửa prompt trong DB mà không hiểu vì sao không ăn.
        _logger.LogWarning(
            "Không có prompt đang hoạt động cho code {Code}; dùng nội dung mặc định trong mã nguồn. Kiểm tra bảng PromptTemplates.",
            code);

        return fallback;
    }

    public async Task<IReadOnlyList<PromptTemplate>> ListActiveAsync(
        PromptKind kind,
        CancellationToken cancellationToken = default)
    {
        return await _db.PromptTemplates
            .AsNoTracking()
            .Where(x => x.Kind == kind && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
    }

    /// <remarks>
    /// Bản mới tạo ra ở trạng thái TẮT. Thêm và bật là hai việc khác nhau: thêm là soạn thảo, bật
    /// là quyết định cho chạy vào job thật đang tiêu tiền. Gộp lại thì không còn chỗ nào để xem
    /// lại trước khi nó có hiệu lực.
    /// </remarks>
    public async Task<PromptTemplate> AddVersionAsync(
        string code,
        PromptKind kind,
        string content,
        string changeNote,
        string? formatCode = null,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters để bản đã xoá mềm vẫn tính vào số thứ tự: dùng lại số cũ sẽ làm
        // unique index (Code, Version) nổ, và tệ hơn là làm hai nội dung khác nhau mang cùng một
        // số hiệu trong nhật ký.
        int lastVersion = await _db.PromptTemplates
            .IgnoreQueryFilters()
            .Where(x => x.Code == code)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var template = new PromptTemplate
        {
            Code = code,
            Version = lastVersion + 1,
            Kind = kind,
            Content = content,
            ChangeNote = changeNote,
            FormatCode = formatCode,
            IsActive = false,
        };

        _db.PromptTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        return template;
    }

    public async Task ActivateVersionAsync(
        string code,
        int version,
        CancellationToken cancellationToken = default)
    {
        PromptTemplate target = await _db.PromptTemplates
            .FirstOrDefaultAsync(x => x.Code == code && x.Version == version, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy prompt {code} bản {version} để bật.");

        List<PromptTemplate> currentlyActive = await _db.PromptTemplates
            .Where(x => x.Code == code && x.IsActive && x.Version != version)
            .ToListAsync(cancellationToken);

        // Hai lần SaveChanges trong MỘT transaction, tắt trước rồi mới bật. Gộp vào một lần
        // SaveChanges thì EF tự chọn thứ tự UPDATE, và nếu nó bật trước khi tắt thì unique index
        // lọc (Code) WHERE IsActive = 1 chặn ngay — lỗi chỉ xuất hiện trên SQL Server thật, còn
        // test in-memory thì không bao giờ thấy.
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        foreach (PromptTemplate item in currentlyActive)
        {
            item.IsActive = false;
        }

        await _db.SaveChangesAsync(cancellationToken);

        target.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        Invalidate();
    }

    public void Invalidate() => _signal.Reset();

    /// <summary>
    /// Mở transaction khi provider hỗ trợ, trả null khi không.
    /// </summary>
    /// <remarks>
    /// Provider in-memory dùng trong test không có transaction và sẽ ném lỗi nếu gọi thẳng. Nhánh
    /// này có mặt để test chạy được, KHÔNG phải để nới lỏng ràng buộc khi chạy thật: với SQL Server
    /// thì luôn có transaction.
    /// </remarks>
    private async Task<IAsyncDisposableTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            return null;
        }

        return new EfTransaction(await _db.Database.BeginTransactionAsync(cancellationToken));
    }

    private interface IAsyncDisposableTransaction : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken);
    }

    private sealed class EfTransaction : IAsyncDisposableTransaction
    {
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _inner;

        public EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner)
            => _inner = inner;

        public Task CommitAsync(CancellationToken cancellationToken) => _inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
