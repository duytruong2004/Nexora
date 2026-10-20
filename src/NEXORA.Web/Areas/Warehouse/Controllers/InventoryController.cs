using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Web.Models;

namespace NEXORA.Web.Areas.Warehouse.Controllers;

[Area("Warehouse")]
[Authorize(Roles = RoleNames.Warehouse + "," + RoleNames.Admin)]
public sealed class InventoryController(IWarehouseInventoryManagementService inventory) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        bool outOfStockOnly = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var results = await inventory.SearchAsync(
            new WarehouseInventoryFilter(search, outOfStockOnly, page), cancellationToken);
        return View(new WarehouseInventoryIndexViewModel
        {
            Search = search,
            OutOfStockOnly = outOfStockOnly,
            Results = results
        });
    }

    public async Task<IActionResult> History(
        int? productId,
        string? transactionType,
        DateTime? from,
        DateTime? to,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
        {
            ModelState.AddModelError(string.Empty, "Ngày bắt đầu không được sau ngày kết thúc.");
            return View(new WarehouseInventoryHistoryViewModel
            {
                ProductId = productId,
                TransactionType = transactionType,
                From = from,
                To = to,
                Products = await inventory.GetProductOptionsAsync(cancellationToken)
            });
        }

        return View(new WarehouseInventoryHistoryViewModel
        {
            ProductId = productId,
            TransactionType = transactionType,
            From = from,
            To = to,
            Results = await inventory.GetHistoryAsync(
                new WarehouseInventoryHistoryFilter(productId, transactionType, from, to, page), cancellationToken),
            Products = await inventory.GetProductOptionsAsync(cancellationToken)
        });
    }

    [Authorize(Roles = RoleNames.Warehouse)]
    public async Task<IActionResult> Receive(int? productId, CancellationToken cancellationToken)
    {
        var model = new WarehouseStockReceiptViewModel
        {
            Products = await inventory.GetProductOptionsAsync(cancellationToken),
            ProductId = productId.GetValueOrDefault()
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Warehouse)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(
        WarehouseStockReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId)) return Forbid();
        if (!ModelState.IsValid)
        {
            model.Products = await inventory.GetProductOptionsAsync(cancellationToken);
            return View(model);
        }

        var result = await inventory.ReceiveAsync(
            new WarehouseStockReceiptInput(model.RequestId, model.ProductId, model.Quantity, model.Reference, model.Note),
            actorId,
            cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = result.Failure == WarehouseInventoryOperationFailure.AlreadyApplied
                ? "Thao tác nhập kho này đã được ghi nhận trước đó; không cộng tồn lần nữa."
                : "Đã nhập kho; tồn vật lý và sổ giao dịch đã được cập nhật cùng transaction.";
            return RedirectToAction(nameof(Index));
        }

        if (result.Failure == WarehouseInventoryOperationFailure.ProductNotFound) return NotFound();
        AddOperationError(result.Failure);
        model.Products = await inventory.GetProductOptionsAsync(cancellationToken);
        return View(model);
    }

    [Authorize(Roles = RoleNames.Warehouse)]
    public async Task<IActionResult> Adjust(int? productId, CancellationToken cancellationToken)
    {
        var model = new WarehouseStockAdjustmentViewModel
        {
            Products = await inventory.GetProductOptionsAsync(cancellationToken),
            ProductId = productId.GetValueOrDefault()
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Warehouse)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(
        WarehouseStockAdjustmentViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorId)) return Forbid();
        if (!ModelState.IsValid)
        {
            model.Products = await inventory.GetProductOptionsAsync(cancellationToken);
            return View(model);
        }

        var result = await inventory.AdjustAsync(
            new WarehouseStockAdjustmentInput(model.RequestId, model.ProductId, model.Direction,
                model.Quantity, model.Reason, model.Note),
            actorId,
            cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = result.Failure == WarehouseInventoryOperationFailure.AlreadyApplied
                ? "Thao tác điều chỉnh này đã được ghi nhận trước đó; tồn kho không bị thay đổi lần nữa."
                : "Đã điều chỉnh tồn; số lượng giữ nguyên và sổ giao dịch đã được cập nhật.";
            return RedirectToAction(nameof(Index));
        }

        if (result.Failure == WarehouseInventoryOperationFailure.ProductNotFound) return NotFound();
        AddOperationError(result.Failure);
        model.Products = await inventory.GetProductOptionsAsync(cancellationToken);
        return View(model);
    }

    private bool TryGetActorId(out int actorId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorId) && actorId > 0;

    private void AddOperationError(WarehouseInventoryOperationFailure failure) =>
        ModelState.AddModelError(string.Empty, failure switch
        {
            WarehouseInventoryOperationFailure.InvalidRequest => "Dữ liệu thao tác không hợp lệ hoặc vượt giới hạn cho phép.",
            WarehouseInventoryOperationFailure.InventoryMissing => "Sản phẩm chưa có bản ghi tồn kho. Hãy báo quản trị viên kiểm tra dữ liệu; thao tác không tự tạo tồn kho.",
            WarehouseInventoryOperationFailure.BelowReservedQuantity => "Không thể giảm tồn vật lý xuống thấp hơn số lượng đang giữ.",
            WarehouseInventoryOperationFailure.IdempotencyConflict => "Mã thao tác đã được dùng cho nội dung khác. Hãy tải lại biểu mẫu trước khi tạo thao tác mới.",
            WarehouseInventoryOperationFailure.ConcurrencyConflict => "Tồn kho đang được thay đổi bởi thao tác khác. Hãy tải lại trang và kiểm tra trước khi thử lại.",
            WarehouseInventoryOperationFailure.CommitOutcomeUnknown => "Chưa xác minh được kết quả ghi kho. Không tạo thao tác mới; tải lại lịch sử để kiểm tra rồi gửi lại cùng biểu mẫu nếu cần.",
            _ => "Không thể ghi giao dịch kho. Chưa xác nhận thành công; hãy kiểm tra lịch sử trước khi thử lại."
        });
}
