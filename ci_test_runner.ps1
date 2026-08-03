# ============================================================
#  ci_test_runner.ps1  —  chay TREN MAY TEST (Windows, 192.168.67.170)
#  Vong lap: git pull -> neu co commit moi thi: restore + build + unit test
#            + chay thu API kiem /health. Ghi ket qua ra ci_test.log.
#  Chay:  powershell -ExecutionPolicy Bypass -File ci_test_runner.ps1
# ============================================================
param(
  [string]$RepoPath        = "C:\ci\tool-excel",   # noi da 'git clone' repo tren may test
  [int]   $IntervalSeconds = 60,                    # bao lau poll GitHub 1 lan
  [int]   $HealthPort      = 5080                    # cong chay thu API
)

$env:GIT_TERMINAL_PROMPT = "0"
Set-Location $RepoPath
$log = Join-Path $RepoPath "ci_test.log"

function Log($m) {
  $line = "{0}  {1}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $m
  Write-Host $line
  $line | Add-Content -Path $log -Encoding UTF8
}

if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
  Log "LOI: $RepoPath chua phai git repo. Chay 'git clone' truoc (xem TEST_MACHINE.md)."
  exit 1
}

# ---- 1 lan chay pipeline day du ----
function Run-Pipeline {
  Log "===== BAT DAU pipeline ====="
  $ok = $true

  Log "restore..."
  dotnet restore 2>&1 | ForEach-Object { Log "  $_" }

  Log "build (Release)..."
  dotnet build backend\ToolExcel.Api.csproj -c Release --nologo 2>&1 | ForEach-Object { Log "  $_" }
  if ($LASTEXITCODE -ne 0) { Log "BUILD FAIL -> dung pipeline."; return $false }

  Log "unit test..."
  dotnet test backend\ToolExcel.Tests\ToolExcel.Tests.csproj -c Release --nologo 2>&1 | ForEach-Object { Log "  $_" }
  if ($LASTEXITCODE -ne 0) { Log "UNIT TEST FAIL."; $ok = $false } else { Log "unit test PASS." }

  # ---- frontend: chi build khi may co Node, khong co thi bo qua (khong tinh la FAIL) ----
  if (Test-Path "frontend\package.json") {
    if (Get-Command npm -ErrorAction SilentlyContinue) {
      Log "frontend: npm ci + npm run build..."
      Push-Location frontend
      cmd /c "npm ci --no-audit --no-fund 2>&1" | ForEach-Object { Log "  $_" }
      cmd /c "npm run build 2>&1"              | ForEach-Object { Log "  $_" }
      $npmExit = $LASTEXITCODE
      Pop-Location
      if ($npmExit -ne 0) { Log "FRONTEND BUILD FAIL."; $ok = $false } else { Log "frontend build PASS." }
    } else {
      Log "frontend: bo qua (may nay chua cai Node/npm). Giao dien se khong co, API van chay."
    }
  }

  # ---- smoke: chay API, goi /health ----
  Log "smoke: khoi dong API tren cong $HealthPort ..."
  $env:ASPNETCORE_URLS = "http://localhost:$HealthPort"
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  $proc = Start-Process dotnet `
    -ArgumentList "run --project backend\ToolExcel.Api.csproj -c Release --no-build" `
    -PassThru -WindowStyle Hidden

  # /health nam sau FallbackPolicy nen KHONG co token se tra 401 — do la app da len va
  # dang bao ve dung. Chi 200 hoac 401 moi tinh la song; khong noi duoc port = chet.
  $healthy = $false
  $seen    = "khong ket noi duoc"
  foreach ($i in 1..20) {
    Start-Sleep -Seconds 2
    try {
      $r = Invoke-WebRequest -UseBasicParsing "http://localhost:$HealthPort/health" -TimeoutSec 3
      $seen = $r.StatusCode
      if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch {
      $code = $null
      if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
      if ($code) { $seen = $code }
      if ($code -eq 401) { $healthy = $true; break }
    }
  }
  if ($healthy) { Log "smoke PASS (/health = $seen; 401 = app da len, dang doi token)." }
  else          { Log "smoke FAIL (/health: $seen)."; $ok = $false }

  # ---- kiem nhan endpoint that: chi chay khi app da len VA da cau hinh Smoke ----
  # Day la buoc duy nhat cham Oracle. Unit test khong bat duoc loi kieu ORA-01745
  # (ten bind trung tu khoa) vi no chi lo ra khi noi DB that.
  if ($healthy -and (Test-Path "smoke_api.ps1")) {
    Log "smoke API: kiem endpoint that..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File "smoke_api.ps1" `
        -BaseUrl "http://localhost:$HealthPort" 2>&1 | ForEach-Object { Log "  $_" }
    switch ($LASTEXITCODE) {
      0       { Log "smoke API PASS." }
      2       { Log "smoke API BO QUA (chua cau hinh muc 'Smoke' trong appsettings.Local.json)." }
      default { Log "smoke API FAIL."; $ok = $false }
    }
  }

  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

  if ($ok) { Log "===== KET QUA: PASS =====" } else { Log "===== KET QUA: FAIL =====" }
  return $ok
}

Log "CI test runner bat dau. Repo=$RepoPath, poll moi ${IntervalSeconds}s."
$first = $true

while ($true) {
  try {
    $before = (git rev-parse HEAD 2>$null)
    git pull --ff-only 2>&1 | ForEach-Object { Log "git: $_" }
    $after = (git rev-parse HEAD 2>$null)

    if ($first -or ($before -ne $after)) {
      Log ("Commit moi: {0} -> {1}" -f $before, $after)
      Run-Pipeline | Out-Null
      $first = $false
    }
  } catch {
    Log ("LOI vong lap: {0}" -f $_.Exception.Message)
  }
  Start-Sleep -Seconds $IntervalSeconds
}
