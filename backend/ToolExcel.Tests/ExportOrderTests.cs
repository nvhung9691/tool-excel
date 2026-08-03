using System.Data;
using ToolExcel.Api.Services;
using Xunit;

namespace ToolExcel.Tests;

/// <summary>
/// Thu tu dong khi export. Con tro cua PKG_DYNAMIC_EXPORT tra ve KHONG theo thu tu nao,
/// va moi bieu mau dung mot cot khac nhau de xep: KTTC03 dung LINEID (SORT rong),
/// KH18 dung SORT (LINEID rong). Day la logic thuan nen test duoc khong can DB.
/// </summary>
public class ExportOrderTests
{
    /// <summary>Dung DataTable co cot SORT/LINEID kieu object de nap duoc ca null.</summary>
    private static DataTable Make(params (object? Sort, object? LineId, string Ma)[] rows)
    {
        var t = new DataTable();
        t.Columns.Add("SORT", typeof(object));
        t.Columns.Add("LINEID", typeof(object));
        t.Columns.Add("MATNR", typeof(string));

        foreach (var (sort, lineId, ma) in rows)
            t.Rows.Add(sort ?? DBNull.Value, lineId ?? DBNull.Value, ma);

        return t;
    }

    private static string[] MaSau(DataTable t) =>
        ExcelExportService.OrderRows(t).Select(r => (string)r["MATNR"]).ToArray();

    [Fact]
    public void KTTC03_SORT_rong_thi_xep_theo_LINEID()
    {
        var t = Make(
            (null, 299m, "7001072"),
            (null, 32m, "7000200"),
            (null, 318m, "7001088"),
            (null, 96m, "7000269"));

        Assert.Equal(new[] { "7000200", "7000269", "7001072", "7001088" }, MaSau(t));
    }

    [Fact]
    public void KH18_LINEID_rong_thi_xep_theo_SORT()
    {
        var t = Make(
            (3m, null, "2000000006502"),
            (1m, null, "2000000006500"),
            (2m, null, "2000000006501"));

        Assert.Equal(new[] { "2000000006500", "2000000006501", "2000000006502" }, MaSau(t));
    }

    [Fact]
    public void SORT_duoc_uu_tien_hon_LINEID()
    {
        var t = Make(
            (2m, 1m, "sau"),
            (1m, 9m, "truoc"));

        Assert.Equal(new[] { "truoc", "sau" }, MaSau(t));
    }

    [Fact]
    public void Dong_khong_co_thu_tu_bi_day_xuong_cuoi_va_giu_nguyen_thu_tu_goc()
    {
        // 4 dong THANNK3/Than Nguyen Khai cua KTTC03 khong co ca SORT lan LINEID.
        var t = Make(
            (null, null, "THANNK3"),
            (null, 300m, "7001073"),
            (null, null, "1007010000001"),
            (null, 299m, "7001072"));

        Assert.Equal(new[] { "7001072", "7001073", "THANNK3", "1007010000001" }, MaSau(t));
    }

    [Fact]
    public void Thu_tu_so_khong_phai_thu_tu_chuoi()
    {
        // Xep theo chuoi thi "100" < "99"; phai xep theo so.
        var t = Make(
            (null, 100m, "tram"),
            (null, 99m, "chin_muoi_chin"));

        Assert.Equal(new[] { "chin_muoi_chin", "tram" }, MaSau(t));
    }

    [Fact]
    public void Gia_tri_thu_tu_dang_chuoi_van_doc_duoc_thanh_so()
    {
        var t = Make(
            (null, "10", "muoi"),
            (null, "9", "chin"));

        Assert.Equal(new[] { "chin", "muoi" }, MaSau(t));
    }

    [Fact]
    public void Khong_co_cot_thu_tu_thi_giu_nguyen_thu_tu_con_tro()
    {
        var t = new DataTable();
        t.Columns.Add("MATNR", typeof(string));
        t.Rows.Add("b");
        t.Rows.Add("a");

        Assert.Equal(new[] { "b", "a" }, MaSau(t));
    }
}
