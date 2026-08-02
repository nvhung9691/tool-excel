namespace ToolExcel.Api.Models;

/// <summary>Mot trang ket qua + du lieu de client dung thanh phan dieu huong.</summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = new();

    /// <summary>Trang thuc su duoc tra ve (da chuan hoa, co the khac tham so client gui).</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = Paging.DefaultPageSize;

    /// <summary>Tong so ban ghi khop dieu kien loc (khong phai so ban ghi trong trang).</summary>
    public int Total { get; set; }

    public int TotalPages => Total <= 0 ? 1 : (Total + PageSize - 1) / PageSize;
}

/// <summary>
/// Chuan hoa tham so phan trang. Tach rieng khoi DB/HTTP de test duoc: day la cho de sinh
/// loi off-by-one va la cho phai chiu tham so bat ky tu client (page=0, pageSize=999999, so am).
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 25;

    /// <summary>Tran tren de mot lan goi khong keo ca bang ve.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Tra ve trang + kich thuoc trang hop le, kem OFFSET de dua vao SQL.
    /// <para>Trang vuot qua cuoi duoc keo ve trang cuoi — de sau khi tat/loc bot user,
    /// client dang o trang 9 khong nhan ve mot bang trong khong hieu tai sao.</para>
    /// </summary>
    public static (int page, int pageSize, int offset) Normalize(int page, int pageSize, int total)
    {
        var size = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var lastPage = total <= 0 ? 1 : (total + size - 1) / size;
        var p = Math.Clamp(page <= 0 ? 1 : page, 1, lastPage);

        return (p, size, (p - 1) * size);
    }
}
