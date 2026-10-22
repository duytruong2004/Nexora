using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class CustomerOrdersIndexViewModel
{
    public IReadOnlyList<OrderListItem> Orders { get; init; } = [];
    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> AvailableStatuses { get; init; } = [];
    public string? SelectedStatus { get; init; }
    public string Search { get; init; } = string.Empty;
    public int TotalOrderCount { get; init; }
}
