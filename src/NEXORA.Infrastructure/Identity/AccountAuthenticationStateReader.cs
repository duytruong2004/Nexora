using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Identity;

public sealed class AccountAuthenticationStateReader(AppDbContext db)
    : IAccountAuthenticationStateReader
{
    public async Task<AccountAuthenticationState?> ReadAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsActive, user.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var roles = await (
            from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name).ToListAsync(cancellationToken);

        return new AccountAuthenticationState(account.IsActive, account.SecurityStamp, roles);
    }
}
