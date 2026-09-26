using AdVideo.Core.Providers;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers;

/// <summary>
/// L5 — khách ép provider qua <c>options.provider</c>. Cửa này mặc định đóng.
/// </summary>
public class ForcedProviderPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Allowlist_rong_thi_khong_ai_ep_duoc_gi(string? csv)
    {
        IReadOnlyList<string> allowlist = ForcedProviderPolicy.ParseAllowlist(csv);

        allowlist.Should().BeEmpty();
        ForcedProviderPolicy.Check("kling", allowlist, isProduction: false)
            .Should().Contain("không nằm trong danh sách");
    }

    [Fact]
    public void Tach_allowlist_theo_nhieu_dau_phan_cach_va_bo_trung()
    {
        ForcedProviderPolicy.ParseAllowlist("kling, seedance;KLING\nvidu")
            .Should().Equal("kling", "seedance", "vidu");
    }

    [Fact]
    public void Provider_trong_allowlist_thi_di_tiep_toi_buoc_kiem_nang_luc()
    {
        ForcedProviderPolicy.Check("Kling", ["kling", "seedance"], isProduction: true).Should().BeNull();
    }

    [Fact]
    public void Ep_provider_gia_o_production_thi_tu_choi_ke_ca_khi_nam_trong_allowlist()
    {
        ForcedProviderPolicy.Check("FAKE", [ProviderNames.Fake], isProduction: true)
            .Should().Contain("provider giả");
    }

    [Fact]
    public void Ngoai_production_thi_provider_gia_van_phai_qua_allowlist()
    {
        ForcedProviderPolicy.Check(ProviderNames.Fake, [], isProduction: false)
            .Should().Contain("không nằm trong danh sách");

        ForcedProviderPolicy.Check(ProviderNames.Fake, [ProviderNames.Fake], isProduction: false)
            .Should().BeNull();
    }

    [Fact]
    public void Nem_khi_ten_rong_hoac_allowlist_null()
    {
        FluentActions.Invoking(() => ForcedProviderPolicy.Check(" ", [], isProduction: false))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => ForcedProviderPolicy.Check("kling", null!, isProduction: false))
            .Should().Throw<ArgumentNullException>();
    }
}
