using System.Text.Json;

namespace AdVideo.Infrastructure.Providers;

/// <summary>Dò dữ liệu trong phản hồi JSON của provider mà không phụ thuộc vào hình dạng cố định.</summary>
/// <remarks>
/// <para>
/// <b>Vì sao dò đệ quy thay vì đọc theo đường dẫn cứng:</b> mỗi model trên fal.ai trả một hình
/// dạng khác nhau (<c>video.url</c>, <c>videos[0].url</c>, <c>output.video_url</c>), và chúng đổi
/// giữa các phiên bản model mà không báo trước. Đọc theo đường dẫn cứng nghĩa là mỗi lần nhà cung
/// cấp đổi tên một trường thì phải sửa mã và deploy lại — trong khi thứ ta cần chỉ là "cái URL
/// video ở đâu đó trong phản hồi này".
/// </para>
/// <para>
/// <b>Giá phải trả:</b> nếu phản hồi có nhiều URL thì hàm này trả về cái gặp đầu tiên theo thứ tự
/// duyệt. Nên ưu tiên khoá có tên gợi ý video trước, rồi mới tới URL bất kỳ.
/// </para>
/// </remarks>
public static class ProviderJson
{
    private static readonly string[] PreferredKeys =
    [
        "video_url",
        "videoUri",
        "video_uri",
        "url",
        "uri",
        "signed_url",
    ];

    private static readonly string[] MediaExtensions = [".mp4", ".webm", ".mov", ".m4v"];

    /// <summary>Tìm URL video trong phản hồi. Ưu tiên URL trỏ tới file media thật.</summary>
    public static Uri? FindVideoUrl(JsonElement root)
    {
        List<string> candidates = [];

        Collect(root, candidates, depth: 0);

        // Vòng một: URL có đuôi file media — chắc chắn nhất.
        foreach (string candidate in candidates)
        {
            if (MediaExtensions.Any(ext => candidate.Contains(ext, StringComparison.OrdinalIgnoreCase))
                && Uri.TryCreate(candidate, UriKind.Absolute, out Uri? mediaUri))
            {
                return mediaUri;
            }
        }

        // Vòng hai: URL bất kỳ. Một số provider trả link đã ký, không có đuôi file nào trong đường dẫn.
        foreach (string candidate in candidates)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https")
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>Đọc một chuỗi theo tên khoá, ở bất kỳ độ sâu nào.</summary>
    public static string? FindString(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                string? nested = FindString(property.Value, propertyName);

                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                string? nested = FindString(item, propertyName);

                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static void Collect(JsonElement element, List<string> candidates, int depth)
    {
        // Giới hạn độ sâu để một phản hồi dị thường không làm tràn ngăn xếp. 12 cấp đã sâu hơn
        // mọi phản hồi provider từng gặp.
        if (depth > 12)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // Duyệt khoá ưu tiên trước để URL video không bị một URL thumbnail chen lên trước.
                foreach (string key in PreferredKeys)
                {
                    if (element.TryGetProperty(key, out JsonElement preferred)
                        && preferred.ValueKind == JsonValueKind.String)
                    {
                        AddIfUrl(preferred.GetString(), candidates);
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        AddIfUrl(property.Value.GetString(), candidates);
                    }
                    else
                    {
                        Collect(property.Value, candidates, depth + 1);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, candidates, depth + 1);
                }

                break;
        }
    }

    private static void AddIfUrl(string? value, List<string> candidates)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !candidates.Contains(value))
        {
            candidates.Add(value);
        }
    }
}
