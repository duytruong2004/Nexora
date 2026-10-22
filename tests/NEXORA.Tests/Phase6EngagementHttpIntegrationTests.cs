using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Admin;
using NEXORA.Application.Commerce;
using NEXORA.Application.Engagement;

namespace NEXORA.Tests;

public sealed class Phase6EngagementHttpIntegrationTests
{
    [Fact]
    public async Task FavoritesReviewsModerationAndReports_EnforceRoleBoundariesAndAntiforgery()
    {
        var favorites = new FakeFavorites();
        var reviews = new FakeReviews();
        var moderation = new FakeModeration();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(NEXORA.Web.Controllers.FavoritesController).Assembly.GetName().Name,
            ContentRootPath = FindWebProjectRoot(),
            WebRootPath = Path.Combine(FindWebProjectRoot(), "wwwroot"),
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Phase6Test")
            .AddScheme<AuthenticationSchemeOptions, Phase6TestAuthenticationHandler>("Phase6Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(NEXORA.Web.Controllers.FavoritesController).Assembly);
        builder.Services.AddSingleton<IFavoriteService>(favorites);
        builder.Services.AddSingleton<IReviewService>(reviews);
        builder.Services.AddSingleton<IReviewModerationService>(moderation);
        builder.Services.AddSingleton<IAdminReportingQueryService, FakeReports>();
        builder.Services.AddSingleton<ICartService, FakeCartService>();

        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = new CookieContainer(),
                UseCookies = true
            })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Favorites?testRole=Customer")).StatusCode);
            using var customerReviews = await client.GetAsync("/Reviews?testRole=Customer");
            Assert.Equal(HttpStatusCode.OK, customerReviews.StatusCode);
            var customerReviewsHtml = await customerReviews.Content.ReadAsStringAsync();
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", customerReviewsHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert(1)</script>", customerReviewsHtml, StringComparison.Ordinal);
            Assert.Contains("role=\"tablist\"", customerReviewsHtml, StringComparison.Ordinal);
            Assert.Contains("Chờ đánh giá", customerReviewsHtml, StringComparison.Ordinal);
            Assert.Contains("Đã đánh giá", customerReviewsHtml, StringComparison.Ordinal);
            Assert.Contains("data-review-dialog", customerReviewsHtml, StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", customerReviewsHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Vui lòng kiểm tra thông tin đánh giá được đánh dấu bên dưới.", customerReviewsHtml, StringComparison.Ordinal);

            var tokenMatch = Regex.Match(customerReviewsHtml,
                "__RequestVerificationToken[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
            Assert.True(tokenMatch.Success, "The review form should include an antiforgery request token.");
            var requestToken = tokenMatch.Groups[1].Success ? tokenMatch.Groups[1].Value : tokenMatch.Groups[2].Value;
            using var invalidReviewPost = await client.PostAsync("/Reviews/Submit?testRole=Customer", new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", requestToken),
                new("orderDetailId", "30"),
                new("rating", "0"),
                new("comment", "")
            ]));
            Assert.Equal(HttpStatusCode.OK, invalidReviewPost.StatusCode);
            var invalidReviewHtml = await invalidReviewPost.Content.ReadAsStringAsync();
            Assert.Contains("data-reopen-review-id=\"30\"", invalidReviewHtml, StringComparison.Ordinal);
            Assert.Contains("aria-invalid=\"true\"", invalidReviewHtml, StringComparison.Ordinal);
            Assert.Contains("Vui lòng chọn số sao từ 1 đến 5.", WebUtility.HtmlDecode(invalidReviewHtml), StringComparison.Ordinal);
            Assert.Equal(0, reviews.SubmitCalls);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Reviews/Index?testRole=Admin")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Reports/Orders?testRole=Admin")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Favorites?testRole=Sales")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Reviews?testRole=Warehouse")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Reviews/Index?testRole=Customer")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Reports/Orders?testRole=Sales")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Favorites?testAnonymous=true")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Admin/Reports/Orders?testAnonymous=true")).StatusCode);

            using var customerPost = await client.PostAsync("/Favorites/Add?testRole=Customer",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("productId", "7")]));
            Assert.Equal(HttpStatusCode.BadRequest, customerPost.StatusCode);
            Assert.Equal(0, favorites.AddCalls);
            using var adminPost = await client.PostAsync("/Admin/Reviews/Transition?testRole=Admin",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("id", "7"), new("action", "Approve")]));
            Assert.Equal(HttpStatusCode.BadRequest, adminPost.StatusCode);
            Assert.Equal(0, moderation.TransitionCalls);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindWebProjectRoot()
    {
        var configuredRepositoryRoot = Environment.GetEnvironmentVariable("NEXORA_REPOSITORY_ROOT");
        var startPaths = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), configuredRepositoryRoot }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!);

        foreach (var startPath in startPaths)
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "NEXORA.sln")))
                current = current.Parent;
            if (current is not null)
                return Path.Combine(current.FullName, "src", "NEXORA.Web");
        }

        throw new DirectoryNotFoundException("NEXORA.sln not found from the test output or working directory.");
    }

    private sealed class Phase6TestAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Query.ContainsKey("testAnonymous")) return Task.FromResult(AuthenticateResult.NoResult());
            var role = Request.Query["testRole"].FirstOrDefault() ?? "Customer";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9001"),
                new Claim(ClaimTypes.Name, "NEXORA phase 6 test"),
                new Claim(ClaimTypes.Role, role)
            ], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class FakeFavorites : IFavoriteService
    {
        public int AddCalls { get; private set; }
        public Task<IReadOnlyList<FavoriteListItem>> GetFavoritesAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FavoriteListItem>>([]);
        public Task<FavoriteOperationResult> AddAsync(int userId, int productId, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            return Task.FromResult(new FavoriteOperationResult(FavoriteFailure.None));
        }
        public Task<FavoriteOperationResult> RemoveAsync(int userId, int productId, CancellationToken cancellationToken = default) => Task.FromResult(new FavoriteOperationResult(FavoriteFailure.None));
    }

    private sealed class FakeReviews : IReviewService
    {
        public int SubmitCalls { get; private set; }

        public Task<IReadOnlyList<EligibleReviewOrderLine>> GetEligibleOrderLinesAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EligibleReviewOrderLine>>([
                new EligibleReviewOrderLine(2, DateTime.UtcNow, 30, 4, "Eligible QA product", 1)
                {
                    ProductImageUrl = "/uploads/qa-review-product.png"
                }
            ]);
        public Task<IReadOnlyList<CustomerReviewItem>> GetCustomerReviewsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerReviewItem>>([
                new CustomerReviewItem(1, 2, 3, 4, "QA product", 5, "<script>alert(1)</script>", "PENDING", DateTime.UtcNow, DateTime.UtcNow)
            ]);
        public Task<ReviewSubmissionResult> SubmitAsync(int userId, int orderDetailId, int rating, string? comment, CancellationToken cancellationToken = default)
        {
            SubmitCalls++;
            return Task.FromResult(new ReviewSubmissionResult(ReviewSubmissionFailure.None));
        }
        public Task<IReadOnlyList<PublicProductReview>> GetApprovedForProductAsync(int productId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicProductReview>>([]);
    }

    private sealed class FakeModeration : IReviewModerationService
    {
        public int TransitionCalls { get; private set; }
        public Task<ReviewModerationPage> SearchAsync(ReviewModerationFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewModerationPage([], 0, 0, 1, 25));
        public Task<ReviewModerationResult> TransitionAsync(int reviewId, int actorUserId, ReviewModerationAction action, CancellationToken cancellationToken = default)
        {
            TransitionCalls++;
            return Task.FromResult(new ReviewModerationResult(ReviewModerationFailure.None));
        }
    }

    private sealed class FakeReports : IAdminReportingQueryService
    {
        public Task<AdminOrdersReport> GetOrdersAsync(ReportDateRange range, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminOrdersReport(range, [], 0));
        public Task<AdminRevenueReport> GetRevenueAsync(ReportDateRange range, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminRevenueReport(range, 0, []));
        public Task<AdminProductSalesReport> GetProductSalesAsync(ReportDateRange range, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminProductSalesReport(range, [], 0, 1, pageSize));
        public Task<AdminInventoryReport> GetInventoryAsync(int threshold, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminInventoryReport(threshold, new InventoryReportCounts(0, 0, 0, 0, 0), [], 0, 1, pageSize));
    }

    private sealed class FakeCartService : ICartService
    {
        public Task<CartSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult(new CartSnapshot(null, []));
        public Task<int> GetItemCountAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<CartMutationResult> AddAsync(int userId, int productId, int quantity, CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);
        public Task<CartMutationResult> SetQuantityAsync(int userId, int cartItemId, int quantity, CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);
        public Task<CartMutationResult> RemoveAsync(int userId, int cartItemId, CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);
    }
}
