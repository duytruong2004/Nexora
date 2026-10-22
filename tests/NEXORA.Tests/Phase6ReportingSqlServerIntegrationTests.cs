using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Admin;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Admin;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class Phase6ReportingSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Reports_UseApprovedDateAndOrderSnapshotDefinitions_AndInventoryIsReadOnly()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var userEmail = $"phase64-report-{marker}@example.invalid";
        var productSku = $"NEXORA-P64-{marker}";
        var missingSku = $"NEXORA-P64-MISSING-{marker}";
        try
        {
            await AssertDedicatedQaAsync(connectionString);
            int productId;
            int missingProductId;
            DateOnly localDate;
            await using (var db = new AppDbContext(options))
            {
                var zone = ReportingBusinessTimeZone.Resolve();
                localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
                var bounds = new ReportDateRange(localDate, localDate).ToUtcBounds(zone);
                var completedAt = bounds.StartUtc.AddHours(12);
                var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var user = CreateUser(userEmail, $"Phase 6.4 report {marker}");
                db.Users.Add(user);
                var product = CreateProduct(categoryId, brandId, productSku, marker);
                var missingProduct = CreateProduct(categoryId, brandId, missingSku, $"Missing {marker}");
                db.Products.AddRange(product, missingProduct);
                await db.SaveChangesAsync();
                productId = product.Id;
                missingProductId = missingProduct.Id;

                var orderOne = CreateOrder(user.Id, marker, OrderStatuses.Completed, completedAt, 1_500m);
                orderOne.Details.Add(CreateLine(productId, $"Snapshot {marker}", 1, 1_000m));
                orderOne.Details.Add(CreateLine(productId, $"Snapshot {marker}", 1, 500m));
                var orderTwo = CreateOrder(user.Id, marker, OrderStatuses.Completed, completedAt.AddHours(1), 500m);
                orderTwo.Details.Add(CreateLine(productId, $"Snapshot {marker}", 2, 250m));
                var pending = CreateOrder(user.Id, marker, OrderStatuses.Pending, null, 9_000m);
                pending.Details.Add(CreateLine(productId, $"Snapshot {marker}", 9, 1_000m));
                db.Orders.AddRange(orderOne, orderTwo, pending);

                var inventory = new Inventory { ProductId = productId };
                inventory.Receive(3);
                inventory.Reserve(1);
                db.Inventories.Add(inventory);
                await db.SaveChangesAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var service = new SqlAdminReportingQueryService(db);
                var range = new ReportDateRange(localDate, localDate);
                var orders = await service.GetOrdersAsync(range);
                Assert.Contains(orders.StatusCounts, item => item.Status == OrderStatuses.Pending && item.Count >= 1);
                Assert.Contains(orders.StatusCounts, item => item.Status == OrderStatuses.Completed && item.Count >= 2);

                var revenue = await service.GetRevenueAsync(range);
                Assert.Equal(2_000m, revenue.TotalRevenue);
                Assert.Equal(2_000m, Assert.Single(revenue.DailyRevenue).Amount);
                Assert.Equal(localDate, revenue.DailyRevenue.Single().Date);

                var sales = await service.GetProductSalesAsync(range);
                var productSales = Assert.Single(sales.Items, item => item.ProductId == productId);
                Assert.Equal(4, productSales.QuantitySold);
                Assert.Equal($"Snapshot {marker}", productSales.ProductNameSnapshot);

                var inventoryReport = await service.GetInventoryAsync(threshold: 2);
                var reportRow = Assert.Single(inventoryReport.Items, item => item.ProductId == productId);
                Assert.Equal(3, reportRow.QuantityInStock);
                Assert.Equal(1, reportRow.ReservedQuantity);
                Assert.Equal(2, reportRow.AvailableQuantity);
                Assert.Equal(InventoryReportStatus.Low, reportRow.Status);
                var missingRow = Assert.Single(inventoryReport.Items, item => item.ProductId == missingProductId);
                Assert.Equal(InventoryReportStatus.Missing, missingRow.Status);
            }
        }
        finally
        {
            await CleanupAsync(options, userEmail, productSku, missingSku);
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

    private static Product CreateProduct(int categoryId, int brandId, string sku, string nameSuffix) => new()
    {
        CategoryId = categoryId,
        BrandId = brandId,
        Name = $"Phase 6.4 QA {nameSuffix}",
        Slug = $"phase-64-{Guid.NewGuid():N}",
        Sku = sku,
        Price = 1_000m,
        WarrantyMonths = 12,
        Status = ProductStatuses.Active,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Order CreateOrder(int userId, string marker, string status, DateTime? completedAt, decimal total) => new()
    {
        UserId = userId,
        CustomerName = $"Phase 6.4 order {marker}",
        Phone = "0000000000",
        ShippingAddress = "QA fixture only",
        OrderDate = completedAt ?? DateTime.UtcNow,
        CompletedAt = completedAt,
        TotalAmount = total,
        Status = status
    };

    private static OrderDetail CreateLine(int productId, string snapshot, int quantity, decimal unitPrice) => new()
    {
        ProductId = productId,
        ProductName = snapshot,
        Quantity = quantity,
        UnitPrice = unitPrice,
        DiscountAmount = 0,
        TotalPrice = unitPrice * quantity
    };

    private static async Task CleanupAsync(DbContextOptions<AppDbContext> options,
        string userEmail, string productSku, string missingSku)
    {
        await using var db = new AppDbContext(options);
        var userIds = await db.Users.Where(user => user.Email == userEmail).Select(user => user.Id).ToArrayAsync();
        var productIds = await db.Products.Where(product => product.Sku == productSku || product.Sku == missingSku)
            .Select(product => product.Id).ToArrayAsync();
        if (productIds.Length > 0)
        {
            await db.InventoryTransactions.Where(transaction => productIds.Contains(transaction.ProductId)).ExecuteDeleteAsync();
            await db.Inventories.Where(inventory => productIds.Contains(inventory.ProductId)).ExecuteDeleteAsync();
        }
        if (userIds.Length > 0)
        {
            await db.Orders.Where(order => userIds.Contains(order.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
        }
        if (productIds.Length > 0)
            await db.Products.Where(product => productIds.Contains(product.Id)).ExecuteDeleteAsync();
    }
}
