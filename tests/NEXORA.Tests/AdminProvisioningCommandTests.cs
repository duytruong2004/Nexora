using NEXORA.Web.Identity;
using NEXORA.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace NEXORA.Tests;

public sealed class AdminProvisioningCommandTests
{
    [Fact]
    public void Parse_WithoutProvisioningCommand_PreservesHostArguments()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--urls", "http://127.0.0.1:5000"]);

        Assert.False(parsed.IsRequested);
        Assert.Equal(["--urls", "http://127.0.0.1:5000"], parsed.HostArguments);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
        Assert.False(parsed.IsDevelopmentDataSeedRequested);
        Assert.False(parsed.IsDevelopmentAccountsSeedRequested);
        Assert.False(parsed.IsDevelopmentDatabaseCheckRequested);
    }

    [Fact]
    public void Parse_ExplicitProvisioningCommand_ConsumesTheCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--provision-admin"]);

        Assert.True(parsed.IsRequested);
        Assert.False(parsed.IsDevelopmentInventorySeedRequested);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
        Assert.False(parsed.IsDevelopmentDataSeedRequested);
        Assert.False(parsed.IsDevelopmentAccountsSeedRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_DevelopmentInventorySeedCommandConsumesOnlyTheStandaloneCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--seed-development-inventory"]);

        Assert.True(parsed.IsRequested);
        Assert.True(parsed.IsDevelopmentInventorySeedRequested);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
        Assert.False(parsed.IsDevelopmentDataSeedRequested);
        Assert.False(parsed.IsDevelopmentAccountsSeedRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_QaStaffProvisioningCommandConsumesOnlyTheStandaloneCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--provision-qa-staff"]);

        Assert.True(parsed.IsRequested);
        Assert.False(parsed.IsDevelopmentInventorySeedRequested);
        Assert.True(parsed.IsQaStaffProvisioningRequested);
        Assert.False(parsed.IsDevelopmentDataSeedRequested);
        Assert.False(parsed.IsDevelopmentAccountsSeedRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Theory]
    [InlineData("--seed-development-data")]
    [InlineData("--seed-development-accounts")]
    [InlineData("--check-development-database")]
    public void Parse_RecognizesExplicitPersonalDevelopmentCommands(string command)
    {
        var parsed = ProvisioningCommandArguments.Parse([command]);

        Assert.True(parsed.IsRequested);
        Assert.Empty(parsed.HostArguments);
        Assert.Equal(command == "--seed-development-data", parsed.IsDevelopmentDataSeedRequested);
        Assert.Equal(command == "--seed-development-accounts", parsed.IsDevelopmentAccountsSeedRequested);
        Assert.Equal(command == "--check-development-database", parsed.IsDevelopmentDatabaseCheckRequested);
    }

    [Fact]
    public void DevelopmentInventoryCommand_OnlyAcceptsPersonalLocalDatabaseWithExplicitProfile()
    {
        const string approvedQa = "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True";
        const string mainDatabase = "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True;TrustServerCertificate=True";
        const string unapprovedServer = "Server=OTHER\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True";
        const string personal = "Server=(localdb)\\MSSQLLocalDB;Database=NEXORA_Dev_Alice;Integrated Security=True;TrustServerCertificate=True";

        Assert.True(DatabaseConnectionPolicy.IsApprovedQaConnectionString(approvedQa));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(approvedQa, DatabaseConnectionPolicy.QaProfile));
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(mainDatabase));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.Resolve(mainDatabase, "Development"));
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(unapprovedServer));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.Resolve(unapprovedServer, "Development"));
        Assert.True(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(personal, DatabaseConnectionPolicy.PersonalDevelopmentProfile));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(personal, null));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase("not a connection string", DatabaseConnectionPolicy.PersonalDevelopmentProfile));
    }

    [Fact]
    public void Parse_ProvisioningCommandWithArguments_RequiresInteractiveStandaloneInvocation()
    {
        Assert.Throws<ArgumentException>(() =>
            ProvisioningCommandArguments.Parse(["--provision-admin", "admin@example.test"]));
    }

    [Fact]
    public void DemoAccountConfiguration_RequiresFourDistinctNonRoutableSingleRoleAccounts()
    {
        var values = new Dictionary<string, string?>();
        foreach (var role in NEXORA.Domain.Security.RoleNames.All)
        {
            values[$"DevelopmentAccounts:{role}:Email"] = $"demo.{role.ToLowerInvariant()}@nexora.test";
            values[$"DevelopmentAccounts:{role}:FullName"] = $"Demo {role}";
            values[$"DevelopmentAccounts:{role}:Password"] = "A-local-only-Test-Password-900!";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.True(DevelopmentAccountProvisioningCommand.TryReadDefinitions(configuration, out var accounts, out var error));
        Assert.Empty(error);
        Assert.Equal(4, accounts.Count);
        Assert.Equal(NEXORA.Domain.Security.RoleNames.All, accounts.Select(account => account.RoleName));
        Assert.DoesNotContain("A-local-only-Test-Password-900!", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoAccountConfiguration_RejectsRoutableEmailAndDoesNotEchoPassword()
    {
        var values = new Dictionary<string, string?>();
        foreach (var role in NEXORA.Domain.Security.RoleNames.All)
        {
            values[$"DevelopmentAccounts:{role}:Email"] = $"demo.{role.ToLowerInvariant()}@nexora.test";
            values[$"DevelopmentAccounts:{role}:FullName"] = $"Demo {role}";
            values[$"DevelopmentAccounts:{role}:Password"] = "A-local-only-Test-Password-900!";
        }
        values["DevelopmentAccounts:Admin:Email"] = "admin@example.com";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.False(DevelopmentAccountProvisioningCommand.TryReadDefinitions(configuration, out _, out var error));
        Assert.Contains("non-routable", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("A-local-only-Test-Password-900!", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingDemoAccount_IsKeptOnlyWhenActiveAndAssignedExactlyOneExpectedRole()
    {
        Assert.Equal(ExistingDevelopmentAccountAction.Create,
            DevelopmentAccountProvisioningCommand.DecideExistingAccount(false, false, [], "Admin"));
        Assert.Equal(ExistingDevelopmentAccountAction.KeepUnchanged,
            DevelopmentAccountProvisioningCommand.DecideExistingAccount(true, true, ["Admin"], "Admin"));
        Assert.Equal(ExistingDevelopmentAccountAction.Refuse,
            DevelopmentAccountProvisioningCommand.DecideExistingAccount(true, true, ["Customer"], "Admin"));
        Assert.Equal(ExistingDevelopmentAccountAction.Refuse,
            DevelopmentAccountProvisioningCommand.DecideExistingAccount(true, true, ["Admin", "Customer"], "Admin"));
        Assert.Equal(ExistingDevelopmentAccountAction.Refuse,
            DevelopmentAccountProvisioningCommand.DecideExistingAccount(true, false, ["Admin"], "Admin"));
    }

    [Fact]
    public void RoleSeeder_OnlyAcceptsTheFourCanonicalRoleNames()
    {
        Assert.True(RoleSeeder.HasOnlyCanonicalRoleNames(NEXORA.Domain.Security.RoleNames.All));
        Assert.True(RoleSeeder.HasOnlyCanonicalRoleNames([]));
        Assert.False(RoleSeeder.HasOnlyCanonicalRoleNames(["Customer", "Sales", "Warehouse", "Admin", "Auditor"]));
        Assert.False(RoleSeeder.HasOnlyCanonicalRoleNames(["Customer", "Sales", "Warehouse", "admin"]));
    }

    [Fact]
    public void QaStaffProvisioning_OnlyAcceptsTheDedicatedIntegratedSecurityDatabase()
    {
        Assert.True(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True;TrustServerCertificate=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=OTHER\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=OTHER-PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=.\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=False;User ID=qa"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString("not a connection string"));
    }

    [Theory]
    [InlineData("qa.sales@nexora.test", true)]
    [InlineData("qa.warehouse@nexora.test", true)]
    [InlineData("user@gmail.com", false)]
    [InlineData("qa.user@example.com", false)]
    [InlineData("sales@nexora.test", false)]
    [InlineData("qa.user@nexora.com", false)]
    [InlineData("not-an-email", false)]
    public void QaStaffProvisioning_OnlyAcceptsNonRoutableQaEmailAddresses(string email, bool expected)
    {
        Assert.Equal(expected, QaStaffProvisioningCommand.IsQaEmailAddress(email));
    }
}
