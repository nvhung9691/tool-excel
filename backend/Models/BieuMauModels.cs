namespace ToolExcel.Api.Models;

/// <summary>Thong tin bieu mau (DM_BIEU_MAU).</summary>
public sealed class BieuMauInfo
{
    public string FormCode { get; set; } = string.Empty;
    public string? TenBieuMau { get; set; }

    /// <summary>Dong (1-based) bat dau vung du lieu chi tiet trong file Excel upload.</summary>
    public int RowExcel { get; set; } = 1;
}

/// <summary>1 dong trong danh sach bieu mau — du de dung dropdown chon FORM_CODE.</summary>
public sealed class BieuMauListItem
{
    public string FormCode { get; set; } = string.Empty;
    public string? TenBieuMau { get; set; }

    /// <summary>Dong bat dau vung du lieu. 0/null = chua khai (xem README, muc gioi han).</summary>
    public int? RowExcel { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>So dong cau hinh cot trong DM_BIEU_MAU_CONFIG. 0 = chua cau hinh cot nao.</summary>
    public int SoCotCauHinh { get; set; }
}

/// <summary>1 dong cau hinh cot (DM_BIEU_MAU_CONFIG) — dieu khien mapping Excel &lt;-&gt; DB.</summary>
public sealed class BieuMauColumnConfig
{
    /// <summary>C### = cot thu ### trong file (C001 = cot 1 = A).</summary>
    public string? ExcelCol { get; set; }

    /// <summary>Ten cot dich: trong T_DATA (detail) hoac H_DATA (neu la header).</summary>
    public string BieumauCol { get; set; } = string.Empty;

    /// <summary>HEADER = 'X' (hoa) -> ghi vao H_DATA; nguoc lai -> T_DATA.</summary>
    public string? Header { get; set; }

    /// <summary>O header trong file (vd B2) -> validate file khop tham so upload.</summary>
    public string? ViTri { get; set; }

    public string? ColTitle { get; set; }
    public int? Stt { get; set; }

    public bool IsHeader => string.Equals(Header, "X", StringComparison.Ordinal);

    /// <summary>So thu tu cot 1-based tu ExcelCol "C###". 0 = khong hop le.</summary>
    public int ExcelColIndex
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExcelCol)) return 0;
            var s = ExcelCol.Trim().TrimStart('C', 'c');
            return int.TryParse(s, out var n) ? n : 0;
        }
    }
}

/// <summary>Tham so header truyen len endpoint (h_BUKRS, h_YEAR, ...).</summary>
public sealed class HeaderParams
{
    /// <summary>key = ten cot header (BUKRS/YEAR/PERIOD/DAY/WERKS), value = gia tri.</summary>
    public Dictionary<string, string?> Values { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string col) => Values.TryGetValue(col, out var v) ? v : null;

    /// <summary>Trich cac query param dang h_&lt;COL&gt; thanh HeaderParams.</summary>
    public static HeaderParams FromQuery(IQueryCollection query)
    {
        var hp = new HeaderParams();
        foreach (var kv in query)
        {
            if (kv.Key.StartsWith("h_", StringComparison.OrdinalIgnoreCase))
            {
                var col = kv.Key[2..];
                hp.Values[col] = kv.Value.ToString();
            }
        }
        return hp;
    }
}

/// <summary>Ket qua import.</summary>
public sealed class ImportResult
{
    public bool Success { get; set; }
    public string FormCode { get; set; } = string.Empty;
    public long? HeaderId { get; set; }
    public int DetailRows { get; set; }
    public string? Message { get; set; }
}
