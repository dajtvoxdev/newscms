using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsCMS.Application.VideoStudio;

namespace NewsCMS.Infrastructure.VideoStudio;

/// <summary>
/// Lịch cập nhật kho prompt theo trend. Mỗi 15 phút hỏi "đến lịch chưa" — bật/tắt và chu kỳ nằm trong
/// DB (sửa ở màn hình quản trị), nên đổi cấu hình có hiệu lực không cần khởi động lại.
/// </summary>
public sealed class VideoPromptTrendWorker : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<VideoPromptTrendWorker> _logger;

    public VideoPromptTrendWorker(IServiceScopeFactory scopes, ILogger<VideoPromptTrendWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Để app khởi động xong (và migration nếu có) trước lần hỏi đầu tiên.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
                IVideoPromptTrendService trends = scope.ServiceProvider.GetRequiredService<IVideoPromptTrendService>();

                if (await trends.IsDueAsync(stoppingToken))
                {
                    var result = await trends.RefreshAsync("schedule", stoppingToken);

                    if (result.Succeeded)
                    {
                        _logger.LogInformation("Kho prompt: thêm {Added} mẫu trend, loại {Rejected}, ẩn {Expired} mẫu hết hạn.",
                            result.Value!.Added, result.Value.Rejected, result.Value.Expired);
                    }
                    else
                    {
                        _logger.LogWarning("Kho prompt: cập nhật theo trend không thành: {Error}", result.Error);
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
                _logger.LogWarning(ex, "Kho prompt: không kiểm tra được lịch cập nhật trend.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
