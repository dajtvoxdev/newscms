#!/usr/bin/env bash
#
# Thêm một host vào bảng SiteDomains để app resolve được domain đó.
#
# App resolve site theo Host header (SiteResolver -> SiteDomains.Host). Host chưa
# có mapping thì trả 404 "No site mapped for host". SiteHostNormalizer chỉ
# lowercase + bỏ scheme/path/port, KHÔNG bỏ "www." -> muốn www chạy thì phải có
# row riêng, hoặc để nginx redirect www -> apex (cách repo này đang dùng).
#
# Idempotent: host đã tồn tại thì chỉ báo và (nếu cần) sửa SiteId/IsPrimary.
#
# Dùng:
#   ./add-site-domain.sh <host> <site-name-hoặc-slug> [--primary]
# Ví dụ:
#   ./add-site-domain.sh chudukafe.vn chu-kafe --primary
set -euo pipefail

HOST_ARG="${1:-}"
SITE_ARG="${2:-}"
MAKE_PRIMARY=0
[ "${3:-}" = "--primary" ] && MAKE_PRIMARY=1

if [ -z "$HOST_ARG" ] || [ -z "$SITE_ARG" ]; then
  echo "Dùng: $0 <host> <site-name-hoặc-slug> [--primary]" >&2
  exit 2
fi

SA_PW_FILE=/root/.newscms-mssql-sa
[ -f "$SA_PW_FILE" ] || { echo "Thiếu $SA_PW_FILE" >&2; exit 1; }
SA_PW=$(cat "$SA_PW_FILE")
SQLCMD=$(command -v sqlcmd || echo /opt/mssql-tools18/bin/sqlcmd)

# Normalize giống SiteHostNormalizer: lowercase, bỏ scheme/path/port.
HOST=$(printf '%s' "$HOST_ARG" | tr '[:upper:]' '[:lower:]' | sed -e 's|^[a-z]*://||' -e 's|/.*$||' -e 's|:.*$||')

sql() { "$SQLCMD" -S 127.0.0.1 -U sa -P "$SA_PW" -C -d NewsCMS -h -1 -W -b -Q "$1"; }

# N'...' để so khớp đúng nvarchar có dấu; escape ' bằng ''.
esc() { printf '%s' "$1" | sed "s/'/''/g"; }
HOST_SQL=$(esc "$HOST")
SITE_SQL=$(esc "$SITE_ARG")

SITE_ID=$(sql "SET NOCOUNT ON; SELECT CAST(Id AS char(36)) FROM Sites WHERE Slug = N'$SITE_SQL' OR Name = N'$SITE_SQL';" | tr -d '\r' | grep -Ei '^[0-9a-f-]{36}$' | head -1 || true)
if [ -z "$SITE_ID" ]; then
  echo "Không tìm thấy site theo slug/name '$SITE_ARG'. Danh sách hiện có:" >&2
  sql "SET NOCOUNT ON; SELECT Slug + '  |  ' + Name FROM Sites ORDER BY Slug;" >&2
  exit 1
fi
echo "Site  : $SITE_ARG -> $SITE_ID"
echo "Host  : $HOST"

sql "
SET NOCOUNT ON;
DECLARE @host nvarchar(255) = N'$HOST_SQL';
DECLARE @site uniqueidentifier = '$SITE_ID';
DECLARE @primary bit = $MAKE_PRIMARY;

IF EXISTS (SELECT 1 FROM SiteDomains WHERE Host = @host)
BEGIN
    -- Host đã có: cảnh báo nếu đang trỏ site khác, không tự chuyển.
    DECLARE @cur uniqueidentifier = (SELECT SiteId FROM SiteDomains WHERE Host = @host);
    IF @cur <> @site
        RAISERROR('Host da ton tai nhung tro site khac (%s). Xoa row cu truoc neu muon doi.', 16, 1, @host);
    ELSE
        PRINT 'Host da ton tai, dung site -> khong insert.';
END
ELSE
BEGIN
    INSERT INTO SiteDomains (Id, SiteId, Host, IsPrimary, CreatedAt)
    VALUES (NEWID(), @site, @host, 0, SYSUTCDATETIME());
    PRINT 'Da insert host.';
END

IF @primary = 1
BEGIN
    -- Chỉ một domain/site được IsPrimary: nó quyết định canonical, og:url, sitemap
    -- (SiteUrlResolver dùng Sites.PrimaryDomain trước, rồi mới IsPrimary).
    UPDATE SiteDomains SET IsPrimary = 0, UpdatedAt = SYSUTCDATETIME()
     WHERE SiteId = @site AND Host <> @host AND IsPrimary = 1;
    UPDATE SiteDomains SET IsPrimary = 1, UpdatedAt = SYSUTCDATETIME()
     WHERE SiteId = @site AND Host = @host AND IsPrimary = 0;
    PRINT 'Da set IsPrimary cho host nay.';
END
"

echo "=== SiteDomains của site này sau khi chạy ==="
sql "SET NOCOUNT ON; SELECT Host + '  IsPrimary=' + CAST(CAST(IsPrimary AS int) AS varchar(1)) FROM SiteDomains WHERE SiteId = '$SITE_ID' ORDER BY IsPrimary DESC, Host;"

# SiteResolver không cache kết quả null nên host mới có hiệu lực ngay, không cần
# restart app. Restart chỉ cần khi đổi mapping của host đã từng resolve thành công.
echo "Xong."
