using AdVideo.Core.Pipeline;
using AdVideo.Worker.Steps;

namespace AdVideo.Worker.Jobs;

/// <summary>
/// Đăng ký toàn bộ pipeline 9 bước cùng hai HttpClient có tên mà pipeline dùng.
/// </summary>
/// <remarks>
/// <para>
/// Tách ra khỏi <c>Program.cs</c> để worker thật và test integration dùng CHUNG một chỗ đăng ký.
/// Nếu test tự liệt kê lại các bước thì nó đang kiểm tra bản sao của chính nó: thêm một bước vào
/// worker mà quên thêm vào test thì test vẫn xanh, và bước mới đó chưa từng chạy lần nào trước
/// khi lên production.
/// </para>
/// <para>
/// Thứ tự gọi <c>AddScoped</c> ở đây KHÔNG quyết định thứ tự chạy —
/// <see cref="AdVideoJobRunner"/> sắp theo <see cref="IPipelineStep.Order"/>. Viết theo đúng thứ
/// tự số để người đọc thấy được pipeline, nhưng đừng tin vào nó khi sửa code.
/// </para>
/// </remarks>
public static class WorkerPipeline
{
    /// <summary>Đăng ký các bước pipeline, kho hiện vật và bộ chạy job.</summary>
    public static IServiceCollection AddAdVideoPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Hai timeout khác nhau có chủ đích: tải một tấm ảnh mà quá 2 phút thì là hỏng, còn tải
        // clip từ hàng đợi của provider thì 10 phút vẫn là bình thường.
        services.AddHttpClient(IngestStep.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        // Client tải clip KHÔNG mang header xác thực nào: URL clip do provider trả về, và key không
        // được đi theo tới host đó. Vẫn bị chặn theo allowlist như client gọi provider.
        services.AddHttpClient(RenderShotsStep.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
            })
            .AddHttpMessageHandler<AdVideo.Infrastructure.Providers.SsrfGuardingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddScoped<JobArtifacts>();

        services.AddScoped<IPipelineStep, IngestStep>();
        services.AddScoped<IPipelineStep, TtsStep>();
        services.AddScoped<IPipelineStep, LockTimelineStep>();
        services.AddScoped<IPipelineStep, RenderShotsStep>();
        services.AddScoped<IPipelineStep, ComposeStep>();
        services.AddScoped<IPipelineStep, QcStep>();

        services.AddScoped<IAdVideoJobRunner, AdVideoJobRunner>();

        return services;
    }
}
