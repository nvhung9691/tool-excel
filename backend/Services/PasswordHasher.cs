namespace ToolExcel.Api.Services;

/// <summary>
/// Sinh PASSWORD_HASH theo dinh dang Spring Security: <c>{bcrypt}$2a$10$...</c>.
/// Doi xung voi <see cref="PasswordVerifier"/>: hash sinh o day phai verify duoc ca o
/// backend Java (BCryptPasswordEncoder strength 10, revision $2a) va o backend C#.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Bang strength mac dinh cua Spring BCryptPasswordEncoder.</summary>
    private const int WorkFactor = 10;

    /// <summary>Do dai toi thieu cua mat khau moi.</summary>
    public const int MinLength = 8;

    /// <summary>Hash mat khau ra <c>{bcrypt}$2a$...</c>. Nem <see cref="ArgumentException"/> neu qua ngan.</summary>
    public static string Hash(string rawPassword)
    {
        // Khong truyen paramName: message nay hien thang len UI, khong nen lo ten bien noi bo.
        if (string.IsNullOrWhiteSpace(rawPassword) || rawPassword.Length < MinLength)
            throw new ArgumentException($"Mat khau phai tu {MinLength} ky tu tro len.");

        return "{bcrypt}" + BCrypt.Net.BCrypt.HashPassword(rawPassword, WorkFactor);
    }
}
