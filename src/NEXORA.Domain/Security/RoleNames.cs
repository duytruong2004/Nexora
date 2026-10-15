namespace NEXORA.Domain.Security;

public static class RoleNames
{
    public const string Customer = "Customer";
    public const string Sales = "Sales";
    public const string Warehouse = "Warehouse";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Customer, Sales, Warehouse, Admin];
}
