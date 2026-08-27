using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// The versioned handshake a connecting ACE extension or Cloud backend presents at a boundary
/// transaction. The Cloud Transaction Authority validates this before authorizing any mutation.
/// </summary>
public sealed record CloudProtocolHandshake(CloudShardId ShardId, CloudComponentVersions Versions);
