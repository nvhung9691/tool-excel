using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;

namespace ToolExcel.Api.Services;

/// <summary>Ket qua kiem tra 1 gia tri BUKRS so voi pham vi cua user.</summary>
public enum ScopeDecision
{
    /// <summary>Duoc phep di tiep.</summary>
    Allow,

    /// <summary>Thieu tham so h_BUKRS ma user lai bi gioi han pham vi -> khong the kiem.</summary>
    MissingBukrs,

    /// <summary>BUKRS khong nam trong pham vi -> 403.</summary>
    Forbidden
}

public interface IUserScopeService
{
    /// <summary>
    /// Cac BUKRS user duoc phep tac dong, mo rong xuong toan bo cay con.
    /// <para><c>null</c> = KHONG gioi han (co vai tro SUPER).</para>
    /// <para>Tap rong = khong duoc phep BUKRS nao.</para>
    /// Hai trang thai nay khac nhau, dung gop lam mot.
    /// </summary>
    Task<IReadOnlySet<string>?> GetAllowedBukrsAsync(
        string username, IEnumerable<string> roles, CancellationToken ct);
}

/// <summary>
/// Phan quyet dinh cua viec chan BUKRS, tach rieng khoi DB va HTTP de test duoc.
/// Day la logic bao mat cot loi cua <c>/api/bieumau/*</c>.
/// </summary>
public static class BukrsScope
{
    public static ScopeDecision Decide(IReadOnlySet<string>? allowed, string? bukrs)
    {
        if (allowed is null)            // SUPER: khong gioi han, khong can h_BUKRS
            return ScopeDecision.Allow;

        if (string.IsNullOrWhiteSpace(bukrs))
            return ScopeDecision.MissingBukrs;

        // Pham vi rong (chua gan don vi nao) roi vao day -> Forbidden, dung Allow.
        return allowed.Contains(bukrs.Trim()) ? ScopeDecision.Allow : ScopeDecision.Forbidden;
    }
}

/// <summary>
/// Doc pham vi don vi tu PT_USER_ORG -> PT_T001 tren schema PT_APP.
/// Doc DB tuoi moi lan goi (khong nhet vao JWT) de thu quyen la co hieu luc ngay,
/// khong phai cho token het han.
/// </summary>
public sealed class UserScopeService : IUserScopeService
{
    /// <summary>Vai tro duoc bo qua moi kiem tra pham vi.</summary>
    public const string SuperRole = "SUPER";

    // Mo rong xuong cay con: gan don vi cha thi duoc ca cac don vi truc thuoc.
    private const string Sql = @"
        SELECT t.BUKRS
        FROM PT_T001 t
        WHERE t.ID IN (
            SELECT ID FROM PT_T001
            START WITH ID IN (
                SELECT uo.ORG_ID FROM PT_USER_ORG uo
                JOIN PT_USER u ON u.ID = uo.USER_ID
                WHERE UPPER(u.USERNAME) = UPPER(:u) AND u.IS_ACTIVE = 'Y'
            )
            CONNECT BY NOCYCLE PRIOR ID = PARENT_ID
        )
        AND t.IS_ACTIVE = 'Y'";

    private readonly IOracleConnectionFactory _factory;
    private readonly AuthOptions _auth;

    public UserScopeService(IOracleConnectionFactory factory, IOptions<AuthOptions> auth)
    {
        _factory = factory;
        _auth = auth.Value;
    }

    public async Task<IReadOnlySet<string>?> GetAllowedBukrsAsync(
        string username, IEnumerable<string> roles, CancellationToken ct)
    {
        if (roles.Any(r => string.Equals(r, SuperRole, StringComparison.OrdinalIgnoreCase)))
            return null;

        await using var conn = await _factory.OpenAsync(_auth.UserConnId, ct);

        await using var cmd = new OracleCommand(Sql, conn) { BindByName = true };
        cmd.Parameters.Add("u", username);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            if (!rd.IsDBNull(0))
                set.Add(rd.GetString(0).Trim());

        return set;
    }
}
