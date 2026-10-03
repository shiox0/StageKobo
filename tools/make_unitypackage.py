#!/usr/bin/env python3
"""すてーじ工房：Unity パッケージ（.unitypackage）を作る

使い方（リポジトリのいちばん上で）:
    python3 tools/make_unitypackage.py dist/StageKobo_Importer_v0.9.0.unitypackage

・unity/Assets/和室/Tools/StageKobo/ の中身を、.meta に書いてある GUID のまま詰める
・.meta が無いファイルがあったら止める（GUID が毎回変わると、使う人が上書き更新したときに
  別のファイル扱いになってスクリプトが二重になったり、参照が外れたりするため）
・名前が「~」で終わるフォルダ（Tools~ など）は Unity が読まないので入れない
・Python の標準ライブラリだけで動く（GitHub Actions でも手元でも同じ）
"""
import gzip
import io
import os
import sys
import tarfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', 'unity'))      # この下が Unity プロジェクトの形（Assets/…）
PKG = 'Assets/和室/Tools/StageKobo'


def guid_of(meta):
    with open(meta, encoding='utf-8-sig') as f:
        for line in f:
            if line.startswith('guid:'):
                return line.split()[1].strip()
    return None


def collect():
    base = os.path.join(ROOT, PKG)
    if not os.path.isdir(base):
        sys.exit('見つかりません: ' + base)
    paths = [base]
    for d, dirs, files in os.walk(base):
        dirs[:] = sorted(x for x in dirs if not x.endswith('~') and not x.startswith('.'))
        for x in dirs:
            paths.append(os.path.join(d, x))
        for f in sorted(files):
            if f.endswith('.meta') or f.startswith('.'):
                continue
            paths.append(os.path.join(d, f))
    entries, missing, seen = [], [], {}
    for p in paths:
        m = p + '.meta'
        rel = os.path.relpath(p, ROOT).replace('\\', '/')
        if not os.path.exists(m):
            missing.append(rel)
            continue
        g = guid_of(m)
        if not g or len(g) != 32:
            missing.append(rel + '（.meta に guid がありません）')
            continue
        if g in seen:
            sys.exit('GUID が重なっています: ' + rel + ' と ' + seen[g])
        seen[g] = rel
        entries.append((p, m, rel, g))
    if missing:
        print('次のファイルに .meta がありません。Unity で一度開いて .meta を作ってから入れてください:', file=sys.stderr)
        for r in missing:
            print('  ' + r, file=sys.stderr)
        sys.exit(1)
    # .meta だけ残っていて中身が無いもの（消したファイルの .meta）も知らせる
    for d, dirs, files in os.walk(base):
        dirs[:] = [x for x in dirs if not x.endswith('~')]
        for f in files:
            if f.endswith('.meta') and not os.path.exists(os.path.join(d, f[:-5])):
                print('注意: 中身の無い .meta があります（消し忘れ？）: ' + os.path.relpath(os.path.join(d, f), ROOT), file=sys.stderr)
    return entries


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    out = sys.argv[1]
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    entries = collect()
    mtime = int(os.environ.get('SOURCE_DATE_EPOCH', time.time()))
    with open(out, 'wb') as raw:
        with gzip.GzipFile(filename='archtemp.tar', mode='wb', fileobj=raw, mtime=mtime) as gz:
            with tarfile.open(fileobj=gz, mode='w') as tar:
                def add(name, data):
                    ti = tarfile.TarInfo(name)
                    ti.size = len(data)
                    ti.mode = 0o644
                    ti.mtime = mtime
                    tar.addfile(ti, io.BytesIO(data))
                for p, m, rel, g in entries:
                    if os.path.isfile(p):
                        with open(p, 'rb') as f:
                            add(g + '/asset', f.read())
                    with open(m, 'rb') as f:
                        add(g + '/asset.meta', f.read())
                    add(g + '/pathname', rel.encode('utf-8'))
    print('作りました: %s（%d 個・%d KB）' % (out, len(entries), os.path.getsize(out) // 1024))


if __name__ == '__main__':
    main()
