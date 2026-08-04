using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

public interface IBieuMauConfigService
{
    /// <summary>Danh sach bieu mau cho dropdown chon FORM_CODE.</summary>
    Task<List<BieuMauListItem>> ListBieuMauAsync(OracleConnection conn, CancellationToken ct);

    Task<BieuMauInfo?> GetBieuMauAsync(OracleConnection conn, string formCode, CancellationToken ct);
    Task<List<BieuMauColumnConfig>> GetColumnsAsync(OracleConnection conn, string formCode, CancellationToken ct);
    Task<HashSet<string>> GetTableColumnsAsync(OracleConnection conn, string tableName, CancellationToken ct);
}

/// <summary>Doc DM_BIEU_MAU + DM_BIEU_MAU_CONFIG de dieu khien mapping dong.</summary>
public sealed class BieuMauConfigService : IBieuMauConfigService
{
    public async Task<List<BieuMauListItem>> ListBieuMauAsync(OracleConnection conn, CancellationToken ct)
    {
        // Kem SO_COT_CAU_HINH: bieu mau khong co dong nao trong DM_BIEU_MAU_CONFIG thi
        // export ra file trong — hien so nay de nhin thay ngay thay vi thu roi doan.
        const string sql = @"
            SELECT m.FORM_CODE,
                   MAX(m.TEN_BIEU_MAU)                                  AS TEN_BIEU_MAU,
                   MAX(m.ROW_EXCEL)                                     AS ROW_EXCEL,
                   MAX(NVL(m.IS_ACTIVE, 'Y'))                           AS IS_ACTIVE,
                   (SELECT COUNT(*) FROM DM_BIEU_MAU_CONFIG c
                     WHERE c.FORM_CODE = m.FORM_CODE)                    AS SO_COT_CAU_HINH
            FROM   DM_BIEU_MAU m
            WHERE  m.FORM_CODE IS NOT NULL
            GROUP BY m.FORM_CODE
            ORDER BY m.FORM_CODE";

        using var cmd = new OracleCommand(sql, conn);

        var list = new List<BieuMauListItem>();
        using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new BieuMauListItem
            {
                FormCode     = rd.GetString(0),
                TenBieuMau   = rd.IsDBNull(1) ? null : rd.GetString(1).Trim(),
                RowExcel     = rd.IsDBNull(2) ? null : Convert.ToInt32(rd.GetValue(2)),
                IsActive     = rd.IsDBNull(3) ||
                               rd.GetString(3).Trim().Equals("Y", StringComparison.OrdinalIgnoreCase),
                SoCotCauHinh = Convert.ToInt32(rd.GetValue(4)),
            });
        }
        return list;
    }

    public async Task<BieuMauInfo?> GetBieuMauAsync(OracleConnection conn, string formCode, CancellationToken ct)
    {
        const string sql = @"
            SELECT FORM_CODE, TEN_BIEU_MAU, NVL(ROW_EXCEL, 1) AS ROW_EXCEL
            FROM   DM_BIEU_MAU
            WHERE  FORM_CODE = :formCode";

        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;
        cmd.Parameters.Add("formCode", formCode);

        using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return null;

        return new BieuMauInfo
        {
            FormCode = rd.GetString(0),
            TenBieuMau = rd.IsDBNull(1) ? null : rd.GetString(1),
            RowExcel = Convert.ToInt32(rd.GetValue(2))
        };
    }

    public async Task<List<BieuMauColumnConfig>> GetColumnsAsync(OracleConnection conn, string formCode, CancellationToken ct)
    {
        const string sql = @"
            SELECT EXCEL_COL, BIEUMAU_COL, HEADER, VITRI, COL_TITLE, STT
            FROM   DM_BIEU_MAU_CONFIG
            WHERE  FORM_CODE = :formCode
            ORDER BY STT NULLS LAST";

        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;
        cmd.Parameters.Add("formCode", formCode);

        var list = new List<BieuMauColumnConfig>();
        using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            list.Add(new BieuMauColumnConfig
            {
                ExcelCol   = rd.IsDBNull(0) ? null : rd.GetString(0),
                BieumauCol = rd.GetString(1),
                Header     = rd.IsDBNull(2) ? null : rd.GetString(2),
                ViTri      = rd.IsDBNull(3) ? null : rd.GetString(3),
                ColTitle   = rd.IsDBNull(4) ? null : rd.GetString(4),
                Stt        = rd.IsDBNull(5) ? null : Convert.ToInt32(rd.GetValue(5))
            });
        }
        return list;
    }

    /// <summary>Lay danh sach cot thuc te cua bang (USER_TAB_COLUMNS) de tranh bay ORA-00904.</summary>
    public async Task<HashSet<string>> GetTableColumnsAsync(OracleConnection conn, string tableName, CancellationToken ct)
    {
        const string sql = @"
            SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tbl";

        using var cmd = new OracleCommand(sql, conn);
        cmd.BindByName = true;
        cmd.Parameters.Add("tbl", tableName.ToUpperInvariant());

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            set.Add(rd.GetString(0));
        return set;
    }
}
