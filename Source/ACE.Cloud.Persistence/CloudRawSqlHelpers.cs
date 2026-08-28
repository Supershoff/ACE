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
}
