using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace NEXORA.Infrastructure.Persistence;

/// <summary>
/// Validates explicit SQL Server targets before they are passed to EF Core.
/// Development has two explicit lanes: the legacy exact QA target and a member-owned local database.
/// Production remains configurable, but never receives an implicit target.
/// </summary>
public static partial class DatabaseConnectionPolicy
{
    public const string QaServer = @"PC01\SQLEXPRESS";
    public const string QaDatabase = "NEXORA_IntegrationTests";
    public const string QaProfile = "QA";
    public const string PersonalDevelopmentProfile = "PersonalDevelopment";

    private const string SafeConfigurationError =
        "The SQL Server connection configuration is missing or not approved for the selected environment. Configure an explicit approved target.";

    [GeneratedRegex(@"\ANEXORA_Dev_[A-Za-z0-9][A-Za-z0-9_-]{0,49}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonalDatabaseNamePattern();

    public static string Resolve(
        string? connectionString,
        string? environmentName,
        string? databaseProfile = null)
    {
        var builder = ParseExplicitConnection(connectionString);
        var environment = environmentName?.Trim();
        var profile = databaseProfile?.Trim();

        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                // Keep the already-configured owner QA lane working, but accept only its exact target.
                if (!IsApprovedQaTarget(builder))
                {
                    throw new InvalidOperationException(SafeConfigurationError);
                }

                return builder.ConnectionString;
            }

            if (string.Equals(profile, QaProfile, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsApprovedQaTarget(builder))
                {
                    throw new InvalidOperationException(SafeConfigurationError);
                }

                return builder.ConnectionString;
            }

            if (string.Equals(profile, PersonalDevelopmentProfile, StringComparison.OrdinalIgnoreCase) &&
                IsApprovedPersonalDevelopmentTarget(builder))
            {
                return builder.ConnectionString;
            }

            throw new InvalidOperationException(SafeConfigurationError);
        }

        if (string.Equals(environment, "QA", StringComparison.OrdinalIgnoreCase))
        {
            if ((!string.IsNullOrWhiteSpace(profile) &&
                 !string.Equals(profile, QaProfile, StringComparison.OrdinalIgnoreCase)) ||
                !IsApprovedQaTarget(builder))
            {
                throw new InvalidOperationException(SafeConfigurationError);
            }

            return builder.ConnectionString;
        }

        // Non-Development environments cannot opt into a local-only profile.
        if (!string.IsNullOrWhiteSpace(profile))
        {
            throw new InvalidOperationException(SafeConfigurationError);
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

    public static bool IsApprovedPersonalDevelopmentConnectionString(string? connectionString)
    {
        try
        {
            return IsApprovedPersonalDevelopmentTarget(ParseExplicitConnection(connectionString));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsPersonalDevelopmentTarget(
        string? connectionString,
        string? databaseProfile,
        string? environmentName)
    {
        return string.Equals(environmentName?.Trim(), "Development", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(databaseProfile?.Trim(), PersonalDevelopmentProfile, StringComparison.OrdinalIgnoreCase) &&
               IsApprovedPersonalDevelopmentConnectionString(connectionString);
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
               string.IsNullOrWhiteSpace(builder.FailoverPartner) &&
               string.IsNullOrWhiteSpace(builder.AttachDBFilename) &&
               !builder.UserInstance;
    }

    private static bool IsApprovedPersonalDevelopmentTarget(SqlConnectionStringBuilder builder)
    {
        return IsLocalSqlServer(builder.DataSource) &&
               PersonalDatabaseNamePattern().IsMatch(builder.InitialCatalog) &&
               !string.Equals(builder.InitialCatalog, QaDatabase, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(builder.InitialCatalog, "NEXORA", StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrWhiteSpace(builder.FailoverPartner) &&
               string.IsNullOrWhiteSpace(builder.AttachDBFilename) &&
               !builder.UserInstance;
    }

    private static bool IsLocalSqlServer(string dataSource)
    {
        var source = dataSource.Trim();
        if (source.StartsWith(@"(localdb)\", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(source, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase);
        }

        var parts = source.Split('\\', 2, StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
        {
            return false;
        }

        var host = parts[0];
        var instance = parts.Length == 2 ? parts[1] : null;
        if (instance is not null &&
            !string.Equals(instance, "SQLEXPRESS", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(instance, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(host, ".", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "(local)", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
