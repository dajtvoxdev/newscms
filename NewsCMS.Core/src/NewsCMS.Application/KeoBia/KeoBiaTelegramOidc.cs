using NewsCMS.Application.Common;

namespace NewsCMS.Application.KeoBia;

/// <summary>Identity extracted from a validated Telegram OIDC id_token.</summary>
public sealed record KeoBiaTelegramOidcIdentity(
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PhotoUrl);

/// <summary>
/// Drives the Telegram OpenID Connect authorization-code (+ PKCE) flow:
/// builds the authorize redirect, then exchanges the code and validates the id_token
/// (signature via JWKS, issuer, audience, expiry, nonce).
/// </summary>
public interface IKeoBiaTelegramOidcService
{
    bool IsConfigured { get; }

    /// <summary>Build the provider authorize URL for the given PKCE/state/nonce.</summary>
    Task<Result<string>> BuildAuthorizationUrlAsync(
        string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct = default);

    /// <summary>Exchange the authorization code and return the validated identity.</summary>
    Task<Result<KeoBiaTelegramOidcIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default);
}
