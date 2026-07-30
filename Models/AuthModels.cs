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
}

/// <summary>Ket qua dang nhap web (co ca user + token).</summary>
public sealed class LoginResponse
{
    public UserInfo User { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
}

/// <summary>Ket qua lay token cho client may (chi token).</summary>
public sealed class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
}
