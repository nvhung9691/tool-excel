namespace ToolExcel.Api.Models;

/// <summary>1 dong trong danh sach nguoi dung (man quan tri).</summary>
public sealed class UserListItem
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Cac BUKRS da gan truc tiep trong PT_USER_ORG (chua mo rong xuong cay con).</summary>
    public IReadOnlyList<string> Bukrs { get; set; } = Array.Empty<string>();

    /// <summary>Vai tro (ROLE_CODE) — chi doc, man nay khong sua vai tro.</summary>
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

/// <summary>1 don vi trong danh muc PT_T001 (dung cho dropdown gan BUKRS).</summary>
public sealed class OrgItem
{
    public long Id { get; set; }
    public string Bukrs { get; set; } = string.Empty;
    public string Butxt { get; set; } = string.Empty;
    public string? OrgType { get; set; }
    public long? ParentId { get; set; }

    /// <summary>Do sau trong cay (0 = goc) — de frontend thut le hien thi.</summary>
    public int Level { get; set; }
}

/// <summary>Tao nguoi dung moi. Mat khau se duoc hash ra {bcrypt}.</summary>
public sealed class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Danh sach BUKRS gan ngay khi tao (co the de trong).</summary>
    public List<string> Bukrs { get; set; } = new();

    /// <summary>BUKRS chinh (IS_PRIMARY='Y'). Phai nam trong <see cref="Bukrs"/> neu co.</summary>
    public string? PrimaryBukrs { get; set; }
}

/// <summary>Sua thong tin nguoi dung. Khong doi mat khau o day.</summary>
public sealed class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Doi mat khau (quan tri dat lai, khong can mat khau cu).</summary>
public sealed class ChangePasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Gan lai toan bo danh sach BUKRS cho 1 user (replace, khong phai them).</summary>
public sealed class AssignBukrsRequest
{
    public List<string> Bukrs { get; set; } = new();
    public string? PrimaryBukrs { get; set; }
}
