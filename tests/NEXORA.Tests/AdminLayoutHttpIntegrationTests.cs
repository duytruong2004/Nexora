using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Admin;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
using NEXORA.Application.Commerce;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Entities;
using NEXORA.Web.Controllers;

namespace NEXORA.Tests;

public sealed class AdminLayoutHttpIntegrationTests
{
    [Fact]
    public async Task AdminWorkspace_RendersAdminLayoutAndLocalAssets_ForTestOnlyAdminPrincipal()
    {
        var webProjectRoot = FindWebProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RoleLandingController).Assembly.GetName().Name,
            ContentRootPath = webProjectRoot,
            WebRootPath = Path.Combine(webProjectRoot, "wwwroot"),
            EnvironmentName = "Development"
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddAuthentication("TestAdmin")
            .AddScheme<AuthenticationSchemeOptions, TestAdminAuthenticationHandler>("TestAdmin", _ => { });
        builder.Services.AddAuthorization();
        builder.Services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(RoleLandingController).Assembly);
        builder.Services.AddSingleton<ICustomerCatalogQueryService, TestCustomerCatalogQueryService>();
        builder.Services.AddSingleton<IReviewService, TestReviewService>();
        builder.Services.AddSingleton<IAdminDashboardQueryService, TestAdminDashboardQueryService>();
        builder.Services.AddSingleton<IConsultationManagementService, TestConsultationManagementService>();
        builder.Services.AddSingleton<IAdminPromotionManagementService, TestAdminPromotionManagementService>();
        builder.Services.AddSingleton<ICartService, TestCartService>();
        builder.Services.AddSingleton<ISalesOrderManagementService, TestSalesOrderManagementService>();
        builder.Services.AddSingleton<IOrderCancellationReviewService, TestCancellationReviewService>();
        builder.Services.AddIdentityCore<NEXORA.Infrastructure.Identity.AppUser>()
            .AddUserStore<TestAppUserStore>();
        builder.Services.AddSingleton<TestCustomerOrderService>();
        builder.Services.AddSingleton<ICustomerOrderService>(services => services.GetRequiredService<TestCustomerOrderService>());

        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address)
            };

            using var response = await client.GetAsync("/workspace/Admin");
            var html = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected HTTP 200, got {(int)response.StatusCode}: {html}");
            Assert.Contains("admin-shell", html, StringComparison.Ordinal);
            Assert.Contains("css/admin.css", html, StringComparison.Ordinal);
            Assert.Contains("Quản lý cửa hàng từ một nơi", html, StringComparison.Ordinal);
            Assert.Contains("Quy tắc xếp hạng", html, StringComparison.Ordinal);
            Assert.Contains("Thông số kỹ thuật", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/Admin/Orders", html, StringComparison.Ordinal);
            Assert.Contains(">Đơn hàng</span>", html, StringComparison.Ordinal);
            Assert.Contains("Tổng sản phẩm</span><strong>123</strong>", html, StringComparison.Ordinal);
            Assert.Contains("Đang kinh doanh</span><strong>117</strong>", html, StringComparison.Ordinal);
            Assert.Contains("Danh mục</span><strong>45</strong>", html, StringComparison.Ordinal);
            Assert.Contains("Nhu cầu tư vấn</span><strong>8</strong>", html, StringComparison.Ordinal);
            Assert.Contains("Quy tắc tư vấn</span><strong>86</strong>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Specification</span>", html, StringComparison.Ordinal);
            Assert.Contains("d-none d-lg-flex", html, StringComparison.Ordinal);
            Assert.Contains("admin-mobile-sidebar d-lg-none", html, StringComparison.Ordinal);
            Assert.Contains("admin-role-badge\">Admin</span>", html, StringComparison.Ordinal);
            Assert.Contains("Đăng xuất", html, StringComparison.Ordinal);

            foreach (var assetPath in new[]
                     {
                         "/css/site.css",
                         "/css/admin.css",
                         "/lib/bootstrap/dist/css/bootstrap.min.css",
                         "/lib/bootstrap/dist/js/bootstrap.bundle.min.js",
                         "/lib/jquery/dist/jquery.min.js",
                         "/lib/jquery-validation/dist/jquery.validate.min.js",
                         "/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"
                     })
            {
                using var assetResponse = await client.GetAsync(assetPath);
                Assert.True(assetResponse.StatusCode == HttpStatusCode.OK,
                    $"Expected static asset {assetPath} to return HTTP 200, got {(int)assetResponse.StatusCode}.");
                if (assetPath == "/css/admin.css")
                {
                    var adminCss = await assetResponse.Content.ReadAsStringAsync();
                    Assert.Contains(".nexora-admin-body .admin-welcome-mark { display: none; }", adminCss, StringComparison.Ordinal);
                    Assert.Contains(".admin-menu-link.active", adminCss, StringComparison.Ordinal);
                    Assert.Contains(".admin-menu-link:focus-visible", adminCss, StringComparison.Ordinal);
                }
            }

            using var adminHomeResponse = await client.GetAsync("/?testRole=Admin");
            var adminHomeHtml = await adminHomeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, adminHomeResponse.StatusCode);
            AssertCommonNavigation(adminHomeHtml);
            Assert.Contains("href=\"/workspace/Admin\">Quản trị</a>", adminHomeHtml, StringComparison.Ordinal);

            using var adminCatalogResponse = await client.GetAsync("/Catalog?testRole=Admin");
            Assert.Equal(HttpStatusCode.OK, adminCatalogResponse.StatusCode);
            Assert.Contains("Không tìm thấy sản phẩm", await adminCatalogResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var catalogVisualResponse = await client.GetAsync("/Catalog?search=ui-product-image-test&testRole=Admin");
            var catalogVisualHtml = await catalogVisualResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, catalogVisualResponse.StatusCode);
            Assert.Contains("nexora-product-visual--card", catalogVisualHtml, StringComparison.Ordinal);
            Assert.Contains("hình ảnh chưa được cập nhật", catalogVisualHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("16 GB", catalogVisualHtml, StringComparison.Ordinal);

            using var customerHomeResponse = await client.GetAsync("/?testRole=Customer");
            var customerHomeHtml = await customerHomeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, customerHomeResponse.StatusCode);
            AssertCommonNavigation(customerHomeHtml);
            Assert.DoesNotContain("href=\"/workspace/Admin\">Quản trị</a>", customerHomeHtml, StringComparison.Ordinal);

            using var salesHomeResponse = await client.GetAsync("/?testRole=Sales");
            var salesHomeHtml = await salesHomeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, salesHomeResponse.StatusCode);
            Assert.Contains("href=\"/Sales/Orders\">Quản lý đơn</a>", salesHomeHtml, StringComparison.Ordinal);

            using var customerCatalogResponse = await client.GetAsync("/Catalog?testRole=Customer&search=laptop");
            Assert.Equal(HttpStatusCode.OK, customerCatalogResponse.StatusCode);
            Assert.Contains("Kết quả cho “laptop”", await customerCatalogResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var stockCatalogResponse = await client.GetAsync("/Catalog?testRole=Customer&search=ui-stock-test");
            var stockCatalogHtml = await stockCatalogResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, stockCatalogResponse.StatusCode);
            Assert.Contains("Còn hàng", stockCatalogHtml, StringComparison.Ordinal);
            Assert.Contains("Hết hàng", stockCatalogHtml, StringComparison.Ordinal);
            Assert.Contains("Chưa có hàng", stockCatalogHtml, StringComparison.Ordinal);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(stockCatalogHtml, "data-catalog-cart-add-button").Count);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(stockCatalogHtml, "data-catalog-cart-add-button[^>]*\\sdisabled(?:\\s|=|>)").Count);
            Assert.Contains("aria-disabled=\"true\"", stockCatalogHtml, StringComparison.Ordinal);
            Assert.Contains("aria-disabled=\"false\"", stockCatalogHtml, StringComparison.Ordinal);

            using var outOfStockDetailsResponse = await client.GetAsync("/Catalog/Details/42?testRole=Customer");
            var outOfStockDetailsHtml = await outOfStockDetailsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, outOfStockDetailsResponse.StatusCode);
            Assert.Contains("Hết hàng", outOfStockDetailsHtml, StringComparison.Ordinal);
            Assert.Contains("Sản phẩm hiện đang hết hàng. Vui lòng quay lại sau.", outOfStockDetailsHtml, StringComparison.Ordinal);
            Assert.Matches("<input[^>]*id=\"cartQuantity\"[^>]*\\sdisabled(?:\\s|=|>)", outOfStockDetailsHtml);
            Assert.Matches("<button[^>]*\\sdisabled(?:\\s|=|>)", outOfStockDetailsHtml);

            using var inStockDetailsResponse = await client.GetAsync("/Catalog/Details/43?testRole=Customer");
            var inStockDetailsHtml = await inStockDetailsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, inStockDetailsResponse.StatusCode);
            Assert.Contains("Có thể bán: 3", inStockDetailsHtml, StringComparison.Ordinal);
            Assert.Contains("max=\"3\"", inStockDetailsHtml, StringComparison.Ordinal);
            Assert.DoesNotMatch("<input[^>]*id=\"cartQuantity\"[^>]*\\sdisabled(?:\\s|=|>)", inStockDetailsHtml);
            Assert.DoesNotMatch("<button[^>]*\\sdisabled(?:\\s|=|>)", inStockDetailsHtml);

            using var missingInventoryDetailsResponse = await client.GetAsync("/Catalog/Details/44?testRole=Customer");
            var missingInventoryDetailsHtml = await missingInventoryDetailsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, missingInventoryDetailsResponse.StatusCode);
            Assert.Contains("Chưa có hàng", missingInventoryDetailsHtml, StringComparison.Ordinal);
            Assert.Matches("<input[^>]*id=\"cartQuantity\"[^>]*\\sdisabled(?:\\s|=|>)", missingInventoryDetailsHtml);
            Assert.Matches("<button[^>]*\\sdisabled(?:\\s|=|>)", missingInventoryDetailsHtml);

            using var adminCompareResponse = await client.GetAsync("/Catalog/Compare?ids=1&ids=2&testRole=Admin");
            Assert.Equal(HttpStatusCode.OK, adminCompareResponse.StatusCode);
            Assert.Contains("So sánh sản phẩm", await adminCompareResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            using var anonymousHomeResponse = await client.GetAsync("/?testRole=Anonymous");
            var anonymousHomeHtml = await anonymousHomeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, anonymousHomeResponse.StatusCode);
            Assert.Contains("Sản phẩm", anonymousHomeHtml, StringComparison.Ordinal);
            Assert.Contains("Tư vấn", anonymousHomeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Tài khoản", anonymousHomeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("href=\"/workspace/Admin\">Quản trị</a>", anonymousHomeHtml, StringComparison.Ordinal);

            using var customerWorkspaceResponse = await client.GetAsync("/workspace/Customer?testRole=Customer");
            Assert.Equal(HttpStatusCode.Redirect, customerWorkspaceResponse.StatusCode);
            Assert.EndsWith("/account/profile", customerWorkspaceResponse.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

            using var adminWorkspaceResponse = await client.GetAsync("/workspace/Customer?testRole=Admin");
            Assert.Equal(HttpStatusCode.Redirect, adminWorkspaceResponse.StatusCode);
            Assert.EndsWith("/account/profile", adminWorkspaceResponse.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

            using var anonymousProfileResponse = await client.GetAsync("/account/profile?testRole=Anonymous");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousProfileResponse.StatusCode);

            using var anonymousCustomerWorkspaceResponse = await client.GetAsync("/workspace/Customer?testRole=Anonymous");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousCustomerWorkspaceResponse.StatusCode);

            using var needsResponse = await client.GetAsync("/Admin/Consultation/UsageNeeds");
            var needsHtml = await needsResponse.Content.ReadAsStringAsync();
            Assert.True(needsResponse.StatusCode == HttpStatusCode.OK, $"Expected UsageNeeds HTTP 200, got {(int)needsResponse.StatusCode}: {needsHtml}");
            Assert.Contains("Tổng nhu cầu", needsHtml, StringComparison.Ordinal);
            Assert.Contains("Quy tắc được gắn", needsHtml, StringComparison.Ordinal);

            using var rulesResponse = await client.GetAsync("/Admin/Consultation/Rules?usageNeedId=1&categoryId=1&isActive=true");
            var rulesHtml = await rulesResponse.Content.ReadAsStringAsync();
            Assert.True(rulesResponse.StatusCode == HttpStatusCode.OK, $"Expected Rules HTTP 200, got {(int)rulesResponse.StatusCode}: {rulesHtml}");
            Assert.Contains("1 quy tắc phù hợp", rulesHtml, StringComparison.Ordinal);
            Assert.Contains("RAM", rulesHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Dung lượng pin", rulesHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"usageNeedId\"", rulesHtml, StringComparison.Ordinal);

            using var promotionsResponse = await client.GetAsync("/Admin/Promotions");
            var promotionsHtml = await promotionsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, promotionsResponse.StatusCode);
            Assert.Contains("Khuyến mại", promotionsHtml, StringComparison.Ordinal);
            Assert.Contains("không cộng dồn", promotionsHtml, StringComparison.OrdinalIgnoreCase);

            using var customerPromotionsResponse = await client.GetAsync("/Admin/Promotions?testRole=Customer");
            Assert.Equal(HttpStatusCode.Forbidden, customerPromotionsResponse.StatusCode);

            using var anonymousPromotionsResponse = await client.GetAsync("/Admin/Promotions?testRole=Anonymous");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPromotionsResponse.StatusCode);

            using var adminOrdersResponse = await client.GetAsync("/Admin/Orders?testRole=Admin");
            var adminOrdersHtml = await adminOrdersResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, adminOrdersResponse.StatusCode);
            Assert.Contains("Đơn hàng", adminOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("Giám sát bán hàng", adminOrdersHtml, StringComparison.Ordinal);
            using var adminOrderDetailsResponse = await client.GetAsync("/Admin/Orders/Details/701?testRole=Admin");
            Assert.Equal(HttpStatusCode.OK, adminOrderDetailsResponse.StatusCode);
            var adminOrderDetailsHtml = await adminOrderDetailsResponse.Content.ReadAsStringAsync();
            Assert.Contains("Xác nhận đơn", adminOrderDetailsHtml, StringComparison.Ordinal);
            Assert.Contains("Yêu cầu hủy", adminOrderDetailsHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", adminOrderDetailsHtml, StringComparison.Ordinal);

            using var customerOrderDetailsResponse = await client.GetAsync("/Orders/Details/701?testRole=Customer");
            var customerOrderDetailsHtml = await customerOrderDetailsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, customerOrderDetailsResponse.StatusCode);
            Assert.Contains("Gửi yêu cầu hủy", customerOrderDetailsHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"RequestId\"", customerOrderDetailsHtml, StringComparison.Ordinal);

            using var customerOrdersResponse = await client.GetAsync("/Orders?testRole=Customer");
            var customerOrdersHtml = await customerOrdersResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, customerOrdersResponse.StatusCode);
            Assert.Contains("Đơn hàng của tôi", customerOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("nexora-orders-card", customerOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("Đặt hàng", customerOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("Tìm mã đơn hàng", customerOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"/Orders/Details/701\"", customerOrdersHtml, StringComparison.Ordinal);
            Assert.Equal(12, app.Services.GetRequiredService<TestCustomerOrderService>().LastListUserId);

            using var exportedOrdersResponse = await client.GetAsync("/Orders?status=EXPORTED&testRole=Customer");
            var exportedOrdersHtml = await exportedOrdersResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, exportedOrdersResponse.StatusCode);
            Assert.Contains("#702", exportedOrdersHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("#701", exportedOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("Đã xuất kho", exportedOrdersHtml, StringComparison.Ordinal);

            using var searchedOrdersResponse = await client.GetAsync("/Orders?search=%23703&testRole=Customer");
            var searchedOrdersHtml = await searchedOrdersResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, searchedOrdersResponse.StatusCode);
            Assert.Contains("#703", searchedOrdersHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("#702", searchedOrdersHtml, StringComparison.Ordinal);

            var antiforgeryToken = System.Text.RegularExpressions.Regex.Match(
                customerOrderDetailsHtml,
                "name=\\\"__RequestVerificationToken\\\" type=\\\"hidden\\\" value=\\\"([^\\\"]+)\\\"");
            var cancellationRequestId = System.Text.RegularExpressions.Regex.Match(
                customerOrderDetailsHtml,
                "name=\\\"RequestId\\\" value=\\\"([^\\\"]+)\\\"");
            Assert.True(antiforgeryToken.Success, "The customer cancellation form must render an antiforgery token.");
            Assert.True(cancellationRequestId.Success, "The customer form must render a stable request id.");
            using var cancellationPost = await client.PostAsync("/Orders/RequestCancellation/701?testRole=Customer",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = antiforgeryToken.Groups[1].Value,
                    ["RequestId"] = cancellationRequestId.Groups[1].Value,
                    ["Reason"] = "HTTP QA cancellation request"
                }));
            Assert.Equal(HttpStatusCode.Redirect, cancellationPost.StatusCode);
            Assert.Equal(12, app.Services.GetRequiredService<TestCustomerOrderService>().LastRequestUserId);
            Assert.Equal("HTTP QA cancellation request", app.Services.GetRequiredService<TestCustomerOrderService>().LastRequestReason);
            using var customerOtherOrderResponse = await client.GetAsync("/Orders/Details/702?testRole=Customer");
            Assert.Equal(HttpStatusCode.NotFound, customerOtherOrderResponse.StatusCode);

            using var customerAdminOrdersResponse = await client.GetAsync("/Admin/Orders?testRole=Customer");
            Assert.Equal(HttpStatusCode.Forbidden, customerAdminOrdersResponse.StatusCode);
            using var customerAdminCancellationReviewResponse = await client.PostAsync(
                "/Admin/Orders/ReviewCancellation/701?testRole=Customer&decision=approve",
                new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.Forbidden, customerAdminCancellationReviewResponse.StatusCode);

            using var salesOrdersResponse = await client.GetAsync("/Sales/Orders?testRole=Sales");
            var salesOrdersHtml = await salesOrdersResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, salesOrdersResponse.StatusCode);
            Assert.Contains("Không gian Sales", salesOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("NEXORA QA Sales Order", salesOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("nexora-navbar", salesOrdersHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"/Sales/Orders/Details/701\"", salesOrdersHtml, StringComparison.Ordinal);

            using var salesWorkspaceResponse = await client.GetAsync("/workspace/Sales?testRole=Sales");
            Assert.Equal(HttpStatusCode.Redirect, salesWorkspaceResponse.StatusCode);
            Assert.EndsWith("/Sales/Orders", salesWorkspaceResponse.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

            using var salesDetailResponse = await client.GetAsync("/Sales/Orders/Details/701?testRole=Sales");
            var salesDetailHtml = await salesDetailResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, salesDetailResponse.StatusCode);
            Assert.Contains("Xác nhận đơn", salesDetailHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", salesDetailHtml, StringComparison.Ordinal);

            using var customerSalesOrdersResponse = await client.GetAsync("/Sales/Orders?testRole=Customer");
            Assert.Equal(HttpStatusCode.Forbidden, customerSalesOrdersResponse.StatusCode);
            using var warehouseSalesCancellationReviewResponse = await client.PostAsync(
                "/Sales/Orders/ReviewCancellation/701?testRole=Warehouse&decision=approve",
                new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.Forbidden, warehouseSalesCancellationReviewResponse.StatusCode);

            using var customerSalesMutationResponse = await client.PostAsync("/Sales/Orders/Confirm/701?testRole=Customer", content: null);
            Assert.Equal(HttpStatusCode.Forbidden, customerSalesMutationResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindWebProjectRoot()
    {
        var configuredRepositoryRoot = Environment.GetEnvironmentVariable("NEXORA_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRepositoryRoot))
        {
            var configuredProjectRoot = Path.Combine(configuredRepositoryRoot, "src", "NEXORA.Web");
            if (File.Exists(Path.Combine(configuredProjectRoot, "NEXORA.Web.csproj")))
            {
                return configuredProjectRoot;
            }
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var projectRoot = Path.Combine(current.FullName, "src", "NEXORA.Web");
                if (File.Exists(Path.Combine(projectRoot, "NEXORA.Web.csproj")))
                {
                    return projectRoot;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/NEXORA.Web from the test working directory.");
    }

    private static void AssertCommonNavigation(string html)
    {
        Assert.Contains("Trang chủ", html, StringComparison.Ordinal);
        Assert.Contains("Sản phẩm", html, StringComparison.Ordinal);
        Assert.Contains("Tư vấn", html, StringComparison.Ordinal);
        Assert.Contains("Tài khoản", html, StringComparison.Ordinal);
        Assert.Contains("role=\"search\"", html, StringComparison.Ordinal);
    }

    private sealed class TestAdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var selectedRole = Request.Query["testRole"].ToString();
            if (string.Equals(selectedRole, "Anonymous", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            selectedRole = string.IsNullOrWhiteSpace(selectedRole) ? "Admin" : selectedRole;
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, string.Equals(selectedRole, "Customer", StringComparison.OrdinalIgnoreCase) ? "12" : "4"),
                new Claim(ClaimTypes.Name, "Admin UI Test"),
                new Claim(ClaimTypes.Role, selectedRole)
            ],
            Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestSalesOrderManagementService : ISalesOrderManagementService
    {
        private static StaffOrderDetail Detail { get; } = new(
            701,
            12,
            new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
            OrderStatuses.Pending,
            "NEXORA QA Sales Order",
            "0000000000",
            "QA address",
            null,
            4,
            new DateTime(2026, 10, 20, 10, 5, 0, DateTimeKind.Utc),
            null,
            null,
            null,
            1_000m,
            [new StaffOrderDetailLine(7, "QA snapshot product", 1, 1_000m, 0m, 1_000m)])
        {
            CancelRequestStatus = OrderCancellationRequestStatuses.Pending,
            CancelRequestReason = "HTTP view-only cancellation fixture",
            CancelRequestId = Guid.NewGuid(),
            CancelRequestedAt = new DateTime(2026, 10, 20, 10, 10, 0, DateTimeKind.Utc)
        };

        public Task<StaffOrderPage> SearchAsync(StaffOrderFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StaffOrderPage(
                [new StaffOrderListItem(701, Detail.OrderDate, Detail.Status, Detail.CustomerName, Detail.Phone, Detail.TotalAmount, 1)],
                1,
                Math.Max(1, filter.Page),
                25));

        public Task<StaffOrderDetail?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StaffOrderDetail?>(orderId == Detail.Id ? Detail : null);

        public Task<OrderTransitionResult> TransitionAsync(
            int orderId,
            int actorUserId,
            OrderWorkflowAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderTransitionResult(orderId == Detail.Id
                ? OrderTransitionFailure.None
                : OrderTransitionFailure.NotFound));
    }

    private sealed class TestCancellationReviewService : IOrderCancellationReviewService
    {
        public Task<CancellationReviewResult> ReviewAsync(
            int orderId,
            int reviewerUserId,
            bool approve,
            string? decisionNote,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancellationReviewResult(CancellationReviewFailure.None,
                approve ? OrderCancellationRequestStatuses.Approved : OrderCancellationRequestStatuses.Rejected));
    }

    private sealed class TestCustomerOrderService : ICustomerOrderService
    {
        public int? LastListUserId { get; private set; }
        public int? LastRequestUserId { get; private set; }
        public string? LastRequestReason { get; private set; }

        public Task<IReadOnlyList<OrderListItem>> GetOrdersAsync(int userId, CancellationToken cancellationToken = default)
        {
            LastListUserId = userId;
            IReadOnlyList<OrderListItem> result = userId == 12
                ? [
                    new OrderListItem(701, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), OrderStatuses.Confirmed, 1_000m, 1),
                    new OrderListItem(702, new DateTime(2026, 10, 20, 10, 5, 0, DateTimeKind.Utc), OrderStatuses.Exported, 48_960_000m, 4),
                    new OrderListItem(703, new DateTime(2026, 10, 20, 10, 10, 0, DateTimeKind.Utc), OrderStatuses.Completed, 5_990_000m, 1),
                    new OrderListItem(704, new DateTime(2026, 10, 20, 10, 15, 0, DateTimeKind.Utc), OrderStatuses.Cancelled, 2_000_000m, 2)
                    {
                        CancelRequestStatus = OrderCancellationRequestStatuses.Approved
                    }
                ]
                : [];
            return Task.FromResult(result);
        }

        public Task<OrderDetailView?> GetOrderAsync(int userId, int orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDetailView?>(userId == 12 && orderId == 701
                ? new OrderDetailView(
                    701,
                    new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
                    OrderStatuses.Confirmed,
                    "NEXORA HTTP QA Customer",
                    "0000000000",
                    "QA address",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    1_000m,
                    [new OrderDetailLine(7, "QA snapshot", 1, 1_000m, 0m, 1_000m)])
                : null);

        public Task<CancelOrderResult> CancelAsync(int userId, int orderId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelOrderResult(CancelOrderFailure.None));

        public Task<CustomerCancellationResult> RequestCancellationAsync(
            int userId,
            int orderId,
            Guid requestId,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            LastRequestUserId = userId;
            LastRequestReason = reason;
            return Task.FromResult(new CustomerCancellationResult(
                CustomerCancellationFailure.None,
                OrderCancellationRequestStatuses.Pending,
                OrderStatuses.Confirmed));
        }
    }

    private sealed class TestAppUserStore : IUserStore<NEXORA.Infrastructure.Identity.AppUser>
    {
        public Task<IdentityResult> CreateAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<NEXORA.Infrastructure.Identity.AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<NEXORA.Infrastructure.Identity.AppUser?>(null);
        public Task<NEXORA.Infrastructure.Identity.AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<NEXORA.Infrastructure.Identity.AppUser?>(null);
        public Task<string> GetUserIdAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(NEXORA.Infrastructure.Identity.AppUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(NEXORA.Infrastructure.Identity.AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(NEXORA.Infrastructure.Identity.AppUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class TestCustomerCatalogQueryService : ICustomerCatalogQueryService
    {
        public Task<CatalogHomeData> GetHomeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogHomeData([], []));

        public Task<CatalogProductPage> SearchAsync(CatalogSearchFilter filter, CancellationToken cancellationToken = default)
        {
            CatalogProductCard[] items = filter.Search switch
            {
                "ui-product-image-test" =>
                [
                    new CatalogProductCard(1, 1, "Ảnh kiểm thử giao diện", "UI-VISUAL-1", "Laptop", "Demo", 10000000m, null, null, null)
                    {
                        KeySpecifications = [new CatalogProductSpecification(1, "RAM", "NUMBER", "GB", "16", 16m, null)]
                    }
                ],
                "ui-stock-test" =>
                [
                    new CatalogProductCard(2, 1, "Sản phẩm còn hàng", "UI-STOCK-AVAILABLE", "Laptop", "Demo", 10000000m, null, null, null) { AvailableQuantity = 3 },
                    new CatalogProductCard(3, 1, "Sản phẩm hết hàng", "UI-STOCK-EMPTY", "Laptop", "Demo", 10000000m, null, null, null) { AvailableQuantity = 0 },
                    new CatalogProductCard(4, 1, "Sản phẩm thiếu Inventory", "UI-STOCK-MISSING", "Laptop", "Demo", 10000000m, null, null, null)
                ],
                _ => []
            };
            return Task.FromResult(new CatalogProductPage(items, [], [], [], null, items.Length, filter.Page, filter.PageSize));
        }

        public Task<CatalogCategoryOption?> GetCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogCategoryOption?>(null);

        public Task<CatalogProductDetails?> GetProductAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogProductDetails?>(productId switch
            {
                42 => new CatalogProductDetails(42, "Sản phẩm hết hàng", "san-pham-het-hang", "UI-STOCK-DETAIL", "Laptop", 1,
                    "Demo", 10_000_000m, null, "Thông tin kiểm thử", 12, [], []) { AvailableQuantity = 0 },
                43 => new CatalogProductDetails(43, "Sản phẩm còn hàng", "san-pham-con-hang", "UI-STOCK-DETAIL-2", "Laptop", 1,
                    "Demo", 10_000_000m, null, "Thông tin kiểm thử", 12, [], []) { AvailableQuantity = 3 },
                44 => new CatalogProductDetails(44, "Sản phẩm chưa có Inventory", "san-pham-chua-co-inventory", "UI-STOCK-DETAIL-3", "Laptop", 1,
                    "Demo", 10_000_000m, null, "Thông tin kiểm thử", 12, [], []),
                _ => null
            });

        public Task<CatalogComparison> CompareAsync(IReadOnlyList<int> productIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogComparison([], []));
    }

    private sealed class TestAdminDashboardQueryService : IAdminDashboardQueryService
    {
        public Task<AdminDashboardMetrics> GetMetricsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminDashboardMetrics(123, 117, 45, 8, 86));
    }

    private sealed class TestReviewService : IReviewService
    {
        public Task<IReadOnlyList<EligibleReviewOrderLine>> GetEligibleOrderLinesAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EligibleReviewOrderLine>>([]);

        public Task<IReadOnlyList<CustomerReviewItem>> GetCustomerReviewsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerReviewItem>>([]);

        public Task<ReviewSubmissionResult> SubmitAsync(int userId, int orderDetailId, int rating, string? comment, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewSubmissionResult(ReviewSubmissionFailure.None));

        public Task<IReadOnlyList<PublicProductReview>> GetApprovedForProductAsync(int productId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicProductReview>>([]);
    }

    private sealed class TestConsultationManagementService : IConsultationManagementService
    {
        private static readonly IReadOnlyList<UsageNeedAdminRow> UsageNeeds =
        [
            new(1, "PROGRAMMING", "Lập trình", "Nhu cầu kiểm thử UI", true, 1),
            new(2, "MOBILE", "Di động", "Nhu cầu đã tắt kiểm thử", false, 2)
        ];

        private static readonly IReadOnlyList<RecommendationRuleAdminRow> Rules =
        [
            new(1, 1, "Lập trình", 1, "Laptop", 1, "RAM", "GB", "NUMBER", "GTE", "16", 3m, true, true),
            new(2, 2, "Di động", 2, "Điện thoại", 2, "Dung lượng pin", "mAh", "NUMBER", "GTE", "5000", 1m, false, false)
        ];

        public Task<IReadOnlyList<UsageNeedAdminRow>> GetUsageNeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(UsageNeeds);

        public Task<int> SaveUsageNeedAsync(UsageNeedInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(input.Id ?? 3);

        public Task<IReadOnlyList<RecommendationRuleAdminRow>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Rules);

        public Task<RecommendationRuleFormOptions> GetRuleFormOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecommendationRuleFormOptions([], [], []));

        public Task<int> SaveRuleAsync(RecommendationRuleInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(input.Id ?? 3);

        public Task<bool> SetUsageNeedActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SetRecommendationRuleActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class TestAdminPromotionManagementService : IAdminPromotionManagementService
    {
        public Task<IReadOnlyList<PromotionAdminRow>> GetPromotionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PromotionAdminRow>>([]);

        public Task<IReadOnlyList<PromotionProductOption>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PromotionProductOption>>([]);

        public Task<PromotionAdminForm?> GetPromotionAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PromotionAdminForm?>(null);

        public Task<int> SaveAsync(PromotionAdminForm input, CancellationToken cancellationToken = default) =>
            Task.FromResult(input.Id ?? 1);

        public Task<bool> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
