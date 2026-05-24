using System.Net.Http.Headers;
using System.Text.Json;
using FocusAgent.Core.Auth;
using FocusAgent.Core.Dtos;
using FocusAgent.Core.Sessions;
using FocusAgent.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAgent.App.Sessions;

/// <summary>
/// Real implementation of <see cref="ISessionRehydrationClient"/> that talks
/// to the backend's <c>GET /sessions/rejoinable</c> endpoint (#54).
///
/// Auth mirrors <see cref="Realtime.SignalRSessionHubConnection"/>: bearer
/// token from <see cref="IAuthTokenProvider"/>, plus the dev-only
/// <c>X-Dev-Impersonate-Oid</c> header when <see cref="DevSettings.ImpersonateOid"/>
/// is set. The header overrides the bearer's <c>oid</c> claim server-side
/// (only in Development), which is how the headless verifier and #44 dev path
/// authenticate without a real WAM token.
/// </summary>
public sealed class HttpSessionRehydrationClient : ISessionRehydrationClient
{
    private const string DevImpersonateOidHeader = "X-Dev-Impersonate-Oid";

    private readonly HttpClient _http;
    private readonly IAuthTokenProvider _tokens;
    private readonly DevSettings _dev;
    private readonly ILogger<HttpSessionRehydrationClient> _log;

    public HttpSessionRehydrationClient(
        HttpClient http,
        IAuthTokenProvider tokens,
        IOptions<BackendSettings> backend,
        IOptions<DevSettings> dev,
        ILogger<HttpSessionRehydrationClient> log)
    {
        _http = http;
        _tokens = tokens;
        _dev = dev.Value;
        _log = log;

        var baseUrl = backend.Value.BaseUrl.TrimEnd('/');
        _http.BaseAddress = new Uri(baseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<SessionStartedPayload>> GetRejoinableSessionsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "sessions/rejoinable");

        var token = await _tokens.GetAccessTokenAsync().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrWhiteSpace(_dev.ImpersonateOid))
        {
            request.Headers.Add(DevImpersonateOidHeader, _dev.ImpersonateOid);
            _log.LogDebug("Rehydration request using impersonation OID {Oid}", _dev.ImpersonateOid);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Backend's SessionSummary.Mode is a SessionMode enum which serializes
        // as an int by default (no JsonStringEnumConverter is configured).
        // Parse via JsonDocument so we tolerate either representation and the
        // agent doesn't have to take a dependency on the backend enum.
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return Array.Empty<SessionStartedPayload>();

        var result = new List<SessionStartedPayload>(doc.RootElement.GetArrayLength());
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            result.Add(new SessionStartedPayload(
                SessionId: el.GetProperty("id").GetGuid(),
                ClassId: el.GetProperty("classId").GetGuid(),
                Mode: ReadModeName(el.GetProperty("mode")),
                StartedAt: el.GetProperty("startedAt").GetDateTimeOffset(),
                JoinCode: el.GetProperty("joinCode").GetString() ?? string.Empty));
        }
        return result;
    }

    // SessionMode values in the backend domain are ordered { Strict = 0,
    // Loose = 1 }. We project them back to the names the rest of the agent
    // already speaks (SessionStartedPayload.Mode is a string).
    private static string ReadModeName(JsonElement modeElement) => modeElement.ValueKind switch
    {
        JsonValueKind.String => modeElement.GetString() ?? "Strict",
        JsonValueKind.Number => modeElement.GetInt32() switch
        {
            0 => "Strict",
            1 => "Loose",
            var n => $"Unknown({n})",
        },
        _ => "Strict",
    };
}
