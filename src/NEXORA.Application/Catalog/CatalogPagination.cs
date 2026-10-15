namespace NEXORA.Application.Catalog;

public sealed record CatalogPageWindow(int Page, int PageSize, int PageCount, int Skip)
{
    public static CatalogPageWindow Create(int requestedPage, int requestedPageSize, int totalCount, int maximumPageSize)
    {
        var pageSize = Math.Clamp(requestedPageSize, 1, Math.Max(1, maximumPageSize));
        var pageCount = Math.Max(1, (int)Math.Ceiling(Math.Max(0, totalCount) / (double)pageSize));
        var page = Math.Clamp(requestedPage, 1, pageCount);
        return new CatalogPageWindow(page, pageSize, pageCount, checked((page - 1) * pageSize));
    }
}
