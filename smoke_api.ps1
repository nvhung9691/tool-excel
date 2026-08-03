# ============================================================
#  smoke_api.ps1  -  kiem nhan endpoint THAT (co cham Oracle)
#
#  Khac unit test: unit test la logic thuan, khong bat duoc loi chi lo ra khi
#  noi DB that (vi du ORA-01745 do ten bind trung tu khoa Oracle). Script nay
#  goi API dang chay va doi chieu ma HTTP tung ca.
#
#  Chay tay:
#     powershell -ExecutionPolicy Bypass -File smoke_api.ps1 -BaseUrl http://localhost:5080
#
#  Mat khau KHONG duoc de trong repo. Script doc mac dinh tu muc "Smoke" cua
#  backend\appsettings.Local.json (file nay da .gitignore):
#
#     "Smoke": {
#       "AdminUser": "admin",  "AdminPassword": "...",
#       "TestUser": "ci_enduser", "TestPassword": "...",
#       "Bukrs": "2100", "FormCode": "KH18", "Year": 2026, "Period": 7
#     }
#
#  Ma thoat:  0 = PASS   1 = FAIL   2 = BO QUA (chua cau hinh)
# ============================================================
param(
  [string]$BaseUrl       = "http://localhost:5080",
  [string]$ConfigPath    = "",          # rong = backend\appsettings.Local.json canh script
  [string]$AdminUser     = "",
  [string]$AdminPassword = "",
  [string]$TestUser      = "",
  [string]$TestPassword  = "",
  [string]$Bukrs         = "",          # ma don vi HOP LE (co trong danh muc chuan)
  [string]$BadBukrs      = "KHONG_CO_MA_NAY",
  [string]$FormCode      = "",
  [int]   $Year          = 0,
  [int]   $Period        = 0
)

$ErrorActionPreference = "Stop"
if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot "backend\appsettings.Local.json" }

# ---------------------------------------------------------------- cau hinh
if (Test-Path $ConfigPath) {
  try {
    $cfg = (Get-Content $ConfigPath -Raw | ConvertFrom-Json).Smoke
  } catch {
    Write-Host "smoke: KHONG doc duoc $ConfigPath ($($_.Exception.Message))"
    exit 2
  }
  if ($cfg) {
    if (-not $AdminUser     -and $cfg.AdminUser)     { $AdminUser     = $cfg.AdminUser }
    if (-not $AdminPassword -and $cfg.AdminPassword) { $AdminPassword = $cfg.AdminPassword }
    if (-not $TestUser      -and $cfg.TestUser)      { $TestUser      = $cfg.TestUser }
    if (-not $TestPassword  -and $cfg.TestPassword)  { $TestPassword  = $cfg.TestPassword }
    if (-not $Bukrs         -and $cfg.Bukrs)         { $Bukrs         = $cfg.Bukrs }
    if (-not $FormCode      -and $cfg.FormCode)      { $FormCode      = $cfg.FormCode }
    if ($Year   -eq 0       -and $cfg.Year)          { $Year          = [int]$cfg.Year }
    if ($Period -eq 0       -and $cfg.Period)        { $Period        = [int]$cfg.Period }
  }
}

if (-not $AdminUser) { $AdminUser = "admin" }
if (-not $TestUser)  { $TestUser  = "ci_enduser" }

if (-not $AdminPassword) {
  Write-Host "smoke: BO QUA - chua co Smoke:AdminPassword trong $ConfigPath (xem dau file nay)."
  exit 2
}
if (-not $Bukrs) {
  Write-Host "smoke: BO QUA - chua co Smoke:Bukrs (ma don vi hop le de doi chieu)."
  exit 2
}

# ---------------------------------------------------------------- ha tang
$script:Pass = 0
$script:Fail = 0
$script:Skip = 0

function Req {
  param($Method, $Path, $Headers, $Body, $OutFile)
  $p = @{ Uri = "$BaseUrl$Path"; Method = $Method; UseBasicParsing = $true; TimeoutSec = 60 }
  if ($Headers) { $p.Headers = $Headers }
  if ($Body)    { $p.ContentType = "application/json"; $p.Body = $Body }
  if ($OutFile) { $p.OutFile = $OutFile; $p.PassThru = $true }
  try {
    $r = Invoke-WebRequest @p
    $text = ""
    if (-not $OutFile) { $text = [string]$r.Content }
    return @{ Code = [int]$r.StatusCode; Text = $text; Type = [string]$r.Headers['Content-Type'] }
  } catch {
    $code = 0; $text = ""
    if ($_.Exception.Response) {
      $code = [int]$_.Exception.Response.StatusCode
      try {
        $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
        $text = $sr.ReadToEnd()
      } catch { }
    } else {
      $text = $_.Exception.Message
    }
    return @{ Code = $code; Text = $text; Type = "" }
  }
}

function Check {
  param($Label, $Expected, $Result, $Extra)
  $okCode = ($Result.Code -eq $Expected)
  $okMore = $true
  if ($Extra) { $okMore = & $Extra $Result }
  if ($okCode -and $okMore) {
    $script:Pass++
    Write-Host ("  PASS  {0,-46} HTTP {1}" -f $Label, $Result.Code)
  } else {
    $script:Fail++
    $t = $Result.Text
    if ($t.Length -gt 180) { $t = $t.Substring(0,180) + "..." }
    Write-Host ("  FAIL  {0,-46} HTTP {1} (mong doi {2}) {3}" -f $Label, $Result.Code, $Expected, $t)
  }
}

function TokenOf {
  param($User, $Pass)
  $r = Req POST "/api/auth/token" $null (@{ username = $User; password = $Pass } | ConvertTo-Json)
  if ($r.Code -ne 200) { return $null }
  return (ConvertFrom-Json $r.Text).accessToken
}

# ---------------------------------------------------------------- chay
Write-Host "smoke: $BaseUrl  (admin=$AdminUser, test=$TestUser, bukrs=$Bukrs)"

$adminTok = TokenOf $AdminUser $AdminPassword
if (-not $adminTok) {
  Write-Host "  FAIL  khong lay duoc token cua '$AdminUser' - dung o day."
  Write-Host "smoke: KET QUA FAIL (0 pass / 1 fail)"
  exit 1
}
$HA = @{ Authorization = "Bearer $adminTok" }

# 1. danh muc don vi phai co ma hop le
Check "GET /api/admin/orgs" 200 (Req GET "/api/admin/orgs" $HA) {
  param($r) ($r.Text -match [regex]::Escape("`"bukrs`":`"$Bukrs`""))
}

# 2. tim tai khoan thu; chua co thi tao (cham INSERT PT_USER + CREATED_BY)
$found = Req GET "/api/admin/users?q=$TestUser&pageSize=50" $HA
Check "GET /api/admin/users?q=$TestUser" 200 $found
$uid = $null
if ($found.Code -eq 200) {
  $items = (ConvertFrom-Json $found.Text).items
  foreach ($it in $items) { if ($it.username -eq $TestUser) { $uid = $it.id } }
}

if (-not $uid) {
  if (-not $TestPassword) {
    $script:Skip++
    Write-Host "  BOQUA tao '$TestUser' - chua co Smoke:TestPassword."
  } else {
    $body = @{ username = $TestUser; password = $TestPassword; fullName = "Tai khoan smoke test"
               isActive = $true; bukrs = @() } | ConvertTo-Json
    $c = Req POST "/api/admin/users" $HA $body
    Check "POST /api/admin/users (tao '$TestUser')" 201 $c
    if ($c.Code -eq 201) { $uid = (ConvertFrom-Json $c.Text).id }
  }
} else {
  Write-Host "  ..... '$TestUser' da co (id=$uid) - bo qua duong tao."
}

if ($uid) {
  # 3. sua ho ten -> cham UPDATED_BY (bind ':actor')
  Check "PUT /api/admin/users/$uid" 200 (Req PUT "/api/admin/users/$uid" $HA (
    @{ fullName = "Tai khoan smoke test"; isActive = $true } | ConvertTo-Json))

  # 4. gan don vi: ma sai phai bi tu choi, ma dung phai vao duoc
  #    (INSERT PT_USER_ORG -> cham bind ':userid')
  Check "PUT users/$uid/bukrs (ma ngoai danh muc)" 400 (Req PUT "/api/admin/users/$uid/bukrs" $HA (
    @{ bukrs = @($BadBukrs); primaryBukrs = $BadBukrs } | ConvertTo-Json))

  Check "PUT users/$uid/bukrs (ma hop le $Bukrs)" 200 (Req PUT "/api/admin/users/$uid/bukrs" $HA (
    @{ bukrs = @($Bukrs); primaryBukrs = $Bukrs } | ConvertTo-Json)) {
    param($r) ($r.Text -match [regex]::Escape($Bukrs))
  }
} else {
  $script:Skip++
  Write-Host "  BOQUA cac ca sua/gan don vi - khong xac dinh duoc id cua '$TestUser'."
}

# 5. phan quyen: tai khoan khong phai ADMIN/SUPER phai bi chan
if ($TestPassword) {
  $userTok = TokenOf $TestUser $TestPassword
  if ($userTok) {
    $HU = @{ Authorization = "Bearer $userTok" }
    Check "'$TestUser' GET /api/admin/users -> chan" 403 (Req GET "/api/admin/users" $HU)

    if ($FormCode -and $Year -gt 0 -and $Period -gt 0) {
      $qs = "connId=PB9&h_YEAR=$Year&h_PERIOD=$Period"
      $tmp = Join-Path $env:TEMP "smoke_export.xlsx"

      Check "'$TestUser' export h_BUKRS=$BadBukrs -> chan" 403 (
        Req GET "/api/bieumau/$FormCode/export?$qs&h_BUKRS=$BadBukrs" $HU) {
        param($r) ($r.Text -match "allowedBukrs")
      }

      Check "'$TestUser' export h_BUKRS=$Bukrs -> cho qua" 200 (
        Req GET "/api/bieumau/$FormCode/export?$qs&h_BUKRS=$Bukrs" $HU $null $tmp) {
        param($r) ((Test-Path $tmp) -and ((Get-Item $tmp).Length -gt 0))
      }
      Remove-Item $tmp -ErrorAction SilentlyContinue
    } else {
      $script:Skip++
      Write-Host "  BOQUA ca export - chua co Smoke:FormCode/Year/Period."
    }
  } else {
    $script:Fail++
    Write-Host "  FAIL  khong lay duoc token cua '$TestUser' (mat khau lech?)."
  }
} else {
  $script:Skip++
  Write-Host "  BOQUA cac ca 403 - chua co Smoke:TestPassword."
}

# 6. khong token phai la 401, khong phai 200
Check "GET /api/admin/orgs khong token" 401 (Req GET "/api/admin/orgs")

# ---------------------------------------------------------------- ket
Write-Host ("smoke: KET QUA {0} ({1} pass / {2} fail / {3} bo qua)" -f `
  $(if ($script:Fail -eq 0) { "PASS" } else { "FAIL" }), $script:Pass, $script:Fail, $script:Skip)

if ($script:Fail -eq 0) { exit 0 } else { exit 1 }
