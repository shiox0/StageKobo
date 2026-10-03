
/* =====================================================================
   ステージカメラ（ライブ映像）→ RenderTarget → LEDモニター
   VRC: Camera(TargetTexture=RenderTexture) を Animator/Udon で動かし、
        モニターのマテリアルにその RenderTexture を挿す構成にそのまま対応
   ===================================================================== */
const CAM_MODES = [['auto', 'オート（2小節ごとにショット切替）'], ['orbit', 'オービット（周回）'], ['dolly', 'ドリー（寄り引き）'], ['crane', 'クレーン（上下）'], ['truck', 'トラック（横移動）'], ['closeup', 'アップ（顔寄り）'], ['audience', '客席から'], ['top', '真上から'], ['fixed', '固定（正面）']];
const AUTO_SHOTS = ['orbit', 'closeup', 'crane', 'truck', 'dolly', 'audience', 'top'];
const CAM_RES = { low: 320, mid: 512, high: 768 };
function makeRT(w, h) { const r = new THREE.WebGLRenderTarget(w, h, { type: THREE.HalfFloatType }); r.texture.generateMipmaps = false; return r; }
/* 解像度（16:9 のときの横幅）と縦横比 → RenderTarget の大きさ。ピクセル数は 16:9 のときとほぼ同じ */
function camRTSize(res, asp) {
  const area = res * Math.round(res * 9 / 16);
  const w = Math.sqrt(area * asp), h = Math.sqrt(area / asp), k = Math.min(1, 2048 / Math.max(w, h));
  return [Math.max(16, Math.round(w * k)), Math.max(16, Math.round(h * k))];
}
/* カメラの画角の縦横比：そのカメラを映しているモニターのうち、いちばん横長のものに合わせる（縦の画角はそのまま）。
   ほかのモニターは、まん中をそのモニターの縦横比で切り抜いて映す（＝そのモニターの縦横比のカメラで撮ったのと同じ絵）。
   モニターの幅・高さを変えても、映像は伸び縮みせず、映る範囲（画角）だけが変わる */
function camAspect(key) {
  let a = 0;
  for (const m of REG.monitors) if (m.inst.p && m.inst.p.src === key) a = Math.max(a, m.mat.uniforms.uAspect.value);
  return a > 0 ? clamp(a, 0.25, 4) : 16 / 9;
}
function makeCamModel(color) {
  const g = new THREE.Group(); g.name = 'StageCamRig';
  const bm = stdMat({ color: '#2b2e3a', roughness: 0.5, metalness: 0.4 });
  g.add(box(0.22, 0.24, 0.42, bm, 0, 0, 0.05));
  const lens = cyl(0.08, 0.1, 0.22, bm, 0, 0, -0.25, 16); lens.rotation.x = Math.PI / 2; g.add(lens);
  const rec = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 6), glowMat(color, 3)); rec.position.set(0.08, 0.14, 0.1); g.add(rec);
  const fr = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.ConeGeometry(0.35, 0.6, 4, 1, true).rotateX(-Math.PI / 2).rotateZ(Math.PI / 4).translate(0, 0, -0.62)), new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.6 }));
  g.add(fr);
  return g;
}
class StageCam {
  constructor(key, color) {
    this.key = key; this.cam = new THREE.PerspectiveCamera(30, 16 / 9, 0.1, 600);
    this.aspect = 16 / 9; this.rtKey = ''; this.ensureRT(CAM_RES.mid, 16 / 9);
    this.model = makeCamModel(color); G.helpers.add(this.model);
    this.pose = { pos: V3(0, 3, 10), look: V3(0, 2, 0) }; this.smooth = V3(0, 3, 10); this.shot = 'orbit';
  }
  ensureRT(res, asp) {
    const [w, h] = camRTSize(res, asp), key = w + 'x' + h;
    if (this.rtKey === key) return; this.rtKey = key;
    if (this.rtA) { this.rtA.dispose(); this.rtB.dispose(); }
    this.rtA = makeRT(w, h); this.rtB = makeRT(w, h); this.read = this.rtA; this.write = this.rtB;
    this.aspect = w / h; this.cam.aspect = this.aspect; this.cam.updateProjectionMatrix();
  }
  swap() { const t = this.read; this.read = this.write; this.write = t; }
}
const SCAMS = [];
function shotPose(mode, t, f, cfg, out) {
  const sp = cfg.speed, d = cfg.dist, h = cfg.height;
  switch (mode) {
    case 'orbit': { const a = Math.sin(t * 0.15 * sp) * 1.1; out.pos.set(f.x + Math.sin(a) * d, f.y + h * 0.4 + 0.3, f.z + Math.cos(a) * d); out.look.copy(f); break; }
    case 'dolly': { const k = 0.5 + 0.5 * Math.sin(t * 0.25 * sp); out.pos.set(f.x, f.y + 0.25, f.z + lerp(d * 0.4, d * 1.4, k)); out.look.copy(f); break; }
    case 'crane': { const k = 0.5 + 0.5 * Math.sin(t * 0.2 * sp); out.pos.set(f.x + Math.sin(t * 0.1 * sp) * 2, f.y + lerp(-0.6, 6, k), f.z + d * 1.1); out.look.copy(f); out.look.y += lerp(0.3, -0.5, k); break; }
    case 'truck': { const s = Math.sin(t * 0.2 * sp); out.pos.set(f.x + s * d * 0.8, f.y + 0.2, f.z + d * 0.8); out.look.set(f.x + s * 1.0, f.y, f.z); break; }
    case 'closeup': { out.pos.set(f.x + Math.sin(t * 0.3 * sp) * 0.6 + Math.sin(t * 0.7) * 0.05, f.y + 0.15 + Math.sin(t * 1.1) * 0.03, f.z + d * 0.45); out.look.set(f.x, f.y + 0.1, f.z); break; }
    case 'audience': { out.pos.set(Math.sin(t * 0.1 * sp) * 3, 2.2 + Math.sin(t * 0.9) * 0.05, S.stage.depth / 2 + 12); out.look.copy(f); break; }
    case 'top': { const a = t * 0.1 * sp; out.pos.set(f.x + Math.cos(a) * 1.5, f.y + d * 1.5 + 4, f.z + Math.sin(a) * 1.5); out.look.copy(f); break; }
    default: out.pos.set(f.x, f.y + 0.4, f.z + d); out.look.copy(f);
  }
  return out;
}
/* アップ（顔）：追う人の顔の正面（客席側）から、胸から上が入る大きさで撮る。Unity の UdonSharp 版では「登録した人」の頭で同じ式を使う
   距離 = 距離 × 0.35（1.2〜6m）、縦に入る高さ = 0.75m × 画角/30 */
function faceDist(cfg) { return clamp(cfg.dist * 0.35, 1.2, 6); }
function faceFov(cfg) { return 2 * Math.atan(0.375 * (cfg.fov / 30) / faceDist(cfg)) * 180 / Math.PI; }
function faceShot(t, hd, cfg, out) {
  const sp = cfg.speed, d = faceDist(cfg);
  out.pos.set(hd.x + Math.sin(t * 0.3 * sp) * d * 0.18 + Math.sin(t * 0.7) * 0.02, hd.y - 0.03 + Math.sin(t * 1.1) * 0.015, hd.z + d);
  out.look.set(hd.x, hd.y - 0.08, hd.z);
  return out;
}
function initStageCams() {
  SCAMS.push(new StageCam('cam1', '#ff4d7a'), new StageCam('cam2', '#4dd8ff'));
}
function updateStageCams() {
  const cfgs = [S.cams.cam1, S.cams.cam2];
  SCAMS.forEach((sc, k) => {
    const cfg = cfgs[k];
    let mode = cfg.mode;
    if (mode === 'auto') { const seg = Math.floor(F.beat / 8); mode = AUTO_SHOTS[(seg * 5 + k * 3) % AUTO_SHOTS.length]; }
    sc.shot = mode;
    const hd = mode === 'closeup' && cfg.target >= 0 ? F.heads[cfg.target] : null;   // アップで追う人の顔（いなければ全員のまん中）
    if (hd) { faceShot(F.t, hd, cfg, sc.pose); sc.cam.fov = faceFov(cfg); }
    else { shotPose(mode, F.t, F.focus, cfg, sc.pose); sc.cam.fov = mode === 'closeup' ? cfg.fov * 0.75 : cfg.fov; }
    sc.cam.position.copy(sc.pose.pos); sc.cam.lookAt(sc.pose.look);
    sc.cam.updateProjectionMatrix();
    sc.model.position.copy(sc.cam.position); sc.model.quaternion.copy(sc.cam.quaternion);
    sc.model.visible = !!S.cams.showRig && VIEW.mode !== sc.key;
  });
}
function camUsed(key) {
  if (VIEW.mode === key) return true;
  if (key === 'cam1' && S.cams.pip) return true;
  for (const m of REG.monitors) if (m.inst.p && m.inst.p.src === key && !monitorShowsVJ(m.inst)) return true;   // VJ 映像に切り替えたモニターはカメラを使わない
  return false;
}
function applyMonitorSources() {
  for (const m of REG.monitors) {
    const src = m.inst.p?.src; const u = m.mat.uniforms;
    if (monitorShowsVJ(m.inst)) continue;   // VJ リモコンの映像（vjApplyTargets が貼る）
    if (src === 'cam1' || src === 'cam2') {
      const sc = SCAMS[src === 'cam1' ? 0 : 1];
      u.uTex.value = sc.read.texture; u.uSrc.value = 0; u.uTexLod.value = 0;
      coverRect(u.uAspect.value, sc.aspect, u.uTexRect.value);   // カメラの絵のまん中を、モニターの縦横比で切り抜く（伸び縮みしない）
    }
    else if (src === 'pattern') u.uSrc.value = 1;
  }
}
let camFrame = 0;
function renderStageCams() {
  camFrame++;
  const every = QUALITY.camEvery;
  const res = CAM_RES[S.cams.res] || CAM_RES.mid;
  const hid = [G.helpers, tcontrols];
  const prevVis = hid.map(o => o.visible);
  hid.forEach(o => { o.visible = false; });
  const rv = reflector ? reflector.visible : false; if (reflector) reflector.visible = false;
  SCAMS.forEach((sc) => sc.ensureRT(res, camAspect(sc.key)));   // 作り直したら古い RT を使っているモニターをすぐ差し替える（下の applyMonitorSources）
  applyMonitorSources();
  SCAMS.forEach((sc, k) => {
    if (!camUsed(sc.key) || (camFrame + k) % every) return;
    renderer.setRenderTarget(sc.write);
    renderer.clear();
    renderer.render(scene, sc.cam);
    sc.swap();
    applyMonitorSources();
  });
  renderer.setRenderTarget(null);
  hid.forEach((o, i) => { o.visible = prevVis[i]; });
  if (reflector) reflector.visible = rv;
}
/* 画面右下のプレビュー（PIP） */
const pipScene = new THREE.Scene(); const pipCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
const pipMat = new THREE.MeshBasicMaterial({ color: '#ffffff' });
pipScene.add(new THREE.Mesh(new THREE.PlaneGeometry(2, 2), pipMat));
const pipLabel = $('#pipLabel');
function renderPIP() {
  const show = S.cams.pip && VIEW.mode !== 'cam1';
  pipLabel.classList.toggle('hide', !show);
  if (!show) return;
  if (pipMat.map !== SCAMS[0].read.texture) { const first = !pipMat.map; pipMat.map = SCAMS[0].read.texture; if (first) pipMat.needsUpdate = true; }
  // カメラ1の縦横比のまま（縦長のモニター用のときは縦長のプレビュー）
  const W = viewEl.clientWidth, H = viewEl.clientHeight, A = SCAMS[0].aspect;
  let w = Math.min(300, W * 0.28), h = w / A; if (h > Math.min(240, H * 0.4)) { h = Math.min(240, H * 0.4); w = h * A; }
  w = Math.round(w); h = Math.round(h); const x = 14, y = 14;
  renderer.setScissorTest(true); renderer.setScissor(x - 2, y - 2, w + 4, h + 4); renderer.setViewport(x - 2, y - 2, w + 4, h + 4);
  renderer.setClearColor('#ff4d7a', 1); renderer.clear(); renderer.setClearColor('#000000', 1);
  renderer.setScissor(x, y, w, h); renderer.setViewport(x, y, w, h);
  renderer.render(pipScene, pipCam);
  renderer.setScissorTest(false); renderer.setViewport(0, 0, W, H);
  pipLabel.style.bottom = (y + h + 6) + 'px';
  pipLabel.textContent = '● CAM1 LIVE — ' + (CAM_MODES.find(m => m[0] === SCAMS[0].shot)?.[1].split('（')[0] || '');
}
