# ToolExcel.Api

API C# (ASP.NET Core .NET 8) tải template Excel động và upload Excel ghi vào `H_DATA`/`T_DATA` của schema PB9 (Than Vàng Danh — TKV). Bản port từ chức năng Excel của Tool_Portal (Spring Boot) sang C#. Mapping cột **hoàn toàn động theo `DM_BIEU_MAU_CONFIG`**, không hardcode.

Đi kèm một **màn quản trị người dùng** (React) để tạo tài khoản và gán đơn vị (`BUKRS`) — đây là lớp xác thực + phân phạm vi cho các hệ thống ngoài (ví dụ APEX) gọi vào `/api/bieumau/*`.

## Thư viện

| Thành phần | Gói | Ghi chú |
|---|---|---|
| Oracle ADO.NET | `Oracle.ManagedDataAccess.Core` | Gọi `PKG_DYNAMIC_EXPORT`, ghi H_DATA/T_DATA |
| Đọc/ghi Excel | `ClosedXML` (MIT) | Miễn phí, hỗ trợ conditional formatting cho cột `AAA` |
| API docs | `Swashbuckle.AspNetCore` | Swagger UI |
| Xác thực | `Microsoft.AspNetCore.Authentication.JwtBearer` + `BCrypt.Net-Next` | JWT HS256, hash `{bcrypt}` tương thích Spring Security |
| Frontend | React 18 + Vite (**không** Ant Design) | ~50 kB gzip, build ra `backend/wwwroot/` |

## Cấu trúc thư mục

```
backend/          Toàn bộ phần .NET (API + test + cấu hình)
  Controllers/ Data/ Models/ Services/ Program.cs
  ToolExcel.Api.csproj
  appsettings.json  appsettings.Development.json
  appsettings.Local.json      <- mật khẩu thật + Jwt:Key, KHÔNG commit
  ToolExcel.Tests/            Unit test (xunit)
  wwwroot/                    Output build của frontend, KHÔNG commit
frontend/         React + Vite (source giao diện quản trị)
ToolExcel.sln     Gộp 2 project .NET -> chạy dotnet build/test từ thư mục gốc
*.ps1 push.bat    Script auto-push / CI trên máy Windows
```

## Cấu hình

Sửa `backend/appsettings.json` → section `Oracle`. Mỗi `connId` là một chuỗi kết nối (mô phỏng `PT_CONNECTION`). Nên đăng nhập bằng chính schema chứa `H_DATA/T_DATA` vì đọc cột qua `USER_TAB_COLUMNS`.

```json
"Oracle": {
  "DefaultConnId": "PB9",
  "Connections": {
    "PB9": { "ConnectionString": "User Id=APEX;Password=***;Data Source=..." }
  }
}
```

Ngoài ra cần `Auth:UserConnId` (connId trỏ tới schema `PT_APP` chứa `PT_USER`/`PT_T001`) và `Jwt:Key` (**≥ 32 byte** cho HS256). Mật khẩu thật + khoá JWT để trong `backend/appsettings.Local.json` (đã `.gitignore`), không commit:

```json
{
  "Oracle": {
    "Connections": {
      "PB9":   { "ConnectionString": "User Id=APEX;Password=...;Data Source=(DESCRIPTION=(TRANSPORT_CONNECT_TIMEOUT=3)(CONNECT_TIMEOUT=5)(RETRY_COUNT=0)(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.67.177)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCLPDB1)));" },
      "PTAPP": { "ConnectionString": "User Id=PT_APP;Password=...;Data Source=(DESCRIPTION=(TRANSPORT_CONNECT_TIMEOUT=3)(CONNECT_TIMEOUT=5)(RETRY_COUNT=0)(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.67.177)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCLPDB1)));" }
    }
  },
  "Jwt": { "Key": "chuoi_ngau_nhien_it_nhat_32_byte_o_day" }
}
```

Thiếu `Jwt:Key` thì app **dừng ngay lúc khởi động** kèm thông báo rõ, không phải chạy lên rồi mỗi request mới lỗi.

### Đừng bỏ `TRANSPORT_CONNECT_TIMEOUT` khỏi connection string

`Connection Timeout=...` của ODP.NET **không có tác dụng** khi host Oracle không tới được — đo thực tế là **60 giây** mỗi lời gọi. Với 60 đơn vị cùng đóng kỳ, DB nghẽn là hàng loạt request treo 60 giây và cạn connection pool.

Ba tham số TNS ở trên là thứ duy nhất làm driver tự bỏ sớm — đo được **3,4 giây** thay vì 60:

| Tham số | Ý nghĩa |
|---|---|
| `TRANSPORT_CONNECT_TIMEOUT=3` | Giới hạn bắt tay TCP |
| `CONNECT_TIMEOUT=5` | Giới hạn cả lượt kết nối (gồm xác thực) |
| `RETRY_COUNT=0` | Không thử lại, tránh nhân thời gian chờ lên nhiều lần |

Ngoài ra `Oracle:OpenTimeoutSeconds` (mặc định 10) là **lưới an toàn ở tầng ứng dụng**, chặn cả trường hợp connection string bị khai thiếu TNS timeout. Đặt `0` để tắt.

## Chạy

Frontend build ra `backend/wwwroot/` nên deploy chỉ **một tiến trình duy nhất**, server **không cần Node**:

```bash
# 1. Build frontend (chỉ cần khi giao diện thay đổi; cần Node 20+)
cd frontend && npm ci && npm run build && cd ..

# 2. Chạy API + giao diện
dotnet restore
dotnet run --project backend/ToolExcel.Api.csproj
```

- Giao diện quản trị: `https://localhost:5001/`
- Swagger: `https://localhost:5001/swagger`

`backend/wwwroot/` là **output build**, đã `.gitignore`. Nếu quên bước 1 thì API vẫn chạy bình thường, chỉ trang chủ trả về dòng nhắc chạy `npm run build` (chứ không phải 404 trắng).

Sửa giao diện thì tiện nhất là chạy 2 tiến trình — Vite dev server tự proxy `/api` sang API:

```bash
dotnet run --project backend/ToolExcel.Api.csproj --urls http://localhost:5199   # cửa sổ 1
cd frontend && npm run dev                                                      # cửa sổ 2 -> http://localhost:5173
```

## Xác thực & phân quyền

Mọi endpoint đều cần `Authorization: Bearer <token>` (`FallbackPolicy` chặn sẵn), trừ `/api/auth/login`, `/api/auth/token` và file tĩnh của giao diện. 401 **không** kèm header `WWW-Authenticate` để browser không bung popup đăng nhập.

| Endpoint | Vai trò cần | Dùng cho |
|---|---|---|
| `POST /api/auth/login` | — | Giao diện web đăng nhập, trả `user` + `accessToken` |
| `POST /api/auth/token` | — | **Client máy (APEX)**, trả `accessToken` + `allowedBukrs` |
| `GET /api/auth/me` | đã đăng nhập | Tải lại hồ sơ khi F5 |
| `GET/POST/PUT /api/admin/*` | `ADMIN` hoặc `SUPER` | Quản trị người dùng, gán BUKRS |
| `GET/POST /api/bieumau/*` | đã đăng nhập + **đúng phạm vi BUKRS** | Tải template / upload dữ liệu |

Mật khẩu đọc từ `PT_USER.PASSWORD_HASH` theo định dạng Spring Security (`{bcrypt}...`, `{noop}...`). Khi tạo/đổi mật khẩu, API sinh hash `{bcrypt}$2a$10$...` — cùng dạng `BCryptPasswordEncoder` mặc định của Spring, nên tài khoản tạo ở bản C# vẫn đăng nhập được ở backend Java và ngược lại.

### Chặn theo phạm vi đơn vị (BUKRS)

Không chặn được ở bước lấy token: lúc đó server chỉ biết *ai đang gọi*, chưa biết sẽ hỏi **BUKRS nào** — `h_BUKRS` là tham số gửi kèm từng lần gọi. Nên việc chặn nằm ở endpoint:

1. `POST /api/auth/token` trả kèm `allowedBukrs` để bên gọi biết phạm vi của mình trước.
2. Mỗi lần gọi `/api/bieumau/{form}/export|import`, API so `h_BUKRS` với `PT_USER_ORG` của user, **mở rộng xuống toàn bộ cây con** (`CONNECT BY`): gán đơn vị cha thì được cả các đơn vị trực thuộc.
3. Ngoài phạm vi → **403** kèm danh sách `allowedBukrs`. Chưa gán đơn vị nào → cũng 403 (tập rỗng nghĩa là *không được gì*, khác `null` của `SUPER` nghĩa là *không giới hạn*).
4. Vai trò `SUPER` bỏ qua toàn bộ kiểm tra này.

Phạm vi đọc **DB tươi mỗi lần gọi**, không nhét vào JWT — nhờ vậy thu quyền ở màn quản trị có hiệu lực ngay, không phải chờ token hết hạn 8 giờ.

### Ví dụ luồng APEX gọi sang

```bash
# 1. Lấy token (tài khoản máy, vai trò APIEXPORT)
curl -X POST https://host/api/auth/token \
     -H 'Content-Type: application/json' \
     -d '{"username":"apiexport","password":"..."}'
# -> {"accessToken":"eyJ...","tokenType":"Bearer","expiresIn":28800,"allowedBukrs":["2100","2110"]}

# 2. Dùng token gọi biểu mẫu
curl -H 'Authorization: Bearer eyJ...' \
     'https://host/api/bieumau/KH18/export?h_BUKRS=2100&h_YEAR=2026&h_PERIOD=7' -o KH18.xlsx

# BUKRS ngoài phạm vi -> 403
# {"error":"Tai khoan khong duoc phep don vi BUKRS='9999'.","allowedBukrs":["2100","2110"]}
```

## Màn quản trị người dùng

Vào bằng tài khoản có vai trò `ADMIN` hoặc `SUPER`. Làm được:

- **Tạo người dùng** — username, mật khẩu (≥ 8 ký tự, hash ra `{bcrypt}`), họ tên, email, bật/tắt.
- **Sửa / tắt** — tắt là `IS_ACTIVE='N'` (xoá mềm), không xoá bản ghi.
- **Đặt lại mật khẩu** — không cần mật khẩu cũ.
- **Gán đơn vị (BUKRS)** — chọn nhiều đơn vị từ cây `PT_T001`, đánh dấu một đơn vị chính (`IS_PRIMARY='Y'`). Lưu là **thay toàn bộ** danh sách cũ.
- **Tìm kiếm + phân trang** — xem mục dưới.

Bảng DB tác động: `PT_USER` (ghi), `PT_USER_ORG` (ghi), `PT_T001` (chỉ đọc), `PT_USER_ROLE`/`PT_ROLE` (chỉ đọc để hiển thị).

### Phân trang

Phân trang **ở phía server** (`OFFSET … FETCH NEXT` — cần Oracle 12c trở lên), không tải cả bảng về rồi cắt ở browser.

```
GET /api/admin/users?q=&includeInactive=true&page=1&pageSize=25
```

```json
{ "items": [...], "page": 1, "pageSize": 25, "total": 312, "totalPages": 13 }
```

| Quy ước | Hành vi |
|---|---|
| `pageSize` mặc định 25, chọn được 25/50/100/200 | Chặn trần ở `Paging.MaxPageSize` = 200 |
| `page` vượt quá trang cuối | Kéo về **trang cuối**, không trả bảng trống |
| `page` ≤ 0 hoặc `pageSize` ≤ 0 | Về trang 1 / kích thước mặc định |
| Đổi ô tìm kiếm, `includeInactive`, hay `pageSize` | Tự về trang 1 |

`page`/`pageSize` trong kết quả là giá trị **backend thực sự dùng** sau khi chuẩn hoá — frontend hiển thị theo đó, không theo tham số nó gửi đi.

Ô tìm kiếm có **debounce 300ms**: gõ 6 ký tự chỉ sinh 1 lời gọi API thay vì 6. Số đếm `total` và danh sách dùng **chung một state đã tải xong**, nên trong lúc đang tải trang mới không xảy ra cảnh dòng tóm tắt ghi "301–312" mà bảng vẫn hiện dữ liệu trang cũ.

### Chưa làm trong màn này

- **Không gán được vai trò** (`PT_USER_ROLE`) — tài khoản mới tạo **chưa có vai trò nào**, nên chưa gọi được `/api/bieumau/*`. Phải `INSERT` tay:
  ```sql
  INSERT INTO PT_USER_ROLE (USER_ID, ROLE_ID)
  SELECT u.ID, r.ID FROM PT_USER u, PT_ROLE r
  WHERE u.USERNAME = 'apiexport_vd' AND r.ROLE_CODE = 'APIEXPORT';
  ```
- **Không quản lý danh mục đơn vị** `PT_T001` — dropdown chỉ đọc những bản ghi `IS_ACTIVE='Y'` có sẵn.
- **Không lọc theo phạm vi đơn vị của chính admin**: bản Java dùng `ScopeService` để admin đơn vị A chỉ thấy user đơn vị A; bản này mọi `ADMIN` thấy toàn bộ danh sách người dùng.

## 2 endpoint biểu mẫu

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
backend/Controllers/AuthController.cs         login / token / me / logout
backend/Controllers/AdminUsersController.cs   Quản trị PT_USER + gán BUKRS (chỉ ADMIN/SUPER)
backend/Controllers/BieuMauController.cs      2 endpoint export/import + chặn theo BUKRS
backend/Data/OracleConnectionFactory.cs       Factory kết nối đa nguồn theo connId
backend/Models/AuthModels.cs                  DTO: login, user info, token
backend/Models/AdminModels.cs                 DTO: danh sách user, đơn vị, request tạo/sửa
backend/Models/BieuMauModels.cs               DTO: config, header params, kết quả
backend/Services/BieuMauConfigService.cs      Đọc DM_BIEU_MAU + DM_BIEU_MAU_CONFIG
backend/Services/ExcelExportService.cs        Gọi PKG_DYNAMIC_EXPORT → dựng Excel
backend/Services/ExcelImportService.cs        Đọc Excel → ghi H_DATA/T_DATA
backend/Services/UserAuthService.cs           Đọc PT_USER/PT_ROLE để xác thực
backend/Services/UserAdminService.cs          CRUD PT_USER + PT_USER_ORG, đọc cây PT_T001
backend/Services/UserScopeService.cs          Phạm vi BUKRS (CONNECT BY) + BukrsScope.Decide()
backend/Services/PasswordVerifier.cs          Verify {bcrypt}/{noop} (định dạng Spring)
backend/Services/PasswordHasher.cs            Sinh {bcrypt}$2a$10$... khi tạo/đổi mật khẩu
backend/Services/JwtTokenService.cs           Phát JWT HS256, sub=username, claim roles

frontend/                                     React + Vite (build → backend/wwwroot/)
  src/App.tsx                                 Vỏ + kiểm vai trò ADMIN/SUPER
  src/Login.tsx                               Màn đăng nhập
  src/UserAdmin.tsx                           Bảng người dùng + 4 dialog
  src/BukrsPicker.tsx                         Chọn nhiều BUKRS + đánh dấu đơn vị chính
  src/api.ts                                  Client fetch, gắn Bearer, 401 → xoá token
```

## Test

```bash
dotnet test          # từ thư mục gốc, qua ToolExcel.sln — 67 test
```

Phủ các chỗ dễ sai nhất, đều là hàm thuần không cần DB:

| File | Nội dung |
|---|---|
| `PasswordVerifierTests.cs` | Verify `{bcrypt}`/`{noop}`, scheme lạ phải ném lỗi rõ ràng |
| `PasswordHasherTests.cs` | Hash ra đúng `{bcrypt}$2a$10$`, round-trip, mật khẩu có dấu tiếng Việt |
| `BukrsScopeTests.cs` | Logic chặn BUKRS — gồm bẫy "tập rỗng ≠ không giới hạn" |
| `OrgTreeTests.cs` | Cây `PT_T001`: đơn vị mồ côi / vòng lặp cha-con không được làm mất bản ghi |
| `PagingTests.cs` | `OFFSET` và số trang — `page` âm/0/vượt cuối, `pageSize` khổng lồ, tổng chia hết |
| `HelpersTests.cs` | Parse `EXCEL_COL`, `HeaderParams.FromQuery` |

## TODO

- **Gán vai trò (`PT_USER_ROLE`) từ màn quản trị** — hiện phải `INSERT` tay, xem mục trên.
- **Quyền theo từng biểu mẫu** — hiện tài khoản có `BUKRS` nào thì gọi được **mọi** biểu mẫu của đơn vị đó. Nếu cần giới hạn theo biểu thì phải thêm bảng map + màn cấu hình; nên quyết trước go-live.
- Quản lý danh mục đơn vị `PT_T001` từ giao diện.
- Lọc danh sách người dùng theo phạm vi đơn vị của admin (`ScopeService` như bản Java).
- Xuất theo template gốc `PT_REPORT_TMPL` khi có (hiện luôn tự sinh từ config).
- `get_data_dynamic_no_marc` / `get_data_kdt05` cho biểu đặc thù.
- **Integration test với Oracle thật** — toàn bộ phần chạm DB (CRUD user, truy vấn phạm vi, 403 khi ngoài phạm vi, câu `OFFSET/FETCH`) hiện chưa có test tự động nào phủ.
- Khi Oracle chết mà có ~60 lời gọi đồng thời, mỗi lời gọi mất 20–30 giây mới trả 503 (ODP.NET tuần tự hoá các lần mở kết nối trên pool đã chết). Muốn nhanh hơn cần circuit breaker.
