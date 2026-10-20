using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class WarehouseQueueViewModel
{
    public string? Search { get; init; }
    public WarehouseQueuePage Results { get; init; } = new([], 0, 1, 25);
}

public sealed class WarehouseOrderDetailsViewModel
{
    public required WarehouseOrderDetail Order { get; init; }
}
