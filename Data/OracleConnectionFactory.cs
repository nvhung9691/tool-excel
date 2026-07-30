using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace ToolExcel.Api.Data;

/// <summary>Cau hinh nhieu nguon CSDL theo connId (mo phong bang PT_CONNECTION cua Tool_Portal).</summary>
public sealed class OracleConnectionOptions
{
    public string DefaultConnId { get; set; } = "PB9";
    public Dictionary<string, ConnectionEntry> Connections { get; set; } = new();

    public sealed class ConnectionEntry
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

public interface IOracleConnectionFactory
{
    /// <summary>Mo connection theo connId. Null/empty -> dung DefaultConnId.</summary>
    OracleConnection Create(string? connId = null);
}

public sealed class OracleConnectionFactory : IOracleConnectionFactory
{
    private readonly OracleConnectionOptions _options;

    public OracleConnectionFactory(IOptions<OracleConnectionOptions> options)
        => _options = options.Value;

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
}
