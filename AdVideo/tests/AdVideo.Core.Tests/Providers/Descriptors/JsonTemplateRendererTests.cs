using System.Text.Json.Nodes;
using AdVideo.Core.Providers.Descriptors;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers.Descriptors;

public class JsonTemplateRendererTests
{
    private static readonly Dictionary<string, Dictionary<string, string>> Maps = new()
    {
        ["aspect"] = new() { ["Portrait9x16"] = "9:16" },
    };

    private static TemplateContext Ctx(params (string Name, object? Value)[] vars) =>
        new(vars.ToDictionary(v => v.Name, v => JsonTemplateRenderer.ToNode(v.Value)), Maps);

    private static string Render(string template, TemplateContext ctx)
    {
        JsonTemplateRenderer.TryRender(JsonNode.Parse(template), ctx, out JsonNode? result).Should().BeTrue();

        return result!.ToJsonString();
    }

    [Fact]
    public void Mot_bien_chiem_tron_chuoi_giu_kieu_tu_nhien()
    {
        Render("""{"seconds":"{{seconds}}","flag":"{{on}}"}""", Ctx(("seconds", 6), ("on", false)))
            .Should().Be("""{"seconds":6,"flag":false}""");
    }

    [Theory]
    [InlineData("{{seconds|string}}", 6, "\"6\"")]
    [InlineData("{{seconds|int}}", "6", "6")]
    [InlineData("{{seconds|int}}", true, "1")]
    [InlineData("{{seconds|int}}", false, "0")]
    [InlineData("{{seconds|bool}}", "x", "true")]
    [InlineData("{{seconds|not}}", "x", "false")]
    [InlineData("{{seconds|urlencode}}", "a b/c", "\"a%20b%2Fc\"")]
    [InlineData("{{seconds|map:aspect}}", "Portrait9x16", "\"9:16\"")]
    [InlineData("{{seconds|map:aspect|string}}", "Portrait9x16", "\"9:16\"")]
    public void Bo_loc_ep_kieu(string expression, object value, string expectedJson)
    {
        Render($$"""{"v":"{{expression}}"}""", Ctx(("seconds", value)))
            .Should().Be($$"""{"v":{{expectedJson}}}""");
    }

    [Fact]
    public void Bo_loc_tren_bien_null()
    {
        Render("""{"a":"{{x|not}}","b":"{{x|bool}}","c":"{{x|string}}"}""", Ctx(("x", null)))
            .Should().Be("""{"a":true,"b":false}""");
    }

    [Fact]
    public void Bo_loc_string_voi_mang_thi_ra_chuoi_json()
    {
        JsonTemplateRenderer.TryRender(JsonNode.Parse("""{"v":"{{x|string}}"}"""), Ctx(("x", new[] { "a" })), out JsonNode? result);

        result!["v"]!.GetValue<string>().Should().Be("[\"a\"]");
    }

    [Theory]
    [InlineData("{{x|int}}", "6.5")]
    [InlineData("{{x|int}}", "abc")]
    [InlineData("{{x|map:aspect}}", "Tall")]
    [InlineData("{{x|map:khongco}}", "Portrait9x16")]
    [InlineData("{{khong_ton_tai}}", "v")]
    [InlineData("{{X}}", "v")]
    public void Loi_luc_dung_thi_nem_ro_ly_do(string expression, string value)
    {
        FluentActions.Invoking(() => Render($$"""{"v":"{{expression}}"}""", Ctx(("x", value))))
            .Should().Throw<TemplateRenderException>();
    }

    [Fact]
    public void Map_khi_khong_co_bang_nao()
    {
        var ctx = new TemplateContext(new Dictionary<string, JsonNode?> { ["x"] = "a" });

        FluentActions.Invoking(() => JsonTemplateRenderer.TryRender(JsonNode.Parse("\"{{x|map:aspect}}\""), ctx, out _))
            .Should().Throw<TemplateRenderException>().WithMessage("*valueMaps.aspect*");
    }

    [Fact]
    public void Bien_null_hoac_rong_thi_ca_cap_khoa_gia_tri_bien_mat()
    {
        Render("""{"a":"{{x}}","b":"{{y}}","c":"Bearer {{x}}","d":"keep"}""", Ctx(("x", null), ("y", "")))
            .Should().Be("""{"d":"keep"}""");
    }

    [Fact]
    public void Chuoi_ghep_nhieu_bien()
    {
        Render("""{"v":"{{a}}-{{b}} x"}""", Ctx(("a", "p"), ("b", 2)))
            .Should().Be("""{"v":"p-2 x"}""");
    }

    [Fact]
    public void Prompt_chua_ky_tu_json_khong_pha_duoc_cau_truc()
    {
        const string evil = "\"}, \"model\": \"dat-tien\", \"x\": {\"";

        JsonTemplateRenderer.TryRender(
            JsonNode.Parse("""{"model":"m","prompt":"{{prompt}}"}"""),
            Ctx(("prompt", evil)),
            out JsonNode? result);

        result!["model"]!.GetValue<string>().Should().Be("m");
        result["prompt"]!.GetValue<string>().Should().Be(evil);
        result.AsObject().Count.Should().Be(2);
    }

    [Fact]
    public void When_quyet_dinh_nhanh_co_mat_hay_khong()
    {
        const string template = """{"image":{"@when":"{{image_url}}","url":"{{image_url}}"}}""";

        Render(template, Ctx(("image_url", "https://i/1.png"))).Should().Be("""{"image":{"url":"https://i/1.png"}}""");
        Render(template, Ctx(("image_url", null))).Should().Be("{}");
    }

    [Fact]
    public void Each_dung_mang_voi_item_va_index()
    {
        Render(
            """{"refs":{"@each":"{{image_urls}}","@item":{"i":"{{index}}","url":"{{item}}"}}}""",
            Ctx(("image_urls", new[] { "a", "b" })))
            .Should().Be("""{"refs":[{"i":0,"url":"a"},{"i":1,"url":"b"}]}""");
    }

    [Fact]
    public void Each_khong_co_item_thi_chep_nguyen_phan_tu()
    {
        Render("""{"refs":{"@each":"{{urls}}"}}""", Ctx(("urls", new[] { "a" })))
            .Should().Be("""{"refs":["a"]}""");
    }

    [Fact]
    public void Each_tren_mang_rong_hoac_khong_phai_mang_thi_khoa_bien_mat()
    {
        Render("""{"refs":{"@each":"{{urls}}"},"k":1}""", Ctx(("urls", Array.Empty<string>()))).Should().Be("""{"k":1}""");
        Render("""{"refs":{"@each":"{{urls}}"},"k":1}""", Ctx(("urls", "a"))).Should().Be("""{"k":1}""");
    }

    [Fact]
    public void Each_bo_phan_tu_ma_item_dung_ra_rong()
    {
        Render(
            """{"refs":{"@each":"{{urls}}","@item":"{{missing}}"}}""",
            new TemplateContext(new Dictionary<string, JsonNode?>
            {
                ["urls"] = new JsonArray("a"),
                ["missing"] = null,
            })).Should().Be("""{"refs":[]}""");
    }

    [Theory]
    [InlineData("""{"x":{"@when":"literal","a":1}}""")]
    [InlineData("""{"x":{"@when":"{{a}} {{b}}","a":1}}""")]
    [InlineData("""{"x":{"@each":5}}""")]
    public void When_va_each_phai_la_dung_mot_bieu_thuc(string template)
    {
        FluentActions.Invoking(() => JsonTemplateRenderer.TryRender(JsonNode.Parse(template), Ctx(("a", 1), ("b", 2)), out _))
            .Should().Throw<TemplateRenderException>();
    }

    [Fact]
    public void Literal_giu_nguyen_ke_ca_null_va_mang_rong()
    {
        Render("""{"a":null,"b":[],"c":1.5,"d":true,"e":["{{x}}"],"f":["{{x}}","k"]}""", Ctx(("x", null)))
            .Should().Be("""{"a":null,"b":[],"c":1.5,"d":true,"f":["k"]}""");
    }

    [Fact]
    public void Goc_bien_mat_thi_tra_false()
    {
        JsonTemplateRenderer.TryRender(JsonNode.Parse("\"{{x}}\""), Ctx(("x", null)), out JsonNode? result).Should().BeFalse();
        result.Should().BeNull();

        JsonTemplateRenderer.TryRender(null, Ctx(), out JsonNode? nothing).Should().BeTrue();
        nothing.Should().BeNull();
    }

    [Fact]
    public void Duong_dan_giu_dau_gach_cheo_va_ma_hoa_tung_doan()
    {
        JsonTemplateRenderer.RenderString("/{{model}}/q?x=1", Ctx(("model", "fal-ai/kling video")), asUrlPath: true)
            .Should().Be("/fal-ai/kling%20video/q?x=1");

        JsonTemplateRenderer.RenderString("/videos/{{id}}", Ctx(("id", "a?b#c")), asUrlPath: true)
            .Should().Be("/videos/a%3Fb%23c");
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../admin")]
    [InlineData("a/./b")]
    public void Duong_dan_tu_choi_doan_thoat_khoi_baseUrl(string id)
    {
        FluentActions.Invoking(() => JsonTemplateRenderer.RenderString("/videos/{{id}}", Ctx(("id", id)), asUrlPath: true))
            .Should().Throw<TemplateRenderException>();
    }

    [Fact]
    public void Chuoi_phang_thieu_bien_thi_null()
    {
        JsonTemplateRenderer.RenderString("Bearer {{k}}", Ctx(("k", null))).Should().BeNull();
        JsonTemplateRenderer.RenderString("Bearer {{k}}", Ctx(("k", "abc"))).Should().Be("Bearer abc");
        JsonTemplateRenderer.RenderString("tinh", Ctx()).Should().Be("tinh");
    }

    [Theory]
    [InlineData("Prompt", "không hợp lệ")]
    [InlineData("x|upper", "không tồn tại")]
    [InlineData("x|map", "cần tên bảng")]
    [InlineData("x|string:y", "không nhận tham số")]
    public void Tach_bieu_thuc_bat_loi_cu_phap(string raw, string message)
    {
        JsonTemplateRenderer.TryParseExpression(raw, out TemplateExpression? expression, out string? error).Should().BeFalse();
        expression.Should().BeNull();
        error.Should().Contain(message);
    }

    [Fact]
    public void Tach_bieu_thuc_hop_le()
    {
        JsonTemplateRenderer.TryParseExpression(" secret.api_key | map : aspect | string ", out TemplateExpression? e, out _).Should().BeTrue();

        e!.Variable.Should().Be("secret.api_key");
        e.Filters.Should().Equal(new TemplateFilter("map", "aspect"), new TemplateFilter("string", null));
    }

    [Fact]
    public void Gia_tri_that()
    {
        JsonTemplateRenderer.IsTruthy(null).Should().BeFalse();
        JsonTemplateRenderer.IsTruthy(new JsonArray()).Should().BeFalse();
        JsonTemplateRenderer.IsTruthy(new JsonArray(1)).Should().BeTrue();
        JsonTemplateRenderer.IsTruthy(new JsonObject()).Should().BeTrue();
        JsonTemplateRenderer.IsTruthy(JsonValue.Create("")).Should().BeFalse();
        JsonTemplateRenderer.IsTruthy(JsonValue.Create(0)).Should().BeTrue();
        JsonTemplateRenderer.IsTruthy(JsonNode.Parse("false")).Should().BeFalse();
        JsonTemplateRenderer.IsTruthy(JsonNode.Parse("true")).Should().BeTrue();
    }

    [Fact]
    public void Doi_gia_tri_clr_sang_node()
    {
        JsonTemplateRenderer.ToNode(5L)!.ToJsonString().Should().Be("5");
        JsonTemplateRenderer.ToNode(1.5m)!.ToJsonString().Should().Be("1.5");
        JsonTemplateRenderer.ToNode(1.5d)!.ToJsonString().Should().Be("1.5");
        JsonTemplateRenderer.ToNode(Guid.Empty)!.GetValue<string>().Should().Be(Guid.Empty.ToString());

        JsonNode existing = JsonValue.Create("x");
        JsonTemplateRenderer.ToNode(existing).Should().BeSameAs(existing);

        FluentActions.Invoking(() => JsonTemplateRenderer.ToNode(DateTime.UtcNow)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tim_bieu_thuc_trong_chuoi()
    {
        JsonTemplateRenderer.FindExpressions("a {{x}} b {{ y|string }}").Should().Equal("x", " y|string ");
    }
}
