using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Web.Email;

namespace NEXORA.Web.Controllers;

[AllowAnonymous]
[Route("dev/mailbox")]
public sealed class DevMailboxController(
    IWebHostEnvironment environment,
    InMemoryEmailMailbox mailbox) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        return View(mailbox.GetRecent());
    }
}
