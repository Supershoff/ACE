namespace ACE.Cloud.Persistence;

/// <summary>
/// Thrown when a Cloud gateway's narrow, expected cross-schema read against ace_shard (character
/// name/account resolution, live allegiance/monarch reads) is refused by MariaDB itself because the
/// companion database identity is missing one of its minimum least-privilege grants (issue #39's
/// blocking human-acceptance fix: local acceptance provisioning granted only <c>ace_cloud.*</c> and
/// omitted the required <c>ace_shard.character</c>/<c>ace_shard.biota_properties_i_i_d</c> SELECT
/// grants). Carries a fixed, safe, operator-actionable message -- never the underlying
/// <see cref="MySqlConnector.MySqlException"/>'s raw text, which embeds the runtime database username
/// and exact schema/table name (DB internals the security baseline forbids surfacing in ordinary API
/// responses or logs), and never any account name or other private data.
/// </summary>
public sealed class CloudDatabasePrivilegeException : Exception
{
    public CloudDatabasePrivilegeException()
        : base("The Cloud database identity is missing a required read permission on ACE character/allegiance data. " +
               "Contact the server operator to verify the companion database's least-privilege grants.")
    {
    }
}
