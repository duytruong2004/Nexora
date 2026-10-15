using NEXORA.Domain.Entities;

namespace NEXORA.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void Reserve_ReducesAvailableQuantityWithoutChangingPhysicalStock()
    {
        var inventory = CreateInventory(available: 8);

        inventory.Reserve(5);

        Assert.Equal(10, inventory.QuantityInStock);
        Assert.Equal(7, inventory.ReservedQuantity);
        Assert.Equal(3, inventory.AvailableQuantity);
    }

    [Fact]
    public void Reserve_RejectsQuantityAboveAvailableStock()
    {
        var inventory = CreateInventory(available: 2);

        Assert.Throws<InvalidOperationException>(() => inventory.Reserve(3));
        Assert.Equal(2, inventory.AvailableQuantity);
    }

    [Fact]
    public void ReleaseReservation_RestoresAvailableQuantity()
    {
        var inventory = CreateInventory(available: 8);
        inventory.Reserve(5);

        inventory.ReleaseReservation(3);

        Assert.Equal(10, inventory.QuantityInStock);
        Assert.Equal(4, inventory.ReservedQuantity);
        Assert.Equal(6, inventory.AvailableQuantity);
    }

    [Fact]
    public void ExportReserved_ReducesPhysicalAndReservedQuantitiesTogether()
    {
        var inventory = CreateInventory(available: 8);
        inventory.Reserve(5);

        inventory.ExportReserved(2);

        Assert.Equal(8, inventory.QuantityInStock);
        Assert.Equal(5, inventory.ReservedQuantity);
        Assert.Equal(3, inventory.AvailableQuantity);
    }

    [Fact]
    public void AdjustStock_CannotReducePhysicalStockBelowReservations()
    {
        var inventory = CreateInventory(available: 8);
        inventory.Reserve(5);

        Assert.Throws<InvalidOperationException>(() => inventory.AdjustStock(-6));
        Assert.Equal(10, inventory.QuantityInStock);
        Assert.Equal(7, inventory.ReservedQuantity);
    }

    [Fact]
    public void InventoryOperations_RequirePositiveQuantities()
    {
        var inventory = CreateInventory(available: 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Reserve(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.ReleaseReservation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.ExportReserved(0));
    }

    private static Inventory CreateInventory(int available)
    {
        var inventory = new Inventory();
        inventory.Receive(available + 2);
        inventory.Reserve(2);
        return inventory;
    }
}
