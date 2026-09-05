namespace NewsCMS.Application.Ar;

/// <summary>One scannable target inside the universal AR scanner.</summary>
public sealed record ArScannerTarget(
    int Index,
    Guid Id,
    string Slug,
    string Title,
    string VideoUrl,
    double VideoWidth,
    double VideoHeight);

/// <summary>
/// Data backing the universal scanner at <c>/ar</c>: a single merged <c>.mind</c> file
/// (all published targets) plus the index→experience map so the client knows which video
/// to play when a given target is found.
/// </summary>
public sealed record ArScannerData(
    string MasterMindUrl,
    IReadOnlyList<ArScannerTarget> Targets);

public interface IArScannerService
{
    /// <summary>
    /// Build (or reuse a cached) merged scanner over all published AR experiences.
    /// Returns <c>null</c> when nothing is published.
    /// </summary>
    Task<ArScannerData?> GetScannerDataAsync(CancellationToken ct = default);
}
