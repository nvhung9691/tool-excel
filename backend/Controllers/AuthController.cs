using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;
using ToolExcel.Api.Services;

namespace ToolExcel.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserAuthService _users;
    private readonly IJwtTokenService _jwt;
    private readonly IUserScopeService _scope;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserAuthService users, IJwtTokenService jwt, IUserScopeService scope,
        ILogger<AuthController> logger)
    {
        _users = users;
        _jwt = jwt;
        _scope = scope;
        _logger = logger;
    }

    /// <summary>Dang nhap web: tra user + token. POST body { username, password }.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        try
        {
            var user = await Authenticate(req, ct);
            if (user is null)
                return Unauthorized(new { error = "Sai tai khoan hoac mat khau." });

            var (token, ttl) = _jwt.Issue(user);
            return Ok(new LoginResponse { User = user, AccessToken = token, ExpiresIn = ttl });
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            return DbUnavailable(ex);
        }
    }

    /// <summary>
    /// Client may (vd APEX): tra token kem danh sach BUKRS duoc phep, de ben goi biet
    /// pham vi cua minh truoc khi goi /api/bieumau/*.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] LoginRequest req, CancellationToken ct)
    {
        try
        {
            var user = await Authenticate(req, ct);
            if (user is null)
                return Unauthorized(new { error = "Sai tai khoan hoac mat khau." });

            var (token, ttl) = _jwt.Issue(user);
            return Ok(new TokenResponse
            {
                AccessToken = token,
                ExpiresIn = ttl,
                AllowedBukrs = user.AllowedBukrs
            });
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            return DbUnavailable(ex);
        }
    }

    /// <summary>Ho so nguoi dung hien tai (can Bearer token).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var username = User.Identity?.Name ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        try
        {
            var user = await _users.GetByUsernameAsync(username, ct);
            if (user is null)
                return Unauthorized();

            await FillScopeAsync(user, ct);
            return Ok(user);
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            return DbUnavailable(ex);
        }
    }

    /// <summary>JWT stateless: server khong luu gi, client tu xoa token.</summary>
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();

    private async Task<UserInfo?> Authenticate(LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return null;

        UserInfo? user;
        try
        {
            user = await _users.ValidateAsync(req.Username.Trim(), req.Password, ct);
        }
        catch (NotSupportedException ex)
        {
            // Hash sai dinh dang / scheme la -> loi cau hinh du lieu, khong phai sai mat khau.
            _logger.LogError(ex, "PASSWORD_HASH sai dinh dang cho user {User}", req.Username);
            throw;
        }

        if (user is not null)
            await FillScopeAsync(user, ct);

        return user;
    }

    private async Task FillScopeAsync(UserInfo user, CancellationToken ct)
    {
        var allowed = await _scope.GetAllowedBukrsAsync(user.Username, user.Roles, ct);
        user.AllowedBukrs = allowed?.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Oracle chet/khong toi duoc -> 503, khong phai 500, va khong lot stack trace.</summary>
    private IActionResult DbUnavailable(Exception ex)
    {
        _logger.LogError(ex, "Khong ket noi duoc CSDL xac thuc");
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { error = "Khong ket noi duoc CSDL xac thuc. Lien he quan tri he thong." });
    }
}
