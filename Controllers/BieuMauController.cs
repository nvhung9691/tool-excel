using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<BieuMauController> _logger;

    public BieuMauController(
        IExcelExportService export, IExcelImportService import, ILogger<BieuMauController> logger)
    {
        _export = export;
        _import = import;
        _logger = logger;
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
    }
}
