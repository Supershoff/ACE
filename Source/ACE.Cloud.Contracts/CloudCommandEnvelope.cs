using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned envelope every Cloud boundary command travels in: a protocol handshake (ARCH-001,
/// OPS-002), an idempotency key (transaction rule 4), an actor identity (EVT-002), an optional
/// expected aggregate version for a mutation against an existing aggregate (transaction rule 3, no
/// <see cref="ExpectedVersion"/> for a command that creates the aggregate), and the strongly typed
/// command payload itself.
/// </summary>
public sealed record CloudCommandEnvelope<TCommand>
{
    public CloudProtocolHandshake Handshake { get; }

    public CloudIdempotencyKey IdempotencyKey { get; }

    public CloudActorIdentity Actor { get; }

    public CloudAggregateVersion? ExpectedVersion { get; }

    public TCommand Command { get; }

    public DateTimeOffset IssuedAtUtc { get; }

    public CloudCommandEnvelope(
        CloudProtocolHandshake handshake,
        CloudIdempotencyKey idempotencyKey,
        CloudActorIdentity actor,
        TCommand command,
        DateTimeOffset issuedAtUtc,
        CloudAggregateVersion? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(handshake);
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);

        Handshake = handshake;
        IdempotencyKey = idempotencyKey;
        Actor = actor;
        Command = command;
        IssuedAtUtc = issuedAtUtc;
        ExpectedVersion = expectedVersion;
    }
}
