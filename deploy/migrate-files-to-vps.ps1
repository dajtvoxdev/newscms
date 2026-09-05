<#
  Migrate dữ liệu FILE từ máy dev lên VPS - chạy trực tiếp qua SSH, không qua
  GitHub Actions.

      powershell -ExecutionPolicy Bypass -File deploy\migrate-files-to-vps.ps1

  Chuyển 2 nhóm:
    wwwroot/uploads  ->  /var/www/newscms/shared/uploads
    App_Data         ->  /var/www/newscms/shared/App_Data

  Vì sao là shared/ chứ không phải trong release: deploy tạo releases/<sha> mới
  mỗi lần rồi đổi symlink `current`, nên mọi thứ nằm trong release sẽ mất sau
  lần deploy kế tiếp. App_Data chứa DataProtection key ring (mất là logout toàn
  bộ session) và state của Telegram bot (mất là bot gửi lại thông báo cũ).

  Idempotent: mỗi thư mục con được so (số file, tổng byte) giữa nguồn và đích,
  khớp thì bỏ qua. Nhờ vậy chạy lại sau khi đứt mạng chỉ đẩy phần còn thiếu.

  Truyền bằng tar + scp một archive cho mỗi thư mục con, không scp từng file:
  4767 file mà scp lẻ thì mỗi file một lượt round-trip, rất chậm. rsync không
  có trên Windows nên không dùng được.
#>
[CmdletBinding()]
param(
    [string]$VpsHost    = '110.172.29.240',
    [string]$VpsUser    = 'root',
    [string]$SshKey     = "$env:USERPROFILE\.ssh\hailuunguoc_vps",
    [string]$WebRoot    = 'D:\hailuunguoc\NewsCMS.Core\src\NewsCMS.Web',
    [string]$DeployRoot = '/var/www/newscms',
    [string]$AppUser    = 'newscms',
    [string]$WorkDir    = "$env:TEMP\newscms-migrate-files",
    [switch]$ReUpload
)

$ErrorActionPreference = 'Stop'

$sshArgs = @('-i', $SshKey, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=20')
$remote  = "$VpsUser@$VpsHost"

function Invoke-Ssh {
    param([string]$Command, [switch]$AllowFail)
    # Native command ghi stderr sẽ thành terminating error khi
    # $ErrorActionPreference='Stop' -> hạ xuống Continue rồi tự xét exit code.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $r = & ssh @sshArgs $remote $Command 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prev }
    if ($code -ne 0 -and -not $AllowFail) {
        $r | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Lệnh trên VPS thất bại (exit $code): $Command"
    }
    return @($r)
}

function Get-LocalStat {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return @{ Count = 0; Bytes = 0 } }
    $m = Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue |
         Measure-Object Length -Sum
    return @{ Count = [int]$m.Count; Bytes = [int64]($m.Sum) }
}

function Get-RemoteStat {
    param([string]$Path)
    $out = Invoke-Ssh "bash /tmp/vps-dir-stat.sh '$Path'"
    $line = ($out | Where-Object { $_ -match '^\d+\|\d+$' } | Select-Object -First 1)
    if (-not $line) { return @{ Count = -1; Bytes = -1 } }
    $p = $line -split '\|'
    return @{ Count = [int]$p[0]; Bytes = [int64]$p[1] }
}

# Đẩy một thư mục con: tar -> scp -> extract. Trả về $true nếu đã đẩy.
function Send-Dir {
    param(
        [string]$LocalParent,
        [string]$Name,
        [string]$RemoteParent
    )
    $localPath = Join-Path $LocalParent $Name
    $ls = Get-LocalStat $localPath
    $rs = Get-RemoteStat "$RemoteParent/$Name"

    if (-not $ReUpload -and $ls.Count -eq $rs.Count -and $ls.Bytes -eq $rs.Bytes) {
        Write-Host ("  {0,-16} SKIP  (đã khớp: {1} file / {2:N1} MB)" -f $Name, $ls.Count, ($ls.Bytes / 1MB))
        return $false
    }

    Write-Host ("  {0,-16} đẩy {1} file / {2:N1} MB  (đích đang có {3} file)" -f `
        $Name, $ls.Count, ($ls.Bytes / 1MB), $rs.Count)

    $tarPath = Join-Path $WorkDir "$Name.tar"
    if (Test-Path $tarPath) { Remove-Item $tarPath -Force }

    # -C <parent> <name>: giữ đúng một cấp thư mục trong archive để extract vào
    # đích ra đường dẫn giống nguồn.
    & tar -cf $tarPath -C $LocalParent $Name
    if ($LASTEXITCODE -ne 0) { throw "tar thất bại cho $Name" }
    $tarMb = [math]::Round((Get-Item $tarPath).Length / 1MB, 1)

    $sw = [Diagnostics.Stopwatch]::StartNew()
    & scp -i $SshKey -o BatchMode=yes $tarPath "${remote}:/tmp/$Name.tar"
    if ($LASTEXITCODE -ne 0) { throw "scp thất bại cho $Name" }
    $sw.Stop()

    Invoke-Ssh "mkdir -p '$RemoteParent' && tar -xf '/tmp/$Name.tar' -C '$RemoteParent' && rm -f '/tmp/$Name.tar'" | Out-Null
    Remove-Item $tarPath -Force

    $rs2 = Get-RemoteStat "$RemoteParent/$Name"
    $ok = ($rs2.Count -eq $ls.Count -and $rs2.Bytes -eq $ls.Bytes)
    Write-Host ("  {0,-16} {1}  ({2} MB trong {3:N0}s, đích: {4} file / {5:N1} MB)" -f `
        $Name, $(if ($ok) { 'OK' } else { 'LỆCH' }), $tarMb, $sw.Elapsed.TotalSeconds,
        $rs2.Count, ($rs2.Bytes / 1MB))
    if (-not $ok) { throw "Sau khi đẩy, $Name vẫn lệch: nguồn $($ls.Count)/$($ls.Bytes) vs đích $($rs2.Count)/$($rs2.Bytes)" }
    return $true
}

# --------------------------------------------------------------------- setup
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "`n=== [1/5] Preflight ===" -ForegroundColor Cyan
Invoke-Ssh 'echo SSH_OK' | Out-Null
& tar --version | Select-Object -First 1 | ForEach-Object { Write-Host "tar: $_" }
& scp -i $SshKey -o BatchMode=yes (Join-Path $scriptDir 'vps-dir-stat.sh') "${remote}:/tmp/vps-dir-stat.sh" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'scp vps-dir-stat.sh thất bại' }

$uploadsLocal = Join-Path $WebRoot 'wwwroot\uploads'
$appDataLocal = Join-Path $WebRoot 'App_Data'
if (-not (Test-Path $uploadsLocal)) { throw "Không thấy $uploadsLocal" }

$srcTotal = Get-LocalStat $uploadsLocal
Write-Host ("Nguồn uploads: {0} file / {1:N1} MB" -f $srcTotal.Count, ($srcTotal.Bytes / 1MB))

Invoke-Ssh "mkdir -p '$DeployRoot/shared/uploads' '$DeployRoot/shared/App_Data'" | Out-Null

# --------------------------------------------------------------------- uploads
Write-Host "`n=== [2/5] Đẩy wwwroot/uploads -> shared/uploads ===" -ForegroundColor Cyan

$sent = 0
foreach ($d in (Get-ChildItem $uploadsLocal -Directory | Sort-Object Name)) {
    if (Send-Dir -LocalParent $uploadsLocal -Name $d.Name -RemoteParent "$DeployRoot/shared/uploads") { $sent++ }
}

# File lẻ ngay dưới uploads/ (ví dụ .gitkeep) - scp trực tiếp, không cần tar.
$looseFiles = @(Get-ChildItem $uploadsLocal -File)
if ($looseFiles.Count -gt 0) {
    foreach ($f in $looseFiles) {
        & scp -i $SshKey -o BatchMode=yes $f.FullName "${remote}:$DeployRoot/shared/uploads/$($f.Name)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "scp $($f.Name) thất bại" }
    }
    Write-Host ("  {0,-16} OK    ({1} file lẻ ở gốc uploads)" -f '(root files)', $looseFiles.Count)
}

# --------------------------------------------------------------------- App_Data
Write-Host "`n=== [3/5] Đẩy App_Data -> shared/App_Data ===" -ForegroundColor Cyan

if (Test-Path $appDataLocal) {
    # tmp/ và tus-uploads/ là dữ liệu tạm trong lúc upload - không migrate.
    $skipDirs = @('tmp', 'tus-uploads', 'keys')
    foreach ($d in (Get-ChildItem $appDataLocal -Directory | Sort-Object Name)) {
        if ($skipDirs -contains $d.Name) {
            Write-Host ("  {0,-16} BỎ QUA (dữ liệu tạm / key sinh lại được)" -f $d.Name)
            continue
        }
        [void](Send-Dir -LocalParent $appDataLocal -Name $d.Name -RemoteParent "$DeployRoot/shared/App_Data")
    }

    # State file của Telegram bot: mất thì bot gửi lại toàn bộ thông báo cũ.
    $stateFiles = @(Get-ChildItem $appDataLocal -File)
    foreach ($f in $stateFiles) {
        & scp -i $SshKey -o BatchMode=yes $f.FullName "${remote}:$DeployRoot/shared/App_Data/$($f.Name)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "scp $($f.Name) thất bại" }
        Write-Host ("  {0,-16} OK    ({1:N1} KB)" -f $f.Name, ($f.Length / 1KB))
    }
} else {
    Write-Host 'Không có App_Data ở máy dev - bỏ qua'
}

# --------------------------------------------------------------------- perms
Write-Host "`n=== [4/5] Đặt quyền cho user $AppUser ===" -ForegroundColor Cyan
Invoke-Ssh "chown -R ${AppUser}:${AppUser} '$DeployRoot/shared' && chmod -R u=rwX,g=rX,o= '$DeployRoot/shared/uploads' '$DeployRoot/shared/App_Data' && ls -ld '$DeployRoot/shared/uploads' '$DeployRoot/shared/App_Data'" |
    ForEach-Object { Write-Host "  $_" }

# --------------------------------------------------------------------- verify
Write-Host "`n=== [5/5] Đối chiếu tổng thể ===" -ForegroundColor Cyan

$tgtUploads = Get-RemoteStat "$DeployRoot/shared/uploads"
Write-Host ("uploads  nguồn: {0} file / {1:N1} MB" -f $srcTotal.Count, ($srcTotal.Bytes / 1MB))
Write-Host ("uploads  đích : {0} file / {1:N1} MB" -f $tgtUploads.Count, ($tgtUploads.Bytes / 1MB))

$fail = @()
if ($tgtUploads.Count -ne $srcTotal.Count) { $fail += "uploads lệch số file: nguồn=$($srcTotal.Count) đích=$($tgtUploads.Count)" }
if ($tgtUploads.Bytes -ne $srcTotal.Bytes) { $fail += "uploads lệch dung lượng: nguồn=$($srcTotal.Bytes) đích=$($tgtUploads.Bytes)" }

if (Test-Path $appDataLocal) {
    $tgtApp = Get-RemoteStat "$DeployRoot/shared/App_Data"
    Write-Host ("App_Data đích : {0} file / {1:N1} MB" -f $tgtApp.Count, ($tgtApp.Bytes / 1MB))
}

Invoke-Ssh 'rm -f /tmp/vps-dir-stat.sh' -AllowFail | Out-Null

if ($fail.Count -gt 0) {
    $fail | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    throw 'Dữ liệu file nguồn và đích KHÔNG khớp'
}
Write-Host 'KHỚP HOÀN TOÀN' -ForegroundColor Green
Write-Host "MIGRATE_FILES_DONE (đã đẩy $sent thư mục uploads)"
