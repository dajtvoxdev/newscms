using AdVideo.Core.Security;
using FluentAssertions;

namespace AdVideo.Core.Tests.Security;

/// <summary>Key không được nằm trong RawError, log hay descriptor dưới dạng chữ thường.</summary>
public class SecretRedactorTests
{
    [Theory]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwx", true)]
    [InlineData("sk_0123456789abcdef0123", true)]
    [InlineData("xi-abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.sig", true)]
    [InlineData("Bearer Zx81kQp2Lm4Nn7Vv0Ww3Yy6Tt9Rr5Ss1Uu", true)]
    [InlineData("fal-ai/kling-video/v3/pro/image-to-video", false)]
    [InlineData("eleven_flash_v2_5", false)]
    [InlineData("content_policy_violation_something_long", false)]
    [InlineData("Bearer {{secret.api_key}}", false)]
    [InlineData("xi-api-key", false)]
    public void Nhan_dien_chuoi_giong_key_that(string text, bool expected)
    {
        SecretRedactor.LooksLikeSecret(text).Should().Be(expected);
    }

    [Fact]
    public void Thay_dung_key_da_biet_ca_dang_urlencode()
    {
        const string key = "abc123/def+456==";

        SecretRedactor.RedactKnown($"echo: {key} and {Uri.EscapeDataString(key)}", key)
            .Should().Be("echo: ****56== and ****56==");
    }

    [Theory]
    [InlineData(null, "abcdefgh12")]
    [InlineData("", "abcdefgh12")]
    [InlineData("nothing here", null)]
    [InlineData("short abc", "abc")]
    public void Khong_co_gi_de_che_thi_giu_nguyen(string? text, string? key)
    {
        SecretRedactor.RedactKnown(text, key).Should().Be(text);
    }

    [Fact]
    public void Key_khong_doi_khi_urlencode_thi_chi_thay_mot_lan()
    {
        SecretRedactor.RedactKnown("k=abcdefgh1234", "abcdefgh1234").Should().Be("k=****1234");
    }

    [Theory]
    [InlineData("{\"error\":\"bad key sk-proj-abcdefghijklmnopqrstuv\"}", "{\"error\":\"bad key ****\"}")]
    [InlineData("key=0123456789abcdef0123456789abcdef&x=1", "key=****&x=1")]
    [InlineData("tok eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.abc end", "tok **** end")]
    [InlineData("task-abcdefghijklmnopqrstu", "task-abcdefghijklmnopqrstu")]
    [InlineData(null, null)]
    public void Che_theo_hinh_dang_khi_khong_biet_key(string? text, string? expected)
    {
        SecretRedactor.RedactPatterns(text).Should().Be(expected);
    }

    [Fact]
    public void Dang_che_giu_bon_ky_tu_cuoi()
    {
        SecretRedactor.MaskOf("abcdefgh").Should().Be("****efgh");
        SecretRedactor.MaskOf("abc").Should().Be("****");
        FluentActions.Invoking(() => SecretRedactor.MaskOf(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => SecretRedactor.LooksLikeSecret(null!)).Should().Throw<ArgumentNullException>();
    }
}
