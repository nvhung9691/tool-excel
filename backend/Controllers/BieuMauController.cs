using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using ToolExcel.Api.Data;
using ToolExcel.Api.Models;
using ToolExcel.Api.Services;

namespace ToolExcel.Api.Controllers;

[ApiController]
[Route("api/bieumau")]
public sealed class BieuMauController : ControllerBase
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IExcelExportService _export;
    private readonly IExcelImportService _import;
    private readonly IUserScopeService _scope;
    private readonly ILogger<BieuMauController> _logger;

    public BieuMauController(
        IExcelExportService export, IExcelImportService import, IUserScopeService scope,
        ILogger<BieuMauController> logger)
    {
        _export = export;
        _import = import;
        _scope = scope;
        _logger = logger;
    }

    /// <summary>
    /// Chan theo pham vi don vi: h_BUKRS phai nam trong PT_USER_ORG cua user goi
    /// (da mo rong xuong cay con). Tra ve null = duoc phep, khac null = ket qua loi de tra ngay.
    /// <para>Doc DB tuoi moi lan goi nen thu quyen o man quan tri co hieu luc ngay,
    /// khong phai cho token het han.</para>
    /// </summary>
    private async Task<IActionResult?> DenyIfOutOfScopeAsync(HeaderParams header, CancellationToken ct)
    {
        var username = User.Identity?.Name ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { error = "Unauthorized" });

        var roles = User.FindAll("roles").Select(c => c.Value).ToList();

        IReadOnlySet<string>? allowed;
        try
        {
            allowed = await _scope.GetAllowedBukrsAsync(username, roles, ct);
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            // Ham nay chay TRUOC khoi try cua endpoint nen phai tu bat, khong thi ra 500.
            return DbUnavailable(ex, "(kiem tra pham vi don vi)");
        }

        var bukrs = header.Get("BUKRS");
        var decision = BukrsScope.Decide(allowed, bukrs);

        if (decision == ScopeDecision.Allow)
            return null;

        if (decision == ScopeDecision.MissingBukrs)
            return BadRequest(new { error = "Thieu tham so h_BUKRS." });

        // Con lai la Forbidden. Decide() chi tra ve gia tri nay khi allowed != null
        // va bukrs co gia tri, nen 2 dau '!' duoi day la an toan.
        var scope = allowed!;
        var requested = bukrs!.Trim();

        _logger.LogWarning(
            "User {User} bi tu choi BUKRS={Bukrs} (pham vi: {Allowed})",
            username, requested, scope.Count == 0 ? "(chua gan don vi nao)" : string.Join(",", scope));

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = $"Tai khoan khong duoc phep don vi BUKRS='{requested}'.",
            allowedBukrs = scope.OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToArray()
        });
    }

    /// <summary>Oracle chet/khong toi duoc -> 503, khong phai 500, va khong lot stack trace.</summary>
    private IActionResult DbUnavailable(Exception ex, string formCode)
    {
        _logger.LogError(ex, "Khong ket noi duoc CSDL khi xu ly form={FormCode}", formCode);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { error = "Khong ket noi duoc CSDL. Lien he quan tri he thong." });
    }

    /// <summary>
    /// Tai template Excel dong cho 1 bieu mau.
    /// VD: GET /api/bieumau/KH18/export?connId=PB9&amp;h_BUKRS=2100&amp;h_YEAR=2026&amp;h_PERIOD=7
    /// </summary>
    [HttpGet("{formCode}/export")]
    public async Task<IActionResult> Export(
        string formCode, [FromQuery] string? connId, CancellationToken ct)
    {
        var header = HeaderParams.FromQuery(Request.Query);

        var denied = await DenyIfOutOfScopeAsync(header, ct);
        if (denied is not null)
            return denied;

        try
        {
            var bytes = await _export.ExportTemplateAsync(connId, formCode, header, ct);
            var fileName = $"{formCode}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
            return File(bytes, XlsxContentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Export loi form={FormCode}", formCode);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            return DbUnavailable(ex, formCode);
        }
    }

    /// <summary>
    /// Upload file Excel da nhap lieu -> ghi H_DATA/T_DATA.
    /// VD: POST /api/bieumau/KH18/import?connId=PB9&amp;h_BUKRS=2100&amp;h_YEAR=2026&amp;h_PERIOD=7 (multipart 'file')
    /// </summary>
    [HttpPost("{formCode}/import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Import(
        string formCode, IFormFile file, [FromQuery] string? connId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Chua co file upload (field 'file')." });

        var header = HeaderParams.FromQuery(Request.Query);

        var denied = await DenyIfOutOfScopeAsync(header, ct);
        if (denied is not null)
            return denied;

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _import.ImportAsync(connId, formCode, header, stream, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Import loi form={FormCode}", formCode);
            return BadRequest(new ImportResult { Success = false, FormCode = formCode, Message = ex.Message });
        }
        catch (Exception ex) when (ex is OracleException or DbUnavailableException)
        {
            return DbUnavailable(ex, formCode);
        }
    }
}
