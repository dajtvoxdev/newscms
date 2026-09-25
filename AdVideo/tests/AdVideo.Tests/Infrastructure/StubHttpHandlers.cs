using System.Net;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Máy chủ ảnh giả cho bước 1 (nạp ảnh sản phẩm).
/// </summary>
/// <remarks>
/// <para>
/// Dùng handler thay vì dựng một Kestrel thật trên cổng ngẫu nhiên: không có cổng thì không có
/// test đỏ vì cổng bận, không có firewall hỏi, và không có test nào lỡ tay gọi ra Internet.
/// </para>
/// <para>
/// <c>IngestStep</c> chỉ tin ba thứ từ phản hồi: mã trạng thái, <c>Content-Type</c> bắt đầu
/// bằng <c>image/</c>, và độ dài. Handler này điều khiển được cả ba để dựng lại đúng những ca
/// hỏng mà máy chủ của khách hay gây ra.
/// </para>
/// </remarks>
public sealed class StubImageHandler : HttpMessageHandler
{
    /// <summary>
    /// Một file PNG 1×1 hợp lệ.
    /// </summary>
    /// <remarks>
    /// Không bước nào trong Sprint 1 giải mã tấm ảnh này — nó chỉ được ghi xuống kho và biến thành
    /// một URL tham chiếu gửi cho provider. Nhúng thẳng vào code thay vì để một file nhị phân
    /// trong repo: một fixture nhị phân 70 byte là thứ không ai review được.
    /// </remarks>
    public static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Mọi URL đã được yêu cầu, theo đúng thứ tự.</summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>Mã trạng thái trả về, theo từng URL. URL không có ở đây thì trả 200.</summary>
    public Dictionary<string, HttpStatusCode> StatusOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Content-Type của phản hồi thành công.</summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>Nội dung trả về cho mọi URL thành công.</summary>
    public byte[] Content { get; set; } = OnePixelPng;

    /// <summary>
    /// Việc chạy xen vào giữa pipeline, ngay lúc bước 1 đang chờ mạng.
    /// </summary>
    /// <remarks>
    /// Có những hành vi chỉ tồn tại khi job đang chạy dở: khách bấm huỷ, người vận hành sửa trạng
    /// thái. Không có chỗ nào xen vào được thì những nhánh đó chỉ kiểm được bằng cách đặt sẵn
    /// trạng thái từ đầu — mà đó lại là một đường đi khác hẳn trong bộ chạy.
    /// </remarks>
    public Func<Uri, Task>? OnRequestAsync { get; set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri uri = request.RequestUri!;
        Requests.Add(uri);

        if (OnRequestAsync is not null)
        {
            await OnRequestAsync(uri);
        }

        if (StatusOverrides.TryGetValue(uri.ToString(), out HttpStatusCode status))
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("stub lỗi"),
                RequestMessage = request,
            };
        }

        var content = new ByteArrayContent(Content);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
            RequestMessage = request,
        };
    }
}

/// <summary>
/// Handler chặn mọi lời gọi mạng và ném ra lý do đọc được.
/// </summary>
/// <remarks>
/// <b>Test tự động không được tiêu tiền.</b> Các HttpClient có tên dùng để gọi provider thật được
/// gắn handler này trong test: nếu một ngày nào đó registry chọn nhầm một provider trả phí thay vì
/// provider giả, test sẽ đỏ với đúng một câu giải thích — chứ không phải xanh kèm một hoá đơn.
/// </remarks>
public sealed class NoNetworkHandler : HttpMessageHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        throw new InvalidOperationException(
            $"Test integration vừa cố gọi ra ngoài: {request.Method} {request.RequestUri}. " +
            "Test tự động chỉ được chạy với provider giả — một lời gọi thật ở đây là một hoá đơn.");
    }
}
