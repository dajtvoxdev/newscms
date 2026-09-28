using Microsoft.AspNetCore.Mvc;

namespace AdVideo.Api;

/// <summary>Dựng phản hồi lỗi <c>application/problem+json</c> thống nhất cho mọi endpoint.</summary>
public static class ApiProblem
{
    public static IResult Create(
        HttpContext http,
        int status,
        string title,
        string? detail,
        IDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(http);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
            Type = $"https://advideo/errors/{status}",
        };

        if (extensions is not null)
        {
            foreach (KeyValuePair<string, object?> pair in extensions)
            {
                problem.Extensions[pair.Key] = pair.Value;
            }
        }

        return Results.Problem(problem);
    }

    /// <summary>422 kèm danh sách lỗi đầy đủ — trả hết một lượt, không bắt sửa từng cái.</summary>
    public static IResult Unprocessable(HttpContext http, string title, IReadOnlyList<string> errors) =>
        Create(
            http,
            StatusCodes.Status422UnprocessableEntity,
            title,
            errors.Count == 1 ? errors[0] : $"{errors.Count} lỗi — xem danh sách errors.",
            new Dictionary<string, object?> { ["errors"] = errors });

    public static IResult NotFound(HttpContext http, string detail) =>
        Create(http, StatusCodes.Status404NotFound, "Không tìm thấy", detail);
}
