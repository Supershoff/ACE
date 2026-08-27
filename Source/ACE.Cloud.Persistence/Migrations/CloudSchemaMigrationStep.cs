namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// One versioned, ordered step of the Cloud schema (OPS-002). Ids sort lexicographically in
/// application order, matching the timestamp-prefixed convention EF Core migrations use, even
/// though these are applied by <see cref="CloudSchemaMigrator"/> rather than EF Core's migrator
/// (see that class for why: this environment could not validate EF Core's migration discovery for
/// the installed Microsoft.EntityFrameworkCore/Pomelo.EntityFrameworkCore.MySql combination, and
/// hand-authoring without the dotnet-ef design-time tool to verify it was too risky to ship).
/// </summary>
public abstract class CloudSchemaMigrationStep
{
    protected CloudSchemaMigrationStep(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A migration step requires a non-empty ID.", nameof(id));
        }

        Id = id;
    }

    public string Id { get; }

    /// <summary>
    /// Forward DDL/DML statements, each executed as one command (a statement may itself be a
    /// compound CREATE TRIGGER body; MariaDB parses that as a single statement server-side).
    /// </summary>
    public abstract IReadOnlyList<string> UpStatements { get; }

    /// <summary>
    /// Statements that exactly undo <see cref="UpStatements"/>, applied in this order.
    /// </summary>
    public abstract IReadOnlyList<string> DownStatements { get; }
}
