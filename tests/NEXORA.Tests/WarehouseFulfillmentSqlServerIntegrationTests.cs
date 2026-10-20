using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class WarehouseFulfillmentSqlServerIntegrationTests
{
    private readonly ITestOutputHelper output;

    public WarehouseFulfillmentSqlServerIntegrationTests(ITestOutputHelper output) => this.output = output;
    [SqlServerFact]
    public async Task Export_DecrementsStockAndReservationOnce_AndWritesOneOutLedger()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options);
        try
        {
            var first = await ExportAsync(options, fixture.OrderId, fixture.UserId);
            var replay = await ExportAsync(options, fixture.OrderId, fixture.UserId);
            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT").ToArrayAsync();

            Assert.Equal(WarehouseExportFailure.None, first.Failure);
            Assert.Equal(WarehouseExportFailure.AlreadyExported, replay.Failure);
            Assert.Equal(OrderStatuses.Exported, order.Status);
            Assert.Equal(8, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            var entry = Assert.Single(ledger);
            Assert.Equal(fixture.ProductId, entry.ProductId);
            Assert.Equal(fixture.UserId, entry.UserId);
            Assert.Equal(2, entry.Quantity);
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task CancellationPendingBlocksExport_RejectionAllowsExport_ApprovalReleasesAndBlocksExport()
    {
        var options = CreateOptions();
        var rejectedFixture = await CreateFixtureAsync(options);
        var approvedFixture = await CreateFixtureAsync(options);
        try
        {
            await using (var customerDb = new AppDbContext(options))
            {
                Assert.Equal(CustomerCancellationFailure.None,
                    (await new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(rejectedFixture.UserId, rejectedFixture.OrderId, Guid.NewGuid(), "QA pending request")).Failure);
            }
            Assert.Equal(WarehouseExportFailure.CancellationPending,
                (await ExportAsync(options, rejectedFixture.OrderId, rejectedFixture.UserId)).Failure);
            await using (var reviewDb = new AppDbContext(options))
            {
                Assert.Equal(CancellationReviewFailure.None,
                    (await new OrderCancellationReviewService(reviewDb, NullLogger<OrderCancellationReviewService>.Instance)
                        .ReviewAsync(rejectedFixture.OrderId, rejectedFixture.UserId, false, "QA rejection")).Failure);
            }
            Assert.Equal(WarehouseExportFailure.None,
                (await ExportAsync(options, rejectedFixture.OrderId, rejectedFixture.UserId)).Failure);

            await using (var customerDb = new AppDbContext(options))
            {
                Assert.Equal(CustomerCancellationFailure.None,
                    (await new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(approvedFixture.UserId, approvedFixture.OrderId, Guid.NewGuid(), "QA approval request")).Failure);
            }
            await using (var reviewDb = new AppDbContext(options))
            {
                Assert.Equal(CancellationReviewFailure.None,
                    (await new OrderCancellationReviewService(reviewDb, NullLogger<OrderCancellationReviewService>.Instance)
                        .ReviewAsync(approvedFixture.OrderId, approvedFixture.UserId, true, "QA approval")).Failure);
            }
            Assert.Equal(WarehouseExportFailure.InvalidState,
                (await ExportAsync(options, approvedFixture.OrderId, approvedFixture.UserId)).Failure);
            await using var verify = new AppDbContext(options);
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item => item.OrderId == approvedFixture.OrderId && item.TransactionType == "OUT"));
            var approvedInventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == approvedFixture.ProductId);
            Assert.Equal(10, approvedInventory.QuantityInStock);
            Assert.Equal(0, approvedInventory.ReservedQuantity);
        }
        finally
        {
            await CleanupAsync(options, rejectedFixture.Marker);
            await CleanupAsync(options, approvedFixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task ExportRejectsMissingInventoryAndRollsBackWhenOutLedgerInsertFails()
    {
        var options = CreateOptions();
        var missing = await CreateFixtureAsync(options);
        var failing = await CreateFixtureAsync(options);
        try
        {
            await using (var deleteInventory = new AppDbContext(options))
                await deleteInventory.Inventories.Where(item => item.ProductId == missing.ProductId).ExecuteDeleteAsync();
            Assert.Equal(WarehouseExportFailure.InventoryMissing,
                (await ExportAsync(options, missing.OrderId, missing.UserId)).Failure);
            await using (var verifyMissing = new AppDbContext(options))
                Assert.Equal(OrderStatuses.Preparing, await verifyMissing.Orders.Where(item => item.Id == missing.OrderId).Select(item => item.Status).SingleAsync());

            var failingOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!)
                .AddInterceptors(new FailOutLedgerInterceptor())
                .Options;
            await using (var db = new AppDbContext(failingOptions))
            {
                var result = await new WarehouseFulfillmentService(db, NullLogger<WarehouseFulfillmentService>.Instance)
                    .ExportAsync(failing.OrderId, failing.UserId);
                Assert.Equal(WarehouseExportFailure.PersistenceFailure, result.Failure);
            }
            await using var verify = new AppDbContext(options);
            Assert.Equal(OrderStatuses.Preparing, await verify.Orders.Where(item => item.Id == failing.OrderId).Select(item => item.Status).SingleAsync());
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == failing.ProductId);
            Assert.Equal(10, inventory.QuantityInStock);
            Assert.Equal(2, inventory.ReservedQuantity);
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item => item.OrderId == failing.OrderId && item.TransactionType == "OUT"));
        }
        finally
        {
            await CleanupAsync(options, missing.Marker);
            await CleanupAsync(options, failing.Marker);
        }
    }

    [SqlServerFact]
    public async Task ExportRejectsInsufficientOrderReservationAndEmptyOrderDetails()
    {
        var options = CreateOptions();
        var insufficient = await CreateFixtureAsync(options);
        var empty = await CreateFixtureAsync(options);
        try
        {
            await using (var db = new AppDbContext(options))
            {
                await db.Inventories.Where(item => item.ProductId == insufficient.ProductId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ReservedQuantity, 1));
            }
            Assert.Equal(WarehouseExportFailure.InsufficientReservation,
                (await ExportAsync(options, insufficient.OrderId, insufficient.UserId)).Failure);
            await using (var db = new AppDbContext(options))
            {
                await db.OrderDetails.Where(item => item.OrderId == empty.OrderId).ExecuteDeleteAsync();
            }
            Assert.Equal(WarehouseExportFailure.InvalidOrderDetails,
                (await ExportAsync(options, empty.OrderId, empty.UserId)).Failure);

            await using var verify = new AppDbContext(options);
            foreach (var fixture in new[] { insufficient, empty })
            {
                Assert.Equal(OrderStatuses.Preparing,
                    await verify.Orders.Where(item => item.Id == fixture.OrderId).Select(item => item.Status).SingleAsync());
                Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT"));
            }
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == insufficient.ProductId);
            Assert.Equal(10, inventory.QuantityInStock);
            Assert.Equal(1, inventory.ReservedQuantity);
        }
        finally
        {
            await CleanupAsync(options, insufficient.Marker);
            await CleanupAsync(options, empty.Marker);
        }
    }

    [SqlServerFact]
    public async Task TwoConcurrentExportsForOneOrder_ProduceOneOutAndOneStockChange()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options);
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WarehouseExportResult> Start()
            {
                await gate.Task;
                return await ExportAsync(options, fixture.OrderId, fixture.UserId);
            }
            var first = Start();
            var second = Start();
            gate.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, item => item.Failure == WarehouseExportFailure.None);
            Assert.All(results, item => Assert.Contains(item.Failure, new[]
            {
                WarehouseExportFailure.None,
                WarehouseExportFailure.AlreadyExported,
                WarehouseExportFailure.ConcurrencyConflict
            }));
            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(8, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(1, await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT"));
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task CustomerCancellationRequestRacingExport_EndsInOneConsistentOutcome()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options);
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<CustomerCancellationResult> Request()
            {
                await gate.Task;
                await using var db = new AppDbContext(options);
                return await new CustomerOrderService(db, NullLogger<CustomerOrderService>.Instance)
                    .RequestCancellationAsync(fixture.UserId, fixture.OrderId, Guid.NewGuid(), "Concurrent QA cancellation request");
            }
            async Task<WarehouseExportResult> Export()
            {
                await gate.Task;
                return await ExportAsync(options, fixture.OrderId, fixture.UserId);
            }
            var requestTask = Request();
            var exportTask = Export();
            gate.SetResult();
            var request = await requestTask;
            var export = await exportTask;
            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            var stock = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            var outCount = await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT");

            if (order.Status == OrderStatuses.Exported)
            {
                Assert.Equal(CustomerCancellationFailure.InvalidState, request.Failure);
                Assert.Contains(export.Failure, new[] { WarehouseExportFailure.None, WarehouseExportFailure.AlreadyExported });
                Assert.Null(order.CancelRequestStatus);
                Assert.Equal(8, stock.QuantityInStock);
                Assert.Equal(0, stock.ReservedQuantity);
                Assert.Equal(1, outCount);
            }
            else
            {
                Assert.Equal(OrderStatuses.Preparing, order.Status);
                Assert.Equal(OrderCancellationRequestStatuses.Pending, order.CancelRequestStatus);
                Assert.Equal(CustomerCancellationFailure.None, request.Failure);
                Assert.Equal(WarehouseExportFailure.CancellationPending, export.Failure);
                Assert.Equal(10, stock.QuantityInStock);
                Assert.Equal(2, stock.ReservedQuantity);
                Assert.Equal(0, outCount);
            }
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task CancellationApprovalRacingExport_CannotReleaseAndExportSameReservation()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options);
        try
        {
            await using (var requestDb = new AppDbContext(options))
                Assert.Equal(CustomerCancellationFailure.None,
                    (await new CustomerOrderService(requestDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(fixture.UserId, fixture.OrderId, Guid.NewGuid(), "Approval/export race QA")).Failure);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<CancellationReviewResult> Approve()
            {
                await gate.Task;
                await using var db = new AppDbContext(options);
                return await new OrderCancellationReviewService(db, NullLogger<OrderCancellationReviewService>.Instance)
                    .ReviewAsync(fixture.OrderId, fixture.UserId, true, "Concurrent QA approval");
            }
            async Task<WarehouseExportResult> Export()
            {
                await gate.Task;
                return await ExportAsync(options, fixture.OrderId, fixture.UserId);
            }
            var approvalTask = Approve();
            var exportTask = Export();
            gate.SetResult();
            var approval = await approvalTask;
            var export = await exportTask;
            Assert.Equal(CancellationReviewFailure.None, approval.Failure);
            Assert.Contains(export.Failure, new[] { WarehouseExportFailure.CancellationPending, WarehouseExportFailure.InvalidState });
            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(OrderStatuses.Cancelled, order.Status);
            Assert.Equal(OrderCancellationRequestStatuses.Approved, order.CancelRequestStatus);
            Assert.Equal(10, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(1, await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item => item.OrderId == fixture.OrderId && item.TransactionType == "OUT"));
        }
        finally { await CleanupAsync(options, fixture.Marker); }
    }

    [SqlServerFact]
    public async Task ConcurrentExportsOfDifferentOrdersSharingProduct_UseStableInventoryLocks()
    {
        var options = CreateOptions();
        var firstFixture = await CreateFixtureAsync(options);
        var secondOrderId = await AddSecondPreparingOrderAsync(options, firstFixture);
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<WarehouseExportResult> Start(int orderId)
            {
                await gate.Task;
                await using var db = new AppDbContext(options);
                return await new WarehouseFulfillmentService(db, new TestOutputLogger<WarehouseFulfillmentService>(output))
                    .ExportAsync(orderId, firstFixture.UserId);
            }
            var first = Start(firstFixture.OrderId);
            var second = Start(secondOrderId);
            gate.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.All(results, item => Assert.Equal(WarehouseExportFailure.None, item.Failure));
            await using var verify = new AppDbContext(options);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == firstFixture.ProductId);
            Assert.Equal(6, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(2, await verify.InventoryTransactions.CountAsync(item =>
                (item.OrderId == firstFixture.OrderId || item.OrderId == secondOrderId) && item.TransactionType == "OUT"));
        }
        finally { await CleanupAsync(options, firstFixture.Marker); }
    }

    private static DbContextOptions<AppDbContext> CreateOptions() => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!).Options;

    private static async Task<WarehouseFixture> CreateFixtureAsync(DbContextOptions<AppDbContext> options)
    {
        var marker = Guid.NewGuid().ToString("N");
        var email = $"phase53-warehouse-{marker}@example.invalid";
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(options);
        var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
        var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
        var user = new AppUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = $"Phase 5.3 Warehouse QA {marker}", IsActive = true, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        var product = new Product
        {
            CategoryId = categoryId, BrandId = brandId,
            Name = $"Phase 5.3 QA product {marker}", Slug = $"phase-53-{marker}", Sku = $"NEXORA-P53-{marker}",
            Price = 1000m, WarrantyMonths = 12, Status = ProductStatuses.Active, CreatedAt = now, UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var inventory = new Inventory { ProductId = product.Id };
        inventory.Receive(10);
        inventory.Reserve(2);
        db.Inventories.Add(inventory);
        db.Orders.Add(CreateOrder(user.Id, product.Id, product.Name, marker, now));
        await db.SaveChangesAsync();
        var orderId = await db.Orders.Where(item => item.CustomerName.Contains(marker)).Select(item => item.Id).SingleAsync();
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = product.Id, UserId = user.Id, OrderId = orderId, TransactionType = "RESERVE", Quantity = 2,
            Note = "Phase 5.3 isolated SQL QA fixture", CreatedAt = now
        });
        await db.SaveChangesAsync();
        return new WarehouseFixture(marker, user.Id, product.Id, orderId);
    }

    private static Order CreateOrder(int userId, int productId, string productName, string marker, DateTime now)
    {
        var order = new Order
        {
            UserId = userId, CustomerName = $"Phase 5.3 QA order {marker}", Phone = "0000000000",
            ShippingAddress = "Isolated Phase 5.3 QA address", OrderDate = now, TotalAmount = 2000m,
            Status = OrderStatuses.Preparing, ConfirmedBy = userId, ConfirmedAt = now,
            Note = "Automated QA fixture"
        };
        order.Details.Add(new OrderDetail
        {
            ProductId = productId, ProductName = productName, Quantity = 2,
            UnitPrice = 1000m, DiscountAmount = 0m, TotalPrice = 2000m
        });
        return order;
    }

    private static async Task<int> AddSecondPreparingOrderAsync(DbContextOptions<AppDbContext> options, WarehouseFixture fixture)
    {
        await using var db = new AppDbContext(options);
        var product = await db.Products.AsNoTracking().SingleAsync(item => item.Id == fixture.ProductId);
        var inventory = await db.Inventories.SingleAsync(item => item.ProductId == fixture.ProductId);
        inventory.Reserve(2);
        var order = CreateOrder(fixture.UserId, product.Id, product.Name, fixture.Marker + "-second", DateTime.UtcNow);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = fixture.ProductId, UserId = fixture.UserId, OrderId = order.Id,
            TransactionType = "RESERVE", Quantity = 2, Note = "Phase 5.3 shared product QA reservation", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<WarehouseExportResult> ExportAsync(DbContextOptions<AppDbContext> options, int orderId, int actorId)
    {
        await using var db = new AppDbContext(options);
        return await new WarehouseFulfillmentService(db, NullLogger<WarehouseFulfillmentService>.Instance).ExportAsync(orderId, actorId);
    }

    private static async Task CleanupAsync(DbContextOptions<AppDbContext> options, string marker)
    {
        await using var db = new AppDbContext(options);
        var productIds = await db.Products.Where(item => item.Sku.Contains(marker)).Select(item => item.Id).ToArrayAsync();
        var orderIds = await db.Orders.Where(item => item.CustomerName.Contains(marker)).Select(item => item.Id).ToArrayAsync();
        if (productIds.Length != 0 || orderIds.Length != 0)
        {
            await db.InventoryTransactions.Where(item =>
                (item.OrderId != null && orderIds.Contains(item.OrderId.Value)) || productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
            await db.OrderDetails.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await db.Orders.Where(item => orderIds.Contains(item.Id)).ExecuteDeleteAsync();
            await db.Inventories.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
            await db.Products.Where(item => productIds.Contains(item.Id)).ExecuteDeleteAsync();
        }
        var users = await db.Users.Where(item => item.Email != null && item.Email.Contains(marker)).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    private sealed record WarehouseFixture(string Marker, int UserId, int ProductId, int OrderId);

    private sealed class TestOutputLogger<T>(ITestOutputHelper output) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            output.WriteLine(formatter(state, exception));
            if (exception is not null) output.WriteLine(exception.ToString());
        }
    }

    private sealed class FailOutLedgerInterceptor : DbCommandInterceptor
    {
        private static bool IsOutInsert(System.Data.Common.DbCommand command) =>
            command.CommandText.Contains("INSERT INTO [inventory_transactions]", StringComparison.OrdinalIgnoreCase) &&
            command.Parameters.Cast<System.Data.Common.DbParameter>().Any(parameter =>
                string.Equals(Convert.ToString(parameter.Value), "OUT", StringComparison.OrdinalIgnoreCase));

        public override InterceptionResult<int> NonQueryExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
            IsOutInsert(command) ? throw new InvalidOperationException("QA forced OUT ledger insert failure.") : result;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            IsOutInsert(command)
                ? ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("QA forced OUT ledger insert failure."))
                : ValueTask.FromResult(result);

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result) =>
            IsOutInsert(command) ? throw new InvalidOperationException("QA forced OUT ledger insert failure.") : result;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default) =>
            IsOutInsert(command)
                ? ValueTask.FromException<InterceptionResult<System.Data.Common.DbDataReader>>(new InvalidOperationException("QA forced OUT ledger insert failure."))
                : ValueTask.FromResult(result);
    }
}
