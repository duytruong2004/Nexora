using Microsoft.AspNetCore.Identity;

namespace NEXORA.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AppRole : IdentityRole<int>
{
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AppUserRole : IdentityUserRole<int>
{
    public DateTime AssignedAt { get; set; }
}
