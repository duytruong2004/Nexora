using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NEXORA.Application.Identity;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Identity;

public sealed class StaffInvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
}

public sealed class StaffInvitationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<StaffInvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtectionProvider, options, logger)
{
}
