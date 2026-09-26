using System.Text.Json.Nodes;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using FluentAssertions;

namespace AdVideo.Core.Tests.Providers.Descriptors;

/// <summary>
/// Mọi lỗi của descriptor phải lộ ra lúc LƯU — lúc gọi là lúc có job của khách và có thể đã tốn tiền.
/// </summary>
public class DescriptorValidatorTests
{
    [Theory]
    [InlineData(Samples.Nova)]
    [InlineData(Samples.FalKling)]
    [InlineData(Samples.ElevenLabs)]
    public void Mau_trong_repo_luon_hop_le(string file)
    {
        DescriptorParseResult result = ProviderDescriptorParser.Parse(Samples.Read(file));

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        result.CapabilityJson.Should().Contain("\"provider\"");
    }

    [Fact]
    public void Capability_cua_mau_doc_duoc_thanh_manifest_that()
    {
        DescriptorParseResult nova = ProviderDescriptorParser.Parse(Samples.Read(Samples.Nova));

        VideoProviderCapability caps = System.Text.Json.JsonSerializer.Deserialize<VideoProviderCapability>(
            nova.CapabilityJson!, ProviderDescriptorParser.JsonOptions)!;

        caps.Provider.Should().Be("nova-grok-video-15");
        caps.AllowedDurationSeconds.Should().Equal(6, 10);
        nova.Descriptor!.Poll!.Mode.Should().Be(DescriptorPollMode.PathTemplate);
        nova.Descriptor.Cost!.RateBy!.Table["1080p"].Should().Be(0.25m);
    }

    public static TheoryData<string, Action<JsonObject>, string> Broken => new()
    {
        { Samples.Nova, d => d["schema"] = "advideo.provider/v2", "schema phải là" },
        { Samples.Nova, d => d["name"] = "Nova Grok", "name chỉ gồm" },
        { Samples.Nova, d => { d["name"] = "fake"; d.Obj("capability")["provider"] = "fake"; }, "dành cho provider giả" },
        { Samples.Nova, d => d.Obj("transport")["baseUrl"] = "http://novagateway.net/v1", "https" },
        { Samples.Nova, d => d.Obj("transport")["baseUrl"] = "https://u:p@novagateway.net/v1?x=1", "không được chứa thông tin đăng nhập" },
        { Samples.Nova, d => d.Obj("transport")["requestTimeoutSeconds"] = 3600, "requestTimeoutSeconds" },
        { Samples.Nova, d => d.Obj("transport.auth")["valueRef"] = "{{model_id}}", "phải tham chiếu {{secret.api_key}}" },
        { Samples.Nova, d => d.Obj("transport.auth")["name"] = "Bad Header", "transport.auth.name" },
        { Samples.Nova, d => d.Obj("transport.headers")["Host"] = "evil.example", "không được đặt header \"Host\"" },
        { Samples.Nova, d => d.Obj("transport.headers")["x-api-key"] = "abc", "header mang secret phải tham chiếu" },
        { Samples.Nova, d => d.Obj("transport.headers")["bad header"] = "x", "tên header \"bad header\" không hợp lệ" },
        { Samples.Nova, d => d.Obj("submit.body.template")["key"] = "{{secret.api_key}}", "chỉ được dùng trong transport.auth" },
        { Samples.Nova, d => d.Obj("transport.headers")["X-Other"] = "{{secret.other}}", "secret \"secret.other\" không tồn tại" },
        { Samples.Nova, d => d.Obj("submit.body.template")["prompt"] = "{{promt}}", "biến \"promt\" không tồn tại" },
        { Samples.Nova, d => d.Obj("submit.body.template")["prompt"] = "{{prompt|upper}}", "Bộ lọc \"upper\" không tồn tại" },
        { Samples.Nova, d => d.Obj("submit.body.template")["prompt"] = "{{prompt|map:ratio}}", "không có bảng valueMaps.ratio" },
        { Samples.Nova, d => d.Obj("submit.body.template")["x"] = "{{provider_request_id}}", "biến \"provider_request_id\" không tồn tại" },
        { Samples.Nova, d => d.Obj("defaults")["prompt"] = "x", "trùng tên biến dựng sẵn" },
        { Samples.Nova, d => d.Obj("defaults")["Bad-Name"] = "x", "tên biến chỉ gồm" },
        { Samples.Nova, d => d.Obj("defaults")["obj"] = new JsonObject(), "chỉ nhận giá trị vô hướng" },
        { Samples.Nova, d => d.Obj("valueMaps")["Bad"] = new JsonObject(), "valueMaps.Bad" },
        { Samples.Nova, d => d.Obj("constraints")["maxInputChars"] = 0, "maxInputChars" },
        { Samples.Nova, d => d.Obj("constraints")["maxReferenceImages"] = -1, "maxReferenceImages" },
        { Samples.Nova, d => d.Obj("submit")["method"] = "DELETE", "submit.method" },
        { Samples.Nova, d => d.Obj("submit")["method"] = "GET", "không gửi được body" },
        { Samples.Nova, d => d.Obj("submit")["path"] = "https://evil.example/videos", "đường dẫn tương đối" },
        { Samples.Nova, d => d.Obj("submit")["path"] = "//evil.example/videos", "đường dẫn tương đối" },
        { Samples.Nova, d => d.Obj("submit")["successStatus"] = new JsonArray(200, 302), "successStatus" },
        { Samples.Nova, d => d.Obj("submit.body").Remove("template"), "template bắt buộc" },
        { Samples.Nova, d => d.Obj("submit")["failWhen"] = new JsonArray(new JsonObject { ["path"] = "error", ["equals"] = 1, ["exists"] = true }), "đúng MỘT trong equals" },
        { Samples.Nova, d => d.Obj("submit")["failWhen"] = new JsonArray(new JsonObject { ["path"] = "a..b", ["exists"] = true }), "failWhen[0].path" },
        { Samples.Nova, d => d.Obj("submit.body.template")["image"] = new JsonObject { ["@when"] = "x {{image_url}}", ["url"] = "{{image_url}}" }, "@when phải là đúng một biểu thức" },
        { Samples.Nova, d => d.Obj("submit.body.template")["refs"] = new JsonObject { ["@each"] = "{{image_urls}}", ["extra"] = 1 }, "chỉ được có @item" },
        { Samples.Nova, d => d.Obj("submit.body.template")["refs"] = new JsonObject { ["@item"] = "{{item}}" }, "khoá điều khiển không hợp lệ" },
        { Samples.Nova, d => d.Obj("submit.body.template")["x"] = "{{item}}", "biến \"item\" không tồn tại" },
        { Samples.Nova, d => d.Obj("poll").Remove("maxWaitSeconds"), "maxWaitSeconds BẮT BUỘC" },
        { Samples.Nova, d => d.Obj("poll")["intervalSeconds"] = 0, "intervalSeconds phải trong khoảng" },
        { Samples.Nova, d => { d.Obj("poll")["intervalSeconds"] = 100; d.Obj("poll")["maxWaitSeconds"] = 50; }, "không được lớn hơn" },
        { Samples.Nova, d => d.Obj("result").Remove("requestIdPath"), "cần result.requestIdPath" },
        { Samples.Nova, d => d.Obj("poll")["statusMap"] = new JsonObject { ["done"] = "failed" }, "statusMap phải có" },
        { Samples.Nova, d => d.Obj("poll").Remove("statusPath"), "poll.statusPath bắt buộc" },
        { Samples.Nova, d => d.Obj("poll")["path"] = "/videos/{{secret.api_key}}", "chỉ được dùng trong transport" },
        { Samples.Nova, d => d.Obj("result").Remove("videoUrlPath"), "Provider video cần một trong" },
        { Samples.Nova, d => d.Obj("result")["audioBase64Path"] = "a", "không khai được audio" },
        { Samples.Nova, d => d.Obj("result")["videoUrlPath"] = "url ||", "result.videoUrlPath" },
        { Samples.Nova, d => d.Obj("result")["billedCharactersHeader"] = "bad header", "billedCharactersHeader" },
        { Samples.Nova, d => d.Obj("result")["contentPath"] = "videos/x", "result.contentPath phải là đường dẫn tương đối" },
        { Samples.Nova, d => d.Obj("errors.byStatus")["4x"] = "Unknown", "mã HTTP 3 chữ số" },
        { Samples.Nova, d => d.Obj("errors.byStatus")["404"] = "None", "không được ánh xạ sang None" },
        { Samples.Nova, d => d.Obj("errors")["default"] = "None", "errors.default" },
        { Samples.Nova, d => d.Obj("errors.byBodyCode.table")["x"] = "None", "byBodyCode.table" },
        { Samples.Nova, d => d.Obj("errors")["deactivateCredentialOn"] = new JsonArray("hết tiền"), "deactivateCredentialOn" },
        { Samples.Nova, d => d.Obj("cost")["rateUsd"] = 0.1, "đúng MỘT trong rateUsd và rateBy" },
        { Samples.Nova, d => d.Obj("cost")["unit"] = "per1000Chars", "cost.unit của provider video" },
        { Samples.Nova, d => d.Obj("cost.rateBy")["variable"] = "khongco", "không phải biến đã biết" },
        { Samples.Nova, d => d.Obj("cost.rateBy")["table"] = new JsonObject(), "phải có ít nhất một dòng" },
        { Samples.Nova, d => d.Obj("cost")["extras"] = new JsonArray(new JsonObject { ["unit"] = "perSecond", ["rateUsd"] = -1 }), "extras[].unit" },
        { Samples.Nova, d => d.Obj("capability")["costPerSecondUsd"] = 0.05, "thấp hơn giá xấu nhất" },
        { Samples.Nova, d => d.Obj("defaults")["resolution"] = "1080p", "thấp hơn giá xấu nhất" },
        { Samples.Nova, d => d.Remove("capability"), "capability bắt buộc" },
        { Samples.Nova, d => d.Obj("capability")["provider"] = "khac", "phải trùng name" },
        { Samples.Nova, d => d.Obj("capability").Remove("modelId"), "capability không đọc được" },
        { Samples.Nova, d => d.Obj("capability")["allowedDurationSeconds"] = new JsonArray(), "allowedDurationSeconds" },
        { Samples.Nova, d => d.Obj("defaults")["token"] = "abc", "mang giá trị literal" },
        { Samples.Nova, d => d.Obj("defaults")["note"] = "sk-proj-abcdefghijklmnopqrstu", "trông như một API key" },
        { Samples.Nova, d => d.Obj("transport.headers")["Accept"] = null, "giá trị rỗng" },
        { Samples.Nova, d => { d.Remove("poll"); d.Obj("result").Remove("requestIdPath"); d.Obj("result")["contentPath"] = "/videos/{{provider_request_id}}/content"; }, "result.contentPath cần result.requestIdPath" },
        { Samples.Nova, d => { d.Obj("cost").Remove("rateBy"); d.Obj("cost")["rateUsd"] = -0.1; }, "cost.rateUsd không được âm" },
        { Samples.FalKling, d => d.Obj("poll").Remove("urlPath"), "poll.urlPath bắt buộc" },
        { Samples.ElevenLabs, d => d["kind"] = "tts2", "Sai hình dạng" },
        { Samples.ElevenLabs, d => d.Obj("result").Remove("alignment"), "result.alignment trống" },
        { Samples.ElevenLabs, d => d.Obj("result.alignment")["format"] = "words", "không suy được mốc ký tự" },
        { Samples.ElevenLabs, d => d.Obj("result").Remove("audioBase64Path"), "Engine TTS cần" },
        { Samples.ElevenLabs, d => d.Obj("result")["videoUrlPath"] = "url", "không khai được video" },
        { Samples.ElevenLabs, d => d.Obj("result.alignment")["startsPath"] = "", "startsPath bắt buộc" },
        { Samples.ElevenLabs, d => d.Obj("cost")["unit"] = "perSecond", "cost.unit của provider tts" },
        { Samples.ElevenLabs, d => d.Obj("capability")["costPer1000CharsUsd"] = 0.01, "thấp hơn đơn giá cao nhất" },
        { Samples.ElevenLabs, d => d.Obj("capability")["supportedLanguages"] = "vi", "capability không đọc được" },
        { Samples.ElevenLabs, d => d["transport"] = null, "không được null" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void Descriptor_hong_bi_tu_choi_voi_ly_do_doc_duoc(string file, Action<JsonObject> mutate, string expectedError)
    {
        DescriptorParseResult result = Samples.Mutate(file, mutate);

        result.IsValid.Should().BeFalse();
        result.Descriptor.Should().BeNull();
        result.Errors.Should().Contain(e => e.Contains(expectedError), "lỗi phải nói rõ: {0}", string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Tra_ve_toan_bo_loi_mot_luot()
    {
        DescriptorParseResult result = Samples.Mutate(Samples.Nova, d =>
        {
            d["schema"] = "x";
            d.Obj("transport")["baseUrl"] = "http://a";
            d.Obj("poll").Remove("maxWaitSeconds");
        });

        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Theory]
    [InlineData("", "rỗng")]
    [InlineData("[1]", "phải là một object")]
    [InlineData("{bị hỏng", "JSON không hợp lệ")]
    [InlineData("{\"schema\":\"advideo.provider/v1\"}", "Sai hình dạng")]
    public void Chuoi_dau_vao_hong(string json, string expected)
    {
        DescriptorParseResult result = ProviderDescriptorParser.Parse(json);

        result.IsValid.Should().BeFalse();
        result.CapabilityJson.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public void Descriptor_qua_dai_bi_tu_choi_truoc_khi_parse()
    {
        string json = "{\"x\":\"" + new string('a', ProviderDescriptorParser.MaxLength) + "\"}";

        ProviderDescriptorParser.Parse(json).Errors.Should().ContainSingle().Which.Should().Contain("vượt trần");
    }

    [Fact]
    public void Chap_nhan_comment_va_dau_phay_thua()
    {
        ProviderDescriptorParser.Parse(Samples.Read(Samples.FalKling)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Poll_none_va_probe_url_khong_doi_status_map()
    {
        DescriptorParseResult probe = Samples.Mutate(Samples.Nova, d =>
        {
            d["poll"] = new JsonObject { ["mode"] = "probeUrl", ["urlPath"] = "url", ["maxWaitSeconds"] = 60 };
        });

        probe.Errors.Should().BeEmpty();

        DescriptorParseResult none = Samples.Mutate(Samples.Nova, d => d.Remove("poll"));

        none.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Errors_va_cost_la_tuy_chon()
    {
        DescriptorParseResult result = Samples.Mutate(Samples.Nova, d =>
        {
            d.Remove("errors");
            d.Remove("cost");
        });

        result.Errors.Should().BeEmpty();
        result.Raw.Should().NotBeNull();
        result.Descriptor!.Errors.Should().BeNull();
    }

    [Fact]
    public void Each_hop_le_voi_item_va_index()
    {
        DescriptorParseResult result = Samples.Mutate(Samples.Nova, d =>
            d.Obj("submit.body.template")["refs"] = new JsonObject
            {
                ["@when"] = "{{image_urls}}",
                ["@each"] = "{{image_urls}}",
                ["@item"] = new JsonObject { ["url"] = "{{item}}", ["i"] = "{{index}}" },
            });

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_nem_khi_thieu_dau_vao()
    {
        ProviderDescriptor descriptor = ProviderDescriptorParser.Parse(Samples.Read(Samples.Nova)).Descriptor!;

        FluentActions.Invoking(() => DescriptorValidator.Validate(null!, new JsonObject())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DescriptorValidator.Validate(descriptor, null!)).Should().Throw<ArgumentNullException>();
    }
}
