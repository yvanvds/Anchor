namespace FocusAgent.Core.Auth;

public interface IAuthTokenProvider
{
    Task<AuthResult> AcquireTokenAsync(CancellationToken ct = default);

    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
