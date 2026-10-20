using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SalesOrderManagementSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task SalesOrderService_SearchesAndTransitionsWithCompareAndSwapWithoutTouchingInventory()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var email = $"NEXORA-SALES-TEST-{marker}@EXAMPLE.INVALID";
        var userId = 0;
        var orderId = 0;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                await using var sqlConnection = new SqlConnection(connectionString);
                await sqlConnection.OpenAsync();
                await using var command = sqlConnection.CreateCommand();
                command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME(), state_desc FROM sys.databases WHERE name = DB_NAME();";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync(), "The SQL connection must report its current database.");
                Assert.Contains("PC01\\SQLEXPRESS", reader.GetString(0), StringComparison.OrdinalIgnoreCase);
                Assert.Equal("NEXORA_IntegrationTests", reader.GetString(1), ignoreCase: true);
                Assert.Equal("ONLINE", reader.GetString(2), ignoreCase: true);
                await reader.CloseAsync();

                var productId = await db.Products.AsNoTracking()
                    .Where(product => product.Status == ProductStatuses.Active)
                    .Select(product => (int?)product.Id)
                    .FirstOrDefaultAsync();
                Assert.NotNull(productId);

                var user = new AppUser
                {
                    UserName = email,
                    NormalizedUserName = email,
                    Email = email,
                    NormalizedEmail = email,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = $"NEXORA QA Sales {marker}",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                userId = user.Id;

                var order = new Order
                {
                    UserId = userId,
                    CustomerName = $"NEXORA QA Order {marker}",
                    Phone = "0000000000",
                    ShippingAddress = "QA fixture only",
                    OrderDate = DateTime.UtcNow,
                    TotalAmount = 100m,
                    Status = OrderStatuses.Pending,
                    Note = "Sales workflow integration fixture."
                };
                order.Details.Add(new OrderDetail
                {
                    ProductId = productId.Value,
                    ProductName = "Snapshot QA line",
                    Quantity = 1,
                    UnitPrice = 100m,
                    DiscountAmount = 0m,
                    TotalPrice = 100m
                });
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                orderId = order.Id;

                var inventoryBefore = await db.Inventories.AsNoTracking()
                    .Where(inventory => inventory.ProductId == productId.Value)
                    .Select(inventory => new { inventory.QuantityInStock, inventory.ReservedQuantity })
                    .SingleOrDefaultAsync();
                var service = new SalesOrderManagementService(db, NullLogger<SalesOrderManagementService>.Instance);

                var page = await service.SearchAsync(new StaffOrderFilter(marker, OrderStatuses.Pending));
                var listed = Assert.Single(page.Items);
                Assert.Equal(orderId, listed.Id);
                Assert.Equal(1, listed.ItemCount);
                var boundedPage = await service.SearchAsync(new StaffOrderFilter(marker, OrderStatuses.Pending, int.MaxValue));
                Assert.Equal(1, boundedPage.Page);
                var detail = await service.GetAsync(orderId);
                Assert.NotNull(detail);
                Assert.Equal($"NEXORA QA Order {marker}", detail.CustomerName);
                Assert.Single(detail.Lines);

                async Task<OrderTransitionResult> ConfirmWithSeparateContextAsync()
                {
                    await using var concurrentContext = new AppDbContext(options);
                    var concurrentService = new SalesOrderManagementService(
                        concurrentContext,
                        NullLogger<SalesOrderManagementService>.Instance);
                    return await concurrentService.TransitionAsync(orderId, userId, OrderWorkflowAction.Confirm);
                }

                var concurrentConfirmations = await Task.WhenAll(
                    ConfirmWithSeparateContextAsync(),
                    ConfirmWithSeparateContextAsync());
                Assert.Single(concurrentConfirmations, result => result.Failure == OrderTransitionFailure.None);
                Assert.Single(concurrentConfirmations, result => result.Failure == OrderTransitionFailure.AlreadyApplied);
                Assert.Equal(OrderTransitionFailure.AlreadyApplied,
                    (await service.TransitionAsync(orderId, userId, OrderWorkflowAction.Confirm)).Failure);
                Assert.Equal(OrderTransitionFailure.None,
                    (await service.TransitionAsync(orderId, userId, OrderWorkflowAction.HandoffToWarehouse)).Failure);
                Assert.Equal(OrderTransitionFailure.InvalidState,
                    (await service.TransitionAsync(orderId, userId, OrderWorkflowAction.MarkShipping)).Failure);

                await db.Orders.Where(item => item.Id == orderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, OrderStatuses.Exported));
                Assert.Equal(OrderTransitionFailure.None,
                    (await service.TransitionAsync(orderId, userId, OrderWorkflowAction.MarkShipping)).Failure);
                Assert.Equal(OrderTransitionFailure.None,
                    (await service.TransitionAsync(orderId, userId, OrderWorkflowAction.Complete)).Failure);

                db.ChangeTracker.Clear();
                var persisted = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == orderId);
                Assert.Equal(OrderStatuses.Completed, persisted.Status);
                Assert.Equal(userId, persisted.ConfirmedBy);
                Assert.NotNull(persisted.ConfirmedAt);
                Assert.NotNull(persisted.CompletedAt);

                var inventoryAfter = await db.Inventories.AsNoTracking()
                    .Where(inventory => inventory.ProductId == productId.Value)
                    .Select(inventory => new { inventory.QuantityInStock, inventory.ReservedQuantity })
                    .SingleOrDefaultAsync();
                Assert.Equal(inventoryBefore, inventoryAfter);
            }
        }
        finally
        {
            if (orderId > 0 || userId > 0)
            {
                await using var cleanup = new AppDbContext(options);
                if (orderId > 0)
                {
                    await cleanup.OrderDetails.Where(line => line.OrderId == orderId).ExecuteDeleteAsync();
                    await cleanup.Orders.Where(order => order.Id == orderId).ExecuteDeleteAsync();
                }
                if (userId > 0)
                {
                    await cleanup.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
                }
            }
        }
    }
}
