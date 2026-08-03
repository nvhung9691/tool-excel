using System.Data;
using System.Text.RegularExpressions;
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

    private const double MaxColumnWidth = 45;
    private const double MinColumnWidth = 6;

    /// <summary>Dia chi o kieu 'B2' trong DM_BIEU_MAU_CONFIG.VITRI.</summary>
    private static readonly Regex CellRef =
        new(@"^([A-Za-z]{1,3})([1-9][0-9]{0,6})$", RegexOptions.Compiled);

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

        // ROW_EXCEL = dong bat dau vung du lieu chi tiet; dong tieu de cot = ROW_EXCEL - 1.
        var dataStartRow = Math.Max(1, bieuMau.RowExcel);
        var headerRow = Math.Max(1, dataStartRow - 1);
        var lastCol = colIndexByName.Values.DefaultIfEmpty(1).Max();

        WriteHeaderBlock(ws, bieuMau, columns, header, dataStartRow, lastCol);
        WriteHeaderRow(ws, headerRow, columns);

        var numberFormats = BuildNumberFormats(table, colIndexByName);
        var lastDataRow = WriteDataRows(ws, dataStartRow, table, colIndexByName, numberFormats);

        ApplyFormatConditionalFormatting(ws, dataStartRow, lastDataRow);
        StyleTable(ws, headerRow, dataStartRow, lastDataRow, lastCol);
        AdjustColumns(ws, lastCol);

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

    /// <summary>
    /// Ghi khoi header phia tren vung du lieu: ten bieu mau + cac tham so h_* vao dung o VITRI.
    /// <para>KHONG phai trang tri. Khi upload, <c>ExcelImportService.ValidateViTri</c> doc lai
    /// chinh cac o nay va so voi tham so h_*; de trong thi file tai ve KHONG upload lai duoc
    /// (400 "File khong khop tham so").</para>
    /// </summary>
    private void WriteHeaderBlock(
        IXLWorksheet ws, BieuMauInfo bieuMau, List<BieuMauColumnConfig> columns,
        HeaderParams header, int dataStartRow, int lastCol)
    {
        // Ten bieu mau o dong 1, gop het be ngang vung hien thi. Chi lam khi con cho phia tren
        // vung du lieu — bieu mau khong khai ROW_EXCEL thi du lieu bat dau ngay dong 1.
        if (dataStartRow > 1 && !string.IsNullOrWhiteSpace(bieuMau.TenBieuMau))
        {
            var title = ws.Cell(1, 1);
            title.Value = bieuMau.TenBieuMau;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (lastCol > 1) ws.Range(1, 1, 1, lastCol).Merge();
        }

        foreach (var c in columns.Where(c => c.IsHeader && !string.IsNullOrWhiteSpace(c.ViTri)))
        {
            var m = CellRef.Match(c.ViTri!.Trim());
            if (!m.Success)
            {
                _logger.LogWarning(
                    "Bieu mau {Form}: VITRI '{ViTri}' cua {Col} khong phai dia chi o hop le, bo qua.",
                    bieuMau.FormCode, c.ViTri, c.BieumauCol);
                continue;
            }

            var rowNo = int.Parse(m.Groups[2].Value);
            var colNo = XLHelper.GetColumnNumberFromLetter(m.Groups[1].Value.ToUpperInvariant());

            // Cau hinh tro vao vung du lieu thi bo qua — de len du lieu con te hon la thieu header.
            if (rowNo >= dataStartRow)
            {
                _logger.LogWarning(
                    "Bieu mau {Form}: VITRI {ViTri} cua {Col} nam trong vung du lieu " +
                    "(ROW_EXCEL={Row}) nen bo qua. File tai ve se khong upload lai duoc.",
                    bieuMau.FormCode, c.ViTri, c.BieumauCol, dataStartRow);
                continue;
            }

            // Nhan mo ta dat o cot ben trai cho de doc; GIA TRI phai dung o VITRI vi import doc o do.
            if (colNo > 1 && !string.IsNullOrWhiteSpace(c.ColTitle))
            {
                var label = ws.Cell(rowNo, colNo - 1);
                label.Value = c.ColTitle;
                label.Style.Font.Bold = true;
            }

            // Luon ghi dang chuoi: BUKRS co the co so 0 dung dau, ep sang so la mat.
            var cell = ws.Cell(rowNo, colNo);
            cell.Style.NumberFormat.Format = "@";
            cell.Value = header.Get(c.BieumauCol) ?? string.Empty;
        }
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

    /// <summary>
    /// Chon dinh dang so cho tung cot, quyet dinh MOT LAN theo ca cot de cac o trong cung cot
    /// hien thi giong nhau: cot toan so nguyen -> "#,##0"; co phan le -> "#,##0.00".
    /// </summary>
    private static Dictionary<int, string> BuildNumberFormats(
        DataTable table, Dictionary<string, int> colIndexByName)
    {
        var formats = new Dictionary<int, string>();

        foreach (DataColumn dc in table.Columns)
        {
            if (!colIndexByName.TryGetValue(dc.ColumnName, out var colIdx)) continue;

            var anyNumber = false;
            var anyFraction = false;

            foreach (DataRow dr in table.Rows)
            {
                if (dr[dc] is not (decimal or double or float or int or long or short)) continue;

                anyNumber = true;
                var d = Convert.ToDouble(dr[dc]);
                if (Math.Abs(d - Math.Truncate(d)) > 1e-9) { anyFraction = true; break; }
            }

            if (anyNumber) formats[colIdx] = anyFraction ? "#,##0.00" : "#,##0";
        }

        return formats;
    }

    /// <summary>Rot du lieu; tra ve dong cuoi co du lieu.</summary>
    private static int WriteDataRows(
        IXLWorksheet ws, int startRow, DataTable table, Dictionary<string, int> colIndexByName,
        IReadOnlyDictionary<int, string> numberFormats)
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
                {
                    var cell = ws.Cell(row, colIdx);
                    SetCell(cell, val);

                    if (cell.DataType == XLDataType.Number &&
                        numberFormats.TryGetValue(colIdx, out var fmt))
                    {
                        cell.Style.NumberFormat.Format = fmt;
                    }
                }
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

    /// <summary>Ke khung vung tieu de/du lieu va co dinh dong tieu de khi cuon.</summary>
    private static void StyleTable(
        IXLWorksheet ws, int headerRow, int dataStartRow, int lastDataRow, int lastCol)
    {
        if (lastCol < 1) return;

        var head = ws.Range(headerRow, 1, headerRow, lastCol);
        head.Style.Alignment.WrapText = true;
        head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        head.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        head.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        if (lastDataRow >= dataStartRow)
        {
            var body = ws.Range(dataStartRow, 1, lastDataRow, lastCol);
            body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // Cuon xuong van thay tieu de cot.
        ws.SheetView.FreezeRows(headerRow);
    }

    /// <summary>
    /// Gian cot theo noi dung nhung chan tran — khong chan thi cot 'Ten chi tieu' keo dai
    /// qua man hinh. An cot FORMAT vi do la co dieu khien noi bo, khong phai du lieu.
    /// </summary>
    private static void AdjustColumns(IXLWorksheet ws, int lastCol)
    {
        ws.Columns(1, lastCol).AdjustToContents();

        foreach (var col in ws.Columns(1, lastCol))
        {
            if (col.Width > MaxColumnWidth) col.Width = MaxColumnWidth;
            if (col.Width < MinColumnWidth) col.Width = MinColumnWidth;
        }

        ws.Column(FormatColumn).Hide();
    }

    private static string SafeSheetName(string name)
    {
        var s = name.Length > 31 ? name[..31] : name;
        foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Sheet1" : s;
    }
}
