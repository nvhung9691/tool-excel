using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolExcel.Api.Models;
using ToolExcel.Api.Services;

namespace ToolExcel.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserAuthService _users;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserAuthService users, IJwtTokenService jwt, ILogger<AuthController> logger)
    {
        _users = users;
        _jwt = jwt;
        _logger = logger;
    }

    /// <summary>Dang nhap web: tra user + token. POST body { username, password }.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await Authenticate(req, ct);
        if (user is null)
            return Unauthorized(new { error = "Sai tai khoan hoac mat khau." });

        var (token, ttl) = _jwt.Issue(user);
        return Ok(new LoginResponse { User = user, AccessToken = token, ExpiresIn = ttl });
    }

    /// <summary>Client may (vd APEX): chi tra token.</summary>
    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await Authenticate(req, ct);
        if (user is null)
            return Unauthorized(new { error = "Sai tai khoan hoac mat khau." });

        var (token, ttl) = _jwt.Issue(user);
        return Ok(new TokenResponse { AccessToken = token, ExpiresIn = ttl });
    }

    /// <summary>Ho so nguoi dung hien tai (can Bearer token).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var username = User.Identity?.Name ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var user = await _users.GetByUsernameAsync(username, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>JWT stateless: server khong luu gi, client tu xoa token.</summary>
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();

    private async Task<UserInfo?> Authenticate(LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return null;

        try
        {
            return await _users.ValidateAsync(req.Username.Trim(), req.Password, ct);
        }
        catch (NotSupportedException ex)
        {
            // Hash sai dinh dang / scheme la -> loi cau hinh du lieu, khong phai sai mat khau.
            _logger.LogError(ex, "PASSWORD_HASH sai dinh dang cho user {User}", req.Username);
            throw;
        }
    }
}
