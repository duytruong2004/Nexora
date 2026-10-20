using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class WarehouseInventorySqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Receive_IncreasesStockOnce_PreservesReservations_AndWritesAuditableLedger()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 8, reserved: 3);
        var operationId = Guid.NewGuid();
        try
        {
            var first = await ReceiveAsync(options, fixture, operationId, 5, "PO-501", "Nhập hàng QA");
            var replay = await ReceiveAsync(options, fixture, operationId, 5, "PO-501", "Nhập hàng QA");
            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var entry = Assert.Single(await verify.InventoryTransactions.AsNoTracking()
                .Where(item => item.ProductId == fixture.ProductId && item.TransactionType == "IN")
                .ToArrayAsync());

            Assert.Equal(WarehouseInventoryOperationFailure.None, first.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.AlreadyApplied, replay.Failure);
            Assert.Equal(13, inventory.QuantityInStock);
            Assert.Equal(3, inventory.ReservedQuantity);
            Assert.Equal(10, inventory.AvailableQuantity);
            Assert.Equal(fixture.ActorId, entry.UserId);
            Assert.Equal(5, entry.Quantity);
            Assert.Null(entry.OrderId);
            Assert.Contains($"OPID={operationId:N};DIR=IN;", entry.Note, StringComparison.Ordinal);
            Assert.Contains("REF=PO-501", entry.Note, StringComparison.Ordinal);
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task Receipt_RejectsInvalidQuantities_MissingProducts_AndMissingInventoryWithoutCreatingIt()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options);
        try
        {
            Assert.Equal(WarehouseInventoryOperationFailure.InvalidRequest,
                (await ReceiveAsync(options, fixture, Guid.NewGuid(), 0, "QA", null)).Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.InvalidRequest,
                (await ReceiveAsync(options, fixture, Guid.NewGuid(), -1, "QA", null)).Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.ProductNotFound,
                (await ReceiveAsync(options, fixture with { ProductId = int.MaxValue }, Guid.NewGuid(), 1, "QA", null)).Failure);

            await using (var delete = new AppDbContext(options))
                await delete.Inventories.Where(item => item.ProductId == fixture.ProductId).ExecuteDeleteAsync();

            Assert.Equal(WarehouseInventoryOperationFailure.InventoryMissing,
                (await ReceiveAsync(options, fixture, Guid.NewGuid(), 1, "QA", null)).Failure);
            await using var verify = new AppDbContext(options);
            Assert.False(await verify.Inventories.AnyAsync(item => item.ProductId == fixture.ProductId));
            Assert.False(await verify.InventoryTransactions.AnyAsync(item => item.ProductId == fixture.ProductId));
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task Adjustment_SupportsIncreaseAndDecrease_AndRejectsReductionBelowReservedStock()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 12, reserved: 4);
        try
        {
            var increaseId = Guid.NewGuid();
            var increase = await AdjustAsync(options, fixture, increaseId, "INCREASE", 3, "Kiểm kê thừa");
            var increaseReplay = await AdjustAsync(options, fixture, increaseId, "INCREASE", 3, "Kiểm kê thừa");
            var decreaseId = Guid.NewGuid();
            var decrease = await AdjustAsync(options, fixture, decreaseId, "DECREASE", 2, "Hàng hỏng");
            var rejected = await AdjustAsync(options, fixture, Guid.NewGuid(), "DECREASE", 10, "Vượt hàng đang giữ");
            var invalidZero = await AdjustAsync(options, fixture, Guid.NewGuid(), "INCREASE", 0, "Sai lượng");
            var invalidNegative = await AdjustAsync(options, fixture, Guid.NewGuid(), "DECREASE", -1, "Sai lượng");

            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(item => item.ProductId == fixture.ProductId && item.TransactionType == "ADJUST")
                .OrderBy(item => item.Id).ToArrayAsync();

            Assert.Equal(WarehouseInventoryOperationFailure.None, increase.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.AlreadyApplied, increaseReplay.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.None, decrease.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.BelowReservedQuantity, rejected.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.InvalidRequest, invalidZero.Failure);
            Assert.Equal(WarehouseInventoryOperationFailure.InvalidRequest, invalidNegative.Failure);
            Assert.Equal(13, inventory.QuantityInStock);
            Assert.Equal(4, inventory.ReservedQuantity);
            Assert.Equal(9, inventory.AvailableQuantity);
            Assert.Collection(ledger,
                item => { Assert.Equal(3, item.Quantity); Assert.Contains($"OPID={increaseId:N};DIR=INCREASE;", item.Note); },
                item => { Assert.Equal(2, item.Quantity); Assert.Contains($"OPID={decreaseId:N};DIR=DECREASE;", item.Note); });
            Assert.All(ledger, item => Assert.Equal(fixture.ActorId, item.UserId));
            Assert.Contains("Kiểm kê thừa", ledger[0].Note, StringComparison.Ordinal);
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task LedgerFailure_RollsBackInventoryMutation()
    {
        var connection = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(new FailInventoryLedgerInsertInterceptor())
            .Options;
        var fixture = await CreateFixtureAsync(CreateOptions(), stock: 20, reserved: 4);
        try
        {
            var result = await ReceiveAsync(options, fixture, Guid.NewGuid(), 6, "ROLLBACK-QA", null);
            await using var verify = new AppDbContext(CreateOptions());
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.False(result.Succeeded);
            Assert.Equal(20, inventory.QuantityInStock);
            Assert.Equal(4, inventory.ReservedQuantity);
            Assert.False(await verify.InventoryTransactions.AnyAsync(item =>
                item.ProductId == fixture.ProductId && item.Note!.Contains("ROLLBACK-QA")));
        }
        finally { await CleanupAsync(CreateOptions(), fixture.Marker); }
    }

    [SqlServerFact]
    public async Task ConcurrentSameRequestToken_AppliesOneReceipt_AndDifferentPayloadIsRejected()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 2);
        var operationId = Guid.NewGuid();
        try
        {
            var parallel = await Task.WhenAll(
                ReceiveAsync(options, fixture, operationId, 7, "PO-SAME", "Same payload"),
                ReceiveAsync(options, fixture, operationId, 7, "PO-SAME", "Same payload"));
            var mismatched = await ReceiveAsync(options, fixture, operationId, 8, "PO-OTHER", "Different payload");

            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var ledgerCount = await verify.InventoryTransactions.CountAsync(item =>
                item.ProductId == fixture.ProductId && item.TransactionType == "IN" && item.Note!.Contains($"OPID={operationId:N};"));

            Assert.Equal(1, parallel.Count(result => result.Failure == WarehouseInventoryOperationFailure.None));
            Assert.Equal(1, parallel.Count(result => result.Failure == WarehouseInventoryOperationFailure.AlreadyApplied));
            Assert.Equal(WarehouseInventoryOperationFailure.IdempotencyConflict, mismatched.Failure);
            Assert.Equal(9, inventory.QuantityInStock);
            Assert.Equal(1, ledgerCount);
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task ConcurrentReceiptsForSameSku_AreSerializedWithoutLostUpdates()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 1);
        try
        {
            var results = await Task.WhenAll(
                ReceiveAsync(options, fixture, Guid.NewGuid(), 4, "PO-A", null),
                ReceiveAsync(options, fixture, Guid.NewGuid(), 6, "PO-B", null));
            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(item => item.ProductId == fixture.ProductId && item.TransactionType == "IN").ToArrayAsync();

            Assert.All(results, result => Assert.Equal(WarehouseInventoryOperationFailure.None, result.Failure));
            Assert.Equal(11, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(2, ledger.Length);
            Assert.Equal(10, ledger.Sum(item => item.Quantity));
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task ConcurrentAdjustmentsForSameSku_PreserveBothDistinctOperations()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 10, reserved: 1);
        var secondActorId = await CreateAdditionalActorAsync(options, fixture.Marker);
        try
        {
            var increase = AdjustAsync(options, fixture, Guid.NewGuid(), "INCREASE", 4, "Kiểm kê Warehouse A");
            var decrease = AdjustAsync(options, fixture with { ActorId = secondActorId }, Guid.NewGuid(), "DECREASE", 2, "Kiểm kê Warehouse B");
            var results = await Task.WhenAll(increase, decrease);

            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(item => item.ProductId == fixture.ProductId && item.TransactionType == "ADJUST").ToArrayAsync();
            Assert.All(results, result => Assert.Equal(WarehouseInventoryOperationFailure.None, result.Failure));
            Assert.Equal(12, inventory.QuantityInStock);
            Assert.Equal(1, inventory.ReservedQuantity);
            Assert.Equal(2, ledger.Length);
            Assert.Equal(new[] { 2, 4 }, ledger.Select(item => item.Quantity).Order().ToArray());
            Assert.Equal(new[] { fixture.ActorId, secondActorId }.Order().ToArray(), ledger.Select(item => item.UserId).Order().ToArray());
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task AdjustmentDecrease_RacesCheckoutWithoutBreakingStockOrReservationInvariants()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 5);
        try
        {
            await using (var setup = new AppDbContext(options))
            {
                var cart = new Cart { UserId = fixture.ActorId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                cart.Items.Add(new CartItem { ProductId = fixture.ProductId, Quantity = 5, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                setup.Carts.Add(cart);
                await setup.SaveChangesAsync();
            }

            CheckoutQuote quote;
            await using (var quoteDb = new AppDbContext(options))
                quote = await new OrderPricingService(quoteDb).GetCurrentQuoteAsync(fixture.ActorId);

            var checkout = CheckoutAsync(options, fixture.ActorId, quote);
            var adjustment = AdjustAsync(options, fixture, Guid.NewGuid(), "DECREASE", 5, "Kiểm kê đồng thời checkout");
            await Task.WhenAll(checkout, adjustment);
            var checkoutResult = await checkout;
            var adjustmentResult = await adjustment;

            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var orders = await verify.Orders.AsNoTracking().Where(item => item.UserId == fixture.ActorId).ToArrayAsync();
            Assert.True(inventory.QuantityInStock >= 0);
            Assert.InRange(inventory.ReservedQuantity, 0, inventory.QuantityInStock);
            if (orders.Length == 1)
            {
                Assert.Equal(5, inventory.QuantityInStock);
                Assert.Equal(5, inventory.ReservedQuantity);
                Assert.Equal(PlaceOrderFailure.None, checkoutResult.Failure);
                Assert.NotEqual(WarehouseInventoryOperationFailure.None, adjustmentResult.Failure);
            }
            else
            {
                Assert.Empty(orders);
                Assert.Equal(0, inventory.QuantityInStock);
                Assert.Equal(0, inventory.ReservedQuantity);
                Assert.Equal(WarehouseInventoryOperationFailure.None, adjustmentResult.Failure);
                Assert.DoesNotContain(await verify.InventoryTransactions.AsNoTracking().Where(item => item.ProductId == fixture.ProductId).ToArrayAsync(),
                    item => item.TransactionType == "RESERVE");
            }
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task AdjustmentIncrease_RacesWarehouseExportWithoutDuplicateOutOrLostStock()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 6, reserved: 2, preparingOrder: true);
        try
        {
            var adjustment = AdjustAsync(options, fixture, Guid.NewGuid(), "INCREASE", 4, "Nhập bù trong lúc xuất");
            var export = ExportAsync(options, fixture.OrderId!.Value, fixture.ActorId);
            await Task.WhenAll(adjustment, export);
            var adjustmentResult = await adjustment;
            var exportResult = await export;

            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(WarehouseInventoryOperationFailure.None, adjustmentResult.Failure);
            Assert.Equal(WarehouseExportFailure.None, exportResult.Failure);
            Assert.Equal(8, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(1, await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT"));
            Assert.Equal(1, await verify.InventoryTransactions.CountAsync(item => item.ProductId == fixture.ProductId && item.TransactionType == "ADJUST"));
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task InventoryQueryIncludesMissingRecords_AndLegacyAdjustmentDirectionIsNotGuessed()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, stock: 3);
        try
        {
            await using (var seed = new AppDbContext(options))
            {
                seed.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, UserId = fixture.ActorId, TransactionType = "ADJUST", Quantity = 1,
                    Note = "Điều chỉnh thủ công cũ không có hướng xác định", CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
                await seed.SaveChangesAsync();
            }

            WarehouseInventoryPage inventoryPage;
            WarehouseInventoryHistoryPage history;
            await using (var queryDb = new AppDbContext(options))
            {
                var service = new WarehouseInventoryManagementService(queryDb, NullLogger<WarehouseInventoryManagementService>.Instance);
                inventoryPage = await service.SearchAsync(new WarehouseInventoryFilter(fixture.Marker));
                history = await service.GetHistoryAsync(new WarehouseInventoryHistoryFilter(
                    fixture.ProductId, "ADJUST", Page: 1, PageSize: 5));
            }

            var inventoryRow = Assert.Single(inventoryPage.Items);
            Assert.True(inventoryRow.HasInventory);
            Assert.Equal(3, inventoryRow.QuantityInStock);
            var historyRow = Assert.Single(history.Items);
            Assert.Equal("Không xác định", historyRow.Direction);
            Assert.Equal($"Phase 5.4 QA {fixture.Marker}", historyRow.ActorName);

            await using (var deleteInventory = new AppDbContext(options))
                await deleteInventory.Inventories.Where(item => item.ProductId == fixture.ProductId).ExecuteDeleteAsync();
            await using var missingDb = new AppDbContext(options);
            var missing = Assert.Single((await new WarehouseInventoryManagementService(
                missingDb, NullLogger<WarehouseInventoryManagementService>.Instance)
                .SearchAsync(new WarehouseInventoryFilter(fixture.Marker, OutOfStockOnly: true))).Items);
            Assert.False(missing.HasInventory);
            Assert.Null(missing.AvailableQuantity);
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    private static DbContextOptions<AppDbContext> CreateOptions() => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!)
        .Options;

    private static async Task<WarehouseInventoryOperationResult> ReceiveAsync(
        DbContextOptions<AppDbContext> options, InventoryFixture fixture, Guid requestId, int quantity, string? reference, string? note)
    {
        await using var db = new AppDbContext(options);
        return await new WarehouseInventoryManagementService(db, NullLogger<WarehouseInventoryManagementService>.Instance)
            .ReceiveAsync(new WarehouseStockReceiptInput(requestId, fixture.ProductId, quantity, reference, note), fixture.ActorId);
    }

    private static async Task<WarehouseInventoryOperationResult> AdjustAsync(
        DbContextOptions<AppDbContext> options, InventoryFixture fixture, Guid requestId, string direction, int quantity, string? reason)
    {
        await using var db = new AppDbContext(options);
        return await new WarehouseInventoryManagementService(db, NullLogger<WarehouseInventoryManagementService>.Instance)
            .AdjustAsync(new WarehouseStockAdjustmentInput(requestId, fixture.ProductId, direction, quantity, reason, null), fixture.ActorId);
    }

    private static async Task<int> CreateAdditionalActorAsync(DbContextOptions<AppDbContext> options, string marker)
    {
        await using var db = new AppDbContext(options);
        var email = $"phase54-other-{marker}@example.invalid";
        var actor = new AppUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = $"Phase 5.4 QA other {marker}", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    private static async Task<PlaceOrderResult> CheckoutAsync(DbContextOptions<AppDbContext> options, int userId, CheckoutQuote quote)
    {
        await using var db = new AppDbContext(options);
        return await new CheckoutService(db, new OrderPricingService(db), NullLogger<CheckoutService>.Instance)
            .PlaceOrderAsync(userId, new PlaceOrderInput(Guid.NewGuid(), "Warehouse QA Customer", "+84901234567",
                "123 Phase 5 QA Street", null, quote.Fingerprint));
    }

    private static async Task<WarehouseExportResult> ExportAsync(DbContextOptions<AppDbContext> options, int orderId, int actorId)
    {
        await using var db = new AppDbContext(options);
        return await new WarehouseFulfillmentService(db, NullLogger<WarehouseFulfillmentService>.Instance)
            .ExportAsync(orderId, actorId);
    }

    private static async Task<InventoryFixture> CreateFixtureAsync(
        DbContextOptions<AppDbContext> options, int stock = 10, int reserved = 0, bool preparingOrder = false)
    {
        var marker = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(options);
        var category = new Category { Name = $"Phase 5.4 QA Category {marker}", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var brand = new Brand { Name = $"Phase 5.4 QA Brand {marker}", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var email = $"phase54-{marker}@example.invalid";
        var user = new AppUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = $"Phase 5.4 QA {marker}", IsActive = true, CreatedAt = now, UpdatedAt = now
        };
        db.Categories.Add(category);
        db.Brands.Add(brand);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var product = new Product
        {
            CategoryId = category.Id, BrandId = brand.Id, Name = $"Phase 5.4 QA Product {marker}",
            Slug = $"phase-54-{marker}", Sku = $"NEXORA-P54-{marker}", Price = 1000m,
            WarrantyMonths = 12, Status = ProductStatuses.Active, CreatedAt = now, UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var inventory = new Inventory { ProductId = product.Id };
        if (stock > 0) inventory.Receive(stock);
        if (reserved > 0) inventory.Reserve(reserved);
        db.Inventories.Add(inventory);
        int? orderId = null;
        if (preparingOrder)
        {
            var order = new Order
            {
                UserId = user.Id, CustomerName = user.FullName, Phone = "+84901234567", ShippingAddress = "123 Phase 5 QA Street",
                OrderDate = now, TotalAmount = checked(product.Price * reserved), Status = OrderStatuses.Preparing,
                ConfirmedBy = user.Id, ConfirmedAt = now, Note = $"Phase 5.4 QA {marker}"
            };
            order.Details.Add(new OrderDetail
            {
                ProductId = product.Id, ProductName = product.Name, Quantity = reserved,
                UnitPrice = product.Price, DiscountAmount = 0, TotalPrice = checked(product.Price * reserved)
            });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id, UserId = user.Id, OrderId = order.Id, TransactionType = "RESERVE",
                Quantity = reserved, Note = "Phase 5.4 QA reservation", CreatedAt = now
            });
        }
        await db.SaveChangesAsync();
        return new InventoryFixture(marker, product.Id, user.Id, orderId);
    }

    private static async Task CleanupAsync(DbContextOptions<AppDbContext> options, string marker)
    {
        await using var db = new AppDbContext(options);
        var productIds = await db.Products.Where(product => product.Sku.Contains(marker)).Select(product => product.Id).ToArrayAsync();
        var categoryIds = await db.Categories.Where(category => category.Name.Contains(marker)).Select(category => category.Id).ToArrayAsync();
        var brandIds = await db.Brands.Where(brand => brand.Name.Contains(marker)).Select(brand => brand.Id).ToArrayAsync();
        var users = await db.Users.Where(user => user.Email != null && user.Email.Contains(marker)).ToListAsync();
        var userIds = users.Select(user => user.Id).ToArray();
        var orderIds = await db.Orders.Where(order => order.Note != null && order.Note.Contains(marker) || userIds.Contains(order.UserId))
            .Select(order => order.Id).ToArrayAsync();
        if (productIds.Length != 0 || orderIds.Length != 0)
        {
            await db.InventoryTransactions.Where(item => productIds.Contains(item.ProductId) || item.OrderId != null && orderIds.Contains(item.OrderId.Value)).ExecuteDeleteAsync();
            await db.OrderDetails.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await db.Orders.Where(item => orderIds.Contains(item.Id)).ExecuteDeleteAsync();
            await db.CartItems.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
            await db.Carts.Where(item => userIds.Contains(item.UserId)).ExecuteDeleteAsync();
            await db.Inventories.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
            await db.Products.Where(item => productIds.Contains(item.Id)).ExecuteDeleteAsync();
        }
        await db.Categories.Where(item => categoryIds.Contains(item.Id)).ExecuteDeleteAsync();
        await db.Brands.Where(item => brandIds.Contains(item.Id)).ExecuteDeleteAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    private sealed record InventoryFixture(string Marker, int ProductId, int ActorId, int? OrderId);

    private sealed class FailInventoryLedgerInsertInterceptor : DbCommandInterceptor
    {
        private static bool IsLedgerInsert(DbCommand command) =>
            command.CommandText.Contains("INSERT INTO [inventory_transactions]", StringComparison.OrdinalIgnoreCase);

        private static void CorruptLedgerTarget(DbCommand command)
        {
            command.CommandText = command.CommandText.Replace(
                "[inventory_transactions]", "[missing_inventory_transactions_qa]", StringComparison.OrdinalIgnoreCase);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (IsLedgerInsert(command)) CorruptLedgerTarget(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsLedgerInsert(command)) CorruptLedgerTarget(command);
            return ValueTask.FromResult(result);
        }
    }
}
