using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;

namespace NEXORA.Web.Areas.Admin.Models;

public sealed class CategoryListViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<CategoryListRow> Items { get; init; } = [];
}

public sealed record CategoryListRow(int Id, string Name, string? ParentName, bool IsActive);

public sealed class CategoryFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(120, ErrorMessage = "Tên danh mục tối đa 120 ký tự.")]
    [Display(Name = "Tên danh mục")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1.000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Danh mục cha")]
    public int? ParentId { get; set; }

    public IReadOnlyList<SelectListItem> ParentOptions { get; set; } = [];
}

public sealed class BrandListViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<BrandListRow> Items { get; init; } = [];
}

public sealed record BrandListRow(int Id, string Name, string? Description, bool IsActive);

public sealed class BrandFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên thương hiệu.")]
    [StringLength(120, ErrorMessage = "Tên thương hiệu tối đa 120 ký tự.")]
    [Display(Name = "Tên thương hiệu")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1.000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [StringLength(2048)]
    [RegularExpression(@"^(https://[^\s]+|/(?!/)[A-Za-z0-9/_\-.%]+)$", ErrorMessage = "Logo phải là URL HTTPS hoặc đường dẫn nội bộ hợp lệ.")]
    [Display(Name = "Đường dẫn logo (HTTPS)")]
    public string? LogoUrl { get; set; }
}

public sealed class ProductListViewModel
{
    public ProductAdminPage Page { get; init; } = new([], 0, 1, 20);
    public ProductAdminFilter Filter { get; init; } = new(null, null, null, null);
    public IReadOnlyList<SelectListItem> CategoryOptions { get; init; } = [];
    public IReadOnlyList<SelectListItem> BrandOptions { get; init; } = [];
}

public sealed class ProductFormViewModel
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn thương hiệu.")]
    [Display(Name = "Thương hiệu")]
    public int BrandId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(240, ErrorMessage = "Tên sản phẩm tối đa 240 ký tự.")]
    [Display(Name = "Tên sản phẩm")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã sản phẩm.")]
    [StringLength(80, ErrorMessage = "Mã sản phẩm tối đa 80 ký tự.")]
    [Display(Name = "Mã sản phẩm")]
    public string Sku { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "Mô tả tối đa 4.000 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Giá phải từ 0 đến 9.999.999.999.999.999,99 đồng.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [Display(Name = "Giá niêm yết (VNĐ)")]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Giá khuyến mại không hợp lệ.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [Display(Name = "Giá khuyến mại (VNĐ)")]
    public decimal? DiscountPrice { get; set; }

    [Range(0, 360, ErrorMessage = "Bảo hành phải từ 0 đến 360 tháng.")]
    [Display(Name = "Bảo hành (tháng)")]
    public int WarrantyMonths { get; set; }

    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE)$", ErrorMessage = "Trạng thái không hợp lệ.")]
    [Display(Name = "Trạng thái")]
    public string Status { get; set; } = ProductStatuses.Active;

    [Display(Name = "Ảnh đại diện")]
    public IFormFile? PrimaryImage { get; set; }

    [Display(Name = "Ảnh chi tiết")]
    public List<IFormFile> DetailImages { get; set; } = [];

    public IReadOnlyList<SelectListItem> CategoryOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> BrandOptions { get; set; } = [];
    public ProductAdminDetails? ExistingProduct { get; set; }
}

public sealed class ProductDetailsViewModel
{
    public ProductAdminDetails Product { get; init; } = null!;
}

public sealed class SpecificationListViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<SpecificationAdminRow> Items { get; init; } = [];
}

public sealed class SpecificationFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên thông số.")]
    [StringLength(160)]
    [Display(Name = "Tên thông số")]
    public string Name { get; set; } = string.Empty;

    [StringLength(40)]
    [Display(Name = "Đơn vị đo")]
    public string? Unit { get; set; }

    [Required]
    [RegularExpression("^(NUMBER|TEXT|ENUM|BOOLEAN)$", ErrorMessage = "Chỉ hỗ trợ NUMBER, TEXT, ENUM hoặc BOOLEAN.")]
    [Display(Name = "Kiểu dữ liệu")]
    public string DataType { get; set; } = "TEXT";
}

public sealed class CategorySpecificationConfigurationViewModel
{
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public IReadOnlyList<CategorySpecificationAdminRow> Assigned { get; init; } = [];
    public IReadOnlyList<SpecificationAdminRow> Available { get; init; } = [];
}

public sealed class ProductSpecificationEditorViewModel
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public List<ProductSpecificationValueViewModel> Values { get; set; } = [];
}

public sealed class ProductSpecificationValueViewModel
{
    public int SpecificationId { get; set; }
    public string Name { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string DataType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public IReadOnlyList<string> EnumOptions { get; init; } = [];
    [StringLength(1000)]
    public string? Value { get; set; }
    [StringLength(300)]
    [Display(Name = "Giá trị hiển thị")]
    public string? DisplayValue { get; set; }
}
