using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

/// <summary>
/// Nguon danh muc don vi. Mac dinh doc T001 cua schema APEX (dong bo tu SAP) —
/// dung bang ma H_DATA.BUKRS lay gia tri, nen khong bao gio lech voi du lieu that.
/// </summary>
public sealed class OrgCatalogOptions
{
    /// <summary>connId tro toi schema chua bang danh muc. Rong = DefaultConnId.</summary>
    public string? ConnId { get; set; } = "PB9";

    public string Table { get; set; } = "T001";
    public string CodeColumn { get; set; } = "BUKRS";
    public string NameColumn { get; set; } = "BUTXT";

    /// <summary>Cot loc ban ghi con hieu luc (vd 'IS_ACTIVE'). Rong = khong loc.</summary>
    public string? ActiveColumn { get; set; }

    public string ActiveValue { get; set; } = "Y";
}

public interface IOrgCatalogService
{
    /// <summary>Danh muc don vi, sap theo ma. Ten bang/cot lay tu cau hinh 'Org'.</summary>
    Task<List<OrgItem>> ListAsync(CancellationToken ct);

    /// <summary>Tap ma don vi hop le — dung de validate truoc khi gan cho nguoi dung.</summary>
    Task<IReadOnlySet<string>> ListCodesAsync(CancellationToken ct);
}

public sealed class OrgCatalogService : IOrgCatalogService
{
    /// <summary>
    /// Ten bang/cot duoc GHEP vao SQL (khong bind duoc ten doi tuong), nen phai chan
    /// truoc. Cho phep chu, so, gach duoi, $ # va dau . cho dang SCHEMA.BANG.
    /// </summary>
    private static readonly Regex SafeIdentifier = new(@"^[A-Za-z][A-Za-z0-9_$#]*(\.[A-Za-z][A-Za-z0-9_$#]*)?$",
        RegexOptions.Compiled);

    private readonly IOracleConnectionFactory _factory;
    private readonly OrgCatalogOptions _opt;
    private readonly ILogger<OrgCatalogService> _logger;

    public OrgCatalogService(
        IOracleConnectionFactory factory, IOptions<OrgCatalogOptions> opt,
        ILogger<OrgCatalogService> logger)
    {
        _factory = factory;
        _opt = opt.Value;
        _logger = logger;

        foreach (var (name, value) in new[]
                 {
                     ("Org:Table", _opt.Table),
                     ("Org:CodeColumn", _opt.CodeColumn),
                     ("Org:NameColumn", _opt.NameColumn),
                 })
        {
            if (!SafeIdentifier.IsMatch(value ?? string.Empty))
                throw new InvalidOperationException(
                    $"Cau hinh {name}='{value}' khong phai ten doi tuong Oracle hop le.");
        }

        if (!string.IsNullOrWhiteSpace(_opt.ActiveColumn) && !SafeIdentifier.IsMatch(_opt.ActiveColumn))
            throw new InvalidOperationException(
                $"Cau hinh Org:ActiveColumn='{_opt.ActiveColumn}' khong hop le.");
    }

    public async Task<List<OrgItem>> ListAsync(CancellationToken ct)
    {
        await using var conn = await _factory.OpenAsync(_opt.ConnId, ct);

        // Ten cot cau hinh co the khong ton tai o moi truong khac -> kiem truoc de bao loi
        // ro rang thay vi ORA-00904 tho.
        var cols = await GetColumnsAsync(conn, _opt.Table, ct);
        Require(cols, _opt.CodeColumn, "Org:CodeColumn");

        // BUTXT/ActiveColumn la tuy chon: thieu thi bo qua chu khong lam vo ca danh muc.
        var hasName = cols.Contains(_opt.NameColumn);
        var hasActive = !string.IsNullOrWhiteSpace(_opt.ActiveColumn) && cols.Contains(_opt.ActiveColumn!);

        if (!hasName)
        {
            _logger.LogWarning(
                "Bang {Table} khong co cot {Col} — danh muc don vi se chi co ma, khong co ten.",
                _opt.Table, _opt.NameColumn);
        }

        var nameExpr = hasName ? _opt.NameColumn : "NULL";
        var where = hasActive ? $"WHERE NVL({_opt.ActiveColumn}, :act) = :act" : string.Empty;

        var sql = $@"
            SELECT {_opt.CodeColumn} AS CODE, {nameExpr} AS NAME
            FROM {_opt.Table}
            {where}
            ORDER BY {_opt.CodeColumn}";

        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        if (hasActive) cmd.Parameters.Add("act", _opt.ActiveValue);

        var list = new List<OrgItem>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var code = rd.IsDBNull(0) ? null : rd.GetString(0).Trim();
            if (string.IsNullOrEmpty(code)) continue;

            list.Add(new OrgItem
            {
                // T001 CO cot PARENT (VARCHAR2(10), tro toi mot BUKRS khac), nhung danh muc o
                // day co y dung PHANG: Id = 0, ParentId = null, Level = 0 — vi OrgItem.ParentId
                // la ID so, khong nhan duoc ma dang chuoi. Pham vi don vi vi vay la khop chinh
                // xac tung ma, khong con "gan don vi cha duoc ca don vi con" nhu hoi dung PT_T001.
                // Muon dung lai phan cap thi phai doc them PARENT va noi cay theo MA, khong phai ID.
                Id = 0,
                Bukrs = code,
                Butxt = rd.IsDBNull(1) ? string.Empty : rd.GetString(1).Trim(),
                OrgType = null,
                ParentId = null,
                Level = 0,
            });
        }

        return list;
    }

    public async Task<IReadOnlySet<string>> ListCodesAsync(CancellationToken ct)
    {
        var orgs = await ListAsync(ct);
        return new HashSet<string>(orgs.Select(o => o.Bukrs), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Doc cot that cua bang — ho tro ca dang SCHEMA.BANG.</summary>
    private static async Task<HashSet<string>> GetColumnsAsync(
        OracleConnection conn, string table, CancellationToken ct)
    {
        var parts = table.Split('.', 2);
        var owner = parts.Length == 2 ? parts[0].ToUpperInvariant() : null;
        var name = (parts.Length == 2 ? parts[1] : parts[0]).ToUpperInvariant();

        var sql = owner is null
            ? "SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = :t"
            : "SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = :t AND OWNER = :o";

        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        cmd.Parameters.Add("t", name);
        if (owner is not null) cmd.Parameters.Add("o", owner);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            set.Add(rd.GetString(0));
        return set;
    }

    private void Require(HashSet<string> cols, string column, string settingName)
    {
        if (cols.Count == 0)
        {
            throw new InvalidOperationException(
                $"Khong thay bang '{_opt.Table}' qua connId='{_opt.ConnId}'. " +
                "Kiem cau hinh 'Org:ConnId' / 'Org:Table' va quyen SELECT cua user ket noi.");
        }

        if (!cols.Contains(column))
        {
            throw new InvalidOperationException(
                $"Bang '{_opt.Table}' khong co cot '{column}' ({settingName}). " +
                $"Cac cot co: {string.Join(", ", cols.Order())}");
        }
    }
}
