using System.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class CheckoutService(
    AppDbContext db,
    IOrderPricingService pricing,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const int MaxConcurrencyAttempts = 3;

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        int userId,
        PlaceOrderInput input,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || input.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(input.CustomerName) || input.CustomerName.Trim().Length is < 2 or > 200 ||
            string.IsNullOrWhiteSpace(input.Phone) || input.Phone.Trim().Length > 32 ||
            !new PhoneAttribute().IsValid(input.Phone) ||
            string.IsNullOrWhiteSpace(input.ShippingAddress) || input.ShippingAddress.Trim().Length is < 8 or > 1000 ||
            input.Note?.Length > 2000 ||
            string.IsNullOrWhiteSpace(input.ExpectedQuoteFingerprint) ||
            input.ExpectedQuoteFingerprint.Length != 64 ||
            input.ExpectedQuoteFingerprint.Any(character => !Uri.IsHexDigit(character)))
        {
            return new PlaceOrderResult(PlaceOrderFailure.InvalidRequest);
        }

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            db.ChangeTracker.Clear();
            var strategy = db.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                    var existing = await db.Orders.AsNoTracking()
                        .Where(order => order.CheckoutRequestId == input.RequestId)
                        .Select(order => new { order.Id, order.UserId })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (existing is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return existing.UserId == userId
                            ? new PlaceOrderResult(PlaceOrderFailure.None, existing.Id)
                            : new PlaceOrderResult(PlaceOrderFailure.InvalidRequest);
                    }

                    var quote = await pricing.GetCurrentQuoteAsync(userId, cancellationToken);
                    if (quote.IsEmpty)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.CartEmpty, CurrentQuote: quote);
                    }

                    if (!string.Equals(quote.Fingerprint, input.ExpectedQuoteFingerprint, StringComparison.Ordinal))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.QuoteChanged, CurrentQuote: quote);
                    }

                    var quoteByProduct = quote.Lines.ToDictionary(line => line.ProductId);
                    var cartItems = await db.Carts
                        .Where(cart => cart.UserId == userId)
                        .SelectMany(cart => cart.Items)
                        .OrderBy(item => item.ProductId)
                        .Include(item => item.Product)
                            .ThenInclude(product => product.Inventory)
                        .Include(item => item.Product.Category)
                        .Include(item => item.Product.Brand)
                        .ToListAsync(cancellationToken);

                    if (cartItems.Count == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.CartEmpty, CurrentQuote: quote);
                    }

                    if (cartItems.Any(item => !quoteByProduct.ContainsKey(item.ProductId)))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.QuoteChanged, CurrentQuote: quote);
                    }

                    if (cartItems.Any(item => item.Product.Status != ProductStatuses.Active ||
                                              !item.Product.Category.IsActive || !item.Product.Brand.IsActive))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.ProductUnavailable, CurrentQuote: quote);
                    }

                    if (cartItems.Any(item => item.Product.Inventory is null ||
                                              item.Quantity > item.Product.Inventory.AvailableQuantity))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new PlaceOrderResult(PlaceOrderFailure.InsufficientStock, CurrentQuote: quote);
                    }

                    var now = DateTime.UtcNow;
                    var order = new Order
                    {
                        UserId = userId,
                        CheckoutRequestId = input.RequestId,
                        CustomerName = input.CustomerName.Trim(),
                        Phone = input.Phone.Trim(),
                        ShippingAddress = input.ShippingAddress.Trim(),
                        Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
                        OrderDate = now,
                        Status = OrderStatuses.Pending,
                        TotalAmount = quote.Total
                    };

                    foreach (var item in cartItems)
                    {
                        var line = quoteByProduct[item.ProductId];
                        item.Product.Inventory!.Reserve(item.Quantity);
                        order.Details.Add(new OrderDetail
                        {
                            Order = order,
                            ProductId = item.ProductId,
                            ProductName = line.ProductName,
                            Quantity = item.Quantity,
                            UnitPrice = line.UnitPrice,
                            DiscountAmount = line.DiscountAmount,
                            TotalPrice = line.TotalPrice
                        });
                        order.InventoryTransactions.Add(new InventoryTransaction
                        {
                            Order = order,
                            ProductId = item.ProductId,
                            UserId = userId,
                            TransactionType = "RESERVE",
                            Quantity = item.Quantity,
                            Note = "Giữ hàng khi tạo đơn PENDING.",
                            CreatedAt = now
                        });
                    }

                    db.Orders.Add(order);
                    db.CartItems.RemoveRange(cartItems);
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new PlaceOrderResult(PlaceOrderFailure.None, order.Id);
                });
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                // Inventory rowversion changed. The next attempt reloads stock and quote inside a new transaction.
            }
            catch (DbUpdateConcurrencyException)
            {
                return new PlaceOrderResult(PlaceOrderFailure.InsufficientStock);
            }
            catch (DbUpdateException exception)
            {
                var verification = await FindCommittedOrderAsync(userId, input.RequestId, cancellationToken);
                if (verification.OrderId is int committedOrderId)
                {
                    return new PlaceOrderResult(PlaceOrderFailure.None, committedOrderId);
                }

                if (!verification.Completed)
                {
                    return new PlaceOrderResult(PlaceOrderFailure.CommitResultUnknown);
                }

                if (IsSqlDeadlock(exception) && attempt < MaxConcurrencyAttempts)
                {
                    logger.LogWarning(exception, "Checkout deadlock; retrying transaction attempt {Attempt}.", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                    continue;
                }

                if (IsTransientSqlFailure(exception))
                {
                    logger.LogWarning(exception, "Checkout transient database failure exhausted its retry budget.");
                    return new PlaceOrderResult(PlaceOrderFailure.TransientFailure);
                }

                logger.LogError(exception, "Checkout persistence failed.");
                return new PlaceOrderResult(PlaceOrderFailure.PersistenceFailure);
            }
            catch (Exception exception) when (exception is RetryLimitExceededException || FindSqlException(exception) is not null)
            {
                var verification = await FindCommittedOrderAsync(userId, input.RequestId, cancellationToken);
                if (verification.OrderId is int committedOrderId)
                {
                    return new PlaceOrderResult(PlaceOrderFailure.None, committedOrderId);
                }

                if (!verification.Completed)
                {
                    return new PlaceOrderResult(PlaceOrderFailure.CommitResultUnknown);
                }

                if (IsSqlDeadlock(exception) && attempt < MaxConcurrencyAttempts)
                {
                    logger.LogWarning(exception, "Checkout deadlock; retrying transaction attempt {Attempt}.", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                    continue;
                }

                if (IsTransientSqlFailure(exception))
                {
                    logger.LogWarning(exception, "Checkout transient database failure exhausted its retry budget.");
                    return new PlaceOrderResult(PlaceOrderFailure.TransientFailure);
                }

                logger.LogError(exception, "Checkout database operation failed without a committed order.");
                return new PlaceOrderResult(PlaceOrderFailure.PersistenceFailure);
            }
        }

        return new PlaceOrderResult(PlaceOrderFailure.InsufficientStock);
    }

    private async Task<CommitVerification> FindCommittedOrderAsync(
        int userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        try
        {
            var orderId = await db.Orders.AsNoTracking()
                .Where(order => order.CheckoutRequestId == requestId && order.UserId == userId)
                .Select(order => (int?)order.Id)
                .SingleOrDefaultAsync(cancellationToken);
            return new CommitVerification(true, orderId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to verify the persisted result for a checkout request.");
            return new CommitVerification(false, null);
        }
    }

    private static bool IsSqlDeadlock(Exception exception) => FindSqlException(exception)?.Number == 1205;

    private static bool IsTransientSqlFailure(Exception exception) =>
        exception is RetryLimitExceededException || FindSqlException(exception) is { } sqlException &&
        (sqlException.Number is -2 or 1205 or 40197 or 40501 or 40613 or 10928 or 10929 or 49918 or 49919 or 49920);

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException) return sqlException;
        }

        return null;
    }

    private sealed record CommitVerification(bool Completed, int? OrderId);
}
