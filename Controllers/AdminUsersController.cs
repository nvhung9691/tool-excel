using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Models;
using ToolExcel.Api.Services;

namespace ToolExcel.Api.Controllers;

/// <summary>
/// Quan tri nguoi dung + gan don vi (BUKRS). Chi ADMIN/SUPER vao duoc.
/// Man nay KHONG sua vai tro (PT_USER_ROLE) — xem README, muc gioi han.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN,SUPER")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _users;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(IUserAdminService users, ILogger<AdminUsersController> logger)
    {
        _users = users;
        _logger = logger;
    }

    private string Actor => User.Identity?.Name ?? User.FindFirstValue("sub") ?? "unknown";

    /// <summary>Danh muc don vi PT_T001 cho dropdown gan BUKRS (da sap xep theo cay).</summary>
    [HttpGet("orgs")]
    public Task<IActionResult> ListOrgs(CancellationToken ct)
        => Run(async () => Ok(await _users.ListOrgsAsync(ct)));

    /// <summary>Danh sach nguoi dung. <paramref name="q"/> loc theo username/ho ten.</summary>
    [HttpGet("users")]
    public Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] bool includeInactive = true, CancellationToken ct = default)
        => Run(async () => Ok(await _users.ListAsync(q, includeInactive, ct)));

    [HttpGet("users/{id:long}")]
    public Task<IActionResult> Get(long id, CancellationToken ct)
        => Run(async () =>
        {
            var user = await _users.GetAsync(id, ct);
            return user is null
                ? NotFound(new { error = $"Khong tim thay nguoi dung ID={id}." })
                : Ok(user);
        });

    /// <summary>Tao nguoi dung. Mat khau duoc hash ra {bcrypt} truoc khi ghi.</summary>
    [HttpPost("users")]
    public Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
        => Run(async () =>
        {
            var id = await _users.CreateAsync(req, Actor, ct);
            var created = await _users.GetAsync(id, ct);
            return CreatedAtAction(nameof(Get), new { id }, created);
        });

    /// <summary>Sua ho ten/email/trang thai. Tat tai khoan = IS_ACTIVE='N' (xoa mem).</summary>
    [HttpPut("users/{id:long}")]
    public Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest req, CancellationToken ct)
        => Run(async () =>
        {
            await _users.UpdateAsync(id, req, Actor, ct);
            return Ok(await _users.GetAsync(id, ct));
        });

    /// <summary>Quan tri dat lai mat khau (khong can mat khau cu).</summary>
    [HttpPost("users/{id:long}/password")]
    public Task<IActionResult> ChangePassword(
        long id, [FromBody] ChangePasswordRequest req, CancellationToken ct)
        => Run(async () =>
        {
            await _users.ChangePasswordAsync(id, req?.NewPassword ?? string.Empty, Actor, ct);
            return NoContent();
        });

    /// <summary>
    /// Gan lai TOAN BO danh sach don vi cho user (replace, khong phai them).
    /// Day chinh la du lieu ma /api/bieumau/* dung de chan h_BUKRS.
    /// </summary>
    [HttpPut("users/{id:long}/bukrs")]
    public Task<IActionResult> AssignBukrs(
        long id, [FromBody] AssignBukrsRequest req, CancellationToken ct)
        => Run(async () =>
        {
            await _users.AssignBukrsAsync(id, req ?? new AssignBukrsRequest(), Actor, ct);
            return Ok(await _users.GetAsync(id, ct));
        });

    /// <summary>
    /// Gom cach xu ly loi cho ca controller: loi nghiep vu -> 400/404,
    /// Oracle khong toi duoc -> 503 (khong de lot stack trace ra client).
    /// </summary>
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OracleException ex)
        {
            _logger.LogError(ex, "Loi Oracle khi quan tri nguoi dung");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Khong ket noi duoc CSDL. Lien he quan tri he thong." });
        }
    }
}
