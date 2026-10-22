using Microsoft.Data.SqlClient;

namespace NEXORA.Infrastructure.Persistence;

/// <summary>
/// Validates explicit SQL Server configuration before it is passed to EF Core.
/// Development and QA are deliberately pinned to the dedicated QA database.
/// Production remains configurable, but never receives an implicit target.
/// </summary>
public static class DatabaseConnectionPolicy
{
    public const string QaServer = @"PC01\SQLEXPRESS";
    public const string QaDatabase = "NEXORA_IntegrationTests";

    private const string SafeConfigurationError =
        "The SQL Server connection configuration is missing or not approved for the selected environment. Configure an explicit approved target.";

    public static string Resolve(string? connectionString, string? environmentName)
    {
        var builder = ParseExplicitConnection(connectionString);
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "QA", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsApprovedQaTarget(builder))
            {
                throw new InvalidOperationException(SafeConfigurationError);
            }
        }

        return builder.ConnectionString;
    }

    public static bool IsApprovedQaConnectionString(string? connectionString)
    {
        try
        {
            return IsApprovedQaTarget(ParseExplicitConnection(connectionString));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static SqlConnectionStringBuilder ParseExplicitConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(SafeConfigurationError);
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) ||
                string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new InvalidOperationException(SafeConfigurationError);
            }

            return builder;
        }
        catch (ArgumentException)
        {
            // SqlClient parser errors can echo input fragments. Do not preserve the original exception.
            throw new InvalidOperationException(SafeConfigurationError);
        }
    }

    private static bool IsApprovedQaTarget(SqlConnectionStringBuilder builder)
    {
        return string.Equals(builder.DataSource.Trim(), QaServer, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(builder.InitialCatalog, QaDatabase, StringComparison.OrdinalIgnoreCase) &&
               builder.IntegratedSecurity &&
               string.IsNullOrWhiteSpace(builder.UserID) &&
               string.IsNullOrEmpty(builder.Password) &&
               string.IsNullOrWhiteSpace(builder.AttachDBFilename) &&
               !builder.UserInstance;
    }
}
