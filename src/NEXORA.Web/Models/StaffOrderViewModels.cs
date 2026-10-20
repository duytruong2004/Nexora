using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class StaffOrderIndexViewModel
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? CancelRequestStatus { get; init; }
    public int Page { get; init; }
    public StaffOrderPage Results { get; init; } = new([], 0, 1, 25);
}

public sealed class StaffOrderDetailsViewModel
{
    public required StaffOrderDetail Order { get; init; }
    public bool CanConfirm { get; init; }
    public bool CanHandoff { get; init; }
    public bool CanMarkShipping { get; init; }
    public bool CanComplete { get; init; }
    public bool CanReviewCancellation { get; init; }
}

public static class CancellationRequestPresentation
{
    public static string Label(string? status) => status switch
    {
        "PENDING" => "Chờ duyệt hủy",
        "APPROVED" => "Đã duyệt hủy",
        "REJECTED" => "Đã từ chối hủy",
        _ => "Chưa yêu cầu"
    };

    public static string BadgeClass(string? status) => status switch
    {
        "PENDING" => "text-bg-warning",
        "APPROVED" => "text-bg-success",
        "REJECTED" => "text-bg-secondary",
        _ => "text-bg-light text-dark"
    };
}

public static class OrderStatusPresentation
{
    public static string Label(string status) => status switch
    {
        "PENDING" => "Chờ xác nhận",
        "CONFIRMED" => "Đã xác nhận",
        "PREPARING" => "Đang chuẩn bị",
        "EXPORTED" => "Đã xuất kho",
        "SHIPPING" => "Đang giao",
        "COMPLETED" => "Hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => status
    };

    public static string BadgeClass(string status) => status switch
    {
        "PENDING" => "text-bg-warning",
        "CONFIRMED" => "text-bg-primary",
        "PREPARING" => "text-bg-info",
        "EXPORTED" => "text-bg-secondary",
        "SHIPPING" => "text-bg-info",
        "COMPLETED" => "text-bg-success",
        "CANCELLED" => "text-bg-danger",
        _ => "text-bg-secondary"
    };
}
