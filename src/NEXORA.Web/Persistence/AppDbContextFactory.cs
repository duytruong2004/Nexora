using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Web.Persistence;

/// <summary>
/// Design-time configuration intentionally follows the Web project's local settings, user secrets,
/// and environment variables, then passes the result through the same target policy as runtime.
/// It never supplies a connection-string fallback and never opens a connection while creating the context.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environmentName = GetEnvironmentName(args);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ResolveWebProjectDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddUserSecrets(typeof(global::Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var configuredConnection = configuration.GetConnectionString("DefaultConnection");
        var profile = configuration["Database:Profile"];
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(profile, DatabaseConnectionPolicy.PersonalDevelopmentProfile, StringComparison.OrdinalIgnoreCase) ||
            !DatabaseConnectionPolicy.IsApprovedPersonalDevelopmentConnectionString(configuredConnection))
        {
            throw new InvalidOperationException(
                "EF Core tooling is restricted to an explicitly configured local NEXORA_Dev_<Member> database.");
        }

        var connectionString = DatabaseConnectionPolicy.Resolve(
            configuredConnection,
            environmentName,
            profile);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }

    private static string ResolveWebProjectDirectory()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NEXORA.Web.csproj")))
            {
                return current.FullName;
            }

            var webProject = Path.Combine(current.FullName, "src", "NEXORA.Web", "NEXORA.Web.csproj");
            if (File.Exists(webProject))
            {
                return Path.GetDirectoryName(webProject)!;
            }

            current = current.Parent;
        }

        var assemblyDirectory = Path.GetDirectoryName(typeof(global::Program).Assembly.Location);
        return string.IsNullOrWhiteSpace(assemblyDirectory)
            ? Directory.GetCurrentDirectory()
            : assemblyDirectory;
    }

    private static string GetEnvironmentName(string[] args)
    {
        var argumentEnvironment = GetArgumentEnvironmentName(args);
        var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspnetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        foreach (var candidate in new[] { argumentEnvironment, dotnetEnvironment, aspnetEnvironment })
        {
            if (string.Equals(candidate, "Development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "QA", StringComparison.OrdinalIgnoreCase))
            {
                return candidate!;
            }
        }

        return dotnetEnvironment ?? aspnetEnvironment ?? argumentEnvironment ?? "Development";
    }

    private static string? GetArgumentEnvironmentName(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if ((string.Equals(argument, "--environment", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument, "-e", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < args.Length)
            {
                return args[index + 1];
            }

            const string longPrefix = "--environment=";
            if (argument.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument[longPrefix.Length..];
            }
        }

        return null;
    }
}
