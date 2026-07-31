using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ToolExcel.Api.Models;

namespace ToolExcel.Api.Services;

/// <summary>Cau hinh JWT. Key phai >= 32 byte cho HS256.</summary>
public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ToolExcel.Api";
    public string Audience { get; set; } = "ToolExcel.Client";
    public int TtlSeconds { get; set; } = 28800; // 8 gio
}

public interface IJwtTokenService
{
    (string token, int expiresIn) Issue(UserInfo user);
}

/// <summary>Phat JWT HS256: sub = username, claim 'roles' = danh sach ROLE_CODE.</summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> opt)
    {
        _opt = opt.Value;
        if (Encoding.UTF8.GetByteCount(_opt.Key) < 32)
            throw new InvalidOperationException(
                "Jwt:Key phai >= 32 byte cho HS256. Dat trong appsettings.Local.json.");
    }

    public (string token, int expiresIn) Issue(UserInfo user)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        foreach (var role in user.Roles)
            claims.Add(new Claim("roles", role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(_opt.TtlSeconds),
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, _opt.TtlSeconds);
    }
}
