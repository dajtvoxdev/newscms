<#
  Migrate database NewsCMS từ SQL Server trên máy dev lên SQL Server trên VPS.
  Chạy trực tiếp từ máy dev qua SSH - KHÔNG đi qua GitHub Actions.

      powershell -ExecutionPolicy Bypass -File deploy\migrate-db-to-vps.ps1

  Luồng: BACKUP (COPY_ONLY) -> VERIFYONLY -> scp -> RESTORE trên VPS
         -> đối chiếu số bảng + số dòng từng bảng giữa nguồn và đích.

  CẢNH BÁO: bước restore dùng WITH REPLACE nên GHI ĐÈ database trên VPS. Nếu đích
  đã có bảng, script dừng lại; phải truyền -Force mới ghi đè.

  Backup là COPY_ONLY nên không phá chuỗi backup hiện có trên máy dev và chạy
  online - không cần tắt app. Nhưng dữ liệu ghi SAU thời điểm backup sẽ không
  được chuyển; muốn khớp tuyệt đối thì dừng app trước khi chạy.
#>
[CmdletBinding()]
param(
    [string]$VpsHost      = '110.172.29.240',
    [string]$VpsUser      = 'root',
    [string]$SshKey       = "$env:USERPROFILE\.ssh\hailuunguoc_vps",
    [string]$SourceServer = '.',
    [string]$Database     = 'NewsCMS',
    [string]$WorkDir      = "$env:TEMP\newscms-migrate",
    [switch]$Force,
    [switch]$SkipBackup
)

$ErrorActionPreference = 'Stop'

# sqlcmd trên máy này không dùng được (thiếu ODBC Driver 17) nên nói chuyện với
# SQL Server bằng SqlClient của .NET - không phụ thuộc ODBC.
function Invoke-Sql {
    param([string]$ConnStr, [string]$Sql, [int]$TimeoutSec = 3600)
    $cn = New-Object System.Data.SqlClient.SqlConnection $ConnStr
    $cn.Open()
    try {
        $out = @()
        $cmd = $cn.CreateCommand()
        $cmd.CommandText = $Sql
        $cmd.CommandTimeout = $TimeoutSec
        $rdr = $cmd.ExecuteReader()
        try {
            do {
                while ($rdr.Read()) {
                    $vals = @()
                    for ($i = 0; $i -lt $rdr.FieldCount; $i++) { $vals += [string]$rdr.GetValue($i) }
                    $out += ($vals -join '|')
                }
            } while ($rdr.NextResult())
        } finally { $rdr.Dispose() }
        return $out
    # Lưu ý: PowerShell "unroll" mảng 1 phần tử thành scalar, khi đó [0] trả về
    # ký tự đầu của string thay vì cả dòng -> mọi call site đều bọc @(...).
    } finally { $cn.Close() }
}

function Step($n, $msg) { Write-Host "`n=== [$n/7] $msg ===" -ForegroundColor Cyan }

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcConn   = "Server=$SourceServer;Database=master;Integrated Security=True;TrustServerCertificate=True;Encrypt=False"
$sshArgs   = @('-i', $SshKey, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=20')
$remote    = "$VpsUser@$VpsHost"
$bakName   = "$Database.bak"

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$localBak = Join-Path $WorkDir $bakName

function Invoke-Ssh {
    param([string]$Command, [switch]$AllowFail)
    # Native command ghi ra stderr sẽ thành terminating error khi
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
    return $r
}

# Đẩy một đoạn SQL lên VPS dạng file rồi chạy bằng -f.
# KHÔNG truyền SQL inline qua ssh: ký tự như COUNT(*) hay [] bị bash hiểu là
# cú pháp shell vì PowerShell không quote được khi gọi native command.
function Invoke-VpsSql {
    param([string]$Sql, [string]$Name, [switch]$AllowFail)
    $local = Join-Path $WorkDir $Name
    $Sql | Set-Content -Encoding ascii $local
    & scp -i $SshKey -o BatchMode=yes $local "${remote}:/tmp/$Name" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "scp $Name thất bại" }
    return (Invoke-Ssh "bash /tmp/vps-sql.sh -f /tmp/$Name" -AllowFail:$AllowFail)
}

# Kiểm kê dùng chung cho cả 2 phía để so sánh đúng kiểu.
# dm_db_partition_stats.row_count là số dòng chính xác (sysindexes.rows chỉ xấp
# xỉ). index_id 0=heap, 1=clustered -> mỗi bảng đúng một dòng kết quả.
$inventorySql = @"
SET NOCOUNT ON;
SELECT s.name + '.' + t.name, SUM(ps.row_count)
FROM [$Database].sys.tables t
JOIN [$Database].sys.schemas s ON s.schema_id = t.schema_id
JOIN [$Database].sys.dm_db_partition_stats ps ON ps.object_id = t.object_id
WHERE ps.index_id IN (0, 1)
GROUP BY s.name, t.name
ORDER BY 1;
"@

# ------------------------------------------------------------------ preflight
Step 1 'Preflight'

$dbState = @(Invoke-Sql $srcConn "SET NOCOUNT ON; SELECT state_desc, recovery_model_desc, collation_name, compatibility_level FROM sys.databases WHERE name = '$Database';")
if ($dbState.Count -eq 0) { throw "Không thấy database [$Database] trên '$SourceServer'" }
Write-Host "Nguồn: $($dbState[0])"

# Enterprise-only feature sẽ làm Express từ chối restore -> chặn ngay từ đây
# thay vì để fail sau khi đã truyền xong file.
$sku = @(Invoke-Sql $srcConn "SET NOCOUNT ON; SELECT feature_name FROM [$Database].sys.dm_db_persisted_sku_features;")
if ($sku.Count -gt 0) { throw "DB dùng feature không có trên Express: $($sku -join ', ')" }
Write-Host 'Không có Enterprise-only feature -> Express restore được'

Invoke-Ssh 'echo SSH_OK' | Out-Null
Write-Host 'SSH OK'

foreach ($f in 'vps-sql.sh', 'restore-db-on-vps.sh') {
    & scp -i $SshKey -o BatchMode=yes (Join-Path $scriptDir $f) "${remote}:/tmp/$f" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "scp $f thất bại" }
}
Write-Host 'Đã đẩy script helper lên /tmp'

# ------------------------------------------------------------------ guard
Step 2 'Kiểm tra đích trước khi ghi đè'

$tgtRaw = @(Invoke-VpsSql -Name 'count-tables.sql' -AllowFail -Sql @"
SET NOCOUNT ON;
SELECT ISNULL((SELECT COUNT(*) FROM [$Database].sys.tables), -1);
"@
)
$tgtTables = 0
if ($tgtRaw) {
    $first = ($tgtRaw | Where-Object { $_ -match '^\s*-?\d+\s*$' } | Select-Object -First 1)
    if ($first) { $tgtTables = [int]$first.Trim() }
}
Write-Host "Đích hiện có: $tgtTables bảng"

if ($tgtTables -gt 0 -and -not $Force) {
    throw "Database [$Database] trên VPS đã có $tgtTables bảng. Restore sẽ GHI ĐÈ và làm mất dữ liệu đó. Chạy lại với -Force nếu chắc chắn."
}

# ------------------------------------------------------------------ backup
Step 3 'Backup database trên máy dev'

if ($SkipBackup -and (Test-Path $localBak)) {
    Write-Host "Dùng lại backup có sẵn: $localBak"
} else {
    # SQL Server ghi file backup bằng service account của nó (NT Service\MSSQL$...)
    # chứ không phải user chạy script, nên phải ghi vào thư mục mà service có
    # quyền. Thư mục Backup mặc định của instance là chắc ăn nhất.
    $mdf = @(Invoke-Sql $srcConn "SET NOCOUNT ON; SELECT physical_name FROM [$Database].sys.database_files WHERE type_desc='ROWS';")[0]
    $dataDir = Split-Path $mdf -Parent
    $sqlBakDir = Join-Path (Split-Path $dataDir -Parent) 'Backup'
    if (-not (Test-Path $sqlBakDir)) { $sqlBakDir = $dataDir }
    $serverBak = Join-Path $sqlBakDir $bakName
    Write-Host "SQL Server ghi backup ra: $serverBak"

    # COPY_ONLY   : không phá chuỗi backup hiện có trên máy dev.
    # CHECKSUM    : để RESTORE VERIFYONLY kiểm tra thực chất chứ không chỉ đọc header.
    # COMPRESSION : Developer Edition tạo được; Express không tạo được nhưng
    #               RESTORE backup nén thì được -> phía VPS vẫn ổn.
    Invoke-Sql $srcConn "BACKUP DATABASE [$Database] TO DISK = N'$serverBak' WITH COPY_ONLY, INIT, FORMAT, CHECKSUM, COMPRESSION, NAME = N'$Database migrate to VPS';" | ForEach-Object { Write-Host "  $_" }

    Write-Host 'Kiểm tra toàn vẹn backup (RESTORE VERIFYONLY)...'
    Invoke-Sql $srcConn "RESTORE VERIFYONLY FROM DISK = N'$serverBak' WITH CHECKSUM;" | ForEach-Object { Write-Host "  $_" }

    Copy-Item $serverBak $localBak -Force
    Remove-Item $serverBak -Force -ErrorAction SilentlyContinue
}

$bakMb = [math]::Round((Get-Item $localBak).Length / 1MB, 1)
Write-Host "Backup: $localBak ($bakMb MB)"

# ------------------------------------------------------------------ inventory
# Kiểm kê NGAY SAU backup để cửa sổ lệch nhỏ nhất: app trên máy dev vẫn đang
# chạy và ghi DB, nên mọi dòng ghi sau thời điểm backup sẽ hiện ra ở bước đối
# chiếu dưới dạng "nguồn nhiều hơn đích". Muốn khớp tuyệt đối thì tắt app trước.
Step 4 'Kiểm kê dữ liệu nguồn'

$srcInv = @(Invoke-Sql $srcConn $inventorySql)
$srcInvFile = Join-Path $WorkDir 'source-inventory.txt'
$srcInv | Set-Content -Encoding utf8 $srcInvFile

$srcMap = @{}
foreach ($l in $srcInv) {
    $p = $l -split '\|'
    if ($p.Count -ge 2) { $srcMap[$p[0].Trim()] = [int]$p[1].Trim() }
}
Write-Host "Nguồn: $($srcMap.Count) bảng, $(($srcMap.Values | Measure-Object -Sum).Sum) dòng"

# ------------------------------------------------------------------ transfer
Step 5 'Đẩy backup lên VPS'

Invoke-Ssh 'mkdir -p /var/opt/mssql/backup && chown mssql:mssql /var/opt/mssql/backup && chmod 750 /var/opt/mssql/backup' | Out-Null

$sw = [Diagnostics.Stopwatch]::StartNew()
& scp -i $SshKey -o BatchMode=yes $localBak "${remote}:/var/opt/mssql/backup/$bakName"
if ($LASTEXITCODE -ne 0) { throw 'scp file backup thất bại' }
$sw.Stop()
Write-Host ("Đã truyền $bakMb MB trong {0:N0}s" -f $sw.Elapsed.TotalSeconds)

# SQL Server chạy bằng user mssql nên phải đọc được file backup.
Invoke-Ssh "chown mssql:mssql /var/opt/mssql/backup/$bakName && chmod 640 /var/opt/mssql/backup/$bakName && ls -l /var/opt/mssql/backup/$bakName" | ForEach-Object { Write-Host "  $_" }

# ------------------------------------------------------------------ restore
Step 6 'Restore trên VPS'

Invoke-Ssh "bash /tmp/restore-db-on-vps.sh /var/opt/mssql/backup/$bakName $Database" | ForEach-Object { Write-Host "  $_" }

# ------------------------------------------------------------------ verify
Step 7 'Đối chiếu nguồn vs đích'

$tgtInv = @(Invoke-VpsSql -Sql $inventorySql -Name 'inventory.sql')
$tgtMap = @{}
foreach ($l in $tgtInv) {
    if ($l -notmatch '\|') { continue }
    $p = $l -split '\|'
    if ($p.Count -ge 2 -and $p[1].Trim() -match '^\d+$') { $tgtMap[$p[0].Trim()] = [int]$p[1].Trim() }
}

$diffs = @()
foreach ($k in $srcMap.Keys) {
    if (-not $tgtMap.ContainsKey($k)) { $diffs += "THIẾU BẢNG Ở ĐÍCH : $k (nguồn $($srcMap[$k]) dòng)" }
    elseif ($tgtMap[$k] -ne $srcMap[$k]) { $diffs += "LỆCH SỐ DÒNG      : $k nguồn=$($srcMap[$k]) đích=$($tgtMap[$k])" }
}
foreach ($k in $tgtMap.Keys) {
    if (-not $srcMap.ContainsKey($k)) { $diffs += "BẢNG LẠ Ở ĐÍCH    : $k ($($tgtMap[$k]) dòng)" }
}

Write-Host ''
Write-Host "Nguồn : $($srcMap.Count) bảng, $(($srcMap.Values | Measure-Object -Sum).Sum) dòng"
Write-Host "Đích  : $($tgtMap.Count) bảng, $(($tgtMap.Values | Measure-Object -Sum).Sum) dòng"

Invoke-Ssh "rm -f /var/opt/mssql/backup/$bakName /tmp/restore-db-on-vps.sh /tmp/vps-sql.sh /tmp/inventory.sql /tmp/count-tables.sql" | Out-Null

if ($diffs.Count -gt 0) {
    Write-Host "CÓ $($diffs.Count) khác biệt:" -ForegroundColor Yellow
    $diffs | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    throw 'Dữ liệu nguồn và đích KHÔNG khớp'
}

Write-Host 'KHỚP HOÀN TOÀN' -ForegroundColor Green
Write-Host "MIGRATE_DB_DONE (backup local giữ lại: $localBak)"
