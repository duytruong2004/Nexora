using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Engagement;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class Phase6EngagementSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task FavoritesAndReviews_EnforceOwnershipUniquenessAndModerationVisibility()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var ownerEmail = $"phase62-owner-{marker}@example.invalid";
        var otherEmail = $"phase62-other-{marker}@example.invalid";
        var productSku = $"NEXORA-P62-{marker}";

        try
        {
            await AssertDedicatedQaAsync(connectionString);
            int ownerId;
            int otherId;
            int productId;
            int detailId;
            int pendingDetailId;
            await using (var db = new AppDbContext(options))
            {
                var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var owner = CreateUser(ownerEmail, $"Phase 6.2 owner {marker}");
                var other = CreateUser(otherEmail, $"Phase 6.2 other {marker}");
                db.Users.AddRange(owner, other);
                var product = CreateProduct(categoryId, brandId, productSku, marker);
                db.Products.Add(product);
                await db.SaveChangesAsync();
                ownerId = owner.Id;
                otherId = other.Id;
                productId = product.Id;

                var order = new Order
                {
                    UserId = ownerId,
                    CustomerName = $"QA review {marker}",
                    Phone = "0000000000",
                    ShippingAddress = "QA fixture only",
                    OrderDate = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    TotalAmount = 1_000m,
                    Status = OrderStatuses.Completed
                };
                order.Details.Add(new OrderDetail
                {
                    ProductId = productId,
                    ProductName = $"Snapshot {marker}",
                    Quantity = 1,
                    UnitPrice = 1_000m,
                    DiscountAmount = 0,
                    TotalPrice = 1_000m
                });
                var pendingOrder = new Order
                {
                    UserId = ownerId,
                    CustomerName = $"QA pending review {marker}",
                    Phone = "0000000000",
                    ShippingAddress = "QA fixture only",
                    OrderDate = DateTime.UtcNow,
                    TotalAmount = 1_000m,
                    Status = OrderStatuses.Pending
                };
                pendingOrder.Details.Add(new OrderDetail
                {
                    ProductId = productId,
                    ProductName = $"Pending snapshot {marker}",
                    Quantity = 1,
                    UnitPrice = 1_000m,
                    DiscountAmount = 0,
                    TotalPrice = 1_000m
                });
                db.Orders.AddRange(order, pendingOrder);
                await db.SaveChangesAsync();
                detailId = order.Details.Single().Id;
                pendingDetailId = pendingOrder.Details.Single().Id;
            }

            await using (var db = new AppDbContext(options))
            {
                var engagement = new CustomerEngagementService(db, NullLogger<CustomerEngagementService>.Instance);
                Assert.True((await engagement.AddAsync(ownerId, productId)).Succeeded);
                var duplicateFavorite = await engagement.AddAsync(ownerId, productId);
                Assert.True(duplicateFavorite.Succeeded);
                Assert.True(duplicateFavorite.AlreadyApplied);
                Assert.Single(await engagement.GetFavoritesAsync(ownerId));
                Assert.Empty(await engagement.GetFavoritesAsync(otherId));

                Assert.Equal(ReviewSubmissionFailure.OrderLineNotFound,
                    (await engagement.SubmitAsync(otherId, detailId, 5, null)).Failure);
                Assert.Equal(ReviewSubmissionFailure.InvalidRating,
                    (await engagement.SubmitAsync(ownerId, detailId, 6, null)).Failure);
                Assert.Equal(ReviewSubmissionFailure.IneligibleOrder,
                    (await engagement.SubmitAsync(ownerId, pendingDetailId, 5, null)).Failure);
                Assert.True((await engagement.SubmitAsync(ownerId, detailId, 5, "Nhận xét QA <script>" )).Succeeded);
                Assert.Equal(ReviewSubmissionFailure.AlreadyReviewed,
                    (await engagement.SubmitAsync(ownerId, detailId, 4, "duplicate")).Failure);
                Assert.Equal(ReviewStatuses.Pending, Assert.Single(await engagement.GetCustomerReviewsAsync(ownerId)).Status);
                Assert.Empty(await engagement.GetApprovedForProductAsync(productId));
                Assert.True((await engagement.RemoveAsync(ownerId, productId)).Succeeded);
                Assert.Empty(await engagement.GetFavoritesAsync(ownerId));
            }

            await using (var db = new AppDbContext(options))
            {
                var moderation = new ReviewModerationService(db, NullLogger<ReviewModerationService>.Instance);
                var reviewId = await db.Reviews.AsNoTracking().Where(review => review.OrderDetailId == detailId)
                    .Select(review => review.Id).SingleAsync();
                Assert.Equal(ReviewModerationFailure.None,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Approve)).Failure);
                Assert.Single(await new CustomerEngagementService(db, NullLogger<CustomerEngagementService>.Instance)
                    .GetApprovedForProductAsync(productId));
                Assert.Equal(ReviewModerationFailure.AlreadyInState,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Approve)).Failure);
                Assert.Equal(ReviewModerationFailure.None,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Hide)).Failure);
                Assert.Empty(await new CustomerEngagementService(db, NullLogger<CustomerEngagementService>.Instance)
                    .GetApprovedForProductAsync(productId));
                Assert.Equal(ReviewModerationFailure.None,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Approve)).Failure);

                await db.Reviews.Where(review => review.Id == reviewId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(review => review.Status, "ARCHIVED"));
                Assert.Empty(await new CustomerEngagementService(db, NullLogger<CustomerEngagementService>.Instance)
                    .GetApprovedForProductAsync(productId));
                Assert.Equal(ReviewModerationFailure.UnknownStatus,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Approve)).Failure);
                await db.Reviews.Where(review => review.Id == reviewId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(review => review.Status, "approved"));
                Assert.Empty(await new CustomerEngagementService(db, NullLogger<CustomerEngagementService>.Instance)
                    .GetApprovedForProductAsync(productId));
                Assert.Equal(ReviewModerationFailure.UnknownStatus,
                    (await moderation.TransitionAsync(reviewId, ownerId, ReviewModerationAction.Approve)).Failure);
            }
        }
        finally
        {
            await CleanupAsync(options, marker, ownerEmail, otherEmail, productSku);
        }
    }

    private static async Task AssertDedicatedQaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME(), state_desc FROM sys.databases WHERE name=DB_NAME();";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Contains("PC01\\SQLEXPRESS", reader.GetString(0), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NEXORA_IntegrationTests", reader.GetString(1), ignoreCase: true);
        Assert.Equal("ONLINE", reader.GetString(2), ignoreCase: true);
    }

    private static AppUser CreateUser(string email, string name) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        FullName = name,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Product CreateProduct(int categoryId, int brandId, string sku, string marker) => new()
    {
        CategoryId = categoryId,
        BrandId = brandId,
        Name = $"Phase 6.2 QA product {marker}",
        Slug = $"phase-62-{marker}",
        Sku = sku,
        Price = 1_000m,
        WarrantyMonths = 12,
        Status = ProductStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static async Task CleanupAsync(DbContextOptions<AppDbContext> options, string marker,
        string ownerEmail, string otherEmail, string productSku)
    {
        await using var db = new AppDbContext(options);
        var userIds = await db.Users.Where(user => user.Email == ownerEmail || user.Email == otherEmail)
            .Select(user => user.Id).ToArrayAsync();
        var productId = await db.Products.Where(product => product.Sku == productSku)
            .Select(product => (int?)product.Id).SingleOrDefaultAsync();
        if (productId is int id)
        {
            await db.Reviews.Where(review => review.ProductId == id).ExecuteDeleteAsync();
            await db.Favorites.Where(favorite => favorite.ProductId == id).ExecuteDeleteAsync();
            await db.InventoryTransactions.Where(transaction => transaction.ProductId == id).ExecuteDeleteAsync();
            await db.Inventories.Where(inventory => inventory.ProductId == id).ExecuteDeleteAsync();
        }
        if (userIds.Length > 0)
        {
            await db.Reviews.Where(review => userIds.Contains(review.UserId)).ExecuteDeleteAsync();
            await db.Favorites.Where(favorite => userIds.Contains(favorite.UserId)).ExecuteDeleteAsync();
            await db.Orders.Where(order => userIds.Contains(order.UserId)).ExecuteDeleteAsync();
        }
        if (productId is int productKey)
            await db.Products.Where(product => product.Id == productKey).ExecuteDeleteAsync();
        if (userIds.Length > 0)
            await db.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
    }
}
