namespace NewsCMS.Application.Common;

public class Result
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public static Result Success() => new() { Succeeded = true };
    public static Result Failure(string error) => new() { Succeeded = false, Error = error };
    public static Result Failure(IReadOnlyDictionary<string, string[]> errors)
        => new() { Succeeded = false, Errors = errors };
}

public class Result<T> : Result
{
    public T? Value { get; init; }
    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };
    public new static Result<T> Failure(string error) => new() { Succeeded = false, Error = error };
}

public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedList<T> Empty(int page = 1, int pageSize = 20)
        => new() { Page = page, PageSize = pageSize };
}
