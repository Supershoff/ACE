using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Tiny ADO.NET helpers shared by every gateway in this project that issues raw SQL on a
/// <see cref="CloudDbContext"/>'s own connection (<see cref="CloudCustodyBoundary"/>,
/// <see cref="CloudIdentityEventGateway"/>, <see cref="CloudAllegianceVaultGateway"/>), so the exact
/// same parameter-binding and duplicate-key detection logic is not repeated in each.
/// </summary>
internal static class CloudRawSqlHelpers
{
    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlException { Number: 1062 };

    /// <summary>MySQL/MariaDB error 1452: "Cannot add or update a child row: a foreign key constraint fails".</summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is MySqlException { Number: 1452 };

    /// <summary>
    /// MySQL/MariaDB error 1142/1143: "SELECT command denied to user ... for table ..." (or column).
    /// Distinguishes a missing least-privilege grant from an ordinary query failure so a caller can
    /// translate it into <see cref="CloudDatabasePrivilegeException"/>'s safe, operator-actionable
    /// message instead of letting the raw exception -- which names the runtime database username and
    /// exact schema/table -- reach an API response or log (issue #39).
    /// </summary>
    public static bool IsAccessDenied(MySqlException ex) =>
        ex.ErrorCode is MySqlErrorCode.TableAccessDenied or MySqlErrorCode.ColumnAccessDenied;
}
