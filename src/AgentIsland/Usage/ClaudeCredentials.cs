namespace AgentIsland.Usage;

/// Claude OAuth credential constants shared across layers. The acquisition
/// flow (env -> credentials file -> refresh -> rotation writeback) lands with
/// the usage layer; the error-message contract is defined here because the
/// activity monitor already matches on it.
public static partial class ClaudeCredentials
{
    /// Emitted when the stored token is structurally valid but missing a scope
    /// the Claude usage endpoint now requires (`user:profile`, added
    /// mid-2026). The UI matches on this exact string to offer re-auth.
    public const string ReauthRequiredMessage = "re-login: claude /login";

    public const string AuthRequiredMessage = "auth required — run claude";

    public static bool IsAuthRecoverableError(string? message) =>
        message is ReauthRequiredMessage or AuthRequiredMessage;
}
