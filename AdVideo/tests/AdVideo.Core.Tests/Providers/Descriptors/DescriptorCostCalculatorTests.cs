using System.Text.Json.Nodes;
using AdVideo.Core.Providers.Descriptors;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers.Descriptors;

public class DescriptorCostCalculatorTests
{
    private static readonly DescriptorCost ByResolution = new()
    {
        Unit = DescriptorCostUnit.PerSecond,
        RateBy = new DescriptorRateTable
        {
            Variable = "resolution",
            Table = new() { ["480p"] = 0.08m, ["1080p"] = 0.25m },
        },
        Extras = [new DescriptorCostExtra { Unit = DescriptorCostUnit.PerReferenceImage, RateUsd = 0.01m }],
    };

    private static Dictionary<string, JsonNode?> Vars(string? resolution) =>
        new() { ["resolution"] = resolution is null ? null : JsonValue.Create(resolution) };

    [Fact]
    public void Don_gia_theo_bien_cong_phi_anh()
    {
        DescriptorCostCalculator.Calculate(ByResolution, new DescriptorUsage(Vars("480p"), Seconds: 6, ReferenceImages: 1))
            .Should().Be(0.49m);
    }

    [Theory]
    [InlineData("720p")]
    [InlineData(null)]
    public void Gia_tri_khong_co_trong_bang_thi_lay_muc_cao_nhat(string? resolution)
    {
        DescriptorCostCalculator.Calculate(ByResolution, new DescriptorUsage(Vars(resolution), Seconds: 4))
            .Should().Be(1.00m);
    }

    [Fact]
    public void Bien_khong_duoc_cung_cap_thi_lay_muc_cao_nhat()
    {
        DescriptorCostCalculator.Calculate(ByResolution, new DescriptorUsage(new Dictionary<string, JsonNode?>(), Seconds: 4))
            .Should().Be(1.00m);
    }

    [Theory]
    [InlineData(DescriptorCostUnit.PerSecond, 2.0)]
    [InlineData(DescriptorCostUnit.PerRequest, 0.5)]
    [InlineData(DescriptorCostUnit.Per1000Chars, 1.0)]
    [InlineData(DescriptorCostUnit.PerReferenceImage, 1.5)]
    public void Don_vi_tinh(DescriptorCostUnit unit, double expected)
    {
        var cost = new DescriptorCost { Unit = unit, RateUsd = 0.5m };

        DescriptorCostCalculator.Calculate(cost, new DescriptorUsage(Vars(null), Seconds: 4, Characters: 2000, ReferenceImages: 3))
            .Should().Be((decimal)expected);
    }

    [Fact]
    public void Khong_khai_don_gia_thi_bang_khong()
    {
        var cost = new DescriptorCost { Unit = DescriptorCostUnit.PerSecond };

        DescriptorCostCalculator.Calculate(cost, new DescriptorUsage(Vars(null), Seconds: 4)).Should().Be(0m);
        DescriptorCostCalculator.MaxRate(cost).Should().Be(0m);
    }

    [Fact]
    public void Gia_moi_giay_xau_nhat_chia_phi_theo_clip_ngan_nhat()
    {
        DescriptorCostCalculator.WorstCasePerSecond(ByResolution, shortestClipSeconds: 5, maxReferenceImages: 1)
            .Should().Be(0.252m);

        DescriptorCostCalculator.WorstCasePerSecond(ByResolution, 5, 1, Vars("480p"))
            .Should().Be(0.082m);

        var perRequest = new DescriptorCost { Unit = DescriptorCostUnit.PerRequest, RateUsd = 0.8m };

        DescriptorCostCalculator.WorstCasePerSecond(perRequest, 8, 0).Should().Be(0.1m);
    }

    [Fact]
    public void Nem_khi_dau_vao_sai()
    {
        FluentActions.Invoking(() => DescriptorCostCalculator.Calculate(null!, new DescriptorUsage(Vars(null)))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DescriptorCostCalculator.Calculate(ByResolution, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DescriptorCostCalculator.MaxRate(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DescriptorCostCalculator.WorstCasePerSecond(null!, 1, 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DescriptorCostCalculator.WorstCasePerSecond(ByResolution, 0, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
