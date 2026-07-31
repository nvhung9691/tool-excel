namespace ToolExcel.Api.Models;

/// <summary>Body dang nhap.</summary>
public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Thong tin nguoi dung tra ve cho client.</summary>
public sealed class UserInfo
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cac BUKRS user duoc phep goi /api/bieumau/* (da mo rong xuong cay con).
    /// <para><c>null</c> = khong gioi han (vai tro SUPER).</para>
    /// <para>Mang rong = chua duoc gan don vi nao -> moi loi goi bieu mau se bi 403.</para>
    /// Chi mang tinh THONG BAO cho client biet pham vi cua minh; viec chan thuc su lam o
    /// endpoint va doc DB tuoi, xem <see cref="Services.IUserScopeService"/>.
    /// </summary>
    public IReadOnlyList<string>? AllowedBukrs { get; set; }
}

/// <summary>Ket qua dang nhap web (co ca user + token).</summary>
public sealed class LoginResponse
{
    public UserInfo User { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
}

/// <summary>Ket qua lay token cho client may (APEX): token + pham vi don vi.</summary>
public sealed class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }

    /// <summary>Xem <see cref="UserInfo.AllowedBukrs"/>. <c>null</c> = khong gioi han (SUPER).</summary>
    public IReadOnlyList<string>? AllowedBukrs { get; set; }
}
