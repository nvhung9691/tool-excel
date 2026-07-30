# ToolExcel.Api

API C# (ASP.NET Core .NET 8) tải template Excel động và upload Excel ghi vào `H_DATA`/`T_DATA` của schema PB9 (Than Vàng Danh — TKV). Bản port từ chức năng Excel của Tool_Portal (Spring Boot) sang C#. Mapping cột **hoàn toàn động theo `DM_BIEU_MAU_CONFIG`**, không hardcode.

## Thư viện

| Thành phần | Gói | Ghi chú |
|---|---|---|
| Oracle ADO.NET | `Oracle.ManagedDataAccess.Core` | Gọi `PKG_DYNAMIC_EXPORT`, ghi H_DATA/T_DATA |
| Đọc/ghi Excel | `ClosedXML` (MIT) | Miễn phí, hỗ trợ conditional formatting cho cột `AAA` |
| API docs | `Swashbuckle.AspNetCore` | Swagger UI |

## Cấu hình

Sửa `appsettings.json` → section `Oracle`. Mỗi `connId` là một chuỗi kết nối (mô phỏng `PT_CONNECTION`). Nên đăng nhập bằng chính schema chứa `H_DATA/T_DATA` vì đọc cột qua `USER_TAB_COLUMNS`.

```json
"Oracle": {
  "DefaultConnId": "PB9",
  "Connections": {
    "PB9": { "ConnectionString": "User Id=APEX;Password=***;Data Source=..." }
  }
}
```

## Chạy

```bash
dotnet restore
dotnet run
```

Mở Swagger: `https://localhost:5001/swagger`.

## 2 endpoint

### Tải template
```
GET /api/bieumau/{formCode}/export?connId=PB9&h_BUKRS=2100&h_YEAR=2026&h_PERIOD=7
```
Gọi `PKG_DYNAMIC_EXPORT.GET_DATA_DYNAMIC` (function trả `SYS_REFCURSOR`), rót dữ liệu ra `.xlsx` theo `EXCEL_COL`. Cột `FORMAT` (B/I/IB) đặt ở cột `AAA` kèm conditional formatting cho vùng dữ liệu.

> Nếu package lỗi (thường do 1 dòng config sai tên cột), nó trả cursor 1 cột `MSG` → API ném `PKG_DYNAMIC_EXPORT: Lỗi: ORA-...` thay vì file trắng.

### Upload dữ liệu
```
POST /api/bieumau/{formCode}/import?connId=PB9&h_BUKRS=2100&h_YEAR=2026&h_PERIOD=7
Body: multipart/form-data, field 'file'
```
Đọc từ dòng `DM_BIEU_MAU.ROW_EXCEL`, validate ô `VITRI` khớp tham số `h_*`, rồi:
- `H_DATA` khớp `FORM_CODE+BUKRS+YEAR+PERIOD+DAY`, `STATUS='D'` → dùng lại ID + `DELETE T_DATA`.
- `STATUS<>'D'` → báo lỗi "phải hủy duyệt mới upload".
- Chưa có → tạo ID mới (`H_DATA_SEQ.NEXTVAL`).

Ghi trong 1 transaction. `T_DATA.ID` là IDENTITY (không truyền); `CREATED_BY/AT` do trigger set.

## Quy ước cột (DM_BIEU_MAU_CONFIG)

| Cột | Ý nghĩa |
|---|---|
| `EXCEL_COL` | `C###` = cột thứ ### (C001 = cột A) |
| `BIEUMAU_COL` | Tên cột đích trong `T_DATA` hoặc `H_DATA` |
| `HEADER` | `'X'` (hoa) → ghi `H_DATA`; ngược lại → `T_DATA` |
| `VITRI` | Ô header trong file (vd `B2`) → validate |
| `COL_TITLE`, `STT` | Tiêu đề / thứ tự cột |

## Cấu trúc mã nguồn

```
Controllers/BieuMauController.cs      2 endpoint export/import
Data/OracleConnectionFactory.cs       Factory kết nối đa nguồn theo connId
Models/BieuMauModels.cs               DTO: config, header params, kết quả
Services/BieuMauConfigService.cs      Đọc DM_BIEU_MAU + DM_BIEU_MAU_CONFIG
Services/ExcelExportService.cs        Gọi PKG_DYNAMIC_EXPORT → dựng Excel
Services/ExcelImportService.cs        Đọc Excel → ghi H_DATA/T_DATA
```

## TODO (chưa làm trong scaffold)

- Xác thực JWT Bearer + role `APIEXPORT` (như Tool_Portal).
- Xuất theo template gốc `PT_REPORT_TMPL` khi có (hiện luôn tự sinh từ config).
- `get_data_dynamic_no_marc` / `get_data_kdt05` cho biểu đặc thù.
- Unit/integration test với Oracle thật.
