namespace NewsCMS.Infrastructure.Storage;

/// <summary>
/// Provider-agnostic file storage seam.
/// Returns host-independent storage keys; resolves public URLs via GetPublicUrl.
/// Swap backends by changing Storage:Provider (Local, Cloud, ...).
/// Supports trash: files soft-deleted are moved to a non-served dir so URLs 404;
/// restore moves them back. Purge removes from either root.
/// </summary>
public interface IFileStorage
{
    /// <summary>Save a file. Returns the storage key (e.g. "images/2026/06/{guid}.ext").</summary>
    Task<string> SaveAsync(Stream content, string originalFileName, string subFolder = "general", CancellationToken ct = default);

    /// <summary>Resolve a public URL for the given storage key.</summary>
    string GetPublicUrl(string key);

    /// <summary>
    /// Đường dẫn vật lý tương ứng với storage key (chỉ có nghĩa với backend local).
    /// Dùng cho các tác vụ cần ghi đè/xử lý file sau khi lưu (vd nén ảnh).
    /// </summary>
    string GetLocalPath(string key);

    /// <summary>Move a live file to trash so its public URL stops serving.</summary>
    Task MoveToTrashAsync(string key, CancellationToken ct = default);

    /// <summary>Restore a trashed file back to live.</summary>
    Task RestoreFromTrashAsync(string key, CancellationToken ct = default);

    /// <summary>Permanently delete from live or trash.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _trashRoot;
    private readonly string _urlPrefix;
    private readonly string _publicBaseUrl;

    public LocalFileStorage(string root, string trashRoot = "App_Data/media-trash", string urlPrefix = "/uploads", string publicBaseUrl = "")
    {
        _root = root;
        _trashRoot = trashRoot;
        _urlPrefix = urlPrefix;
        _publicBaseUrl = publicBaseUrl;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, string subFolder = "general", CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        var name = $"{Guid.NewGuid():N}{ext}";
        var key = $"{subFolder}/{DateTime.UtcNow:yyyy/MM}/{name}";
        var folder = Path.Combine(_root, Path.GetDirectoryName(key)!);
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(_root, key);

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public string GetPublicUrl(string key) => $"{_publicBaseUrl}{_urlPrefix}/{key}";

    public string GetLocalPath(string key) => Path.Combine(_root, key);

    public Task MoveToTrashAsync(string key, CancellationToken ct = default)
    {
        Move(_root, _trashRoot, key);
        return Task.CompletedTask;
    }

    public Task RestoreFromTrashAsync(string key, CancellationToken ct = default)
    {
        Move(_trashRoot, _root, key);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        TryDelete(_root, key);
        TryDelete(_trashRoot, key);
        return Task.CompletedTask;
    }

    private static void Move(string fromRoot, string toRoot, string key)
    {
        var from = Path.Combine(fromRoot, key);
        var to = Path.Combine(toRoot, key);
        if (!File.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: true);
    }

    private static void TryDelete(string root, string key)
    {
        var path = Path.Combine(root, key);
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>
/// Placeholder for future object-storage providers (S3/Azure/R2/...).
/// Wires in DI as a no-op so the seam is exercised; replace with a real impl later.
/// </summary>
public class CloudFileStorage : IFileStorage
{
    public Task<string> SaveAsync(Stream content, string originalFileName, string subFolder = "general", CancellationToken ct = default)
        => throw new NotImplementedException("Cloud storage provider not yet configured. Implement a concrete IFileStorage and register via Storage:Provider.");

    public string GetPublicUrl(string key)
        => throw new NotImplementedException("Cloud storage provider not yet configured.");

    public string GetLocalPath(string key)
        => throw new NotImplementedException("Cloud storage provider not yet configured.");

    public Task MoveToTrashAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("Cloud storage provider not yet configured.");

    public Task RestoreFromTrashAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("Cloud storage provider not yet configured.");

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("Cloud storage provider not yet configured.");
}
