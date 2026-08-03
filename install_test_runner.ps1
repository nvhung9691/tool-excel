# ============================================================
#  install_test_runner.ps1  —  chay TREN MAY TEST (192.168.67.170)
#  Dang ky CI runner tu chay moi khi dang nhap Windows.
#  Chay 1 lan:  powershell -ExecutionPolicy Bypass -File install_test_runner.ps1
# ============================================================
$RepoPath = "C:\ci\tool-excel"                 # sua cho khop noi clone repo
$TaskName = "ToolExcel CI Test Runner"
$script   = Join-Path $RepoPath "ci_test_runner.ps1"

if (-not (Test-Path $script)) { Write-Host "Khong thay $script"; exit 1 }

$action = New-ScheduledTaskAction -Execute "powershell.exe" `
  -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger  = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
  -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
  -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
  -Settings $settings -Description "Tu dong pull + build + test tool-excel" -Force | Out-Null

Write-Host "Da dang ky '$TaskName' (chay moi lan dang nhap)."
Write-Host "Chay ngay:  Start-ScheduledTask -TaskName '$TaskName'"
Write-Host "Go bo:      Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false"
