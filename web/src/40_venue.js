
/* =====================================================================
   共通パーツ（トラス・ネオン・LEDパネル）＋ 会場 ＋ 背景
   ===================================================================== */
const V3 = (x = 0, y = 0, z = 0) => new THREE.Vector3(x, y, z);
function cylBetween(p0, p1, r, seg = 5, tile = 0) {
  const d = p1.clone().sub(p0); const L = d.length();
  const g = new THREE.CylinderGeometry(r, r, L, seg, 1, true); if (tile) prefitUV(g, tile); g.translate(0, L / 2, 0);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(V3(0, 1, 0), d.normalize()));
  g.translate(p0.x, p0.y, p0.z); return g;
}
const trussCache = new Map();
/* 角トラス：+Y方向に長さ len、断面 w×w */
function trussGeo(len, w = 0.4) {
  len = Math.max(0.2, len); w = Math.max(0.1, w);
  const key = len.toFixed(2) + '|' + w.toFixed(2);
  if (trussCache.has(key)) return trussCache.get(key);
  const geos = []; const r1 = w * 0.07, r2 = w * 0.032, hw = w / 2;
  const ch = [[-hw, -hw], [hw, -hw], [hw, hw], [-hw, hw]];
  const T = SURFACES.alu.tile;   // パイプの長さ方向に筋が通るよう、結合する前に UV を合わせる
  for (const [x, z] of ch) { const c = new THREE.CylinderGeometry(r1, r1, len, 8, 1, true); prefitUV(c, T); c.translate(x, len / 2, z); geos.push(c); }
  const n = Math.max(1, Math.round(len / w)); const step = len / n;
  for (let f = 0; f < 4; f++) {
    const a = ch[f], b = ch[(f + 1) % 4];
    for (let i = 0; i < n; i++) {
      const y0 = i * step, y1 = (i + 1) * step;
      const s = i % 2 ? [b, a] : [a, b];
      geos.push(cylBetween(V3(s[0][0], y0, s[0][1]), V3(s[1][0], y1, s[1][1]), r2, 5, T));
      if (i % 2 === 0) geos.push(cylBetween(V3(a[0], y0, a[1]), V3(b[0], y0, b[1]), r2, 5, T));
    }
  }
  const g = mergeGeometries(geos); g.userData.shared = true; g.userData.uvKey = String(T);
  trussCache.set(key, g); return g;
}
/* axis: 'y'（縦） / 'x'（横） / 'z'（奥行） 。from は始点 */
function trussMesh(len, w, mat, from, axis = 'y') {
  const m = new THREE.Mesh(trussGeo(len, w), mat);
  if (axis === 'x') m.rotation.z = -Math.PI / 2;
  if (axis === 'z') m.rotation.x = Math.PI / 2;
  m.position.copy(from); m.name = 'Truss';
  return m;
}
/* 装飾ランプ（小さな灯体） */
function decoLamp(color = '#fff6e0', s = 1, body = '#20222c') {
  const g = new THREE.Group();
  const b = new THREE.Mesh(new THREE.CylinderGeometry(0.11 * s, 0.13 * s, 0.26 * s, 16), paintMat(body, { roughness: 0.5, metalness: 0.5 }));
  b.rotation.x = Math.PI / 2; g.add(b);
  const l = new THREE.Mesh(new THREE.CircleGeometry(0.09 * s, 16), glowMat(color, 3)); l.position.z = 0.135 * s; g.add(l);
  return g;
}

/* ---------- ネオン図形 ---------- */
const NEON_SHAPES = [['triangle', '三角'], ['circle', '丸'], ['star', '星'], ['heart', 'ハート'], ['diamond', 'ひし形'], ['square', '四角'], ['hexagon', '六角'], ['planet', '惑星'], ['note', '音符'], ['line', '直線'], ['bolt', 'いなずま']];
function shapePts2D(kind, s) {
  const P = []; const r = s / 2;
  const poly = (n, rot = Math.PI / 2, rr = r) => { for (let i = 0; i < n; i++) { const a = rot + i / n * TAU; P.push(new THREE.Vector2(Math.cos(a) * rr, Math.sin(a) * rr)); } };
  switch (kind) {
    case 'triangle': poly(3); break;
    case 'square': poly(4, Math.PI / 4); break;
    case 'diamond': P.push(new THREE.Vector2(0, r), new THREE.Vector2(r * 0.7, 0), new THREE.Vector2(0, -r), new THREE.Vector2(-r * 0.7, 0)); break;
    case 'hexagon': poly(6, 0); break;
    case 'star': for (let i = 0; i < 10; i++) { const a = Math.PI / 2 + i / 10 * TAU; const rr = i % 2 ? r * 0.45 : r; P.push(new THREE.Vector2(Math.cos(a) * rr, Math.sin(a) * rr)); } break;
    case 'heart': for (let i = 0; i < 72; i++) { const t = i / 72 * TAU; const x = 16 * Math.sin(t) ** 3, y = 13 * Math.cos(t) - 5 * Math.cos(2 * t) - 2 * Math.cos(3 * t) - Math.cos(4 * t); P.push(new THREE.Vector2(x / 34 * s, y / 34 * s + s * 0.05)); } break;
    case 'bolt': [[0.15, 0.5], [-0.25, 0.02], [0.02, 0.02], [-0.15, -0.5], [0.25, -0.02], [-0.02, -0.02]].forEach(([x, y]) => P.push(new THREE.Vector2(x * s, y * s))); break;
    default: poly(64, 0);
  }
  return P;
}
function neonTube(points2D, thick, mat, closed = true) {
  const pts = points2D.map(p => V3(p.x, p.y, 0));
  const curve = new THREE.CatmullRomCurve3(pts, closed, 'catmullrom', 0.02);
  return new THREE.Mesh(new THREE.TubeGeometry(curve, Math.max(24, pts.length * 6), thick, 6, closed), mat);
}
function neonShape(kind, size, thick, color, inten = 2.2, fill = 0) {
  const g = new THREE.Group(); g.name = 'Neon_' + kind;
  const mat = glowMat(color, inten);
  if (kind === 'planet') {
    g.add(neonTube(shapePts2D('circle', size * 0.62), thick, mat));
    const ring = shapePts2D('circle', size).map(p => new THREE.Vector2(p.x * 1.0, p.y * 0.32));
    const t = neonTube(ring, thick, mat); t.rotation.z = 0.35; g.add(t);
    if (fill > 0) { const f = new THREE.Mesh(new THREE.CircleGeometry(size * 0.31, 40), glowMat(color, 0.6 * fill, { transparent: true, opacity: 0.35 * fill })); f.position.z = -0.02; g.add(f); }
  } else if (kind === 'note') {
    const head = shapePts2D('circle', size * 0.32).map(p => new THREE.Vector2(p.x * 1.25 - size * 0.12, p.y - size * 0.32));
    g.add(neonTube(head, thick, mat));
    g.add(neonTube([new THREE.Vector2(size * 0.08, -size * 0.3), new THREE.Vector2(size * 0.08, size * 0.42), new THREE.Vector2(size * 0.32, size * 0.25)], thick, mat, false));
  } else if (kind === 'line') {
    g.add(neonTube([new THREE.Vector2(-size / 2, 0), new THREE.Vector2(size / 2, 0)], thick, mat, false));
  } else {
    const P = shapePts2D(kind, size);
    g.add(neonTube(P, thick, mat));
    if (fill > 0) {
      const f = new THREE.Mesh(new THREE.ShapeGeometry(new THREE.Shape(P), 24), glowMat(color, 0.9 * fill + 0.2, { transparent: true, opacity: Math.min(1, 0.25 + fill * 0.75) }));
      f.position.z = -0.01; g.add(f);
    }
  }
  return g;
}
/* LED パネル（パターン or 画像）。ledList に登録すると時間で動く */
function ledPanel(w, h, o, ledList) {
  const m = ledMat({ pattern: o.pattern, c1: o.c1, c2: o.c2, c3: o.c3, bright: o.bright ?? 1.2, dotsX: Math.max(8, Math.round(w * (o.density ?? 9))), dotsY: Math.max(8, Math.round(h * (o.density ?? 9))), aspect: w / h, gap: o.gap ?? 0.3, round: o.round, led: o.led });
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, h), m); mesh.name = 'LEDPanel';
  if (ledList) ledList.push(m);
  return mesh;
}
function normalizeUV(geo) {
  geo.computeBoundingBox(); const b = geo.boundingBox; const uv = geo.attributes.uv; const p = geo.attributes.position;
  for (let i = 0; i < uv.count; i++) uv.setXY(i, (p.getX(i) - b.min.x) / (b.max.x - b.min.x), (p.getY(i) - b.min.y) / (b.max.y - b.min.y));
  uv.needsUpdate = true; return geo;
}

/* ---------- 会場 ---------- */
const VENUE_TYPES = [['hall', 'ホール（屋内アリーナ）'], ['dome', 'ドーム（星空天井）'], ['outdoor_day', '野外フェス（昼）'], ['outdoor_night', '野外フェス（夜）'], ['void', 'シンプル（暗転・背景色のみ）']];
const VENUE_ENV = { hemi: 0.35, key: 0.35, sun: 0, fogMul: 1, beamMul: 1 };
const VENUE_ANIMS = [];
function crowdDotsMat() {
  return new THREE.ShaderMaterial({
    uniforms: { uC1: { value: col('#ffffff') }, uC2: { value: col('#ffffff') }, uC3: { value: col('#ffffff') }, uBeat: { value: 0 }, uMode: { value: 0 }, uTex: { value: texSoft() } },
    vertexShader: /* glsl */`attribute float aR; uniform float uBeat; varying float vR; void main(){ vR = aR; vec3 p = position; p.y += 0.12*sin(uBeat*3.14159 + aR*6.0); p.x += 0.08*sin(uBeat*3.14159*0.5 + aR*9.0);
      vec4 mv = modelViewMatrix*vec4(p,1.0); gl_PointSize = 9.0 * (40.0 / -mv.z); gl_Position = projectionMatrix*mv; }`,
    fragmentShader: /* glsl */`uniform vec3 uC1; uniform vec3 uC2; uniform vec3 uC3; uniform float uMode; uniform sampler2D uTex; varying float vR;
      void main(){ float a = texture2D(uTex, gl_PointCoord).r; vec3 c = vR<0.33 ? uC1 : (vR<0.66 ? uC2 : uC3);
      if(uMode>0.5){ float h=fract(vR*7.0); c = clamp(abs(mod(h*6.0+vec3(0.0,4.0,2.0),6.0)-3.0)-1.0,0.0,1.0)*0.8+0.2; }
      gl_FragColor = vec4(c*1.6, a); }`,
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
  });
}
function buildSeats(root, v, z0, z1, width, withDots) {
  const rows = Math.floor((z1 - z0) / 0.9); const geos = [];
  for (let r = 0; r < rows; r++) { const h = 0.45 * (r + 1); const b = new THREE.BoxGeometry(width, h, 0.9); b.translate(0, h / 2, z0 + r * 0.9 + 0.45); geos.push(b); }
  const m = new THREE.Mesh(mergeGeometries(geos), stdMat({ color: '#15172a', roughness: 0.9, env: 0.05, surf: 'fabric', tile: 0.3 })); m.name = 'Venue_Seats'; root.add(m);
  if (withDots) {
    const n = Math.round(rows * width * 1.1); const pos = new Float32Array(n * 3), rr = new Float32Array(n); const R = mulberry32(21);
    for (let i = 0; i < n; i++) { const r = Math.floor(R() * rows); pos[i * 3] = (R() - 0.5) * width * 0.96; pos[i * 3 + 1] = 0.45 * (r + 1) + 1.3; pos[i * 3 + 2] = z0 + r * 0.9 + 0.45; rr[i] = R(); }
    const gg = new THREE.BufferGeometry(); gg.setAttribute('position', new THREE.BufferAttribute(pos, 3)); gg.setAttribute('aR', new THREE.BufferAttribute(rr, 1));
    const pts = new THREE.Points(gg, crowdDotsMat()); markFx(pts); pts.name = 'Venue_CrowdDots'; root.add(pts);
    VENUE_ANIMS.push((F) => { const u = pts.material.uniforms; u.uC1.value.copy(F.colors[0]); u.uC2.value.copy(F.colors[1]); u.uC3.value.copy(F.colors[2]); u.uBeat.value = F.beat; u.uMode.value = F.cue.penlight === 'rainbow' ? 1 : 0; });
  }
}
function buildTrees(root, color, count = 90, rMin = 110, rMax = 180) {
  const R = mulberry32(9);
  const cone = new THREE.InstancedMesh(new THREE.ConeGeometry(4, 12, 7), stdMat({ color, roughness: 0.9, env: 0.1 }), count);
  const M = new THREE.Matrix4(), q = new THREE.Quaternion(), s = V3();
  for (let i = 0; i < count; i++) {
    const a = R() * TAU, r = lerp(rMin, rMax, R()), k = 0.7 + R() * 1.2;
    M.compose(V3(Math.cos(a) * r, 6 * k, Math.sin(a) * r), q, s.set(k, k, k)); cone.setMatrixAt(i, M);
  }
  cone.name = 'Venue_Trees'; root.add(cone);
}
function buildVenue() {
  clearGroup(G.venue); VENUE_ANIMS.length = 0;
  const v = S.venue; const root = new THREE.Group(); G.venue.add(root);
  const ground = (mat, size = 300) => { const m = new THREE.Mesh(new THREE.PlaneGeometry(size, size).rotateX(-Math.PI / 2), mat); m.name = 'Venue_Ground'; m.position.y = -0.01; root.add(m); return m; };
  let bg = col(v.color);
  Object.assign(VENUE_ENV, { hemi: 0.35, key: 0.35, sun: 0, fogMul: 1, beamMul: 1 });
  sun.intensity = 0;
  const sky = (o) => { const m = new THREE.Mesh(new THREE.SphereGeometry(700, 48, 24), skyMat(o)); m.renderOrder = -100; markFx(m); m.name = 'Sky'; root.add(m); VENUE_ANIMS.push((F) => { m.material.uniforms.uTime.value = F.t; }); return m; };
  switch (v.type) {
    case 'hall': {
      ground(stdMat({ color: v.floorColor, roughness: 0.85, env: 0.12, surf: 'concrete' }));
      const walls = new THREE.Mesh(new THREE.BoxGeometry(120, 34, 130), stdMat({ color: v.color, roughness: 0.95, env: 0.05, side: THREE.BackSide, surf: 'velvet', tile: 3 }));
      walls.position.set(0, 16.9, 20); walls.name = 'Venue_Walls'; root.add(walls);
      // 天井リグ
      const tm = metalMat('#3a3d4a');
      for (let i = -3; i <= 3; i++) root.add(trussMesh(70, 0.45, tm, V3(-35, 22, i * 6 + 8), 'x'));
      for (let i = -2; i <= 2; i++) root.add(trussMesh(40, 0.45, tm, V3(i * 12, 22, -12), 'z'));
      if (v.stars) {
        const n = 260, pos = new Float32Array(n * 3); const R = mulberry32(4);
        for (let i = 0; i < n; i++) { pos[i * 3] = (R() - 0.5) * 110; pos[i * 3 + 1] = 33 - R() * 0.5; pos[i * 3 + 2] = (R() - 0.5) * 120 + 20; }
        const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        const p = new THREE.Points(g, new THREE.PointsMaterial({ color: col('#cfe0ff', 2), size: 0.35, map: texSoft(), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
        markFx(p); root.add(p);
      }
      if (v.seats) buildSeats(root, v, 30, 52, 70, v.crowdDots);
      bg = col(v.color).multiplyScalar(0.6);
      break;
    }
    case 'dome': {
      ground(stdMat({ color: v.floorColor, roughness: 0.6, env: 0.15, surf: 'concrete' }));
      const c = new THREE.Color(v.color);
      sky({ top: '#' + c.clone().multiplyScalar(1.6).getHexString(), hor: v.color, bot: v.color, stars: v.stars ? 1 : 0 });
      if (v.seats) buildSeats(root, v, 30, 55, 80, v.crowdDots);
      break;
    }
    case 'outdoor_day': {
      ground(stdMat({ color: '#4f8a3a', roughness: 0.95, env: 0.2, surf: 'grass' }), 600);
      sky({ top: '#3d8ff0', hor: '#d6ecff', bot: '#9fbf8f', clouds: 1 });
      buildTrees(root, '#2f5e2a');
      Object.assign(VENUE_ENV, { hemi: 1.1, key: 0.2, sun: 2.4, fogMul: 0.25, beamMul: 0.45 });
      bg = col('#d6ecff');
      break;
    }
    case 'outdoor_night': {
      ground(stdMat({ color: '#1d3320', roughness: 0.95, env: 0.1, surf: 'grass' }), 600);
      sky({ top: '#060a22', hor: '#1d2552', bot: '#0a0f14', stars: v.stars ? 1 : 0 });
      buildTrees(root, '#0c1a12');
      if (v.seats && v.crowdDots) buildSeats(root, v, 34, 50, 70, true);
      Object.assign(VENUE_ENV, { hemi: 0.4, key: 0.3, fogMul: 0.6 });
      bg = col('#1d2552');
      break;
    }
    default: {
      ground(stdMat({ color: v.floorColor, roughness: 0.35, metalness: 0.2, env: 0.3, surf: 'vinyl', tile: 3 }), 400);
      bg = col(v.color);
    }
  }
  fitSurfaceUVs(root);
  scene.background = bg; scene.fog.color.copy(bg);
  sun.intensity = VENUE_ENV.sun;
  applyLightLevels();
}
function applyLightLevels() {
  const L = S.lights;
  hemi.intensity = VENUE_ENV.hemi * L.ambient / 0.35;
  keyLight.intensity = VENUE_ENV.key * L.ambient / 0.35;
  scene.fog.density = L.fog * VENUE_ENV.fogMul;
  bloomPass.strength = L.bloom; bloomPass.radius = L.bloomRadius; bloomPass.threshold = L.bloomThreshold;
  renderer.toneMappingExposure = L.exposure;
}

/* ---------- 背景（差し替えアセット） ---------- */
const BACKDROP_TYPES = [['truss_led', 'トラス＋LEDパネル（ポップ）'], ['led_wall', '大型LEDウォール（アリーナ）'], ['starframe', '光の額縁スクリーン（クール）'], ['neon_geo', 'ネオン幾何学（アニメ風）'], ['arc', 'リングアーチ（近未来）'], ['speaker_wall', 'スピーカーウォール（DJ）'], ['curtain', 'カーテン（劇場）'], ['image', '画像パネル（自分の画像に差し替え）'], ['none', 'なし']];
const BD_LEDS = [];
const BD_ANIMS = [];
const BACKDROPS = {
  truss_led(root, b, W, H) {
    const tm = metalMat('#c9ced8');
    const ow = 0.45;
    for (const s of [-1, 1]) root.add(trussMesh(H, ow, tm, V3(s * W / 2, 0, 0)));
    root.add(trussMesh(W + ow, ow, tm, V3(-W / 2 - ow / 2, H, 0), 'x'));
    const iw = W * 0.21, ih = H * 0.8;
    for (const s of [-1, 1]) root.add(trussMesh(ih, 0.4, tm, V3(s * iw, 0, 0.6)));
    root.add(trussMesh(iw * 2 + 0.4, 0.4, tm, V3(-iw - 0.2, ih, 0.6), 'x'));
    root.add(trussMesh(W + ow, 0.35, tm, V3(-W / 2 - ow / 2, H * 0.62, -0.4), 'x'));
    for (const s of [-1, 1]) {
      const p = ledPanel(W * 0.13, H * 0.66, b, BD_LEDS); p.position.set(s * W * 0.355, H * 0.42, 0.15); root.add(p);
      const fr = new THREE.Mesh(new THREE.BoxGeometry(W * 0.13 + 0.2, H * 0.66 + 0.2, 0.1), paintMat('#2a2f3c', { roughness: 0.5, metalness: 0.5 }));
      fr.position.set(s * W * 0.355, H * 0.42, 0.08); root.add(fr);
      const cols = [b.c2, b.c3, b.c1, b.c2, b.c3];
      for (let i = 0; i < 5; i++) {
        const tri = neonShape('triangle', 0.85, 0.035, cols[i], 1.8, 1);
        tri.position.set(s * (W / 2 + 0.05), H * (0.2 + i * 0.16), 0.45); tri.rotation.z = (i % 2 ? Math.PI : 0) + s * 0.2; root.add(tri);
      }
    }
    const wall = new THREE.Mesh(new THREE.PlaneGeometry(W * 1.7, H * 1.5), stdMat({ color: '#0b0e1c', roughness: 0.9, surf: 'velvet', tile: 2 })); wall.position.set(0, H * 0.7, -1.4); root.add(wall);
    const lm = glowMat(b.c1, 2.2);
    for (const s of [-1, 1]) for (const k of [0, 1]) {
      const a = V3(s * W * (0.18 + k * 0.32), H * 1.25, -1.3), c = V3(s * W * (0.36 + k * 0.32), H * 0.55, -1.3);
      root.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.LineCurve3(a, c), 2, 0.05, 6), lm));
    }
    for (let i = 0; i < 9; i++) { const l = decoLamp('#fff2d8', 1.2); l.position.set(-W * 0.4 + i * W * 0.1, H - 0.35, 0.25); l.rotation.x = 0.5; root.add(l); }
  },
  led_wall(root, b, W, H) {
    const cw = W * 0.6, ch = H * 0.72;
    const c = ledPanel(cw, ch, b, BD_LEDS); c.position.set(0, 0.8 + ch / 2, 0); root.add(c);
    const fm = glowMat(b.c2, 1.8);
    const frame = (w, h, x, y, z, ry = 0) => { const g = new THREE.Group(); const t = 0.06;
      for (const [bw, bh, px, py] of [[w + t, t, 0, h / 2], [w + t, t, 0, -h / 2], [t, h, w / 2, 0], [t, h, -w / 2, 0]]) { const m = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, t), fm); m.position.set(px, py, 0.02); g.add(m); }
      g.position.set(x, y, z); g.rotation.y = ry; root.add(g); };
    frame(cw, ch, 0, 0.8 + ch / 2, 0);
    const top = ledPanel(cw * 0.7, H * 0.1, { ...b, pattern: 'bars' }, BD_LEDS); top.position.set(0, 0.8 + ch + H * 0.1, 0); root.add(top);
    for (const s of [-1, 1]) {
      const sw = W * 0.17, sh = H * 0.55;
      const p = ledPanel(sw, sh, b, BD_LEDS); const x = s * (cw / 2 + sw / 2 + 0.6);
      p.position.set(x, 0.8 + sh / 2, 0.6); p.rotation.y = -s * 0.22; root.add(p);
      frame(sw, sh, x, 0.8 + sh / 2, 0.6, -s * 0.22);
    }
    const back = new THREE.Mesh(new THREE.BoxGeometry(W * 1.1, H * 1.05, 0.6), paintMat('#0a0b12', { roughness: 0.8, metalness: 0.2 })); back.position.set(0, H * 0.52, -0.45); root.add(back);
    const tm = metalMat('#2c2f3a');
    for (const r of [0, 1]) {
      root.add(trussMesh(W * 1.05, 0.35, tm, V3(-W * 0.525, H * 1.02 + r * 0.9, 0.5 - r * 0.3), 'x'));
      for (let i = 0; i < 22; i++) { const l = decoLamp('#ffe8c0', 1); l.position.set(-W * 0.5 + i * W / 21, H * 1.02 + r * 0.9 - 0.25, 0.65 - r * 0.3); l.rotation.x = 0.35; root.add(l); }
    }
  },
  starframe(root, b, W, H) {
    const sw = W * 0.52, sh = H * 0.55, sy = H * 0.25 + sh / 2;
    const scr = new THREE.Mesh(new THREE.PlaneGeometry(sw, sh), stdMat({ color: '#22324a', roughness: 0.4, emissive: b.c1, ei: 0.12, transparent: true, opacity: 0.88 }));
    scr.position.set(0, sy, 0); root.add(scr);
    const pts = [];
    const per = (x0, y0, x1, y1) => { const n = Math.max(1, Math.round(Math.hypot(x1 - x0, y1 - y0) / 0.42)); for (let i = 0; i < n; i++) pts.push([lerp(x0, x1, i / n), lerp(y0, y1, i / n)]); };
    const hx = sw / 2 + 0.25, hy = sh / 2 + 0.25;
    per(-hx, -hy, hx, -hy); per(hx, -hy, hx, hy); per(hx, hy, -hx, hy); per(-hx, hy, -hx, -hy);
    const towers = [[-W * 0.33, H * 0.62], [-W * 0.42, H * 0.5], [W * 0.33, H * 0.62], [W * 0.42, H * 0.5]];
    const tm = metalMat('#5a6a8a');
    for (const [x, h] of towers) { root.add(trussMesh(h, 0.4, tm, V3(x, 0, 0.3))); for (let y = 0.5; y < h; y += 0.55) pts.push([x, y - sy, 0.55]); }
    const dg = new THREE.PlaneGeometry(0.13, 0.13); dg.rotateZ(Math.PI / 4);
    const im = new THREE.InstancedMesh(dg, new THREE.MeshBasicMaterial({ color: '#ffffff' }), pts.length);
    const M = new THREE.Matrix4(); const base = col(b.c3, 2.4);
    pts.forEach(([x, y, z], i) => { M.makeTranslation(x, y + sy, (z ?? 0) + 0.05); im.setMatrixAt(i, M); im.setColorAt(i, base); });
    im.name = 'FrameLights'; root.add(im);
    const tmp = new THREE.Color();
    BD_ANIMS.push((F) => { for (let i = 0; i < pts.length; i++) { const k = 0.55 + 0.45 * Math.sin(F.t * 2 + i * 0.7) + (Math.floor(F.beat * 4) % pts.length === i ? 1.5 : 0); im.setColorAt(i, tmp.copy(base).multiplyScalar(k)); } im.instanceColor.needsUpdate = true; });
    for (let i = 0; i < 8; i++) {
      const pw = W * 0.08, ph = H * (0.5 + 0.35 * hash1(i + 3));
      const p = new THREE.Mesh(new THREE.BoxGeometry(pw, ph, 0.2), stdMat({ color: '#141c33', emissive: b.c1, ei: 0.08, roughness: 0.6 }));
      p.position.set(-W * 0.62 + i * W * 0.177, ph / 2, -2.5); root.add(p);
    }
    for (let i = 0; i < 5; i++) { const l = decoLamp('#e8f3ff', 1.3); l.position.set(-W * 0.3 + i * W * 0.15, H * 1.0, 0.4); l.rotation.x = 1.1; root.add(l); }
  },
  neon_geo(root, b, W, H) {
    const back = ledPanel(W * 1.3, H * 1.15, { ...b, pattern: b.pattern === 'triangles' ? 'dots' : b.pattern, bright: b.bright * 0.75, density: 5 }, BD_LEDS);
    back.position.set(0, H * 0.55, -0.6); root.add(back);
    const Y = '#fff38f';
    const items = [
      ['triangle', 2.6, b.c3, W * 0.3, H * 0.82, 0.3], ['triangle', 1.2, b.c1, -W * 0.2, H * 0.9, -0.4], ['planet', 2.4, Y, -W * 0.38, H * 0.45, 0],
      ['circle', 1.4, b.c1, W * 0.12, H * 0.62, 0], ['circle', 0.8, b.c2, W * 0.2, H * 0.5, 0], ['circle', 0.6, Y, -W * 0.08, H * 0.58, 0],
      ['diamond', 1.6, b.c1, -W * 0.48, H * 0.82, 0.2], ['planet', 2.2, b.c1, W * 0.42, H * 0.42, 0.4], ['circle', 0.9, b.c2, -W * 0.25, H * 0.35, 0],
      ['square', 1.0, b.c2, -W * 0.05, H * 0.95, 0.3], ['star', 1.2, Y, W * 0.05, H * 1.0, 0],
    ];
    for (const [k, s, c, x, y, r] of items) { const n = neonShape(k, s, 0.045, c, 2.4, 0); n.position.set(x, y, 0.4); n.rotation.z = r; root.add(n); }
    const cm = stdMat({ color: b.c1, roughness: 0.15, metalness: 0.3, env: 0.8, emissive: b.c1, ei: 0.35, transparent: true, opacity: 0.85, flat: true });
    for (const s of [-1, 1]) for (const [dx, h, rz] of [[0, H * 1.0, 0.06], [0.9, H * 0.75, -0.08], [-0.7, H * 0.6, 0.1]]) {
      const c = new THREE.Mesh(new THREE.CylinderGeometry(0.35, 0.55, h, 4), cm); c.position.set(s * (W * 0.47 + dx), h / 2, 0.9); c.rotation.z = rz * s; c.rotation.y = 0.4; root.add(c);
    }
  },
  arc(root, b, W, H) {
    const R = W * 0.44;
    const band = new THREE.Mesh(new THREE.CylinderGeometry(R, R, 0.9, 96, 1, true), stdMat({ color: '#0d2a5a', roughness: 0.35, metalness: 0.4, emissive: b.c1, ei: 0.35, side: THREE.DoubleSide }));
    band.position.set(0, H * 0.95, R * 0.55); band.rotation.x = 0.12; root.add(band);
    for (const dy of [-0.45, 0.45]) { const t = new THREE.Mesh(new THREE.TorusGeometry(R, 0.05, 6, 128), glowMat(b.c1, 2.4)); t.rotation.x = Math.PI / 2; t.position.y = dy; band.add(t); }
    const colM = paintMat('#1b3d7a', { roughness: 0.3, metalness: 0.5, env: 0.6 });
    const barM = glowMat('#ffffff', 2.6);
    for (const s of [-1, 1]) {
      const cx = s * W * 0.38, ch = H * 0.88;
      const c = new THREE.Mesh(new THREE.CylinderGeometry(0.75, 0.75, ch, 24), colM); c.position.set(cx, ch / 2, 0.6); root.add(c);
      for (let i = 0; i < 9; i++) { const a = i / 9 * Math.PI - Math.PI / 2 + (s > 0 ? Math.PI : 0) * 0; const h = ch * (0.35 + 0.55 * hash1(i * 3 + (s > 0 ? 50 : 0)));
        const bar = new THREE.Mesh(new THREE.BoxGeometry(0.09, h, 0.09), barM); bar.position.set(cx + Math.sin(a) * 1.25 * -s, h / 2 + 0.2, 0.6 + Math.cos(a) * 1.25); root.add(bar); }
      const pts = []; for (let i = 0; i <= 80; i++) { const t = i / 80; const a = t * TAU * 1.4 + (s > 0 ? Math.PI : 0); pts.push(V3(cx + Math.cos(a) * 1.6, 0.3 + t * ch * 0.85, 0.6 + Math.sin(a) * 1.6)); }
      root.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 160, 0.16, 8), stdMat({ color: b.c1, roughness: 0.2, metalness: 0.3, emissive: b.c1, ei: 0.8, env: 0.6 })));
    }
    const tri = new THREE.Shape([new THREE.Vector2(-W * 0.2, 0), new THREE.Vector2(W * 0.2, 0), new THREE.Vector2(0, H * 0.72)]);
    const tm = new THREE.Mesh(normalizeUV(new THREE.ShapeGeometry(tri)), ledMat({ pattern: 'tunnel', c1: b.c1, c2: b.c3, c3: b.c2, bright: b.bright, dotsX: 80, dotsY: 70, aspect: (W * 0.4) / (H * 0.72) }));
    BD_LEDS.push(tm.material); tm.position.set(0, 0.3, 0); root.add(tm);
    const sp = ledPanel(W * 1.4, H * 1.2, { ...b, pattern: 'sparkle', bright: 0.7, density: 4 }, BD_LEDS); sp.position.set(0, H * 0.55, -1.5); root.add(sp);
  },
  speaker_wall(root, b, W, H) {
    const cab = cabMat('#1c1830'); const cone = stdMat({ color: '#0c0b12', roughness: 0.6, metalness: 0.1, surf: 'fabric', tile: 0.06 });
    const fm = glowMat(b.c1, 2.0); const fm2 = glowMat(b.c2, 2.0);
    const cw = 1.3, chh = 1.6;
    const mk = (x, y, alt) => {
      const g = new THREE.Group(); g.position.set(x, y, 0);
      const bx = new THREE.Mesh(new THREE.BoxGeometry(cw, chh, 0.7), cab); g.add(bx);
      for (const [cy, r] of [[0.38, 0.32], [-0.32, 0.42]]) { const c = new THREE.Mesh(new THREE.CircleGeometry(r, 24), cone); c.position.set(0, cy, 0.36); g.add(c);
        const rim = new THREE.Mesh(new THREE.RingGeometry(r, r + 0.04, 32), glowMat(alt ? b.c2 : b.c1, 1.4)); rim.position.set(0, cy, 0.365); g.add(rim); }
      const t = 0.05; const m = alt ? fm2 : fm;
      for (const [bw, bh, px, py] of [[cw, t, 0, chh / 2], [cw, t, 0, -chh / 2], [t, chh, cw / 2, 0], [t, chh, -cw / 2, 0]]) { const e = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, t), m); e.position.set(px, py, 0.37); g.add(e); }
      root.add(g);
    };
    const cols = Math.max(2, Math.floor((W * 0.32) / cw)); const rows = Math.max(2, Math.floor(H * 0.8 / chh));
    for (const s of [-1, 1]) for (let c = 0; c < cols; c++) for (let r = 0; r < rows; r++) mk(s * (W * 0.2 + c * (cw + 0.08) + cw / 2), chh / 2 + r * (chh + 0.06), (c + r) % 2);
    const p = ledPanel(W * 0.36, H * 0.55, { ...b, pattern: b.pattern === 'triangles' ? 'dots' : b.pattern }, BD_LEDS); p.position.set(0, H * 0.25 + H * 0.275 + 0.6, -0.1); root.add(p);
    { const pw = W * 0.36 + 0.3, ph = H * 0.55 + 0.3, t = 0.07, fm2b = glowMat(b.c2, 2.2); for (const [bw, bh, px, py] of [[pw, t, 0, ph / 2], [pw, t, 0, -ph / 2], [t, ph, pw / 2, 0], [t, ph, -pw / 2, 0]]) root.add(box(bw, bh, t, fm2b, px, py + p.position.y, 0.05)); }
    const wall = new THREE.Mesh(new THREE.PlaneGeometry(W * 1.6, H * 1.4), stdMat({ color: '#1a0f2e', roughness: 0.9, surf: 'velvet', tile: 2 })); wall.position.set(0, H * 0.7, -0.8); root.add(wall);
    const tm = metalMat('#33304a'); root.add(trussMesh(W, 0.4, tm, V3(-W / 2, H * 0.95, 0.3), 'x'));
    for (let i = 0; i < 6; i++) { const l = decoLamp(i % 2 ? b.c1 : '#fff1b8', 1.8); l.position.set(-W * 0.4 + i * W * 0.16, H * 0.95 - 0.4, 0.5); l.rotation.x = 0.6; root.add(l); }
  },
  curtain(root, b, W, H) {
    const mk = (w, h, amp, freq, color) => {
      const g = new THREE.PlaneGeometry(w, h, 180, 1); const p = g.attributes.position;
      for (let i = 0; i < p.count; i++) { const x = p.getX(i); p.setZ(i, Math.sin(x * freq) * amp + Math.sin(x * freq * 2.7) * amp * 0.25); }
      g.computeVertexNormals();
      return new THREE.Mesh(g, stdMat({ color, roughness: 0.85, env: 0.1, side: THREE.DoubleSide, surf: 'velvet' }));
    };
    const c = mk(W * 1.25, H * 1.1, 0.18, 6.5, b.c1); c.position.set(0, H * 0.55, 0); root.add(c);
    const val = mk(W * 1.3, H * 0.16, 0.12, 9, '#' + new THREE.Color(b.c1).multiplyScalar(0.7).getHexString()); val.position.set(0, H * 1.02, 0.5); root.add(val);
    const trim = new THREE.Mesh(new THREE.BoxGeometry(W * 1.3, 0.05, 0.05), glowMat(b.c3, 1.3)); trim.position.set(0, H * 0.94, 0.65); root.add(trim);
    for (const s of [-1, 1]) { const l = mk(W * 0.12, H * 1.1, 0.12, 8, b.c1); l.position.set(s * W * 0.6, H * 0.55, 0.9); root.add(l); }
    for (let i = 0; i < 5; i++) { const d = new THREE.Mesh(spotGeo, spotMat()); d.material.uniforms.uColor.value = col(b.c2); d.material.uniforms.uInt.value = 0.5; d.scale.set(4, 1, 2.2); d.rotation.x = Math.PI / 2; d.position.set(-W * 0.4 + i * W * 0.2, 1.4, 0.3); markFx(d); root.add(d); }
  },
  image(root, b, W, H) {
    const tex = imageTexture(b.image) || textTexture('画像を読み込んでください', { color: '#9fb4ff' });
    const asp = tex.userData.aspect || 1.6;
    let h = H, w = H * asp; if (w > W * 1.4) { w = W * 1.4; h = w / asp; }
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ map: tex, color: col('#ffffff', b.bright), fog: false }));
    m.position.set(0, h / 2 + 0.3, 0); m.name = 'BackdropImage'; root.add(m);
    const t = 0.08, fm = glowMat(b.c1, 2);
    for (const [bw, bh, px, py] of [[w + t, t, 0, h / 2], [w + t, t, 0, -h / 2], [t, h, w / 2, 0], [t, h, -w / 2, 0]]) { const e = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, t), fm); e.position.set(px, py + h / 2 + 0.3, 0.03); root.add(e); }
  },
  none() {},
};
function buildBackdrop() {
  clearGroup(G.backdrop); BD_LEDS.length = 0; BD_ANIMS.length = 0;
  const b = S.backdrop;
  const root = new THREE.Group(); root.name = 'Backdrop_' + b.type;
  root.position.set(0, 0, -S.stage.depth / 2 - 0.9 + b.z); root.scale.setScalar(b.scale);
  (BACKDROPS[b.type] || BACKDROPS.none)(root, b, b.width, b.height);
  fitSurfaceUVs(root);
  G.backdrop.add(root);
}
