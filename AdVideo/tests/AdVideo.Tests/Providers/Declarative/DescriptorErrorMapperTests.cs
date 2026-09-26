using System.Net;
using System.Text.Json.Nodes;
using AdVideo.Core.Providers;
using AdVideo.Core.Providers.Descriptors;
using AdVideo.Infrastructure.Providers.Declarative;
using FluentAssertions;

namespace AdVideo.Tests.Providers.Declarative;

public class DescriptorErrorMapperTests
{
    private static readonly DescriptorErrors Errors = new()
    {
        ByBodyCode = new DescriptorBodyCodeMap { Path = "error.code", Table = new() { ["CONTENT_POLICY"] = VideoFailureKind.ContentRejected } },
        ByStatus = new() { ["404"] = VideoFailureKind.Transient, ["4xx"] = VideoFailureKind.ProviderUnavailable },
        Default = VideoFailureKind.Transient,
        DeactivateCredentialOn = ["402", "5xx"],
    };

    [Theory]
    [InlineData(400, "{\"error\":{\"code\":\"content_policy\"}}", VideoFailureKind.ContentRejected)]
    [InlineData(404, "{}", VideoFailureKind.Transient)]
    [InlineData(418, "{}", VideoFailureKind.ProviderUnavailable)]
    [InlineData(500, "{}", VideoFailureKind.ProviderUnavailable)]
    [InlineData(302, "{}", VideoFailureKind.Transient)]
    public void Thu_tu_ma_than_roi_ma_http_roi_luat_chung_roi_default(int status, string body, VideoFailureKind expected)
    {
        DescriptorErrorMapper.FromResponse(Errors, (HttpStatusCode)status, JsonNode.Parse(body), body).Should().Be(expected);
    }

    [Fact]
    public void Khong_khai_errors_thi_dung_luat_chung()
    {
        DescriptorErrorMapper.FromResponse(null, HttpStatusCode.PaymentRequired, null, null).Should().Be(VideoFailureKind.ProviderUnavailable);
        DescriptorErrorMapper.FromResponse(null, HttpStatusCode.NotFound, null, null).Should().Be(VideoFailureKind.Unknown);
        DescriptorErrorMapper.FromFailedBody(null, null, "{\"status\":\"x\"}").Should().Be(VideoFailureKind.ProviderUnavailable);
    }

    [Fact]
    public void Ma_than_khong_co_trong_bang_thi_roi_xuong_tang_duoi()
    {
        JsonNode body = JsonNode.Parse("{\"error\":{\"code\":\"khac\"}}")!;

        DescriptorErrorMapper.FromResponse(Errors, HttpStatusCode.BadRequest, body, body.ToJsonString()).Should().Be(VideoFailureKind.ProviderUnavailable);
        DescriptorErrorMapper.FromFailedBody(Errors, body, body.ToJsonString()).Should().Be(VideoFailureKind.ProviderUnavailable);
    }

    [Theory]
    [InlineData(402, true)]
    [InlineData(503, true)]
    [InlineData(429, false)]
    public void Tat_credential_theo_ma_hoac_lop_ma(int status, bool expected)
    {
        DescriptorErrorMapper.ShouldDeactivate(Errors, (HttpStatusCode)status).Should().Be(expected);
        DescriptorErrorMapper.ShouldDeactivate(null, (HttpStatusCode)status).Should().BeFalse();
    }
}
