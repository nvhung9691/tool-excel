# Go bo auto-push. Chay: powershell -ExecutionPolicy Bypass -File uninstall_autopush.ps1
$TaskName = "ToolExcel Auto Push"
Stop-ScheduledTask   -TaskName $TaskName -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Write-Host "Da go task '$TaskName' (neu co)."
