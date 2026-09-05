#!/usr/bin/env bash
#
# Cấp và cài chứng chỉ Let's Encrypt cho một domain của NewsCMS trên VPS Ubuntu.
#
# Dùng `certbot certonly --webroot` thay vì `certbot --nginx`: plugin nginx tự
# viết lại file config, khó đọc lại và dễ đụng vào server block catch-all đang
# phục vụ các domain khác. Ở đây nginx config do script này viết hẳn ra.
#
# Kiến trúc nginx sau khi chạy:
#   :80  <domain>, www.<domain>  -> ACME challenge + 301 sang https://<domain>
#   :443 www.<domain>            -> 301 sang https://<domain>  (canonical apex)
#   :443 <domain>                -> proxy sang Kestrel 127.0.0.1:5000
#   :80  server_name _           -> giữ nguyên, các domain khác vẫn chạy HTTP
#
# App không dùng UseHttpsRedirection (sẽ loop sau proxy) — redirect làm ở nginx.
# App đọc X-Forwarded-Proto qua UseForwardedHeaders nên link sinh ra sẽ là https.
#
# LƯU Ý www: SiteHostNormalizer KHÔNG bỏ tiền tố "www." nên app coi
# www.<domain> là host khác. Vì vậy www được redirect ở nginx thay vì thêm row
# SiteDomains — vừa đúng canonical SEO, vừa không phải nhân đôi mapping.
#
# Idempotent: chạy lại chỉ ghi lại config + reload, không xin cert mới nếu cert
# hiện tại còn hạn (certbot tự bỏ qua).
#
# Dùng:
#   ./setup-ssl.sh <domain> [email]
# Ví dụ:
#   ./setup-ssl.sh chudukafe.vn admin@chudukafe.vn
set -euo pipefail

DOMAIN="${1:-}"
EMAIL="${2:-}"
if [ -z "$DOMAIN" ]; then
  echo "Dùng: $0 <domain> [email]" >&2
  exit 2
fi
WWW="www.$DOMAIN"
WEBROOT=/var/www/certbot
CONF="/etc/nginx/sites-available/newscms-$DOMAIN"
SNIPPET=/etc/nginx/snippets/newscms-proxy.conf
LIVE="/etc/letsencrypt/live/$DOMAIN"

echo "==> [1/6] Cài certbot"
if ! command -v certbot >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq certbot
else
  echo "    certbot đã có: $(certbot --version 2>&1)"
fi

echo "==> [2/6] Snippet proxy dùng chung"
mkdir -p /etc/nginx/snippets "$WEBROOT"
cat > "$SNIPPET" <<'SNIP'
# Proxy sang Kestrel. Dùng chung cho mọi server block của NewsCMS.
proxy_pass         http://127.0.0.1:5000;
proxy_http_version 1.1;

# WebSocket / SignalR.
proxy_set_header   Upgrade           $http_upgrade;
proxy_set_header   Connection        keep-alive;

# $host giữ nguyên host client gửi -> SiteResolver map đúng site.
proxy_set_header   Host              $host;
proxy_set_header   X-Real-IP         $remote_addr;
proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
proxy_set_header   X-Forwarded-Proto $scheme;
proxy_cache_bypass $http_upgrade;

# Upload file lớn / transcode chậm.
proxy_read_timeout    300s;
proxy_send_timeout    300s;
proxy_request_buffering off;
SNIP

echo "==> [3/6] Server block :80 tạm (ACME + proxy) để xin cert"
# Chỉ làm khi chưa có cert. Chạy lại script mà vẫn ghi config không-TLS này thì
# HTTPS sẽ chết trong khoảng giữa reload ở bước 3 và reload ở bước 5.
if [ -f "$LIVE/fullchain.pem" ]; then
  echo "    Cert đã có -> giữ nguyên config hiện tại, không hạ TLS."
else
  # Chưa redirect sang https ở bước này: cert chưa có, redirect sẽ làm site chết.
  cat > "$CONF" <<CONF80
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN $WWW;

    client_max_body_size 512M;

    location /.well-known/acme-challenge/ {
        root $WEBROOT;
    }

    location / {
        include $SNIPPET;
    }
}
CONF80
  ln -sfn "$CONF" "/etc/nginx/sites-enabled/newscms-$DOMAIN"
  nginx -t
  systemctl reload nginx
fi
ln -sfn "$CONF" "/etc/nginx/sites-enabled/newscms-$DOMAIN"

echo "==> [4/6] Xin certificate"
if [ -f "$LIVE/fullchain.pem" ]; then
  echo "    Cert đã có, bỏ qua bước xin mới:"
  certbot certificates --cert-name "$DOMAIN" 2>/dev/null | sed -n '1,12p'
else
  EMAIL_ARGS=(--register-unsafely-without-email)
  if [ -n "$EMAIL" ]; then
    EMAIL_ARGS=(--email "$EMAIL" --no-eff-email)
  fi

  # Dry-run trước: Let's Encrypt giới hạn 5 lần fail/host/giờ, thử thật mà sai
  # DNS/firewall là bị khoá cả tiếng.
  echo "    -- dry run --"
  certbot certonly --webroot -w "$WEBROOT" \
    -d "$DOMAIN" -d "$WWW" \
    --agree-tos "${EMAIL_ARGS[@]}" --non-interactive --dry-run

  echo "    -- thật --"
  certbot certonly --webroot -w "$WEBROOT" \
    -d "$DOMAIN" -d "$WWW" \
    --agree-tos "${EMAIL_ARGS[@]}" --non-interactive
fi

echo "==> [5/6] Server block cuối (443 + redirect)"
# nginx 1.18 (Ubuntu 22.04) chưa có directive `http2 on;` -> dùng `listen ... http2`.
cat > "$CONF" <<CONFSSL
# ---- HTTP: chỉ để ACME renew + đẩy sang HTTPS ----
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN $WWW;

    location /.well-known/acme-challenge/ {
        root $WEBROOT;
    }

    location / {
        return 301 https://$DOMAIN\$request_uri;
    }
}

# ---- HTTPS www -> apex (canonical) ----
# App coi www.<domain> là host khác (SiteHostNormalizer không bỏ "www."), nên
# phải redirect ở đây, không thì www trả 404 "No site mapped for host".
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $WWW;

    ssl_certificate     $LIVE/fullchain.pem;
    ssl_certificate_key $LIVE/privkey.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers off;
    ssl_session_cache   shared:SSL:10m;
    ssl_session_timeout 1d;
    ssl_session_tickets off;

    return 301 https://$DOMAIN\$request_uri;
}

# ---- HTTPS apex: site thật ----
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name $DOMAIN;

    ssl_certificate     $LIVE/fullchain.pem;
    ssl_certificate_key $LIVE/privkey.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers off;
    ssl_session_cache   shared:SSL:10m;
    ssl_session_timeout 1d;
    ssl_session_tickets off;

    # KHÔNG add_header Strict-Transport-Security ở đây: app đã tự gửi HSTS
    # (max-age=2592000) khi thấy X-Forwarded-Proto=https. Thêm nữa là response
    # có 2 header HSTS, và theo RFC 6797 §8.1 browser chỉ dùng cái ĐẦU (của app)
    # rồi bỏ cái sau -> chỉ gây nhiễu, không nâng được max-age.
    # Muốn đổi max-age thì sửa UseHsts trong Program.cs.
    #
    # KHÔNG ssl_stapling: cert Let's Encrypt không còn OCSP responder URL nên
    # nginx chỉ log "ssl_stapling ignored".

    # Upload media: NewsCMS cho upload video nên nới giới hạn body.
    client_max_body_size 512M;

    location / {
        include $SNIPPET;
    }
}
CONFSSL
nginx -t
systemctl reload nginx

echo "==> [6/6] Kiểm tra auto-renew"
systemctl is-enabled certbot.timer 2>/dev/null || systemctl enable --now certbot.timer
systemctl is-active certbot.timer  2>/dev/null || systemctl start certbot.timer
# Renew chạy được nhưng nginx không reload thì client vẫn thấy cert cũ tới lần
# reload sau -> gắn deploy hook.
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh <<'HOOK'
#!/usr/bin/env bash
systemctl reload nginx
HOOK
chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh

certbot renew --dry-run 2>&1 | tail -5
echo
echo "Xong. Cert cho: $DOMAIN, $WWW"
