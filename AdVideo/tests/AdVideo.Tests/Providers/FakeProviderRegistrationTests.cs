using AdVideo.Core.Providers;
using AdVideo.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdVideo.Tests.Providers;

/// <summary>
/// L5 — provider giả không bao giờ được vào danh sách provider trên production.
/// </summary>
public class FakeProviderRegistrationTests
{
    private static IConfiguration Config(string? fakeEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AdVideoDb"] = "Server=khong-dung-toi;Database=khong-dung-toi;",
            ["AdVideo:Storage:Provider"] = "LocalDisk",
        };

        if (fakeEnabled is not null)
        {
            values["AdVideo:FakeProviders:Enabled"] = fakeEnabled;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Thieu_section_thi_provider_gia_mac_dinh_tat()
    {
        var services = new ServiceCollection();

        services.AddAdVideoInfrastructure(Config(fakeEnabled: null), "Development");

        services.Should().NotContain(d => d.ServiceType == typeof(IVideoProvider));
        services.Should().NotContain(d => d.ServiceType == typeof(ITtsProvider));
    }

    [Fact]
    public void Bat_provider_gia_tren_production_thi_host_tu_choi_khoi_dong()
    {
        var services = new ServiceCollection();

        FluentActions.Invoking(() => services.AddAdVideoInfrastructure(Config("true"), "Production"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Production*");
    }

    [Fact]
    public void Bat_tuong_minh_ngoai_production_thi_duoc_dang_ky()
    {
        var services = new ServiceCollection();

        services.AddAdVideoInfrastructure(Config("true"), "Development");

        services.Should().Contain(d => d.ServiceType == typeof(IVideoProvider));
    }
}
