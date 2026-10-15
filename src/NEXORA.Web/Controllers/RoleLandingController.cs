using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;

namespace NEXORA.Web.Controllers;

[Route("workspace/[action]")]
public sealed class RoleLandingController : Controller
{
    [Authorize(Roles = RoleNames.Customer)]
    public IActionResult Customer() => Show("Khu vực khách hàng");

    [Authorize(Roles = RoleNames.Sales)]
    public IActionResult Sales() => Show("Khu vực nhân viên bán hàng");

    [Authorize(Roles = RoleNames.Warehouse)]
    public IActionResult Warehouse() => Show("Khu vực nhân viên kho");

    [Authorize(Roles = RoleNames.Admin)]
    public IActionResult Admin() => Show("Khu vực quản trị viên");

    private IActionResult Show(string title)
    {
        ViewData["Title"] = title;
        return View("RoleLanding");
    }
}
