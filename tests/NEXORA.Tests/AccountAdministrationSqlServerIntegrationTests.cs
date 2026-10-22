using System.Data;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Email;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Email;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AccountAdministrationSqlServerIntegrationTests
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261015173046_InitialFoundation",
        "20261016133620_AddCheckoutRequestId",
        "20261019174632_AddOrderCancellationRequestFields"
    ];

    [SqlServerFact]
    public async Task StaffInvitation_CreateActivateAndReplay_AreAtomicPurposeBoundAndOneUse()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var mailbox = provider.GetRequiredService<InMemoryEmailMailbox>();
        var email = NewQaEmail("activation");
        var password = NewPassword();

        try
        {
            AccountOperationResult created;
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                created = await service.CreateStaffAsync(new CreateStaffInput("Phase 6.1 QA Sales", email, RoleNames.Sales), 900001);
                Assert.Equal(AccountOperationStatus.Success, created.Status);
                Assert.NotNull(created.UserId);
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var pending = await userManager.FindByEmailAsync(email);
                Assert.NotNull(pending);
                Assert.False(pending.IsActive);
                Assert.False(pending.EmailConfirmed);
                Assert.Null(pending.PasswordHash);
                Assert.Equal(new[] { RoleNames.Sales }, await userManager.GetRolesAsync(pending));
            }

            var invitationToken = ReadToken(mailbox.GetRecent().Single(message => message.Message.To == email).Message);
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.True(await service.IsInvitationValidAsync(email, invitationToken));
                var activated = await service.ActivateStaffAsync(email, invitationToken, password);
                Assert.Equal(AccountOperationStatus.Success, activated.Status);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var active = await userManager.FindByEmailAsync(email);
                Assert.NotNull(active);
                Assert.True(active.IsActive);
                Assert.True(active.EmailConfirmed);
                Assert.NotNull(active.PasswordHash);
                Assert.Equal(new[] { RoleNames.Sales }, await userManager.GetRolesAsync(active));

                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                var replay = await service.ActivateStaffAsync(email, invitationToken, NewPassword());
                Assert.Equal(AccountOperationStatus.InvalidInvitation, replay.Status);
                Assert.True(active.IsActive);
                Assert.Equal(new[] { RoleNames.Sales }, await userManager.GetRolesAsync(active));
            }
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task StaffInvitation_ResendInvalidatesOldToken_AndDoesNotCreateDuplicate()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var mailbox = provider.GetRequiredService<InMemoryEmailMailbox>();
        var email = NewQaEmail("resend");

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.CreateStaffAsync(new CreateStaffInput("Phase 6.1 QA Resend", email, RoleNames.Warehouse), 900002)).Status);
            }

            var oldToken = ReadToken(mailbox.GetRecent().Single(message => message.Message.To == email).Message);
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success, (await service.ResendInvitationAsync(
                    await GetFixtureUserIdAsync(scope.ServiceProvider, email), 900002)).Status);
                Assert.False(await service.IsInvitationValidAsync(email, oldToken));
            }

            var latestToken = ReadToken(mailbox.GetRecent().First(message => message.Message.To == email).Message);
            Assert.NotEqual(oldToken, latestToken);
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.InvalidInvitation,
                    (await service.ActivateStaffAsync(email, oldToken, NewPassword())).Status);
                Assert.True(await service.IsInvitationValidAsync(email, latestToken));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var normalizedEmail = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().NormalizeEmail(email);
                Assert.Equal(1, await db.Users.AsNoTracking().CountAsync(user => user.NormalizedEmail == normalizedEmail));
            }
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task StaffInvitation_ConcurrentRedemption_AllowsOnlyOneActivation()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var mailbox = provider.GetRequiredService<InMemoryEmailMailbox>();
        var email = NewQaEmail("concurrent");
        var passwordA = NewPassword();
        var passwordB = NewPassword();

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.CreateStaffAsync(new CreateStaffInput("Phase 6.1 QA Concurrent", email, RoleNames.Sales), 900003)).Status);
            }

            var token = ReadToken(mailbox.GetRecent().Single(message => message.Message.To == email).Message);
            var tasks = new[]
            {
                RedeemAsync(provider, email, token, passwordA),
                RedeemAsync(provider, email, token, passwordB)
            };
            var results = await Task.WhenAll(tasks);

            Assert.Single(results, result => result.Status == AccountOperationStatus.Success);
            Assert.Single(results, result => result.Status == AccountOperationStatus.InvalidInvitation);
            await using var verificationScope = provider.CreateAsyncScope();
            var users = verificationScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var activated = await users.FindByEmailAsync(email);
            Assert.NotNull(activated);
            Assert.True(activated.IsActive);
            Assert.True(activated.EmailConfirmed);
            Assert.NotNull(activated.PasswordHash);
            Assert.Equal(new[] { RoleNames.Sales }, await users.GetRolesAsync(activated));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task StaffInvitation_ExpiredTokenAndSuspendedAccount_CannotActivateOrBeUnlockedAsPending()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var mailbox = provider.GetRequiredService<InMemoryEmailMailbox>();
        var email = NewQaEmail("suspended");

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                var created = await service.CreateStaffAsync(
                    new CreateStaffInput("Phase 6.1 QA Suspended", email, RoleNames.Warehouse), 900004);
                Assert.Equal(AccountOperationStatus.Success, created.Status);
                var unlockedPending = await service.UnlockUserAsync(created.UserId!.Value, 900004);
                Assert.Equal(AccountOperationStatus.SecurityBlocked, unlockedPending.Status);
            }

            var invitation = ReadToken(mailbox.GetRecent().Single(message => message.Message.To == email).Message);
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.ActivateStaffAsync(email, invitation, NewPassword())).Status);
                var userId = await GetFixtureUserIdAsync(scope.ServiceProvider, email);
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.LockUserAsync(userId, 900005)).Status);
                Assert.False(await service.IsInvitationValidAsync(email, invitation));
                Assert.Equal(AccountOperationStatus.InvalidInvitation,
                    (await service.ActivateStaffAsync(email, invitation, NewPassword())).Status);
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.UnlockUserAsync(userId, 900005)).Status);
            }

            await using var verifyScope = provider.CreateAsyncScope();
            var manager = verifyScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var unlocked = await manager.FindByEmailAsync(email);
            Assert.NotNull(unlocked);
            Assert.True(unlocked.IsActive);
            Assert.True(unlocked.EmailConfirmed);
            Assert.NotNull(unlocked.PasswordHash);
            Assert.Equal(new[] { RoleNames.Warehouse }, await manager.GetRolesAsync(unlocked));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task StaffCreation_EmailDeliveryFailureLeavesAccountInactiveAndResendReusesIt()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString, emailSender: new FailingEmailSender());
        var email = NewQaEmail("delivery-failure");

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                var result = await service.CreateStaffAsync(
                    new CreateStaffInput("Phase 6.1 QA Delivery", email, RoleNames.Sales), 900006);
                Assert.Equal(AccountOperationStatus.DeliveryFailed, result.Status);
                Assert.NotNull(result.UserId);
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var pending = await manager.FindByEmailAsync(email);
                Assert.NotNull(pending);
                Assert.False(pending.IsActive);
                Assert.False(pending.EmailConfirmed);
                Assert.Null(pending.PasswordHash);

                var resend = await service.ResendInvitationAsync(pending.Id, 900006);
                Assert.Equal(AccountOperationStatus.DeliveryFailed, resend.Status);
                Assert.Equal(pending.Id, resend.UserId);
            }

            await using var verificationScope = provider.CreateAsyncScope();
            var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Users.AsNoTracking().CountAsync(user => user.Email == email));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task DuplicateStaffCreationRace_CreatesOneUserAndOneRoleMapping()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var email = NewQaEmail("duplicate-race");
        var input = new CreateStaffInput("Phase 6.1 QA Duplicate", email, RoleNames.Sales);

        try
        {
            var results = await Task.WhenAll(
                CreateStaffAsync(provider, input, 900007),
                CreateStaffAsync(provider, input, 900008));
            Assert.Single(results, result => result.Status == AccountOperationStatus.Success);
            Assert.Single(results, result => result.Status == AccountOperationStatus.Conflict);

            await using var scope = provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await manager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(new[] { RoleNames.Sales }, await manager.GetRolesAsync(user));
            Assert.False(user.IsActive);
            Assert.Null(user.PasswordHash);
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task IdentityRoleAssignmentFailure_RollsBackTheNewUser()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString, rejectRoleAssignment: true);
        var email = NewQaEmail("role-rollback");

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
            var result = await service.CreateStaffAsync(
                new CreateStaffInput("Phase 6.1 QA Rollback", email, RoleNames.Sales), 900009);
            Assert.Equal(AccountOperationStatus.ValidationError, result.Status);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(user => user.Email == email));
            Assert.False(await db.UserRoles.AsNoTracking().AnyAsync(userRole =>
                db.Users.Any(user => user.Id == userRole.UserId && user.Email == email)));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task StaffRoleChange_ReplacesRoleAtomicallyAndRotatesSecurityStamp()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var email = NewQaEmail("role-change");

        try
        {
            int userId;
            string? oldStamp;
            await using (var scope = provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var user = await CreateActiveStaffAsync(manager, email, RoleNames.Sales);
                userId = user.Id;
                oldStamp = user.SecurityStamp;
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                var result = await service.ChangeStaffRoleAsync(userId, RoleNames.Warehouse, 900010);
                Assert.Equal(AccountOperationStatus.Success, result.Status);
            }

            await using var verify = provider.CreateAsyncScope();
            var userManager = verify.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var changed = await userManager.FindByEmailAsync(email);
            Assert.NotNull(changed);
            Assert.NotEqual(oldStamp, changed.SecurityStamp);
            Assert.Equal(new[] { RoleNames.Warehouse }, await userManager.GetRolesAsync(changed));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task LastActiveAdmin_ConcurrentLockRequestsAreRejectedWithoutChangingExistingAdmin()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        var existingActiveAdminIds = await GetActiveAdminIdsAsync(connectionString);
        Assert.Single(existingActiveAdminIds);
        var adminId = existingActiveAdminIds[0];
        await using var provider = BuildProvider(connectionString);

        var results = await Task.WhenAll(
            LockUserAsync(provider, adminId, 900011),
            LockUserAsync(provider, adminId, 900012));
        Assert.All(results, result => Assert.Equal(AccountOperationStatus.SecurityBlocked, result.Status));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Users.AsNoTracking().SingleAsync(user => user.Id == adminId);
        Assert.True(account.IsActive);
        Assert.Contains(RoleNames.Admin, await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetRolesAsync(account));
    }

    [SqlServerFact]
    public async Task ExpiredStaffInvitationToken_IsRejected()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString, tokenLifetime: TimeSpan.FromMilliseconds(1));
        var mailbox = provider.GetRequiredService<InMemoryEmailMailbox>();
        var email = NewQaEmail("expired");

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.CreateStaffAsync(new CreateStaffInput("Phase 6.1 QA Expired", email, RoleNames.Sales), 900013)).Status);
            }

            var token = ReadToken(mailbox.GetRecent().Single(message => message.Message.To == email).Message);
            await Task.Delay(50);
            await using var verifyScope = provider.CreateAsyncScope();
            var serviceToCheck = verifyScope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
            Assert.False(await serviceToCheck.IsInvitationValidAsync(email, token));
            Assert.Equal(AccountOperationStatus.InvalidInvitation,
                (await serviceToCheck.ActivateStaffAsync(email, token, NewPassword())).Status);
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task CustomerCannotBePromotedThroughStaffRoleChange()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var email = NewQaEmail("customer-promotion");

        try
        {
            int userId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var customer = await CreateActiveStaffAsync(manager, email, RoleNames.Customer);
                userId = customer.Id;
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.SecurityBlocked,
                    (await service.ChangeStaffRoleAsync(userId, RoleNames.Sales, 900014)).Status);
            }

            await using var verification = provider.CreateAsyncScope();
            var managerAfter = verification.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var unchanged = await managerAfter.FindByEmailAsync(email);
            Assert.NotNull(unchanged);
            Assert.Equal(new[] { RoleNames.Customer }, await managerAfter.GetRolesAsync(unchanged));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task UnlockDoesNotReactivateAnAccountWithMultipleRoles()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var email = NewQaEmail("unlock-multi-role");

        try
        {
            int userId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    FullName = "NEXORA Phase 6.1 QA inconsistent role fixture",
                    EmailConfirmed = true,
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var created = await manager.CreateAsync(user, NewPassword());
                Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(error => error.Code)));
                Assert.True((await manager.AddToRoleAsync(user, RoleNames.Customer)).Succeeded);
                Assert.True((await manager.AddToRoleAsync(user, RoleNames.Sales)).Succeeded);
                userId = user.Id;
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                var result = await service.UnlockUserAsync(userId, 900015);
                Assert.Equal(AccountOperationStatus.SecurityBlocked, result.Status);
                Assert.Equal("AccountRoleStateInconsistent", result.ErrorCode);
            }

            await using var verifyScope = provider.CreateAsyncScope();
            var database = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await database.Users.AsNoTracking().SingleAsync(user => user.Id == userId);
            Assert.False(unchanged.IsActive);
            var userManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.Equal(new[] { RoleNames.Customer, RoleNames.Sales },
                (await userManager.GetRolesAsync(unchanged)).OrderBy(role => role, StringComparer.Ordinal));
        }
        finally
        {
            await DeleteFixtureAsync(provider, email);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        TimeSpan? tokenLifetime = null,
        IEmailSender? emailSender = null,
        bool rejectRoleAssignment = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddNexoraInfrastructure(connectionString, "QA");
        services.AddOptions<StaffInvitationOptions>().Configure(options => options.PublicBaseUrl = "https://nexora.test");
        services.Configure<StaffInvitationTokenProviderOptions>(options =>
            options.TokenLifespan = tokenLifetime ?? TimeSpan.FromHours(24));
        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<StaffInvitationTokenProvider>(StaffInvitationPurpose.ProviderName);

        if (rejectRoleAssignment)
        {
            services.AddScoped<UserManager<AppUser>>(provider => new RejectingRoleAssignmentUserManager(
                provider.GetRequiredService<IUserStore<AppUser>>(),
                provider.GetRequiredService<IOptions<IdentityOptions>>(),
                provider.GetRequiredService<IPasswordHasher<AppUser>>(),
                provider.GetServices<IUserValidator<AppUser>>(),
                provider.GetServices<IPasswordValidator<AppUser>>(),
                provider.GetRequiredService<ILookupNormalizer>(),
                provider.GetRequiredService<IdentityErrorDescriber>(),
                provider,
                provider.GetRequiredService<ILogger<UserManager<AppUser>>>()));
        }

        services.AddSingleton<InMemoryEmailMailbox>();
        services.AddScoped<IEmailSender>(_ => emailSender ?? new DevelopmentEmailSender(
            _.GetRequiredService<InMemoryEmailMailbox>()));
        services.AddScoped<IAccountAdministrationService, IdentityAccountAdministrationService>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<string> GetVerifiedQaConnectionStringAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION");
        Assert.True(QaStaffProvisioningCommand.IsApprovedQaConnectionString(connectionString),
            "SQL tests only accept Windows Authentication to PC01\\SQLEXPRESS / NEXORA_IntegrationTests.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var target = connection.CreateCommand())
        {
            target.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')), DB_NAME(), CONVERT(nvarchar(30),DATABASEPROPERTYEX(DB_NAME(),'Status'));";
            await using var reader = await target.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("PC01\\SQLEXPRESS", reader.GetString(0), ignoreCase: true);
            Assert.Equal("NEXORA_IntegrationTests", reader.GetString(1), ignoreCase: true);
            Assert.Equal("ONLINE", reader.GetString(2), ignoreCase: true);
        }

        await using (var migrationQuery = connection.CreateCommand())
        {
            migrationQuery.CommandText = "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await migrationQuery.ExecuteReaderAsync();
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            Assert.Equal(ExpectedMigrations, actual);
        }

        await using (var schemaQuery = connection.CreateCommand())
        {
            schemaQuery.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;";
            Assert.Equal(27, Convert.ToInt32(await schemaQuery.ExecuteScalarAsync()));
        }

        await using (var roleQuery = connection.CreateCommand())
        {
            roleQuery.CommandText = "SELECT name FROM dbo.roles ORDER BY name;";
            await using var reader = await roleQuery.ExecuteReaderAsync();
            var roles = new List<string>();
            while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
            Assert.Equal(new[] { RoleNames.Admin, RoleNames.Customer, RoleNames.Sales, RoleNames.Warehouse }, roles);
        }

        return connectionString!;
    }

    private static async Task<int[]> GetActiveAdminIdsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT u.id FROM dbo.users u JOIN dbo.user_roles ur ON ur.user_id=u.id JOIN dbo.roles r ON r.id=ur.role_id WHERE u.is_active=1 AND r.normalized_name='ADMIN' ORDER BY u.id;";
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<int>();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        return ids.ToArray();
    }

    private static async Task<AccountOperationResult> CreateStaffAsync(
        ServiceProvider provider,
        CreateStaffInput input,
        int actorUserId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>()
            .CreateStaffAsync(input, actorUserId);
    }

    private static async Task<AccountOperationResult> RedeemAsync(
        ServiceProvider provider,
        string email,
        string token,
        string password)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>()
            .ActivateStaffAsync(email, token, password);
    }

    private static async Task<AccountOperationResult> LockUserAsync(ServiceProvider provider, int userId, int actorUserId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>()
            .LockUserAsync(userId, actorUserId);
    }

    private static async Task<int> GetFixtureUserIdAsync(IServiceProvider services, string email)
    {
        var user = await services.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    private static async Task<AppUser> CreateActiveStaffAsync(
        UserManager<AppUser> userManager,
        string email,
        string role)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = "NEXORA Phase 6.1 SQL fixture",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var created = await userManager.CreateAsync(user, NewPassword());
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(error => error.Code)));
        var assigned = await userManager.AddToRoleAsync(user, role);
        Assert.True(assigned.Succeeded, string.Join(",", assigned.Errors.Select(error => error.Code)));
        return user;
    }

    private static async Task DeleteFixtureAsync(ServiceProvider provider, string email)
    {
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await manager.FindByEmailAsync(email);
        if (user is not null)
        {
            var deleted = await manager.DeleteAsync(user);
            Assert.True(deleted.Succeeded, string.Join(",", deleted.Errors.Select(error => error.Code)));
        }
    }

    private static string ReadToken(EmailMessage message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message.HtmlBody, "href=\\\"([^\\\"]+)\\\"");
        Assert.True(match.Success, "Captured QA email should contain an invitation link.");
        var url = WebUtility.HtmlDecode(match.Groups[1].Value);
        var uri = new Uri(url);
        Assert.Empty(uri.Query);
        Assert.NotEmpty(uri.Fragment);
        var query = uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var tokenPair = query.Select(part => part.Split('=', 2))
            .Single(pair => Uri.UnescapeDataString(pair[0]) == "token");
        return Uri.UnescapeDataString(tokenPair[1].Replace('+', ' '));
    }

    private static string NewQaEmail(string scenario) => $"qa.phase61.{scenario}.{Guid.NewGuid():N}@nexora.test";

    private static string NewPassword() => $"Nx!Qa{Guid.NewGuid():N}aA1";

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("QA-only simulated delivery failure.");
    }

    private sealed class RejectingRoleAssignmentUserManager(
        IUserStore<AppUser> store,
        IOptions<IdentityOptions> options,
        IPasswordHasher<AppUser> passwordHasher,
        IEnumerable<IUserValidator<AppUser>> userValidators,
        IEnumerable<IPasswordValidator<AppUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<AppUser>> logger)
        : UserManager<AppUser>(store, options, passwordHasher, userValidators, passwordValidators,
            keyNormalizer, errors, services, logger)
    {
        public override Task<IdentityResult> AddToRoleAsync(AppUser user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "QA_TEST_ROLE_ASSIGNMENT_REJECTED" }));
    }
}
