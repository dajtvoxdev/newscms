#!/usr/bin/env bash
#
# Provision Ubuntu 22.04 VPS để chạy NewsCMS.Web + nhận deploy từ GitHub Actions.
#
# Chạy MỘT LẦN trên VPS với quyền root:
#   bash provision-vps.sh
#
# Idempotent: chạy lại nhiều lần không hỏng gì.
#
# Layout sau khi provision (kiểu Capistrano - swap bằng symlink, rollback nhanh):
#   /var/www/newscms/
#   ├── releases/<git-sha>/     mỗi lần deploy là 1 thư mục mới
#   ├── current -> releases/…   symlink, systemd trỏ vào đây
#   └── shared/                 dữ liệu KHÔNG bị ghi đè khi deploy
#       ├── appsettings.Production.json   (secret thật, chỉ nằm trên server)
#       ├── uploads/                      (media user upload)
#       └── logs/
#
set -euo pipefail

APP_NAME=newscms
APP_USER=newscms
DEPLOY_ROOT=/var/www/newscms
APP_PORT=5000
DOTNET_CHANNEL=8.0

log() { printf '\n=== %s\n' "$*"; }

if [[ $EUID -ne 0 ]]; then
  echo "Phải chạy bằng root." >&2
  exit 1
fi

# ── 1. Base packages ────────────────────────────────────────────────────────
log "1/7 Cài package cơ bản"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq curl ca-certificates gnupg rsync nginx ufw ffmpeg jq

# ffmpeg: NewsCMS dùng để transcode video upload.
# jq:     deploy đọc connection string từ appsettings.Production.json để chạy
#         EF migration, nhờ vậy secret không cần đưa vào GitHub Secrets.

# ── 2. ASP.NET Core 8 runtime ───────────────────────────────────────────────
# Chỉ cần runtime, không cần SDK: publish đã build sẵn trên GitHub runner.
# EF migration chạy bằng "migration bundle" (self-contained) nên cũng không cần SDK.
log "2/7 Cài ASP.NET Core ${DOTNET_CHANNEL} runtime"
if ! command -v dotnet >/dev/null 2>&1; then
  # Ubuntu 22.04 có dotnet trong feed chính thức của Microsoft.
  curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb \
    -o /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  rm -f /tmp/packages-microsoft-prod.deb
  apt-get update -qq
fi
apt-get install -y -qq "aspnetcore-runtime-${DOTNET_CHANNEL}"
dotnet --list-runtimes | grep -E "AspNetCore.App ${DOTNET_CHANNEL}" || {
  echo "Không thấy ASP.NET Core ${DOTNET_CHANNEL} runtime sau khi cài." >&2
  exit 1
}

# ── 3. Service account ──────────────────────────────────────────────────────
log "3/7 Tạo user chạy app (${APP_USER})"
if ! id -u "$APP_USER" >/dev/null 2>&1; then
  # --system: không login được, không có shell -> giảm bề mặt tấn công.
  useradd --system --create-home --home-dir "/home/${APP_USER}" \
          --shell /usr/sbin/nologin "$APP_USER"
fi

# ── 4. Thư mục deploy ───────────────────────────────────────────────────────
log "4/7 Dựng cây thư mục ${DEPLOY_ROOT}"
mkdir -p "${DEPLOY_ROOT}/releases"
mkdir -p "${DEPLOY_ROOT}/shared/uploads"
mkdir -p "${DEPLOY_ROOT}/shared/logs"

# appsettings.Production.json: KHÔNG commit vào git, chỉ tồn tại ở đây.
# Tạo file rỗng làm chỗ giữ nếu chưa có; điền giá trị thật sau khi provision.
if [[ ! -f "${DEPLOY_ROOT}/shared/appsettings.Production.json" ]]; then
  cat > "${DEPLOY_ROOT}/shared/appsettings.Production.json" <<'JSON'
{
  "ConnectionStrings": {
    "Default": "REPLACE_ME"
  }
}
JSON
  chmod 600 "${DEPLOY_ROOT}/shared/appsettings.Production.json"
  echo "  -> Đã tạo appsettings.Production.json rỗng, PHẢI điền connection string thật."
fi

chown -R "${APP_USER}:${APP_USER}" "$DEPLOY_ROOT"

# ── 5. systemd unit ─────────────────────────────────────────────────────────
log "5/7 Cài systemd service ${APP_NAME}.service"
cat > "/etc/systemd/system/${APP_NAME}.service" <<UNIT
[Unit]
Description=NewsCMS.Web (ASP.NET Core)
After=network.target

[Service]
# current là symlink; systemd resolve lúc start nên deploy chỉ cần đổi symlink + restart.
WorkingDirectory=${DEPLOY_ROOT}/current
ExecStart=/usr/bin/dotnet ${DEPLOY_ROOT}/current/NewsCMS.Web.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=${APP_NAME}
User=${APP_USER}
Group=${APP_USER}

Environment=DOTNET_NOLOGO=true
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=ASPNETCORE_ENVIRONMENT=Production
# Chỉ bind loopback: nginx là thứ duy nhất tiếp Internet.
Environment=ASPNETCORE_URLS=http://127.0.0.1:${APP_PORT}

# Hardening cơ bản.
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=${DEPLOY_ROOT}

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable "${APP_NAME}.service" >/dev/null

# Chưa start: chưa có release nào trong current. Deploy đầu tiên sẽ start.

# ── 6. nginx reverse proxy ──────────────────────────────────────────────────
log "6/7 Cấu hình nginx reverse proxy"
cat > "/etc/nginx/sites-available/${APP_NAME}" <<'NGINX'
server {
    listen 80;
    listen [::]:80;
    server_name _;

    # Upload media: NewsCMS cho upload video nên nới giới hạn body.
    client_max_body_size 512M;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;

        # WebSocket / SignalR.
        proxy_set_header   Upgrade           $http_upgrade;
        proxy_set_header   Connection        keep-alive;

        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;

        # Upload file lớn / transcode chậm.
        proxy_read_timeout    300s;
        proxy_send_timeout    300s;
        proxy_request_buffering off;
    }
}
NGINX

ln -sfn "/etc/nginx/sites-available/${APP_NAME}" "/etc/nginx/sites-enabled/${APP_NAME}"
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl reload nginx

# ── 7. Firewall ─────────────────────────────────────────────────────────────
log "7/7 Mở firewall (SSH + HTTP/HTTPS)"
ufw allow OpenSSH   >/dev/null
ufw allow 80/tcp    >/dev/null
ufw allow 443/tcp   >/dev/null
# Cổng 5000 KHÔNG mở ra ngoài: app chỉ nghe loopback.
ufw --force enable  >/dev/null
ufw status verbose | head -20

cat <<DONE

════════════════════════════════════════════════════════════════
 Provision xong.
════════════════════════════════════════════════════════════════
 Còn phải làm bằng tay:

 1. Điền connection string thật vào:
      ${DEPLOY_ROOT}/shared/appsettings.Production.json

 2. Database: app dùng SQL Server. Connection string hiện tại trong
    repo là "Server=." (instance local trên máy Windows) -> KHÔNG
    dùng được ở đây. Chọn một trong:
      a) Cài mssql-server ngay trên VPS này (cần >= 2GB RAM)
      b) Trỏ sang SQL Server đang chạy ở máy khác (mở firewall)
      c) Azure SQL / managed

 3. TLS: hiện chỉ có HTTP. Sau khi trỏ domain về IP này:
      apt-get install -y certbot python3-certbot-nginx
      certbot --nginx -d <domain>

 4. ĐỔI MẬT KHẨU admin của CMS. README ghi mặc định là
    admin / Admin@123 - trên VPS công khai thì phải đổi ngay.

 5. Cân nhắc tắt đăng nhập root bằng password sau khi SSH key đã
    hoạt động:  PermitRootLogin prohibit-password  trong
    /etc/ssh/sshd_config  rồi  systemctl reload ssh
DONE
