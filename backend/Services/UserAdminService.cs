using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

public interface IUserAdminService
{
    /// <summary>
    /// Mot trang danh sach nguoi dung.
    /// <para><paramref name="q"/> loc theo username/ho ten;
    /// <paramref name="bukrs"/> loc theo don vi da gan truc tiep;
    /// <paramref name="unassignedOnly"/> chi lay nguoi dung CHUA gan don vi nao (khi bat, no
    /// thang the <paramref name="bukrs"/>).</para>
    /// </summary>
    Task<PagedResult<UserListItem>> ListAsync(
        string? q, bool includeInactive, string? bukrs, bool unassignedOnly,
        int page, int pageSize, CancellationToken ct);
    Task<UserListItem?> GetAsync(long id, CancellationToken ct);
    Task<long> CreateAsync(CreateUserRequest req, string actor, CancellationToken ct);
    Task UpdateAsync(long id, UpdateUserRequest req, string actor, CancellationToken ct);
    Task ChangePasswordAsync(long id, string newPassword, string actor, CancellationToken ct);
    Task AssignBukrsAsync(long id, AssignBukrsRequest req, string actor, CancellationToken ct);

    /// <summary>Danh muc don vi chuan (mac dinh T001 cua APEX) — dung cho dropdown gan BUKRS.</summary>
    Task<List<OrgItem>> ListOrgsAsync(CancellationToken ct);
}

/// <summary>
/// Quan tri PT_USER + gan don vi PT_USER_ORG tren schema PT_APP.
/// Cot audit (CREATED_BY/UPDATED_BY/UPDATED_AT) chi duoc ghi neu bang that co cot do —
/// kiem qua USER_TAB_COLUMNS nhu cach <see cref="BieuMauConfigService"/> lam, tranh ORA-00904.
/// </summary>
public sealed class UserAdminService : IUserAdminService
{
    private readonly IOracleConnectionFactory _factory;
    private readonly IBieuMauConfigService _schema;
    private readonly IOrgCatalogService _catalog;
    private readonly AuthOptions _auth;

    public UserAdminService(
        IOracleConnectionFactory factory, IBieuMauConfigService schema,
        IOrgCatalogService catalog, IOptions<AuthOptions> auth)
    {
        _factory = factory;
        _schema = schema;
        _catalog = catalog;
        _auth = auth.Value;
    }

    private Task<OracleConnection> OpenAsync(CancellationToken ct)
        => _factory.OpenAsync(_auth.UserConnId, ct);

    // ---------------------------------------------------------------- doc

    /// <summary>
    /// Dieu kien loc dung chung cho ca dem tong va lay trang, de hai cau khong lech nhau.
    /// <para>Loc theo BUKRS la khop TRUC TIEP trong PT_USER_ORG — dung nhung gi cot
    /// "DON VI (BUKRS)" tren bang dang hien, khong mo rong xuong cay con. Nho vay loc theo
    /// cai gi thi thay dung cai do.</para>
    /// </summary>
    private const string ListWhere = @"
            WHERE (:q IS NULL
                   OR UPPER(u.USERNAME)           LIKE '%' || UPPER(:q) || '%'
                   OR UPPER(NVL(u.FULL_NAME,' ')) LIKE '%' || UPPER(:q) || '%')
              AND (:inc = 'Y' OR u.IS_ACTIVE = 'Y')
              AND (:bukrs IS NULL OR EXISTS (
                       SELECT 1 FROM PT_USER_ORG uo
                       WHERE uo.USER_ID = u.ID AND UPPER(uo.BUKRS) = UPPER(:bukrs)))
              AND (:noorg = 'N' OR NOT EXISTS (
                       SELECT 1 FROM PT_USER_ORG uo
                       WHERE uo.USER_ID = u.ID AND uo.BUKRS IS NOT NULL))";

    public async Task<PagedResult<UserListItem>> ListAsync(
        string? q, bool includeInactive, string? bukrs, bool unassignedOnly,
        int page, int pageSize, CancellationToken ct)
    {
        const string countSql = "SELECT COUNT(*) FROM PT_USER u" + ListWhere;

        // ORDER BY USERNAME du de xac dinh thu tu vi USERNAME la UNIQUE — khong co nguy co
        // mot ban ghi xuat hien o 2 trang hoac bi bo qua giua cac trang.
        const string pageSql = @"
            SELECT u.ID, u.USERNAME, u.FULL_NAME, u.EMAIL, u.IS_ACTIVE
            FROM PT_USER u" + ListWhere + @"
            ORDER BY u.USERNAME
            OFFSET :off ROWS FETCH NEXT :lim ROWS ONLY";

        var filter = NullIfBlank(q);
        var inc = includeInactive ? "Y" : "N";

        // "Chua gan don vi" va "loc theo 1 don vi" loai tru nhau: chon cai truoc thi bo cai sau,
        // khong thi ra tap rong ma nguoi dung khong hieu tai sao.
        var org = unassignedOnly ? null : NullIfBlank(bukrs);
        var noorg = unassignedOnly ? "Y" : "N";

        void Bind(OracleCommand cmd)
        {
            cmd.Parameters.Add("q", (object?)filter ?? DBNull.Value);
            cmd.Parameters.Add("inc", inc);
            cmd.Parameters.Add("bukrs", (object?)org ?? DBNull.Value);
            cmd.Parameters.Add("noorg", noorg);
        }

        await using var conn = await OpenAsync(ct);

        int total;
        await using (var cmd = new OracleCommand(countSql, conn) { BindByName = true })
        {
            Bind(cmd);
            total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        // Dem tong TRUOC de keo duoc trang vuot qua cuoi ve trang cuoi.
        var (effPage, effSize, offset) = Paging.Normalize(page, pageSize, total);

        var result = new PagedResult<UserListItem> { Page = effPage, PageSize = effSize, Total = total };
        if (total == 0)
            return result;

        await using (var cmd = new OracleCommand(pageSql, conn) { BindByName = true })
        {
            Bind(cmd);
            cmd.Parameters.Add("off", offset);
            cmd.Parameters.Add("lim", effSize);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                result.Items.Add(ReadUser(rd));
        }

        if (result.Items.Count > 0)
            await FillBukrsAndRolesAsync(conn, result.Items, ct);

        return result;
    }

    public async Task<UserListItem?> GetAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT ID, USERNAME, FULL_NAME, EMAIL, IS_ACTIVE FROM PT_USER WHERE ID = :id";

        await using var conn = await OpenAsync(ct);

        UserListItem? user = null;
        await using (var cmd = new OracleCommand(sql, conn) { BindByName = true })
        {
            cmd.Parameters.Add("id", id);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))
                user = ReadUser(rd);
        }

        if (user is null)
            return null;

        var one = new List<UserListItem> { user };
        await FillBukrsAndRolesAsync(conn, one, ct);
        return user;
    }

    /// <summary>
    /// Danh muc don vi lay tu NGUON CHUAN (<see cref="IOrgCatalogService"/> — mac dinh T001 cua
    /// APEX), khong con doc PT_T001. Nho vay ma don vi o day luon trung voi ma ma APEX ghi vao
    /// <c>H_DATA.BUKRS</c>, khong the lech.
    /// <para>T001 khong co cot cha-con nen danh muc PHANG. <see cref="OrderAsTree"/> giu lai de
    /// dung neu sau nay chuyen sang nguon co phan cap.</para>
    /// </summary>
    public Task<List<OrgItem>> ListOrgsAsync(CancellationToken ct)
        => _catalog.ListAsync(ct);

    /// <summary>
    /// Sap xep cha truoc con va tinh <see cref="OrgItem.Level"/> de frontend thut le.
    /// Ban ghi co PARENT_ID tro vao don vi khong ton tai/khong active duoc coi nhu goc,
    /// nen khong bao gio bi mat khoi danh sach.
    /// </summary>
    public static List<OrgItem> OrderAsTree(List<OrgItem> flat)
    {
        var byId = flat.ToDictionary(o => o.Id);
        var children = new Dictionary<long, List<OrgItem>>();
        var roots = new List<OrgItem>();

        foreach (var o in flat)
        {
            if (o.ParentId is long p && p != o.Id && byId.ContainsKey(p))
            {
                if (!children.TryGetValue(p, out var list))
                    children[p] = list = new List<OrgItem>();
                list.Add(o);
            }
            else
            {
                roots.Add(o);
            }
        }

        var result = new List<OrgItem>(flat.Count);
        var seen = new HashSet<long>();

        void Walk(OrgItem node, int level)
        {
            if (!seen.Add(node.Id)) return; // chan vong lap cha-con neu du lieu loi
            node.Level = level;
            result.Add(node);
            if (children.TryGetValue(node.Id, out var kids))
                foreach (var kid in kids) Walk(kid, level + 1);
        }

        foreach (var root in roots) Walk(root, 0);

        // Con sot lai = nam trong mot vong lap -> van phai tra ve, khong duoc am tham bo.
        foreach (var o in flat.Where(o => !seen.Contains(o.Id)))
        {
            o.Level = 0;
            result.Add(o);
        }

        return result;
    }

    // ---------------------------------------------------------------- ghi

    public async Task<long> CreateAsync(CreateUserRequest req, string actor, CancellationToken ct)
    {
        var username = (req.Username ?? string.Empty).Trim();
        if (username.Length == 0)
            throw new InvalidOperationException("Thieu ten dang nhap.");

        var hash = PasswordHasher.Hash(req.Password);

        await using var conn = await OpenAsync(ct);
        var userCols = await _schema.GetTableColumnsAsync(conn, "PT_USER", ct);

        using var tx = conn.BeginTransaction();
        try
        {
            if (await UsernameExistsAsync(conn, tx, username, ct))
                throw new InvalidOperationException($"Ten dang nhap '{username}' da ton tai.");

            var cols = new List<string> { "USERNAME", "PASSWORD_HASH", "FULL_NAME", "EMAIL", "IS_ACTIVE" };
            var vals = new List<string> { ":u", ":p", ":f", ":e", ":a" };
            var binds = new List<OracleParameter>
            {
                new("u", username),
                new("p", hash),
                new("f", (object?)NullIfBlank(req.FullName) ?? DBNull.Value),
                new("e", (object?)NullIfBlank(req.Email) ?? DBNull.Value),
                new("a", req.IsActive ? "Y" : "N")
            };

            if (userCols.Contains("CREATED_BY"))
            {
                cols.Add("CREATED_BY");
                vals.Add(":by");
                binds.Add(new OracleParameter("by", actor));
            }

            var sql = $@"INSERT INTO PT_USER ({string.Join(", ", cols)})
                         VALUES ({string.Join(", ", vals)})
                         RETURNING ID INTO :newId";

            long newId;
            await using (var ins = new OracleCommand(sql, conn) { Transaction = tx, BindByName = true })
            {
                foreach (var p in binds) ins.Parameters.Add(p);
                var outId = new OracleParameter("newId", OracleDbType.Decimal)
                    { Direction = System.Data.ParameterDirection.Output };
                ins.Parameters.Add(outId);

                await ins.ExecuteNonQueryAsync(ct);
                newId = ToInt64(outId.Value);
            }

            await ReplaceBukrsAsync(conn, tx, newId, req.Bukrs, req.PrimaryBukrs, ct);

            tx.Commit();
            return newId;
        }
        catch
        {
            SafeRollback(tx);
            throw;
        }
    }

    public async Task UpdateAsync(long id, UpdateUserRequest req, string actor, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var userCols = await _schema.GetTableColumnsAsync(conn, "PT_USER", ct);

        var sets = new List<string> { "FULL_NAME = :f", "EMAIL = :e", "IS_ACTIVE = :a" };
        var binds = new List<OracleParameter>
        {
            new("f", (object?)NullIfBlank(req.FullName) ?? DBNull.Value),
            new("e", (object?)NullIfBlank(req.Email) ?? DBNull.Value),
            new("a", req.IsActive ? "Y" : "N")
        };
        AddAuditSets(userCols, sets, binds, actor);

        var sql = $"UPDATE PT_USER SET {string.Join(", ", sets)} WHERE ID = :id";

        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        foreach (var p in binds) cmd.Parameters.Add(p);
        cmd.Parameters.Add("id", id);

        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            throw new KeyNotFoundException($"Khong tim thay nguoi dung ID={id}.");
    }

    public async Task ChangePasswordAsync(long id, string newPassword, string actor, CancellationToken ct)
    {
        var hash = PasswordHasher.Hash(newPassword);

        await using var conn = await OpenAsync(ct);
        var userCols = await _schema.GetTableColumnsAsync(conn, "PT_USER", ct);

        var sets = new List<string> { "PASSWORD_HASH = :p" };
        var binds = new List<OracleParameter> { new("p", hash) };
        AddAuditSets(userCols, sets, binds, actor);

        var sql = $"UPDATE PT_USER SET {string.Join(", ", sets)} WHERE ID = :id";

        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        foreach (var p in binds) cmd.Parameters.Add(p);
        cmd.Parameters.Add("id", id);

        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            throw new KeyNotFoundException($"Khong tim thay nguoi dung ID={id}.");
    }

    public async Task AssignBukrsAsync(long id, AssignBukrsRequest req, string actor, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        using var tx = conn.BeginTransaction();
        try
        {
            await using (var chk = new OracleCommand(
                "SELECT COUNT(*) FROM PT_USER WHERE ID = :id", conn)
                { Transaction = tx, BindByName = true })
            {
                chk.Parameters.Add("id", id);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) == 0)
                    throw new KeyNotFoundException($"Khong tim thay nguoi dung ID={id}.");
            }

            await ReplaceBukrsAsync(conn, tx, id, req.Bukrs, req.PrimaryBukrs, ct);
            tx.Commit();
        }
        catch
        {
            SafeRollback(tx);
            throw;
        }
    }

    /// <summary>
    /// Thay TOAN BO danh sach don vi cua user: xoa het roi ghi lai theo <paramref name="bukrsList"/>.
    /// <para>Ma don vi duoc validate voi DANH MUC CHUAN (<see cref="IOrgCatalogService"/> — mac dinh
    /// la T001 cua APEX), khong phai voi PT_T001. Ma la -> nem loi, khong bo qua im lang.</para>
    /// <para>Ghi vao cot <c>PT_USER_ORG.BUKRS</c>. Van ghi kem <c>ORG_ID</c> khi ma do tinh co co
    /// trong PT_T001, de backend Java (doc ORG_ID trong ScopeService) tiep tuc chay dung voi cac ma
    /// no biet — xem db/01_pt_user_org_bukrs.sql.</para>
    /// </summary>
    private async Task ReplaceBukrsAsync(
        OracleConnection conn, OracleTransaction tx, long userId,
        IEnumerable<string>? bukrsList, string? primaryBukrs, CancellationToken ct)
    {
        var wanted = (bukrsList ?? Enumerable.Empty<string>())
            .Select(b => (b ?? string.Empty).Trim())
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = NullIfBlank(primaryBukrs);
        if (primary is not null && !wanted.Contains(primary, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"BUKRS chinh '{primary}' khong nam trong danh sach don vi da chon.");

        // Khong chon don vi chinh nhung co danh sach -> lay cai dau tien lam chinh.
        primary ??= wanted.FirstOrDefault();

        await using (var del = new OracleCommand(
            "DELETE FROM PT_USER_ORG WHERE USER_ID = :id", conn)
            { Transaction = tx, BindByName = true })
        {
            del.Parameters.Add("id", userId);
            await del.ExecuteNonQueryAsync(ct);
        }

        if (wanted.Count == 0)
            return;

        // Validate voi danh muc chuan TRUOC khi ghi, de bao dung ma nao sai.
        var valid = await _catalog.ListCodesAsync(ct);
        var unknown = wanted.Where(b => !valid.Contains(b)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Ma don vi khong co trong danh muc chuan: {string.Join(", ", unknown)}.");
        }

        var orgCols = await _schema.GetTableColumnsAsync(conn, "PT_USER_ORG", ct);
        if (!orgCols.Contains("BUKRS"))
        {
            throw new InvalidOperationException(
                "Bang PT_USER_ORG chua co cot BUKRS. Chay db/01_pt_user_org_bukrs.sql truoc.");
        }

        // ORG_ID chi ghi khi con cot do VA tra duoc ID trong PT_T001; khong tra duoc thi de NULL.
        var withOrgId = orgCols.Contains("ORG_ID");
        var insSql = withOrgId
            ? @"INSERT INTO PT_USER_ORG (USER_ID, BUKRS, IS_PRIMARY, ORG_ID)
                VALUES (:uid, :bukrs, :pri,
                        (SELECT MIN(t.ID) FROM PT_T001 t WHERE UPPER(t.BUKRS) = UPPER(:bukrs)))"
            : @"INSERT INTO PT_USER_ORG (USER_ID, BUKRS, IS_PRIMARY)
                VALUES (:uid, :bukrs, :pri)";

        foreach (var bukrs in wanted)
        {
            await using var ins = new OracleCommand(insSql, conn)
                { Transaction = tx, BindByName = true };
            ins.Parameters.Add("uid", userId);
            ins.Parameters.Add("bukrs", bukrs);
            ins.Parameters.Add("pri",
                string.Equals(bukrs, primary, StringComparison.OrdinalIgnoreCase) ? "Y" : "N");

            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    // ---------------------------------------------------------------- helper

    private static async Task<bool> UsernameExistsAsync(
        OracleConnection conn, OracleTransaction tx, string username, CancellationToken ct)
    {
        await using var cmd = new OracleCommand(
            "SELECT COUNT(*) FROM PT_USER WHERE UPPER(USERNAME) = UPPER(:u)", conn)
            { Transaction = tx, BindByName = true };
        cmd.Parameters.Add("u", username);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>Gioi han cua danh sach IN trong Oracle la 1000 phan tu -> chia lo.</summary>
    private const int InListChunk = 1000;

    /// <summary>
    /// Do BUKRS + ROLE_CODE vao danh sach user da doc, LOC theo dung cac user do
    /// (khong quet ca PT_USER_ORG / PT_USER_ROLE).
    /// </summary>
    private static async Task FillBukrsAndRolesAsync(
        OracleConnection conn, List<UserListItem> users, CancellationToken ct)
    {
        var bukrs = users.ToDictionary(u => u.Id, _ => new List<string>());
        var roles = users.ToDictionary(u => u.Id, _ => new List<string>());

        // {0} duoc thay bang danh sach bind :u0,:u1,... — chi sinh tu ID so nguyen doc tu DB,
        // khong nhan gi tu client, nen khong phai mat phoi injection.
        // Doc BUKRS THANG tu PT_USER_ORG, khong join PT_T001 nua — danh muc chuan gio la
        // T001 cua APEX, PT_T001 chi con la di san cho backend Java.
        const string bukrsSql = @"
            SELECT uo.USER_ID, uo.BUKRS
            FROM PT_USER_ORG uo
            WHERE uo.USER_ID IN ({0}) AND uo.BUKRS IS NOT NULL
            ORDER BY uo.USER_ID, NVL(uo.IS_PRIMARY,'N') DESC, uo.BUKRS";

        const string roleSql = @"
            SELECT ur.USER_ID, r.ROLE_CODE
            FROM PT_USER_ROLE ur JOIN PT_ROLE r ON r.ID = ur.ROLE_ID
            WHERE r.IS_ACTIVE = 'Y' AND ur.USER_ID IN ({0})
            ORDER BY ur.USER_ID, r.ROLE_CODE";

        var ids = users.Select(u => u.Id).ToList();
        await ReadPairsAsync(conn, bukrsSql, ids, bukrs, ct);
        await ReadPairsAsync(conn, roleSql, ids, roles, ct);

        foreach (var u in users)
        {
            u.Bukrs = bukrs[u.Id];
            u.Roles = roles[u.Id];
        }
    }

    private static async Task ReadPairsAsync(
        OracleConnection conn, string sqlTemplate, List<long> userIds,
        Dictionary<long, List<string>> sink, CancellationToken ct)
    {
        for (var offset = 0; offset < userIds.Count; offset += InListChunk)
        {
            var chunk = userIds.Skip(offset).Take(InListChunk).ToList();
            var binds = string.Join(", ", chunk.Select((_, i) => $":u{i}"));

            await using var cmd = new OracleCommand(string.Format(sqlTemplate, binds), conn)
                { BindByName = true };
            for (var i = 0; i < chunk.Count; i++)
                cmd.Parameters.Add($"u{i}", chunk[i]);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var userId = Convert.ToInt64(rd.GetValue(0));
                if (sink.TryGetValue(userId, out var list) && !rd.IsDBNull(1))
                    list.Add(rd.GetString(1).Trim());
            }
        }
    }

    private static UserListItem ReadUser(OracleDataReader rd) => new()
    {
        Id       = Convert.ToInt64(rd.GetValue(0)),
        Username = rd.GetString(1),
        FullName = rd.IsDBNull(2) ? null : rd.GetString(2),
        Email    = rd.IsDBNull(3) ? null : rd.GetString(3),
        IsActive = !rd.IsDBNull(4) && rd.GetString(4).Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)
    };

    private static void AddAuditSets(
        IReadOnlySet<string> cols, List<string> sets, List<OracleParameter> binds, string actor)
    {
        if (cols.Contains("UPDATED_BY"))
        {
            sets.Add("UPDATED_BY = :by");
            binds.Add(new OracleParameter("by", actor));
        }
        if (cols.Contains("UPDATED_AT"))
            sets.Add("UPDATED_AT = SYSDATE");
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static long ToInt64(object? value) => value switch
    {
        null or DBNull => 0L,
        Oracle.ManagedDataAccess.Types.OracleDecimal od => (long)od.Value,
        _ => Convert.ToInt64(value)
    };

    private static void SafeRollback(OracleTransaction tx)
    {
        try { tx.Rollback(); } catch { /* connection da dong: khong con gi de rollback */ }
    }
}
