using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Admin;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
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
        builder.Services.AddSingleton<IAdminDashboardQueryService, TestAdminDashboardQueryService>();
        builder.Services.AddSingleton<IConsultationManagementService, TestConsultationManagementService>();

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

            using var customerCatalogResponse = await client.GetAsync("/Catalog?testRole=Customer&search=laptop");
            Assert.Equal(HttpStatusCode.OK, customerCatalogResponse.StatusCode);
            Assert.Contains("Kết quả cho “laptop”", await customerCatalogResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

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
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindWebProjectRoot()
    {
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
                new Claim(ClaimTypes.NameIdentifier, "admin-layout-test-only"),
                new Claim(ClaimTypes.Name, "Admin UI Test"),
                new Claim(ClaimTypes.Role, selectedRole)
            ],
            Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestCustomerCatalogQueryService : ICustomerCatalogQueryService
    {
        public Task<CatalogHomeData> GetHomeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogHomeData([], []));

        public Task<CatalogProductPage> SearchAsync(CatalogSearchFilter filter, CancellationToken cancellationToken = default)
        {
            var items = filter.Search == "ui-product-image-test"
                ? new CatalogProductCard(1, 1, "Ảnh kiểm thử giao diện", "UI-VISUAL-1", "Laptop", "Demo", 10000000m, null, null, null)
                {
                    KeySpecifications = [new CatalogProductSpecification(1, "RAM", "NUMBER", "GB", "16", 16m, null)]
                }
                : null;
            var products = items is null ? Array.Empty<CatalogProductCard>() : [items];
            return Task.FromResult(new CatalogProductPage(products, [], [], [], null, products.Length, filter.Page, filter.PageSize));
        }

        public Task<CatalogCategoryOption?> GetCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogCategoryOption?>(null);

        public Task<CatalogProductDetails?> GetProductAsync(int productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogProductDetails?>(null);

        public Task<CatalogComparison> CompareAsync(IReadOnlyList<int> productIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogComparison([], []));
    }

    private sealed class TestAdminDashboardQueryService : IAdminDashboardQueryService
    {
        public Task<AdminDashboardMetrics> GetMetricsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminDashboardMetrics(123, 117, 45, 8, 86));
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
}
