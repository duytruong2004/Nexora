using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Web.Areas.Warehouse.Controllers;

namespace NEXORA.Tests;

public sealed class WarehouseAuthorizationHttpIntegrationTests
{
    [Fact]
    public async Task WarehousePages_AreRoleProtected_AndExportRequiresAntiforgery()
    {
        var root = FindWebProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OrdersController).Assembly.GetName().Name,
            ContentRootPath = root,
            WebRootPath = Path.Combine(root, "wwwroot"),
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("WarehouseTest")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("WarehouseTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(OrdersController).Assembly);
        builder.Services.AddSingleton<TestWarehouseFulfillmentService>();
        builder.Services.AddSingleton<IWarehouseFulfillmentService>(services => services.GetRequiredService<TestWarehouseFulfillmentService>());
        builder.Services.AddSingleton<TestWarehouseInventoryService>();
        builder.Services.AddSingleton<IWarehouseInventoryManagementService>(services => services.GetRequiredService<TestWarehouseInventoryService>());

        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Orders}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };

            using (var anonymous = await client.GetAsync("/Warehouse/Orders?testRole=Anonymous"))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using (var anonymousInventory = await client.GetAsync("/Warehouse/Inventory?testRole=Anonymous"))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousInventory.StatusCode);

            foreach (var role in new[] { "Customer", "Sales", "Admin" })
            {
                using var deniedQueue = await client.GetAsync($"/Warehouse/Orders?testRole={role}");
                using var deniedExport = await client.PostAsync($"/Warehouse/Orders/Export/71?testRole={role}", new FormUrlEncodedContent([]));
                Assert.Equal(HttpStatusCode.Forbidden, deniedQueue.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, deniedExport.StatusCode);

                using var inventoryRead = await client.GetAsync($"/Warehouse/Inventory?testRole={role}");
                using var inventoryHistoryRead = await client.GetAsync($"/Warehouse/Inventory/History?testRole={role}");
                using var receivePage = await client.GetAsync($"/Warehouse/Inventory/Receive?testRole={role}");
                using var receivePost = await client.PostAsync($"/Warehouse/Inventory/Receive?testRole={role}", new FormUrlEncodedContent([]));
                Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, inventoryRead.StatusCode);
                Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, inventoryHistoryRead.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, receivePage.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, receivePost.StatusCode);
            }

            using var clientWithoutCookie = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            using (var missingToken = await clientWithoutCookie.PostAsync("/Warehouse/Orders/Export/71?testRole=Warehouse", new FormUrlEncodedContent([])))
                Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

            var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var warehouseClient = new HttpClient(handler) { BaseAddress = new Uri(address) };
            using var queue = await warehouseClient.GetAsync("/Warehouse/Orders?testRole=Warehouse");
            var queueHtml = await queue.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            Assert.Contains("Hàng đợi kho", queueHtml, StringComparison.Ordinal);
            Assert.Contains("#71", queueHtml, StringComparison.Ordinal);

            using (var inventoryPage = await warehouseClient.GetAsync("/Warehouse/Inventory?testRole=Warehouse"))
            {
                var inventoryHtml = await inventoryPage.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, inventoryPage.StatusCode);
                Assert.Contains("Quản lý tồn kho", inventoryHtml, StringComparison.Ordinal);
                Assert.Contains("Có thể bán", inventoryHtml, StringComparison.Ordinal);
                Assert.Contains("Nhập kho", inventoryHtml, StringComparison.Ordinal);
            }
            using (var receivePage = await warehouseClient.GetAsync("/Warehouse/Inventory/Receive?testRole=Warehouse"))
            {
                var receiveHtml = await receivePage.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, receivePage.StatusCode);
                Assert.Contains("name=\"__RequestVerificationToken\"", receiveHtml, StringComparison.Ordinal);
            }
            using (var missingInventoryToken = await warehouseClient.PostAsync(
                       "/Warehouse/Inventory/Receive?testRole=Warehouse", new FormUrlEncodedContent([])))
                Assert.Equal(HttpStatusCode.BadRequest, missingInventoryToken.StatusCode);
            using (var receiveForm = await warehouseClient.GetAsync("/Warehouse/Inventory/Receive?testRole=Warehouse"))
            {
                var html = await receiveForm.Content.ReadAsStringAsync();
                var receiveToken = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
                Assert.True(receiveToken.Success);
                using var posted = await warehouseClient.PostAsync("/Warehouse/Inventory/Receive?testRole=Warehouse",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = WebUtility.HtmlDecode(receiveToken.Groups[1].Value),
                        ["RequestId"] = Guid.NewGuid().ToString(), ["ProductId"] = "17", ["Quantity"] = "1", ["Reference"] = "HTTP QA"
                    }));
                Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
                Assert.Equal(44, app.Services.GetRequiredService<TestWarehouseInventoryService>().LastReceiptActorId);
            }
            using (var adjustForm = await warehouseClient.GetAsync("/Warehouse/Inventory/Adjust?testRole=Warehouse"))
            {
                var html = await adjustForm.Content.ReadAsStringAsync();
                var adjustToken = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
                Assert.True(adjustToken.Success);
                using var posted = await warehouseClient.PostAsync("/Warehouse/Inventory/Adjust?testRole=Warehouse",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = WebUtility.HtmlDecode(adjustToken.Groups[1].Value),
                        ["RequestId"] = Guid.NewGuid().ToString(), ["ProductId"] = "17", ["Quantity"] = "1",
                        ["Direction"] = "INCREASE", ["Reason"] = "HTTP QA"
                    }));
                Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
                Assert.Equal(44, app.Services.GetRequiredService<TestWarehouseInventoryService>().LastAdjustmentActorId);
            }

            using var detail = await warehouseClient.GetAsync("/Warehouse/Orders/Details/71?testRole=Warehouse");
            var detailHtml = await detail.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Contains("Xác nhận xuất kho", detailHtml, StringComparison.Ordinal);
            Assert.Contains("NEXORA QA Warehouse Order", detailHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", detailHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("qa.customer@example.invalid", detailHtml, StringComparison.Ordinal);
            using var unavailable = await warehouseClient.GetAsync("/Warehouse/Orders/Details/999?testRole=Warehouse");
            Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);

            var token = Regex.Match(detailHtml, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
            Assert.True(token.Success, "Warehouse export form should include an antiforgery token.");
            using var export = await warehouseClient.PostAsync("/Warehouse/Orders/Export/71?testRole=Warehouse",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }));
            Assert.Equal(HttpStatusCode.Redirect, export.StatusCode);
            Assert.Equal(71, app.Services.GetRequiredService<TestWarehouseFulfillmentService>().LastExportOrderId);
            Assert.Equal(44, app.Services.GetRequiredService<TestWarehouseFulfillmentService>().LastActorUserId);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindWebProjectRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("NEXORA_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var project = Path.Combine(configuredRoot, "src", "NEXORA.Web");
            if (File.Exists(Path.Combine(project, "NEXORA.Web.csproj"))) return project;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var project = Path.Combine(current.FullName, "src", "NEXORA.Web");
                if (File.Exists(Path.Combine(project, "NEXORA.Web.csproj"))) return project;
                current = current.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate src/NEXORA.Web.");
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Query["testRole"].ToString();
            if (role == "Anonymous") return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "44"),
                new Claim(ClaimTypes.Name, "Warehouse HTTP test"),
                new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(role) ? "Warehouse" : role)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestWarehouseFulfillmentService : IWarehouseFulfillmentService
    {
        public int? LastExportOrderId { get; private set; }
        public int? LastActorUserId { get; private set; }

        public Task<WarehouseQueuePage> GetQueueAsync(WarehouseQueueFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WarehouseQueuePage(
                [new WarehouseQueueItem(71, DateTime.UtcNow, 1, 2, null, null)], 1, 1, 25));

        public Task<WarehouseOrderDetail?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WarehouseOrderDetail?>(orderId == 71
                ? new WarehouseOrderDetail(71, DateTime.UtcNow, OrderStatuses.Preparing,
                    "QA recipient", "QA address", null, 2000m, null, null, null,
                    [new WarehouseOrderLine(8, "QA-SKU", "NEXORA QA Warehouse Order", 2, 10, 2, 2, 8, true)], true, null)
                : null);

        public Task<WarehouseExportResult> ExportAsync(int orderId, int warehouseUserId, CancellationToken cancellationToken = default)
        {
            LastExportOrderId = orderId;
            LastActorUserId = warehouseUserId;
            return Task.FromResult(new WarehouseExportResult(WarehouseExportFailure.None));
        }
    }

    private sealed class TestWarehouseInventoryService : IWarehouseInventoryManagementService
    {
        public int? LastReceiptActorId { get; private set; }
        public int? LastAdjustmentActorId { get; private set; }
        public Task<WarehouseInventoryPage> SearchAsync(WarehouseInventoryFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WarehouseInventoryPage(
                [new WarehouseInventoryRow(17, "NEXORA-QA-INV", "NEXORA QA Inventory", "QA", 8, 2, 6)], 1, 1, 25));

        public Task<IReadOnlyList<WarehouseInventoryProductOption>> GetProductOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseInventoryProductOption>>(
                [new WarehouseInventoryProductOption(17, "NEXORA-QA-INV", "NEXORA QA Inventory")]);

        public Task<WarehouseInventoryHistoryPage> GetHistoryAsync(WarehouseInventoryHistoryFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WarehouseInventoryHistoryPage([], 0, 1, 25));

        public Task<WarehouseInventoryOperationResult> ReceiveAsync(WarehouseStockReceiptInput input, int warehouseUserId, CancellationToken cancellationToken = default)
        {
            LastReceiptActorId = warehouseUserId;
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.None, 9, 2, 7));
        }

        public Task<WarehouseInventoryOperationResult> AdjustAsync(WarehouseStockAdjustmentInput input, int warehouseUserId, CancellationToken cancellationToken = default)
        {
            LastAdjustmentActorId = warehouseUserId;
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.None, 9, 2, 7));
        }
    }
}
