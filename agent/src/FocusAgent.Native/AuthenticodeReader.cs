using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace FocusAgent.Native;

/// <summary>
/// Reads the Authenticode signer's simple display name (typically the
/// publisher CN) from a signed PE. Best-effort: returns null for unsigned
/// files, corrupt signatures, missing files, or anything else that throws.
/// We deliberately do NOT validate the chain — for allowlist matching we
/// only care about *naming* the embedded signer.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AuthenticodeReader
{
    public static string? ReadPublisher(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is the canonical Authenticode reader on Windows.
            using var cert = X509Certificate.CreateFromSignedFile(executablePath);
#pragma warning restore SYSLIB0057
            return SimpleName(cert.Subject);
        }
        catch
        {
            return null;
        }
    }

    // Subject is a comma-separated RDN string like
    // "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US".
    // Return the CN value if we find one; otherwise fall back to the raw
    // subject so callers still get *something* identifiable.
    private static string? SimpleName(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        foreach (var part in SplitRdn(subject))
        {
            var trimmed = part.TrimStart();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return Unquote(trimmed[3..].Trim());
        }
        return subject.Trim();
    }

    private static IEnumerable<string> SplitRdn(string subject)
    {
        var inQuotes = false;
        var start = 0;
        for (var i = 0; i < subject.Length; i++)
        {
            var c = subject[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                yield return subject[start..i];
                start = i + 1;
            }
        }
        if (start < subject.Length)
            yield return subject[start..];
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }
}
