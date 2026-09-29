using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.ImageStudio;

namespace NewsCMS.Infrastructure.ImageStudio;

/// <summary>
/// Lịch cập nhật kho mẫu ảnh theo trend. Mỗi 15 phút hỏi "đến lịch chưa" — bật/tắt và chu kỳ nằm trong DB,
/// đổi cấu hình có hiệu lực không cần khởi động lại. Giống <c>VideoPromptTrendWorker</c>.
/// </summary>
public sealed class ImagePromptTrendWorker : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ImagePromptTrendWorker> _logger;

    public ImagePromptTrendWorker(IServiceScopeFactory scopes, ILogger<ImagePromptTrendWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Để app khởi động xong trước lần hỏi đầu tiên; lệch 2 phút với kho video để hai lần gọi AI không trùng giờ.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(CheckEvery);

        do
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                IImagePromptTrendService trends = scope.ServiceProvider.GetRequiredService<IImagePromptTrendService>();

                if (await trends.IsDueAsync(stoppingToken))
                {
                    var result = await trends.RefreshAsync(ImagePromptTrendService.ScheduleTrigger, stoppingToken);

                    if (result.Succeeded)
                    {
                        _logger.LogInformation("Kho mẫu ảnh: thêm {Added} mẫu trend, loại {Rejected}, ẩn {Expired} mẫu hết hạn, tạo {Demos} ảnh demo.",
                            result.Value!.Added, result.Value.Rejected, result.Value.Expired, result.Value.DemosCreated);
                    }
                    else
                    {
                        _logger.LogWarning("Kho mẫu ảnh: cập nhật theo trend không thành: {Error}", result.Error);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Bảng chưa có (chưa migrate) hay DB tạm mất — thử lại lượt sau, không làm sập web.
                _logger.LogWarning(ex, "Kho mẫu ảnh: không kiểm tra được lịch cập nhật trend.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
