using ToolExcel.Api.Services;
using Xunit;

namespace ToolExcel.Tests;

/// <summary>
/// Logic chan BUKRS cua /api/bieumau/*. Day la lop bao ve chinh cho phia APEX goi sang,
/// nen tung nhanh phai co test — nhat la nhanh "chua gan don vi nao" (tap rong).
/// </summary>
public class BukrsScopeTests
{
    private static IReadOnlySet<string> Set(params string[] items)
        => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Super_KhongGioiHan_ChoQua()
        => Assert.Equal(ScopeDecision.Allow, BukrsScope.Decide(null, "2100"));

    [Fact]
    public void Super_ThieuBukrs_VanChoQua()
        => Assert.Equal(ScopeDecision.Allow, BukrsScope.Decide(null, null));

    [Fact]
    public void TrongPhamVi_ChoQua()
        => Assert.Equal(ScopeDecision.Allow, BukrsScope.Decide(Set("2100", "2200"), "2100"));

    [Fact]
    public void NgoaiPhamVi_Chan()
        => Assert.Equal(ScopeDecision.Forbidden, BukrsScope.Decide(Set("2100"), "9999"));

    [Fact]
    public void ChuaGanDonViNao_Chan_KhongPhaiChoQua()
    {
        // Bay nguy hiem nhat: tap rong nghia la "khong duoc gi", khong phai "khong gioi han".
        Assert.Equal(ScopeDecision.Forbidden, BukrsScope.Decide(Set(), "2100"));
    }

    [Fact]
    public void ThieuBukrs_KhiBiGioiHan_BaoThieuThamSo()
        => Assert.Equal(ScopeDecision.MissingBukrs, BukrsScope.Decide(Set("2100"), null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BukrsRong_BaoThieuThamSo(string bukrs)
        => Assert.Equal(ScopeDecision.MissingBukrs, BukrsScope.Decide(Set("2100"), bukrs));

    [Fact]
    public void BukrsCoKhoangTrang_VanKhopSauKhiTrim()
        => Assert.Equal(ScopeDecision.Allow, BukrsScope.Decide(Set("2100"), " 2100 "));

    [Fact]
    public void SoSanhKhongPhanBietHoaThuong()
        => Assert.Equal(ScopeDecision.Allow, BukrsScope.Decide(Set("VD01"), "vd01"));
}
