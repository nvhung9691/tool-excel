# ============================================================
#  install_autopush.ps1
#  Dang ky watcher auto-push chay TU DONG moi khi dang nhap Windows.
#  Chay 1 lan (khong can quyen admin):
#    powershell -ExecutionPolicy Bypass -File install_autopush.ps1
# ============================================================
$RepoPath = "D:\1_Claude\tool-excel"
$TaskName = "ToolExcel Auto Push"
$script   = Join-Path $RepoPath "auto_push_watch.ps1"

if (-not (Test-Path $script)) {
  Write-Host "Khong thay $script"; exit 1
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
  -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""

$trigger = New-ScheduledTaskTrigger -AtLogOn

# Chay lien tuc, tu khoi dong lai neu loi, khong dung khi dung pin
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
  -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
  -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
  -Settings $settings -Description "Tu dong commit + push tool-excel khi co thay doi file" -Force | Out-Null

Write-Host "Da dang ky task '$TaskName' (chay moi lan dang nhap Windows)."
Write-Host "Chay NGAY bay gio (khong can dang xuat):"
Write-Host "    Start-ScheduledTask -TaskName '$TaskName'"
