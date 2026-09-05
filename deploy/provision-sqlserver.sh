#!/usr/bin/env bash
#
# Cài SQL Server cho NewsCMS trên Ubuntu 22.04 (VPS).
#
# Chạy bằng root, idempotent — chạy lại nhiều lần không hỏng gì:
#   sudo MSSQL_PID=Express bash deploy/provision-sqlserver.sh
#
# Script này:
#   1. Cài mssql-server 2022 + mssql-tools18 (sqlcmd) + jq.
#   2. Sinh mật khẩu SA và mật khẩu app ngẫu nhiên, lưu root-only.
#   3. Bind SQL Server vào 127.0.0.1 và KHÔNG mở 1433 ra ngoài.
#   4. Tạo database NewsCMS + login riêng cho app (không dùng SA để chạy app).
#   5. Ghi connection string vào shared/appsettings.Production.json cho deploy.yml.
#
# EDITION: mặc định Express — bản free DUY NHẤT được phép dùng cho production.
#   Giới hạn: 10GB/database, ~1.4GB buffer pool, 1 socket hoặc 4 core.
#   Developer là free nhưng CHỈ dùng cho dev/test, không được dùng production.
#   Đổi bằng: MSSQL_PID=Standard (cần license) hoặc MSSQL_PID=Developer.
set -euo pipefail

MSSQL_PID="${MSSQL_PID:-Express}"
DB_NAME="${DB_NAME:-NewsCMS}"
DB_USER="${DB_USER:-newscms_app}"
DB_COLLATION="${DB_COLLATION:-SQL_Latin1_General_CP1_CI_AS}"
DEPLOY_ROOT="${DEPLOY_ROOT:-/var/www/newscms}"
SA_PASS_FILE=/root/.newscms-mssql-sa
APP_PASS_FILE=/root/.newscms-mssql-app
SQLCMD=/opt/mssql-tools18/bin/sqlcmd

log() { printf '\n=== %s\n' "$*"; }
die() { printf '\nLỖI: %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Phải chạy bằng root."

# ---------------------------------------------------------------- preflight
log "Preflight"
. /etc/os-release
[ "${VERSION_ID:-}" = "22.04" ] || die "Script này dựng cho Ubuntu 22.04, máy đang là ${VERSION_ID:-?}."

TOTAL_MB=$(free -m | awk '/^Mem:/{print $2}')
echo "RAM: ${TOTAL_MB}MB, CPU: $(nproc) core, disk trống: $(df -h --output=avail / | tail -1 | tr -d ' ')"
# SQL Server từ chối cài nếu dưới 2GB RAM.
[ "$TOTAL_MB" -ge 2000 ] || die "SQL Server cần tối thiểu 2GB RAM, máy chỉ có ${TOTAL_MB}MB."

# Chừa RAM cho app + OS: cấp tối đa nửa RAM cho SQL Server, sàn 1024MB.
# Express dù sao cũng bị chặn ở ~1410MB nên set cao hơn là vô nghĩa.
MEM_LIMIT=$(( TOTAL_MB / 2 ))
[ "$MEM_LIMIT" -lt 1024 ] && MEM_LIMIT=1024

# Không swap + DB trên cùng máy với app là dễ bị OOM kill. Tạo swap 2GB.
if [ -z "$(swapon --show 2>/dev/null)" ]; then
  log "Chưa có swap — tạo /swapfile 2GB (tránh OOM khi SQL Server + app cùng chạy)"
  fallocate -l 2G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=2048
  chmod 600 /swapfile
  mkswap /swapfile >/dev/null
  swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
else
  echo "Swap đã có, bỏ qua."
fi

# ---------------------------------------------------------------- repo + cài
export DEBIAN_FRONTEND=noninteractive

log "Thêm repository Microsoft"
install -d -m 0755 /usr/share/keyrings
if [ ! -f /usr/share/keyrings/microsoft-prod.gpg ]; then
  curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
    | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
fi

# apt-key đã deprecated -> ghim signed-by vào từng .list.
add_repo() { # $1 = url .list, $2 = file đích
  local tmp; tmp=$(mktemp)
  curl -fsSL "$1" -o "$tmp"
  sed -i 's|^deb \[|deb [signed-by=/usr/share/keyrings/microsoft-prod.gpg |; t; s|^deb |deb [signed-by=/usr/share/keyrings/microsoft-prod.gpg] |' "$tmp"
  install -m 0644 "$tmp" "$2"
  rm -f "$tmp"
}
add_repo https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list /etc/apt/sources.list.d/mssql-server-2022.list
add_repo https://packages.microsoft.com/config/ubuntu/22.04/prod.list             /etc/apt/sources.list.d/mssql-prod.list

apt-get update -qq

log "Cài mssql-server + công cụ"
apt-get install -y -qq mssql-server
# ACCEPT_EULA cần cho msodbcsql18 / mssql-tools18 khi cài không tương tác.
ACCEPT_EULA=Y apt-get install -y -qq mssql-tools18 unixodbc-dev jq
[ -x "$SQLCMD" ] || die "Không tìm thấy sqlcmd ở $SQLCMD"

# ---------------------------------------------------------------- mật khẩu
# Alphabet cố ý bỏ ; ' " \ và khoảng trắng: ; phá connection string,
# ' phá câu T-SQL, " và \ phá JSON.
gen_pass() {
  local body
  body=$(tr -dc 'A-Za-z0-9!#%*+-.=?@^_~' </dev/urandom | head -c 28)
  # Bảo đảm đủ 3/4 nhóm ký tự theo policy của SQL Server.
  printf 'Aa1%s' "$body"
}

if [ -f "$SA_PASS_FILE" ]; then
  echo "Dùng lại mật khẩu SA đã lưu."
  SA_PASS=$(cat "$SA_PASS_FILE")
else
  SA_PASS=$(gen_pass)
  umask 077; printf '%s' "$SA_PASS" > "$SA_PASS_FILE"; chmod 600 "$SA_PASS_FILE"
fi

if [ -f "$APP_PASS_FILE" ]; then
  echo "Dùng lại mật khẩu app đã lưu."
  APP_PASS=$(cat "$APP_PASS_FILE")
else
  APP_PASS=$(gen_pass)
  umask 077; printf '%s' "$APP_PASS" > "$APP_PASS_FILE"; chmod 600 "$APP_PASS_FILE"
fi

# ---------------------------------------------------------------- service helper
# `systemctl restart mssql-server` có thể treo vĩnh viễn ở stop-sigterm:
# sqlservr không thoát, unit đứng ở "deactivating" và mọi lệnh systemctl sau đó
# block theo. Đã gặp thật khi cài. Nên tự stop có timeout + SIGKILL fallback.
stop_mssql() {
  if ! systemctl is-active --quiet mssql-server && \
     [ "$(systemctl is-active mssql-server)" != "deactivating" ]; then
    return 0
  fi
  echo "Dừng mssql-server..."
  timeout 45 systemctl stop mssql-server 2>/dev/null || true
  for _ in $(seq 1 10); do
    case "$(systemctl is-active mssql-server)" in
      inactive|failed) return 0 ;;
    esac
    sleep 2
  done
  echo "Stop bình thường không xong — SIGKILL."
  systemctl kill -s SIGKILL mssql-server 2>/dev/null || true
  pkill -9 -x sqlservr 2>/dev/null || true
  sleep 3
  systemctl reset-failed mssql-server 2>/dev/null || true
}

start_mssql() {
  systemctl reset-failed mssql-server 2>/dev/null || true
  systemctl start mssql-server
}

# ---------------------------------------------------------------- setup engine
log "Cấu hình SQL Server (edition: $MSSQL_PID)"
# Mốc nhận biết "đã setup" phải là master.mdf, KHÔNG phải mssql.conf:
# gói .deb tạo mssql.conf ngay khi cài (mục [sqlagent]) nên dựa vào nó sẽ
# skip setup -> instance không bao giờ được khởi tạo và service chết với
# "The SQL Server End-User License Agreement (EULA) must be accepted".
if [ ! -f /var/opt/mssql/data/master.mdf ]; then
  echo "Chưa khởi tạo instance — chạy setup."
  ACCEPT_EULA=Y MSSQL_SA_PASSWORD="$SA_PASS" MSSQL_PID="$MSSQL_PID" \
    /opt/mssql/bin/mssql-conf -n setup
else
  echo "Instance đã khởi tạo (master.mdf tồn tại) — bỏ qua setup."
fi

# Chỉ nghe trên loopback: app nằm cùng máy, không cần lộ 1433 ra mạng.
# Đặt config khi service đã dừng để không phải restart giữa lúc đang chạy.
stop_mssql
/opt/mssql/bin/mssql-conf set network.ipaddress 127.0.0.1 || \
  echo "CẢNH BÁO: không set được network.ipaddress (bản SQL Server có thể không hỗ trợ) — dựa vào firewall."
/opt/mssql/bin/mssql-conf set memory.memorylimitmb "$MEM_LIMIT"

systemctl enable mssql-server >/dev/null 2>&1 || true
start_mssql

log "Chờ SQL Server nhận kết nối"
ready=0
for i in $(seq 1 40); do
  if "$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PASS" -C -N -l 5 -Q "SELECT 1" >/dev/null 2>&1; then
    ready=1; echo "SQL Server đã sẵn sàng (sau ${i} lần thử)."; break
  fi
  sleep 3
done
[ "$ready" -eq 1 ] || {
  systemctl status mssql-server --no-pager 2>&1 | head -12 || true
  tail -20 /var/opt/mssql/log/errorlog 2>/dev/null || true
  echo
  echo "Gợi ý: nếu errorlog cho thấy engine vẫn chạy bình thường thì lỗi là"
  echo "đăng nhập SA — instance đã được setup trước đó bằng mật khẩu khác."
  echo "Đặt lại: systemctl stop mssql-server && ACCEPT_EULA=Y MSSQL_SA_PASSWORD='<mk>' /opt/mssql/bin/mssql-conf set-sa-password"
  die "SQL Server không phản hồi sau 120s."
}

# Ghi lại edition thật để đối chiếu với MSSQL_PID đã yêu cầu.
EDITION=$("$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PASS" -C -N -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('Edition') AS varchar(80));" 2>/dev/null | head -1 | tr -d '\r')
SRV_VER=$("$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PASS" -C -N -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(50));" 2>/dev/null | head -1 | tr -d '\r')
echo "Edition thực tế: ${EDITION:-?} (version ${SRV_VER:-?})"

# ---------------------------------------------------------------- database
log "Tạo database $DB_NAME + login $DB_USER"
SQL_TMP=$(mktemp); trap 'rm -f "$SQL_TMP"' EXIT
cat > "$SQL_TMP" <<SQL
SET NOCOUNT ON;
IF DB_ID(N'$DB_NAME') IS NULL
BEGIN
    CREATE DATABASE [$DB_NAME] COLLATE $DB_COLLATION;
    PRINT 'Đã tạo database $DB_NAME';
END
ELSE
    PRINT 'Database $DB_NAME đã tồn tại — giữ nguyên dữ liệu';
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$DB_USER')
BEGIN
    CREATE LOGIN [$DB_USER] WITH PASSWORD = '$APP_PASS',
        CHECK_POLICY = ON, DEFAULT_DATABASE = [$DB_NAME];
    PRINT 'Đã tạo login $DB_USER';
END
ELSE
BEGIN
    ALTER LOGIN [$DB_USER] WITH PASSWORD = '$APP_PASS';
    PRINT 'Đã đồng bộ lại mật khẩu login $DB_USER';
END
GO
USE [$DB_NAME];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$DB_USER')
    CREATE USER [$DB_USER] FOR LOGIN [$DB_USER];
-- db_owner: EF Core migration cần quyền DDL (CREATE/ALTER TABLE).
-- Chỉ trong database này, không phải sysadmin toàn server.
ALTER ROLE db_owner ADD MEMBER [$DB_USER];
PRINT 'Đã gán db_owner trên $DB_NAME cho $DB_USER';
GO
-- AUTO_CLOSE mặc định BẬT trên Express: DB đóng khi hết connection cuối, nên
-- request đầu sau lúc idle phải mở lại DB (chậm + spam log). Web app thì phải tắt.
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$DB_NAME' AND is_auto_close_on = 1)
BEGIN
    ALTER DATABASE [$DB_NAME] SET AUTO_CLOSE OFF WITH NO_WAIT;
    PRINT 'Đã tắt AUTO_CLOSE';
END
-- SIMPLE recovery: không có job backup transaction log nên FULL sẽ làm log
-- phình vô hạn. DB nguồn trên máy dev cũng đang SIMPLE.
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$DB_NAME' AND recovery_model_desc <> 'SIMPLE')
BEGIN
    ALTER DATABASE [$DB_NAME] SET RECOVERY SIMPLE WITH NO_WAIT;
    PRINT 'Đã chuyển recovery model sang SIMPLE';
END
GO
SQL
"$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PASS" -C -N -b -i "$SQL_TMP"

log "Kiểm tra login app kết nối được"
"$SQLCMD" -S 127.0.0.1 -U "$DB_USER" -P "$APP_PASS" -d "$DB_NAME" -C -N -b \
  -Q "SELECT DB_NAME() AS [database], SUSER_NAME() AS [login], @@VERSION AS [version];"

# ---------------------------------------------------------------- appsettings
log "Ghi connection string vào $DEPLOY_ROOT/shared/appsettings.Production.json"
install -d -m 0755 "$DEPLOY_ROOT/shared"
CFG="$DEPLOY_ROOT/shared/appsettings.Production.json"
CONN="Server=127.0.0.1,1433;Database=$DB_NAME;User Id=$DB_USER;Password=$APP_PASS;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"

[ -f "$CFG" ] || echo '{}' > "$CFG"
TMP_CFG=$(mktemp)
# --arg để jq tự escape JSON, không tự nối chuỗi.
jq --arg c "$CONN" '.ConnectionStrings.Default = $c' "$CFG" > "$TMP_CFG"
mv "$TMP_CFG" "$CFG"
# newscms có thể chưa tồn tại nếu provision-vps.sh chưa chạy.
chown root:"$(id -gn newscms 2>/dev/null || echo root)" "$CFG"
chmod 640 "$CFG"

# ---------------------------------------------------------------- firewall
log "Firewall"
# Cố ý KHÔNG chạm vào ufw ở đây: bật/tắt firewall là việc của
# provision-vps.sh. Script này chỉ đảm bảo không tự mở 1433.
echo "1433 KHÔNG được mở ra ngoài (đúng chủ ý) — SQL Server chỉ nghe loopback."
if command -v ufw >/dev/null 2>&1; then
  echo "ufw hiện tại: $(ufw status 2>/dev/null | head -1)"
fi
ss -tlnp 2>/dev/null | grep ':1433' || echo "CẢNH BÁO: không thấy 1433 listening"

cat <<DONE

======================================================================
XONG. SQL Server đã chạy.

  Edition        : $MSSQL_PID (thực tế: ${EDITION:-?}, version ${SRV_VER:-?})
  Database       : $DB_NAME (collation $DB_COLLATION)
  App login      : $DB_USER  (db_owner chỉ trên $DB_NAME)
  Bind           : 127.0.0.1:1433 — KHÔNG mở ra internet
  Memory limit   : ${MEM_LIMIT}MB
  Connection str : $CFG

Mật khẩu lưu root-only, KHÔNG in ra đây:
  SA  : $SA_PASS_FILE
  App : $APP_PASS_FILE

Xem lại connection string:  sudo jq -r '.ConnectionStrings.Default' $CFG
======================================================================
DONE
