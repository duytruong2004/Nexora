using NEXORA.Domain.Security;

namespace NEXORA.Application.Identity;

public enum AccountLifecycleState
{
    Active,
    PendingInvitation,
    Suspended,
    Inconsistent
}

public static class StaffInvitationPurpose
{
    public const string ProviderName = "StaffInvitation";
    public const string TokenPurpose = "NEXORA.StaffInvitation.v1";
}

/// <summary>
/// Authorization policy for the approved single-role account model. A cookie may only
/// be issued to an active account with exactly one canonical NEXORA role.
/// </summary>
public static class AccountAuthenticationPolicy
{
    public static bool CanIssueCookie(bool isActive, IEnumerable<string?> persistedRoles) =>
        isActive && TryGetSingleKnownRole(persistedRoles, out _);

    public static bool HasCurrentSingleRole(
        bool isActive,
        IEnumerable<string?> persistedRoles,
        IEnumerable<string?> principalRoles)
    {
        if (!isActive ||
            !TryGetSingleKnownRole(persistedRoles, out var persistedRole) ||
            !TryGetSingleKnownRole(principalRoles, out var principalRole))
        {
            return false;
        }

        return string.Equals(persistedRole, principalRole, StringComparison.Ordinal);
    }

    private static bool TryGetSingleKnownRole(IEnumerable<string?> roles, out string? role)
    {
        using var enumerator = roles.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            role = null;
            return false;
        }

        role = enumerator.Current;
        if (enumerator.MoveNext() || role is null || !RoleNames.All.Contains(role, StringComparer.Ordinal))
        {
            role = null;
            return false;
        }

        return true;
    }
}

public sealed record AccountAuthenticationState(
    bool IsActive,
    string? SecurityStamp,
    IReadOnlyList<string?> Roles);

public interface IAccountAuthenticationStateReader
{
    Task<AccountAuthenticationState?> ReadAsync(
        int userId,
        CancellationToken cancellationToken = default);
}

public static class StaffAccountStatePolicy
{
    public static AccountLifecycleState Classify(
        bool isActive,
        bool emailConfirmed,
        bool hasPassword,
        IReadOnlyCollection<string> roles)
    {
        if (roles.Count != 1 || !IsKnownRole(roles.First()))
        {
            return AccountLifecycleState.Inconsistent;
        }

        var isStaff = roles.Contains(RoleNames.Sales, StringComparer.Ordinal) ||
                      roles.Contains(RoleNames.Warehouse, StringComparer.Ordinal);

        if (isActive)
        {
            return AccountLifecycleState.Active;
        }

        if (isStaff && !emailConfirmed && !hasPassword)
        {
            return AccountLifecycleState.PendingInvitation;
        }

        if (hasPassword)
        {
            return AccountLifecycleState.Suspended;
        }

        return AccountLifecycleState.Inconsistent;
    }

    public static bool IsStaffRole(string? role) =>
        string.Equals(role, RoleNames.Sales, StringComparison.Ordinal) ||
        string.Equals(role, RoleNames.Warehouse, StringComparison.Ordinal);

    public static bool IsKnownRole(string? role) =>
        role is not null && RoleNames.All.Contains(role, StringComparer.Ordinal);
}

public sealed record AccountSearchFilter(
    string? Search = null,
    string? Role = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25);

public sealed record ManagedAccountRow(
    int Id,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool EmailConfirmed,
    bool HasPassword,
    DateTime CreatedAt,
    AccountLifecycleState LifecycleState)
{
    public bool HasSingleRole => Roles.Count == 1;
}

public sealed record ManagedAccountPage(
    IReadOnlyList<ManagedAccountRow> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record ManagedAccountDetails(
    int Id,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool EmailConfirmed,
    bool HasPassword,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AccountLifecycleState LifecycleState)
{
    public bool HasSingleRole => Roles.Count == 1;
}

public sealed record CreateStaffInput(string FullName, string Email, string Role);

public sealed record StaffInvitationValidationInput(string Email, string Token);

public enum AccountOperationStatus
{
    Success,
    ValidationError,
    Forbidden,
    NotFound,
    Conflict,
    AlreadyInState,
    DeliveryFailed,
    SecurityBlocked,
    InvalidInvitation
}

public sealed record AccountOperationResult(
    AccountOperationStatus Status,
    int? UserId = null,
    string? ErrorCode = null)
{
    public bool Succeeded => Status == AccountOperationStatus.Success;
}

public interface IAccountAdministrationService
{
    Task<ManagedAccountPage> SearchUsersAsync(
        AccountSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<ManagedAccountDetails?> GetUserDetailsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> CreateStaffAsync(
        CreateStaffInput input,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> LockUserAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> UnlockUserAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> ChangeStaffRoleAsync(
        int targetUserId,
        string newRole,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> ResendInvitationAsync(
        int targetUserId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> IsInvitationValidAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default);

    Task<AccountOperationResult> ActivateStaffAsync(
        string email,
        string token,
        string password,
        CancellationToken cancellationToken = default);
}
