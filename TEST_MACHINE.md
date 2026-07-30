# Máy test 192.168.67.170 — tự pull + build + test

Luồng tổng thể:

```
PC dev  --(auto_push_watch.ps1)-->  GitHub (nvhung9691/tool-excel)  --(git pull)-->  Máy test .170
                                                                                        └─ build + unit test + chạy thử /health
```

Máy test cùng mạng Oracle (192.168.67.177) nên chạy được API thật. Tất cả chạy trên máy của anh,
không dùng GitHub Actions.

## Chuẩn bị 1 lần trên máy .170

1. Cài sẵn: **.NET SDK 8** và **git** (kiểm tra: `dotnet --version`, `git --version`).
2. Clone repo về (chọn 1 thư mục, ví dụ `D:\ci`):
   ```bat
   mkdir D:\ci & cd /d D:\ci
   git clone https://github.com/nvhung9691/tool-excel.git
   ```
   Lần clone đầu đăng nhập bằng **Personal Access Token** (như GIT_SETUP.md) để Windows nhớ
   credential cho các lần `git pull` sau.
3. (Tuỳ chọn — để chạy thử endpoint export/import gọi Oracle thật) tạo file
   `D:\ci\tool-excel\appsettings.Local.json` với mật khẩu DB thật:
   ```json
   { "Oracle": { "Connections": { "PB9": {
     "ConnectionString": "User Id=APEX;Password=MAT_KHAU_THAT;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.67.177)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=ORCLPDB)));"
   }}}}
   ```
   File này đã được `.gitignore` bỏ qua nên không lên GitHub. Nếu chỉ cần build + unit test +
   smoke `/health` thì **không bắt buộc** bước này (health không đụng DB).

## Chạy runner

Chạy thử ngay (cửa sổ hiện log):

```bat
cd /d D:\ci\tool-excel
powershell -ExecutionPolicy Bypass -File ci_test_runner.ps1
```

Mỗi 60 giây nó `git pull`; thấy commit mới thì tự **restore → build (Release) → `dotnet test` →
chạy API kiểm `/health`**. Kết quả ghi ở `ci_test.log` (dòng cuối là `KET QUA: PASS/FAIL`).

Đổi tham số nếu cần:
```bat
powershell -ExecutionPolicy Bypass -File ci_test_runner.ps1 -RepoPath "D:\ci\tool-excel" -IntervalSeconds 30 -HealthPort 5080
```

## Cho tự chạy nền mỗi khi bật máy

```bat
cd /d D:\ci\tool-excel
powershell -ExecutionPolicy Bypass -File install_test_runner.ps1
powershell -Command "Start-ScheduledTask -TaskName 'ToolExcel CI Test Runner'"
```

> Nếu clone vào chỗ khác `D:\ci\tool-excel`, sửa biến `$RepoPath` ở đầu `install_test_runner.ps1`
> và truyền `-RepoPath` cho `ci_test_runner.ps1`.

## "Test" gồm những gì

- **Unit test** (`ToolExcel.Tests`): kiểm logic thuần — parse `EXCEL_COL` (C### → số cột),
  cờ `HEADER='X'`, trích tham số `h_*`. Không cần DB.
- **Smoke test**: build xong chạy API, gọi `GET /health` — chứng minh app khởi động được.
- **Chạy thử endpoint thật** (export/import gọi Oracle): cần bước 3 ở trên; hiện làm **thủ công**
  khi cần, ví dụ:
  ```bat
  curl "http://localhost:5080/api/bieumau/KH18/export?connId=PB9&h_BUKRS=2100&h_YEAR=2026&h_PERIOD=7" -o test.xlsx
  ```

## Ghi chú

- Runner dùng `git pull --ff-only` — nếu ai đó sửa tay trên máy test gây lệch nhánh, pull sẽ báo
  lỗi trong log; khi đó `git reset --hard origin/main` trên máy test để đồng bộ lại.
- Unit test fail hoặc smoke fail đều ghi `KET QUA: FAIL` trong `ci_test.log` để anh biết.
