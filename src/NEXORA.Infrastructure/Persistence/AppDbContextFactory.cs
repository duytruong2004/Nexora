using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NEXORA.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = DatabaseConnectionPolicy.Resolve(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
            GetEnvironmentName(args));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }

    private static string GetEnvironmentName(string[] args)
    {
        var argumentEnvironment = GetArgumentEnvironmentName(args);
        var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var aspnetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        // If any supplied environment indicator selects Development/QA, retain the stricter target policy.
        foreach (var candidate in new[] { argumentEnvironment, dotnetEnvironment, aspnetEnvironment })
        {
            if (string.Equals(candidate, "Development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "QA", StringComparison.OrdinalIgnoreCase))
            {
                return candidate!;
            }
        }

        // EF tooling without an explicit environment is treated as Development, never as Production.
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
