#!/usr/bin/env python3
"""すてーじ工房：Release 用の情報を出す（GitHub Actions の「Release を作る」から使う）

  python3 tools/release_info.py version       → インポーターのバージョン（Unity の README の 1 行目「インポーター v0.9.0」）
  python3 tools/release_info.py web           → ブラウザ版のバージョン（web/stage-kobo.html の APP_VERSION）
  python3 tools/release_info.py notes 0.9.0   → Release の説明（CHANGELOG.md の「## v0.9.0」の段＋入れ方）
"""
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
UNITY_README = os.path.join(ROOT, 'unity', 'Assets', '和室', 'Tools', 'StageKobo', 'README.md')
WEB_HTML = os.path.join(ROOT, 'web', 'stage-kobo.html')
CHANGELOG = os.path.join(ROOT, 'CHANGELOG.md')


def read(p):
    with open(p, encoding='utf-8-sig') as f:
        return f.read()


def importer_version():
    first = read(UNITY_README).splitlines()[0]
    m = re.search(r'v(\d+\.\d+\.\d+)', first)
    if not m:
        sys.exit('Unity の README の 1 行目にバージョン（v0.0.0）がありません: ' + first)
    return m.group(1)


def web_version():
    m = re.search(r"APP_VERSION\s*=\s*'([^']+)'", read(WEB_HTML))
    return m.group(1) if m else '?'


def notes(ver):
    text = read(CHANGELOG)
    # 「## v0.9.0（…）」から次の「## 」まで
    m = re.search(r'^## v' + re.escape(ver) + r'\b.*?$(.*?)(?=^## |\Z)', text, re.S | re.M)
    body = m.group(1).strip() if m else '変更点は CHANGELOG.md を見てください。'
    if not m:
        print('注意: CHANGELOG.md に「## v%s」の段がありません' % ver, file=sys.stderr)
    return (body + '\n\n'
            '---\n\n'
            '### 入れ方\n\n'
            '1. 下の **StageKobo_Importer_v%s.unitypackage** をダウンロードして、Unity のプロジェクトにドラッグ（または Assets → Import Package → Custom Package）→「Import」\n'
            '   - 前の版が入っていても、そのまま上から入れて大丈夫です（設定やシーンはそのまま）\n'
            '2. メニュー「和室 → すてーじ工房 → インポーター」\n\n'
            'ブラウザ版（ステージを作る Web ページ v%s）は https://shiox0.github.io/StageKobo/ か、下の **stage-kobo.html** をダウンロードしてブラウザで開いても使えます。\n'
            % (ver, web_version()))


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    cmd = sys.argv[1]
    if cmd == 'version':
        print(importer_version())
    elif cmd == 'web':
        print(web_version())
    elif cmd == 'notes':
        print(notes(sys.argv[2] if len(sys.argv) > 2 else importer_version()))
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
