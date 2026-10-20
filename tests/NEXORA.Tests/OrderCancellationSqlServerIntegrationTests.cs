using Microsoft.Data.SqlClient;
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
public sealed class OrderCancellationSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task CustomerRequest_IsOwnerScopedIdempotentAndDoesNotTouchOrderInventoryOrSnapshots()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Confirmed);
        try
        {
            await using var db = new AppDbContext(options);
            var service = new CustomerOrderService(db, NullLogger<CustomerOrderService>.Instance);
            var before = await ReadStateAsync(db, fixture.OrderId, fixture.ProductId);
            var requestId = Guid.NewGuid();

            Assert.Equal(CustomerCancellationFailure.NotFound,
                (await service.RequestCancellationAsync(fixture.OwnerUserId + 500_000, fixture.OrderId, requestId, "Not owner")).Failure);
            var created = await service.RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, requestId, "  Customer changed plans  ");
            var replay = await service.RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, requestId, "Different retry text");
            var secondRequest = await service.RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, Guid.NewGuid(), "Second request");

            Assert.Equal(CustomerCancellationFailure.None, created.Failure);
            Assert.Equal(OrderCancellationRequestStatuses.Pending, created.RequestStatus);
            Assert.Equal(OrderStatuses.Confirmed, created.OrderStatus);
            Assert.Equal(CustomerCancellationFailure.AlreadyApplied, replay.Failure);
            Assert.Equal(CustomerCancellationFailure.AlreadyRequested, secondRequest.Failure);

            var after = await ReadStateAsync(db, fixture.OrderId, fixture.ProductId);
            Assert.Equal(before.OrderStatus, after.OrderStatus);
            Assert.Equal(before.Stock, after.Stock);
            Assert.Equal(before.Reserved, after.Reserved);
            Assert.Equal(before.UnitPrice, after.UnitPrice);
            Assert.Equal(before.Discount, after.Discount);
            Assert.Equal(OrderCancellationRequestStatuses.Pending, after.RequestStatus);
            Assert.Equal(requestId, after.RequestId);
            Assert.Equal("Customer changed plans", after.RequestReason);
            Assert.Equal(0, after.ReleaseCount);
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task RequestEligibility_AllowsConfirmedPreparingAndRejectsPendingOrAfterExport()
    {
        var options = CreateOptions();
        var markers = new List<string>();
        try
        {
            foreach (var status in new[]
                     {
                         OrderStatuses.Pending,
                         OrderStatuses.Confirmed,
                         OrderStatuses.Preparing,
                         OrderStatuses.Exported,
                         OrderStatuses.Shipping,
                         OrderStatuses.Completed
                     })
            {
                var fixture = await CreateFixtureAsync(options, status);
                markers.Add(fixture.Marker);
                await using var db = new AppDbContext(options);
                var service = new CustomerOrderService(db, NullLogger<CustomerOrderService>.Instance);
                var result = await service.RequestCancellationAsync(
                    fixture.OwnerUserId, fixture.OrderId, Guid.NewGuid(), "QA eligibility fixture");

                var expected = status is OrderStatuses.Confirmed or OrderStatuses.Preparing
                    ? CustomerCancellationFailure.None
                    : CustomerCancellationFailure.InvalidState;
                Assert.Equal(expected, result.Failure);
                Assert.Equal(status, await db.Orders.Where(order => order.Id == fixture.OrderId)
                    .Select(order => order.Status).SingleAsync());
                Assert.Equal(status is OrderStatuses.Confirmed or OrderStatuses.Preparing
                        ? OrderCancellationRequestStatuses.Pending
                        : null,
                    await db.Orders.Where(order => order.Id == fixture.OrderId)
                        .Select(order => order.CancelRequestStatus).SingleAsync());
            }
        }
        finally
        {
            foreach (var marker in markers) await CleanupFixtureAsync(options, marker);
        }
    }

    [SqlServerFact]
    public async Task ConcurrentSameRequestIsIdempotentAndRequestRacingSalesHandoffRemainsEligible()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Confirmed);
        try
        {
            var requestId = Guid.NewGuid();
            async Task<CustomerCancellationResult> SubmitAsync()
            {
                await using var context = new AppDbContext(options);
                return await new CustomerOrderService(context, NullLogger<CustomerOrderService>.Instance)
                    .RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, requestId, "Concurrent same-key request");
            }

            var submissions = await Task.WhenAll(SubmitAsync(), SubmitAsync());
            Assert.Single(submissions, item => item.Failure == CustomerCancellationFailure.None);
            Assert.Single(submissions, item => item.Failure == CustomerCancellationFailure.AlreadyApplied);
            await using var verify = new AppDbContext(options);
            Assert.Equal(1, await verify.Orders.CountAsync(order => order.Id == fixture.OrderId &&
                order.CancelRequestStatus == OrderCancellationRequestStatuses.Pending));
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item =>
                item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task ApprovalIsAtomicReleasesExactlyOnceAndOnlyOneConcurrentReviewerWins()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Preparing);
        try
        {
            var requestId = Guid.NewGuid();
            await using (var customerDb = new AppDbContext(options))
            {
                var customer = new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance);
                Assert.Equal(CustomerCancellationFailure.None,
                    (await customer.RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, requestId, "Approved QA cancellation")).Failure);
            }

            async Task<CancellationReviewResult> ApproveAsync(int reviewerId)
            {
                await using var reviewerDb = new AppDbContext(options);
                var service = new OrderCancellationReviewService(reviewerDb, NullLogger<OrderCancellationReviewService>.Instance);
                return await service.ReviewAsync(fixture.OrderId, reviewerId, approve: true, "Reviewed in QA");
            }

            var decisions = await Task.WhenAll(
                ApproveAsync(fixture.OwnerUserId),
                ApproveAsync(fixture.ReviewerUserId));
            Assert.Single(decisions, item => item.Failure == CancellationReviewFailure.None);
            Assert.Single(decisions, item => item.Failure is CancellationReviewFailure.AlreadyReviewed or CancellationReviewFailure.ConcurrencyConflict);

            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            Assert.Equal(OrderStatuses.Cancelled, order.Status);
            Assert.Equal(OrderCancellationRequestStatuses.Approved, order.CancelRequestStatus);
            Assert.Equal("Approved QA cancellation", order.CancelReason);
            Assert.NotNull(order.CancelledAt);
            Assert.NotNull(order.CancelReviewedBy);
            Assert.NotNull(order.CancelReviewedAt);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(5, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);
            Assert.Equal(1, await verify.InventoryTransactions.CountAsync(item =>
                item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));

            await using var retryDb = new AppDbContext(options);
            var retry = await new OrderCancellationReviewService(retryDb, NullLogger<OrderCancellationReviewService>.Instance)
                .ReviewAsync(fixture.OrderId, fixture.ReviewerUserId, approve: true, "Repeated review");
            Assert.Equal(CancellationReviewFailure.AlreadyReviewed, retry.Failure);
            Assert.Equal(1, await retryDb.InventoryTransactions.CountAsync(item =>
                item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task RejectionKeepsOrderAndReservationAndOrderMayLaterComplete()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Preparing);
        try
        {
            await using (var customerDb = new AppDbContext(options))
            {
                var customer = new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance);
                Assert.Equal(CustomerCancellationFailure.None,
                    (await customer.RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, Guid.NewGuid(), "Rejected QA cancellation")).Failure);
            }

            await using (var reviewerDb = new AppDbContext(options))
            {
                var reviewer = new OrderCancellationReviewService(reviewerDb, NullLogger<OrderCancellationReviewService>.Instance);
                Assert.Equal(CancellationReviewFailure.None,
                    (await reviewer.ReviewAsync(fixture.OrderId, fixture.ReviewerUserId, approve: false, "Reasoned QA rejection")).Failure);
                Assert.Equal(CustomerCancellationFailure.AlreadyRequested,
                    (await new CustomerOrderService(reviewerDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, Guid.NewGuid(), "Try again")).Failure);
            }

            await using (var transitionDb = new AppDbContext(options))
            {
                await transitionDb.Orders.Where(order => order.Id == fixture.OrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Status, OrderStatuses.Exported));
                var sales = new SalesOrderManagementService(transitionDb, NullLogger<SalesOrderManagementService>.Instance);
                Assert.Equal(OrderTransitionFailure.None,
                    (await sales.TransitionAsync(fixture.OrderId, fixture.ReviewerUserId, OrderWorkflowAction.MarkShipping)).Failure);
                Assert.Equal(OrderTransitionFailure.None,
                    (await sales.TransitionAsync(fixture.OrderId, fixture.ReviewerUserId, OrderWorkflowAction.Complete)).Failure);
            }

            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            Assert.Equal(OrderStatuses.Completed, order.Status);
            Assert.Equal(OrderCancellationRequestStatuses.Rejected, order.CancelRequestStatus);
            Assert.Equal("Reasoned QA rejection", order.CancelDecisionNote);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(5, inventory.QuantityInStock);
            Assert.Equal(1, inventory.ReservedQuantity);
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item =>
                item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task LedgerWriteFailureRollsBackApprovalAndInventoryRelease()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Confirmed);
        try
        {
            await using (var customerDb = new AppDbContext(options))
            {
                Assert.Equal(CustomerCancellationFailure.None,
                    (await new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, Guid.NewGuid(), "QA rollback request")).Failure);
            }

            var failingOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION"))
                .AddInterceptors(new FailInventoryTransactionInsertInterceptor())
                .Options;
            await using (var failingDb = new AppDbContext(failingOptions))
            {
                var result = await new OrderCancellationReviewService(failingDb, NullLogger<OrderCancellationReviewService>.Instance)
                    .ReviewAsync(fixture.OrderId, fixture.ReviewerUserId, approve: true, null);
                Assert.Equal(CancellationReviewFailure.PersistenceFailure, result.Failure);
            }

            await using var verify = new AppDbContext(options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(item => item.Id == fixture.OrderId);
            Assert.Equal(OrderStatuses.Confirmed, order.Status);
            Assert.Equal(OrderCancellationRequestStatuses.Pending, order.CancelRequestStatus);
            Assert.Null(order.CancelReviewedBy);
            Assert.Null(order.CancelledAt);
            var inventory = await verify.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == fixture.ProductId);
            Assert.Equal(5, inventory.QuantityInStock);
            Assert.Equal(1, inventory.ReservedQuantity);
            Assert.Equal(0, await verify.InventoryTransactions.CountAsync(item =>
                item.OrderId == fixture.OrderId && item.TransactionType == "RELEASE"));
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
        }
    }

    [SqlServerFact]
    public async Task MigrationConstraintsAndIndexesExistAndRejectInvalidRows()
    {
        var options = CreateOptions();
        var fixture = await CreateFixtureAsync(options, OrderStatuses.Confirmed);
        var duplicateFixture = await CreateFixtureAsync(options, OrderStatuses.Preparing);
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
                    WHERE t.name='orders' AND c.is_nullable=1 AND (
                        (c.name='cancel_request_status' AND ty.name='nvarchar' AND c.max_length=40) OR
                        (c.name='cancel_request_reason' AND ty.name='nvarchar' AND c.max_length=2000) OR
                        (c.name='cancel_request_id' AND ty.name='uniqueidentifier') OR
                        (c.name='cancel_requested_at' AND ty.name='datetime2') OR
                        (c.name='cancel_reviewed_by' AND ty.name='int') OR
                        (c.name='cancel_reviewed_at' AND ty.name='datetime2') OR
                        (c.name='cancel_decision_note' AND ty.name='nvarchar' AND c.max_length=2000));
                    SELECT COUNT(*) FROM sys.check_constraints WHERE name='ck_orders_cancel_request_consistent';
                    SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_orders_users_cancel_reviewed_by' AND delete_referential_action_desc='NO_ACTION';
                    SELECT COUNT(*) FROM sys.indexes WHERE name='ux_orders_cancel_request_id' AND is_unique=1 AND has_filter=1 AND filter_definition='([cancel_request_id] IS NOT NULL)';
                    SELECT COUNT(*) FROM sys.indexes WHERE name='ix_orders_cancel_pending_review' AND has_filter=1 AND CHARINDEX('PENDING', filter_definition)>0;
                    SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId='20261019174632_AddOrderCancellationRequestFields';
                    SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                var values = new List<int>();
                do
                {
                    if (await reader.ReadAsync()) values.Add(reader.GetInt32(0));
                } while (await reader.NextResultAsync());
                Assert.Equal(new[] { 7, 1, 1, 1, 1, 1, 27 }, values);
            }

            Guid requestId;
            await using (var customerDb = new AppDbContext(options))
            {
                requestId = Guid.NewGuid();
                Assert.Equal(CustomerCancellationFailure.None,
                    (await new CustomerOrderService(customerDb, NullLogger<CustomerOrderService>.Instance)
                        .RequestCancellationAsync(fixture.OwnerUserId, fixture.OrderId, requestId, "constraint fixture request")).Failure);
            }

            var checkFailure = await Record.ExceptionAsync(async () =>
            {
                await using var db = new AppDbContext(options);
                await db.Orders.Where(order => order.Id == fixture.OrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.CancelRequestReason, "   "));
            });
            Assert.Equal(547, FindSqlException(checkFailure)?.Number);

            var foreignKeyFailure = await Record.ExceptionAsync(async () =>
            {
                await using var db = new AppDbContext(options);
                await db.Orders.Where(order => order.Id == fixture.OrderId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.CancelRequestStatus, OrderCancellationRequestStatuses.Rejected)
                        .SetProperty(order => order.CancelReviewedBy, (int?)int.MaxValue)
                        .SetProperty(order => order.CancelReviewedAt, (DateTime?)DateTime.UtcNow)
                        .SetProperty(order => order.CancelDecisionNote, "FK validation fixture"));
            });
            Assert.Equal(547, FindSqlException(foreignKeyFailure)?.Number);

            await using (var duplicateDb = new AppDbContext(options))
            {
                var duplicate = await new CustomerOrderService(duplicateDb, NullLogger<CustomerOrderService>.Instance)
                    .RequestCancellationAsync(duplicateFixture.OwnerUserId, duplicateFixture.OrderId, requestId, "duplicate ID fixture");
                Assert.Equal(CustomerCancellationFailure.DuplicateRequestId, duplicate.Failure);
            }

            await using var verify = new AppDbContext(options);
            Assert.Equal(OrderCancellationRequestStatuses.Pending,
                await verify.Orders.Where(order => order.Id == fixture.OrderId).Select(order => order.CancelRequestStatus).SingleAsync());
            Assert.Null(await verify.Orders.Where(order => order.Id == duplicateFixture.OrderId)
                .Select(order => order.CancelRequestStatus).SingleAsync());
        }
        finally
        {
            await CleanupFixtureAsync(options, fixture.Marker);
            await CleanupFixtureAsync(options, duplicateFixture.Marker);
        }
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is SqlException sqlException) return sqlException;
            exception = exception.InnerException;
        }
        return null;
    }

    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!)
            .Options;

    private static async Task<Fixture> CreateFixtureAsync(DbContextOptions<AppDbContext> options, string orderStatus)
    {
        var marker = Guid.NewGuid().ToString("N");
        var email = $"phase52-cancel-{marker}@example.invalid";
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(options);
        var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive)
            .Select(item => item.Id).FirstAsync();
        var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive)
            .Select(item => item.Id).FirstAsync();
        var owner = CreateUser(email, $"Phase 5.2 QA owner {marker}");
        var reviewer = CreateUser($"phase52-review-{marker}@example.invalid", $"Phase 5.2 QA reviewer {marker}");
        db.Users.AddRange(owner, reviewer);
        var product = new Product
        {
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Phase 5.2 cancellation QA product {marker}",
            Slug = $"phase-52-cancel-{marker}",
            Sku = $"NEXORA-P52-CANCEL-{marker}",
            Price = 1_000m,
            WarrantyMonths = 12,
            Status = ProductStatuses.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var inventory = new Inventory { ProductId = product.Id };
        inventory.Receive(5);
        inventory.Reserve(1);
        db.Inventories.Add(inventory);
        var order = new Order
        {
            UserId = owner.Id,
            CustomerName = $"Phase 5.2 cancellation QA order {marker}",
            Phone = "0000000000",
            ShippingAddress = "QA fixture only",
            OrderDate = now,
            TotalAmount = 1_000m,
            Status = orderStatus,
            ConfirmedBy = orderStatus == OrderStatuses.Pending ? null : owner.Id,
            ConfirmedAt = orderStatus == OrderStatuses.Pending ? null : now,
            Note = "QA cancellation fixture only"
        };
        order.Details.Add(new OrderDetail
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = 1_000m,
            DiscountAmount = 0m,
            TotalPrice = 1_000m
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = product.Id,
            UserId = owner.Id,
            OrderId = order.Id,
            TransactionType = "RESERVE",
            Quantity = 1,
            Note = "QA fixture reservation",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        return new Fixture(marker, owner.Id, reviewer.Id, product.Id, order.Id);
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

    private static async Task<(string OrderStatus, string? RequestStatus, Guid? RequestId, string? RequestReason,
        int Stock, int Reserved, decimal UnitPrice, decimal Discount, int ReleaseCount)> ReadStateAsync(
        AppDbContext db,
        int orderId,
        int productId)
    {
        var order = await db.Orders.AsNoTracking().Where(item => item.Id == orderId)
            .Select(item => new
            {
                item.Status,
                item.CancelRequestStatus,
                item.CancelRequestId,
                item.CancelRequestReason,
                UnitPrice = item.Details.Select(line => line.UnitPrice).Single(),
                Discount = item.Details.Select(line => line.DiscountAmount).Single()
            }).SingleAsync();
        var inventory = await db.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == productId);
        var releaseCount = await db.InventoryTransactions.CountAsync(item => item.OrderId == orderId && item.TransactionType == "RELEASE");
        return (order.Status, order.CancelRequestStatus, order.CancelRequestId, order.CancelRequestReason,
            inventory.QuantityInStock, inventory.ReservedQuantity, order.UnitPrice, order.Discount, releaseCount);
    }

    private static async Task CleanupFixtureAsync(DbContextOptions<AppDbContext> options, string marker)
    {
        await using var db = new AppDbContext(options);
        var productIds = await db.Products.Where(item => item.Sku.Contains(marker)).Select(item => item.Id).ToArrayAsync();
        var orderIds = await db.Orders.Where(item => item.CustomerName.Contains(marker)).Select(item => item.Id).ToArrayAsync();
        if (productIds.Length > 0 || orderIds.Length > 0)
        {
            await db.InventoryTransactions.Where(item =>
                (item.OrderId != null && orderIds.Contains(item.OrderId.Value)) || productIds.Contains(item.ProductId))
                .ExecuteDeleteAsync();
            await db.OrderDetails.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await db.Orders.Where(item => orderIds.Contains(item.Id)).ExecuteDeleteAsync();
            await db.Inventories.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync();
            await db.Products.Where(item => productIds.Contains(item.Id)).ExecuteDeleteAsync();
        }

        var users = await db.Users.Where(item => item.Email != null && item.Email.Contains(marker)).ToListAsync();
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    private sealed record Fixture(string Marker, int OwnerUserId, int ReviewerUserId, int ProductId, int OrderId);

    private sealed class FailInventoryTransactionInsertInterceptor : DbCommandInterceptor
    {
        private static bool IsInventoryInsert(System.Data.Common.DbCommand command) =>
            command.CommandText.Contains("INSERT INTO [inventory_transactions]", StringComparison.OrdinalIgnoreCase);

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result) =>
            IsInventoryInsert(command) ? throw new InvalidOperationException("QA forced inventory ledger failure.") : result;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            IsInventoryInsert(command)
                ? ValueTask.FromException<InterceptionResult<System.Data.Common.DbDataReader>>(new InvalidOperationException("QA forced inventory ledger failure."))
                : ValueTask.FromResult(result);

        public override InterceptionResult<int> NonQueryExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result) =>
            IsInventoryInsert(command) ? throw new InvalidOperationException("QA forced inventory ledger failure.") : result;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            IsInventoryInsert(command)
                ? ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("QA forced inventory ledger failure."))
                : ValueTask.FromResult(result);
    }
}
