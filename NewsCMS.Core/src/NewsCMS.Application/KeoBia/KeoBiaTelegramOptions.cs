namespace NewsCMS.Application.KeoBia;

/// <summary>
/// Telegram login config. Two verification paths are supported:
///   * Login Widget  — the JS widget, verified by HMAC of the bot token.
///   * OIDC redirect  — an OpenID Connect authorization-code (+ PKCE) flow against
///     an <see cref="Authority"/> that issues an <c>id_token</c>; verified by JWKS.
/// Secrets (BotToken, ClientSecret) must come from user-secrets / env vars, never committed.
/// </summary>
public sealed class KeoBiaTelegramOptions
{
    public const string SectionName = "KeoBia:Telegram";

    /// <summary>Bot username without the leading @ (public, used by the login widget).</summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>Bot token from @BotFather (secret, used to verify the login HMAC).</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Reject a login payload older than this (replay protection).</summary>
    public int MaxAuthAgeSeconds { get; set; } = 86400;

    // ----- OIDC redirect flow -----

    /// <summary>
    /// OIDC issuer base URL. Its <c>/.well-known/openid-configuration</c> is used to
    /// discover the authorization, token, and JWKS endpoints. Empty disables the OIDC flow.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>OAuth2 client_id (for Telegram OIDC this is the bot id, also the id_token aud).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client_secret (secret).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Absolute redirect_uri registered with the provider, e.g.
    /// https://cakeo26.click/keobia/telegram/callback. If empty, it is derived from the request.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Requested scopes; must include openid.</summary>
    public string Scope { get; set; } = "openid profile";

    /// <summary>Webhook settings used by the Telegram bot command surface.</summary>
    public KeoBiaTelegramWebhookOptions Webhook { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotUsername) && !string.IsNullOrWhiteSpace(BotToken);

    /// <summary>True when the OIDC redirect flow has the minimum config to run.</summary>
    public bool IsOidcConfigured =>
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool IsWebhookConfigured =>
        IsConfigured &&
        Webhook.Enabled &&
        !string.IsNullOrWhiteSpace(Webhook.Url) &&
        !string.IsNullOrWhiteSpace(Webhook.Secret);
}

public sealed class KeoBiaTelegramWebhookOptions
{
    public bool Enabled { get; set; }
    public string Secret { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
