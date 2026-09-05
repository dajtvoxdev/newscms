#!/usr/bin/env bash
#
# Chạy query trên SQL Server của VPS bằng credential sa lưu ở /root.
# Tách ra file riêng để phía Windows không phải nhét mật khẩu + SQL qua 3 lớp
# quoting (PowerShell -> ssh -> bash), vốn là chỗ rất dễ vỡ.
#
#   bash vps-sql.sh -q "SELECT 1"
#   bash vps-sql.sh -f /tmp/inventory.sql
#
set -euo pipefail

SQLCMD="$(command -v sqlcmd || echo /opt/mssql-tools18/bin/sqlcmd)"
[ -x "$SQLCMD" ] || { echo "Không tìm thấy sqlcmd" >&2; exit 1; }

SA_PW_FILE=/root/.newscms-mssql-sa
[ -f "$SA_PW_FILE" ] || { echo "Thiếu $SA_PW_FILE" >&2; exit 1; }

SEP="${SEP:-|}"

# -C: cert của SQL Server là self-signed, sqlcmd 18 mặc định Encrypt=Mandatory.
# -b: exit code khác 0 khi SQL lỗi.
set -- "${1:?thiếu -q hoặc -f}" "${2:?thiếu query hoặc file}"
case "$1" in
    -q) exec "$SQLCMD" -S 127.0.0.1 -U sa -P "$(cat "$SA_PW_FILE")" -C -b -h -1 -W -s "$SEP" -Q "$2" ;;
    -f) exec "$SQLCMD" -S 127.0.0.1 -U sa -P "$(cat "$SA_PW_FILE")" -C -b -h -1 -W -s "$SEP" -i "$2" ;;
    *)  echo "Tham số phải là -q hoặc -f" >&2; exit 2 ;;
esac
