using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace ToolExcel.Api.Data;

/// <summary>Cau hinh nhieu nguon CSDL theo connId (mo phong bang PT_CONNECTION cua Tool_Portal).</summary>
public sealed class OracleConnectionOptions
{
    public string DefaultConnId { get; set; } = "PB9";

    /// <summary>
    /// Gioi han thoi gian cho MO ket noi (giay). 0 hoac am = khong gioi han.
    /// <para>Day la LUOI AN TOAN, khong phai cach chinh. 'Connection Timeout' trong connection
    /// string KHONG duoc ODP.NET ton trong khi host khong toi duoc — do thuc te la 60 giay.</para>
    /// <para>Cach chinh la dat timeout o tang TNS ngay trong Data Source, do la thu duy nhat lam
    /// driver tu bo som (do duoc: 3.4 giay thay vi 60):</para>
    /// <code>
    /// Data Source=(DESCRIPTION=(TRANSPORT_CONNECT_TIMEOUT=3)(CONNECT_TIMEOUT=5)(RETRY_COUNT=0)
    ///             (ADDRESS=(PROTOCOL=TCP)(HOST=...)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=...)))
    /// </code>
    /// Giu ca hai vi neu ai do khai connection string khong co TNS timeout thi lop nay van chan
    /// duoc, khong de request treo tron 60 giay.
    /// </summary>
    public int OpenTimeoutSeconds { get; set; } = 10;

    public Dictionary<string, ConnectionEntry> Connections { get; set; } = new();

    public sealed class ConnectionEntry
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

/// <summary>
/// CSDL khong san sang: khong mo duoc ket noi trong thoi gian cho phep, hoac Oracle bao loi
/// ngay luc mo. Controller map thanh HTTP 503 (khong phai 500).
/// </summary>
public sealed class DbUnavailableException : Exception
{
    public DbUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IOracleConnectionFactory
{
    /// <summary>Tao doi tuong connection (CHUA mo) theo connId. Null/empty -> dung DefaultConnId.</summary>
    OracleConnection Create(string? connId = null);

    /// <summary>
    /// Tao va MO ket noi, co gioi han thoi gian theo <see cref="OracleConnectionOptions.OpenTimeoutSeconds"/>.
    /// Khong mo duoc -> nem <see cref="DbUnavailableException"/>.
    /// </summary>
    Task<OracleConnection> OpenAsync(string? connId, CancellationToken ct);
}

public sealed class OracleConnectionFactory : IOracleConnectionFactory
{
    private readonly OracleConnectionOptions _options;
    private readonly ILogger<OracleConnectionFactory> _logger;

    public OracleConnectionFactory(
        IOptions<OracleConnectionOptions> options, ILogger<OracleConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public OracleConnection Create(string? connId = null)
    {
        var id = string.IsNullOrWhiteSpace(connId) ? _options.DefaultConnId : connId;

        if (!_options.Connections.TryGetValue(id, out var entry) ||
            string.IsNullOrWhiteSpace(entry.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Khong tim thay cau hinh ket noi cho connId='{id}'. Kiem tra section 'Oracle:Connections'.");
        }

        return new OracleConnection(entry.ConnectionString);
    }

    public async Task<OracleConnection> OpenAsync(string? connId, CancellationToken ct)
    {
        var conn = Create(connId);
        var timeout = _options.OpenTimeoutSeconds;

        if (timeout <= 0)
        {
            try
            {
                await conn.OpenAsync(ct);
                return conn;
            }
            catch (OracleException ex)
            {
                await conn.DisposeAsync();
                throw new DbUnavailableException($"Khong mo duoc ket noi '{connId}'.", ex);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Task.Run la co y, khong phai thua: OpenAsync cua ODP.NET co mot doan chay DONG BO
        // truoc khi tra Task (cho pool dang khoi tao). Goi truc tiep thi chinh dong `var openTask =`
        // bi treo, Task.WhenAny duoi day khong bao gio kip tinh gio — do la loi da do duoc:
        // request treo tron 60 giay du OpenTimeoutSeconds = 10.
        var openTask = Task.Run(async () => await conn.OpenAsync(cts.Token), CancellationToken.None);

        // Dua vao Task.WhenAny thay vi chi dua CancellationToken cho OpenAsync: neu driver
        // khong ton trong token thi ta van thoat dung han, khong cho het 60 giay cua driver.
        var finished = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(timeout), ct));

        if (finished != openTask)
        {
            ct.ThrowIfCancellationRequested();   // client huy request -> khong phai loi DB

            cts.Cancel();                        // xin driver dung, neu no ho tro
            Abandon(conn, openTask, connId);     // don rac khi no ket thuc muon

            throw new DbUnavailableException(
                $"Khong mo duoc ket noi '{connId}' trong {timeout} giay.");
        }

        try
        {
            await openTask;                      // lay lai exception that (neu co)
            return conn;
        }
        catch (OracleException ex)
        {
            await conn.DisposeAsync();
            throw new DbUnavailableException($"Khong mo duoc ket noi '{connId}'.", ex);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Bo mot lan mo ket noi da qua han: khong cho no nua, nhung phai dispose khi no ket thuc
    /// va phai quan sat exception, khong thi thanh unobserved task exception.
    /// </summary>
    private void Abandon(OracleConnection conn, Task openTask, string? connId)
    {
        _ = openTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger.LogWarning(t.Exception, "Ket noi '{ConnId}' qua han roi loi tiep", connId);

            try { conn.Dispose(); } catch { /* da dong: khong con gi de don */ }
        }, TaskScheduler.Default);
    }
}
