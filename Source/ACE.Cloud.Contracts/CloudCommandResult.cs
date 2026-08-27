using System.Text.Json.Serialization;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The explicit outcome of one Cloud boundary command (transaction rules 3, 4, and 8): exactly
/// one of success, conflict, validation failure, unavailable, or idempotent replay. A caller must
/// never infer success from a timeout; it must requery the idempotency record (transaction rule
/// 8), which this type's <see cref="Kind"/> makes an explicit, exhaustive choice instead.
/// </summary>
public sealed record CloudCommandResult<TPayload>
{
    public CloudCommandResultKind Kind { get; }

    /// <summary>The committed or replayed payload; present only for <see cref="CloudCommandResultKind.Success"/> and <see cref="CloudCommandResultKind.IdempotentReplay"/>.</summary>
    public TPayload? Payload { get; }

    /// <summary>The reason a non-success outcome occurred; present for every kind except <see cref="CloudCommandResultKind.Success"/> and <see cref="CloudCommandResultKind.IdempotentReplay"/>.</summary>
    public string? Reason { get; }

    [JsonConstructor]
    private CloudCommandResult(CloudCommandResultKind kind, TPayload? payload, string? reason)
    {
        Kind = kind;
        Payload = payload;
        Reason = reason;
    }

    public static CloudCommandResult<TPayload> Success(TPayload payload) =>
        new(CloudCommandResultKind.Success, RequirePayload(payload), reason: null);

    public static CloudCommandResult<TPayload> IdempotentReplay(TPayload payload) =>
        new(CloudCommandResultKind.IdempotentReplay, RequirePayload(payload), reason: null);

    public static CloudCommandResult<TPayload> Conflict(string reason) =>
        new(CloudCommandResultKind.Conflict, payload: default, RequireReason(reason));

    public static CloudCommandResult<TPayload> ValidationFailed(string reason) =>
        new(CloudCommandResultKind.ValidationFailed, payload: default, RequireReason(reason));

    public static CloudCommandResult<TPayload> Unavailable(string reason) =>
        new(CloudCommandResultKind.Unavailable, payload: default, RequireReason(reason));

    private static TPayload RequirePayload(TPayload payload)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload), "A success or replay result requires its payload.");
        }

        return payload;
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A non-success result requires a reason.", nameof(reason));
        }

        return reason;
    }
}
