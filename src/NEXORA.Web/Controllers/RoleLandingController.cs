using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Admin;
using NEXORA.Domain.Security;

namespace NEXORA.Web.Controllers;

[Route("workspace/[action]")]
public sealed class RoleLandingController(IAdminDashboardQueryService dashboard) : Controller
{
    [Authorize(Roles = RoleNames.Customer + "," + RoleNames.Admin)]
    public IActionResult Customer() => RedirectToAction("Profile", "Account");

    [Authorize(Roles = RoleNames.Sales)]
    public IActionResult Sales() => Show("Khu vực nhân viên bán hàng");

    [Authorize(Roles = RoleNames.Warehouse)]
    public IActionResult Warehouse() => Show("Khu vực nhân viên kho");

    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Tổng quan";
        ViewData["UseAdminLayout"] = true;
        ViewData["AdminDashboardMetrics"] = await dashboard.GetMetricsAsync(cancellationToken);
        return View("RoleLanding");
    }

    private IActionResult Show(string title, bool useAdminLayout = false)
    {
        ViewData["Title"] = title;
        ViewData["UseAdminLayout"] = useAdminLayout;
        return View("RoleLanding");
    }
}
