namespace ACE.Cloud.Domain;

public sealed record CloudOriginCheckResult(bool IsAllowed, string? Reason)
{
    public static CloudOriginCheckResult Allowed() => new(true, Reason: null);

    public static CloudOriginCheckResult Denied(string reason) => new(false, reason);
}

/// <summary>
/// Strict origin policy for state-changing Cloud backend requests (security baseline: "strict
/// origin policy"). A missing Origin header is denied rather than treated as same-origin: a
/// same-origin fetch/XHR from this deployment's own web client always sends one for state-changing
/// requests, so its absence is itself suspicious rather than merely unhelpful.
/// </summary>
public static class CloudRequestOriginPolicy
{
    public static CloudOriginCheckResult Evaluate(string? originHeader, IReadOnlyCollection<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        if (string.IsNullOrWhiteSpace(originHeader))
        {
            return CloudOriginCheckResult.Denied("Request is missing an Origin header.");
        }

        foreach (var allowedOrigin in allowedOrigins)
        {
            if (string.Equals(originHeader, allowedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return CloudOriginCheckResult.Allowed();
            }
        }

        return CloudOriginCheckResult.Denied($"Origin '{originHeader}' is not an allowed origin for this deployment.");
    }
}
