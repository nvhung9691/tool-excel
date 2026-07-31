using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ToolExcel.Api.Models;
using Xunit;

namespace ToolExcel.Tests;

public class BieuMauColumnConfigTests
{
    [Theory]
    [InlineData("C001", 1)]
    [InlineData("C010", 10)]
    [InlineData("C123", 123)]
    [InlineData("c5", 5)]
    public void ExcelColIndex_ParsesCColumn(string excelCol, int expected)
    {
        var c = new BieuMauColumnConfig { ExcelCol = excelCol, BieumauCol = "X" };
        Assert.Equal(expected, c.ExcelColIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]   // khong bat dau bang C so -> khong parse duoc
    public void ExcelColIndex_InvalidReturnsZero(string? excelCol)
    {
        var c = new BieuMauColumnConfig { ExcelCol = excelCol, BieumauCol = "X" };
        Assert.Equal(0, c.ExcelColIndex);
    }

    [Fact]
    public void IsHeader_TrueOnlyForUppercaseX()
    {
        Assert.True(new BieuMauColumnConfig { Header = "X", BieumauCol = "BUKRS" }.IsHeader);
        Assert.False(new BieuMauColumnConfig { Header = "x", BieumauCol = "BUKRS" }.IsHeader);
        Assert.False(new BieuMauColumnConfig { Header = null, BieumauCol = "GT01" }.IsHeader);
        Assert.False(new BieuMauColumnConfig { Header = "Y", BieumauCol = "GT01" }.IsHeader);
    }
}

public class HeaderParamsTests
{
    private static IQueryCollection Query(params (string key, string val)[] items)
    {
        var d = new Dictionary<string, StringValues>();
        foreach (var (k, v) in items) d[k] = v;
        return new QueryCollection(d);
    }

    [Fact]
    public void FromQuery_ExtractsOnlyHPrefixed()
    {
        var hp = HeaderParams.FromQuery(Query(
            ("h_BUKRS", "2100"),
            ("h_YEAR", "2026"),
            ("connId", "PB9")));   // khong co tien to h_ -> bo qua

        Assert.Equal("2100", hp.Get("BUKRS"));
        Assert.Equal("2026", hp.Get("YEAR"));
        Assert.Null(hp.Get("connId"));
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var hp = HeaderParams.FromQuery(Query(("h_BUKRS", "2100")));
        Assert.Equal("2100", hp.Get("bukrs"));
        Assert.Equal("2100", hp.Get("BUKRS"));
    }
}
