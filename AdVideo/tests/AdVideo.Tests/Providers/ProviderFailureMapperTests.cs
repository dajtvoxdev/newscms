using System.Net;
using AdVideo.Core.Providers;
using AdVideo.Infrastructure.Providers;
using FluentAssertions;

namespace AdVideo.Tests.Providers;

/// <summary>
/// Luật 2 — phân loại lỗi quyết định CÓ TRẢ TIỀN LẦN NỮA hay không (<c>CanRetry</c>).
/// </summary>
public class ProviderFailureMapperTests
{
    [Fact]
    public void Het_tien_402_la_provider_khong_dung_duoc_chu_khong_phai_loi_khong_ro()
    {
        VideoFailureKind kind = ProviderFailureMapper.FromStatus(HttpStatusCode.PaymentRequired, "{\"message\":\"no credit\"}");

        kind.Should().Be(VideoFailureKind.ProviderUnavailable);
        new VideoResult { IsSuccess = false, FailureKind = kind }.CanRetry.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"content_policy\",\"message\":\"x\"}}")]
    [InlineData("{\"detail\":[{\"loc\":[\"body\"],\"msg\":\"x\",\"type\":\"content_policy_violation\"}]}")]
    [InlineData("{\"code\":\"NSFW\"}")]
    public void Ma_kiem_duyet_trong_than_duoc_doc_theo_ma(string body)
    {
        ProviderFailureMapper.FromStatus(HttpStatusCode.UnprocessableEntity, body)
            .Should().Be(VideoFailureKind.ContentRejected);
    }

    [Fact]
    public void Ma_trong_than_thang_ma_http()
    {
        // ElevenLabs trả 401 kèm detail.status = quota_exceeded; 500 kèm rate_limit_exceeded.
        ProviderFailureMapper.FromStatus(HttpStatusCode.InternalServerError, "{\"error\":{\"type\":\"rate_limit_exceeded\"}}")
            .Should().Be(VideoFailureKind.RateLimited);

        ProviderFailureMapper.FromStatus(HttpStatusCode.BadRequest, "{\"detail\":{\"status\":\"quota_exceeded\"}}")
            .Should().Be(VideoFailureKind.ProviderUnavailable);
    }

    [Theory]
    [InlineData("content_policy blocked")]
    [InlineData("content-policy")]
    [InlineData("Content Policy")]
    public void Marker_chuoi_khop_ca_gach_duoi_va_gach_ngang(string body)
    {
        ProviderFailureMapper.LooksLikeContentPolicy(body).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khong phai json")]
    [InlineData("{\"error\":{\"code\":\"something_else\"}}")]
    [InlineData("{\"error\":0}")]
    [InlineData("{bị hỏng")]
    public void Than_khong_co_ma_da_biet_thi_khong_doan(string? body)
    {
        ProviderFailureMapper.FromErrorCode(body).Should().BeNull();
    }

    [Fact]
    public void Ma_nam_qua_sau_thi_khong_doc_de_khoi_nham_du_lieu_doi_lai_tu_request()
    {
        const string body = "{\"a\":{\"b\":{\"c\":{\"d\":{\"code\":\"nsfw\"}}}}}";

        ProviderFailureMapper.FromErrorCode(body).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, VideoFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, VideoFailureKind.ProviderUnavailable)]
    [InlineData(HttpStatusCode.Forbidden, VideoFailureKind.ProviderUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, VideoFailureKind.Unknown)]
    [InlineData(HttpStatusCode.RequestTimeout, VideoFailureKind.Transient)]
    [InlineData(HttpStatusCode.GatewayTimeout, VideoFailureKind.Transient)]
    [InlineData(HttpStatusCode.BadGateway, VideoFailureKind.ProviderUnavailable)]
    [InlineData(HttpStatusCode.NotFound, VideoFailureKind.Unknown)]
    public void Ma_http_khong_kem_ma_trong_than(HttpStatusCode status, VideoFailureKind expected)
    {
        ProviderFailureMapper.FromStatus(status, "{}").Should().Be(expected);
    }

    [Fact]
    public void Hang_doi_bao_hong_khong_ly_do_thi_la_provider_khong_dung_duoc()
    {
        ProviderFailureMapper.FromFailedJobBody("{\"status\":\"FAILED\"}").Should().Be(VideoFailureKind.ProviderUnavailable);
        ProviderFailureMapper.FromFailedJobBody("{\"error\":\"content_policy\"}").Should().Be(VideoFailureKind.ContentRejected);
        ProviderFailureMapper.FromFailedJobBody("{\"error\":{\"code\":\"nsfw\"}}").Should().Be(VideoFailureKind.ContentRejected);
    }
}
