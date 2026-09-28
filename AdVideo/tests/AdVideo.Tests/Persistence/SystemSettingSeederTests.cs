using AdVideo.Core.Entities;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Persistence.Seeding;
using FluentAssertions;

namespace AdVideo.Tests.Persistence;

/// <summary>
/// I1 — mặc định seed của <c>ProviderHostAllowlist</c> không được chứa host nội bộ.
/// </summary>
/// <remarks>
/// <para>
/// Allowlist là hàng rào SSRF: JSON dán vào DB không được phép gọi vào mạng nội bộ. Seed sẵn một
/// host loopback là mở sẵn đường đó cho <b>mọi</b> bản cài — kể cả máy không hề chạy engine tự host.
/// Ai chạy VieNeu (<c>127.0.0.1:8080</c>) thì tự thêm host bằng <c>set-setting</c>; allowlist đóng
/// khi lỗi nên bỏ mục này chỉ ảnh hưởng đúng máy đó.
/// </para>
/// <para>
/// Đọc thẳng <see cref="SystemSettingSeeder.Definitions"/> nên không cần DB — đây là kiểm tra giá
/// trị seed, không phải kiểm tra <c>SeedAsync</c> ghi được vào bảng.
/// </para>
/// </remarks>
public class SystemSettingSeederTests
{
    private static string SeededAllowlist => SystemSettingSeeder.Definitions
        .Single(seed => seed.Key == SettingKeys.ProviderHostAllowlist)
        .Value;

    [Theory]
    [InlineData("127.")]
    [InlineData("localhost")]
    [InlineData("http://")]
    public void Allowlist_mac_dinh_khong_chua_host_noi_bo(string cam)
    {
        SeededAllowlist.Should().NotContain(cam);
    }

    [Fact]
    public void Allowlist_mac_dinh_van_goi_duoc_provider_ngoai()
    {
        // Không có mục nội bộ là điều kiện cần, chưa đủ: một allowlist rỗng cũng thoả điều đó
        // nhưng chặn hết mọi provider. Mặc định vẫn phải gọi được provider thật.
        ProviderHostAllowlist allowlist = ProviderHostAllowlist.Parse(SeededAllowlist);

        allowlist.InvalidEntries.Should().BeEmpty();
        allowlist.IsAllowed(new Uri("https://queue.fal.run/fal-ai/kling-video/v3")).Should().BeTrue();
    }

    [Fact]
    public void Allowlist_mac_dinh_chan_loopback()
    {
        ProviderHostAllowlist allowlist = ProviderHostAllowlist.Parse(SeededAllowlist);

        allowlist.IsAllowed(new Uri("http://127.0.0.1:8080/tts")).Should().BeFalse();
        allowlist.IsAllowed(new Uri("http://localhost:8080/tts")).Should().BeFalse();
    }
}
