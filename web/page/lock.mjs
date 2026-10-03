// すてーじ工房：Webページにパスワードの鍵をかける（GitHub Pages。Secrets に PAGE_PASSWORD があるときだけ使う）
//
// 使い方:  PAGE_PASSWORD=パスワード node web/page/lock.mjs web/stage-kobo.html _site/index.html
//   ・中身（stage-kobo.html）を AES-GCM で暗号化して、パスワード入力のページ（web/page/lock.html）に入れる
//   ・鍵はパスワードから PBKDF2（SHA-256・60万回）で作る。ブラウザの Web Crypto で開く（外のライブラリは使わない）
//   ・塩（salt）は PAGE_SALT（ふつうはリポジトリ名）から決まった値にする
//     → パスワードを変えない限り、ページを更新しても「この端末で覚えておく」がそのまま使える
//   ・同じ場所に robots.txt（検索エンジンに載せない）も作る
// Node 18 以上（GitHub Actions は 20 / 24）。外のパッケージは要らない
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { webcrypto as crypto } from 'node:crypto';

const [, , inPath, outPath] = process.argv;
if (!inPath || !outPath) {
  console.error('使い方: PAGE_PASSWORD=… node web/page/lock.mjs <中身.html> <出力.html>');
  process.exit(2);
}
const password = process.env.PAGE_PASSWORD || '';
if (!password) {
  console.error('PAGE_PASSWORD（Webページのパスワード）がありません。GitHub の Settings → Secrets and variables → Actions に入れてください');
  process.exit(1);
}
if (password.length < 8) console.warn('注意: パスワードが短いです（8文字以上がおすすめ）');

const ITER = 600000;
const enc = new TextEncoder();
const b64 = (buf) => Buffer.from(buf).toString('base64');

const saltText = 'stagekobo-page:' + (process.env.PAGE_SALT || 'StageKobo');
const salt = new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(saltText)));
const base = await crypto.subtle.importKey('raw', enc.encode(password.normalize('NFC')), 'PBKDF2', false, ['deriveBits']);
const raw = await crypto.subtle.deriveBits({ name: 'PBKDF2', salt, iterations: ITER, hash: 'SHA-256' }, base, 256);
const key = await crypto.subtle.importKey('raw', raw, 'AES-GCM', false, ['encrypt']);
const iv = crypto.getRandomValues(new Uint8Array(12));
const html = readFileSync(inPath, 'utf8');
const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, enc.encode(html));

const here = dirname(fileURLToPath(import.meta.url));
const tpl = readFileSync(join(here, 'lock.html'), 'utf8');
const fill = { __ITER__: String(ITER), __SALT__: b64(salt), __IV__: b64(iv), __DATA__: b64(data) };
let out = tpl;
for (const [k, v] of Object.entries(fill)) {
  if (!out.includes(k)) { console.error('lock.html に ' + k + ' がありません'); process.exit(1); }
  out = out.split(k).join(v);   // replace は $ を特別扱いするので split / join
}
if (out.includes(html.slice(0, 200))) { console.error('中身がそのまま入っています（暗号化に失敗）'); process.exit(1); }

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, out);
writeFileSync(join(dirname(outPath), 'robots.txt'), 'User-agent: *\nDisallow: /\n');
console.log('鍵をかけました: ' + outPath + '（' + Math.round(out.length / 1024) + ' KB）');
