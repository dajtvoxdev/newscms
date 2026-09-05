#!/usr/bin/env bash
#
# Restore file .bak (từ SQL Server trên máy dev) vào SQL Server trên VPS.
#
# Idempotent: chạy lại sẽ RESTORE ... WITH REPLACE, tức GHI ĐÈ database đích.
# An toàn khi đích là bản copy của nguồn; NGUY HIỂM nếu đích đã có dữ liệu mới
# hơn (dữ liệu đó sẽ mất). Vì vậy script luôn dump số bảng/số dòng của đích
# trước khi ghi đè để còn đối chiếu.
#
# Dùng: ./restore-db-on-vps.sh /var/opt/mssql/backup/NewsCMS.bak NewsCMS
#
set -euo pipefail

BAK="${1:?thiếu đường dẫn file .bak}"
DB="${2:-NewsCMS}"
APP_LOGIN="${3:-newscms_app}"

SQLCMD="$(command -v sqlcmd || echo /opt/mssql-tools18/bin/sqlcmd)"
[ -x "$SQLCMD" ] || { echo "Không tìm thấy sqlcmd" >&2; exit 1; }

SA_PW_FILE=/root/.newscms-mssql-sa
[ -f "$SA_PW_FILE" ] || { echo "Thiếu $SA_PW_FILE" >&2; exit 1; }
SA_PW="$(cat "$SA_PW_FILE")"

[ -f "$BAK" ] || { echo "Không thấy file backup: $BAK" >&2; exit 1; }

DATA_DIR=/var/opt/mssql/data

# -C: sqlcmd 18 mặc định Encrypt=Mandatory, cert của SQL Server là self-signed.
# -b: thoát với exit code khác 0 khi SQL lỗi (không im lặng bỏ qua).
sq() { "$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PW" -C -b -h -1 -W "$@"; }

echo "=== [1/6] Kiểm tra kết nối + trạng thái đích trước khi ghi đè ==="
sq -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(30), SERVERPROPERTY('ProductVersion'));"
before_tables=$(sq -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM [$DB].sys.tables;" 2>/dev/null | tr -d '[:space:]' || echo 0)
before_rows=$(sq -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(row_count),0) FROM [$DB].sys.dm_db_partition_stats ps JOIN [$DB].sys.tables t ON t.object_id=ps.object_id WHERE ps.index_id IN (0,1);" 2>/dev/null | tr -d '[:space:]' || echo 0)
echo "Đích hiện tại: ${before_tables:-0} bảng, ${before_rows:-0} dòng  (sẽ bị ghi đè)"

echo "=== [2/6] Đọc danh sách file logic trong backup ==="
# Không hardcode tên file logic - lấy thẳng từ backup.
mapfile -t FILELIST < <(sq -s '|' -W -Q \
  "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK='$BAK';" \
  | awk -F'|' 'NF>2 {print $1 "|" $3}')

MOVE_CLAUSES=""
for row in "${FILELIST[@]}"; do
  logical="${row%%|*}"
  ftype="${row##*|}"
  case "$ftype" in
    D) target="$DATA_DIR/${DB}.mdf" ;;
    L) target="$DATA_DIR/${DB}_log.ldf" ;;
    *) echo "Bỏ qua file type lạ: $row"; continue ;;
  esac
  echo "  MOVE '$logical' -> $target"
  MOVE_CLAUSES="$MOVE_CLAUSES, MOVE '$logical' TO '$target'"
done
[ -n "$MOVE_CLAUSES" ] || { echo "Không đọc được file list từ backup" >&2; exit 1; }

echo "=== [3/6] Kiểm tra tính toàn vẹn của backup ==="
sq -Q "RESTORE VERIFYONLY FROM DISK='$BAK' WITH CHECKSUM;"

echo "=== [4/6] Restore ==="
# SINGLE_USER + ROLLBACK IMMEDIATE: cắt mọi connection đang giữ DB, nếu không
# RESTORE sẽ báo "database is in use". Bọc IF EXISTS vì lần đầu DB có thể chưa có.
sq -Q "IF DB_ID('$DB') IS NOT NULL
       BEGIN
         ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
       END"
sq -Q "RESTORE DATABASE [$DB] FROM DISK='$BAK'
       WITH REPLACE, RECOVERY, STATS=10 ${MOVE_CLAUSES};"
sq -Q "ALTER DATABASE [$DB] SET MULTI_USER;"

echo "=== [5/6] Chuẩn hoá thiết lập DB + map lại login cho app ==="
# AUTO_CLOSE: mặc định Express bật; DB đóng khi hết connection cuối -> request
# đầu sau lúc idle phải mở lại DB (chậm, và làm collation_name trả NULL).
# SIMPLE: không có job backup log -> FULL sẽ làm log phình vô hạn.
sq -Q "ALTER DATABASE [$DB] SET AUTO_CLOSE OFF;
       ALTER DATABASE [$DB] SET RECOVERY SIMPLE;"

# Sau RESTORE, user trong DB đến từ máy nguồn nên KHÔNG map tới login trên VPS
# (khác SID) -> app login được vào server nhưng không có quyền trên DB.
# Xử lý cả 2 trường hợp: user đã tồn tại (orphan) thì ALTER, chưa có thì CREATE.
sq -Q "USE [$DB];
       IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name='$APP_LOGIN' AND type IN ('S','U'))
         ALTER USER [$APP_LOGIN] WITH LOGIN = [$APP_LOGIN];
       ELSE
         CREATE USER [$APP_LOGIN] FOR LOGIN [$APP_LOGIN];
       IF IS_ROLEMEMBER('db_owner','$APP_LOGIN') = 0
         ALTER ROLE db_owner ADD MEMBER [$APP_LOGIN];"

echo "--- user Windows từ máy nguồn (orphan, vô hại - chỉ để biết) ---"
sq -Q "SET NOCOUNT ON; USE [$DB];
       SELECT name FROM sys.database_principals
       WHERE type IN ('U','G') AND name LIKE '%\\%' ESCAPE '\\';" || true

echo "=== [6/6] Xác minh sau restore ==="
# KHÔNG concat các cột này: collation của server (Latin1_General_CI_AS_KS_WS)
# khác collation của DB vừa restore (SQL_Latin1_General_CP1_CI_AS) nên toán tử
# + sẽ báo collation conflict. Trả về từng cột riêng.
sq -s '|' -Q "SET NOCOUNT ON;
       SELECT state_desc, collation_name, recovery_model_desc,
              compatibility_level, CONVERT(int, is_auto_close_on)
       FROM sys.databases WHERE name='$DB';"
after_tables=$(sq -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM [$DB].sys.tables;" | tr -d '[:space:]')
after_rows=$(sq -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(row_count),0) FROM [$DB].sys.dm_db_partition_stats ps JOIN [$DB].sys.tables t ON t.object_id=ps.object_id WHERE ps.index_id IN (0,1);" | tr -d '[:space:]')
echo "TARGET_TABLES=$after_tables"
echo "TARGET_ROWS=$after_rows"

# Kết nối bằng đúng credential mà app dùng - bắt lỗi phân quyền ngay tại đây
# thay vì để app chết lúc khởi động.
if [ -f /root/.newscms-mssql-app ]; then
  APP_PW="$(cat /root/.newscms-mssql-app)"
  if "$SQLCMD" -S 127.0.0.1 -U "$APP_LOGIN" -P "$APP_PW" -d "$DB" -C -b -h -1 -W -s '|' \
       -Q "SET NOCOUNT ON; SELECT DB_NAME(), SUSER_SNAME(), COUNT(*) FROM sys.tables;"; then
    echo "APP_LOGIN_CHECK=OK"
  else
    echo "APP_LOGIN_CHECK=FAILED" >&2
    exit 1
  fi
fi

echo "RESTORE_DONE"
