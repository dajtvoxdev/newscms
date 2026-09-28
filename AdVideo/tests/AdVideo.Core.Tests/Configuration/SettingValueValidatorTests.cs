using AdVideo.Core.Configuration;
using AdVideo.Core.Entities;
using FluentAssertions;
using Xunit;

namespace AdVideo.Core.Tests.Configuration;

public sealed class SettingValueValidatorTests
{
    private static SystemSetting Setting(
        string key, SettingValueType type, string? min = null, string? max = null) =>
        new()
        {
            Key = key,
            Value = "0",
            ValueType = type,
            Description = "mô tả",
            MinValue = min,
            MaxValue = max,
        };

    [Theory]
    [InlineData("1", true)]
    [InlineData("8", true)]
    [InlineData("0", false)]
    [InlineData("9", false)]
    [InlineData("2.5", false)] // Int không nhận số lẻ
    [InlineData("hai", false)]
    public void So_nguyen_kiem_kieu_va_khoang(string value, bool ok)
    {
        SystemSetting setting = Setting(SettingKeys.MaxConcurrentShots, SettingValueType.Int, "1", "8");

        (SettingValueValidator.Validate(setting, value) is null).Should().Be(ok);
    }

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("-14", true)]
    [InlineData("0,5", false)] // dấu phẩy là phân cách nghìn trong InvariantCulture, không phải thập phân
    public void So_thap_phan_doc_theo_dau_cham(string value, bool ok)
    {
        SystemSetting setting = Setting(SettingKeys.MaxCostPerJobUsd, SettingValueType.Decimal, "-100", "500");

        (SettingValueValidator.Validate(setting, value) is null).Should().Be(ok);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("0", true)]
    [InlineData("yes", false)]
    public void Bool_chi_nhan_true_false_1_0(string value, bool ok)
    {
        SystemSetting setting = Setting(SettingKeys.ProviderSmokeTestEnabled, SettingValueType.Bool);

        (SettingValueValidator.Validate(setting, value) is null).Should().Be(ok);
    }

    [Fact]
    public void Json_phai_parse_duoc()
    {
        SystemSetting setting = Setting("BatKy", SettingValueType.Json);

        SettingValueValidator.Validate(setting, "{\"a\":1}").Should().BeNull();
        SettingValueValidator.Validate(setting, "{a:").Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Chu_nhan_AI_rong_la_tat_nhan_doi_lot_nen_bi_tu_choi(string value)
    {
        SystemSetting setting = Setting(SettingKeys.AiLabelOverlayText, SettingValueType.String);

        SettingValueValidator.Validate(setting, value).Should().Contain("không được để trống");
    }

    [Fact]
    public void Chu_nhan_AI_qua_dai_bi_tu_choi()
    {
        SystemSetting setting = Setting(SettingKeys.AiLabelOverlayText, SettingValueType.String);

        SettingValueValidator.Validate(setting, new string('a', SettingValueValidator.MaxAiLabelLength + 1)).Should().NotBeNull();
        SettingValueValidator.Validate(setting, "Nội dung tạo bằng AI").Should().BeNull();
    }

    [Fact]
    public void Khoa_font_nhan_khong_sua_tay_duoc()
    {
        SystemSetting setting = Setting(SettingKeys.AiLabelFontObjectKey, SettingValueType.String);

        SettingValueValidator.Validate(setting, "fonts/abc.ttf").Should().Contain("label-font");
    }

    [Fact]
    public void Thieu_gia_tri_bi_tu_choi()
    {
        SettingValueValidator.Validate(Setting("X", SettingValueType.String), null).Should().NotBeNull();
    }
}
