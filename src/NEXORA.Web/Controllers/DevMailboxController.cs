using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;
using NEXORA.Web.Email;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
[Route("dev/mailbox")]
public sealed class DevMailboxController(
    IWebHostEnvironment environment,
    InMemoryEmailMailbox mailbox) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (!environment.IsDevelopment() ||
            HttpContext.Connection.RemoteIpAddress is not { } remoteAddress ||
            !System.Net.IPAddress.IsLoopback(remoteAddress))
        {
            return NotFound();
        }

        return View(mailbox.GetRecent());
    }
}
