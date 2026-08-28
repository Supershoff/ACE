namespace ACE.Cloud.Domain;

/// <summary>
/// Scrubs specific known-sensitive raw values out of a free-text message before it reaches a log,
/// trace, error, or webhook payload (security baseline: "Passwords, login account names, withdrawal
/// tokens, auth grants, connection strings, and raw webhook secrets must never enter normal logs or
/// public telemetry"). Callers pass the exact secret values in play for the current operation (a
/// password, a signed grant token, a session cookie value, a connection string) rather than this
/// type guessing at patterns in arbitrary text -- both more reliable and impossible to bypass by
/// formatting a secret differently than a regex would expect.
/// </summary>
public static class CloudSensitiveValueRedactor
{
    private const string Redacted = "[redacted]";

    public static string Redact(string message, params string?[] sensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(message);

        var result = message;
        foreach (var sensitiveValue in sensitiveValues)
        {
            if (!string.IsNullOrEmpty(sensitiveValue))
            {
                result = result.Replace(sensitiveValue, Redacted, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
