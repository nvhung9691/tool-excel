# Đẩy dự án lên GitHub (nvhung9691)

Chạy các lệnh sau **trên máy Windows của anh** (mở **Command Prompt** hoặc **PowerShell**,
`cd` vào thư mục dự án). Không chạy trong Cowork vì ổ mount không chạy git ổn định.

```bat
cd /d D:\1_Claude\tool-excel
```

## Bước 0 — Cấu hình git (chỉ làm 1 lần cho máy)

```bat
git config --global user.name "Nguyen Viet Hung"
git config --global user.email "hungnv67@fpt.com"
```

## Bước 1 — Khởi tạo repo + commit đầu tiên

```bat
git init
git branch -M main
git add -A
git commit -m "Initial commit: ToolExcel.Api - export template + import Excel vao Oracle PB9"
```

## Bước 2 — Tạo repo trên GitHub rồi push

Chọn **một** trong hai cách.

### Cách A — Dùng GitHub CLI (gh) — gọn nhất

Cần cài `gh` (https://cli.github.com) và đăng nhập `gh auth login` một lần.

```bat
gh repo create nvhung9691/tool-excel --private --source=. --remote=origin --push
```

Xong. `--private` để repo riêng tư; muốn công khai thì đổi thành `--public`.

### Cách B — Tạo repo trên web, rồi nối remote

1. Vào https://github.com/new → **Repository name**: `tool-excel` → chọn Private/Public →
   **KHÔNG** tick "Add a README" (để trống) → **Create repository**.
2. Về máy, chạy:

```bat
git remote add origin https://github.com/nvhung9691/tool-excel.git
git push -u origin main
```

> Lần push đầu, GitHub hỏi đăng nhập: **không dùng mật khẩu tài khoản** (GitHub đã bỏ).
> Dùng **Personal Access Token (PAT)**: vào GitHub → Settings → Developer settings →
> Personal access tokens → tạo token (scope `repo`), rồi dán token đó vào ô mật khẩu khi git hỏi.
> Hoặc dùng cách A (`gh`) để khỏi phải xử lý token thủ công.

## Các lần sau — đẩy nhanh bằng 1 lệnh

Đã có sẵn `push.bat` trong thư mục:

```bat
push.bat "noi dung thay doi"
```

Nó tự chạy `git add -A` + `git commit` + `git push`. Không ghi message thì mặc định là `update`.

## Ghi chú bảo mật

- `appsettings.json` đang để mật khẩu DB là `CHANGE_ME` (placeholder) — an toàn để commit.
- **Mật khẩu DB thật** hãy để ở file `appsettings.Local.json` (đã được `.gitignore` bỏ qua,
  không bao giờ lên GitHub). ASP.NET Core tự nạp file này nếu có.
- Nếu lỡ commit mật khẩu thật rồi mới phát hiện: đổi mật khẩu đó trên Oracle ngay, vì nó đã
  nằm trong lịch sử git.
