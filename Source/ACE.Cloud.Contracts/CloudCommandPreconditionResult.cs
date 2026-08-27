namespace ACE.Cloud.Contracts;

/// <summary>
/// The outcome of validating a <see cref="CloudCommandEnvelope{TCommand}"/>'s boundary
/// preconditions (<see cref="CloudCommandGuard"/>) before any business rule or mutation runs.
/// </summary>
public sealed record CloudCommandPreconditionResult
{
    public bool Passed { get; }

    public CloudCommandResultKind? FailureKind { get; }

    public string? Reason { get; }

    private CloudCommandPreconditionResult(bool passed, CloudCommandResultKind? failureKind, string? reason)
    {
        Passed = passed;
        FailureKind = failureKind;
        Reason = reason;
    }

    public static CloudCommandPreconditionResult Ok() => new(true, null, null);

    public static CloudCommandPreconditionResult Failed(CloudCommandResultKind failureKind, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed precondition requires a reason.", nameof(reason));
        }

        return new CloudCommandPreconditionResult(false, failureKind, reason);
    }

    /// <summary>
    /// Converts this precondition outcome into a terminal command result. Must not be called when
    /// <see cref="Passed"/> is true; a passing precondition has no payload to resolve <c>Success</c> with.
    /// </summary>
    public CloudCommandResult<TPayload> ToFailureResult<TPayload>()
    {
        if (Passed)
        {
            throw new InvalidOperationException("A passing precondition has no failure result to convert.");
        }

        return FailureKind switch
        {
            CloudCommandResultKind.Conflict => CloudCommandResult<TPayload>.Conflict(Reason!),
            CloudCommandResultKind.Unavailable => CloudCommandResult<TPayload>.Unavailable(Reason!),
            _ => CloudCommandResult<TPayload>.ValidationFailed(Reason!),
        };
    }
}
