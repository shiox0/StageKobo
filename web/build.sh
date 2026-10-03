#!/bin/bash
# すてーじ工房：src/ のファイルをつなげて stage-kobo.html（1 ファイル版）を作る
#   bash web/build.sh
set -e
cd "$(dirname "$0")"
cat src/00_head.html src/10_core.js src/20_materials.js src/25_surfaces.js src/30_stage.js src/40_venue.js src/50_assets.js \
    src/60_lights.js src/65_vj.js src/70_cams.js src/80_ui.js src/85_vjui.js src/90_main.js src/99_tail.html > stage-kobo.html
# 文法のチェック（Node があれば）
if command -v node >/dev/null 2>&1; then
  tmp="$(mktemp -d)"
  sed -n '/<script type="module">/,/<\/script>/p' stage-kobo.html | sed '1d;$d' > "$tmp/check.mjs"
  node --check "$tmp/check.mjs" && echo "文法 OK"
  rm -rf "$tmp"
fi
echo "作りました: web/stage-kobo.html（$(wc -c < stage-kobo.html) バイト）"
