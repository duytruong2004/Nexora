using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class WarehouseInventoryIndexViewModel
{
    public string? Search { get; init; }
    public bool OutOfStockOnly { get; init; }
    public WarehouseInventoryPage Results { get; init; } = new([], 0, 1, 25);
}

public sealed class WarehouseInventoryHistoryViewModel
{
    public int? ProductId { get; init; }
    public string? TransactionType { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public WarehouseInventoryHistoryPage Results { get; init; } = new([], 0, 1, 25);
    public IReadOnlyList<WarehouseInventoryProductOption> Products { get; init; } = [];
}

public abstract class WarehouseInventoryOperationViewModel
{
    [Required]
    public Guid RequestId { get; set; } = Guid.NewGuid();

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải là số nguyên dương.")]
    public int Quantity { get; set; }

    public IReadOnlyList<WarehouseInventoryProductOption> Products { get; set; } = [];
}

public sealed class WarehouseStockReceiptViewModel : WarehouseInventoryOperationViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã tham chiếu hoặc lý do nhập kho.")]
    [StringLength(250)]
    public string? Reference { get; set; }

    [StringLength(350)]
    public string? Note { get; set; }
}

public sealed class WarehouseStockAdjustmentViewModel : WarehouseInventoryOperationViewModel
{
    [Required]
    [RegularExpression("^(INCREASE|DECREASE)$", ErrorMessage = "Chọn hướng tăng hoặc giảm tồn.")]
    public string? Direction { get; set; }

    [Required(ErrorMessage = "Lý do điều chỉnh là bắt buộc.")]
    [StringLength(250)]
    public string? Reason { get; set; }

    [StringLength(350)]
    public string? Note { get; set; }
}
