using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Email;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Identity;

public sealed class IdentityAccountAdministrationService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IEmailSender emailSender,
    IOptions<StaffInvitationOptions> invitationOptions,
    ILogger<IdentityAccountAdministrationService> logger) : IAccountAdministrationService
{
    private const string AccountPolicyLock = "NEXORA:ACCOUNT:POLICY";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    public async Task<ManagedAccountPage> SearchUsersAsync(
        AccountSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var requestedPage = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 50);
        var search = filter.Search?.Trim();
        if (search?.Length > 256)
        {
            search = search[..256];
        }
        var role = NormalizeRoleFilter(filter.Role);

        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user => user.FullName.Contains(search) ||
                                        (user.Email != null && user.Email.Contains(search)));
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(user => user.IsActive == filter.IsActive.Value);
        }

        if (role is not null)
        {
            var normalizedRole = roleManager.NormalizeKey(role);
            query = query.Where(user => db.UserRoles.Any(userRole => userRole.UserId == user.Id &&
                db.Roles.Any(candidate => candidate.Id == userRole.RoleId && candidate.NormalizedName == normalizedRole)));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Min(requestedPage, pageCount);
        var users = await query.OrderBy(user => user.Email).ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.IsActive,
                user.EmailConfirmed,
                HasPassword = user.PasswordHash != null,
                user.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var ids = users.Select(user => user.Id).ToArray();
        var roleRows = await (
            from userRole in db.UserRoles.AsNoTracking()
            join candidate in db.Roles.AsNoTracking() on userRole.RoleId equals candidate.Id
            where ids.Contains(userRole.UserId)
            orderby candidate.Name
            select new { userRole.UserId, candidate.Name }).ToListAsync(cancellationToken);

        var rolesByUser = roleRows.GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group
                .Select(item => item.Name ?? string.Empty).ToArray());
        var items = users.Select(user =>
        {
            var roles = rolesByUser.GetValueOrDefault(user.Id, []);
            var state = StaffAccountStatePolicy.Classify(user.IsActive, user.EmailConfirmed, user.HasPassword, roles);
            return new ManagedAccountRow(user.Id, user.FullName, user.Email ?? string.Empty, roles,
                user.IsActive, user.EmailConfirmed, user.HasPassword, user.CreatedAt, state);
        }).ToArray();

        return new ManagedAccountPage(items, total, page, pageSize);
    }

    public async Task<ManagedAccountDetails?> GetUserDetailsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.FullName,
                candidate.Email,
                candidate.IsActive,
                candidate.EmailConfirmed,
                HasPassword = candidate.PasswordHash != null,
                candidate.CreatedAt,
                candidate.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = (await db.UserRoles.AsNoTracking()
                .Where(userRole => userRole.UserId == userId)
                .Join(db.Roles.AsNoTracking(), userRole => userRole.RoleId, candidate => candidate.Id,
                    (_, candidate) => candidate.Name ?? string.Empty)
                .OrderBy(name => name)
                .ToListAsync(cancellationToken))
            .ToArray();
        var state = StaffAccountStatePolicy.Classify(user.IsActive, user.EmailConfirmed, user.HasPassword, roles);

        return new ManagedAccountDetails(user.Id, user.FullName, user.Email ?? string.Empty, roles,
            user.IsActive, user.EmailConfirmed, user.HasPassword, user.CreatedAt, user.UpdatedAt, state);
    }

    public async Task<AccountOperationResult> CreateStaffAsync(
        CreateStaffInput input,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var fullName = input.FullName?.Trim();
        var email = input.Email?.Trim();
        var role = input.Role?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 ||
            !IsValidEmail(email) || !StaffAccountStatePolicy.IsStaffRole(role))
        {
            return new(AccountOperationStatus.ValidationError, ErrorCode: "InvalidStaffInput");
        }

        var normalizedEmail = userManager.NormalizeEmail(email!);
        var normalizedUserName = userManager.NormalizeName(email!);
        var execution = new AccountExecutionState();
        AccountOperationResult result;
        try
        {
            result = await ExecuteTransactionalAsync(
                async token =>
                {
                    db.ChangeTracker.Clear();
                    execution.Reset();
                    if (!await AcquireAccountPolicyLockAsync(token))
                    {
                        return new(AccountOperationStatus.Conflict, ErrorCode: "AccountPolicyBusy");
                    }

                    if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail ||
                                                       user.NormalizedUserName == normalizedUserName, token))
                    {
                        return new(AccountOperationStatus.Conflict, ErrorCode: "EmailAlreadyInUse");
                    }

                    if (!await roleManager.RoleExistsAsync(role!))
                    {
                        return new(AccountOperationStatus.SecurityBlocked, ErrorCode: "StaffRoleUnavailable");
                    }

                    var user = new AppUser
                    {
                        UserName = email!,
                        Email = email!,
                        FullName = fullName!,
                        IsActive = false,
                        EmailConfirmed = false,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    var created = await userManager.CreateAsync(user);
                    if (!created.Succeeded)
                    {
                        return new(AccountOperationStatus.ValidationError,
                            ErrorCode: SafeIdentityErrorCode(created));
                    }

                    var assigned = await userManager.AddToRoleAsync(user, role!);
                    if (!assigned.Succeeded)
                    {
                        throw new IdentityOperationRejectedException(SafeIdentityErrorCode(assigned));
                    }

                    var tokenValue = await userManager.GenerateUserTokenAsync(
                        user, StaffInvitationPurpose.ProviderName, StaffInvitationPurpose.TokenPurpose);
                    execution.UserId = user.Id;
                    execution.ExpectedSecurityStamp = user.SecurityStamp;
                    execution.Invitation = new InvitationPayload(user.Id, user.Email!, user.FullName, tokenValue);
                    return new(AccountOperationStatus.Success, user.Id);
                },
                async token =>
                {
                    db.ChangeTracker.Clear();
                    if (execution.UserId is not int userId || execution.ExpectedSecurityStamp is null)
                    {
                        return false;
                    }

                    return await IsUserInStateAsync(userId, normalizedEmail, role!,
                        isActive: false, emailConfirmed: false, hasPassword: false,
                        execution.ExpectedSecurityStamp, token);
                }, cancellationToken);
        }
        catch (IdentityOperationRejectedException exception)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning("Staff creation rolled back after Identity rejected role assignment. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Outcome={Outcome}, ErrorCode={ErrorCode}, CorrelationId={CorrelationId}",
                actorUserId, execution.UserId, "RolledBack", exception.ErrorCode, CorrelationId());
            return new(AccountOperationStatus.ValidationError, ErrorCode: exception.ErrorCode);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var duplicate = await db.Users.AsNoTracking().AnyAsync(
                user => user.NormalizedEmail == normalizedEmail || user.NormalizedUserName == normalizedUserName,
                cancellationToken);
            return new(AccountOperationStatus.Conflict,
                ErrorCode: duplicate ? "EmailAlreadyInUse" : "PersistenceConflict");
        }

        if (result.Status == AccountOperationStatus.Success && execution.Invitation is not null)
        {
            var sent = await SendInvitationAsync(execution.Invitation, actorUserId, "Created", cancellationToken);
            if (!sent)
            {
                result = new(AccountOperationStatus.DeliveryFailed, result.UserId, "InvitationDeliveryFailed");
            }
        }

        logger.LogInformation("Staff account creation. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, NewRole={NewRole}, Outcome={Outcome}, CorrelationId={CorrelationId}",
            actorUserId, result.UserId, "CreateStaff", role, result.Status.ToString(), CorrelationId());
        return result;
    }

    public Task<AccountOperationResult> LockUserAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default) => ExecuteUserMutationAsync(
        targetUserId,
        actorUserId,
        "LockUser",
        async (user, roles, token) =>
        {
            if (targetUserId == actorUserId)
            {
                return new(AccountOperationStatus.Forbidden, targetUserId, "CannotLockSelf");
            }

            if (!user.IsActive)
            {
                return new(AccountOperationStatus.AlreadyInState, targetUserId, "AccountAlreadyInactive");
            }

            if (roles.Contains(RoleNames.Admin, StringComparer.Ordinal) &&
                await CountActiveAdminsAsync(token) <= 1)
            {
                return new(AccountOperationStatus.SecurityBlocked, targetUserId, "LastActiveAdmin");
            }

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            return await PersistWithNewSecurityStampAsync(user, targetUserId);
        },
        async token => await IsAccountActiveAsync(targetUserId, false, token),
        cancellationToken);

    public Task<AccountOperationResult> UnlockUserAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default) => ExecuteUserMutationAsync(
        targetUserId,
        actorUserId,
        "UnlockUser",
        async (user, roles, token) =>
        {
            if (user.IsActive)
            {
                return new(AccountOperationStatus.AlreadyInState, targetUserId, "AccountAlreadyActive");
            }

            if (roles.Count != 1 || !StaffAccountStatePolicy.IsKnownRole(roles[0]) || user.PasswordHash is null)
            {
                return new(AccountOperationStatus.SecurityBlocked, targetUserId,
                    roles.Count == 1 && StaffAccountStatePolicy.IsKnownRole(roles[0])
                    ? "AccountHasNotBeenActivated"
                    : "AccountRoleStateInconsistent");
            }

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            return await PersistWithNewSecurityStampAsync(user, targetUserId);
        },
        async token => await IsAccountActiveAsync(targetUserId, true, token),
        cancellationToken);

    public Task<AccountOperationResult> ChangeStaffRoleAsync(
        int targetUserId,
        string newRole,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var requestedRole = newRole?.Trim();
        if (!StaffAccountStatePolicy.IsStaffRole(requestedRole))
        {
            return Task.FromResult(new AccountOperationResult(AccountOperationStatus.Forbidden,
                targetUserId, "RoleNotAllowed"));
        }

        string? oldRoleForLog = null;
        var operation = ExecuteUserMutationAsync(targetUserId, actorUserId, "ChangeStaffRole",
            async (user, roles, token) =>
            {
                if (targetUserId == actorUserId)
                {
                    return new(AccountOperationStatus.Forbidden, targetUserId, "CannotChangeOwnRole");
                }

                if (roles.Count != 1 || !StaffAccountStatePolicy.IsStaffRole(roles[0]))
                {
                    return new(AccountOperationStatus.SecurityBlocked, targetUserId, "AccountRoleStateInconsistent");
                }

                var previousRole = roles[0];
                oldRoleForLog = previousRole;
                if (string.Equals(previousRole, requestedRole, StringComparison.Ordinal))
                {
                    return new(AccountOperationStatus.AlreadyInState, targetUserId, "RoleAlreadyAssigned");
                }

                var removed = await userManager.RemoveFromRoleAsync(user, previousRole);
                if (!removed.Succeeded)
                {
                    throw new IdentityOperationRejectedException(SafeIdentityErrorCode(removed));
                }

                var added = await userManager.AddToRoleAsync(user, requestedRole!);
                if (!added.Succeeded)
                {
                    throw new IdentityOperationRejectedException(SafeIdentityErrorCode(added));
                }

                user.UpdatedAt = DateTime.UtcNow;
                var stamp = await userManager.UpdateSecurityStampAsync(user);
                if (!stamp.Succeeded)
                {
                    throw new IdentityOperationRejectedException(SafeIdentityErrorCode(stamp));
                }

                return new(AccountOperationStatus.Success, targetUserId);
            },
            async token => await IsOnlyStaffRoleAsync(targetUserId, requestedRole!, token),
            cancellationToken);
        return LogRoleTransitionAsync(operation, targetUserId, actorUserId, oldRoleForLog, requestedRole!);
    }

    public Task<AccountOperationResult> ResendInvitationAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var execution = new AccountExecutionState();
        return ResendInvitationCoreAsync(targetUserId, actorUserId, execution, cancellationToken);
    }

    private async Task<AccountOperationResult> ResendInvitationCoreAsync(
        int targetUserId,
        int actorUserId,
        AccountExecutionState execution,
        CancellationToken cancellationToken)
    {
        AccountOperationResult result;
        try
        {
            result = await ExecuteTransactionalAsync(
                async token =>
                {
                    db.ChangeTracker.Clear();
                    execution.Reset();
                    if (!await AcquireAccountPolicyLockAsync(token))
                    {
                        return new(AccountOperationStatus.Conflict, targetUserId, "AccountPolicyBusy");
                    }

                    var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == targetUserId, token);
                    if (user is null)
                    {
                        return new(AccountOperationStatus.NotFound, targetUserId);
                    }

                    var roles = await userManager.GetRolesAsync(user);
                    if (!IsPendingInvitation(user, roles))
                    {
                        return new(AccountOperationStatus.SecurityBlocked, targetUserId, "InvitationNotPending");
                    }

                    user.UpdatedAt = DateTime.UtcNow;
                    var stamp = await userManager.UpdateSecurityStampAsync(user);
                    if (!stamp.Succeeded)
                    {
                        throw new IdentityOperationRejectedException(SafeIdentityErrorCode(stamp));
                    }

                    var tokenValue = await userManager.GenerateUserTokenAsync(
                        user, StaffInvitationPurpose.ProviderName, StaffInvitationPurpose.TokenPurpose);
                    execution.UserId = user.Id;
                    execution.ExpectedSecurityStamp = user.SecurityStamp;
                    execution.Invitation = new InvitationPayload(user.Id, user.Email!, user.FullName, tokenValue);
                    return new(AccountOperationStatus.Success, user.Id);
                },
                async token =>
                {
                    db.ChangeTracker.Clear();
                    if (execution.ExpectedSecurityStamp is null)
                    {
                        return false;
                    }

                    return await IsUserInStateAsync(targetUserId, null, null,
                        isActive: false, emailConfirmed: false, hasPassword: false,
                        execution.ExpectedSecurityStamp, token);
                }, cancellationToken);
        }
        catch (IdentityOperationRejectedException exception)
        {
            db.ChangeTracker.Clear();
            return new(AccountOperationStatus.Conflict, targetUserId, exception.ErrorCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(AccountOperationStatus.Conflict, targetUserId, "ConcurrencyConflict");
        }

        if (result.Status == AccountOperationStatus.Success && execution.Invitation is not null)
        {
            if (!await SendInvitationAsync(execution.Invitation, actorUserId, "Resent", cancellationToken))
            {
                result = new(AccountOperationStatus.DeliveryFailed, targetUserId, "InvitationDeliveryFailed");
            }
        }

        LogOutcome(actorUserId, targetUserId, "ResendInvitation", result.Status.ToString());
        return result;
    }

    public async Task<bool> IsInvitationValidAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(email) || string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return false;
        }

        var roles = await userManager.GetRolesAsync(user);
        return IsPendingInvitation(user, roles) &&
               await userManager.VerifyUserTokenAsync(
                   user, StaffInvitationPurpose.ProviderName, StaffInvitationPurpose.TokenPurpose, token);
    }

    public async Task<AccountOperationResult> ActivateStaffAsync(
        string email,
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(email) || string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return new(AccountOperationStatus.InvalidInvitation, ErrorCode: "InvitationInvalid");
        }

        var normalizedEmail = userManager.NormalizeEmail(email.Trim());
        var execution = new AccountExecutionState();
        AccountOperationResult result;
        try
        {
            result = await ExecuteTransactionalAsync(
                async tokenCancellation =>
                {
                    db.ChangeTracker.Clear();
                    execution.Reset();
                    if (!await AcquireAccountPolicyLockAsync(tokenCancellation))
                    {
                        return new(AccountOperationStatus.Conflict, ErrorCode: "AccountPolicyBusy");
                    }

                    var user = await db.Users.SingleOrDefaultAsync(
                        candidate => candidate.NormalizedEmail == normalizedEmail, tokenCancellation);
                    if (user is null)
                    {
                        return new(AccountOperationStatus.InvalidInvitation, ErrorCode: "InvitationInvalid");
                    }

                    var roles = await userManager.GetRolesAsync(user);
                    if (!IsPendingInvitation(user, roles) ||
                        !await userManager.VerifyUserTokenAsync(
                            user, StaffInvitationPurpose.ProviderName, StaffInvitationPurpose.TokenPurpose, token))
                    {
                        return new(AccountOperationStatus.InvalidInvitation, user.Id, "InvitationInvalidOrUsed");
                    }

                    var addedPassword = await userManager.AddPasswordAsync(user, password);
                    if (!addedPassword.Succeeded)
                    {
                        return new(AccountOperationStatus.ValidationError, user.Id,
                            SafeIdentityErrorCode(addedPassword));
                    }

                    user.EmailConfirmed = true;
                    user.IsActive = true;
                    user.UpdatedAt = DateTime.UtcNow;
                    var stamp = await userManager.UpdateSecurityStampAsync(user);
                    if (!stamp.Succeeded)
                    {
                        throw new IdentityOperationRejectedException(SafeIdentityErrorCode(stamp));
                    }

                    execution.UserId = user.Id;
                    execution.ExpectedSecurityStamp = user.SecurityStamp;
                    return new(AccountOperationStatus.Success, user.Id);
                },
                async tokenCancellation =>
                {
                    db.ChangeTracker.Clear();
                    if (execution.UserId is not int userId || execution.ExpectedSecurityStamp is null)
                    {
                        return false;
                    }

                    var staffRole = await GetSingleStaffRoleAsync(userId, tokenCancellation);
                    return staffRole is not null && await IsUserInStateAsync(userId, normalizedEmail, staffRole,
                        isActive: true, emailConfirmed: true, hasPassword: true,
                        execution.ExpectedSecurityStamp, tokenCancellation);
                }, cancellationToken);
        }
        catch (IdentityOperationRejectedException exception)
        {
            db.ChangeTracker.Clear();
            LogOutcome(null, execution.UserId, "ActivateStaff", "RolledBack", exception.ErrorCode);
            return new(AccountOperationStatus.Conflict, execution.UserId, "ConcurrencyConflict");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(AccountOperationStatus.Conflict, execution.UserId, "ConcurrencyConflict");
        }

        LogOutcome(null, result.UserId, "ActivateStaff", result.Status.ToString(), result.ErrorCode);
        return result;
    }

    private async Task<AccountOperationResult> ExecuteUserMutationAsync(
        int targetUserId,
        int actorUserId,
        string action,
        Func<AppUser, IList<string>, CancellationToken, Task<AccountOperationResult>> mutation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        AccountOperationResult result;
        try
        {
            result = await ExecuteTransactionalAsync(
                async token =>
                {
                    db.ChangeTracker.Clear();
                    if (!await AcquireAccountPolicyLockAsync(token))
                    {
                        return new(AccountOperationStatus.Conflict, targetUserId, "AccountPolicyBusy");
                    }

                    var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == targetUserId, token);
                    if (user is null)
                    {
                        return new(AccountOperationStatus.NotFound, targetUserId);
                    }

                    var roles = await userManager.GetRolesAsync(user);
                    var changed = await mutation(user, roles, token);
                    return changed;
                },
                async token =>
                {
                    db.ChangeTracker.Clear();
                    return await verifySucceeded(token);
                }, cancellationToken);
        }
        catch (IdentityOperationRejectedException exception)
        {
            db.ChangeTracker.Clear();
            LogOutcome(actorUserId, targetUserId, action, "RolledBack", exception.ErrorCode);
            return new(AccountOperationStatus.Conflict, targetUserId, "IdentityOperationFailed");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(AccountOperationStatus.Conflict, targetUserId, "ConcurrencyConflict");
        }

        LogOutcome(actorUserId, targetUserId, action, result.Status.ToString(), result.ErrorCode);
        return result;
    }

    private async Task<AccountOperationResult> PersistWithNewSecurityStampAsync(AppUser user, int userId)
    {
        var updated = await userManager.UpdateSecurityStampAsync(user);
        if (!updated.Succeeded)
        {
            throw new IdentityOperationRejectedException(SafeIdentityErrorCode(updated));
        }

        return new(AccountOperationStatus.Success, userId);
    }

    private async Task<bool> SendInvitationAsync(
        InvitationPayload invitation,
        int actorUserId,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            var baseUri = GetValidatedBaseUri(invitationOptions.Value.PublicBaseUrl);
            if (baseUri is null)
            {
                logger.LogWarning("Staff invitation delivery was not attempted because no trusted public origin is configured. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, Outcome={Outcome}, CorrelationId={CorrelationId}",
                    actorUserId, invitation.UserId, action, "ConfigurationMissing", CorrelationId());
                return false;
            }

            var uriBuilder = new UriBuilder(baseUri)
            {
                Path = "/account/activatestaff",
                Fragment = $"email={Uri.EscapeDataString(invitation.Email)}&token={Uri.EscapeDataString(invitation.Token)}"
            };
            var safeName = HtmlEncoder.Default.Encode(invitation.FullName);
            var safeLink = HtmlEncoder.Default.Encode(uriBuilder.Uri.AbsoluteUri);
            await emailSender.SendAsync(new EmailMessage(
                invitation.Email,
                "Lời mời kích hoạt tài khoản NEXORA",
                $"<p>Xin chào {safeName},</p><p>Hãy mở liên kết để thiết lập mật khẩu và kích hoạt tài khoản nhân viên:</p><p><a href=\"{safeLink}\">Kích hoạt tài khoản</a></p><p>Liên kết có thời hạn 24 giờ và chỉ sử dụng được một lần.</p>"), cancellationToken);
            logger.LogInformation("Staff invitation delivery completed. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, Outcome={Outcome}, CorrelationId={CorrelationId}",
                actorUserId, invitation.UserId, action, "AcceptedBySender", CorrelationId());
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Staff invitation delivery failed. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, Outcome={Outcome}, ErrorType={ErrorType}, CorrelationId={CorrelationId}",
                actorUserId, invitation.UserId, action, "DeliveryFailed", exception.GetType().Name, CorrelationId());
            return false;
        }
    }

    private async Task<AccountOperationResult> ExecuteTransactionalAsync(
        Func<CancellationToken, Task<AccountOperationResult>> operation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await RelationalExecutionStrategyExtensions.ExecuteInTransactionAsync(
            strategy,
            operation,
            verifySucceeded,
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    private async Task<bool> AcquireAccountPolicyLockAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout; SELECT @result;";
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = AccountPolicyLock;
        command.Parameters.Add(resource);
        var timeout = command.CreateParameter();
        timeout.ParameterName = "@timeout";
        timeout.Value = (int)LockTimeout.TotalMilliseconds;
        command.Parameters.Add(timeout);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) => await (
        from user in db.Users
        join userRole in db.UserRoles on user.Id equals userRole.UserId
        join role in db.Roles on userRole.RoleId equals role.Id
        where user.IsActive && role.NormalizedName == RoleNames.Admin.ToUpperInvariant()
        select user.Id).Distinct().CountAsync(cancellationToken);

    private async Task<bool> IsUserInStateAsync(
        int userId,
        string? normalizedEmail,
        string? roleName,
        bool isActive,
        bool emailConfirmed,
        bool hasPassword,
        string expectedSecurityStamp,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null ||
            normalizedEmail is not null && user.NormalizedEmail != normalizedEmail ||
            user.IsActive != isActive || user.EmailConfirmed != emailConfirmed ||
            (user.PasswordHash is not null) != hasPassword || user.SecurityStamp != expectedSecurityStamp)
        {
            return false;
        }

        var roles = await GetRoleNamesAsync(userId, cancellationToken);
        return roles.Count == 1 && (roleName is null || string.Equals(roles[0], roleName, StringComparison.Ordinal));
    }

    private async Task<bool> IsAccountActiveAsync(int userId, bool expected, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsActive == expected, cancellationToken);

    private async Task<bool> IsOnlyStaffRoleAsync(int userId, string role, CancellationToken cancellationToken)
    {
        var roles = await GetRoleNamesAsync(userId, cancellationToken);
        return roles.Count == 1 && string.Equals(roles[0], role, StringComparison.Ordinal);
    }

    private async Task<string?> GetSingleStaffRoleAsync(int userId, CancellationToken cancellationToken)
    {
        var roles = await GetRoleNamesAsync(userId, cancellationToken);
        return roles.Count == 1 && StaffAccountStatePolicy.IsStaffRole(roles[0]) ? roles[0] : null;
    }

    private async Task<IList<string>> GetRoleNamesAsync(int userId, CancellationToken cancellationToken) => await (
        from userRole in db.UserRoles.AsNoTracking()
        join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
        where userRole.UserId == userId
        orderby role.Name
        select role.Name ?? string.Empty).ToListAsync(cancellationToken);

    private static bool IsPendingInvitation(AppUser user, IList<string> roles) =>
        !user.IsActive && !user.EmailConfirmed && user.PasswordHash is null &&
        roles.Count == 1 && StaffAccountStatePolicy.IsStaffRole(roles[0]);

    private static string? NormalizeRoleFilter(string? role) =>
        string.Equals(role, RoleNames.Customer, StringComparison.OrdinalIgnoreCase) ? RoleNames.Customer :
        string.Equals(role, RoleNames.Sales, StringComparison.OrdinalIgnoreCase) ? RoleNames.Sales :
        string.Equals(role, RoleNames.Warehouse, StringComparison.OrdinalIgnoreCase) ? RoleNames.Warehouse :
        string.Equals(role, RoleNames.Admin, StringComparison.OrdinalIgnoreCase) ? RoleNames.Admin : null;

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
        {
            return false;
        }

        try
        {
            return string.Equals(new MailAddress(email).Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Uri? GetValidatedBaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return null;
        }

        return uri;
    }

    private static string SafeIdentityErrorCode(IdentityResult result) =>
        result.Errors.Select(error => error.Code).FirstOrDefault(code =>
            code is "DuplicateEmail" or "DuplicateUserName" or "PasswordTooShort" or "PasswordRequiresDigit" or
                "PasswordRequiresUpper" or "PasswordRequiresLower" or "PasswordRequiresNonAlphanumeric" or
                "PasswordRequiresUniqueChars" or "ConcurrencyFailure" or "RoleNotFound") ?? "IdentityOperationRejected";

    private static string? CorrelationId() => Activity.Current?.TraceId.ToString();

    private void LogOutcome(int? actorUserId, int? targetUserId, string action, string outcome, string? errorCode = null)
    {
        logger.LogInformation("Account administration operation. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, Outcome={Outcome}, ErrorCode={ErrorCode}, CorrelationId={CorrelationId}",
            actorUserId, targetUserId, action, outcome, errorCode, CorrelationId());
    }

    private async Task<AccountOperationResult> LogRoleTransitionAsync(
        Task<AccountOperationResult> operation,
        int targetUserId,
        int actorUserId,
        string? oldRole,
        string newRole)
    {
        var result = await operation;
        if (result.Status == AccountOperationStatus.Success)
        {
            logger.LogInformation("Account role transition. ActorUserId={ActorUserId}, TargetUserId={TargetUserId}, Action={Action}, OldRole={OldRole}, NewRole={NewRole}, Outcome={Outcome}, CorrelationId={CorrelationId}",
                actorUserId, targetUserId, "ChangeStaffRole", oldRole, newRole, "Success", CorrelationId());
        }

        return result;
    }

    private sealed record InvitationPayload(int UserId, string Email, string FullName, string Token);

    private sealed class AccountExecutionState
    {
        public int? UserId { get; set; }
        public string? ExpectedSecurityStamp { get; set; }
        public InvitationPayload? Invitation { get; set; }

        public void Reset()
        {
            UserId = null;
            ExpectedSecurityStamp = null;
            Invitation = null;
        }
    }

    private sealed class IdentityOperationRejectedException(string errorCode) : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
