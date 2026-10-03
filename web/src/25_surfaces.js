/* =====================================================================
   質感（PBR 生成テクスチャ）：色ムラ・ノーマル・ラフネス/AO をコードで作る
   - 高さマップ（継ぎ目なく繰り返す）を作り、そこから
       map（色ムラ。マテリアルの色に掛け算）／normalMap／ORM（R=AO・G=ラフネス・B=メタル）
     の3枚を作る。ORM は glTF の occlusion と metallicRoughness にそのまま書き出せる並び
   - すべての質感は「テクスチャ1枚 = tile メートル」。UV は fitSurfaceUVs で「メートル ÷ tile」に合わせる
     （テクスチャの repeat は 1 のまま → GLB でも Unity でも同じ見え方。Unity の Standard はマップごとの repeat を持てない）
   - ノーマルマップは glTF の決まり（緑 = 画像の上向き）で描く。three では flipY=false ＋ normalScale.y を反転して使う
   ===================================================================== */
const _ss = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };

/* 継ぎ目なく繰り返すバリューノイズ（横 cx × 縦 cy の格子、oct オクターブ） */
function tnoise(N, cx, cy, seed, oct = 1, pers = 0.5) {
  const out = new Float32Array(N * N); let amp = 1, tot = 0;
  for (let o = 0; o < oct; o++) {
    const gx = cx << o, gy = cy << o; const R = mulberry32(seed * 977 + o * 131);
    const grid = new Float32Array(gx * gy); for (let i = 0; i < grid.length; i++) grid[i] = R();
    for (let y = 0; y < N; y++) {
      const fy = y / N * gy, iy = Math.floor(fy); let ty = fy - iy; ty = ty * ty * (3 - 2 * ty);
      const r0 = (iy % gy) * gx, r1 = ((iy + 1) % gy) * gx;
      for (let x = 0; x < N; x++) {
        const fx = x / N * gx, ix = Math.floor(fx); let tx = fx - ix; tx = tx * tx * (3 - 2 * tx);
        const x0 = ix % gx, x1 = (ix + 1) % gx;
        const a = grid[r0 + x0], b = grid[r0 + x1], c = grid[r1 + x0], d = grid[r1 + x1];
        out[y * N + x] += amp * (a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty);
      }
    }
    tot += amp; amp *= pers;
  }
  for (let i = 0; i < out.length; i++) out[i] /= tot;
  return out;
}
/* 継ぎ目なく繰り返すセル（ボロノイ）：d = いちばん近い点までの距離（セル幅 = 1）、id = セルごとの乱数 */
function tworley(N, cells, seed) {
  const R = mulberry32(seed * 733 + 5); const P = new Float32Array(cells * cells * 3);
  for (let i = 0; i < cells * cells; i++) { P[i * 3] = R(); P[i * 3 + 1] = R(); P[i * 3 + 2] = R(); }
  const d = new Float32Array(N * N), id = new Float32Array(N * N);
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
    const fx = x / N * cells, fy = y / N * cells, ix = Math.floor(fx), iy = Math.floor(fy);
    let best = 9, bid = 0;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const gx = ((ix + dx) % cells + cells) % cells, gy = ((iy + dy) % cells + cells) % cells, k = (gy * cells + gx) * 3;
      const px = ix + dx + P[k], py = iy + dy + P[k + 1];
      const dd = Math.hypot(px - fx, py - fy);
      if (dd < best) { best = dd; bid = P[k + 2]; }
    }
    d[y * N + x] = best; id[y * N + x] = bid;
  }
  return { d, id };
}
/* ずらして並べた格子（六角配置）の、いちばん近い点までの距離（横の間隔 = 1）。rows は偶数にする */
function hexDist(N, cols, rows, x, y) {
  const px = N / cols, py = N / rows; const r = Math.floor(y / py); let best = 9;
  for (let k = -1; k <= 1; k++) {
    const rr = r + k, off = (((rr % rows) + rows) % 2) ? px / 2 : 0, cy = rr * py + py / 2;
    const c = Math.round((x - off - px / 2) / px);
    for (let j = -1; j <= 1; j++) {
      let dx = x - (off + (c + j) * px + px / 2); dx -= Math.round(dx / N) * N;
      let dy = y - cy; dy -= Math.round(dy / N) * N;
      best = Math.min(best, Math.hypot(dx, dy) / px);
    }
  }
  return best;
}
/* ぼかし（ラップあり・横→縦の箱型） */
function tblur(src, N, r) {
  const tmp = new Float32Array(N * N), out = new Float32Array(N * N), w = 2 * r + 1;
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) { let s = 0; for (let k = -r; k <= r; k++) s += src[y * N + ((x + k + N) % N)]; tmp[y * N + x] = s / w; }
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) { let s = 0; for (let k = -r; k <= r; k++) s += tmp[((y + k + N) % N) * N + x]; out[y * N + x] = s / w; }
  return out;
}
/* ひっかき傷（高さを少し削る線をランダムに） */
function tscratch(h, N, count, seed, depth, lenMin = 0.05, lenMax = 0.3, angle = null) {
  const R = mulberry32(seed * 389 + 7);
  for (let i = 0; i < count; i++) {
    let x = R() * N, y = R() * N; const a = angle === null ? R() * Math.PI : angle + (R() - 0.5) * 0.25;
    const L = (lenMin + R() * (lenMax - lenMin)) * N, dx = Math.cos(a), dy = Math.sin(a), dep = depth * (0.4 + R() * 0.6);
    for (let s = 0; s < L; s++) { const ix = ((Math.round(x + dx * s) % N) + N) % N, iy = ((Math.round(y + dy * s) % N) + N) % N; h[iy * N + ix] -= dep * Math.sin(Math.PI * s / L); }
  }
}

/* ---------- 質感の定義（N = ピクセル、tile = 1枚のメートル、ns = ノーマルの強さ） ----------
   gen は { h（高さ 0-1）, alb（明るさ 0-1）または rgb（色・0-1 の sRGB）, rough（0-1）, nx/ny（ノーマルを直接） } を返す */
const SURFACES = {
  // ブラシ仕上げのアルミ（トラス・柵・脚）：長さ方向（V）の細い筋＋小傷
  alu: { size: 256, tile: 0.5, ns: 1.0, gen(N) {
    const fine = tnoise(N, 128, 1, 1, 2), mid = tnoise(N, 24, 2, 2, 2), blot = tnoise(N, 3, 3, 3, 3);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = fine[i] * 0.55 + mid[i] * 0.45; alb[i] = 0.84 + 0.1 * fine[i] + 0.12 * (blot[i] - 0.5); rough[i] = 0.72 + 0.2 * mid[i] + 0.12 * (blot[i] - 0.5); }
    tscratch(h, N, 18, 4, 0.25, 0.05, 0.3);
    return { h, alb, rough };
  } },
  // 焼付け塗装（灯体・機材の筐体）：細かいゆず肌＋うっすらムラ
  powder: { size: 256, tile: 0.15, ns: 0.3, gen(N) {
    const a = tnoise(N, 64, 64, 11, 2), b = tnoise(N, 16, 16, 12, 2), c = tnoise(N, 3, 3, 13, 3);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = a[i] * 0.75 + b[i] * 0.25; alb[i] = 0.95 + 0.07 * (c[i] - 0.5) + 0.03 * a[i]; rough[i] = 0.86 + 0.1 * b[i] + 0.08 * (c[i] - 0.5); }
    tscratch(h, N, 5, 14, 0.25, 0.03, 0.1);
    return { h, alb, rough };
  } },
  // スピーカーの外装（トーレックス）：細かい粒々
  tolex: { size: 256, tile: 0.1, ns: 2.2, gen(N) {
    const w = tworley(N, 22, 21), f = tnoise(N, 64, 64, 22, 1), c = tnoise(N, 4, 4, 23, 2);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { const b = (1 - _ss(0.08, 0.62, w.d[i])) * (0.75 + 0.25 * w.id[i]); h[i] = b * 0.85 + f[i] * 0.15; alb[i] = 0.82 + 0.18 * b + 0.06 * (c[i] - 0.5); rough[i] = 0.98 - 0.16 * b; }
    return { h, alb, rough };
  } },
  // パンチングメタル（スピーカーのグリル・マイクの頭）
  grille: { size: 256, tile: 0.06, ns: 2.5, gen(N) {
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N); const n = tnoise(N, 16, 16, 31, 2);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, d = hexDist(N, 16, 18, x, y), plate = _ss(0.28, 0.36, d);
      h[i] = plate * 0.9 + n[i] * 0.1; alb[i] = 0.05 + 0.95 * plate * (0.9 + 0.1 * n[i]); rough[i] = 1 - 0.25 * plate;
    }
    return { h, alb, rough, ao: 1.4 };
  } },
  // 舞台の台（ステージデッキ）：すべり止めの六角の粒
  deck: { size: 256, tile: 0.18, ns: 1.6, gen(N) {
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    const n = tnoise(N, 8, 8, 41, 3), f = tnoise(N, 64, 64, 42, 1);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, d = hexDist(N, 14, 16, x, y), b = 1 - _ss(0.14, 0.34, d);
      h[i] = b * 0.75 + f[i] * 0.1 + n[i] * 0.15; alb[i] = 0.9 + 0.06 * b + 0.1 * (n[i] - 0.5); rough[i] = 0.78 + 0.18 * b + 0.06 * f[i];
    }
    return { h, alb, rough };
  } },
  // 舞台幕（プリーツのベロア）：縦ひだ＋毛並み
  velour: { size: 256, tile: 0.6, ns: 3.2, gen(N) {
    const fib = tnoise(N, 160, 3, 51, 2), mot = tnoise(N, 4, 4, 52, 3);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, p = 0.5 - 0.5 * Math.cos(x / N * TAU * 8), s = Math.pow(p, 0.75);
      h[i] = s * 0.85 + fib[i] * 0.08 + mot[i] * 0.07; alb[i] = (0.42 + 0.58 * s) * (0.93 + 0.07 * fib[i]) * (0.94 + 0.12 * (mot[i] - 0.5)); rough[i] = 0.93 + 0.07 * fib[i];
    }
    return { h, alb, rough, ao: 1.2 };
  } },
  // ベルベット（劇場カーテン：ひだは形で作るので、ここは毛並みとムラだけ）
  velvet: { size: 256, tile: 0.8, ns: 1.0, gen(N) {
    const fib = tnoise(N, 192, 4, 61, 2), mot = tnoise(N, 5, 5, 62, 3), str = tnoise(N, 24, 1, 63, 2);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = fib[i] * 0.55 + str[i] * 0.15 + mot[i] * 0.3; alb[i] = 0.88 + 0.07 * fib[i] + 0.1 * (mot[i] - 0.5) + 0.03 * (str[i] - 0.5); rough[i] = 0.92 + 0.08 * fib[i]; }
    return { h, alb, rough };
  } },
  // 布（衣装・座席・旗）：平織り＋ゆるいしわ
  fabric: { size: 256, tile: 0.12, ns: 1.6, gen(N) {
    const T = 40, wr = tnoise(N, 3, 3, 71, 3), sl = tnoise(N, 96, 96, 72, 1);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, u = x / N * T, v = y / N * T, iu = Math.floor(u), iv = Math.floor(v), fu = u - iu, fv = v - iv;
      const warp = (iu + iv) & 1, prof = warp ? Math.sin(Math.PI * fu) * (0.75 + 0.25 * Math.sin(Math.PI * fv)) : Math.sin(Math.PI * fv) * (0.75 + 0.25 * Math.sin(Math.PI * fu));
      h[i] = prof * 0.45 + wr[i] * 0.45 + sl[i] * 0.1; alb[i] = (0.8 + 0.2 * prof) * (0.95 + 0.1 * (wr[i] - 0.5)); rough[i] = 0.9 + 0.1 * (1 - prof);
    }
    return { h, alb, rough };
  } },
  // サテン（アイドル衣装）：つやのある布。細かい織り目は弱く、光沢のムラ
  satin: { size: 256, tile: 0.25, ns: 0.9, gen(N) {
    const f = tnoise(N, 96, 48, 81, 2), w = tnoise(N, 3, 4, 82, 3);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = w[i] * 0.9 + f[i] * 0.1; alb[i] = 0.95 + 0.04 * f[i] + 0.08 * (w[i] - 0.5); rough[i] = 0.75 + 0.25 * f[i] - 0.1 * (w[i] - 0.5); }
    return { h, alb, rough };
  } },
  // プリーツ（アイドル衣装のスカート）：つやのある布にひだ
  pleat: { size: 256, tile: 0.4, ns: 2.0, gen(N) {
    const f = tnoise(N, 96, 24, 85, 2), w = tnoise(N, 3, 2, 86, 3);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, t = (x / N * 8) % 1, p = t < 0.5 ? t * 2 : (1 - t) * 2;   // ジグザグ（折り目がはっきりしたひだ）
      h[i] = p * 0.85 + w[i] * 0.1 + f[i] * 0.05; alb[i] = (0.74 + 0.26 * p) * (0.97 + 0.06 * (w[i] - 0.5)); rough[i] = 0.75 + 0.2 * f[i] + 0.1 * (1 - p);
    }
    return { h, alb, rough };
  } },
  // 髪：毛の流れ（V 方向）＋毛束
  hair: { size: 256, tile: 0.12, ns: 2.4, gen(N) {
    const fine = tnoise(N, 160, 1, 91, 2), cl = tnoise(N, 14, 2, 92, 2);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = fine[i] * 0.5 + cl[i] * 0.5; alb[i] = 0.72 + 0.22 * fine[i] + 0.14 * cl[i]; rough[i] = 0.55 + 0.4 * (1 - fine[i]); }
    return { h, alb, rough };
  } },
  // 化粧板（ステージの側面・壁）：パネルの目地とビス
  panel: { size: 256, tile: 1.2, ns: 2.0, gen(N) {
    const n = tnoise(N, 6, 6, 101, 3), f = tnoise(N, 64, 64, 102, 1);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N); const sc = N * 0.06;
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, ex = Math.min(x, N - 1 - x), ey = Math.min(y, N - 1 - y), seam = _ss(0.6, 2.2, Math.min(ex, ey));
      let screw = 0; for (const sx of [sc, N - sc]) for (const sy of [sc, N - sc]) { const d = Math.hypot(x - sx, y - sy); screw = Math.max(screw, 1 - _ss(1.5, 3.5, d)); }
      h[i] = seam * 0.8 + screw * 0.25 + n[i] * 0.05 + f[i] * 0.03; alb[i] = (0.35 + 0.65 * seam) * (0.93 + 0.12 * (n[i] - 0.5)) * (1 - 0.25 * screw); rough[i] = 0.85 + 0.15 * n[i];
    }
    return { h, alb, rough };
  } },
  // ミラーボールの鏡タイル：1枚ずつ少し違う向き＋目地
  mirrortile: { size: 256, tile: 0.05, ns: 1, absRough: true, gen(N) {
    const C = 8, R = mulberry32(111), tilt = []; for (let i = 0; i < C * C; i++) tilt.push([(R() - 0.5) * 0.35, (R() - 0.5) * 0.35]);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N), nx = new Float32Array(N * N), ny = new Float32Array(N * N);
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, cx = Math.floor(x / N * C), cy = Math.floor(y / N * C), fx = x / N * C - cx, fy = y / N * C - cy;
      const g = _ss(0.03, 0.08, Math.min(fx, 1 - fx, fy, 1 - fy)), t = tilt[cy * C + cx];
      nx[i] = t[0] * g; ny[i] = t[1] * g; h[i] = g; alb[i] = 0.12 + 0.88 * g; rough[i] = 0.06 + 0.8 * (1 - g);
    }
    return { h, alb, rough, nx, ny };
  } },
  // 芝生（野外の地面）
  grass: { size: 256, tile: 1.5, ns: 1.8, gen(N) {
    const h = tnoise(N, 8, 8, 121, 3), alb = new Float32Array(N * N), rough = new Float32Array(N * N); const R = mulberry32(122);
    for (let i = 0; i < N * N; i++) { alb[i] = 0.75 + 0.3 * h[i]; rough[i] = 0.92 + 0.08 * h[i]; h[i] *= 0.3; }
    for (let k = 0; k < 9000; k++) {
      const x0 = R() * N, y0 = R() * N, L = 3 + R() * 7, a = -Math.PI / 2 + (R() - 0.5) * 0.9, b = 0.5 + R() * 0.7;
      for (let s = 0; s < L; s++) { const ix = ((Math.round(x0 + Math.cos(a) * s) % N) + N) % N, iy = ((Math.round(y0 + Math.sin(a) * s) % N) + N) % N, j = iy * N + ix; h[j] = Math.max(h[j], 0.3 + 0.7 * s / L); alb[j] = 0.65 + 0.55 * b * s / L; }
    }
    return { h, alb, rough };
  } },
  // コンクリート（ホールの床）
  concrete: { size: 256, tile: 4, ns: 1.3, gen(N) {
    const a = tnoise(N, 8, 8, 131, 5), st = tnoise(N, 3, 3, 132, 3), w = tworley(N, 40, 133);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { const pit = 1 - _ss(0.0, 0.12, w.d[i]) * (w.id[i] > 0.7 ? 1 : 0); h[i] = a[i] * 0.8 + pit * 0.2; alb[i] = (0.72 + 0.3 * a[i]) * (0.88 + 0.24 * (st[i] - 0.5)) * (0.85 + 0.15 * pit); rough[i] = 0.85 + 0.15 * a[i]; }
    tscratch(h, N, 30, 134, 0.3, 0.05, 0.25);
    return { h, alb, rough };
  } },
  // ダンスフロア（つやのある床）：ほとんど平ら。ラフネスのムラとすり傷で映り込みが自然になる
  vinyl: { size: 256, tile: 2, ns: 0.6, gen(N) {
    const w = tnoise(N, 3, 3, 141, 3), m = tnoise(N, 10, 10, 142, 3), f = tnoise(N, 96, 96, 143, 1);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N);
    for (let i = 0; i < N * N; i++) { h[i] = w[i] * 0.85 + f[i] * 0.15; alb[i] = 0.97 + 0.04 * (m[i] - 0.5); rough[i] = 0.55 + 0.35 * m[i]; }
    const sc = new Float32Array(N * N); tscratch(sc, N, 160, 144, -1, 0.02, 0.12);
    for (let i = 0; i < N * N; i++) { rough[i] = Math.min(1, rough[i] + sc[i] * 0.35); h[i] -= sc[i] * 0.04; }
    return { h, alb, rough };
  } },
  // フローリング（木の床）：板・継ぎ目・木目。1枚 = 床の模様 1周期
  planks: { size: 256, tile: 2, ns: 1.8, gen(N) {
    const rows = 12, R = mulberry32(151), joints = [], tone = [];
    for (let r = 0; r < rows; r++) { const o = R(); joints.push([o, (o + 0.45 + R() * 0.1) % 1]); tone.push([0.82 + R() * 0.3, 0.82 + R() * 0.3, 0.82 + R() * 0.3]); }
    const grain = tnoise(N, 4, 96, 152, 3), fine = tnoise(N, 2, 256, 153, 1), knot = tworley(N, 6, 154);
    const h = new Float32Array(N * N), alb = new Float32Array(N * N), rough = new Float32Array(N * N); const rh = N / rows;
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, r = Math.floor(y / rh), fy = y - r * rh, u = x / N, [j0, j1] = joints[r];
      const seg = (u >= Math.min(j0, j1) && u < Math.max(j0, j1)) ? 1 : (u < Math.min(j0, j1) ? 0 : 2);
      const dj = Math.min(Math.abs(u - j0), Math.abs(u - j1), 1 - Math.abs(u - j0), 1 - Math.abs(u - j1)) * N;
      const groove = _ss(0.4, 1.6, Math.min(fy, rh - 1 - fy)) * _ss(0.4, 1.6, dj);
      const kn = knot.id[i] > 0.82 ? 1 - _ss(0.02, 0.16, knot.d[i]) : 0;
      const gr = grain[i] * 0.7 + fine[i] * 0.3 - kn * 0.4;
      h[i] = groove * (0.85 + 0.15 * gr); alb[i] = tone[r][seg] * (0.72 + 0.32 * gr) * (0.4 + 0.6 * groove); rough[i] = 0.8 + 0.2 * (1 - gr);
    }
    return { h, alb, rough };
  } },
  // タイル（市松の床）：色は引数。1枚 = 2×2 マス
  tiles: { size: 256, tile: 2, ns: 2.2, absRough: true, gen(N, a) {
    // THREE.Color は内部がリニアなので、sRGB に戻してからキャンバスに描く
    const c1 = new THREE.Color(a.c1).convertLinearToSRGB(), c2 = new THREE.Color(a.c2).convertLinearToSRGB();
    const s1 = [c1.r, c1.g, c1.b], s2 = [c2.r, c2.g, c2.b], n = tnoise(N, 6, 6, 161, 3), f = tnoise(N, 48, 48, 162, 1);
    const h = new Float32Array(N * N), rgb = new Float32Array(N * N * 3), rough = new Float32Array(N * N), half = N / 2;
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x, cx = x < half ? 0 : 1, cy = y < half ? 0 : 1, fx = x - cx * half, fy = y - cy * half;
      const e = Math.min(fx, half - 1 - fx, fy, half - 1 - fy), grout = _ss(1.2, 2.6, e), bev = _ss(1.2, 6, e);
      const c = (cx ^ cy) ? s2 : s1, v = (0.95 + 0.08 * (n[i] - 0.5)) * (0.55 + 0.45 * grout);
      rgb[i * 3] = c[0] * v; rgb[i * 3 + 1] = c[1] * v; rgb[i * 3 + 2] = c[2] * v;
      h[i] = bev * 0.9 + f[i] * 0.05 + n[i] * 0.05; rough[i] = grout > 0.5 ? 0.12 + 0.18 * n[i] : 0.85;
    }
    return { h, rgb, rough };
  } },
};

/* 生成したテクスチャ一式（同じ質感・同じ引数なら使い回す → GLB にも1枚だけ入る） */
const SURF_CACHE = new Map();
const SURF_STATS = { count: 0, ms: 0 };
function surfaceTextures(name, args = null) {
  const def = SURFACES[name]; if (!def) return null;
  const key = name + (args ? '|' + JSON.stringify(args) : '');
  if (SURF_CACHE.has(key)) return SURF_CACHE.get(key);
  const t0 = performance.now();
  // 引数つき（色を含む床など）は色を変えるたびに増えるので、同じ質感の古いものは 3 つまで残して片付ける
  if (args) { const old = [...SURF_CACHE.keys()].filter(k => k.startsWith(name + '|')); while (old.length >= 3) { const k = old.shift(); const o = SURF_CACHE.get(k); for (const t of [o.map, o.normalMap, o.orm]) t.dispose(); SURF_CACHE.delete(k); } }
  const N = def.size; const r = def.gen(N, args || {});
  const h = r.h, ns = 2.0;
  // AO：まわりより低いところ（溝・穴）を暗く
  const bl = tblur(h, N, Math.max(2, Math.round(N / 96)));
  const mk = (fill) => { const cv = document.createElement('canvas'); cv.width = cv.height = N; const g = cv.getContext('2d'); const im = g.createImageData(N, N); fill(im.data); g.putImageData(im, 0, 0); return cv; };
  const albCv = mk(d => {
    for (let i = 0; i < N * N; i++) {
      if (r.rgb) { d[i * 4] = r.rgb[i * 3] * 255; d[i * 4 + 1] = r.rgb[i * 3 + 1] * 255; d[i * 4 + 2] = r.rgb[i * 3 + 2] * 255; }
      else { const v = Math.max(0, Math.min(1, r.alb[i])) * 255; d[i * 4] = d[i * 4 + 1] = d[i * 4 + 2] = v; }
      d[i * 4 + 3] = 255;
    }
  });
  const nrmCv = mk(d => {
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const i = y * N + x; let nx, ny;
      if (r.nx) { nx = r.nx[i]; ny = r.ny[i]; }
      else {
        nx = -(h[y * N + (x + 1) % N] - h[y * N + (x - 1 + N) % N]) * ns;
        ny = (h[((y + 1) % N) * N + x] - h[((y - 1 + N) % N) * N + x]) * ns;   // 緑 = 画像の上向き（glTF の決まり）
      }
      const L = Math.hypot(nx, ny, 1);
      d[i * 4] = (nx / L * 0.5 + 0.5) * 255; d[i * 4 + 1] = (ny / L * 0.5 + 0.5) * 255; d[i * 4 + 2] = (1 / L * 0.5 + 0.5) * 255; d[i * 4 + 3] = 255;
    }
  });
  const aoK = 3.5 * (r.ao ?? 1);
  const ormCv = mk(d => {
    for (let i = 0; i < N * N; i++) {
      const ao = Math.max(0.35, Math.min(1, 1 - aoK * Math.max(0, bl[i] - h[i])));
      d[i * 4] = ao * 255; d[i * 4 + 1] = Math.max(0.02, Math.min(1, r.rough[i])) * 255; d[i * 4 + 2] = 255; d[i * 4 + 3] = 255;
    }
  });
  const tex = (cv, srgb) => {
    const t = new THREE.CanvasTexture(cv); t.flipY = false; t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.colorSpace = srgb ? THREE.SRGBColorSpace : THREE.NoColorSpace; t.anisotropy = 8; t.userData.shared = true;
    t.userData.mimeType = 'image/jpeg';   // GLB を軽くする（模様は細かいノイズなので JPEG で十分）
    return t;
  };
  const out = { map: tex(albCv, true), normalMap: tex(nrmCv, false), orm: tex(ormCv, false), def };
  SURF_CACHE.set(key, out);
  SURF_STATS.count++; SURF_STATS.ms += performance.now() - t0;
  (SURF_STATS.each || (SURF_STATS.each = {}))[name] = Math.round(performance.now() - t0);
  return out;
}
/* マテリアルに質感を付ける。o.tile：1枚のメートル（省略時は質感の既定）、o.ns：ノーマルの強さ、o.args：色などの引数 */
function applySurface(m, name, o = {}) {
  const S = surfaceTextures(name, o.args); if (!S) return m;
  if (!m.map || o.replaceMap) m.map = S.map;
  m.normalMap = S.normalMap; const k = (o.ns ?? 1) * S.def.ns; m.normalScale.set(k, -k);
  m.roughnessMap = m.metalnessMap = m.aoMap = S.orm; m.aoMapIntensity = o.ao ?? 1;
  if (S.def.absRough) m.roughness = 1;
  m.userData.surf = name; m.userData.surfTile = o.tile ?? S.def.tile;
  m.needsUpdate = true;
  return m;
}

/* ---------- UV を「メートル ÷ tile」に合わせる ----------
   形の種類ごとに、テクスチャの向き（筋・ひだ・毛の流れ）が自然になる方向で UV を作り直す */
function fitUV(geo, mats) {
  const uv = geo.attributes.uv, pos = geo.attributes.position; if (!uv || !pos) return;
  const tileOf = (mi) => (mats[mi] && mats[mi].userData.surfTile) || 0;
  const one = mats.map((m, i) => tileOf(i)).find(t => t > 0) || 1;
  const p = geo.parameters || {};
  const scaleRange = (from, to, su, sv, cu = 0, cv = 0) => { for (let i = from; i < to; i++) uv.setXY(i, (uv.getX(i) - cu) * su, (uv.getY(i) - cv) * sv); };
  const around = (len, t, full) => full ? Math.max(1, Math.round(len / t)) : len / t;   // 一周するものは整数回にして継ぎ目を消す
  switch (geo.type) {
    case 'CylinderGeometry': case 'ConeGeometry': {
      const rt = geo.type === 'ConeGeometry' ? 0 : p.radiusTop, rb = geo.type === 'ConeGeometry' ? p.radius : p.radiusBottom;
      const rs = p.radialSegments, hs = p.heightSegments, arc = p.thetaLength ?? TAU;
      const torso = (rs + 1) * (hs + 1), C = arc * (rt + rb) / 2, side = Math.hypot(p.height, rt - rb);
      // グループ：0 = 側面、1 = 上のふた、2 = 下のふた（マテリアルを分けていればそれぞれの質感で）
      const tS = tileOf(0) || one, tCaps = [tileOf(1) || one, tileOf(2) || one];
      scaleRange(0, torso, around(C, tS, Math.abs(arc - TAU) < 1e-4), side / tS);
      let start = torso, ci = 0; const capN = 2 * rs + 1;   // ふた（中心 0.5,0.5 の円）は「中心 rs 個＋ふち rs+1 個」。半径 0 の側は作られない
      if (!p.openEnded) for (const r of [rt, rb]) { if (r > 0) { const tc = tCaps[ci]; scaleRange(start, Math.min(uv.count, start + capN), 2 * r / tc, 2 * r / tc, 0.5, 0.5); start += capN; } ci++; }
      break;
    }
    case 'SphereGeometry': { const r = p.radius, ph = p.phiLength ?? TAU; scaleRange(0, uv.count, around(r * ph, one, Math.abs(ph - TAU) < 1e-4), r * (p.thetaLength ?? Math.PI) / one); break; }
    case 'IcosahedronGeometry': case 'OctahedronGeometry': case 'DodecahedronGeometry': { const r = p.radius; scaleRange(0, uv.count, around(r * TAU, one, true), r * Math.PI / one); break; }
    case 'TorusGeometry': { const arc = p.arc ?? TAU; scaleRange(0, uv.count, around(p.radius * arc, one, Math.abs(arc - TAU) < 1e-4), around(p.tube * TAU, one, true)); break; }
    case 'CapsuleGeometry': scaleRange(0, uv.count, around(p.radius * TAU, one, true), (p.length + Math.PI * p.radius) / one); break;
    case 'PlaneGeometry': scaleRange(0, uv.count, p.width / one, p.height / one); break;
    case 'CircleGeometry': case 'RingGeometry': { const r = p.radius ?? p.outerRadius; scaleRange(0, uv.count, 2 * r / one, 2 * r / one, 0.5, 0.5); break; }
    case 'ExtrudeGeometry': case 'ShapeGeometry': {
      // UV はもともとメートル（形の座標）。グループごとの質感で割る
      if (geo.groups.length && mats.length > 1) {
        const idx = geo.index;
        for (const gr of geo.groups) { const t = tileOf(gr.materialIndex); if (!t) continue; const seen = new Set();
          for (let k = gr.start; k < gr.start + gr.count; k++) { const v = idx ? idx.getX(k) : k; if (seen.has(v)) continue; seen.add(v); uv.setXY(v, uv.getX(v) / t, uv.getY(v) / t); } }
      } else scaleRange(0, uv.count, 1 / one, 1 / one);
      break;
    }
    default: {
      // 箱・結合した形など：面の向きに合わせた投影（側面は縦＝V、上面は X・Z）
      let nor = geo.attributes.normal; if (!nor) { geo.computeVertexNormals(); nor = geo.attributes.normal; }
      const tiles = new Float32Array(uv.count).fill(one);
      if (geo.groups.length && mats.length > 1) {
        const idx = geo.index;
        for (const gr of geo.groups) { const t = tileOf(gr.materialIndex); for (let k = gr.start; k < gr.start + gr.count; k++) tiles[idx ? idx.getX(k) : k] = t; }
      }
      for (let i = 0; i < uv.count; i++) {
        const t = tiles[i]; if (!t) continue;
        const ax = Math.abs(nor.getX(i)), ay = Math.abs(nor.getY(i)), az = Math.abs(nor.getZ(i)), x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
        if (ay >= ax && ay >= az) uv.setXY(i, x / t, z / t);
        else if (ax >= az) uv.setXY(i, -z * Math.sign(nor.getX(i) || 1) / t, y / t);
        else uv.setXY(i, x * Math.sign(nor.getZ(i) || 1) / t, y / t);
      }
    }
  }
  uv.needsUpdate = true;
}
/* 結合する前の部品の UV を合わせる（結合すると形の種類が分からなくなるため） */
function prefitUV(geo, tile) { fitUV(geo, [{ userData: { surfTile: tile } }]); geo.userData.uvKey = String(tile); return geo; }
/* 組み立てたものの中で、質感付きのマテリアルを使うメッシュの UV を合わせる（共有の形は複製してから） */
function fitSurfaceUVs(root) {
  root.traverse(o => {
    if (!o.isMesh || !o.geometry || !o.geometry.attributes.uv) return;
    const mats = Array.isArray(o.material) ? o.material : [o.material];
    if (!mats.some(m => m && m.userData && m.userData.surfTile)) return;
    const key = mats.map(m => (m && m.userData && m.userData.surfTile) || 0).join(',');
    let g = o.geometry;
    if (g.userData.uvKey === key) return;
    if (g.userData.uvKey !== undefined || g.userData.shared) { const c = g.clone(); c.userData = { ...g.userData, shared: false, uvKey: undefined }; o.geometry = g = c; }
    fitUV(g, mats);
    g.userData.uvKey = key;
  });
}
