#!/usr/bin/env bash
#
# In ra "<số file>|<tổng byte>" của một thư mục trên VPS, để phía Windows đối
# chiếu với thư mục nguồn. Thư mục không tồn tại -> "0|0".
#
set -euo pipefail
DIR="${1:?thiếu đường dẫn thư mục}"
if [ ! -d "$DIR" ]; then
    echo "0|0"
    exit 0
fi
# -printf '%s' nhanh hơn nhiều so với gọi stat cho từng file.
find "$DIR" -type f -printf '%s\n' 2>/dev/null \
  | awk '{n++; s+=$1} END {printf "%d|%d\n", n+0, s+0}'
