using ToolExcel.Api.Models;
using ToolExcel.Api.Services;
using Xunit;

namespace ToolExcel.Tests;

/// <summary>
/// <see cref="UserAdminService.OrderAsTree"/> dung de sap cay don vi cho dropdown gan BUKRS.
/// Yeu cau quan trong nhat: KHONG BAO GIO lam mat don vi khoi danh sach, du du lieu PT_T001 loi
/// (cha bi tat, PARENT_ID tro vao ban ghi khong con, hoac cha-con vong lai nhau).
/// </summary>
public class OrgTreeTests
{
    private static OrgItem Org(long id, string bukrs, long? parentId = null)
        => new() { Id = id, Bukrs = bukrs, Butxt = "DV " + bukrs, ParentId = parentId };

    [Fact]
    public void ChaTruocCon_VaTinhDungLevel()
    {
        var result = UserAdminService.OrderAsTree(new List<OrgItem>
        {
            Org(1, "TKV"),
            Org(2, "2100", 1),
            Org(3, "2110", 2),
            Org(4, "2200", 1),
        });

        Assert.Equal(new[] { "TKV", "2100", "2110", "2200" }, result.Select(o => o.Bukrs));
        Assert.Equal(new[] { 0, 1, 2, 1 }, result.Select(o => o.Level));
    }

    [Fact]
    public void ChaKhongTonTai_CoiNhuGoc_VanTraVe()
    {
        // PARENT_ID=99 khong co trong danh sach (cha bi IS_ACTIVE='N' hoac da xoa).
        var result = UserAdminService.OrderAsTree(new List<OrgItem> { Org(2, "2100", 99) });

        var one = Assert.Single(result);
        Assert.Equal("2100", one.Bukrs);
        Assert.Equal(0, one.Level);
    }

    [Fact]
    public void TuTroVaoChinhMinh_CoiNhuGoc()
    {
        var result = UserAdminService.OrderAsTree(new List<OrgItem> { Org(1, "TKV", 1) });

        Assert.Equal("TKV", Assert.Single(result).Bukrs);
    }

    [Fact]
    public void VongLapChaCon_VanTraVeDuKhongTreo()
    {
        // 1 -> 2 -> 1: khong co goc nao. Phai tra ve ca hai, khong lap vo han.
        var result = UserAdminService.OrderAsTree(new List<OrgItem>
        {
            Org(1, "A", 2),
            Org(2, "B", 1),
        });

        Assert.Equal(2, result.Count);
        Assert.Contains("A", result.Select(o => o.Bukrs));
        Assert.Contains("B", result.Select(o => o.Bukrs));
    }

    [Fact]
    public void KhongLamMatBanGhiNao()
    {
        var flat = new List<OrgItem>
        {
            Org(1, "TKV"),
            Org(2, "2100", 1),
            Org(3, "2110", 2),
            Org(4, "MOCOI", 77),   // cha khong ton tai
            Org(5, "VONG1", 6),    // vong lap
            Org(6, "VONG2", 5),
        };

        var result = UserAdminService.OrderAsTree(flat);

        Assert.Equal(flat.Count, result.Count);
        Assert.Equal(flat.Select(o => o.Id).OrderBy(i => i), result.Select(o => o.Id).OrderBy(i => i));
    }

    [Fact]
    public void DanhSachRong_TraVeRong()
        => Assert.Empty(UserAdminService.OrderAsTree(new List<OrgItem>()));
}
