# Máy test 192.168.67.170 — tự pull + build + test

Luồng tổng thể:

```
PC dev  --(auto_push_watch.ps1)-->  GitHub (nvhung9691/tool-excel)  --(git pull)-->  Máy test .170
                                                                                        └─ build + unit test + chạy thử /health
```

Máy test cùng mạng Oracle (192.168.67.177) nên chạy được API thật. Tất cả chạy trên máy của anh,
không dùng GitHub Actions.

## Chuẩn bị 1 lần trên máy .170

1. Cài sẵn: **.NET SDK 8 trở lên** và **git** (kiểm tra: `dotnet --version`, `git --version`).
   Dự án target `net8.0`. SDK 9 build được, **miễn là có runtime 8** — kiểm bằng
   `dotnet --list-runtimes`, phải thấy `Microsoft.NETCore.App 8.x`. Máy `.170` hiện dùng
   **SDK 9.0.314 + runtime 8.0.27**, build và test đều sạch.
   Muốn có **giao diện quản trị** thì cài thêm **Node 20+** (`node --version`) — runner sẽ tự
   `npm run build`. Không có Node thì runner bỏ qua bước đó, API vẫn chạy nhưng không có giao diện.
2. Clone repo về (chọn 1 thư mục):
   ```bat
   mkdir C:\ci & cd /d C:\ci
   git clone https://github.com/nvhung9691/tool-excel.git
   ```
   Lần clone đầu đăng nhập bằng **Personal Access Token** (như GIT_SETUP.md) để Windows nhớ
   credential cho các lần `git pull` sau.

   > Trên máy `.170` phải dùng `C:\ci`: ổ `D:` ở đó là **CD-ROM virtio-win**, không ghi được.
   > Các lệnh dưới đây viết theo `C:\ci\tool-excel`; clone chỗ khác thì đổi đường dẫn cho khớp.
3. Tạo file `C:\ci\tool-excel\backend\appsettings.Local.json` (**chú ý: trong `backend\`**,
   không phải thư mục gốc):
   ```json
   {
     "Oracle": { "Connections": {
       "PB9":   { "ConnectionString": "User Id=APEX;Password=MAT_KHAU_APEX;Data Source=(DESCRIPTION=(TRANSPORT_CONNECT_TIMEOUT=3)(CONNECT_TIMEOUT=5)(RETRY_COUNT=0)(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.67.177)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCLPDB1)));" },
       "PTAPP": { "ConnectionString": "User Id=PT_APP;Password=MAT_KHAU_PTAPP;Data Source=(DESCRIPTION=(TRANSPORT_CONNECT_TIMEOUT=3)(CONNECT_TIMEOUT=5)(RETRY_COUNT=0)(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.67.177)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCLPDB1)));" }
     }},
     "Jwt": { "Key": "doi_thanh_chuoi_ngau_nhien_it_nhat_32_byte" }
   }
   ```

   > Giữ nguyên 3 tham số `TRANSPORT_CONNECT_TIMEOUT` / `CONNECT_TIMEOUT` / `RETRY_COUNT` —
   > không có chúng thì mỗi lời gọi treo 60 giây khi Oracle không tới được. Xem README.
   File này đã được `.gitignore` bỏ qua nên không lên GitHub.

   > `Jwt:Key` là **bắt buộc** — thiếu thì app dừng ngay lúc khởi động (smoke test sẽ FAIL).
   > `PTAPP` cần cho đăng nhập (`PT_USER`) và cho việc kiểm phạm vi BUKRS; nếu `PT_USER` nằm
   > cùng schema `APEX` thì đặt `"Auth": { "UserConnId": "PB9" }` thay vì khai `PTAPP`.
   > `SERVICE_NAME`: `appsettings.json` đang dùng `ORCLPDB1` — kiểm lại cho khớp môi trường thật.

## Chạy runner

Chạy thử ngay (cửa sổ hiện log):

```bat
cd /d C:\ci\tool-excel
powershell -ExecutionPolicy Bypass -File ci_test_runner.ps1
```

Mỗi 60 giây nó `git pull`; thấy commit mới thì tự **restore → build (Release) → `dotnet test` →
chạy API kiểm `/health`**. Kết quả ghi ở `ci_test.log` (dòng cuối là `KET QUA: PASS/FAIL`).

Đổi tham số nếu cần:
```bat
powershell -ExecutionPolicy Bypass -File ci_test_runner.ps1 -RepoPath "C:\ci\tool-excel" -IntervalSeconds 30 -HealthPort 5080
```

## Cho tự chạy nền mỗi khi bật máy

```bat
cd /d C:\ci\tool-excel
powershell -ExecutionPolicy Bypass -File install_test_runner.ps1
powershell -Command "Start-ScheduledTask -TaskName 'ToolExcel CI Test Runner'"
```

> Cả `install_test_runner.ps1` và `ci_test_runner.ps1` mặc định `$RepoPath = "C:\ci\tool-excel"`.
> Clone vào chỗ khác thì sửa biến đó ở đầu `install_test_runner.ps1` và truyền `-RepoPath`
> cho `ci_test_runner.ps1`.

## "Test" gồm những gì

- **Unit test** (`backend\ToolExcel.Tests`, 67 test): logic thuần, không cần DB — parse
  `EXCEL_COL` (C### → số cột), cờ `HEADER='X'`, trích tham số `h_*`, hash `{bcrypt}`,
  logic chặn BUKRS, dựng cây đơn vị `PT_T001`.
- **Frontend build**: `npm ci` + `npm run build` trong `frontend\` → ra `backend\wwwroot\`.
  Bỏ qua nếu máy chưa có Node (ghi log, không tính là FAIL).
- **Smoke test**: build xong chạy API, gọi `GET /health`.

  > `/health` nằm sau `FallbackPolicy` nên **không có token sẽ trả 401** — đó là app đã lên và
  > đang bảo vệ đúng. Runner tính **cả 200 và 401** là PASS; chỉ khi không nối được cổng mới FAIL.

- **Chạy thử endpoint thật** (export/import gọi Oracle): cần bước 3 ở trên, và cần **token**
  vì mọi endpoint đều đã được bảo vệ. Hiện làm **thủ công**:
  ```bat
  :: 1. lay token
  curl -X POST http://localhost:5080/api/auth/token -H "Content-Type: application/json" ^
       -d "{\"username\":\"apiexport\",\"password\":\"MAT_KHAU\"}"

  :: 2. dung token goi bieu mau (thay <token> bang accessToken vua nhan)
  curl -H "Authorization: Bearer <token>" ^
       "http://localhost:5080/api/bieumau/KH18/export?connId=PB9&h_BUKRS=2100&h_YEAR=2026&h_PERIOD=7" ^
       -o test.xlsx
  ```
  Đổi `h_BUKRS` sang mã đơn vị **chưa gán** cho tài khoản đó → phải nhận **403** kèm
  danh sách `allowedBukrs`.

  > **Tài khoản thử phân quyền**: `ci_enduser` / `Ci@12345` — không có vai trò nào, chỉ gán
  > `BUKRS=2100`. Dùng để kiểm hai lớp chặn: gọi `/api/admin/*` phải ra **403**, và export với
  > `h_BUKRS` khác `2100` cũng phải ra **403**. Đừng gán vai trò cho nó, mất tác dụng thử.
  >
  > Tài khoản `SUPER` **bỏ qua toàn bộ kiểm phạm vi BUKRS** (`UserScopeService.SuperRole`) — thấy
  > nó export được mã lạ là **đúng thiết kế**, không phải lỗi. Muốn thử chặn thì phải dùng tài
  > khoản không phải `SUPER`.

- **Giao diện quản trị**: mở `http://localhost:5080/` bằng tài khoản có vai trò `ADMIN`/`SUPER`.

## Ghi chú

- Runner dùng `git pull --ff-only` — nếu ai đó sửa tay trên máy test gây lệch nhánh, pull sẽ báo
  lỗi trong log; khi đó `git reset --hard origin/main` trên máy test để đồng bộ lại.
- Runner pull **nhánh đang checkout**. Muốn test một nhánh feature thì
  `git checkout <ten-nhanh>` một lần trên máy test, runner sẽ theo nhánh đó.
- Unit test fail, frontend build fail hoặc smoke fail đều ghi `KET QUA: FAIL` trong `ci_test.log`.
