using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Commerce;

namespace NEXORA.Web.Models;

public sealed class CheckoutViewModel
{
    [Required, StringLength(200, MinimumLength = 2)]
    [Display(Name = "Người nhận")]
    public string CustomerName { get; set; } = string.Empty;

    [Required, StringLength(32)]
    [RegularExpression(@"^\+?[0-9][0-9\s().-]{7,30}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(1000, MinimumLength = 8)]
    [Display(Name = "Địa chỉ giao hàng")]
    public string ShippingAddress { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public Guid RequestId { get; set; }
    public string QuoteToken { get; set; } = string.Empty;
    public CheckoutQuote Quote { get; set; } = new([], DateTime.UtcNow, string.Empty);
}
