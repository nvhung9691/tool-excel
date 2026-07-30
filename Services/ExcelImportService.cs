using System.Globalization;
using ClosedXML.Excel;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

public interface IExcelImportService
{
    Task<ImportResult> ImportAsync(
        string? connId, string formCode, HeaderParams header, Stream fileStream, CancellationToken ct);
}

/// <summary>
/// Doc file Excel tu ROW_EXCEL, validate o VITRI, ghi thang H_DATA/T_DATA trong 1 transaction.
/// Mapping cot hoan toan theo DM_BIEU_MAU_CONFIG (khong hardcode).
/// </summary>
public sealed class ExcelImportService : IExcelImportService
{
    private readonly IOracleConnectionFactory _factory;
    private readonly IBieuMauConfigService _config;
    private readonly ILogger<ExcelImportService> _logger;

    public ExcelImportService(
        IOracleConnectionFactory factory,
        IBieuMauConfigService config,
        ILogger<ExcelImportService> logger)
    {
        _factory = factory;
        _config = config;
        _logger = logger;
    }

    public async Task<ImportResult> ImportAsync(
        string? connId, string formCode, HeaderParams header, Stream fileStream, CancellationToken ct)
    {
        using var conn = _factory.Create(connId);
        await conn.OpenAsync(ct);

        var bieuMau = await _config.GetBieuMauAsync(conn, formCode, ct)
            ?? throw new KeyNotFoundException($"Khong tim thay bieu mau FORM_CODE='{formCode}'.");
        var columns = await _config.GetColumnsAsync(conn, formCode, ct);

        // Cot thuc te cua H_DATA/T_DATA -> tranh bay ORA-00904 khi config sai ten cot.
        var hDataCols = await _config.GetTableColumnsAsync(conn, "H_DATA", ct);
        var tDataCols = await _config.GetTableColumnsAsync(conn, "T_DATA", ct);

        using var wb = new XLWorkbook(fileStream);
        var ws = wb.Worksheets.First();

        ValidateViTri(ws, columns, header);

        var detailCols = columns
            .Where(c => !c.IsHeader && c.ExcelColIndex > 0 &&
                        tDataCols.Contains(c.BieumauCol))
            .ToList();

        var rows = ReadDetailRows(ws, bieuMau.RowExcel, detailCols);

        using var tx = conn.BeginTransaction();
        try
        {
            var headerId = await UpsertHeaderAsync(conn, tx, formCode, header, hDataCols, ct);
            var inserted = await InsertDetailsAsync(conn, tx, headerId, rows, ct);
            tx.Commit();

            return new ImportResult
            {
                Success = true,
                FormCode = formCode,
                HeaderId = headerId,
                DetailRows = inserted,
                Message = $"Da nap {inserted} dong vao T_DATA (HEADER_ID={headerId})."
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Doc o VITRI trong file, so voi tham so h_*; lech -> loi.</summary>
    private static void ValidateViTri(IXLWorksheet ws, List<BieuMauColumnConfig> columns, HeaderParams header)
    {
        foreach (var c in columns.Where(c => c.IsHeader && !string.IsNullOrWhiteSpace(c.ViTri)))
        {
            var expected = header.Get(c.BieumauCol);
            if (string.IsNullOrWhiteSpace(expected)) continue;

            var actual = ws.Cell(c.ViTri!).GetString().Trim();
            if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"File khong khop tham so: o {c.ViTri} ({c.BieumauCol}) = '{actual}', " +
                    $"tham so h_{c.BieumauCol} = '{expected}'.");
            }
        }
    }

    private static List<Dictionary<string, object?>> ReadDetailRows(
        IXLWorksheet ws, int rowExcel, List<BieuMauColumnConfig> detailCols)
    {
        var result = new List<Dictionary<string, object?>>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (var r = Math.Max(1, rowExcel); r <= lastRow; r++)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var hasValue = false;

            foreach (var c in detailCols)
            {
                var cell = ws.Cell(r, c.ExcelColIndex);
                if (cell.IsEmpty()) continue;

                dict[c.BieumauCol] = ConvertCell(cell);
                hasValue = true;
            }

            if (hasValue) result.Add(dict);
        }
        return result;
    }

    private static object? ConvertCell(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble();
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        var s = cell.GetString().Trim();
        if (string.IsNullOrEmpty(s)) return null;

        // Thu ep so (cot GT/SL) khi cell luu dang text.
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return num;
        return s;
    }

    /// <summary>
    /// Tra H_DATA theo FORM_CODE+BUKRS+YEAR+PERIOD+DAY.
    /// STATUS='D' -> dung lai ID + xoa T_DATA cu; STATUS&lt;&gt;'D' -> bao loi; chua co -> tao moi.
    /// </summary>
    private async Task<long> UpsertHeaderAsync(
        OracleConnection conn, OracleTransaction tx, string formCode,
        HeaderParams header, HashSet<string> hDataCols, CancellationToken ct)
    {
        const string findSql = @"
            SELECT ID, STATUS FROM H_DATA
            WHERE FORM_CODE = :formCode
              AND NVL(BUKRS,'~')  = NVL(:bukrs,'~')
              AND NVL(YEAR,-1)    = NVL(:year,-1)
              AND NVL(PERIOD,-1)  = NVL(:period,-1)
              AND NVL(DAY,-1)     = NVL(:day,-1)";

        long? existingId = null;
        string? status = null;

        using (var find = new OracleCommand(findSql, conn) { Transaction = tx, BindByName = true })
        {
            find.Parameters.Add("formCode", formCode);
            find.Parameters.Add("bukrs",  (object?)header.Get("BUKRS") ?? DBNull.Value);
            find.Parameters.Add("year",   ToNum(header.Get("YEAR")));
            find.Parameters.Add("period", ToNum(header.Get("PERIOD")));
            find.Parameters.Add("day",    ToNum(header.Get("DAY")));

            using var rd = await find.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))
            {
                existingId = Convert.ToInt64(rd.GetValue(0));
                status = rd.IsDBNull(1) ? null : rd.GetString(1);
            }
        }

        if (existingId is not null)
        {
            if (!string.Equals(status, "D", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Phieu da qua nhap (STATUS='{status}'). Phai huy duyet moi upload lai.");

            using var del = new OracleCommand(
                "DELETE FROM T_DATA WHERE HEADER_ID = :id", conn)
                { Transaction = tx, BindByName = true };
            del.Parameters.Add("id", existingId.Value);
            await del.ExecuteNonQueryAsync(ct);
            return existingId.Value;
        }

        // Tao header moi: bo qua ID de cot tu sinh theo DEFAULT (APEX.H_DATA_SEQ.NEXTVAL),
        // roi RETURNING ID de lay lai. Chi ghi cac cot header co that trong H_DATA.
        var cols = new List<string> { "FORM_CODE", "STATUS" };
        var vals = new List<string> { ":formCode", "'D'" };
        var binds = new List<OracleParameter> { new("formCode", formCode) };

        foreach (var key in new[] { "BUKRS", "YEAR", "PERIOD", "DAY", "WERKS" })
        {
            var v = header.Get(key);
            if (v is null || !hDataCols.Contains(key)) continue;

            cols.Add(key);
            vals.Add($":{key}");
            var isNumeric = key is "YEAR" or "PERIOD" or "DAY";
            binds.Add(new OracleParameter(key, isNumeric ? ToNum(v) : v));
        }

        var insertSql = $@"
            INSERT INTO H_DATA ({string.Join(", ", cols)})
            VALUES ({string.Join(", ", vals)})
            RETURNING ID INTO :newId";

        using (var ins = new OracleCommand(insertSql, conn) { Transaction = tx, BindByName = true })
        {
            foreach (var p in binds) ins.Parameters.Add(p);
            var newId = new OracleParameter("newId", OracleDbType.Decimal)
                { Direction = System.Data.ParameterDirection.Output };
            ins.Parameters.Add(newId);

            await ins.ExecuteNonQueryAsync(ct);
            return ToInt64(newId.Value);
        }
    }

    /// <summary>Chuyen gia tri OUT param (OracleDecimal/long/decimal) ve long an toan.</summary>
    private static long ToInt64(object? value) => value switch
    {
        null or DBNull => 0L,
        Oracle.ManagedDataAccess.Types.OracleDecimal od => (long)od.Value,
        _ => Convert.ToInt64(value)
    };

    private static async Task<int> InsertDetailsAsync(
        OracleConnection conn, OracleTransaction tx, long headerId,
        List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        var count = 0;
        foreach (var row in rows)
        {
            // T_DATA.ID la IDENTITY -> khong truyen. CREATED_BY/AT do trigger set.
            var cols = new List<string> { "HEADER_ID" };
            var vals = new List<string> { ":headerId" };
            using var cmd = new OracleCommand { Connection = conn, Transaction = tx, BindByName = true };
            cmd.Parameters.Add("headerId", headerId);

            var i = 0;
            foreach (var kv in row)
            {
                var p = $"p{i++}";
                cols.Add(kv.Key);
                vals.Add($":{p}");
                cmd.Parameters.Add(new OracleParameter(p, kv.Value ?? DBNull.Value));
            }

            cmd.CommandText =
                $"INSERT INTO T_DATA ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
            await cmd.ExecuteNonQueryAsync(ct);
            count++;
        }
        return count;
    }

    private static object ToNum(string? s)
        => int.TryParse(s, out var n) ? n : DBNull.Value;
}
