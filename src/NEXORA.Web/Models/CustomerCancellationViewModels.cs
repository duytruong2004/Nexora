using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class CustomerOrderDetailsViewModel
{
    public required OrderDetailView Order { get; init; }
    public Guid RequestId { get; init; }
}

public sealed class CustomerCancellationRequestInput
{
    public Guid RequestId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do yêu cầu hủy.")]
    [StringLength(1000, ErrorMessage = "Lý do không được vượt quá 1.000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
