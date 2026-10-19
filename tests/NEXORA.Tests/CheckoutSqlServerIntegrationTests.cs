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
public sealed class CheckoutSqlServerIntegrationTests
{
    [CheckoutSqlServerFact]
    public async Task ConcurrentCheckout_IsIdempotent_SnapshotsPrice_AndCancellationReleasesOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("NEXORA_IntegrationTests", connection.InitialCatalog, ignoreCase: true);
        Assert.Contains("PC01\\SQLEXPRESS", connection.DataSource, StringComparison.OrdinalIgnoreCase);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null))
            .Options;
        var userIds = new List<int>();
        var productId = 0;
        var orderIds = new List<int>();
        var marker = Guid.NewGuid().ToString("N");

        try
        {
            await using (var setup = new AppDbContext(options))
            {
                var categoryId = await setup.Categories.AsNoTracking().Where(item => item.IsActive)
                    .Select(item => item.Id).FirstAsync();
                var brandId = await setup.Brands.AsNoTracking().Where(item => item.IsActive)
                    .Select(item => item.Id).FirstAsync();
                for (var index = 0; index < 2; index++)
                {
                    var email = $"phase4-checkout-{marker}-{index}@example.invalid";
                    var user = new AppUser
                    {
                        UserName = email,
                        NormalizedUserName = email.ToUpperInvariant(),
                        Email = email,
                        NormalizedEmail = email.ToUpperInvariant(),
                        EmailConfirmed = true,
                        SecurityStamp = Guid.NewGuid().ToString("N"),
                        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                        FullName = $"NEXORA checkout QA {marker}",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    setup.Users.Add(user);
                    await setup.SaveChangesAsync();
                    userIds.Add(user.Id);
                }

                var product = new Product
                {
                    CategoryId = categoryId,
                    BrandId = brandId,
                    Name = $"NEXORA checkout QA {marker}",
                    Slug = $"phase4-checkout-{marker}",
                    Sku = $"PH4-{marker}",
                    Price = 1_000m,
                    Status = ProductStatuses.Active,
                    WarrantyMonths = 12,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                setup.Products.Add(product);
                await setup.SaveChangesAsync();
                productId = product.Id;

                var inventory = new Inventory { ProductId = productId };
                inventory.Receive(1);
                setup.Inventories.Add(inventory);
                foreach (var userId in userIds)
                {
                    var cart = new Cart { UserId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                    cart.Items.Add(new CartItem
                    {
                        ProductId = productId,
                        Quantity = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    setup.Carts.Add(cart);
                }

                await setup.SaveChangesAsync();
            }

            var inputs = new List<PlaceOrderInput>();
            foreach (var userId in userIds)
            {
                await using var quoteDb = new AppDbContext(options);
                var quote = await new OrderPricingService(quoteDb).GetCurrentQuoteAsync(userId);
                Assert.True(quote.CanCheckout);
                inputs.Add(new PlaceOrderInput(
                    Guid.NewGuid(),
                    $"Checkout QA {marker}",
                    "+84901234567",
                    "12 Nguyen Hue, District 1",
                    null,
                    quote.Fingerprint));
            }

            var results = await Task.WhenAll(userIds.Select(async (userId, index) =>
            {
                await using var checkoutDb = new AppDbContext(options);
                var service = new CheckoutService(checkoutDb, new OrderPricingService(checkoutDb),
                    NullLogger<CheckoutService>.Instance);
                return (UserId: userId, Input: inputs[index], Result: await service.PlaceOrderAsync(userId, inputs[index]));
            }));

            var winner = Assert.Single(results, item => item.Result.Succeeded);
            var loser = Assert.Single(results, item => !item.Result.Succeeded);
            Assert.Equal(PlaceOrderFailure.InsufficientStock, loser.Result.Failure);
            var orderId = Assert.IsType<int>(winner.Result.OrderId);
            orderIds.Add(orderId);

            await using (var verification = new AppDbContext(options))
            {
                var inventory = await verification.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == productId);
                Assert.Equal(1, inventory.QuantityInStock);
                Assert.Equal(1, inventory.ReservedQuantity);
                Assert.Equal(1, await verification.Orders.CountAsync(order => order.UserId == winner.UserId));
                Assert.Equal(0, await verification.Orders.CountAsync(order => order.UserId == loser.UserId));
                Assert.Equal(1, await verification.InventoryTransactions.CountAsync(item => item.OrderId == orderId && item.TransactionType == "RESERVE"));

                var orderLine = await verification.OrderDetails.AsNoTracking().SingleAsync(item => item.OrderId == orderId);
                Assert.Equal("NEXORA checkout QA " + marker, orderLine.ProductName);
                Assert.Equal(1_000m, orderLine.UnitPrice);
                Assert.Equal(0m, orderLine.DiscountAmount);
                Assert.Equal(1_000m, orderLine.TotalPrice);
                Assert.Equal(1_000m, await verification.Orders.Where(item => item.Id == orderId)
                    .Select(item => item.TotalAmount).SingleAsync());
            }

            await using (var orderQueryDb = new AppDbContext(options))
            {
                var service = new CustomerOrderService(orderQueryDb, NullLogger<CustomerOrderService>.Instance);
                var ownerOrders = await service.GetOrdersAsync(winner.UserId);
                Assert.Single(ownerOrders);
                Assert.Equal(orderId, ownerOrders[0].Id);
                var ownerDetail = await service.GetOrderAsync(winner.UserId, orderId);
                Assert.NotNull(ownerDetail);
                Assert.Equal(1_000m, Assert.Single(ownerDetail.Lines).UnitPrice);
                Assert.Null(await service.GetOrderAsync(loser.UserId, orderId));
                Assert.Equal(CancelOrderFailure.NotFound,
                    (await service.CancelAsync(loser.UserId, orderId, "Wrong owner test")).Failure);
            }

            await using (var replayDb = new AppDbContext(options))
            {
                var replay = await new CheckoutService(replayDb, new OrderPricingService(replayDb),
                    NullLogger<CheckoutService>.Instance)
                    .PlaceOrderAsync(winner.UserId, winner.Input);
                Assert.True(replay.Succeeded);
                Assert.Equal(orderId, replay.OrderId);
                Assert.Equal(1, await replayDb.Orders.CountAsync(order => order.CheckoutRequestId == winner.Input.RequestId));
            }

            await using (var updatePriceDb = new AppDbContext(options))
            {
                await updatePriceDb.Products.Where(item => item.Id == productId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Price, 2_500m));
                Assert.Equal(1_000m, await updatePriceDb.OrderDetails.AsNoTracking()
                    .Where(item => item.OrderId == orderId).Select(item => item.UnitPrice).SingleAsync());
            }

            await using (var cancellationDb = new AppDbContext(options))
            {
                var service = new CustomerOrderService(cancellationDb, NullLogger<CustomerOrderService>.Instance);
                Assert.True((await service.CancelAsync(winner.UserId, orderId, "QA cancellation")).Succeeded);
                Assert.Equal(CancelOrderFailure.CannotCancel,
                    (await service.CancelAsync(winner.UserId, orderId, "Second attempt")).Failure);
            }

            await using (var verification = new AppDbContext(options))
            {
                var inventory = await verification.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == productId);
                Assert.Equal(1, inventory.QuantityInStock);
                Assert.Equal(0, inventory.ReservedQuantity);
                Assert.Equal(1, await verification.InventoryTransactions.CountAsync(item => item.OrderId == orderId && item.TransactionType == "RELEASE"));
                Assert.Equal(OrderStatuses.Cancelled, await verification.Orders.Where(item => item.Id == orderId)
                    .Select(item => item.Status).SingleAsync());
            }
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            cleanup.ChangeTracker.Clear();
            var userOrders = await cleanup.Orders.Where(order => userIds.Contains(order.UserId)).ToListAsync();
            orderIds.AddRange(userOrders.Select(order => order.Id));
            orderIds = orderIds.Distinct().ToList();
            var transactions = await cleanup.InventoryTransactions
                .Where(item => orderIds.Contains(item.OrderId ?? 0) || userIds.Contains(item.UserId))
                .ToListAsync();
            cleanup.InventoryTransactions.RemoveRange(transactions);
            var details = await cleanup.OrderDetails.Where(item => orderIds.Contains(item.OrderId)).ToListAsync();
            cleanup.OrderDetails.RemoveRange(details);
            cleanup.Orders.RemoveRange(userOrders);
            var cartItems = await cleanup.CartItems.Where(item => userIds.Contains(item.Cart.UserId)).ToListAsync();
            cleanup.CartItems.RemoveRange(cartItems);
            var carts = await cleanup.Carts.Where(item => userIds.Contains(item.UserId)).ToListAsync();
            cleanup.Carts.RemoveRange(carts);
            if (productId > 0)
            {
                var inventory = await cleanup.Inventories.SingleOrDefaultAsync(item => item.ProductId == productId);
                if (inventory is not null) cleanup.Inventories.Remove(inventory);
                var product = await cleanup.Products.SingleOrDefaultAsync(item => item.Id == productId);
                if (product is not null) cleanup.Products.Remove(product);
            }
            var users = await cleanup.Users.Where(item => userIds.Contains(item.Id)).ToListAsync();
            cleanup.Users.RemoveRange(users);
            await cleanup.SaveChangesAsync();
        }
    }
}
