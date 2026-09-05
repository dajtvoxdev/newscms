using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NewsCMS.Application.Common;
using NewsCMS.Application.KeoBia;

namespace NewsCMS.Infrastructure.KeoBia;

/// <summary>
/// Telegram OpenID Connect flow. Endpoints (authorize / token / JWKS) are discovered
/// from <c>{Authority}/.well-known/openid-configuration</c> and cached/refreshed by
/// <see cref="ConfigurationManager{T}"/>; the id_token signature is validated against
/// the discovered JWKS.
/// </summary>
public sealed class KeoBiaTelegramOidcService : IKeoBiaTelegramOidcService
{
    public const string HttpClientName = "keobia-telegram-oidc";

    private readonly KeoBiaTelegramOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    public KeoBiaTelegramOidcService(IOptions<KeoBiaTelegramOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;

        if (_options.IsOidcConfigured)
        {
            var authority = _options.Authority.TrimEnd('/');
            var metadataAddress = $"{authority}/.well-known/openid-configuration";
            // Allow plain http only for local development authorities (localhost / 127.0.0.1).
            var requireHttps = !authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_httpClientFactory.CreateClient(HttpClientName)) { RequireHttps = requireHttps });
        }
    }

    public bool IsConfigured => _options.IsOidcConfigured;

    public async Task<Result<string>> BuildAuthorizationUrlAsync(
        string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct = default)
    {
        if (_configManager is null)
            return Result<string>.Failure("Đăng nhập Telegram (OIDC) chưa được cấu hình.");

        OpenIdConnectConfiguration config;
        try
        {
            config = await _configManager.GetConfigurationAsync(ct);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Không tải được cấu hình OIDC: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(config.AuthorizationEndpoint))
            return Result<string>.Failure("Provider OIDC thiếu authorization_endpoint.");

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.IsNullOrWhiteSpace(_options.Scope) ? "openid" : _options.Scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var separator = config.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return Result<string>.Success($"{config.AuthorizationEndpoint}{separator}{qs}");
    }

    public async Task<Result<KeoBiaTelegramOidcIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default)
    {
        if (_configManager is null)
            return Result<KeoBiaTelegramOidcIdentity>.Failure("Đăng nhập Telegram (OIDC) chưa được cấu hình.");
        if (string.IsNullOrWhiteSpace(code))
            return Result<KeoBiaTelegramOidcIdentity>.Failure("Thiếu authorization code.");

        OpenIdConnectConfiguration config;
        try
        {
            config = await _configManager.GetConfigurationAsync(ct);
        }
        catch (Exception ex)
        {
            return Result<KeoBiaTelegramOidcIdentity>.Failure($"Không tải được cấu hình OIDC: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(config.TokenEndpoint))
            return Result<KeoBiaTelegramOidcIdentity>.Failure("Provider OIDC thiếu token_endpoint.");

        // ----- Authorization code -> token exchange -----
        string? idToken;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code_verifier"] = codeVerifier
            });

            using var response = await client.PostAsync(config.TokenEndpoint, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return Result<KeoBiaTelegramOidcIdentity>.Failure(
                    $"Token endpoint trả về {(int)response.StatusCode}: {Truncate(body, 300)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            idToken = doc.RootElement.TryGetProperty("id_token", out var idEl) ? idEl.GetString() : null;
        }
        catch (Exception ex)
        {
            return Result<KeoBiaTelegramOidcIdentity>.Failure($"Trao đổi token thất bại: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(idToken))
            return Result<KeoBiaTelegramOidcIdentity>.Failure("Phản hồi token thiếu id_token.");

        // ----- Validate id_token (signature via JWKS, iss/aud/exp/nonce) -----
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = config.Issuer,
            ValidateIssuer = true,
            ValidAudience = _options.ClientId,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        var handler = new JsonWebTokenHandler();
        var validation = await handler.ValidateTokenAsync(idToken, validationParameters);
        if (!validation.IsValid)
            return Result<KeoBiaTelegramOidcIdentity>.Failure(
                $"id_token không hợp lệ: {validation.Exception?.Message ?? "unknown"}");

        var jwt = (JsonWebToken)validation.SecurityToken;

        // Nonce binds this id_token to the authorize request we started.
        var nonce = GetClaim(jwt, "nonce");
        if (!string.IsNullOrEmpty(expectedNonce) &&
            !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            return Result<KeoBiaTelegramOidcIdentity>.Failure("Nonce không khớp (nghi ngờ replay).");

        if (!TryGetTelegramId(jwt, out var telegramId))
        {
            var claims = string.Join(", ", jwt.Claims.Select(c => $"{c.Type}={Truncate(c.Value, 40)}"));
            return Result<KeoBiaTelegramOidcIdentity>.Failure(
                $"id_token thiếu sub (Telegram id) hợp lệ. Claims: {claims}");
        }

        var firstName = GetClaim(jwt, "given_name") ?? GetClaim(jwt, "name");
        var lastName = GetClaim(jwt, "family_name");
        var username = GetClaim(jwt, "preferred_username");
        var photoUrl = GetClaim(jwt, "picture");

        return Result<KeoBiaTelegramOidcIdentity>.Success(
            new KeoBiaTelegramOidcIdentity(telegramId, username, firstName, lastName, photoUrl));
    }

    // Telegram puts the real numeric user id in the non-standard "id" claim. "sub" is an
    // opaque pairwise subject (often negative) and is NOT the user id, so prefer "id".
    private static bool TryGetTelegramId(JsonWebToken jwt, out long telegramId)
    {
        if (jwt.TryGetPayloadValue<long>("id", out telegramId) && telegramId > 0)
            return true;
        if (jwt.TryGetPayloadValue<string>("id", out var idStr) &&
            long.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out telegramId) && telegramId > 0)
            return true;

        // Fallback: derive a stable id from the opaque subject (allow negative).
        if (jwt.TryGetPayloadValue<long>("sub", out telegramId) && telegramId != 0)
            return true;
        return jwt.TryGetPayloadValue<string>("sub", out var subStr) &&
            long.TryParse(subStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out telegramId) && telegramId != 0;
    }

    private static string? GetClaim(JsonWebToken jwt, string type)
    {
        if (jwt.TryGetClaim(type, out var claim) && !string.IsNullOrWhiteSpace(claim.Value))
            return claim.Value;
        // Fall back to the raw payload value for non-string claims.
        return jwt.TryGetPayloadValue<string>(type, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length > max ? value[..max] : value);
}
