using System.Data;
using ClosedXML.Excel;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

public interface IExcelExportService
{
    /// <summary>Sinh file template .xlsx (byte[]) cho 1 bieu mau + tham so header.</summary>
    Task<byte[]> ExportTemplateAsync(string? connId, string formCode, HeaderParams header, CancellationToken ct);
}

/// <summary>
/// Goi PKG_DYNAMIC_EXPORT.GET_DATA_DYNAMIC (function tra SYS_REFCURSOR) roi rot ra Excel.
/// Cot dat theo DM_BIEU_MAU_CONFIG.EXCEL_COL; cot FORMAT luon o cot 'AAA' + conditional formatting.
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    // Cot FORMAT co dinh o cot AAA (giong Tool_Portal).
    private const string FormatColumn = "AAA";

    private readonly IOracleConnectionFactory _factory;
    private readonly IBieuMauConfigService _config;
    private readonly ILogger<ExcelExportService> _logger;

    public ExcelExportService(
        IOracleConnectionFactory factory,
        IBieuMauConfigService config,
        ILogger<ExcelExportService> logger)
    {
        _factory = factory;
        _config = config;
        _logger = logger;
    }

    public async Task<byte[]> ExportTemplateAsync(
        string? connId, string formCode, HeaderParams header, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(connId, ct);

        var bieuMau = await _config.GetBieuMauAsync(conn, formCode, ct)
            ?? throw new KeyNotFoundException($"Khong tim thay bieu mau FORM_CODE='{formCode}'.");
        var columns = await _config.GetColumnsAsync(conn, formCode, ct);

        // Map ten cot (upper) -> chi so cot Excel 1-based (tu config).
        var colIndexByName = columns
            .Where(c => c.ExcelColIndex > 0)
            .GroupBy(c => c.BieumauCol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ExcelColIndex, StringComparer.OrdinalIgnoreCase);

        using var table = await LoadCursorAsync(conn, formCode, header, ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(formCode));

        // ROW_EXCEL = dong bat dau vung du lieu chi tiet; dong tieu de = ROW_EXCEL - 1.
        var dataStartRow = Math.Max(1, bieuMau.RowExcel);
        var headerRow = Math.Max(1, dataStartRow - 1);

        WriteHeaderRow(ws, headerRow, columns);
        var lastDataRow = WriteDataRows(ws, dataStartRow, table, colIndexByName);
        ApplyFormatConditionalFormatting(ws, dataStartRow, lastDataRow);

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Goi function ref cursor va nap toan bo vao DataTable. Bay cursor loi 1 cot MSG.</summary>
    private async Task<DataTable> LoadCursorAsync(
        OracleConnection conn, string formCode, HeaderParams header, CancellationToken ct)
    {
        const string plsql = @"
            BEGIN
              :ret := PKG_DYNAMIC_EXPORT.GET_DATA_DYNAMIC(:p_form, :p_bukrs, :p_year, :p_period, :p_day, :p_werks);
            END;";

        using var cmd = new OracleCommand(plsql, conn) { BindByName = true };

        var ret = new OracleParameter("ret", OracleDbType.RefCursor, ParameterDirection.Output);
        cmd.Parameters.Add(ret);
        cmd.Parameters.Add(new OracleParameter("p_form",   OracleDbType.Varchar2) { Value = formCode });
        cmd.Parameters.Add(new OracleParameter("p_bukrs",  OracleDbType.Varchar2) { Value = (object?)header.Get("BUKRS")  ?? DBNull.Value });
        cmd.Parameters.Add(new OracleParameter("p_year",   OracleDbType.Varchar2) { Value = (object?)header.Get("YEAR")   ?? DBNull.Value });
        cmd.Parameters.Add(new OracleParameter("p_period", OracleDbType.Varchar2) { Value = (object?)header.Get("PERIOD") ?? DBNull.Value });
        cmd.Parameters.Add(new OracleParameter("p_day",    OracleDbType.Varchar2) { Value = (object?)header.Get("DAY")    ?? DBNull.Value });
        cmd.Parameters.Add(new OracleParameter("p_werks",  OracleDbType.Varchar2) { Value = (object?)header.Get("WERKS")  ?? DBNull.Value });

        await cmd.ExecuteNonQueryAsync(ct);

        using var refCursor = (OracleRefCursor)ret.Value;
        using var reader = refCursor.GetDataReader();

        // Bay: package loi -> cursor chi co 1 cot MSG = 'Loi: ORA-...'
        if (reader.FieldCount == 1 &&
            string.Equals(reader.GetName(0), "MSG", StringComparison.OrdinalIgnoreCase))
        {
            var msg = reader.Read() ? reader.GetValue(0)?.ToString() : "unknown";
            throw new InvalidOperationException($"PKG_DYNAMIC_EXPORT: {msg}");
        }

        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private static void WriteHeaderRow(IXLWorksheet ws, int row, List<BieuMauColumnConfig> columns)
    {
        foreach (var c in columns.Where(c => c.ExcelColIndex > 0))
        {
            var cell = ws.Cell(row, c.ExcelColIndex);
            cell.Value = c.ColTitle ?? c.BieumauCol;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    /// <summary>Rot du lieu; tra ve dong cuoi co du lieu.</summary>
    private static int WriteDataRows(
        IXLWorksheet ws, int startRow, DataTable table, Dictionary<string, int> colIndexByName)
    {
        var row = startRow;

        foreach (DataRow dr in table.Rows)
        {
            foreach (DataColumn dc in table.Columns)
            {
                var name = dc.ColumnName;
                var val = dr[dc];

                // FORMAT -> luon o cot AAA, khong chiem cot hien thi.
                if (string.Equals(name, "FORMAT", StringComparison.OrdinalIgnoreCase))
                {
                    if (val != DBNull.Value)
                        ws.Cell(row, FormatColumn).Value = val.ToString();
                    continue;
                }

                if (colIndexByName.TryGetValue(name, out var colIdx) && val != DBNull.Value)
                    SetCell(ws.Cell(row, colIdx), val);
            }
            row++;
        }

        return row - 1;
    }

    private static void SetCell(IXLCell cell, object val)
    {
        switch (val)
        {
            case null or DBNull:
                break;
            case decimal or double or float or int or long or short:
                cell.Value = Convert.ToDouble(val);
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            default:
                cell.Value = val.ToString();
                break;
        }
    }

    /// <summary>Conditional formatting theo cot AAA: B=dam, I=nghieng, IB/BI=dam+nghieng.</summary>
    private static void ApplyFormatConditionalFormatting(IXLWorksheet ws, int startRow, int endRow)
    {
        if (endRow < startRow) return;

        // Ap cho toan vung hien thi (cot 1..AAA-1) de dong user nhap them cung an dinh dang.
        var lastVisibleCol = Math.Max(1, XLHelper.GetColumnNumberFromLetter(FormatColumn) - 1);
        var range = ws.Range(startRow, 1, endRow, lastVisibleCol);
        var fmtColRef = $"${FormatColumn}{startRow}";

        range.AddConditionalFormat().WhenIsTrue($"={fmtColRef}=\"B\"").Font.SetBold(true);
        range.AddConditionalFormat().WhenIsTrue($"={fmtColRef}=\"I\"").Font.SetItalic(true);
        range.AddConditionalFormat().WhenIsTrue($"=OR({fmtColRef}=\"IB\",{fmtColRef}=\"BI\")")
             .Font.SetBold(true).Font.SetItalic(true);
    }

    private static string SafeSheetName(string name)
    {
        var s = name.Length > 31 ? name[..31] : name;
        foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Sheet1" : s;
    }
}
