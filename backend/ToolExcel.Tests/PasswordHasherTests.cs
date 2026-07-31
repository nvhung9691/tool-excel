using ToolExcel.Api.Services;
using Xunit;

namespace ToolExcel.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_CoTienTo_Bcrypt2a()
    {
        // Phai la $2a$ + strength 10 de trung dinh dang BCryptPasswordEncoder cua Spring,
        // nho vay hash sinh o C# van dang nhap duoc o backend Java va nguoc lai.
        var hash = PasswordHasher.Hash("Test@12345");
        Assert.StartsWith("{bcrypt}$2a$10$", hash);
    }

    [Fact]
    public void Hash_RoiVerify_TraTrue()
    {
        var hash = PasswordHasher.Hash("Test@12345");
        Assert.True(PasswordVerifier.Verify("Test@12345", hash));
    }

    [Fact]
    public void Hash_MatKhauKhac_TraFalse()
    {
        var hash = PasswordHasher.Hash("Test@12345");
        Assert.False(PasswordVerifier.Verify("Test@12346", hash));
    }

    [Fact]
    public void Hash_MatKhauCoDauTiengViet_VanVerifyDuoc()
    {
        // Bay encoding UTF-8: hash va verify phai dung cung mot cach ma hoa byte.
        const string pwd = "Mật@Khẩu123";
        Assert.True(PasswordVerifier.Verify(pwd, PasswordHasher.Hash(pwd)));
    }

    [Fact]
    public void Hash_HaiLan_RaHaiHashKhacNhau()
    {
        // Salt ngau nhien: cung mat khau phai ra hash khac nhau, nhung deu verify duoc.
        var a = PasswordHasher.Hash("Test@12345");
        var b = PasswordHasher.Hash("Test@12345");
        Assert.NotEqual(a, b);
        Assert.True(PasswordVerifier.Verify("Test@12345", a));
        Assert.True(PasswordVerifier.Verify("Test@12345", b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short7c")]   // 7 ky tu -> duoi MinLength
    public void Hash_MatKhauKhongDatYeuCau_NemArgumentException(string? pwd)
        => Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(pwd!));

    [Fact]
    public void Hash_DungBangDoDaiToiThieu_KhongNem()
    {
        var pwd = new string('a', PasswordHasher.MinLength);
        Assert.True(PasswordVerifier.Verify(pwd, PasswordHasher.Hash(pwd)));
    }
}
