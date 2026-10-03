
/* =====================================================================
   ステージ本体（メイン形状・花道・サブステージ・正面階段・ひな壇・センターサークル）
   ===================================================================== */
const STAGE_INFO = { H: 1.2, pieces: [], upper: null, front: () => 4 };
let reflector = null;
const QUALITY = { level: 'mid', reflect: true, reflectScale: 0.5, camRes: 480, pixelRatio: 1.5, camEvery: 1 };

const apronR = st => Math.min(st.width * 0.35, st.depth * 0.8);
function stageFront(st) {
  const hw = st.width / 2, hd = st.depth / 2, W = st.width, D = st.depth;
  switch (st.shape) {
    case 'octagon': { const c = Math.min(W, D) * 0.22; return x => Math.abs(x) > hw - c ? hd - (Math.abs(x) - (hw - c)) : hd; }
    case 'apron': { const R = apronR(st); return x => Math.abs(x) < R ? hd + Math.sqrt(R * R - x * x) : hd; }
    case 'round': return x => -hd + D * Math.sqrt(Math.max(0, 1 - (x / hw) ** 2));
    default: return () => hd;
  }
}
function stageOutline(st) {
  const hw = st.width / 2, hd = st.depth / 2, D = st.depth, W = st.width;
  const P = [];
  switch (st.shape) {
    case 'octagon': { const c = Math.min(W, D) * 0.22; P.push([-hw, -hd], [hw, -hd], [hw, hd - c], [hw - c, hd], [-hw + c, hd], [-hw, hd - c]); break; }
    case 'apron': { const R = apronR(st); P.push([-hw, -hd], [hw, -hd], [hw, hd]); const N = 40; for (let i = 0; i <= N; i++) { const a = i / N * Math.PI; P.push([R * Math.cos(a), hd + R * Math.sin(a)]); } P.push([-hw, hd]); break; }
    case 'round': { const N = 64; for (let i = 0; i <= N; i++) { const a = i / N * Math.PI; P.push([hw * Math.cos(a), -hd + D * Math.sin(a)]); } break; }
    default: P.push([-hw, -hd], [hw, -hd], [hw, hd], [-hw, hd]);
  }
  return P;
}
const rectPts = (x0, z0, x1, z1) => [[x0, z0], [x1, z0], [x1, z1], [x0, z1]];
const circlePts = (cx, cz, r, n = 64) => Array.from({ length: n }, (_, i) => [cx + r * Math.cos(i / n * TAU), cz + r * Math.sin(i / n * TAU)]);

function stageLayout(st) {
  const front = stageFront(st);
  const L = { front, H: st.height, pieces: [], rw: 0, subC: null };
  L.pieces.push({ kind: 'Main', P: stageOutline(st) });
  const zf = front(0);
  const rw = st.runway.on ? Math.min(st.runway.width, st.width * 0.8) : 0;
  const hw2 = rw / 2;
  let zEnd = zf + st.runway.length;
  if (st.sub.on) {
    const s = st.sub.size;
    if (st.sub.shape === 'round') {
      const r = s / 2, zc = zEnd + r;
      L.pieces.push({ kind: 'Sub', P: circlePts(0, zc, r, 72), round: true });
      L.subC = { z: zc, r, round: true };
      zEnd = zc - Math.sqrt(Math.max(0.01, r * r - hw2 * hw2)) + 0.05;
    } else {
      const w = s * 1.25, d = s * 0.85;
      L.pieces.push({ kind: 'Sub', P: rectPts(-w / 2, zEnd, w / 2, zEnd + d) });
      L.subC = { z: zEnd + d / 2, r: d / 2, round: false };
      zEnd = zEnd + 0.05;
    }
  }
  if (st.runway.on) {
    const z0 = Math.min(front(hw2), front(-hw2)) - 0.05;
    L.pieces.push({ kind: 'Runway', P: rectPts(-hw2, z0, hw2, zEnd) });
    L.runway = { z0, z1: zEnd };
  }
  L.rw = rw;
  return L;
}
function densifySegs(P, closed, maxLen = 0.3) {
  const out = []; const n = P.length; const m = closed ? n : n - 1;
  for (let i = 0; i < m; i++) {
    const a = P[i], b = P[(i + 1) % n];
    const len = Math.hypot(b[0] - a[0], b[1] - a[1]); const k = Math.max(1, Math.ceil(len / maxLen));
    for (let j = 0; j < k; j++) out.push([lerp(a[0], b[0], j / k), lerp(a[1], b[1], j / k), lerp(a[0], b[0], (j + 1) / k), lerp(a[1], b[1], (j + 1) / k)]);
  }
  return out;
}
function ribbonGeo(segs, y, th = 0.05) {
  const geos = [];
  for (const [x1, z1, x2, z2] of segs) {
    const dx = x2 - x1, dz = z2 - z1, len = Math.hypot(dx, dz); if (len < 1e-4) continue;
    const b = new THREE.BoxGeometry(th, th, len + th * 0.6);
    b.rotateY(Math.atan2(dx, dz)); b.translate((x1 + x2) / 2, y, (z1 + z2) / 2);
    geos.push(b);
  }
  return geos.length ? mergeGeometries(geos) : null;
}
function pointInPoly(x, z, P) {
  let inside = false;
  for (let i = 0, j = P.length - 1; i < P.length; j = i++) {
    const [xi, zi] = P[i], [xj, zj] = P[j];
    if (((zi > z) !== (zj > z)) && (x < (xj - xi) * (z - zi) / (zj - zi) + xi)) inside = !inside;
  }
  return inside;
}
/* ビームが当たる床の高さ（ステージ上なら天板、外なら地面） */
function floorHeightAt(x, z) {
  const U = STAGE_INFO.upper;
  if (U && Math.abs(x) <= U.w / 2 && z >= U.z0 && z <= U.z1) return U.top;
  for (const p of STAGE_INFO.pieces) if (pointInPoly(x, z, p.P)) return STAGE_INFO.H;
  return 0;
}

/* 床：模様の1周期（メートル）と向きを userData に入れておき、床の形の UV をそれに合わせる */
function floorMaterial(st) {
  const tile = Math.max(0.2, st.tile || 1);
  let m, period = 2 * tile, rot = 0;
  switch (st.floor) {
    case 'checker': m = stdMat({ color: '#ffffff', roughness: 0.22, metalness: 0.05, env: 0.6, surf: 'tiles', args: { c1: st.floorColor, c2: st.floorColor2 }, tile: period }); rot = Math.PI / 4; break;
    case 'grid': m = stdMat({ color: st.floorColor, roughness: 0.3, env: 0.4, emissive: st.floorColor2, ei: 1.8, emissiveMap: texGridLines(), surf: 'vinyl', tile: period }); break;
    case 'led': { period = 8 * tile; const t = ledFloorTexture(); t.repeat.set(1, 1); m = stdMat({ color: '#111118', roughness: 0.25, env: 0.4, emissive: '#ffffff', ei: 1.4, emissiveMap: t, surf: 'vinyl', tile: period }); break; }
    case 'starry': period = 4 * tile; m = stdMat({ color: st.floorColor, roughness: 0.3, metalness: 0.2, env: 0.5, emissive: st.floorColor2, ei: 1.5, emissiveMap: texStarry(), surf: 'vinyl', tile: period }); break;
    case 'wood': m = stdMat({ color: st.floorColor, roughness: 0.55, env: 0.3, surf: 'planks', tile: period }); break;
    case 'mirror': m = stdMat({ color: st.floorColor, roughness: 0.04, metalness: 0.7, env: 0.9, surf: 'vinyl', tile: period, ns: 0.5 }); break;
    default: m = stdMat({ color: st.floorColor, roughness: 0.14, metalness: 0.2, env: 0.5, surf: 'vinyl', tile: period });
  }
  m.userData.floorRot = rot;
  if (st.reflect && QUALITY.reflect) { m.transparent = true; m.opacity = 1 - clamp(st.reflectStrength, 0, 1) * 0.72; m.userData.reflectFloor = true; }
  return m;
}
/* ステージ側面の UV：横 = 外周に沿った長さ（丸いステージでも模様が途切れない）、縦 = 上端からの距離（パネルの目地が上端にそろう）。単位はメートル
   ExtrudeGeometry の側面は「外周の1辺 = 頂点6個（a,b,d,b,c,d）」の順に並んでいる */
function sideWallUV(geo, H) {
  const g1 = geo.groups[1]; if (!g1 || geo.index) return;
  const uv = geo.attributes.uv, pos = geo.attributes.position; let acc = 0;
  for (let q = g1.start; q + 5 < g1.start + g1.count; q += 6) {
    const L = Math.hypot(pos.getX(q + 1) - pos.getX(q), pos.getZ(q + 1) - pos.getZ(q));
    const us = [acc, acc + L, acc, acc + L, acc + L, acc];
    for (let k = 0; k < 6; k++) uv.setXY(q + k, us[k], H - pos.getY(q + k));
    acc += L;
  }
}
/* 床の UV ＝（床の上の位置 x・-z を模様の向きに回して）÷ 1周期。fn は頂点 → 床の上の位置（メートル） */
function floorUV(geo, mat, fn) {
  const period = mat.userData.surfTile || 1, r = mat.userData.floorRot || 0, c = Math.cos(r), s = Math.sin(r);
  const uv = geo.attributes.uv, pos = geo.attributes.position;
  for (let i = 0; i < uv.count; i++) { const [x, y] = fn(pos.getX(i), pos.getY(i), pos.getZ(i)); uv.setXY(i, (x * c - y * s) / period, (x * s + y * c) / period); }
  uv.needsUpdate = true; geo.userData.uvKey = String(period);
}
function frontMaterial(st) {
  switch (st.front) {
    case 'ledgrid': return stdMat({ color: st.frontColor, roughness: 0.5, emissive: st.frontGlow, ei: 1.5, emissiveMap: texGridLines(), surf: 'powder', tile: 0.25 });
    case 'mirror': return stdMat({ color: st.frontColor, roughness: 0.1, metalness: 0.9, env: 1.0, surf: 'alu', tile: 0.6 });
    case 'plain': return stdMat({ color: st.frontColor, roughness: 0.7, surf: 'powder' });
    case 'skirt': return stdMat({ color: st.frontColor, roughness: 0.95, env: 0.12, surf: 'velour' });
    default: return stdMat({ color: st.frontColor, roughness: 0.55, env: 0.4, surf: 'panel' });
  }
}

/* 階段（z=0 が段の奥＝ステージ側、+z に向かって下っていく） */
function buildStairs(o) {
  const g = new THREE.Group(); g.name = 'Stairs';
  const n = clamp(Math.round(o.count), 1, 12), H = o.height, t = o.tread, W = o.width;
  const mat = stdMat({ color: o.color, roughness: 0.45, env: 0.45, surf: 'deck' });
  const dots = [];
  for (let k = 1; k <= n; k++) {
    const top = H * (n + 1 - k) / (n + 1);
    const box = new THREE.Mesh(new THREE.BoxGeometry(W, top, t), mat);
    box.position.set(0, top / 2, (k - 0.5) * t); g.add(box);
    const below = k < n ? H * (n - k) / (n + 1) : 0;
    if (o.led) dots.push({ y: (top + below) / 2, z: k * t + 0.004, row: k });
  }
  if (o.led && dots.length) {
    const sp = 0.24, nx = Math.max(1, Math.floor((W - 0.2) / sp));
    const im = new THREE.InstancedMesh(new THREE.CircleGeometry(0.045, 10), new THREE.MeshBasicMaterial({ color: '#ffffff' }), nx * dots.length);
    const M = new THREE.Matrix4(); const c1 = col(o.led1, 2.2), c2 = col(o.led2, 2.2); let i = 0;
    for (const d of dots) for (let x = 0; x < nx; x++) {
      M.makeTranslation(-((nx - 1) * sp) / 2 + x * sp, d.y, d.z); im.setMatrixAt(i, M);
      im.setColorAt(i, ((x >> 1) + d.row) % 2 ? c1 : c2); i++;
    }
    im.name = 'StairsLED'; g.add(im);
    // 段鼻のライン
    const nose = glowMat(o.led1, 1.4);
    for (let k = 1; k <= n; k++) {
      const top = H * (n + 1 - k) / (n + 1);
      const l = new THREE.Mesh(new THREE.BoxGeometry(W, 0.025, 0.025), nose); l.position.set(0, top, k * t - 0.012); g.add(l);
    }
  }
  return g;
}

function buildStage() {
  if (reflector) { reflector.dispose(); reflector = null; }
  clearGroup(G.stage);
  const st = S.stage; const H = Math.max(0.2, st.height);
  const L = stageLayout(st);
  STAGE_INFO.H = H; STAGE_INFO.pieces = L.pieces; STAGE_INFO.front = L.front; STAGE_INFO.layout = L; STAGE_INFO.upper = null;
  const capMat = stdMat({ color: st.bodyColor, roughness: 0.85, surf: 'powder' });
  const sideMat = frontMaterial(st);
  const floorMat = floorMaterial(st);
  const floorGeos = [];
  L.pieces.forEach((pc, i) => {
    const shape = new THREE.Shape(pc.P.map(([x, z]) => new THREE.Vector2(x, -z)));
    const bg = new THREE.ExtrudeGeometry(shape, { depth: H, bevelEnabled: false, curveSegments: 6 }).rotateX(-Math.PI / 2);
    sideWallUV(bg, H);
    const body = new THREE.Mesh(bg, [capMat, sideMat]);
    body.name = 'Stage_' + pc.kind; G.stage.add(body);
    const fg = new THREE.ShapeGeometry(shape, 12);
    floorGeos.push(fg.clone());
    floorUV(fg, floorMat, (x, y) => [x, y]);
    const fl = new THREE.Mesh(fg, floorMat); fl.name = 'StageFloor_' + pc.kind;
    fl.rotation.x = -Math.PI / 2; fl.position.y = H + 0.004 + i * 0.0015; fl.renderOrder = -2;
    G.stage.add(fl);
  });
  if (st.reflect && QUALITY.reflect) {
    const w = Math.max(256, Math.round(viewEl.clientWidth * QUALITY.reflectScale)), h = Math.max(256, Math.round(viewEl.clientHeight * QUALITY.reflectScale));
    reflector = new Reflector(mergeGeometries(floorGeos), { clipBias: 0.003, textureWidth: w, textureHeight: h, color: 0x9a9a9a });
    reflector.rotation.x = -Math.PI / 2; reflector.position.y = H + 0.002; reflector.name = 'Reflector';
    markFx(reflector); G.stage.add(reflector);
  }
  // エッジライト
  if (st.edge) {
    const hw2 = L.rw / 2; let segs = [];
    for (const pc of L.pieces) {
      let s = densifySegs(pc.P, true, 0.3);
      if (pc.kind === 'Main' && L.runway) s = s.filter(([a, b, c, d]) => !(Math.abs((a + c) / 2) < hw2 - 0.02 && (b + d) / 2 > 0));
      if (pc.kind === 'Sub' && L.runway) s = s.filter(([a, b, c, d]) => !(Math.abs((a + c) / 2) < hw2 - 0.02 && (b + d) / 2 < L.subC.z));
      if (pc.kind === 'Runway') s = s.filter(([a, b, c, d]) => Math.abs(b - d) > 1e-3 || (!L.subC && Math.abs((b + d) / 2 - L.runway.z1) < 1e-3));
      segs = segs.concat(s);
    }
    const em = glowMat(st.edgeColor, st.edgeIntensity);
    const top = ribbonGeo(segs, H + 0.03, 0.055);
    if (top) { const m = new THREE.Mesh(top, em); m.name = 'StageEdgeLight'; G.stage.add(m); }
    if (st.edgeBottom) { const bot = ribbonGeo(segs, 0.04, 0.06); if (bot) { const m = new THREE.Mesh(bot, em); m.name = 'StageEdgeLightBottom'; G.stage.add(m); } }
  }
  // 正面階段
  const sp = st.steps;
  if (sp.on) {
    const xs = sp.pos === 'sides' ? [-sp.sideX, sp.sideX] : [0];
    for (const x of xs) {
      const s = buildStairs({ width: sp.width, count: sp.count, height: H, tread: sp.tread, color: sp.color, led: sp.led, led1: sp.led1, led2: sp.led2 });
      const z = Math.min(L.front(x - sp.width / 2), L.front(x + sp.width / 2), L.front(x));
      s.position.set(x, 0, z - 0.01); s.name = 'Stage_FrontStairs'; G.stage.add(s);
    }
  }
  // ひな壇（奥の上段）
  const up = st.upper;
  if (up.on) {
    const hd = st.depth / 2, uw = Math.min(up.width, st.width), ud = Math.min(up.depth, st.depth * 0.8);
    const box = new THREE.Mesh(new THREE.BoxGeometry(uw, up.height, ud), [sideMat, sideMat, capMat, capMat, sideMat, sideMat]);
    box.position.set(0, H + up.height / 2, -hd + ud / 2); box.name = 'Stage_Upper'; G.stage.add(box);
    const fgu = new THREE.PlaneGeometry(uw, ud).rotateX(-Math.PI / 2); floorUV(fgu, floorMat, (x, y, z) => [x, -(z - hd + ud / 2)]);
    const ft = new THREE.Mesh(fgu, floorMat);
    ft.position.set(0, H + up.height + 0.004, -hd + ud / 2); ft.renderOrder = -2; ft.name = 'StageFloor_Upper'; G.stage.add(ft);
    if (st.edge) {
      const z1 = -hd + ud, x = uw / 2, y = H + up.height + 0.03;
      const g = ribbonGeo([[-x, z1, x, z1], [-x, -hd, -x, z1], [x, -hd, x, z1]], y, 0.05);
      const m = new THREE.Mesh(g, glowMat(st.edgeColor, st.edgeIntensity)); G.stage.add(m);
    }
    const s = buildStairs({ width: up.stepWidth, count: up.stepCount, height: up.height, tread: 0.38, color: sp.color, led: sp.led, led1: sp.led1, led2: sp.led2 });
    s.position.set(0, H, -hd + ud - 0.01); s.name = 'Stage_UpperStairs'; G.stage.add(s);
    STAGE_INFO.upper = { w: uw, z0: -hd, z1: -hd + ud, top: H + up.height };
  }
  // センターサークル
  const rg = st.ring;
  if (rg.on) {
    const ring = new THREE.Mesh(new THREE.RingGeometry(rg.radius - 0.07, rg.radius, 96).rotateX(-Math.PI / 2), glowMat(rg.color, rg.intensity * 1.4));
    ring.position.set(0, H + 0.012, rg.z); ring.name = 'CenterRing'; G.stage.add(ring);
    const ring2 = new THREE.Mesh(new THREE.RingGeometry(rg.radius + 0.12, rg.radius + 0.16, 96).rotateX(-Math.PI / 2), glowMat(rg.color, rg.intensity * 0.7));
    ring2.position.copy(ring.position); G.stage.add(ring2);
    if (rg.disc) {
      const d = new THREE.Mesh(spotGeo, spotMat()); d.material.uniforms.uColor.value = col(rg.color); d.material.uniforms.uInt.value = 0.55 * rg.intensity;
      d.scale.set(rg.radius * 2.1, 1, rg.radius * 2.1); d.position.set(0, H + 0.01, rg.z); markFx(d); G.stage.add(d);
    }
  }
  fitSurfaceUVs(G.stage);
  G.stage.traverse(o => { o.userData.stagePart = true; });
}
