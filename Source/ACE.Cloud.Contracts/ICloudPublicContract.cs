namespace ACE.Cloud.Contracts;

/// <summary>
/// Marks a contract shape as safe to reach an unauthenticated public surface or an authorized
/// browser's Live State Stream (EVT-007, MKT-201). Every type that implements this interface is
/// covered by the reflection-based privacy sweep that proves it carries no private account names,
/// Withdrawal Tokens, or other secret-bearing material (see the public-contract privacy tests).
/// A contract with no marker is private by default and must never be serialized directly onto a
/// public or Live State Stream surface.
/// </summary>
public interface ICloudPublicContract
{
}
