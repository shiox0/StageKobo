
/* =====================================================================
   アセット（共通フォーマット＋ビルダー）
   inst = { id, type, name, pos[3], rot[3](度), scale[3], color, color2, p{固有}, mirror, arr{n,dx}, visible }
   build(inst, ctx) は「原点・無回転」で中身を作って { obj, fixtures, monitors, anim, worldFx } を返す
   ===================================================================== */
const ASSET_CATS = ['トラス', '照明', '映像', '音響', '造作・装飾', '特効', '人', 'カスタム'];
const ASSET_DEFS = {};
const RT_ASSETS = new Map();
let REG_DIRTY = true;
const REG = { fixtures: [], monitors: [], anims: [], performers: [] };
const FX_TRIG = { spark: -99, confetti: -99, smoke: -99, strobe: -99 };

const P_R = (k, label, min, max, step = 0.1) => ({ k, label, t: 'range', min, max, step });
const P_SEL = (k, label, opts) => ({ k, label, t: 'select', opts });
const P_CHK = (k, label) => ({ k, label, t: 'check' });
const P_CLR = (k, label) => ({ k, label, t: 'color' });
const P_TXT = (k, label) => ({ k, label, t: 'text' });
const P_IMG = (k, label) => ({ k, label, t: 'image' });
function defAsset(type, d) { d.type = type; ASSET_DEFS[type] = d; }
function box(w, h, d, mat, x = 0, y = 0, z = 0) { const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat); m.position.set(x, y, z); return m; }
function cyl(rt, rb, h, mat, x = 0, y = 0, z = 0, seg = 16) { const m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, seg), mat); m.position.set(x, y, z); return m; }
const UPV = V3(0, 1, 0);
const _v1 = V3(), _v2 = V3(), _v3 = V3(), _m4 = new THREE.Matrix4(), _q = new THREE.Quaternion(), _c = new THREE.Color();
const wrapPI = a => { while (a > Math.PI) a -= TAU; while (a < -Math.PI) a += TAU; return a; };

function makeInst(type, over = {}) {
  const d = ASSET_DEFS[type];
  const base = { id: uid(type.slice(0, 4)), type, name: d.label, pos: [0, 0, 0], rot: [0, 0, 0], scale: [1, 1, 1], color: d.color ?? '#cccccc', color2: d.color2 ?? '#ffffff', p: deepClone(d.p ?? {}), mirror: false, arr: { n: 1, dx: 2 }, visible: true };
  const inst = deepMerge(base, over);
  if (!over.id) inst.id = base.id;
  return inst;
}
function findInst(id) { return S.assets.find(a => a.id === id); }

/* ---------- ビーム計算（床に当たる点を探す） ---------- */
function beamHit(O, D) {
  if (D.y > -0.02) return null;
  const ys = [];
  if (STAGE_INFO.upper) ys.push(STAGE_INFO.upper.top);
  ys.push(STAGE_INFO.H, 0);
  for (const y of ys) {
    if (O.y <= y + 0.01) continue;
    const t = (y - O.y) / D.y; const x = O.x + D.x * t, z = O.z + D.z * t;
    if (y === 0 || Math.abs(floorHeightAt(x, z) - y) < 0.02) return { t, x, y, z };
  }
  return null;
}
/* 灯体共通：ビーム＋床の光だまり */
function makeBeamRig(halfAngleDeg, intensity) {
  const beam = new THREE.Mesh(beamGeometry(), beamMat()); markFx(beam); beam.name = 'Beam';
  const decal = new THREE.Mesh(spotGeo, spotMat()); markFx(decal); decal.name = 'BeamSpot';
  return { beam, decal, half: halfAngleDeg * DEG, inten: intensity };
}
function updateBeamRig(rig, color, dim, F, widthMul = 1, maxLen = 38) {
  const { beam, decal } = rig;
  beam.updateMatrixWorld(true);
  const O = _v1.setFromMatrixPosition(beam.matrixWorld);
  const D = _v2.set(0, 1, 0).transformDirection(beam.matrixWorld);
  const hit = beamHit(O, D);
  const len = hit ? hit.t : maxLen;
  const ws = _v3.setFromMatrixScale(beam.parent.matrixWorld).y || 1;
  const r = Math.tan(rig.half * widthMul) * len;
  beam.scale.set(r / ws, len / ws, r / ws);
  const u = beam.material.uniforms;
  u.uColor.value.copy(color); u.uInt.value = rig.inten * dim; u.uLen.value = len; u.uHaze.value = F.haze; u.uEndFade.value = hit ? 0 : 1; u.uTime.value = F.t;
  beam.visible = dim > 0.003;
  if (hit && dim > 0.003) {
    decal.visible = true;
    decal.position.set(hit.x, hit.y + 0.02, hit.z);
    const el = clamp(1 / Math.max(0.25, Math.abs(D.y)), 1, 4);
    decal.scale.set(r * 2.4, 1, r * 2.4 * el); decal.rotation.y = Math.atan2(D.x, D.z);
    decal.material.uniforms.uColor.value.copy(color); decal.material.uniforms.uInt.value = dim * 0.9 * Math.min(1.5, 0.6 / Math.max(0.08, r));
  } else decal.visible = false;
}
/* ワールド方向 → 灯体のパン/チルト（ミラー・吊り・回転を全部吸収するIK） */
function aimLocal(panNode, dirWorld, out) {
  _m4.copy(panNode.parent.matrixWorld).invert();
  const l = _v3.copy(dirWorld).transformDirection(_m4);
  out.pan = Math.atan2(l.x, l.z); out.tilt = Math.atan2(Math.hypot(l.x, l.z), l.y);
  return out;
}
function sphDir(panDeg, tiltDeg, hang, out) {
  const p = panDeg * DEG, t = tiltDeg * DEG;
  return out.set(Math.sin(t) * Math.sin(p), (hang ? -1 : 1) * Math.cos(t), Math.sin(t) * Math.cos(p));
}

/* =================== トラス =================== */
defAsset('truss_tower', { label: 'トラスタワー', cat: 'トラス', color: '#c9ced8', color2: '#bfe4ff', p: { height: 6, width: 0.4, base: true, dots: 0 },
  params: [P_R('height', '高さ', 1, 20), P_R('width', '断面', 0.2, 1, 0.05), P_CHK('base', 'ベースプレート'), P_R('dots', '光の粒（個）', 0, 30, 1)],
  colors: [['color', '金属色'], ['color2', '光の粒']],
  place: () => [-7, 0, -2],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p; const m = metalMat(inst.color);
    g.add(trussMesh(p.height, p.width, m, V3(0, 0, 0)));
    if (p.base) g.add(box(p.width * 2.2, 0.04, p.width * 2.2, m, 0, 0.02, 0));
    if (p.dots > 0) { const dm = glowMat(inst.color2, 2.4); const dg = new THREE.PlaneGeometry(0.11, 0.11).rotateZ(Math.PI / 4);
      for (let i = 0; i < p.dots; i++) { const d = new THREE.Mesh(dg, dm); d.position.set(0, (i + 0.5) * p.height / p.dots, p.width / 2 + 0.02); g.add(d); } }
    return { obj: g };
  } });
defAsset('truss_beam', { label: 'トラス（横）', cat: 'トラス', color: '#c9ced8', p: { length: 12, width: 0.4 },
  params: [P_R('length', '長さ', 1, 40), P_R('width', '断面', 0.2, 1, 0.05)], colors: [['color', '金属色']],
  place: () => [0, 8, 1],
  build(inst) { const g = new THREE.Group(); g.add(trussMesh(inst.p.length, inst.p.width, metalMat(inst.color), V3(-inst.p.length / 2, 0, 0), 'x')); return { obj: g }; } });
defAsset('truss_gate', { label: 'トラスゲート（門型）', cat: 'トラス', color: '#c9ced8', p: { width: 14, height: 8, tw: 0.4 },
  params: [P_R('width', '幅', 2, 40), P_R('height', '高さ', 2, 20), P_R('tw', '断面', 0.2, 1, 0.05)], colors: [['color', '金属色']],
  place: () => [0, 0, 2],
  build(inst) {
    const g = new THREE.Group(); const { width: W, height: H, tw } = inst.p; const m = metalMat(inst.color);
    for (const s of [-1, 1]) { g.add(trussMesh(H, tw, m, V3(s * W / 2, 0, 0))); g.add(box(tw * 2.2, 0.04, tw * 2.2, m, s * W / 2, 0.02, 0)); }
    g.add(trussMesh(W + tw, tw, m, V3(-W / 2 - tw / 2, H, 0), 'x'));
    return { obj: g };
  } });
defAsset('truss_circle', { label: 'サークルトラス', cat: 'トラス', color: '#c9ced8', p: { radius: 4, width: 0.35, seg: 16, vertical: false },
  params: [P_R('radius', '半径', 1, 15), P_R('width', '断面', 0.2, 1, 0.05), P_R('seg', '分割数', 6, 32, 1), P_CHK('vertical', '縦向き')], colors: [['color', '金属色']],
  place: () => [0, 9, 2],
  build(inst) {
    const g = new THREE.Group(); const { radius: R, width: w } = inst.p; const n = Math.round(inst.p.seg); const m = metalMat(inst.color);
    const inner = new THREE.Group(); g.add(inner);
    const segLen = 2 * R * Math.sin(Math.PI / n);
    for (let i = 0; i < n; i++) {
      const a = i / n * TAU; const t = trussMesh(segLen, w, m, V3(0, 0, 0), 'x');
      const holder = new THREE.Group(); holder.position.set(Math.cos(a) * R, 0, Math.sin(a) * R); holder.rotation.y = -a - Math.PI / 2 - Math.PI / n;
      t.position.set(0, 0, 0); holder.add(t); inner.add(holder);
    }
    if (inst.p.vertical) inner.rotation.x = Math.PI / 2;
    return { obj: g };
  } });

/* =================== 照明 =================== */
const BEAM_TYPES = { beam: [2.2, 1.0], spot: [6, 0.75], wash: [13, 0.38] };
defAsset('moving_head', { label: 'ムービングライト', cat: '照明', color: '#1d1f28', p: { mount: 'floor', beam: 'beam', follow: true, fixed: '#ffffff', power: 1 },
  params: [P_SEL('mount', '設置', [['floor', '床置き（上向き）'], ['hang', '吊り（下向き）']]), P_SEL('beam', 'ビーム', [['beam', 'ビーム（細）'], ['spot', 'スポット（中）'], ['wash', 'ウォッシュ（太）']]),
    P_CHK('follow', 'キューの色に従う'), P_CLR('fixed', '固定色'), P_R('power', '明るさ', 0, 3, 0.05)],
  colors: [['color', '本体色']],
  place: () => [-6, S.stage.height, -S.stage.depth / 2 + 1],
  build(inst) {
    const g = new THREE.Group(); const hang = inst.p.mount === 'hang';
    const flip = new THREE.Group(); flip.name = 'Mount'; if (hang) flip.rotation.z = Math.PI; g.add(flip);
    const bm = paintMat(inst.color);
    flip.add(box(0.44, 0.16, 0.34, bm, 0, 0.08, 0));
    const pan = new THREE.Group(); pan.name = 'Pan'; pan.position.y = 0.17; flip.add(pan);
    pan.add(box(0.36, 0.05, 0.16, bm, 0, 0.02, 0));
    for (const s of [-1, 1]) pan.add(box(0.05, 0.36, 0.14, bm, s * 0.18, 0.2, 0));
    const tilt = new THREE.Group(); tilt.name = 'Tilt'; tilt.position.y = 0.3; pan.add(tilt);
    tilt.add(cyl(0.13, 0.15, 0.34, bm, 0, 0, 0, 20));
    const lens = new THREE.Mesh(new THREE.CircleGeometry(0.105, 20).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: '#ffffff' })); lens.name = 'Lens'; lens.position.y = 0.172; tilt.add(lens);
    const [ang, inten] = BEAM_TYPES[inst.p.beam] || BEAM_TYPES.beam;
    const rig = makeBeamRig(ang, inten); rig.beam.position.y = 0.17; tilt.add(rig.beam);
    const cur = { pan: 0, tilt: 0 }, tgt = { pan: 0, tilt: 0 }; const fixed = col(inst.p.fixed);
    const fx = {
      kind: 'mh', node: g,
      update(F, i, n, xn) {
        const cue = F.cue; let D;
        if (cue.pattern === 'center') {
          _v1.setFromMatrixPosition(tilt.matrixWorld);
          const tp = F.performers.length ? F.performers[i % F.performers.length] : F.focus;
          D = _v2.copy(tp).sub(_v1);
          D.x += Math.sin(F.beat * 0.8 + i) * cue.amp * 0.03; D.normalize();
        } else {
          const pt = (PATTERNS[cue.pattern] || PATTERNS.still)(cue, F.beat, i, n, xn, xn < 0 ? -1 : 1);
          D = sphDir(pt.pan, pt.tilt, hang, _v2);
        }
        aimLocal(pan, D, tgt);
        const k = 1 - Math.exp(-F.dt * 7);
        if (tgt.tilt > 0.03) cur.pan += wrapPI(tgt.pan - cur.pan) * k;
        cur.tilt += (tgt.tilt - cur.tilt) * k;
        pan.rotation.y = cur.pan; tilt.rotation.x = cur.tilt;
        const c = inst.p.follow ? F.colorAt(i, n, xn) : fixed;
        const dim = F.dimAt(i, n) * F.master * inst.p.power;
        lens.material.color.copy(c).multiplyScalar(0.3 + 3 * dim);
        updateBeamRig(rig, c, dim, F, cue.beam);
      },
    };
    return { obj: g, fixtures: [fx], worldFx: [rig.decal] };
  } });
defAsset('par_light', { label: 'パーライト（固定）', cat: '照明', color: '#1d1f28', p: { tilt: 35, angle: 12, follow: true, fixed: '#ffd9a8', power: 0.8 },
  params: [P_R('tilt', '首の角度', -90, 90, 1), P_R('angle', '照射角', 3, 30, 0.5), P_CHK('follow', 'キューの色に従う'), P_CLR('fixed', '固定色'), P_R('power', '明るさ', 0, 3, 0.05)],
  colors: [['color', '本体色']],
  place: () => [-4, 7.6, 3],
  build(inst) {
    const g = new THREE.Group(); const bm = paintMat(inst.color, { roughness: 0.4, metalness: 0.5 });
    g.add(box(0.36, 0.04, 0.06, bm, 0, 0.2, 0));
    const head = new THREE.Group(); head.name = 'Head'; head.rotation.x = Math.PI - inst.p.tilt * DEG; g.add(head);
    head.add(cyl(0.12, 0.12, 0.34, bm, 0, 0, 0, 18));
    const lens = new THREE.Mesh(new THREE.CircleGeometry(0.1, 18).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: '#fff' })); lens.name = 'Lens'; lens.position.y = 0.171; head.add(lens);
    const rig = makeBeamRig(inst.p.angle, 0.45); rig.beam.position.y = 0.17; head.add(rig.beam);
    const fixed = col(inst.p.fixed);
    return { obj: g, worldFx: [rig.decal], fixtures: [{ kind: 'par', node: g, update(F, i, n, xn) {
      const c = inst.p.follow ? F.colorAt(i, n, xn) : fixed; const dim = F.dimAt(i, n) * F.master * inst.p.power;
      lens.material.color.copy(c).multiplyScalar(0.3 + 2.5 * dim); updateBeamRig(rig, c, dim, F, 1);
    } }] };
  } });
defAsset('pin_spot', { label: 'ピンスポット（追従）', cat: '照明', color: '#22242e', p: { target: 'performer', angle: 5, fixed: '#fff4e6', power: 1.0, real: true },
  params: [P_SEL('target', '狙う先', [['performer', '演者（いなければ中央）'], ['center', 'センターサークル']]), P_R('angle', '照射角', 1, 20, 0.5), P_CLR('fixed', '光の色'), P_R('power', '明るさ', 0, 3, 0.05), P_CHK('real', '演者を実際に照らす')],
  colors: [['color', '本体色']],
  place: () => [0, 12, 8],
  build(inst) {
    const g = new THREE.Group(); const bm = paintMat(inst.color, { roughness: 0.4, metalness: 0.5 });
    const yaw = new THREE.Group(); yaw.name = 'Pan'; g.add(yaw); const head = new THREE.Group(); head.name = 'Tilt'; yaw.add(head);
    head.add(cyl(0.16, 0.2, 0.7, bm, 0, -0.1, 0, 18));
    const lens = new THREE.Mesh(new THREE.CircleGeometry(0.15, 18).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: '#fff' })); lens.name = 'Lens'; lens.position.y = 0.251; head.add(lens);
    const rig = makeBeamRig(inst.p.angle, 0.6); rig.beam.position.y = 0.25; head.add(rig.beam);
    let light = null;
    if (inst.p.real) { light = new THREE.SpotLight(inst.p.fixed, 0, 0, inst.p.angle * 1.6 * DEG, 0.5, 0); g.add(light); g.add(light.target); light.userData.fx = true; }
    const cur = { pan: 0, tilt: 0 }, tgt = { pan: 0, tilt: 0 }; const c0 = col(inst.p.fixed);
    return { obj: g, worldFx: [rig.decal], fixtures: [{ kind: 'pin', node: g, update(F, i, n) {
      _v1.setFromMatrixPosition(head.matrixWorld);
      const tp = (inst.p.target === 'performer' && F.performers.length) ? F.performers[i % F.performers.length] : F.center;
      const D = _v2.copy(tp).sub(_v1).normalize();
      aimLocal(yaw, D, tgt);
      const k = 1 - Math.exp(-F.dt * 5);
      if (tgt.tilt > 0.02) cur.pan += wrapPI(tgt.pan - cur.pan) * k; cur.tilt += (tgt.tilt - cur.tilt) * k;
      yaw.rotation.y = cur.pan; head.rotation.x = cur.tilt;
      const on = F.cue.pattern === 'center' || F.cue.pattern === 'still' ? 1 : 0.55;
      const dim = inst.p.power * on * F.master;
      lens.material.color.copy(c0).multiplyScalar(0.5 + 3 * dim);
      updateBeamRig(rig, c0, dim, F, 1, 60);
      if (light) { light.intensity = dim * 2.2; light.position.set(0, 0, 0); light.target.position.copy(g.worldToLocal(_v3.copy(tp))); light.color.copy(c0); }
    } }] };
  } });
defAsset('laser', { label: 'レーザー', cat: '照明', color: '#15161c', p: { count: 8, spread: 50, follow: true, fixed: '#7dff9a', power: 1, tilt: 8 },
  params: [P_R('count', '本数', 1, 24, 1), P_R('spread', '広がり（度）', 5, 140, 1), P_R('tilt', '基本の上向き角', -30, 60, 1), P_CHK('follow', 'キューの色に従う'), P_CLR('fixed', '固定色'), P_R('power', '明るさ', 0, 3, 0.05)],
  colors: [['color', '本体色']],
  place: () => [0, S.stage.height, -S.stage.depth / 2 + 0.6],
  build(inst) {
    const g = new THREE.Group(); const bm = paintMat(inst.color, { roughness: 0.4, metalness: 0.5 });
    g.add(box(0.5, 0.22, 0.4, bm, 0, 0.11, 0));
    const ap = new THREE.Mesh(new THREE.CircleGeometry(0.05, 12), new THREE.MeshBasicMaterial({ color: '#fff' })); ap.name = 'Aperture'; ap.position.set(0, 0.13, 0.201); g.add(ap);
    const n = Math.round(inst.p.count); const beams = [];
    const geo = new THREE.CylinderGeometry(1, 1, 1, 6, 1, true); geo.translate(0, 0.5, 0);
    for (let j = 0; j < n; j++) { const b = new THREE.Mesh(geo, beamMat()); b.position.set(0, 0.13, 0.2); markFx(b); g.add(b); beams.push(b); }
    const fixed = col(inst.p.fixed); const L = 45;
    return { obj: g, fixtures: [{ kind: 'laser', node: g, update(F, i, n2, xn) {
      const mode = F.cue.laser; const c = inst.p.follow ? F.colorAt(i, n2, xn) : fixed;
      const dim = (mode === 'off' ? 0 : 1) * F.master * inst.p.power * (F.cue.dim === 'strobe' ? F.dimAt(i, n2) : 1);
      const ws = _v3.setFromMatrixScale(g.matrixWorld).y || 1;
      const sp = inst.p.spread * DEG; const b = F.beat;
      beams.forEach((bm2, j) => {
        bm2.visible = dim > 0.01; if (!bm2.visible) return;
        const k = n > 1 ? j / (n - 1) - 0.5 : 0; let yaw = 0, pitch = inst.p.tilt * DEG;
        if (mode === 'fan') { yaw = k * sp * (0.55 + 0.45 * Math.sin(b * Math.PI / 4)); pitch += 0.12 * Math.sin(b * Math.PI / 2); }
        else if (mode === 'sweep') { yaw = k * sp * 0.15 + Math.sin(b * Math.PI / 4) * sp * 0.45; }
        else if (mode === 'cross') { yaw = k * sp * 0.4 + (i % 2 ? 1 : -1) * Math.sin(b * Math.PI / 4) * sp * 0.3; pitch += (i % 2 ? 0.1 : -0.05); }
        else if (mode === 'cone') { const a = j / n * TAU + b * Math.PI / 2; yaw = Math.cos(a) * sp * 0.2; pitch += Math.sin(a) * sp * 0.2; }
        const d = _v1.set(Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), Math.cos(yaw) * Math.cos(pitch));
        bm2.quaternion.setFromUnitVectors(UPV, d);
        const r = 0.02 / ws; bm2.scale.set(r, L / ws, r);
        const u = bm2.material.uniforms; u.uColor.value.copy(c); u.uInt.value = 5 * dim; u.uLen.value = 18; u.uHaze.value = Math.max(0.35, F.haze); u.uEndFade.value = 1; u.uTime.value = F.t;
      });
      ap.material.color.copy(c).multiplyScalar(1 + 4 * dim);
    } }] };
  } });
defAsset('led_bar', { label: 'LEDバー', cat: '照明', color: '#ffffff', p: { length: 2.5, vertical: true, seg: 14, mode: 'follow', power: 1 },
  params: [P_R('length', '長さ', 0.3, 12), P_CHK('vertical', '縦向き'), P_R('seg', '分割数', 1, 40, 1), P_SEL('mode', '光り方', [['follow', 'キューに合わせる'], ['chase', '流れる'], ['vu', 'レベルメーター'], ['static', '常時点灯（本体色）']]), P_R('power', '明るさ', 0, 3, 0.05)],
  colors: [['color', '光の色（常時点灯時）']],
  place: () => [-3, S.stage.height, -S.stage.depth / 2 + 0.4],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p; const n = Math.round(p.seg); const L = p.length; const sl = L / n;
    const im = new THREE.InstancedMesh(new THREE.BoxGeometry(0.07, sl * 0.88, 0.07), new THREE.MeshBasicMaterial({ color: '#ffffff' }), n);
    for (let j = 0; j < n; j++) { _m4.makeTranslation(0, (j + 0.5) * sl, 0); im.setMatrixAt(j, _m4); im.setColorAt(j, _c.set(0, 0, 0)); }
    im.name = 'LEDSegments'; const holder = new THREE.Group(); if (!p.vertical) { holder.rotation.z = -Math.PI / 2; holder.position.x = -L / 2; } holder.add(im); g.add(holder);
    g.add(box(0.1, 0.03, 0.1, paintMat('#222228'), 0, 0, 0));
    const base = col(inst.color);
    return { obj: g, fixtures: [{ kind: 'bar', node: g, update(F, i, n2, xn) {
      const c = p.mode === 'static' ? base : F.colorAt(i, n2, xn); const dimAll = F.master * p.power;
      for (let j = 0; j < n; j++) {
        let k = 1;
        if (p.mode === 'chase') k = (Math.floor(F.beat * 4) % n === j || Math.floor(F.beat * 4 + n / 2) % n === j) ? 1.6 : 0.12;
        else if (p.mode === 'vu') { const lv = (0.25 + 0.75 * hash1(i * 3 + Math.floor(F.beat * 2))) * (0.5 + 0.5 * Math.exp(-fract(F.beat) * 3)); k = j / n < lv ? 1 : 0.05; }
        else if (p.mode === 'follow') k = F.dimAt(i, n2) * (0.6 + 0.4 * Math.sin(F.t * 3 + j * 0.5 + i));
        im.setColorAt(j, _c.copy(c).multiplyScalar(2.2 * k * dimAll));
      }
      im.instanceColor.needsUpdate = true;
    } }] };
  } });
defAsset('strobe', { label: 'ストロボ／ブラインダー', cat: '照明', color: '#1d1f28', p: { cells: 4, accent: true, power: 1 },
  params: [P_R('cells', '灯数', 1, 8, 1), P_CHK('accent', '小節頭でフラッシュ'), P_R('power', '明るさ', 0, 3, 0.05)], colors: [['color', '本体色']],
  place: () => [-8, S.stage.height, S.stage.depth / 2 - 0.5],
  build(inst) {
    const g = new THREE.Group(); const n = Math.round(inst.p.cells); const w = n * 0.3;
    g.add(box(w + 0.1, 0.36, 0.2, paintMat(inst.color, { roughness: 0.4, metalness: 0.4 }), 0, 0.3, 0));
    const lm = new THREE.MeshBasicMaterial({ color: '#000' });
    for (let j = 0; j < n; j++) { const l = new THREE.Mesh(new THREE.CircleGeometry(0.12, 16), lm); l.name = 'StrobeCell'; l.position.set(-w / 2 + 0.15 + j * 0.3, 0.3, 0.101); g.add(l); }
    return { obj: g, fixtures: [{ kind: 'strobe', node: g, update(F) {
      let k = 0;
      if (F.strobeTrig || F.cue.dim === 'strobe') k = fract(F.t * 13) < 0.35 ? 6 : 0;
      else if (inst.p.accent) k = F.accent * 4;
      lm.color.set('#fff4e8').multiplyScalar(k * inst.p.power * F.master + 0.02);
    } }] };
  } });
defAsset('mirror_ball', { label: 'ミラーボール', cat: '照明', color: '#d8dce6', p: { radius: 0.5, dots: 500, spread: 18, speed: 0.15, follow: false },
  params: [P_R('radius', '半径', 0.2, 2, 0.05), P_R('dots', '光の粒の数', 0, 1500, 10), P_R('spread', '光の粒の距離', 5, 50, 1), P_R('speed', '回転速度', 0, 1, 0.01), P_CHK('follow', '粒をキューの色に')],
  colors: [['color', 'ボールの色']],
  place: () => [0, 10, 3],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p;
    g.add(cyl(0.01, 0.01, 2, metalMat('#55585f'), 0, 1 + p.radius, 0, 4));
    // 小さな鏡のタイルを貼った球（タイルごとに少しずつ違う向き → 光の粒がきらめく）
    const ball = new THREE.Mesh(new THREE.SphereGeometry(p.radius, 64, 32), stdMat({ color: inst.color, metalness: 1, roughness: 0.12, env: 1.6, surf: 'mirrortile', tile: Math.max(0.03, p.radius * 0.1) }));
    ball.name = 'Ball'; g.add(ball);
    let pts = null;
    if (p.dots > 0) {
      const n = Math.round(p.dots), pos = new Float32Array(n * 3); const R = mulberry32(13);
      for (let i = 0; i < n; i++) { const u = R() * 2 - 1, a = R() * TAU, s = Math.sqrt(1 - u * u); const r = p.spread * (0.6 + R() * 0.4); pos[i * 3] = Math.cos(a) * s * r; pos[i * 3 + 1] = -Math.abs(u) * r * 0.7 + r * 0.15; pos[i * 3 + 2] = Math.sin(a) * s * r; }
      const gg = new THREE.BufferGeometry(); gg.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      pts = new THREE.Points(gg, new THREE.PointsMaterial({ color: col('#ffffff', 2), size: 0.22, map: texSoft(), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
      markFx(pts); g.add(pts);
    }
    return { obj: g, anim(F) { ball.rotation.y += F.dt * p.speed * TAU * 0.25; if (pts) { pts.rotation.y = ball.rotation.y; pts.material.color.copy(p.follow ? F.colors[Math.floor(F.beat) % 3] : _c.set(1, 1, 1)).multiplyScalar(1.6 * F.master); } } };
  } });

/* =================== 映像 =================== */
const MON_SOURCES = [['cam1', 'ステージカメラ1（ライブ映像）'], ['cam2', 'ステージカメラ2（ライブ映像）'], ['vj', 'VJリモコンの映像'], ['pattern', 'VJ映像パターン（このモニターだけ）'], ['image', '画像'], ['text', '文字'], ['off', '消灯']];
function monitorTextTex(text, aspect, c1, c2) {
  const key = 'mt|' + text + '|' + aspect.toFixed(2) + c1 + c2;
  return canvasTex(key, 512, Math.max(64, Math.round(512 / aspect)), (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, w, h); gr.addColorStop(0, c1); gr.addColorStop(1, c2); g.fillStyle = gr; g.fillRect(0, 0, w, h);
    let fs = h * 0.5; g.font = `bold ${fs}px sans-serif`; const tw = g.measureText(text).width; if (tw > w * 0.9) fs *= w * 0.9 / tw;
    g.font = `bold ${fs}px "Hiragino Maru Gothic ProN","Yu Gothic UI",sans-serif`; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.shadowColor = 'rgba(255,255,255,0.9)'; g.shadowBlur = fs * 0.15; g.fillStyle = '#ffffff'; g.fillText(text, w / 2, h / 2);
  });
}
defAsset('led_monitor', { label: 'LEDモニター（ドット）', cat: '映像', color: '#1c1f2a', color2: '#8fd3ff',
  p: { w: 9, h: 5, density: 12, gap: 0.35, round: true, bright: 1.0, src: 'cam1', group: 'auto', pattern: 'triangles', image: null, text: 'LIVE!', c1: '#8fd3ff', c2: '#ff8ad8', c3: '#ffffff', frame: 'bezel', mount: 'stand', lift: 1.5, flip: false, edge: true },
  params: [P_R('w', '幅(m)', 1, 30), P_R('h', '高さ(m)', 0.5, 20), P_SEL('src', '映すもの', MON_SOURCES),
    P_SEL('group', 'リモコンの区分（ライブ映像／VJ映像の切り替え）', [['auto', '自動（左右ミラーはサブ）'], ['main', 'メイン'], ['sub', 'サブ']]), P_SEL('pattern', 'VJパターン', PATTERN_LIST), P_IMG('image', '画像'), P_TXT('text', '文字'),
    P_CLR('c1', 'パターン色1'), P_CLR('c2', 'パターン色2'), P_CLR('c3', 'パターン色3'),
    P_R('density', 'ドット密度(個/m)', 2, 40, 1), P_R('gap', 'ドットの隙間', 0, 0.8, 0.01), P_CHK('round', '丸ドット'), P_R('bright', '明るさ', 0, 4, 0.05),
    P_SEL('frame', 'フレーム', [['bezel', 'ベゼル'], ['truss', 'トラス枠'], ['none', 'なし']]), P_SEL('mount', '設置', [['stand', '脚で自立'], ['hang', '吊り'], ['wall', '壁付け（脚なし）']]), P_R('lift', '下端の高さ', 0, 15), P_CHK('edge', 'ふちを光らせる'), P_CHK('flip', '左右反転')],
  colors: [['color', 'フレーム色'], ['color2', 'ふちの光']],
  place: () => [0, 0, -S.stage.depth / 2 - 0.2],
  build(inst, ctx) {
    const g = new THREE.Group(); const p = inst.p; const W = p.w, H = p.h, y0 = p.lift;
    const m = ledMat({ pattern: p.pattern, c1: p.c1, c2: p.c2, c3: p.c3, bright: p.bright, dotsX: Math.max(8, Math.round(W * p.density)), dotsY: Math.max(8, Math.round(H * p.density)), aspect: W / H, gap: p.gap, round: p.round });
    m.uniforms.uFlip.value = (p.flip !== ctx.mirrored) ? 1 : 0;
    if (p.src === 'image') { const t = imageTexture(p.image); m.uniforms.uTex.value = t || BLACK_TEX; m.uniforms.uSrc.value = t ? 0 : 2; }
    if (p.src === 'text') { m.uniforms.uTex.value = monitorTextTex(p.text || ' ', W / H, p.c1, p.c2); m.uniforms.uSrc.value = 0; }
    if (p.src === 'off') m.uniforms.uSrc.value = 2;
    // 画像：まん中をモニターの縦横比で切り抜く（画像は伸び縮みしない。文字はモニターの縦横比で作るのでそのまま）
    // 画像・文字：ドット1個ぶんの平均色を取る（細かい模様がチラつかない）
    const tx = m.uniforms.uTex.value;
    if (p.src === 'image' && tx && tx.image && tx.image.width) coverRect(W / H, tx.image.width / tx.image.height, m.uniforms.uTexRect.value);
    if ((p.src === 'image' || p.src === 'text') && tx && tx.image && tx.image.width) m.uniforms.uTexLod.value = Math.max(0, Math.log2(tx.image.width * m.uniforms.uTexRect.value.z / m.uniforms.uDots.value.x) - 0.5);
    const scr = new THREE.Mesh(new THREE.PlaneGeometry(W, H), m); scr.position.set(0, y0 + H / 2, 0.08); scr.name = 'LEDScreen'; g.add(scr);
    const fm = paintMat(inst.color, { roughness: 0.5, metalness: 0.5 });
    if (p.frame === 'bezel') g.add(box(W + 0.24, H + 0.24, 0.14, fm, 0, y0 + H / 2, 0));
    if (p.frame === 'truss') {
      const tm = metalMat('#c9ced8');
      g.add(box(W + 0.1, H + 0.1, 0.1, fm, 0, y0 + H / 2, 0));
      g.add(trussMesh(W + 0.8, 0.35, tm, V3(-W / 2 - 0.4, y0 + H + 0.25, 0), 'x'));
      for (const s of [-1, 1]) g.add(trussMesh(y0 + H + 0.42, 0.35, tm, V3(s * (W / 2 + 0.22), 0, 0)));
    }
    if (p.edge) {
      const em = glowMat(inst.color2, 2.2); const t = 0.05;
      for (const [bw, bh, px, py] of [[W + 0.3, t, 0, H / 2 + 0.14], [W + 0.3, t, 0, -H / 2 - 0.14], [t, H + 0.3, W / 2 + 0.14, 0], [t, H + 0.3, -W / 2 - 0.14, 0]]) g.add(box(bw, bh, t, em, px, py + y0 + H / 2, 0.1));
    }
    if (p.mount === 'stand' && y0 > 0.05 && p.frame !== 'truss') for (const s of [-1, 1]) { g.add(box(0.14, y0, 0.14, fm, s * W * 0.35, y0 / 2, 0)); g.add(box(0.5, 0.05, 0.7, fm, s * W * 0.35, 0.025, 0)); }
    if (p.mount === 'hang') for (const s of [-1, 1]) g.add(cyl(0.012, 0.012, 4, metalMat('#7a7d86'), s * W * 0.4, y0 + H + 2, 0, 4));
    return { obj: g, monitors: [{ mat: m, inst }] };
  } });
defAsset('text_panel', { label: '文字・ロゴ（ネオン）', cat: '映像', color: '#ff8fd0', color2: '#ffffff', p: { text: 'IDOL LIVE', height: 1.2, style: 'neon', glow: 2, anim: 'pulse' },
  params: [P_TXT('text', '文字'), P_R('height', '文字の高さ', 0.2, 6, 0.05), P_SEL('style', 'スタイル', [['neon', 'ネオン'], ['solid', 'べた塗り']]), P_R('glow', '明るさ', 0, 5, 0.05), P_SEL('anim', '動き', [['none', 'なし'], ['pulse', '拍で脈打つ'], ['blink', '点滅'], ['rainbow', '虹色']])],
  colors: [['color', '文字の色']],
  place: () => [0, 9, -S.stage.depth / 2 + 0.2],
  build(inst, ctx) {
    const g = new THREE.Group(); const p = inst.p;
    const tex = textTexture(p.text || ' ', { color: '#ffffff', style: p.style });
    const h = p.height * 2.2, w = h * tex.userData.aspect;
    const mat = new THREE.MeshBasicMaterial({ map: tex, color: col(inst.color, p.glow), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending });
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), mat); m.position.y = h / 2; if (ctx.mirrored) m.scale.x = -1; m.name = 'TextPanel'; g.add(m);
    const base = col(inst.color, p.glow);
    return { obj: g, anim(F) {
      let k = 1;
      if (p.anim === 'pulse') k = 0.55 + 0.45 * Math.exp(-fract(F.beat) * 3) + 0.2;
      else if (p.anim === 'blink') k = Math.floor(F.beat * 2) % 2 ? 1 : 0.15;
      if (p.anim === 'rainbow') mat.color.setHSL(fract(F.t * 0.1), 0.8, 0.6, THREE.SRGBColorSpace).multiplyScalar(p.glow);
      else mat.color.copy(base).multiplyScalar(k);
    } };
  } });

/* =================== 音響 =================== */
defAsset('line_array', { label: 'ラインアレイ（吊りスピーカー）', cat: '音響', color: '#18181e', p: { boxes: 8, curve: 3 },
  params: [P_R('boxes', '段数', 2, 16, 1), P_R('curve', '反り（度/段）', 0, 8, 0.1)], colors: [['color', '本体色']],
  place: () => [-10, 9, 2],
  build(inst) {
    const g = new THREE.Group(); const m = cabMat(inst.color); const gm = stdMat({ color: '#3a3b42', roughness: 0.6, metalness: 0.6, env: 0.4, surf: 'grille' });
    g.add(box(1.4, 0.08, 0.8, paintMat('#202026'), 0, 0.1, 0)); g.add(cyl(0.015, 0.015, 3, metalMat('#7a7d86'), 0, 1.6, 0, 4));
    let y = 0, a = 0; const holder = new THREE.Group(); g.add(holder);
    for (let i = 0; i < Math.round(inst.p.boxes); i++) {
      const b = new THREE.Group(); b.position.set(0, y - 0.18, 0); b.rotation.x = a;
      b.add(box(1.3, 0.34, 0.75, m)); const f = new THREE.Mesh(new THREE.PlaneGeometry(1.25, 0.3), gm); f.position.z = 0.376; b.add(f);
      holder.add(b); y -= 0.36 * Math.cos(a); a += inst.p.curve * DEG;
    }
    return { obj: g };
  } });
defAsset('speaker_stack', { label: 'スピーカースタック', cat: '音響', color: '#ffd23f', color2: '#ffffff', p: { height: 2.4, woofers: 3, width: 0.9 },
  params: [P_R('height', '高さ', 0.6, 5), P_R('width', '幅', 0.4, 2), P_R('woofers', 'ウーファー数', 1, 6, 1)], colors: [['color', '本体色'], ['color2', '縁取り']],
  place: () => [-6, S.stage.height, S.stage.depth / 2 - 1],
  build(inst) {
    const g = new THREE.Group(); const { height: H, width: W } = inst.p; const n = Math.round(inst.p.woofers);
    g.add(box(W, H, 0.75, cabMat(inst.color, { roughness: 0.6, env: 0.4 }), 0, H / 2, 0));
    const cm = stdMat({ color: '#2a2a30', roughness: 0.55, metalness: 0.1, surf: 'fabric', tile: 0.06 }); const rm = paintMat(inst.color2, { roughness: 0.3, metalness: 0.75, env: 0.7 });
    const r = Math.min(W * 0.38, H / n * 0.4);
    for (let i = 0; i < n; i++) {
      const y = H * (i + 0.5) / n; const c = new THREE.Mesh(new THREE.CylinderGeometry(r, r * 0.4, 0.12, 24), cm); c.rotation.x = Math.PI / 2; c.position.set(0, y, 0.33); g.add(c);
      const rim = new THREE.Mesh(new THREE.TorusGeometry(r, 0.035, 8, 32), rm); rim.position.set(0, y, 0.38); g.add(rim);
      const dust = new THREE.Mesh(new THREE.SphereGeometry(r * 0.25, 12, 8), cm); dust.position.set(0, y, 0.36); dust.scale.z = 0.5; g.add(dust);
    }
    return { obj: g };
  } });
defAsset('wedge', { label: '足元モニター（ころがし）', cat: '音響', color: '#1a1a20', p: {}, params: [], colors: [['color', '本体色']],
  place: () => [2, S.stage.height, S.stage.depth / 2 - 0.5],
  build(inst) {
    const g = new THREE.Group(); const sh = new THREE.Shape([new THREE.Vector2(0, 0), new THREE.Vector2(0.55, 0), new THREE.Vector2(0.55, 0.12), new THREE.Vector2(0.1, 0.38), new THREE.Vector2(0, 0.38)]);
    const geo = new THREE.ExtrudeGeometry(sh, { depth: 0.7, bevelEnabled: false }); geo.translate(-0.275, 0, -0.35); geo.rotateY(Math.PI / 2);
    g.add(new THREE.Mesh(geo, cabMat(inst.color))); return { obj: g };
  } });
defAsset('dj_booth', { label: 'DJブース', cat: '音響', color: '#241a3a', color2: '#ff8fd0', p: { width: 2.4 },
  params: [P_R('width', '幅', 1.2, 5)], colors: [['color', '本体色'], ['color2', '前面の光']],
  place: () => [0, S.stage.height, -1],
  build(inst) {
    const g = new THREE.Group(); const W = inst.p.width; const m = paintMat(inst.color, { roughness: 0.5, metalness: 0.15 });
    g.add(box(W, 1.0, 0.8, m, 0, 0.5, 0));
    const pm = ledMat({ pattern: 'dots', c1: inst.color2, c2: '#8fd3ff', c3: '#ffffff', dotsX: Math.round(W * 12), dotsY: 10, aspect: W / 0.8, bright: 1.4 });
    const pnl = new THREE.Mesh(new THREE.PlaneGeometry(W - 0.1, 0.8), pm); pnl.position.set(0, 0.5, 0.401); g.add(pnl);
    for (const s of [-1, 1]) { const tt = cyl(0.22, 0.22, 0.03, paintMat('#111114', { roughness: 0.3 }), s * W * 0.28, 1.02, 0, 32); g.add(tt); }
    g.add(box(0.5, 0.05, 0.35, paintMat('#222228'), 0, 1.03, 0));
    return { obj: g, monitors: [{ mat: pm, inst: { p: { src: 'pattern' } } }] };
  } });
defAsset('mic_stand', { label: 'マイクスタンド', cat: '音響', color: '#202028', color2: '#d8dce6', p: { height: 1.45, deco: true },
  params: [P_R('height', '高さ', 0.8, 2), P_CHK('deco', 'リボン飾り')], colors: [['color', 'スタンド'], ['color2', 'マイク']],
  place: () => [0, S.stage.height, 1.6],
  build(inst) {
    const g = new THREE.Group(); const m = paintMat(inst.color, { metalness: 0.7, roughness: 0.3, env: 0.6 }); const H = inst.p.height;
    g.add(cyl(0.16, 0.18, 0.03, m, 0, 0.015, 0, 24)); g.add(cyl(0.012, 0.012, H, m, 0, H / 2, 0, 8));
    const mic = new THREE.Group(); mic.position.y = H; mic.rotation.x = 0.5; g.add(mic);
    mic.add(cyl(0.022, 0.016, 0.18, stdMat({ color: inst.color2, metalness: 0.6, roughness: 0.3, env: 0.8, surf: 'alu', tile: 0.1 }), 0, 0.09, 0, 16));
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.035, 20, 14), stdMat({ color: '#9aa0aa', metalness: 0.8, roughness: 0.4, env: 0.8, surf: 'grille', tile: 0.012 })); head.position.y = 0.19; mic.add(head);
    if (inst.p.deco) { const r = new THREE.Mesh(new THREE.TorusGeometry(0.05, 0.012, 6, 16), stdMat({ color: '#ff8fd0', roughness: 0.5 })); r.position.set(0.04, H - 0.05, 0); r.rotation.y = 1.2; g.add(r); }
    return { obj: g };
  } });

/* =================== 造作・装飾 =================== */
defAsset('riser', { label: '台（ライザー）', cat: '造作・装飾', color: '#1b1f3d', color2: '#7fe8ff', p: { w: 3, d: 2, h: 0.6, edge: true, round: false },
  params: [P_R('w', '幅', 0.5, 20), P_R('d', '奥行', 0.5, 12), P_R('h', '高さ', 0.1, 5, 0.05), P_CHK('round', '円形'), P_CHK('edge', 'エッジライト')], colors: [['color', '本体色'], ['color2', 'エッジ色']],
  place: () => [0, S.stage.height, -1],
  build(inst) {
    const g = new THREE.Group(); const { w, d, h } = inst.p;
    // 上面＝すべり止めのデッキ、側面＝ひだのある幕（本物の舞台の台と同じ作り）
    const deck = stdMat({ color: inst.color, roughness: 0.55, env: 0.35, surf: 'deck' });
    const skirt = stdMat({ color: '#' + col(inst.color).multiplyScalar(0.75).getHexString(), roughness: 0.95, env: 0.12, surf: 'velour' });
    if (inst.p.round) {
      g.add(new THREE.Mesh(new THREE.CylinderGeometry(w / 2, w / 2, h, 64), [skirt, deck, deck]).translateY(h / 2));
      if (inst.p.edge) { const t = new THREE.Mesh(new THREE.TorusGeometry(w / 2, 0.03, 6, 96), glowMat(inst.color2, 2.2)); t.rotation.x = Math.PI / 2; t.position.y = h + 0.01; g.add(t); }
    } else {
      g.add(new THREE.Mesh(new THREE.BoxGeometry(w, h, d), [skirt, skirt, deck, deck, skirt, skirt]).translateY(h / 2));
      if (inst.p.edge) { const gg = ribbonGeo(densifySegs(rectPts(-w / 2, -d / 2, w / 2, d / 2), true, 1), h + 0.02, 0.05); g.add(new THREE.Mesh(gg, glowMat(inst.color2, 2.2))); }
    }
    return { obj: g };
  } });
defAsset('stairs', { label: '階段', cat: '造作・装飾', color: '#20264f', color2: '#7fe8ff', p: { width: 3, count: 4, height: 1.2, tread: 0.4, led: true, led2: '#ff8ad8' },
  params: [P_R('width', '幅', 0.6, 20), P_R('count', '段数', 1, 12, 1), P_R('height', '高さ', 0.2, 5, 0.05), P_R('tread', '踏み面', 0.2, 1, 0.02), P_CHK('led', 'LEDドット'), P_CLR('led2', 'LED色2')],
  colors: [['color', '本体色'], ['color2', 'LED色1']],
  place: () => [5, 0, S.stage.depth / 2],
  build(inst) { const p = inst.p; const g = new THREE.Group(); g.add(buildStairs({ width: p.width, count: p.count, height: p.height, tread: p.tread, color: inst.color, led: p.led, led1: inst.color2, led2: p.led2 })); return { obj: g }; } });
defAsset('neon', { label: 'ネオン装飾', cat: '造作・装飾', color: '#7fe8ff', p: { shape: 'triangle', size: 1.5, thick: 0.045, fill: 0, glow: 2.2, anim: 'none' },
  params: [P_SEL('shape', '形', NEON_SHAPES), P_R('size', '大きさ', 0.2, 10, 0.05), P_R('thick', '線の太さ', 0.01, 0.2, 0.005), P_R('fill', '塗り', 0, 1, 0.05), P_R('glow', '明るさ', 0, 5, 0.05),
    P_SEL('anim', '動き', [['none', 'なし'], ['pulse', '拍で脈打つ'], ['spin', '回転'], ['float', 'ふわふわ'], ['blink', '点滅'], ['rainbow', '虹色']])],
  colors: [['color', 'ネオン色']],
  place: () => [-5, 6, -S.stage.depth / 2 + 0.2],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p; const n = neonShape(p.shape, p.size, p.thick, inst.color, p.glow, p.fill); g.add(n);
    const mats = []; n.traverse(o => { if (o.material) { o.material.userData.base = o.material.color.clone(); mats.push(o.material); } });
    return { obj: g, anim(F) {
      let k = 1;
      if (p.anim === 'pulse') k = 0.45 + 0.75 * Math.exp(-fract(F.beat) * 3);
      if (p.anim === 'blink') k = Math.floor(F.beat * 2) % 2 ? 1 : 0.1;
      if (p.anim === 'spin') n.rotation.z += F.dt * 0.6;
      if (p.anim === 'float') n.position.y = Math.sin(F.t * 1.2 + inst.pos[0]) * 0.15;
      for (const m of mats) {
        const b0 = m.userData.base;
        if (p.anim === 'rainbow') m.color.setHSL(fract(F.t * 0.12 + inst.pos[0] * 0.05), 0.85, 0.6, THREE.SRGBColorSpace).multiplyScalar(Math.max(b0.r, b0.g, b0.b));
        else m.color.copy(b0).multiplyScalar(k);
      }
    } };
  } });
defAsset('flower_stand', { label: 'フラワースタンド（フラスタ）', cat: '造作・装飾', color: '#ff9fd0', color2: '#ffffff', p: { size: 1, accent: '#bfe4ff', text: '祝 ご出演', board: '#fff7fb', balloons: true },
  params: [P_R('size', '大きさ', 0.5, 2.5, 0.05), P_CLR('accent', 'お花の色3'), P_TXT('text', '立て札の文字'), P_CLR('board', '台紙の色'), P_CHK('balloons', 'バルーン飾り')],
  colors: [['color', 'お花の色1'], ['color2', 'お花の色2']],
  place: () => [-9, 0, S.stage.depth / 2 + 3],
  build(inst, ctx) {
    const g = new THREE.Group(); const p = inst.p; const s = p.size; const lm = metalMat('#e8e2d8');
    for (const [x, z] of [[-0.3, 0.15], [0.3, 0.15], [0, -0.25]]) { const l = cyl(0.02, 0.02, 1.45 * s, lm, x * s, 0.72 * s, z * s, 6); l.rotation.x = z * 0.3; g.add(l); }
    const board = new THREE.Mesh(new THREE.CircleGeometry(0.8 * s, 48), stdMat({ color: p.board, roughness: 0.85, surf: 'fabric', tile: 0.15 })); board.position.set(0, 2.0 * s, -0.1); g.add(board);
    // 葉っぱ（ふち）
    const leaves = 28; const lf = new THREE.InstancedMesh(new THREE.SphereGeometry(0.1 * s, 8, 6), stdMat({ color: '#5f9a5a', roughness: 0.7 }), leaves);
    for (let i = 0; i < leaves; i++) { const a = i / leaves * TAU; _m4.compose(_v1.set(Math.cos(a) * 0.74 * s, 2.0 * s + Math.sin(a) * 0.74 * s, -0.04), _q.setFromEuler(new THREE.Euler(0, 0, a)), _v2.set(1.8, 0.7, 0.3)); lf.setMatrixAt(i, _m4); }
    lf.userData.exportGeo = new THREE.SphereGeometry(0.1 * s, 6, 4);   // Unity 書き出しは軽い形で
    g.add(lf);
    // お花（大小2種類）
    const R = mulberry32(31); const cs = [col(p.accent), col(inst.color), col(inst.color2)];
    const big = new THREE.InstancedMesh(bloomGeometry(), stdMat({ color: '#ffffff', roughness: 0.65, env: 0.3 }), 44);
    for (let i = 0; i < 44; i++) { const a = R() * TAU, r = Math.sqrt(R()) * 0.64 * s; const k = (1.4 + R() * 0.9) * s; _m4.compose(_v1.set(Math.cos(a) * r, 2.0 * s + Math.sin(a) * r, 0.06 + R() * 0.08), _q.setFromEuler(new THREE.Euler((R() - 0.5) * 0.6, (R() - 0.5) * 0.6, R() * TAU)), _v2.set(k, k, k)); big.setMatrixAt(i, _m4); big.setColorAt(i, cs[i % 3]); }
    const small = new THREE.InstancedMesh(new THREE.SphereGeometry(0.035 * s, 8, 6), stdMat({ color: '#ffffff', roughness: 0.6 }), 90);
    for (let i = 0; i < 90; i++) { const a = R() * TAU, r = Math.sqrt(R()) * 0.74 * s; _m4.makeTranslation(Math.cos(a) * r, 2.0 * s + Math.sin(a) * r, 0.14 + R() * 0.07); small.setMatrixAt(i, _m4); small.setColorAt(i, i % 3 ? col('#ffffff') : cs[(i + 1) % 3]); }
    big.userData.exportGeo = bloomGeometryLite().clone(); small.userData.exportGeo = new THREE.SphereGeometry(0.035 * s, 6, 4);
    g.add(big, small);
    // リボン（結び目＋垂れ）
    const rm = stdMat({ color: inst.color, roughness: 0.4, env: 0.5, side: THREE.DoubleSide, surf: 'satin', tile: 0.15 });
    for (const sd of [-1, 1]) { const lp = new THREE.Mesh(new THREE.TorusGeometry(0.11 * s, 0.035 * s, 8, 16), rm); lp.scale.set(1.4, 0.9, 0.6); lp.position.set(sd * 0.14 * s, 1.3 * s, 0.14); g.add(lp);
      const tl = new THREE.Mesh(new THREE.PlaneGeometry(0.07 * s, 0.36 * s), rm); tl.position.set(sd * 0.12 * s, 1.1 * s, 0.15); tl.rotation.z = sd * 0.45; g.add(tl); }
    const kn = new THREE.Mesh(new THREE.SphereGeometry(0.05 * s, 10, 8), rm); kn.position.set(0, 1.3 * s, 0.16); g.add(kn);
    // 立て札
    if (p.text) {
      const tex = bannerTexture(p.text, { bg: '#ffffff', fg: '#3a2a4a', border: inst.color, vertical: false, w: 512, h: 160 });
      const tag = new THREE.Mesh(new THREE.PlaneGeometry(0.9 * s, 0.28 * s), new THREE.MeshStandardMaterial({ map: tex, roughness: 0.8 }));
      tag.material.envMapIntensity = 0.3; tag.position.set(0, 0.86 * s, 0.17); if (ctx.mirrored) tag.scale.x = -1; g.add(tag);
    }
    if (p.balloons) {
      const bm = [stdMat({ color: inst.color, roughness: 0.2, metalness: 0.1, env: 0.7 }), stdMat({ color: p.accent, roughness: 0.2, metalness: 0.1, env: 0.7 })];
      [[-0.55, 2.75, 0], [0.58, 2.8, 1], [0.05, 2.95, 0]].forEach(([x, y, k]) => { const b = new THREE.Mesh(new THREE.SphereGeometry(0.17 * s, 16, 12), bm[k]); b.scale.y = 1.15; b.position.set(x * s, y * s, 0); g.add(b);
        g.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.LineCurve3(V3(x * s, (y - 0.19) * s, 0), V3(x * 0.6 * s, 2.55 * s, 0)), 1, 0.004, 4), lm)); });
    }
    return { obj: g };
  } });
/* お花1輪（花びら6枚＋芯）。フラスタ用 */
let _bloomGeo = null;
function bloomGeometry() {
  if (_bloomGeo) return _bloomGeo;
  const parts = [];
  for (let i = 0; i < 6; i++) { const a = i / 6 * TAU; const pt = new THREE.SphereGeometry(0.05, 10, 6); pt.scale(1, 0.55, 0.32); pt.rotateZ(a); pt.translate(Math.cos(a) * 0.05, Math.sin(a) * 0.05, 0); parts.push(pt.toNonIndexed()); }
  const c = new THREE.SphereGeometry(0.026, 8, 6); c.translate(0, 0, 0.012); parts.push(c.toNonIndexed());
  _bloomGeo = mergeGeometries(parts); _bloomGeo.computeVertexNormals(); _bloomGeo.userData.shared = true;
  return _bloomGeo;
}
// Unity 書き出し用の軽いお花（ブラウザでは1つの形を使い回すので細かくても軽いが、書き出すと全部が別の頂点になるため）
let _bloomLite = null;
function bloomGeometryLite() {
  if (_bloomLite) return _bloomLite;
  const parts = [];
  for (let i = 0; i < 6; i++) { const a = i / 6 * TAU; const pt = new THREE.SphereGeometry(0.05, 6, 3); pt.scale(1, 0.55, 0.32); pt.rotateZ(a); pt.translate(Math.cos(a) * 0.05, Math.sin(a) * 0.05, 0); parts.push(pt); }
  const c = new THREE.SphereGeometry(0.026, 5, 3); c.translate(0, 0, 0.012); parts.push(c);
  _bloomLite = mergeGeometries(parts); _bloomLite.userData.shared = true;
  return _bloomLite;
}
/* 文字入りの布・札のテクスチャ（横書き / 縦書き） */
function bannerTexture(text, o) {
  const key = 'banner|' + text + '|' + JSON.stringify(o);
  return canvasTex(key, o.w || 512, o.h || 512, (g, w, h) => {
    g.fillStyle = o.bg; g.fillRect(0, 0, w, h);
    if (o.border) { g.strokeStyle = o.border; g.lineWidth = Math.min(w, h) * 0.06; g.strokeRect(0, 0, w, h); g.lineWidth = Math.min(w, h) * 0.012; g.strokeRect(w * 0.06, h * 0.06 * (w > h ? 2 : 1) / 2 + h * 0.02, w * 0.88, h - h * 0.08 - h * 0.04); }
    g.fillStyle = o.fg; g.textAlign = 'center'; g.textBaseline = 'middle';
    const font = (sz) => `bold ${sz}px "Hiragino Maru Gothic ProN","Yu Gothic UI","Meiryo",sans-serif`;
    const chars = [...(text || ' ')];
    if (o.vertical) {
      let sz = Math.min(w * 0.62, (h * 0.84) / chars.length); g.font = font(sz);
      chars.forEach((c, i) => g.fillText(c, w / 2, h * 0.08 + sz * (i + 0.5)));
    } else {
      let sz = h * 0.56; g.font = font(sz); const tw = g.measureText(text).width; if (tw > w * 0.86) { sz *= w * 0.86 / tw; g.font = font(sz); }
      g.fillText(text, w / 2, h / 2 + sz * 0.04);
    }
  });
}
defAsset('balloon', { label: 'バルーン', cat: '造作・装飾', color: '#ff9fd0', color2: '#9fdcff', p: { count: 5, shape: 'round', size: 0.45, height: 2.6, accent: '#fff3a8', glow: 0, sway: 1 },
  params: [P_R('count', '個数', 1, 12, 1), P_SEL('shape', '形', [['round', 'まる'], ['heart', 'ハート'], ['star', '星（ホイル）'], ['mix', 'ミックス']]), P_R('size', '大きさ', 0.15, 1.5, 0.01),
    P_R('height', '高さ', 0.5, 8, 0.05), P_CLR('accent', '色3'), P_R('glow', '光る（LEDバルーン）', 0, 3, 0.05), P_R('sway', 'ゆれ', 0, 2, 0.05)],
  colors: [['color', '色1'], ['color2', '色2']],
  place: () => [S.stage.width / 2 - 1, S.stage.height, -S.stage.depth / 2 + 1],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p; const n = Math.round(p.count); const R = mulberry32(17 + n);
    const cols = [inst.color, inst.color2, p.accent];
    const base = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.16, 0.08, 24), metalMat('#d8dce6')); base.position.y = 0.04; g.add(base);
    const heartShape = new THREE.Shape(shapePts2D('heart', 1)); const starShape = new THREE.Shape(shapePts2D('star', 1));
    const geoFor = (kind) => {
      if (kind === 'round') { const s = new THREE.SphereGeometry(0.5, 20, 16); s.scale(1, 1.15, 1); return s; }
      const e = new THREE.ExtrudeGeometry(kind === 'heart' ? heartShape : starShape, { depth: 0.18, bevelEnabled: true, bevelThickness: 0.12, bevelSize: 0.08, bevelSegments: 4, curveSegments: 16 });
      e.center(); return e;
    };
    const balls = [];
    const strM = stdMat({ color: '#f4f4f8', roughness: 0.6 });
    for (let i = 0; i < n; i++) {
      const kind = p.shape === 'mix' ? ['round', 'heart', 'star'][i % 3] : p.shape;
      const c = cols[i % 3];
      const m = stdMat({ color: c, roughness: kind === 'star' ? 0.15 : 0.22, metalness: kind === 'star' ? 0.75 : 0.1, env: kind === 'star' ? 1.1 : 0.7, emissive: p.glow > 0 ? c : null, ei: p.glow });
      const a = (i / n) * TAU + R() * 0.5, r = n > 1 ? 0.25 + R() * 0.35 : 0;
      const top = V3(Math.cos(a) * r, p.height * (0.8 + R() * 0.25), Math.sin(a) * r * 0.6);
      const piv = new THREE.Group(); piv.position.set(0, 0.08, 0); g.add(piv);
      const b = new THREE.Mesh(geoFor(kind), m); b.scale.setScalar(p.size); b.position.copy(top); piv.add(b);
      const knot = new THREE.Mesh(new THREE.ConeGeometry(0.05 * p.size, 0.08 * p.size, 8), m); knot.position.copy(top).y -= 0.55 * p.size; knot.rotation.x = Math.PI; piv.add(knot);
      const str = new THREE.Mesh(new THREE.TubeGeometry(new THREE.QuadraticBezierCurve3(V3(0, 0, 0), V3(top.x * 0.3, top.y * 0.4, top.z * 0.3), V3(top.x, top.y - 0.58 * p.size, top.z)), 12, 0.004, 4), strM); piv.add(str);
      balls.push({ piv, ph: R() * TAU, b });
    }
    return { obj: g, anim(F) {
      for (const q of balls) { q.piv.rotation.z = Math.sin(F.t * 0.9 + q.ph) * 0.05 * p.sway; q.piv.rotation.x = Math.sin(F.t * 0.7 + q.ph * 1.3) * 0.04 * p.sway; q.b.rotation.y = Math.sin(F.t * 0.5 + q.ph) * 0.4 * p.sway; }
    } };
  } });
defAsset('banner', { label: '垂れ幕・横断幕', cat: '造作・装飾', color: '#ffffff', color2: '#e0407a', p: { text: 'ようこそ！', vertical: true, width: 1.2, height: 4.5, border: '#8fd3ff', wave: 1 },
  params: [P_TXT('text', '文字'), P_CHK('vertical', '縦書き（垂れ幕）'), P_R('width', '幅', 0.3, 20, 0.05), P_R('height', '高さ', 0.3, 15, 0.05), P_CLR('border', 'ふちの色'), P_R('wave', 'はためき', 0, 2, 0.05)],
  colors: [['color', '布の色'], ['color2', '文字の色']],
  place: () => [-(S.stage.width / 2 + 1.2), 9, -S.stage.depth / 2 + 0.5],
  build(inst, ctx) {
    const g = new THREE.Group(); const p = inst.p; const W = p.width, H = p.height;
    const tex = bannerTexture(p.text || ' ', { bg: inst.color, fg: inst.color2, border: p.border, vertical: p.vertical, w: p.vertical ? 256 : 1024, h: p.vertical ? 1024 : 256 });
    const geo = new THREE.PlaneGeometry(W, H, p.vertical ? 4 : 24, p.vertical ? 24 : 4); geo.translate(0, -H / 2, 0);
    const mat = new THREE.MeshStandardMaterial({ map: tex, roughness: 0.85, side: THREE.DoubleSide }); mat.envMapIntensity = 0.25;
    const cloth = new THREE.Mesh(geo, mat); cloth.name = 'BannerCloth'; if (ctx.mirrored) cloth.scale.x = -1; g.add(cloth);
    const rod = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, W + 0.2, 12), metalMat('#c9ced8')); rod.rotation.z = Math.PI / 2; rod.position.y = 0.02; g.add(rod);
    const base = Float32Array.from(geo.attributes.position.array);
    return { obj: g, anim(F) {
      if (!p.wave) return;
      const pos = geo.attributes.position;
      for (let i = 0; i < pos.count; i++) {
        const x = base[i * 3], y = base[i * 3 + 1]; const k = Math.min(1, -y / H * 1.4);
        pos.setZ(i, Math.sin(F.t * 1.6 + y * 1.3 + x * 0.8) * 0.06 * p.wave * k);
      }
      pos.needsUpdate = true;
    } };
  } });
defAsset('barricade', { label: 'バリケード（柵）', cat: '造作・装飾', color: '#3a3d48', p: { length: 12 },
  params: [P_R('length', '長さ', 1, 60)], colors: [['color', '色']],
  place: () => [0, 0, S.stage.depth / 2 + 2.2],
  build(inst) {
    const g = new THREE.Group(); const L = inst.p.length; const m = metalMat(inst.color); const geos = [];
    const n = Math.max(1, Math.round(L / 1.2)); const sl = L / n;
    for (let i = 0; i <= n; i++) { const x = -L / 2 + i * sl; const c = new THREE.CylinderGeometry(0.025, 0.025, 1.15, 6); c.translate(x, 0.575, 0); geos.push(c); }
    for (const y of [0.15, 1.1]) { const c = new THREE.CylinderGeometry(0.025, 0.025, L, 6); c.rotateZ(Math.PI / 2); c.translate(0, y, 0); geos.push(c); }
    for (let x = -L / 2; x < L / 2; x += 0.13) { const c = new THREE.CylinderGeometry(0.008, 0.008, 0.95, 4); c.translate(x, 0.625, 0); geos.push(c); }
    for (let i = 0; i <= n; i++) { const c = new THREE.BoxGeometry(0.06, 0.04, 0.9); c.translate(-L / 2 + i * sl, 0.02, 0.25); geos.push(c); }
    const T = SURFACES.alu.tile; const mg = mergeGeometries(geos.map(x => { prefitUV(x, T); return x.index ? x.toNonIndexed() : x; })); mg.userData.uvKey = String(T);
    g.add(new THREE.Mesh(mg, m));
    return { obj: g };
  } });
defAsset('chairs', { label: '客席イス', cat: '造作・装飾', color: '#2a2d48', p: { rows: 6, cols: 14, sx: 0.6, sz: 1.0 },
  params: [P_R('rows', '列', 1, 40, 1), P_R('cols', '1列の席数', 1, 60, 1), P_R('sx', '横の間隔', 0.45, 1.2, 0.01), P_R('sz', '前後の間隔', 0.7, 2, 0.01)], colors: [['color', '色']],
  place: () => [0, 0, S.stage.depth / 2 + 4],
  build(inst) {
    const g = new THREE.Group(); const { rows, cols, sx, sz } = inst.p; const n = Math.round(rows) * Math.round(cols);
    const parts = [new THREE.BoxGeometry(0.44, 0.05, 0.42).translate(0, 0.45, 0), new THREE.BoxGeometry(0.44, 0.45, 0.05).translate(0, 0.7, 0.2)];
    for (const [x, z] of [[-0.19, -0.18], [0.19, -0.18], [-0.19, 0.18], [0.19, 0.18]]) parts.push(new THREE.CylinderGeometry(0.015, 0.015, 0.45, 5).translate(x, 0.225, z).toNonIndexed());
    const cm = paintMat(inst.color, { roughness: 0.55, metalness: 0.15 }); const T = cm.userData.surfTile;
    const geo = mergeGeometries(parts.map(p => { prefitUV(p, T); return p.index ? p.toNonIndexed() : p; })); geo.userData.uvKey = String(T);
    const im = new THREE.InstancedMesh(geo, cm, n); let i = 0;
    for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) { _m4.makeTranslation((c - (cols - 1) / 2) * sx, 0, r * sz); im.setMatrixAt(i++, _m4); }
    g.add(im); return { obj: g };
  } });

/* =================== 特効（パーティクル） =================== */
function particleMat(additive) {
  return new THREE.ShaderMaterial({
    uniforms: { uTex: { value: additive ? texSpark() : texSoft() }, uScale: { value: 1 } },
    vertexShader: /* glsl */`attribute vec3 aColor; attribute float aAlpha; attribute float aSize; varying vec3 vC; varying float vA; uniform float uScale;
      void main(){ vC=aColor; vA=aAlpha; vec4 mv = modelViewMatrix*vec4(position,1.0); gl_PointSize = aSize*uScale*(300.0 / max(0.1,-mv.z)); gl_Position = projectionMatrix*mv; }`,
    fragmentShader: /* glsl */`uniform sampler2D uTex; varying vec3 vC; varying float vA; void main(){ float a = texture2D(uTex, gl_PointCoord).r * vA; if(a<0.003) discard; gl_FragColor = vec4(vC, a); }`,
    transparent: true, depthWrite: false, blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
  });
}
function particlePool(N, additive) {
  const geo = new THREE.BufferGeometry();
  const pos = new Float32Array(N * 3), colr = new Float32Array(N * 3), al = new Float32Array(N), sz = new Float32Array(N);
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3)); geo.setAttribute('aColor', new THREE.BufferAttribute(colr, 3));
  geo.setAttribute('aAlpha', new THREE.BufferAttribute(al, 1)); geo.setAttribute('aSize', new THREE.BufferAttribute(sz, 1));
  const pts = new THREE.Points(geo, particleMat(additive)); pts.frustumCulled = false; markFx(pts);
  const P = { pts, N, pos, colr, al, sz, vel: new Float32Array(N * 3), life: new Float32Array(N), max: new Float32Array(N), next: 0 };
  P.flush = () => { for (const k of ['position', 'aColor', 'aAlpha', 'aSize']) geo.attributes[k].needsUpdate = true; };
  P.spawn = (x, y, z, vx, vy, vz, life, size, c) => { const i = P.next; P.next = (P.next + 1) % N; P.pos.set([x, y, z], i * 3); P.vel.set([vx, vy, vz], i * 3); P.life[i] = life; P.max[i] = life; P.sz[i] = size; P.colr.set([c.r, c.g, c.b], i * 3); };
  return P;
}
const fxActive = (F, key, dur) => F.t - FX_TRIG[key] < dur;
defAsset('spark', { label: 'コールドスパーク（噴水）', cat: '特効', color: '#2a2a30', color2: '#ffd98a', p: { height: 4, mode: 'trigger', rate: 260 },
  params: [P_R('height', '噴き上げ高さ', 1, 10, 0.1), P_SEL('mode', '発動', [['trigger', '特効ボタン・キューで'], ['always', '常に']]), P_R('rate', '量', 50, 800, 10)], colors: [['color', '本体色'], ['color2', '火花の色']],
  place: () => [-4, S.stage.height, S.stage.depth / 2 - 0.4],
  build(inst) {
    const g = new THREE.Group(); g.add(box(0.36, 0.18, 0.36, paintMat(inst.color, { roughness: 0.5, metalness: 0.4 }), 0, 0.09, 0));
    const P = particlePool(1400, true); g.add(P.pts); const c = col(inst.color2, 3.2), cw = col('#ffffff', 4); let acc = 0;
    const v0 = Math.sqrt(2 * 9.8 * inst.p.height);
    return { obj: g, anim(F) {
      const on = inst.p.mode === 'always' || fxActive(F, 'spark', 3.2);
      if (on) { acc += F.dt * inst.p.rate; while (acc > 1) { acc--; const a = Math.random() * TAU, s = Math.random() * 0.5; P.spawn(0, 0.2, 0, Math.cos(a) * s, v0 * (0.85 + Math.random() * 0.2), Math.sin(a) * s, 0.9 + Math.random() * 1.1, 0.05 + Math.random() * 0.05, Math.random() < 0.3 ? cw : c); } }
      for (let i = 0; i < P.N; i++) {
        if (P.life[i] <= 0) { P.al[i] = 0; continue; }
        P.life[i] -= F.dt; P.vel[i * 3 + 1] -= 9.8 * F.dt * 0.9;
        P.pos[i * 3] += P.vel[i * 3] * F.dt; P.pos[i * 3 + 1] += P.vel[i * 3 + 1] * F.dt; P.pos[i * 3 + 2] += P.vel[i * 3 + 2] * F.dt;
        if (P.pos[i * 3 + 1] < 0.05) { P.pos[i * 3 + 1] = 0.05; P.vel[i * 3 + 1] *= -0.2; P.vel[i * 3] *= 1.6; P.vel[i * 3 + 2] *= 1.6; }
        P.al[i] = Math.min(1, P.life[i] / P.max[i] * 2) * (0.6 + 0.4 * Math.random());
      }
      P.flush();
    } };
  } });
defAsset('confetti', { label: '紙吹雪キャノン', cat: '特効', color: '#2a2a30', p: { count: 500, power: 14, mode: 'trigger', pastel: true },
  params: [P_R('count', '枚数', 50, 1500, 10), P_R('power', '飛ばす強さ', 4, 30, 0.5), P_SEL('mode', '発動', [['trigger', '特効ボタン・キューで'], ['always', '常に降らせる']]), P_CHK('pastel', 'パステルカラー')],
  colors: [['color', '本体色']],
  place: () => [5, S.stage.height, S.stage.depth / 2 - 0.6],
  build(inst) {
    const g = new THREE.Group(); const N = Math.round(inst.p.count);
    const cn = new THREE.Group(); cn.rotation.x = 0.55; g.add(cn); cn.add(cyl(0.12, 0.16, 0.9, paintMat(inst.color, { metalness: 0.5, roughness: 0.4 }), 0, 0.45, 0, 24));
    const im = new THREE.InstancedMesh(new THREE.PlaneGeometry(0.07, 0.1), new THREE.MeshStandardMaterial({ color: '#ffffff', side: THREE.DoubleSide, roughness: 0.5, metalness: 0.3, emissive: '#ffffff', emissiveIntensity: 0.25 }), N);
    im.frustumCulled = false; markFx(im);
    const st = Array.from({ length: N }, () => ({ p: V3(0, -999, 0), v: V3(), r: V3(Math.random() * 6, Math.random() * 6, 0), w: V3(Math.random() * 6 - 3, Math.random() * 6 - 3, Math.random() * 6 - 3), life: 0 }));
    const R = mulberry32(77);
    for (let i = 0; i < N; i++) { im.setColorAt(i, inst.p.pastel ? _c.setHSL(R(), 0.85, 0.78, THREE.SRGBColorSpace) : _c.setHSL(R(), 1, 0.55, THREE.SRGBColorSpace)); _m4.makeScale(0, 0, 0); im.setMatrixAt(i, _m4); }
    g.add(im);
    let lastTrig = -99, alive = 0, acc = 0;
    const launch = (s) => { const a = (Math.random() - 0.5) * 0.9, up = 0.9 + Math.random() * 0.5; const v = inst.p.power * (0.5 + Math.random() * 0.6); s.p.set(0, 0.9, 0.35); s.v.set(Math.sin(a) * v * 0.6, Math.cos(0.55) * v * up, Math.sin(0.55) * v); s.life = 6 + Math.random() * 4; };
    return { obj: g, anim(F) {
      if (inst.p.mode === 'trigger' && FX_TRIG.confetti !== lastTrig && fxActive(F, 'confetti', 0.5)) { lastTrig = FX_TRIG.confetti; for (const s of st) launch(s); }
      if (inst.p.mode === 'always') { acc += F.dt * N / 8; while (acc > 1) { acc--; const s = st.find(x => x.life <= 0); if (!s) break; s.p.set((Math.random() - 0.5) * 16, 14, (Math.random() - 0.5) * 8 + 4); s.v.set(0, -0.5, 0); s.life = 12; } }
      alive = 0;
      for (let i = 0; i < N; i++) {
        const s = st[i];
        if (s.life <= 0) { if (s.p.y > -900) { s.p.y = -999; _m4.makeScale(0, 0, 0); im.setMatrixAt(i, _m4); alive++; } continue; }
        alive++; s.life -= F.dt;
        const drag = s.v.y < -1.2 ? 4 : 0.6;
        s.v.x -= s.v.x * Math.min(1, drag * 0.5 * F.dt); s.v.z -= s.v.z * Math.min(1, drag * 0.5 * F.dt);
        s.v.y -= 9.8 * F.dt; if (s.v.y < -1.0) s.v.y += (-1.0 - s.v.y) * Math.min(1, 6 * F.dt);
        s.p.addScaledVector(s.v, F.dt); s.p.x += Math.sin(F.t * 2 + i) * 0.4 * F.dt;
        if (s.p.y < 0.02) { s.p.y = 0.02; s.v.set(0, 0, 0); s.life = Math.min(s.life, 3); }
        s.r.addScaledVector(s.w, F.dt * (s.p.y > 0.05 ? 1 : 0));
        _q.setFromEuler(new THREE.Euler(s.r.x, s.r.y, s.r.z)); _m4.compose(s.p, _q, _v3.set(1, 1, 1)); im.setMatrixAt(i, _m4);
      }
      if (alive) im.instanceMatrix.needsUpdate = true;
    } };
  } });
defAsset('smoke', { label: 'スモーク／CO2', cat: '特効', color: '#2a2a30', p: { kind: 'co2', mode: 'trigger', amount: 1 },
  params: [P_SEL('kind', '種類', [['co2', 'CO2ジェット（上に噴射）'], ['lowfog', 'ドライアイス（床を這う）']]), P_SEL('mode', '発動', [['trigger', '特効ボタン・キューで'], ['always', '常に']]), P_R('amount', '量', 0.2, 3, 0.05)],
  colors: [['color', '本体色']],
  place: () => [3, S.stage.height, S.stage.depth / 2 - 0.5],
  build(inst) {
    const g = new THREE.Group(); g.add(box(0.4, 0.25, 0.5, paintMat(inst.color, { roughness: 0.6, metalness: 0.3 }), 0, 0.125, 0));
    const low = inst.p.kind === 'lowfog'; const P = particlePool(low ? 260 : 400, false); g.add(P.pts); const cw = col('#eef3ff', low ? 1.6 : 1.3); let acc = 0;
    return { obj: g, anim(F) {
      const on = inst.p.mode === 'always' || fxActive(F, 'smoke', low ? 6 : 1.6);
      if (on) { acc += F.dt * (low ? 30 : 160) * inst.p.amount; while (acc > 1) { acc--; const a = Math.random() * TAU;
        if (low) P.spawn((Math.random() - 0.5) * 0.4, 0.12, 0.3, Math.cos(a) * 0.9, 0.0, Math.abs(Math.sin(a)) * 1.2 + 0.3, 5 + Math.random() * 3, 2.2 + Math.random() * 1.5, cw);
        else P.spawn(0, 0.3, 0, (Math.random() - 0.5) * 0.6, 9 + Math.random() * 3, (Math.random() - 0.5) * 0.6, 1.3 + Math.random() * 0.5, 0.5, cw); } }
      for (let i = 0; i < P.N; i++) {
        if (P.life[i] <= 0) { P.al[i] = 0; continue; }
        P.life[i] -= F.dt; const k = 1 - P.life[i] / P.max[i];
        const damp = low ? 0.35 : 1.4; P.vel[i * 3] *= 1 - damp * F.dt; P.vel[i * 3 + 1] *= 1 - damp * F.dt; P.vel[i * 3 + 2] *= 1 - damp * F.dt;
        P.pos[i * 3] += P.vel[i * 3] * F.dt; P.pos[i * 3 + 1] += P.vel[i * 3 + 1] * F.dt; P.pos[i * 3 + 2] += P.vel[i * 3 + 2] * F.dt;
        P.sz[i] += F.dt * (low ? 0.6 : 1.6); if (low) P.pos[i * 3 + 1] = Math.max(0.1, P.pos[i * 3 + 1]);
        P.al[i] = (low ? 0.12 : 0.3) * Math.sin(Math.PI * Math.min(1, k * 1.2));
      }
      P.flush();
    } };
  } });

/* =================== 人 =================== */
defAsset('performer', { label: '演者ダミー（仮の人形）', cat: '人', color: '#ffb3d9', color2: '#5a3a2a', p: { dance: 'bounce', height: 1.6, hair: 'twin', accent: '#ffffff', mic: true },
  params: [P_SEL('dance', '動き', [['bounce', 'ノリノリ'], ['sway', 'ゆらゆら'], ['wave', '手を振る'], ['idle', '立ち']]), P_R('height', '身長', 1.2, 2, 0.01),
    P_SEL('hair', '髪型', [['twin', 'ツインテール'], ['pony', 'ポニーテール'], ['bob', 'ボブ']]), P_CLR('accent', 'リボン・靴の色'), P_CHK('mic', 'マイクを持つ')],
  colors: [['color', '衣装の色'], ['color2', '髪の色']],
  place: () => [0, S.stage.height, S.stage.ring.z],
  build(inst) {
    // VRChat では実際の演者（プレイヤー）に置き換わる「仮の人形」。オリジナルの簡単なデザイン
    const g = new THREE.Group(); const p = inst.p; const s = p.height / 1.6; const body = new THREE.Group(); body.name = 'PerfBody'; body.scale.setScalar(s); g.add(body);
    // 衣装＝サテン、髪＝毛の流れ、白い部分（フリル・手袋・靴下）＝布、リボン・靴＝つやのあるサテン
    const skin = stdMat({ color: '#ffe6d6', roughness: 0.62 }), dress = stdMat({ color: inst.color, roughness: 0.42, env: 0.5, surf: 'satin', tile: 0.18 }), hair = stdMat({ color: inst.color2, roughness: 0.5, env: 0.35, surf: 'hair', tile: 0.1 });
    const acc = stdMat({ color: p.accent, roughness: 0.35, env: 0.55, surf: 'satin', tile: 0.1 }), white = stdMat({ color: '#fbf7ff', roughness: 0.75, surf: 'fabric', tile: 0.05 }), dark = stdMat({ color: '#2a2030', roughness: 0.3, env: 0.6 });
    // 脚・靴
    for (const x of [-0.075, 0.075]) {
      body.add(cyl(0.042, 0.036, 0.6, skin, x, 0.33, 0, 10));
      body.add(cyl(0.046, 0.044, 0.22, white, x, 0.17, 0, 10));
      const shoe = new THREE.Mesh(new THREE.SphereGeometry(0.055, 12, 8), acc); shoe.scale.set(0.9, 0.55, 1.4); shoe.position.set(x, 0.03, 0.025); body.add(shoe);
    }
    // スカート（2段＋フリル）
    const skirtG = new THREE.Group(); skirtG.name = 'PerfSkirt'; skirtG.position.y = 0.98; body.add(skirtG);
    const pleatM = stdMat({ color: inst.color, roughness: 0.42, env: 0.5, surf: 'pleat', side: THREE.DoubleSide });   // プリーツスカート
    const sk1 = new THREE.Mesh(new THREE.CylinderGeometry(0.13, 0.36, 0.34, 48, 1, true), pleatM); sk1.position.y = -0.17; sk1.material.side = THREE.DoubleSide; skirtG.add(sk1);
    const pet = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.39, 0.06, 24, 1, true), white); pet.position.y = -0.33; pet.material.side = THREE.DoubleSide; skirtG.add(pet);
    const fr = new THREE.Mesh(new THREE.TorusGeometry(0.365, 0.022, 6, 32), white); fr.rotation.x = Math.PI / 2; fr.position.y = -0.34; skirtG.add(fr);
    // 胴・リボン・パフスリーブ
    body.add(cyl(0.12, 0.14, 0.32, dress, 0, 1.13, 0, 16));
    const belt = new THREE.Mesh(new THREE.TorusGeometry(0.135, 0.018, 6, 24), acc); belt.rotation.x = Math.PI / 2; belt.position.y = 0.985; body.add(belt);
    for (const sd of [-1, 1]) { const bw = new THREE.Mesh(new THREE.ConeGeometry(0.045, 0.09, 8), acc); bw.rotation.z = sd * Math.PI / 2; bw.position.set(sd * 0.045, 1.2, 0.125); body.add(bw); }
    const knot = new THREE.Mesh(new THREE.SphereGeometry(0.022, 8, 6), acc); knot.position.set(0, 1.2, 0.13); body.add(knot);
    body.add(cyl(0.035, 0.035, 0.06, skin, 0, 1.32, 0, 8));
    // 頭・顔
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.135, 24, 18), skin); head.position.y = 1.45; head.name = 'PerfHead'; body.add(head);   // カメラのアップが追う顔
    for (const sd of [-1, 1]) {
      const eye = new THREE.Mesh(new THREE.SphereGeometry(0.02, 10, 8), dark); eye.scale.set(0.8, 1.25, 0.5); eye.position.set(sd * 0.047, 1.455, 0.122); body.add(eye);
      const hl = new THREE.Mesh(new THREE.SphereGeometry(0.006, 6, 4), glowMat('#ffffff', 1.2)); hl.position.set(sd * 0.047 + 0.006, 1.465, 0.131); body.add(hl);
      const blush = new THREE.Mesh(new THREE.CircleGeometry(0.018, 12), glowMat('#ff9fbf', 0.9, { transparent: true, opacity: 0.6 })); blush.position.set(sd * 0.075, 1.425, 0.118); blush.rotation.y = sd * 0.5; body.add(blush);
    }
    const hr = new THREE.Mesh(new THREE.SphereGeometry(0.148, 24, 16, 0, TAU, 0, Math.PI * 0.6), hair); hr.position.y = 1.465; hr.rotation.x = -0.32; body.add(hr);
    const back = new THREE.Mesh(new THREE.SphereGeometry(0.145, 18, 12, 0, TAU, Math.PI * 0.35, Math.PI * 0.5), hair); back.position.set(0, 1.44, -0.012); back.rotation.x = 0.2; body.add(back);
    const tails = [];
    const tail = (x, z, len) => { const t = new THREE.Group(); t.name = 'PerfTail' + tails.length; t.position.set(x, 1.52, z); const m = new THREE.Mesh(new THREE.CapsuleGeometry(0.055, len, 4, 10), hair); m.position.y = -len / 2 - 0.02; t.add(m);
      const rb = new THREE.Mesh(new THREE.TorusGeometry(0.04, 0.016, 6, 14), acc); rb.position.y = 0.01; rb.rotation.y = Math.PI / 2; t.add(rb); body.add(t); tails.push(t); return t; };
    if (p.hair === 'twin') { tail(-0.16, -0.04, 0.32); tail(0.16, -0.04, 0.32); }
    else if (p.hair === 'pony') tail(0, -0.13, 0.38);
    else { const bob = new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.165, 0.16, 20, 1, true), hair); bob.position.y = 1.39; bob.material.side = THREE.DoubleSide; body.add(bob); }
    // 腕（パフスリーブ・手袋）
    const arms = [];
    for (const sd of [-1, 1]) {
      const sh = new THREE.Group(); sh.name = 'PerfArm' + arms.length; sh.position.set(sd * 0.165, 1.25, 0); body.add(sh); arms.push(sh);
      const puff = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 10), dress); sh.add(puff);
      sh.add(cyl(0.03, 0.026, 0.38, skin, 0, -0.22, 0, 8));
      const hand = new THREE.Mesh(new THREE.SphereGeometry(0.034, 10, 8), white); hand.position.y = -0.42; sh.add(hand);
      if (p.mic && sd < 0) { const mic = new THREE.Group(); mic.position.set(0, -0.42, 0.03); mic.rotation.x = -1.2; sh.add(mic);
        mic.add(cyl(0.016, 0.012, 0.16, stdMat({ color: '#e8eaf2', metalness: 0.7, roughness: 0.3, env: 0.8, surf: 'alu', tile: 0.1 }), 0, 0.05, 0, 12));
        const mh = new THREE.Mesh(new THREE.SphereGeometry(0.027, 16, 12), stdMat({ color: '#9aa0aa', metalness: 0.8, roughness: 0.45, env: 0.8, surf: 'grille', tile: 0.01 })); mh.position.y = 0.14; mic.add(mh); }
    }
    const ph = hash1(inst.pos[0] * 3.1 + inst.pos[2]);
    return { obj: g, performer: true, anim(F) {
      const b = F.beat + ph; const d = p.dance; const hop = Math.abs(Math.sin(b * Math.PI));
      body.position.y = 0; body.rotation.set(0, 0, 0);
      if (d === 'bounce') {
        body.position.y = hop * 0.06; body.rotation.y = Math.sin(b * Math.PI * 0.5) * 0.35;
        arms[0].rotation.set(p.mic ? -1.1 : 0, 0, p.mic ? -0.35 : -2.4 + Math.sin(b * Math.PI) * 0.4);
        arms[1].rotation.set(-0.4, 0, 2.5 + Math.sin(b * Math.PI + 1) * 0.35);
      } else if (d === 'sway') {
        body.rotation.z = Math.sin(b * Math.PI * 0.5) * 0.08; body.rotation.y = Math.sin(b * Math.PI * 0.25) * 0.2;
        arms[0].rotation.set(p.mic ? -1.1 : 0, 0, p.mic ? -0.3 : -0.5 + Math.sin(b * Math.PI * 0.5) * 0.3);
        arms[1].rotation.set(0, 0, 0.5 + Math.sin(b * Math.PI * 0.5) * 0.3);
      } else if (d === 'wave') {
        body.rotation.y = Math.sin(b * Math.PI * 0.25) * 0.15;
        arms[0].rotation.set(p.mic ? -1.1 : 0, 0, p.mic ? -0.3 : -0.2);
        arms[1].rotation.set(0, 0, 2.6 + Math.sin(F.t * 7) * 0.35);
      } else { body.position.y = Math.sin(F.t * 1.5) * 0.005; arms[0].rotation.set(p.mic ? -1.1 : 0, 0, p.mic ? -0.3 : -0.15); arms[1].rotation.set(0, 0, 0.15); }
      skirtG.rotation.z = -body.rotation.z * 0.8 + Math.sin(b * Math.PI) * 0.03;
      for (const t of tails) t.rotation.z = Math.sin(b * Math.PI + 0.6) * 0.18 * (d === 'idle' ? 0.2 : 1) * (t.position.x < 0 ? 1 : -1);
    } };
  } });
defAsset('audience', { label: '観客（ペンライト）', cat: '人', color: '#ffffff', p: { width: 22, depth: 10, gap: 0.75, penlight: 'cue', body: true, sway: 1, two: false, jump: 0.5 },
  params: [P_R('width', 'エリアの幅', 2, 80), P_R('depth', 'エリアの奥行', 1, 60), P_R('gap', '人の間隔', 0.5, 2, 0.05), P_SEL('penlight', 'ペンライトの色', [['cue', 'キューに合わせる'], ['white', '白'], ['rainbow', '虹色'], ['pastel', 'パステルばらばら'], ['custom', '指定色（下の色）']]),
    P_CHK('body', '人のシルエット'), P_CHK('two', '両手に持つ'), P_R('sway', '振りの大きさ', 0, 2, 0.05), P_R('jump', 'ノリ（拍で弾む）', 0, 2, 0.05)],
  colors: [['color', 'ペンライト指定色']],
  place: () => [0, 0, S.stage.depth / 2 + 3.5],
  build(inst) {
    const g = new THREE.Group(); const p = inst.p; const cols = Math.max(1, Math.floor(p.width / p.gap)), rows = Math.max(1, Math.floor(p.depth / p.gap));
    const people = []; const R = mulberry32(5);
    for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) people.push({ x: (c - (cols - 1) / 2) * p.gap + (R() - 0.5) * p.gap * 0.5 + (r % 2) * p.gap * 0.3, z: r * p.gap + (R() - 0.5) * p.gap * 0.4, h: 1.5 + R() * 0.25, ph: R(), hue: R() });
    const N = people.length;
    if (p.body) {
      const bg = mergeGeometries([new THREE.CapsuleGeometry(0.2, 0.75, 3, 8).translate(0, 0.6, 0).toNonIndexed(), new THREE.SphereGeometry(0.12, 8, 6).translate(0, 1.2, 0).toNonIndexed()]);
      const bm = new THREE.InstancedMesh(bg, stdMat({ color: '#0d0e18', roughness: 0.9, env: 0.05 }), N);
      people.forEach((q, i) => { const k = q.h / 1.6; _m4.compose(_v1.set(q.x, 0, q.z), _q.identity(), _v2.set(k, k, k)); bm.setMatrixAt(i, _m4); });
      bm.name = 'AudienceBodies'; g.add(bm); g.userData.bodies = bm;
      bm.userData.crowd = { ph: people.map(q => q.ph) };   // Unity 書き出し用：1人ずつの位相（シェーダーで弾ませる）
      // Unity 書き出し用の軽い形（遠目のシルエットなので低ポリで十分。VRChat・Quest の負荷を抑える）
      bm.userData.exportGeo = mergeGeometries([new THREE.CapsuleGeometry(0.2, 0.75, 2, 6).translate(0, 0.6, 0), new THREE.SphereGeometry(0.12, 6, 4).translate(0, 1.2, 0)]);
    }
    const bodies = g.userData.bodies || null;
    const hands = p.two ? 2 : 1; const M = N * hands;
    const pg = new THREE.CylinderGeometry(0.014, 0.016, 0.28, 6); pg.translate(0, 0.14, 0);
    const pl = new THREE.InstancedMesh(pg, new THREE.MeshBasicMaterial({ color: '#ffffff' }), M); pl.name = 'Penlights'; pl.frustumCulled = false; g.add(pl);
    const E = new THREE.Euler(); const custom = col(inst.color);
    for (let i = 0; i < M; i++) pl.setColorAt(i, _c.set(1, 1, 1));
    // Unity 書き出し用：ペンライト1本ずつの「位相」と「左右」（シェーダーで揺らすため）
    pl.userData.penlight = { len: 0.28, sticks: [] };
    pl.userData.exportGeo = pg.clone();   // 書き出しはインデックス付きのまま（頂点数を抑える）
    for (let i = 0; i < N; i++) for (let hnd = 0; hnd < hands; hnd++) pl.userData.penlight.sticks.push({ ph: people[i].ph, hue: people[i].hue, y: 1.42 * people[i].h / 1.6, side: hands === 2 ? (hnd ? 1 : -1) : (people[i].ph > 0.5 ? 1 : -1) });
    return { obj: g, anim(F) {
      const mode = p.penlight === 'cue' ? (F.cue.penlight || 'cue') : p.penlight;
      for (let i = 0; i < N; i++) {
        const q = people[i];
        const k0 = q.h / 1.6, jy = p.jump > 0 ? Math.pow(Math.abs(Math.sin((F.beat + q.ph * 0.3) * Math.PI)), 2) * 0.07 * p.jump : 0;
        if (bodies && p.jump > 0) { _m4.compose(_v1.set(q.x, jy, q.z), _q.identity(), _v2.set(k0, k0, k0)); bodies.setMatrixAt(i, _m4); }
        for (let hnd = 0; hnd < hands; hnd++) {
          const j = i * hands + hnd; const side = hands === 2 ? (hnd ? 1 : -1) : (q.ph > 0.5 ? 1 : -1);
          const sw = Math.sin((F.beat + q.ph * 0.15) * Math.PI) * 0.5 * p.sway;
          E.set(0.25, 0, sw + side * 0.1); _q.setFromEuler(E);
          const k = q.h / 1.6;
          _m4.compose(_v1.set(q.x + side * 0.22 * k, 1.42 * k + jy, q.z + 0.05), _q, _v2.set(1, 1, 1)); pl.setMatrixAt(j, _m4);
          let c;
          if (mode === 'white') c = _c.set(1, 1, 1);
          else if (mode === 'rainbow') c = _c.setHSL(fract(q.hue + F.t * 0.05), 0.9, 0.6, THREE.SRGBColorSpace);
          else if (mode === 'pastel') c = _c.setHSL(q.hue, 0.8, 0.75, THREE.SRGBColorSpace);
          else if (mode === 'custom') c = _c.copy(custom);
          else c = _c.copy(F.colors[Math.floor(q.hue * 3) % 3]);
          pl.setColorAt(j, c.multiplyScalar(1.25));
        }
      }
      pl.instanceMatrix.needsUpdate = true; pl.instanceColor.needsUpdate = true;
      if (bodies && p.jump > 0) bodies.instanceMatrix.needsUpdate = true;
    } };
  } });

/* =================== カスタム（GLB読み込み） =================== */
const glbCache = new Map();
function loadGLB(id) {
  if (glbCache.has(id)) return glbCache.get(id);
  const b = BLOBS[id]; if (!b) return Promise.reject(new Error('モデルのデータが見つかりません（JSON保存したファイルから読み込むと復元できます）'));
  const pr = fetch(b.dataURL).then(r => r.arrayBuffer()).then(buf => new Promise((res, rej) => new GLTFLoader().parse(buf, '', g => res(g.scene), rej)));
  glbCache.set(id, pr); return pr;
}
defAsset('glb', { label: '自作モデル（GLB）', cat: 'カスタム', color: '#cccccc', p: { file: null }, params: [], colors: [],
  place: () => [0, S.stage.height, 0],
  build(inst) {
    const g = new THREE.Group();
    const ph = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshBasicMaterial({ color: '#7fe8ff', wireframe: true })); ph.position.y = 0.5; g.add(ph);
    if (inst.p.file) loadGLB(inst.p.file).then(sc => { g.remove(ph); const c = sc.clone(true); c.traverse(o => { o.userData.instId = inst.id; }); g.add(c); }).catch(e => toast('GLB読み込み失敗: ' + e.message));
    return { obj: g };
  } });

/* ---------- 生成・配置 ---------- */
function disposeInstance(id) {
  const rt = RT_ASSETS.get(id); if (!rt) return;
  G.assets.remove(rt.root); disposeTree(rt.root); RT_ASSETS.delete(id); REG_DIRTY = true;
}
function buildInstance(inst) {
  disposeInstance(inst.id);
  const d = ASSET_DEFS[inst.type]; if (!d) { console.warn('unknown asset', inst.type); return; }
  const root = new THREE.Group(); root.name = `${inst.type}_${inst.id}`;
  const rt = { root, units: [] };
  const n = clamp(Math.round(inst.arr?.n ?? 1), 1, 30);
  for (const mir of inst.mirror ? [false, true] : [false]) for (let i = 0; i < n; i++) {
    const wrap = new THREE.Group(); wrap.name = mir ? 'Wrap_R' : 'Wrap_L'; if (mir) wrap.scale.x = -1;
    let u;
    try { u = d.build(inst, { mirrored: mir, idx: i }) || {}; } catch (e) { console.error(e); u = { obj: new THREE.Group() }; }
    const obj = u.obj || new THREE.Group(); obj.name = `${d.type}${mir ? '_R' : ''}${n > 1 ? '_' + i : ''}`;
    fitSurfaceUVs(obj);   // 質感のテクスチャが「メートル単位」で同じ細かさになるように UV を合わせる
    wrap.add(obj); root.add(wrap);
    for (const w of u.worldFx || []) root.add(w);
    rt.units.push({ wrap, obj, mirrored: mir, idx: i, fixtures: u.fixtures || [], monitors: u.monitors || [], anim: u.anim || null, performer: !!u.performer });
  }
  root.traverse(o => { o.userData.instId = inst.id; });
  root.visible = inst.visible !== false;
  G.assets.add(root); RT_ASSETS.set(inst.id, rt);
  layoutInstance(inst); REG_DIRTY = true;
}
function layoutInstance(inst) {
  const rt = RT_ASSETS.get(inst.id); if (!rt) return;
  for (const u of rt.units) {
    u.obj.position.set(inst.pos[0] + u.idx * (inst.arr?.dx ?? 2), inst.pos[1], inst.pos[2]);
    u.obj.rotation.set(inst.rot[0] * DEG, inst.rot[1] * DEG, inst.rot[2] * DEG);
    u.obj.scale.set(inst.scale[0], inst.scale[1], inst.scale[2]);
  }
  rt.root.visible = inst.visible !== false;
}
function rebuildAllAssets() {
  for (const id of [...RT_ASSETS.keys()]) disposeInstance(id);
  for (const a of S.assets) buildInstance(a);
}
function refreshRegistry() {
  REG.fixtures = []; REG.monitors = []; REG.anims = []; REG.performerUnits = [];
  for (const a of S.assets) {
    if (a.visible === false) continue;
    const rt = RT_ASSETS.get(a.id); if (!rt) continue;
    rt.units.forEach((u, k) => {
      for (const f of u.fixtures) REG.fixtures.push(f);
      for (const m of u.monitors) REG.monitors.push(m);
      if (u.anim) REG.anims.push(u.anim);
      if (u.performer) { u.obj.userData.perfName = a.name + (rt.units.length > 1 ? `（${k + 1}）` : ''); REG.performerUnits.push(u.obj); }
    });
  }
  REG_DIRTY = false;
}
