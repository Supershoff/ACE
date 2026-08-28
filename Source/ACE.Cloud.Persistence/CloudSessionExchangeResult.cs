namespace ACE.Cloud.Persistence;

public enum CloudSessionExchangeOutcomeKind
{
    Created,
    GrantAlreadyUsed,
}

/// <summary>The outcome of <see cref="CloudSessionGateway.ExchangeGrantForSessionAsync"/> (AUTH-002).</summary>
public sealed record CloudSessionExchangeResult(CloudSessionExchangeOutcomeKind Kind, CloudWebSession? Session)
{
    public bool IsCreated => Kind == CloudSessionExchangeOutcomeKind.Created;

    public static CloudSessionExchangeResult Created(CloudWebSession session) => new(CloudSessionExchangeOutcomeKind.Created, session);

    public static CloudSessionExchangeResult GrantAlreadyUsed() => new(CloudSessionExchangeOutcomeKind.GrantAlreadyUsed, Session: null);
}
