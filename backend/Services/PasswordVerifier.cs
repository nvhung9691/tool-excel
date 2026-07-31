using System.Security.Cryptography;
using System.Text;

namespace ToolExcel.Api.Services;

/// <summary>
/// Verify mat khau theo dinh dang Spring Security: PASSWORD_HASH co tien to {scheme}
/// (vd {bcrypt}$2a$..., {noop}Admin@123). ASP.NET Core khong co san co che nay nen tu xu ly,
/// neu bo qua thi khong ai dang nhap duoc.
/// </summary>
public static class PasswordVerifier
{
    public static bool Verify(string rawPassword, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        var (scheme, payload) = SplitPrefix(storedHash);

        return scheme switch
        {
            "bcrypt" => BCrypt.Net.BCrypt.Verify(rawPassword, payload),
            "noop"   => FixedTimeEquals(rawPassword, payload),
            _        => throw new NotSupportedException(
                            $"Password scheme khong ho tro: '{{{scheme}}}'. Chi ho tro {{bcrypt}} va {{noop}}.")
        };
    }

    private static (string scheme, string payload) SplitPrefix(string hash)
    {
        if (hash.Length > 1 && hash[0] == '{')
        {
            var end = hash.IndexOf('}');
            if (end > 0)
                return (hash.Substring(1, end - 1).ToLowerInvariant(), hash[(end + 1)..]);
        }

        // Khong co tien to {...} -> tu choi (giong DelegatingPasswordEncoder cua Spring).
        throw new NotSupportedException(
            "PASSWORD_HASH thieu tien to {scheme}. Vd hop le: {bcrypt}$2a$... hoac {noop}matkhau.");
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
