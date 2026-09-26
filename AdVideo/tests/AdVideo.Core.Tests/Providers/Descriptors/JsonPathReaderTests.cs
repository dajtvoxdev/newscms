using System.Text.Json.Nodes;
using AdVideo.Core.Providers.Descriptors;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers.Descriptors;

public class JsonPathReaderTests
{
    private static readonly JsonNode Doc = JsonNode.Parse("""
        {
          "id": "abc",
          "empty": "",
          "count": 6,
          "price": "0.25",
          "flag": true,
          "video": { "url": "https://x/v.mp4" },
          "videos": [ { "url": "https://x/1.mp4" }, { "url": "https://x/2.mp4" } ],
          "alignment": {
            "characters": ["a", " ", "b"],
            "starts": [0, 0.1, 0.2],
            "mixed": [1, "x"]
          }
        }
        """)!;

    [Theory]
    [InlineData("id", "abc")]
    [InlineData("video.url", "https://x/v.mp4")]
    [InlineData("videos[1].url", "https://x/2.mp4")]
    [InlineData("videos[-1].url", "https://x/2.mp4")]
    [InlineData("$.video.url", "https://x/v.mp4")]
    [InlineData("count", "6")]
    [InlineData("flag", "true")]
    [InlineData("missing || empty || video.url", "https://x/v.mp4")]
    public void Doc_duoc_duong_dan(string path, string expected)
    {
        JsonPathReader.ReadString(Doc, path).Should().Be(expected);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("videos[5].url")]
    [InlineData("videos[-9].url")]
    [InlineData("id.sub")]
    [InlineData("video[0]")]
    [InlineData("video")]
    public void Khong_khop_thi_null_chu_khong_nem(string path)
    {
        JsonPathReader.ReadString(Doc, path).Should().BeNull();
    }

    [Fact]
    public void Goc_la_ca_cay()
    {
        JsonPathReader.Read(Doc, "$").Should().BeSameAs(Doc);
        JsonPathReader.Read(Doc, "").Should().BeSameAs(Doc);
    }

    [Fact]
    public void Doc_so_tu_ca_so_lan_chuoi()
    {
        JsonPathReader.ReadDecimal(Doc, "count").Should().Be(6m);
        JsonPathReader.ReadDecimal(Doc, "price").Should().Be(0.25m);
        JsonPathReader.ReadDecimal(Doc, "id").Should().BeNull();
        JsonPathReader.ReadDecimal(Doc, "flag").Should().BeNull();
        JsonPathReader.ReadDecimal(Doc, "video").Should().BeNull();
    }

    [Fact]
    public void Doc_mang_so_va_mang_chuoi()
    {
        JsonPathReader.ReadNumberArray(Doc, "alignment.starts").Should().Equal(0, 0.1, 0.2);
        JsonPathReader.ReadStringArray(Doc, "alignment.characters").Should().Equal("a", " ", "b");

        JsonPathReader.ReadNumberArray(Doc, "alignment.mixed").Should().BeNull();
        JsonPathReader.ReadNumberArray(Doc, "id").Should().BeNull();
        JsonPathReader.ReadStringArray(Doc, "videos").Should().BeNull();
        JsonPathReader.ReadStringArray(Doc, "id").Should().BeNull();
    }

    [Fact]
    public void Gia_tri_null_trong_json_khong_doc_thanh_chuoi()
    {
        JsonNode node = JsonNode.Parse("{\"a\":null,\"b\":[null]}")!;

        JsonPathReader.ReadString(node, "a").Should().BeNull();
        JsonPathReader.AsString(JsonNode.Parse("null")).Should().BeNull();
        JsonPathReader.ReadStringArray(node, "b").Should().BeNull();
    }

    [Fact]
    public void Gia_tri_vo_huong_khong_phai_json_nguyen_thuy_thi_khong_doc_thanh_chuoi()
    {
        JsonPathReader.AsString(JsonValue.Create(new Dictionary<string, int> { ["a"] = 1 })).Should().BeNull();
        JsonPathReader.AsString(JsonValue.Create(false)).Should().Be("false");
    }

    [Theory]
    [InlineData("url ||")]
    [InlineData("a..b")]
    [InlineData("a[x]")]
    [InlineData("a[1")]
    [InlineData("a.")]
    [InlineData("a b")]
    [InlineData("[0]x")]
    [InlineData("a || b c")]
    [InlineData("a{b}")]
    public void Cu_phap_sai_bi_bat_luc_luu(string path)
    {
        JsonPathReader.TryParse(path, out string? error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();

        FluentActions.Invoking(() => JsonPathReader.Read(Doc, path)).Should().Throw<FormatException>();
    }

    [Fact]
    public void Duong_dan_null_la_loi()
    {
        JsonPathReader.TryParse(null, out string? error).Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("a.b[0].c")]
    [InlineData("a[0][1]")]
    [InlineData("x || y.z")]
    public void Cu_phap_dung(string path)
    {
        JsonPathReader.TryParse(path, out _).Should().BeTrue();
    }
}
