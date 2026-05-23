namespace FocusAgent.Core.Auth;

public sealed record AuthResult(
    string AccessToken,
    string Username,
    string DisplayName,
    DateTimeOffset ExpiresOn);
