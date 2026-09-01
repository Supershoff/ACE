namespace ACE.Cloud.Domain;

/// <summary>
/// Every fact <see cref="CloudMonarchVaultRecoveryPolicy.Authorize"/> needs, gathered by the
/// persistence layer from a fresh Auth Bridge access-level read (ADM-001), the locked
/// <c>CloudMonarchDeletionDiagnostic</c> row, the request body, and the current mutation gate --
/// never guessed or defaulted, matching every other Cloud policy request record in this namespace.
/// </summary>
public sealed record CloudMonarchVaultRecoveryRequest(
    bool AdminAuthorized,
    CloudMutationGateState GateState,
    bool DiagnosticFound,
    bool AlreadyResolved,
    string? Reason,
    bool Confirmed,
    Guid SourceVaultOwnerId,
    Guid DestinationOwnerId);
