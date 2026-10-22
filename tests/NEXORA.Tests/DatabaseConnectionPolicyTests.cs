using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DatabaseConnectionPolicyTests
{
    private const string QaConnection =
        "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_RejectsMissingConnectionString(string? connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseConnectionPolicy.Resolve(connectionString, "Development"));

        Assert.DoesNotContain("Server=", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsMalformedConnectionWithoutEchoingInput()
    {
        const string secretMarker = "private-test-secret";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionPolicy.Resolve(
                $"Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Password={secretMarker};UnknownKeyword=x",
                "QA"));

        Assert.False(exception.ToString().Contains(secretMarker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    public void Resolve_RejectsMainDatabaseInDevelopmentAndQa(string environmentName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionPolicy.Resolve(
                "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True",
                environmentName));
    }

    [Fact]
    public void InfrastructureRegistration_RejectsMainDatabaseInDevelopment()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddNexoraInfrastructure(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True",
            "Development"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    public void Resolve_SelectsApprovedQaConfigurationWithoutOpeningConnection(string environmentName)
    {
        var selected = DatabaseConnectionPolicy.Resolve(QaConnection, environmentName);
        var parsed = new SqlConnectionStringBuilder(selected);

        Assert.Equal(DatabaseConnectionPolicy.QaServer, parsed.DataSource);
        Assert.Equal(DatabaseConnectionPolicy.QaDatabase, parsed.InitialCatalog);
        Assert.True(parsed.IntegratedSecurity);
    }

    [Fact]
    public void Resolve_AllowsExplicitProductionConfiguration()
    {
        var selected = DatabaseConnectionPolicy.Resolve(
            "Server=approved.example.test;Database=NEXORA;Integrated Security=True",
            "Production");

        Assert.Contains("approved.example.test", selected, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QaPolicy_RejectsSqlAuthenticationAndAttachedDatabase()
    {
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;User ID=qa;Password=not-a-real-secret"));
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;AttachDbFilename=qa.mdf"));
    }

    [Fact]
    public async Task DevelopmentInventoryProvisioning_RejectsMainDatabaseBeforeResolvingServices()
    {
        var result = await DevelopmentInventoryProvisioningCommand.ExecuteAsync(
            services: null!,
            connectionString: "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True",
            logger: NullLogger.Instance);

        Assert.Equal(2, result);
    }

    [Fact]
    public void DesignTimeFactory_RequiresExplicitConnectionAndSelectsQaWithoutConnecting()
    {
        using var environment = new ProcessEnvironmentScope(
            ("ConnectionStrings__DefaultConnection", QaConnection),
            ("DOTNET_ENVIRONMENT", "QA"),
            ("ASPNETCORE_ENVIRONMENT", null));

        using var context = new AppDbContextFactory().CreateDbContext([]);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        var selected = new SqlConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString);
        Assert.Equal(DatabaseConnectionPolicy.QaDatabase, selected.InitialCatalog);
    }

    [Fact]
    public void DesignTimeFactory_FailsWhenConnectionIsMissingAndDoesNotUseFallback()
    {
        using var environment = new ProcessEnvironmentScope(
            ("ConnectionStrings__DefaultConnection", null),
            ("DOTNET_ENVIRONMENT", "Development"),
            ("ASPNETCORE_ENVIRONMENT", null));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new AppDbContextFactory().CreateDbContext([]));

        Assert.DoesNotContain("NEXORA;", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLEXPRESS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesignTimeFactory_DefaultEnvironmentFailsClosedForMainDatabase()
    {
        using var environment = new ProcessEnvironmentScope(
            ("ConnectionStrings__DefaultConnection", "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True"),
            ("DOTNET_ENVIRONMENT", null),
            ("ASPNETCORE_ENVIRONMENT", null));

        Assert.Throws<InvalidOperationException>(
            () => new AppDbContextFactory().CreateDbContext([]));
    }

    [Fact]
    public void DesignTimeFactory_DevelopmentArgumentCannotBypassQaTargetGuard()
    {
        using var environment = new ProcessEnvironmentScope(
            ("ConnectionStrings__DefaultConnection", "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True"),
            ("DOTNET_ENVIRONMENT", "Production"),
            ("ASPNETCORE_ENVIRONMENT", null));

        Assert.Throws<InvalidOperationException>(() =>
            new AppDbContextFactory().CreateDbContext(["--environment", "Development"]));
    }

    [Fact]
    public void DesignTimeFactory_AspNetCoreDevelopmentEnvironmentRequiresQaTarget()
    {
        using var environment = new ProcessEnvironmentScope(
            ("ConnectionStrings__DefaultConnection", "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True"),
            ("DOTNET_ENVIRONMENT", null),
            ("ASPNETCORE_ENVIRONMENT", "Development"));

        Assert.Throws<InvalidOperationException>(
            () => new AppDbContextFactory().CreateDbContext([]));
    }

    private sealed class ProcessEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public ProcessEnvironmentScope(params (string Name, string? Value)[] values)
        {
            _originalValues = values.ToDictionary(
                item => item.Name,
                item => Environment.GetEnvironmentVariable(item.Name));

            foreach (var (name, value) in values)
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            }
        }
    }
}
