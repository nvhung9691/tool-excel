# ============================================================
#  auto_push_watch.ps1
#  Theo doi thu muc du an, tu dong commit + push khi co thay doi.
#  Gom nhieu lan luu lien tiep (debounce) roi moi day 1 lan.
#  Chay: powershell -ExecutionPolicy Bypass -File auto_push_watch.ps1
# ============================================================
param(
  [string]$RepoPath = "D:\1_Claude\tool-excel",
  [int]$DebounceSeconds = 5
)

# Khong hoi mat khau/token o cua so an -> that bai nhanh thay vi treo mai
$env:GIT_TERMINAL_PROMPT = "0"

Set-Location $RepoPath
$log = Join-Path $RepoPath "auto_push.log"

function Log($m) {
  ("{0}  {1}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $m) | Add-Content -Path $log -Encoding UTF8
}

# Kiem tra da la git repo + co remote chua
if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
  Log "LOI: chua phai git repo. Chay cac buoc trong GIT_SETUP.md truoc."
  Write-Host "Chua phai git repo. Xem GIT_SETUP.md."
  exit 1
}

Log "Auto-push watcher bat dau tai $RepoPath (debounce ${DebounceSeconds}s)."

$fsw = New-Object System.IO.FileSystemWatcher
$fsw.Path = $RepoPath
$fsw.IncludeSubdirectories = $true
$fsw.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor `
                    [System.IO.NotifyFilters]::DirectoryName -bor `
                    [System.IO.NotifyFilters]::LastWrite

while ($true) {
  # Cho mot thay doi bat ky (timeout 10 phut roi lap lai de giu tien trinh song)
  $change = $fsw.WaitForChanged([System.IO.WatcherChangeTypes]::All, 600000)
  if ($change.TimedOut) { continue }

  # Debounce: tiep tuc cho cho den khi "lang" DebounceSeconds giay
  do {
    $more = $fsw.WaitForChanged([System.IO.WatcherChangeTypes]::All, ($DebounceSeconds * 1000))
  } while (-not $more.TimedOut)

  # git status tu bo qua .git, bin/obj va cac file trong .gitignore
  $dirty = git status --porcelain
  if ([string]::IsNullOrWhiteSpace($dirty)) { continue }

  Log "Co thay doi -> commit + push"
  git add -A
  git commit -m ("auto: {0}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) | Out-Null

  $push = git push 2>&1
  if ($LASTEXITCODE -eq 0) {
    Log "Push OK."
  } else {
    Log ("Push LOI: {0}" -f ($push -join ' | '))
    Log "  -> Thuong do chua luu credential. Push tay 1 lan (xem GIT_SETUP.md) roi thu lai."
  }
}
