using ToolExcel.Api.Models;
using Xunit;

namespace ToolExcel.Tests;

/// <summary>
/// <see cref="Paging.Normalize"/> nhan tham so bat ky tu client (page=0, so am, pageSize khong lo)
/// va phai sinh OFFSET dung. Off-by-one o day lam mat hoac lap ban ghi giua cac trang.
/// </summary>
public class PagingTests
{
    [Fact]
    public void TrangDau_OffsetBangKhong()
    {
        var (page, size, offset) = Paging.Normalize(1, 25, 312);
        Assert.Equal((1, 25, 0), (page, size, offset));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 25)]
    [InlineData(3, 50)]
    [InlineData(13, 300)]   // trang cuoi cua 312 ban ghi, size 25
    public void Offset_TinhDung(int page, int expectedOffset)
    {
        var (_, _, offset) = Paging.Normalize(page, 25, 312);
        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void TrangVuotQuaCuoi_KeoVeTrangCuoi()
    {
        // 312 ban ghi / 25 = 13 trang. Xin trang 99 -> phai ve 13, khong tra bang trong.
        var (page, _, offset) = Paging.Normalize(99, 25, 312);
        Assert.Equal(13, page);
        Assert.Equal(300, offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void TrangKhongDuong_VeTrang1(int page)
    {
        var (p, _, offset) = Paging.Normalize(page, 25, 312);
        Assert.Equal(1, p);
        Assert.Equal(0, offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageSizeKhongDuong_DungMacDinh(int pageSize)
    {
        var (_, size, _) = Paging.Normalize(1, pageSize, 312);
        Assert.Equal(Paging.DefaultPageSize, size);
    }

    [Fact]
    public void PageSizeQuaLon_BiChanTran()
    {
        var (_, size, _) = Paging.Normalize(1, 999_999, 312);
        Assert.Equal(Paging.MaxPageSize, size);
    }

    [Fact]
    public void KhongCoBanGhi_VanLaTrang1_KhongChiaChoKhong()
    {
        var (page, size, offset) = Paging.Normalize(5, 25, 0);
        Assert.Equal(1, page);
        Assert.Equal(25, size);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void SoBanGhiChiaHetChoPageSize_KhongSinhTrangRongOCuoi()
    {
        // 100 / 25 = dung 4 trang, khong duoc ra 5.
        var (page, _, offset) = Paging.Normalize(99, 25, 100);
        Assert.Equal(4, page);
        Assert.Equal(75, offset);
    }

    [Fact]
    public void ChiMotBanGhi_MotTrang()
    {
        var (page, _, offset) = Paging.Normalize(1, 25, 1);
        Assert.Equal(1, page);
        Assert.Equal(0, offset);
    }

    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(100, 25, 4)]
    [InlineData(101, 25, 5)]
    [InlineData(312, 25, 13)]
    public void TotalPages_TinhDung(int total, int pageSize, int expected)
    {
        var result = new PagedResult<string> { Total = total, PageSize = pageSize };
        Assert.Equal(expected, result.TotalPages);
    }
}
