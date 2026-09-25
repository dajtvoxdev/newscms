using AdVideo.Core.Security;
using FluentAssertions;

namespace AdVideo.Core.Tests.Security;

/// <summary>
/// API key của tenant: sinh, băm, tra cứu, đối chiếu, che.
/// </summary>
/// <remarks>
/// Test ở đây canh ba thứ không nhìn thấy khi đọc code: key sinh ra có thật sự ngẫu nhiên không,
/// phần bí mật có lọt vào prefix dùng để ghi log không, và <c>Verify</c> có vô tình chấp nhận
/// thứ không nên chấp nhận không.
/// </remarks>
public class ApiKeyHasherTests
{
    [Fact]
    public void Key_sinh_ra_mang_tien_to_nhan_dien_va_du_dai()
    {
        string key = ApiKeyHasher.Generate();

        key.Should().StartWith(ApiKeyHasher.KeyPrefix, "một key lọt ra ngoài phải nhìn là biết của hệ thống nào");
        key.Length.Should().BeGreaterThan(ApiKeyHasher.LookupPrefixLength + 20);
    }

    [Fact]
    public void Key_khong_chua_ky_tu_lam_hong_URL_YAML_hay_bien_moi_truong()
    {
        // '+', '/' và '=' của Base64 chuẩn bị biến dạng khi dán vào URL, YAML hoặc .env — và lỗi
        // đó hiện ra dưới dạng "key sai" chứ không dưới dạng "key bị cắt".
        var keys = Enumerable.Range(0, 50).Select(_ => ApiKeyHasher.Generate()).ToList();

        keys.Should().OnlyContain(k => k.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
    }

    [Fact]
    public void Moi_lan_sinh_ra_mot_key_khac_nhau()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => ApiKeyHasher.Generate()).ToHashSet(StringComparer.Ordinal);

        keys.Should().HaveCount(200, "trùng key nghĩa là tenant này gọi API bằng danh tính tenant khác");
    }

    [Fact]
    public void Ham_bam_on_dinh_va_tra_hex_thuong_64_ky_tu()
    {
        string hash = ApiKeyHasher.Hash("adv_abc");

        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        ApiKeyHasher.Hash("adv_abc").Should().Be(hash, "hash phải ổn định qua các lần gọi, nếu không không ai đăng nhập được");
    }

    [Fact]
    public void Hai_key_khac_nhau_cho_hai_hash_khac_nhau()
    {
        ApiKeyHasher.Hash("adv_abc").Should().NotBe(ApiKeyHasher.Hash("adv_abd"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bam_chuoi_rong_thi_nem_chu_khong_tra_ve_mot_hash_hop_le(string? key)
    {
        // Một hash "hợp lệ" của chuỗi rỗng là một hash mà bất kỳ ai gửi key rỗng cũng khớp.
        ((Action)(() => ApiKeyHasher.Hash(key!))).Should().Throw<ArgumentException>();
        ((Action)(() => ApiKeyHasher.LookupPrefix(key!))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Prefix_tra_cuu_cat_dung_do_dai_da_khai_bao()
    {
        string key = ApiKeyHasher.Generate();

        ApiKeyHasher.LookupPrefix(key).Should()
            .HaveLength(ApiKeyHasher.LookupPrefixLength)
            .And.Be(key[..ApiKeyHasher.LookupPrefixLength]);
    }

    [Fact]
    public void Prefix_khong_du_de_dung_lai_key()
    {
        // Prefix nằm trong log và trong index DB, nên phải là phần KHÔNG bí mật. Nếu nó dài bằng
        // key thì mỗi dòng log là một lần rò key.
        string key = ApiKeyHasher.Generate();

        ApiKeyHasher.LookupPrefix(key).Length.Should().BeLessThan(key.Length / 2);
    }

    [Fact]
    public void Key_ngan_hon_do_dai_prefix_thi_tra_nguyen_van_chu_khong_crash()
    {
        // Key rác gửi từ ngoài vào phải kết thúc bằng 401, không phải bằng 500.
        ApiKeyHasher.LookupPrefix("adv_1").Should().Be("adv_1");
        ApiKeyHasher.LookupPrefix("adv_12345678").Should().Be("adv_12345678", "đúng bằng độ dài prefix cũng không cắt");
    }

    [Fact]
    public void Verify_chap_nhan_dung_key_da_bam()
    {
        string key = ApiKeyHasher.Generate();

        ApiKeyHasher.Verify(key, ApiKeyHasher.Hash(key)).Should().BeTrue();
    }

    [Fact]
    public void Verify_tu_choi_key_sai()
    {
        ApiKeyHasher.Verify("adv_sai", ApiKeyHasher.Hash("adv_dung")).Should().BeFalse();
    }

    [Fact]
    public void Verify_phan_biet_hoa_thuong_o_hash_luu_trong_DB()
    {
        // Hash lưu dạng hex thường. Một cột collation không phân biệt hoa thường có thể khiến DB
        // trả về bản ghi có hash viết hoa; so sánh ở đây phải vẫn chặt.
        string key = ApiKeyHasher.Generate();

        ApiKeyHasher.Verify(key, ApiKeyHasher.Hash(key).ToUpperInvariant()).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "abc")]
    [InlineData("", "abc")]
    [InlineData("   ", "abc")]
    [InlineData("adv_abc", null)]
    [InlineData("adv_abc", "")]
    [InlineData("adv_abc", "   ")]
    public void Verify_tra_false_thay_vi_nem_khi_thieu_du_lieu(string? key, string? storedHash)
    {
        // Đây là đường đi của mọi request vào hệ thống: tenant chưa có hash, header rỗng, hash
        // null trong DB. Ném ở đây biến một 401 bình thường thành 500 và một dòng stack trace.
        ApiKeyHasher.Verify(key!, storedHash!).Should().BeFalse();
    }

    [Fact]
    public void Mask_giu_prefix_va_giau_phan_bi_mat()
    {
        string key = ApiKeyHasher.Generate();
        string masked = ApiKeyHasher.Mask(key);

        masked.Should().StartWith(ApiKeyHasher.LookupPrefix(key)).And.EndWith("…");
        key.Should().NotBe(masked);
        masked.Length.Should().Be(ApiKeyHasher.LookupPrefixLength + 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Mask_chuoi_rong_khong_nem(string? key)
    {
        // Mask được gọi từ trong code ghi log. Một exception ở đây sẽ làm hỏng đúng dòng log mà
        // người ta đang cần đọc để hiểu chuyện gì đang xảy ra.
        ApiKeyHasher.Mask(key!).Should().Be("(trống)");
    }
}
