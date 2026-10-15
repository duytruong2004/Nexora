using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

public class HomeController(ICustomerCatalogQueryService catalog) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(new StorefrontHomeViewModel
        {
            Data = await catalog.GetHomeAsync(cancellationToken)
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
