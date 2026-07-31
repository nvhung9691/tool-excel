using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

/// <summary>Cau hinh xac thuc: dung connId nao de doc PT_USER/PT_ROLE.</summary>
public sealed class AuthOptions
{
    public string UserConnId { get; set; } = "PTAPP";
}

public interface IUserAuthService
{
    /// <summary>Kiem tra tai khoan + mat khau. Dung -> UserInfo (kem roles); sai -> null.</summary>
    Task<UserInfo?> ValidateAsync(string username, string password, CancellationToken ct);

    /// <summary>Lay ho so + roles theo username (khong kiem mat khau).</summary>
    Task<UserInfo?> GetByUsernameAsync(string username, CancellationToken ct);
}

/// <summary>
/// Doc PT_USER / PT_ROLE / PT_USER_ROLE cua schema PT_APP (qua ket nol rieng, khong dung APEX).
/// Chi lay ban ghi active; PASSWORD_HASH giu nguyen ca tien to cho <see cref="PasswordVerifier"/>.
/// </summary>
public sealed class UserAuthService : IUserAuthService
{
    private const string UserSql = @"SELECT ID, USERNAME, PASSWORD_HASH, FULL_NAME, EMAIL
                                     FROM PT_USER WHERE USERNAME = :u AND IS_ACTIVE = 'Y'";

    private const string RoleSql = @"SELECT r.ROLE_CODE FROM PT_USER_ROLE ur
                                     JOIN PT_ROLE r ON r.ID = ur.ROLE_ID
                                     WHERE ur.USER_ID = :id AND r.IS_ACTIVE = 'Y'";

    private readonly IOracleConnectionFactory _factory;
    private readonly AuthOptions _auth;

    public UserAuthService(IOracleConnectionFactory factory, IOptions<AuthOptions> auth)
    {
        _factory = factory;
        _auth = auth.Value;
    }

    public async Task<UserInfo?> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var (user, hash) = await LoadUserAsync(username, ct);
        if (user is null)
            return null;

        return PasswordVerifier.Verify(password, hash) ? user : null;
    }

    public async Task<UserInfo?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        var (user, _) = await LoadUserAsync(username, ct);
        return user;
    }

    private async Task<(UserInfo? user, string? hash)> LoadUserAsync(string username, CancellationToken ct)
    {
        await using var conn = _factory.Create(_auth.UserConnId);
        await conn.OpenAsync(ct);

        UserInfo? user = null;
        string? hash = null;
        long userId = 0;

        await using (var cmd = new OracleCommand(UserSql, conn) { BindByName = true })
        {
            cmd.Parameters.Add(new OracleParameter("u", username));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                userId = Convert.ToInt64(r["ID"]);
                hash = r["PASSWORD_HASH"] as string;
                user = new UserInfo
                {
                    Id = userId,
                    Username = r["USERNAME"] as string ?? username,
                    FullName = r["FULL_NAME"] as string,
                    Email = r["EMAIL"] as string
                };
            }
        }

        if (user is null)
            return (null, null);

        var roles = new List<string>();
        await using (var cmd = new OracleCommand(RoleSql, conn) { BindByName = true })
        {
            cmd.Parameters.Add(new OracleParameter("id", userId));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                roles.Add((r["ROLE_CODE"] as string ?? string.Empty).Trim());
        }
        user.Roles = roles;

        return (user, hash);
    }
}
