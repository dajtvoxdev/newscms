using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using NewsCMS.Application.Ar;
using NewsCMS.Infrastructure.Persistence;

namespace NewsCMS.Infrastructure.Ar;

/// <summary>
/// Builds the universal AR scanner: merges every published experience's <c>.mind</c> into a
/// single multi-target file (cached on disk) and returns the index→experience map. The merged
/// file is regenerated automatically whenever the set of published experiences changes
/// (detected via a lightweight signature), so it is always in sync without a manual rebuild.
/// </summary>
public sealed class ArScannerService : IArScannerService
{
    private const string MasterKey = "ar-experiences/_scanner/master.mind";
    private static readonly SemaphoreSlim BuildLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ILogger<ArScannerService> _logger;
    private readonly string _storageRoot;
    private readonly string _urlPrefix;

    public ArScannerService(
        AppDbContext db,
        IConfiguration cfg,
        IWebHostEnvironment env,
        ILogger<ArScannerService> logger)
    {
        _db = db;
        _logger = logger;
        _storageRoot = ResolveStoragePath(env, cfg["Storage:LocalRoot"]);
        _urlPrefix = cfg["Storage:UrlPrefix"] ?? "/uploads";
    }

    public async Task<ArScannerData?> GetScannerDataAsync(CancellationToken ct = default)
    {
        var published = await _db.ArExperiences
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id, x.Slug, x.Title, x.VideoUrl, x.MindFileUrl,
                x.VideoWidth, x.VideoHeight, x.UpdatedAt, x.CreatedAt
            })
            .ToListAsync(ct);

        if (published.Count == 0) return null;

        var readable = published.Where(p =>
        {
            var path = ResolvePhysicalPath(p.MindFileUrl);
            if (File.Exists(path)) return true;

            _logger.LogWarning("AR scanner: .mind file missing for {Url}, skipping.", p.MindFileUrl);
            return false;
        }).ToList();

        // Do not fail the public /ar page when uploads were removed or a deployment
        // is missing its AR files. The page can show its normal empty-state instead.
        if (readable.Count == 0) return null;

        var signature = string.Join('|', readable.Select(p =>
            $"{p.Id:N}:{(p.UpdatedAt ?? p.CreatedAt).Ticks}"));

        var masterPath = Path.Combine(_storageRoot, MasterKey);
        var sigPath = masterPath + ".sig";
        var masterUrl = $"{_urlPrefix}/{MasterKey}";

        if (!IsCacheFresh(masterPath, sigPath, signature))
            await RebuildAsync(masterPath, sigPath, signature, readable.Select(p => p.MindFileUrl).ToList(), ct);

        // Map target indices to experiences in the same order they were merged.
        var targets = new List<ArScannerTarget>();
        int index = 0;
        foreach (var p in readable)
        {
            var mindPath = ResolvePhysicalPath(p.MindFileUrl);
            int count = 1;
            if (File.Exists(mindPath))
            {
                try { count = Math.Max(1, MindFileMerger.CountTargets(await File.ReadAllBytesAsync(mindPath, ct))); }
                catch { count = 1; }
            }
            for (int k = 0; k < count; k++)
                targets.Add(new ArScannerTarget(index++, p.Id, p.Slug, p.Title, p.VideoUrl, p.VideoWidth, p.VideoHeight));
        }

        return new ArScannerData(masterUrl, targets);
    }

    private static bool IsCacheFresh(string masterPath, string sigPath, string signature)
    {
        if (!File.Exists(masterPath) || !File.Exists(sigPath)) return false;
        try { return File.ReadAllText(sigPath) == signature; }
        catch { return false; }
    }

    private async Task RebuildAsync(
        string masterPath, string sigPath, string signature,
        IReadOnlyList<string> mindUrls, CancellationToken ct)
    {
        await BuildLock.WaitAsync(ct);
        try
        {
            // Re-check after acquiring the lock — another request may have just rebuilt it.
            if (IsCacheFresh(masterPath, sigPath, signature)) return;

            var minds = new List<byte[]>();
            foreach (var url in mindUrls)
            {
                var path = ResolvePhysicalPath(url);
                if (!File.Exists(path))
                {
                    _logger.LogWarning("AR scanner: .mind file missing for {Url}, skipping.", url);
                    continue;
                }
                minds.Add(await File.ReadAllBytesAsync(path, ct));
            }

            if (minds.Count == 0) return;

            var merged = MindFileMerger.Merge(minds);
            Directory.CreateDirectory(Path.GetDirectoryName(masterPath)!);
            await File.WriteAllBytesAsync(masterPath, merged, ct);
            await File.WriteAllTextAsync(sigPath, signature, ct);
            _logger.LogInformation("AR scanner rebuilt: {Count} target file(s), {Bytes} bytes.", minds.Count, merged.Length);
        }
        finally
        {
            BuildLock.Release();
        }
    }

    /// <summary>Map a stored public URL ("/uploads/ar-experiences/..") back to a physical path.</summary>
    private string ResolvePhysicalPath(string publicUrl)
    {
        var rel = publicUrl;
        if (rel.StartsWith(_urlPrefix, StringComparison.OrdinalIgnoreCase))
            rel = rel[_urlPrefix.Length..];
        rel = rel.TrimStart('/');
        return Path.Combine(_storageRoot, rel);
    }

    private static string ResolveStoragePath(IWebHostEnvironment env, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads"));

        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var normalized = configuredPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return normalized.StartsWith($"wwwroot{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(env.WebRootPath) ?? env.ContentRootPath, normalized))
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, normalized));
    }
}
