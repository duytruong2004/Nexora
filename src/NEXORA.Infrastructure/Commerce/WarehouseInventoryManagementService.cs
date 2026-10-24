using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class WarehouseInventoryManagementService(
    AppDbContext db,
    ILogger<WarehouseInventoryManagementService> logger) : IWarehouseInventoryManagementService
{
    private const int MaximumPageSize = 50;
    private const int MaximumNoteLength = 1000;

    public async Task<WarehouseInventoryPage> SearchAsync(
        WarehouseInventoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var requestedPage = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        if (search is { Length: > 100 }) search = search[..100];

        var query = db.Products.AsNoTracking().AsQueryable();
        if (search is not null)
        {
            query = int.TryParse(search, out var productId)
                ? query.Where(product => product.Id == productId || product.Name.Contains(search) || product.Sku.Contains(search))
                : query.Where(product => product.Name.Contains(search) || product.Sku.Contains(search));
        }

        if (filter.OutOfStockOnly)
            query = query.Where(product => product.Inventory == null || product.Inventory.QuantityInStock <= product.Inventory.ReservedQuantity);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, pageCount);
        var rows = await query
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new WarehouseInventoryRow(
                product.Id,
                product.Sku,
                product.Name,
                product.Category.Name,
                product.Inventory == null ? null : product.Inventory.QuantityInStock,
                product.Inventory == null ? null : product.Inventory.ReservedQuantity,
                product.Inventory == null ? null : product.Inventory.QuantityInStock - product.Inventory.ReservedQuantity))
            .ToListAsync(cancellationToken);

        return new WarehouseInventoryPage(rows, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<WarehouseInventoryProductOption>> GetProductOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(product => product.Inventory != null)
            .OrderBy(product => product.Name)
            .Select(product => new WarehouseInventoryProductOption(product.Id, product.Sku, product.Name))
            .ToListAsync(cancellationToken);

    public async Task<WarehouseInventoryHistoryPage> GetHistoryAsync(
        WarehouseInventoryHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var requestedPage = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var type = string.IsNullOrWhiteSpace(filter.TransactionType) ? null : filter.TransactionType.Trim().ToUpperInvariant();
        if (type is not null && type is not ("IN" or "OUT" or "ADJUST" or "RESERVE" or "RELEASE")) type = null;

        var query = from transaction in db.InventoryTransactions.AsNoTracking()
                    join product in db.Products.AsNoTracking() on transaction.ProductId equals product.Id
                    join user in db.Users.AsNoTracking() on transaction.UserId equals user.Id
                    select new { transaction, product, user };
        if (filter.ProductId is > 0)
            query = query.Where(row => row.transaction.ProductId == filter.ProductId.Value);
        if (type is not null)
            query = query.Where(row => row.transaction.TransactionType == type);
        if (filter.From.HasValue)
        {
            var from = ToUtcBoundary(filter.From.Value.Date);
            query = query.Where(row => row.transaction.CreatedAt >= from);
        }
        if (filter.To.HasValue)
        {
            var toExclusive = ToUtcBoundary(filter.To.Value.Date.AddDays(1));
            query = query.Where(row => row.transaction.CreatedAt < toExclusive);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, pageCount);
        var raw = await query
            .OrderByDescending(row => row.transaction.CreatedAt)
            .ThenByDescending(row => row.transaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new
            {
                row.transaction.Id,
                row.transaction.CreatedAt,
                ProductId = row.product.Id,
                row.product.Sku,
                ProductName = row.product.Name,
                row.transaction.TransactionType,
                row.transaction.Quantity,
                ActorName = row.user.FullName,
                row.transaction.OrderId,
                row.transaction.Note
            })
            .ToListAsync(cancellationToken);

        var rows = raw.Select(row => new WarehouseInventoryHistoryRow(
            row.Id,
            row.CreatedAt,
            row.ProductId,
            row.Sku,
            row.ProductName,
            row.TransactionType,
            row.Quantity,
            GetDirection(row.TransactionType, row.Note),
            row.ActorName,
            row.OrderId,
            row.Note)).ToArray();
        return new WarehouseInventoryHistoryPage(rows, totalCount, page, pageSize);
    }

    public Task<WarehouseInventoryOperationResult> ReceiveAsync(
        WarehouseStockReceiptInput input,
        int warehouseUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reference = input.Reference?.Trim();
        var note = input.Note?.Trim();
        if (input.RequestId == Guid.Empty || input.ProductId <= 0 || input.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(reference) || reference.Length > 250 || note?.Length > 350)
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest));

        var ledgerNote = $"OPID={input.RequestId:N};DIR=IN;REF={Encode(reference)};NOTE={Encode(note)}";
        if (ledgerNote.Length > MaximumNoteLength)
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest));

        return ExecuteAsync(input.RequestId, input.ProductId, input.Quantity, "IN", ledgerNote, warehouseUserId, cancellationToken);
    }

    public Task<WarehouseInventoryOperationResult> AdjustAsync(
        WarehouseStockAdjustmentInput input,
        int warehouseUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var direction = input.Direction?.Trim().ToUpperInvariant();
        var reason = input.Reason?.Trim();
        var note = input.Note?.Trim();
        if (input.RequestId == Guid.Empty || input.ProductId <= 0 || input.Quantity <= 0 ||
            direction is not ("INCREASE" or "DECREASE") || string.IsNullOrWhiteSpace(reason) ||
            reason.Length > 250 || note?.Length > 350)
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest));

        var signedQuantity = direction == "INCREASE" ? input.Quantity : -input.Quantity;
        var ledgerNote = $"OPID={input.RequestId:N};DIR={direction};REASON={Encode(reason)};NOTE={Encode(note)}";
        if (ledgerNote.Length > MaximumNoteLength)
            return Task.FromResult(new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest));

        return ExecuteAsync(input.RequestId, input.ProductId, signedQuantity, "ADJUST", ledgerNote, warehouseUserId, cancellationToken);
    }

    private async Task<WarehouseInventoryOperationResult> ExecuteAsync(
        Guid requestId,
        int productId,
        int signedQuantity,
        string transactionType,
        string ledgerNote,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId <= 0)
            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    // The operation token is serialized by a transaction-owned applock. Inventory
                    // itself is serialized by the shared UPDLOCK/HOLDLOCK helper used by checkout/export.
                    // ReadCommitted avoids retaining broad range locks while scanning the unindexed ledger.
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                    try
                    {
                        if (!await AcquireOperationLockAsync(requestId, cancellationToken))
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.ConcurrencyConflict);
                        }

                        var replay = await FindOperationAsync(requestId, productId, signedQuantity, transactionType, ledgerNote, actorUserId, cancellationToken);
                        if (replay is not null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return replay;
                        }

                        if (!await db.Products.AsNoTracking().AnyAsync(product => product.Id == productId, cancellationToken))
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.ProductNotFound);
                        }

                        var inventory = await InventoryRowLocking.LoadForUpdateAsync(db, productId, cancellationToken);
                        if (inventory is null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InventoryMissing);
                        }

                        try
                        {
                            if (transactionType == "IN") inventory.Receive(signedQuantity);
                            else inventory.AdjustStock(signedQuantity);
                        }
                        catch (InvalidOperationException) when (transactionType == "ADJUST" && signedQuantity < 0)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseInventoryOperationResult(
                                WarehouseInventoryOperationFailure.BelowReservedQuantity,
                                inventory.QuantityInStock,
                                inventory.ReservedQuantity,
                                inventory.AvailableQuantity);
                        }
                        catch (OverflowException)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.InvalidRequest);
                        }

                        var magnitude = Math.Abs(signedQuantity);
                        db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductId = productId,
                            UserId = actorUserId,
                            OrderId = null,
                            TransactionType = transactionType,
                            Quantity = magnitude,
                            Note = ledgerNote,
                            CreatedAt = DateTime.UtcNow
                        });

                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        logger.LogInformation(
                            "Warehouse inventory operation {TransactionType} completed for product {ProductId} by user {ActorUserId}.",
                            transactionType,
                            productId,
                            actorUserId);
                        return new WarehouseInventoryOperationResult(
                            WarehouseInventoryOperationFailure.None,
                            inventory.QuantityInStock,
                            inventory.ReservedQuantity,
                            inventory.AvailableQuantity);
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None); }
                        catch { /* Disposing the transaction remains the final rollback boundary. */ }
                        throw;
                    }
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning("Warehouse inventory rowversion conflict for product {ProductId}.", productId);
                return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.ConcurrencyConflict);
            }
            catch (Exception exception) when (exception is DbUpdateException or SqlException or InvalidOperationException or TimeoutException or RetryLimitExceededException)
            {
                db.ChangeTracker.Clear();
                var verified = await TryResolveOperationAsync(requestId, productId, signedQuantity, transactionType, ledgerNote, actorUserId, cancellationToken);
                if (verified is not null)
                {
                    logger.LogInformation("Warehouse inventory operation was verified from its ledger marker after a database error.");
                    return verified;
                }

                var sqlError = FindSqlException(exception)?.Number;
                if (sqlError == 1205 && attempt < 3)
                {
                    logger.LogWarning("Warehouse inventory deadlock for product {ProductId}; safe retry {Attempt} of 3.", productId, attempt);
                    await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt), cancellationToken);
                    continue;
                }

                logger.LogError(
                    "Warehouse inventory operation failed for product {ProductId} ({TransactionType}, {ExceptionType}, SQL error {SqlErrorNumber}).",
                    productId,
                    transactionType,
                    exception.GetType().Name,
                    sqlError);
                return new WarehouseInventoryOperationResult(
                    IsCommitOutcomeUnknown(exception, sqlError)
                        ? WarehouseInventoryOperationFailure.CommitOutcomeUnknown
                        : sqlError == 1205
                            ? WarehouseInventoryOperationFailure.ConcurrencyConflict
                            : WarehouseInventoryOperationFailure.PersistenceFailure);
            }
        }

        return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.ConcurrencyConflict);
    }

    private async Task<WarehouseInventoryOperationResult?> FindOperationAsync(
        Guid requestId,
        int productId,
        int signedQuantity,
        string transactionType,
        string expectedNote,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var marker = OperationMarker(requestId);
        var entries = await db.InventoryTransactions.AsNoTracking()
            .Where(entry => entry.Note != null && entry.Note.Contains(marker))
            .Take(2)
            .ToListAsync(cancellationToken);
        if (entries.Count == 0) return null;
        if (entries.Count != 1) return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.IdempotencyConflict);

        var entry = entries[0];
        var expectedQuantity = Math.Abs(signedQuantity);
        if (entry.ProductId != productId || entry.UserId != actorUserId || entry.OrderId is not null ||
            entry.TransactionType != transactionType || entry.Quantity != expectedQuantity || entry.Note != expectedNote)
            return new WarehouseInventoryOperationResult(WarehouseInventoryOperationFailure.IdempotencyConflict);

        var inventory = await db.Inventories.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        return new WarehouseInventoryOperationResult(
            WarehouseInventoryOperationFailure.AlreadyApplied,
            inventory?.QuantityInStock,
            inventory?.ReservedQuantity,
            inventory?.AvailableQuantity);
    }

    private async Task<WarehouseInventoryOperationResult?> TryResolveOperationAsync(
        Guid requestId,
        int productId,
        int signedQuantity,
        string transactionType,
        string expectedNote,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            return await FindOperationAsync(requestId, productId, signedQuantity, transactionType, expectedNote, actorUserId, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> AcquireOperationLockAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @result;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = $"NEXORA:WAREHOUSE:INV:{requestId:N}";
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) >= 0;
    }

    private static string OperationMarker(Guid requestId) => $"OPID={requestId:N};";

    private static string GetDirection(string transactionType, string? note) => transactionType switch
    {
        "IN" => "Nhập",
        "OUT" => "Xuất",
        "RESERVE" => "Giữ",
        "RELEASE" => "Giải phóng",
        "ADJUST" when HasCanonicalAdjustmentDirection(note, "INCREASE") => "Tăng",
        "ADJUST" when HasCanonicalAdjustmentDirection(note, "DECREASE") => "Giảm",
        "ADJUST" => "Không xác định",
        _ => "Không xác định"
    };

    private static string Encode(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool HasCanonicalAdjustmentDirection(string? note, string direction)
    {
        if (string.IsNullOrEmpty(note)) return false;
        var fields = note.Split(';', StringSplitOptions.None);
        return fields.Length >= 3 && fields[0].Length == 37 && fields[0].StartsWith("OPID=", StringComparison.Ordinal) &&
               Guid.TryParseExact(fields[0][5..], "N", out _) && fields[1] == $"DIR={direction}";
    }

    private static DateTime ToUtcBoundary(DateTime value) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Local));

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException) return sqlException;
        }
        return null;
    }

    private static bool IsCommitOutcomeUnknown(Exception exception, int? sqlError) =>
        exception is RetryLimitExceededException or TimeoutException ||
        sqlError is -2 or 2 or 20 or 64 or 233 or 10053 or 10054 or 10060 or 11001;
}
