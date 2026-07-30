using ToolExcel.Api.Services;
using Xunit;

namespace ToolExcel.Tests;

public class PasswordVerifierTests
{
    [Fact]
    public void Noop_MatKhauDung_TraTrue()
        => Assert.True(PasswordVerifier.Verify("Admin@123", "{noop}Admin@123"));

    [Fact]
    public void Noop_MatKhauSai_TraFalse()
        => Assert.False(PasswordVerifier.Verify("sai", "{noop}Admin@123"));

    [Fact]
    public void Bcrypt_MatKhauDung_TraTrue()
    {
        var hash = "{bcrypt}" + BCrypt.Net.BCrypt.HashPassword("Secret@123");
        Assert.True(PasswordVerifier.Verify("Secret@123", hash));
    }

    [Fact]
    public void Bcrypt_MatKhauSai_TraFalse()
    {
        var hash = "{bcrypt}" + BCrypt.Net.BCrypt.HashPassword("Secret@123");
        Assert.False(PasswordVerifier.Verify("khac", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HashRong_TraFalse(string? storedHash)
        => Assert.False(PasswordVerifier.Verify("x", storedHash));

    [Fact]
    public void SchemeLa_NemLoi()
        => Assert.Throws<NotSupportedException>(() => PasswordVerifier.Verify("x", "{md5}abc"));

    [Fact]
    public void ThieuTienTo_NemLoi()
        => Assert.Throws<NotSupportedException>(() => PasswordVerifier.Verify("x", "$2a$10$abcdef"));
}
