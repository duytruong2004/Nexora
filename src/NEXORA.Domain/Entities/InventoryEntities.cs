namespace NEXORA.Domain.Entities;

public sealed class Inventory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int QuantityInStock { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Product Product { get; set; } = null!;
    public int AvailableQuantity => QuantityInStock - ReservedQuantity;

    public void Receive(int quantity)
    {
        EnsurePositive(quantity);
        QuantityInStock = checked(QuantityInStock + quantity);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reserve(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException("Insufficient available inventory.");
        }

        ReservedQuantity = checked(ReservedQuantity + quantity);
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReleaseReservation(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > ReservedQuantity)
        {
            throw new InvalidOperationException("Cannot release more inventory than is reserved.");
        }

        ReservedQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ExportReserved(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > ReservedQuantity || quantity > QuantityInStock)
        {
            throw new InvalidOperationException("Cannot export inventory that is not reserved and in stock.");
        }

        QuantityInStock -= quantity;
        ReservedQuantity -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdjustStock(int delta)
    {
        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Inventory adjustment must be non-zero.");
        }

        var adjusted = checked(QuantityInStock + delta);
        if (adjusted < ReservedQuantity)
        {
            throw new InvalidOperationException("Adjustment cannot reduce stock below the reserved quantity.");
        }

        QuantityInStock = adjusted;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }
    }
}

public sealed class InventoryTransaction
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int UserId { get; set; }
    public int? OrderId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Product Product { get; set; } = null!;
    public Order? Order { get; set; }
}
