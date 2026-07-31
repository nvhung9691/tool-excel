# Tự động đẩy lên GitHub khi có thay đổi

Cơ chế: một watcher PowerShell (`auto_push_watch.ps1`) chạy nền trên máy Windows, theo dõi thư
mục dự án. Mỗi khi anh lưu file, nó chờ **5 giây lặng** (gom các lần lưu liên tiếp) rồi tự
`git add + commit + push`. Anh không phải làm gì.

## Làm 1 lần: kết nối repo + push đầu (BẮT BUỘC)

Automation chỉ chạy được sau khi repo đã nối GitHub và Windows đã **nhớ credential**. Làm theo
`GIT_SETUP.md` — tóm tắt:

```bat
cd /d D:\1_Claude\tool-excel
git init && git branch -M main && git add -A && git commit -m "Initial commit"
gh repo create nvhung9691/tool-excel --private --source=. --remote=origin --push
```

(Không có `gh` thì dùng Cách B trong `GIT_SETUP.md`: tạo repo trên web + `git remote add` +
`git push -u origin main`, đăng nhập bằng Personal Access Token. Lần push tay này giúp Windows
**lưu token** cho các lần tự động sau.)

## Bật tự động (chạy 1 lần)

Mở **PowerShell**, chạy:

```powershell
cd D:\1_Claude\tool-excel
powershell -ExecutionPolicy Bypass -File install_autopush.ps1
```

Lệnh này đăng ký một Scheduled Task tên **"ToolExcel Auto Push"** tự chạy **mỗi khi đăng nhập
Windows**. Để chạy ngay lúc này (khỏi đăng xuất):

```powershell
Start-ScheduledTask -TaskName "ToolExcel Auto Push"
```

Xong. Từ giờ cứ sửa file trong thư mục là tự đẩy lên GitHub.

## Kiểm tra / theo dõi

- Nhật ký nằm ở `auto_push.log` trong thư mục dự án — mở ra xem lần đẩy gần nhất, có lỗi không.
- Xem task đang chạy: `Get-ScheduledTask -TaskName "ToolExcel Auto Push"`.

## Tắt tự động

```powershell
powershell -ExecutionPolicy Bypass -File uninstall_autopush.ps1
```

## Lưu ý

- Nếu `auto_push.log` báo **Push LOI** kèm "chua luu credential": push tay 1 lần theo
  `GIT_SETUP.md` (để Windows lưu token), rồi watcher sẽ tự chạy lại được.
- Watcher tự bỏ qua `.git`, `bin/`, `obj/`, `auto_push.log` và mọi file trong `.gitignore` —
  nên mật khẩu DB thật ở `backend/appsettings.Local.json` **không** bị đẩy.
- Mỗi lần đẩy tạo 1 commit `auto: <thời gian>`. Muốn gộp lại gọn thì thỉnh thoảng
  `git rebase -i` hoặc squash khi merge — tuỳ anh.
