using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Web.Models;

namespace NEXORA.Web.Areas.Warehouse.Controllers;

[Area("Warehouse")]
[Authorize(Roles = RoleNames.Warehouse)]
public sealed class OrdersController(IWarehouseFulfillmentService warehouse) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var results = await warehouse.GetQueueAsync(new WarehouseQueueFilter(search, page), cancellationToken);
        return View(new WarehouseQueueViewModel { Search = search, Results = results });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await warehouse.GetOrderAsync(id, cancellationToken);
        return order is null
            ? NotFound()
            : View(new WarehouseOrderDetailsViewModel { Order = order });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var warehouseUserId) || warehouseUserId <= 0)
            return Forbid();

        var result = await warehouse.ExportAsync(id, warehouseUserId, cancellationToken);
        if (result.Failure == WarehouseExportFailure.NotFound) return NotFound();
        if (result.Failure == WarehouseExportFailure.InvalidRequest) return BadRequest();

        TempData[result.Succeeded || result.Failure == WarehouseExportFailure.AlreadyExported ? "Success" : "Error"] = result.Failure switch
        {
            WarehouseExportFailure.None => "Đã xuất toàn bộ đơn hàng; tồn kho, reservation, ledger OUT và trạng thái đã được lưu cùng transaction.",
            WarehouseExportFailure.AlreadyExported => "Đơn này đã được xuất trước đó. Không trừ tồn hoặc ghi OUT lần nữa.",
            WarehouseExportFailure.CancellationPending => "Đơn có yêu cầu hủy đang chờ. Kho chưa thể xuất hàng.",
            WarehouseExportFailure.InvalidOrderDetails => "Chi tiết đơn không hợp lệ. Chưa có thay đổi kho nào được lưu.",
            WarehouseExportFailure.InventoryMissing => "Thiếu bản ghi tồn kho cho sản phẩm trong đơn. Chưa có thay đổi kho nào được lưu.",
            WarehouseExportFailure.InsufficientReservation => "Tồn kho hoặc reservation không đủ cho toàn bộ đơn. Chưa có thay đổi kho nào được lưu.",
            WarehouseExportFailure.ConcurrencyConflict => "Tồn kho vừa được thay đổi bởi thao tác khác. Hãy tải lại đơn rồi kiểm tra.",
            WarehouseExportFailure.CommitResultUnknown => "Chưa xác minh được kết quả xuất kho. Không gửi lại thao tác liên tục; hãy tải lại đơn và kiểm tra ledger.",
            WarehouseExportFailure.InvalidState => "Chỉ đơn đang chuẩn bị mới được xuất kho.",
            _ => "Không thể xuất kho. Transaction đã được rollback nếu chưa commit. Hãy tải lại và thử lại sau."
        };

        return RedirectToAction(nameof(Details), new { id });
    }
}
