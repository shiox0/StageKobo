
/* =====================================================================
   シーンプリセット（雰囲気ごとのひな形）・初期化・ループ
   ===================================================================== */
const A = (type, over) => makeInst(type, over);
function paletteCues(c1, c2, c3) {
  return defaultCues().map(q => (q.colorMode === 'rainbow' || q.pattern === 'center') ? q : { ...q, c1, c2, c3 });
}
function rigBase(st, o = {}) {
  const H = st.height, D = st.depth, W = st.width;
  const ty = o.trussY ?? 8.5, tz = o.trussZ ?? D / 2 - 1; const hn = o.hangN ?? 4;
  const list = [
    A('moving_head', { name: 'ムービング（奥・床置き）', pos: [o.backX ?? 1.6, (o.backY ?? H), o.backZ ?? -D / 2 + 0.6], mirror: true, arr: { n: o.backN ?? 3, dx: o.backDx ?? 2.4 }, p: { mount: 'floor', beam: 'beam' } }),
    A('truss_beam', { name: 'フロントトラス', pos: [0, ty, tz], p: { length: W + 6, width: 0.4 } }),
    A('moving_head', { name: 'ムービング（吊り）', pos: [1.4, ty - 0.22, tz], mirror: true, arr: { n: hn, dx: (W / 2 + 1.5) / hn }, p: { mount: 'hang', beam: o.hangBeam ?? 'spot' } }),
    A('pin_spot', { name: 'ピンスポット', pos: [0, ty + 3, D / 2 + 11] }),
  ];
  return list;
}
const perf = (st, x, color, hair = '#5a3a2a', z) => A('performer', { name: '演者ダミー', pos: [x, st.height, z ?? st.ring.z], color, color2: hair });
const PRESETS = {
  pop: { label: 'ポップ・パステル', desc: 'トラス＋LEDパネル、チェッカー床、LED階段', build() {
    const s = baseState(); s.name = 'POP STAGE';
    Object.assign(s.stage, { shape: 'rect', width: 16, depth: 8, height: 1.2, floor: 'checker', floorColor: '#d3e9ff', floorColor2: '#5fa8e6', tile: 0.9, reflect: true, reflectStrength: 0.45, edgeColor: '#7fe8ff', front: 'panel', frontColor: '#2c4f8f' });
    Object.assign(s.stage.steps, { on: true, pos: 'center', width: 10, count: 4 }); s.stage.ring.on = false;
    Object.assign(s.venue, { type: 'hall', color: '#0f1638' });
    Object.assign(s.backdrop, { type: 'truss_led', c1: '#8fd3ff', c2: '#ff8ad8', c3: '#ffffff', pattern: 'triangles', width: 22, height: 10 });
    s.lights.cues = paletteCues('#8fd3ff', '#ff8ad8', '#ffffff');
    const st = s.stage;
    s.assets = [
      ...rigBase(st),
      A('led_monitor', { name: 'メインモニター', pos: [0, 0, -4.0], p: { w: 8, h: 4.5, lift: 2.3, src: 'cam1', frame: 'bezel', mount: 'wall', density: 13 } }),
      A('led_monitor', { name: 'サイドモニター', pos: [13.6, 0, -1.2], rot: [0, -16, 0], mirror: true, p: { w: 4, h: 5.6, lift: 1.6, src: 'cam2', frame: 'truss', density: 12 } }),
      A('line_array', { name: 'ラインアレイ', pos: [11.6, 9.8, 2.2], rot: [0, -12, 0], mirror: true }),
      perf(st, 0, '#ff9fd0'), perf(st, -1.7, '#8fe3ff', '#3a2a1a', 0.7), perf(st, 1.7, '#fff19a', '#6a4a2a', 0.7),
      A('mic_stand', { name: 'マイクスタンド', pos: [0, st.height, 2.6] }),
      A('spark', { name: 'コールドスパーク', pos: [3.2, st.height, 3.5], mirror: true, arr: { n: 2, dx: 3 } }),
      A('confetti', { name: '紙吹雪キャノン', pos: [7.2, st.height, 3.3], rot: [0, -20, 0], mirror: true }),
      A('audience', { name: '観客', pos: [0, 0, st.depth / 2 + 4.5], p: { width: 26, depth: 12, penlight: 'cue' } }),
      A('barricade', { name: 'バリケード', pos: [0, 0, st.depth / 2 + 3.6], p: { length: 26 } }),
      A('flower_stand', { name: 'フラスタ', pos: [10.5, 0, st.depth / 2 + 1.2], mirror: true }),
    ];
    return s;
  } },
  cool: { label: 'クール・ブルー', desc: '光の額縁スクリーン、星屑の床、センターサークル', build() {
    const s = baseState(); s.name = 'BLUE STAGE';
    Object.assign(s.stage, { shape: 'apron', width: 16, depth: 8, height: 1.0, floor: 'starry', floorColor: '#0d1838', floorColor2: '#9fd0ff', tile: 1, reflect: true, reflectStrength: 0.55, edgeColor: '#9fd6ff', edgeIntensity: 1.8, front: 'panel', frontColor: '#101a36' });
    s.stage.steps.on = false; Object.assign(s.stage.upper, { on: true, width: 9, depth: 2.4, height: 0.9, stepWidth: 5, stepCount: 3 });
    Object.assign(s.stage.ring, { on: true, radius: 2.2, z: 2.2, color: '#cfeaff', intensity: 2.2, disc: true });
    Object.assign(s.venue, { type: 'dome', color: '#0a1430', floorColor: '#060a18' });
    Object.assign(s.backdrop, { type: 'starframe', c1: '#7fb4ff', c2: '#bfe4ff', c3: '#dff0ff', width: 22, height: 10 });
    s.lights.cues = paletteCues('#bfe4ff', '#7f9cff', '#ffffff'); s.lights.glitterColor = '#d9ecff';
    const st = s.stage;
    s.assets = [
      ...rigBase(st, { hangBeam: 'beam' }),
      A('led_monitor', { name: 'スクリーン（ライブ映像）', pos: [0, 0, -4.75], p: { w: 10.5, h: 5.2, lift: 2.75, src: 'cam1', frame: 'none', mount: 'wall', edge: false, bright: 0.85, density: 9 } }),
      A('led_bar', { name: '光の柱', pos: [5.6, st.height + 0.9, -3.4], mirror: true, arr: { n: 3, dx: 0.7 }, color: '#e8f4ff', p: { length: 3.2, mode: 'static', seg: 16 } }),
      perf(st, 0, '#eef4ff', '#d9c8b8', 2.2),
      A('audience', { name: '観客', pos: [0, 0, 10.5], p: { width: 28, depth: 14, penlight: 'white', gap: 0.8 } }),
      A('smoke', { name: 'ドライアイス', pos: [2.5, st.height, 3.2], mirror: true, p: { kind: 'lowfog', mode: 'always', amount: 0.6 } }),
    ];
    return s;
  } },
  neon: { label: 'ネオン・アニメ', desc: 'ネオン幾何学、グリッド床、八角ステージ', build() {
    const s = baseState(); s.name = 'NEON STAGE';
    Object.assign(s.stage, { shape: 'octagon', width: 16, depth: 9, height: 1.0, floor: 'grid', floorColor: '#0a0e2a', floorColor2: '#58d8ff', tile: 1.4, reflect: true, reflectStrength: 0.4, edgeColor: '#ff6fd8', edgeIntensity: 2.4, front: 'ledgrid', frontColor: '#140a2a', frontGlow: '#ff4fd0' });
    s.stage.steps.on = false; s.stage.ring.on = false;
    Object.assign(s.venue, { type: 'void', color: '#12082b', floorColor: '#0b0618' });
    Object.assign(s.backdrop, { type: 'neon_geo', c1: '#7fe8ff', c2: '#ff6fd8', c3: '#c79bff', pattern: 'dots', bright: 0.9, width: 24, height: 11 });
    s.lights.cues = paletteCues('#7fe8ff', '#ff6fd8', '#c79bff');
    const st = s.stage;
    s.assets = [
      ...rigBase(st, { backN: 2, backDx: 3 }),
      A('spark', { name: 'スパークの柱', pos: [4.2, st.height, 2.6], mirror: true, p: { mode: 'always', rate: 140, height: 3.5 } }),
      perf(st, 1.8, '#f4f0ff', '#e8d8ff', 1), A('performer', { name: '演者ダミー', pos: [-1.8, st.height, 1], color: '#e0f8ff', color2: '#c8e8ff', p: { dance: 'sway' } }),
      A('laser', { name: 'レーザー', pos: [2.4, st.height, -st.depth / 2 + 0.6], mirror: true, p: { count: 8, spread: 70 } }),
      A('led_monitor', { name: 'サイドモニター', pos: [13, 0, -1], rot: [0, -18, 0], mirror: true, p: { w: 4.5, h: 6, lift: 1.4, src: 'cam1', frame: 'bezel', c1: '#ff6fd8' }, color2: '#ff6fd8' }),
      A('audience', { name: '観客', pos: [0, 0, st.depth / 2 + 2.5], p: { width: 26, depth: 12, penlight: 'pastel', body: true } }),
    ];
    return s;
  } },
  arena: { label: 'アリーナ（花道つき）', desc: '大型LEDウォール、花道＋センターステージ、レーザー', build() {
    const s = baseState(); s.name = 'ARENA';
    Object.assign(s.stage, { shape: 'rect', width: 20, depth: 9, height: 1.5, floor: 'gloss', floorColor: '#121218', reflect: true, reflectStrength: 0.5, edgeColor: '#5fe8ff', front: 'panel', frontColor: '#16161e' });
    Object.assign(s.stage.steps, { on: true, pos: 'sides', width: 3, count: 5, sideX: 7.5 });
    Object.assign(s.stage.runway, { on: true, length: 12, width: 3 }); Object.assign(s.stage.sub, { on: true, shape: 'round', size: 7 });
    Object.assign(s.stage.ring, { on: true, radius: 1.8, z: 1.5 });
    Object.assign(s.venue, { type: 'hall', color: '#08080f' });
    Object.assign(s.backdrop, { type: 'led_wall', c1: '#ff9ad5', c2: '#9fd8ff', c3: '#fff3c4', pattern: 'blobs', width: 26, height: 11 });
    s.lights.cues = paletteCues('#ff7fd0', '#b07bff', '#ffffff');
    const st = s.stage;
    s.assets = [
      ...rigBase(st, { backN: 4, backDx: 2.2, hangN: 5, trussY: 10 }),
      A('laser', { name: 'レーザー', pos: [2, st.height, -st.depth / 2 + 0.5], mirror: true, arr: { n: 2, dx: 4 }, p: { count: 10, spread: 60, tilt: 12 } }),
      A('led_monitor', { name: 'サイドモニター（大）', pos: [17, 0, -1.5], rot: [0, -14, 0], mirror: true, p: { w: 6, h: 9, lift: 2.5, src: 'cam2', frame: 'truss', density: 10 } }),
      A('line_array', { name: 'ラインアレイ', pos: [14, 11.5, 2], rot: [0, -15, 0], mirror: true, p: { boxes: 10 } }),
      perf(st, 0, '#ffb3d9'), perf(st, -2.2, '#b8a0ff', '#2a1a1a', 0.5), perf(st, 2.2, '#9fe0ff', '#4a3020', 0.5),
      A('audience', { name: '観客（左右ブロック）', pos: [8.8, 0, st.depth / 2 + 1.5], mirror: true, p: { width: 13, depth: 20, penlight: 'cue' } }),
      A('spark', { name: 'コールドスパーク', pos: [1.9, st.height, st.depth / 2 + 12.5], mirror: true }),
    ];
    return s;
  } },
  outdoor: { label: '野外フェス（昼）', desc: '青空・芝生、パステルLED、黄色いスピーカー', build() {
    const s = baseState(); s.name = 'OUTDOOR FES';
    Object.assign(s.stage, { shape: 'rect', width: 18, depth: 7, height: 1.4, floor: 'gloss', floorColor: '#e9e9f0', reflect: false, edge: false, front: 'plain', frontColor: '#26262e' });
    s.stage.steps.on = false; s.stage.ring.on = false;
    Object.assign(s.venue, { type: 'outdoor_day' });
    Object.assign(s.backdrop, { type: 'truss_led', c1: '#ffc8e4', c2: '#bfe9ff', c3: '#fff3a8', pattern: 'stripes', bright: 1.3, width: 24, height: 10 });
    s.lights.cues = paletteCues('#ffd1e8', '#bfe9ff', '#fff3a8'); s.lights.glitter = false; s.lights.bloom = 0.5; s.lights.exposure = 0.95;
    const st = s.stage;
    s.assets = [
      ...rigBase(st, { backN: 2, backDx: 3, hangN: 3, hangBeam: 'wash' }),
      A('led_monitor', { name: 'メインモニター', pos: [0, 0, -3.6], p: { w: 8, h: 4.5, lift: 2.4, src: 'cam1', frame: 'bezel', mount: 'wall' } }),
      A('speaker_stack', { name: 'スピーカー', pos: [5.6, st.height, -2.6], mirror: true, p: { height: 2.6 } }),
      A('led_bar', { name: 'フットライト', pos: [0, st.height, st.depth / 2 - 0.15], color: '#ffffff', p: { length: 16, vertical: false, seg: 32, mode: 'chase' } }),
      perf(st, 0, '#ff9fd0'), perf(st, -1.8, '#9fdcff', '#3a2a1a', 0.3), perf(st, 1.8, '#fff19a', '#6a4a2a', 0.3),
      A('audience', { name: '観客', pos: [0, 0, st.depth / 2 + 4], p: { width: 30, depth: 16, penlight: 'pastel', gap: 0.8 } }),
      A('confetti', { name: '紙吹雪キャノン', pos: [8, st.height, 3], rot: [0, -20, 0], mirror: true }),
    ];
    return s;
  } },
  party: { label: 'パーティ／DJ', desc: 'スピーカーウォール、LEDフロア、ミラーボール', build() {
    const s = baseState(); s.name = 'PARTY';
    Object.assign(s.stage, { shape: 'round', width: 18, depth: 9, height: 1.2, floor: 'led', tile: 1, reflect: true, reflectStrength: 0.35, edgeColor: '#c87bff', front: 'ledgrid', frontColor: '#1a0d33', frontGlow: '#b06bff' });
    Object.assign(s.stage.steps, { on: true, pos: 'center', width: 6, count: 4, led1: '#c87bff', led2: '#ff6fd8' }); s.stage.ring.on = false;
    Object.assign(s.venue, { type: 'hall', color: '#1b0f38' });
    Object.assign(s.backdrop, { type: 'speaker_wall', c1: '#b06bff', c2: '#ff6fd8', c3: '#7fe8ff', pattern: 'bars', width: 24, height: 10 });
    s.lights.cues = paletteCues('#b06bff', '#ff6fd8', '#7fe8ff');
    const st = s.stage;
    s.assets = [
      ...rigBase(st, { backN: 3, backZ: -st.depth / 2 + 0.6 }),
      A('mirror_ball', { name: 'ミラーボール', pos: [0, 9.5, 1.5] }),
      A('dj_booth', { name: 'DJブース', pos: [0, st.height, -1.6] }),
      A('laser', { name: 'レーザー', pos: [3, st.height, -st.depth / 2 + 0.5], mirror: true, p: { follow: false, fixed: '#c46bff', count: 12 } }),
      A('strobe', { name: 'ストロボ', pos: [7.5, st.height, 0], rot: [0, -30, 0], mirror: true }),
      A('text_panel', { name: 'ロゴ', pos: [0, 10.4, -st.depth / 2 - 0.5], color: '#ff6fd8', p: { text: 'PARTY!', height: 1.3 } }),
      perf(st, 0, '#ff9fd0', '#2a1a3a', 0.6),
      A('confetti', { name: '紙吹雪キャノン', pos: [6, st.height, 2.6], rot: [0, -25, 0], mirror: true }),
      A('smoke', { name: 'CO2', pos: [4.2, st.height, 2.8], mirror: true, p: { kind: 'co2' } }),
      A('audience', { name: '観客', pos: [0, 0, st.depth / 2 + 3.5], p: { width: 26, depth: 12, penlight: 'rainbow' } }),
    ];
    return s;
  } },
  blank: { label: '空のステージ', desc: 'ステージ本体だけから始める', build() {
    const s = baseState(); s.name = 'NEW STAGE'; s.backdrop.type = 'none'; s.venue.type = 'void'; s.assets = []; return s;
  } },
};
function applyPreset(k) {
  const p = PRESETS[k]; if (!p) return;
  const keepPlay = S.lights.playing;
  const keepVJ = S.vj;   // VJ の設定（画像バンク・シーン）はステージのひな形と関係ないので残す
  S = normalizeState(p.build()); S.lights.playing = keepPlay; SEL.id = null; LT.demoIdx = null;
  if (keepVJ) S.vj = keepVJ;
  rebuildEverything(); refreshAllUI(); commit();
  toast(`プリセット「${p.label}」にしました（Ctrl+Z で戻せます）`, 3000);
}

/* ---------- 画質 ---------- */
const QUALITY_PRESETS = {
  low: { reflect: false, reflectScale: 0.35, pixelRatio: 1, camEvery: 2 },
  mid: { reflect: true, reflectScale: 0.5, pixelRatio: Math.min(devicePixelRatio, 1.25), camEvery: 1 },
  high: { reflect: true, reflectScale: 0.8, pixelRatio: Math.min(devicePixelRatio, 2), camEvery: 1 },
};
function applyQuality() {
  Object.assign(QUALITY, QUALITY_PRESETS[QUALITY.level] || QUALITY_PRESETS.mid);
  renderer.setPixelRatio(QUALITY.pixelRatio); composer.setPixelRatio(QUALITY.pixelRatio); onResize();
}
function onResize() {
  const w = Math.max(1, viewEl.clientWidth), h = Math.max(1, viewEl.clientHeight);
  renderer.setSize(w, h); composer.setSize(w, h); camera.aspect = w / h; camera.updateProjectionMatrix();
  placeRemote(); placeVJRemote();
}
new ResizeObserver(onResize).observe(viewEl);

/* ---------- ループ ---------- */
const clock = new THREE.Clock(); let lastBeatInt = -1; let beatDot = null;
function frame() {
  requestAnimationFrame(frame);
  const dt = Math.min(clock.getDelta(), 0.1);
  try {
    processDirty();
    if (REG_DIRTY) refreshRegistry();
    scene.updateMatrixWorld();
    updateLights(dt);
    vjFrame(dt);   // VJ 映像（必要なときだけ作る）→ LED モニター・背景 LED に貼る
    if (VJOUT.full) {
      // VJ 出力モード：ステージは描かず、VJ 映像だけを全画面に
      vjRenderFull();
      if (SHOT.req) { SHOT.req = false; const a = h('a', { href: renderer.domElement.toDataURL('image/png'), download: `${safeName()}_vj.png` }); a.click(); }
    } else {
      updateStageCams();
      updateViewTween(dt);
      if (VIEW.mode !== 'free') {
        const sc = SCAMS[VIEW.mode === 'cam1' ? 0 : 1];
        camera.position.copy(sc.cam.position); camera.quaternion.copy(sc.cam.quaternion); camera.fov = sc.cam.fov; camera.updateProjectionMatrix();
      } else orbit.update();
      renderStageCams();
      composer.render(dt);
      if (SHOT.req) { SHOT.req = false; const a = h('a', { href: renderer.domElement.toDataURL('image/png'), download: `${safeName()}.png` }); a.click(); toast('スクリーンショットを保存しました'); }
      renderPIP();
      renderVJPreview();
    }
    vjUITick();
  } catch (e) { console.error(e); if (!frame.err) { frame.err = true; toast('エラー: ' + e.message, 6000); } }
  if (!beatDot || !beatDot.isConnected) beatDot = $('#beatDot'); const bi = Math.floor(LT.beat); if (beatDot && bi !== lastBeatInt) { lastBeatInt = bi; beatDot.classList.add('on'); setTimeout(() => beatDot.classList.remove('on'), 90); }
}

function init() {
  try { const q = localStorage.getItem('stagekobo.quality'); if (q && QUALITY_PRESETS[q]) QUALITY.level = q; } catch (e) {}
  Object.assign(QUALITY, QUALITY_PRESETS[QUALITY.level]); renderer.setPixelRatio(QUALITY.pixelRatio); composer.setPixelRatio(QUALITY.pixelRatio);
  onResize();
  initStageCams(); buildHUD(); applySnap();
  const saved = loadLocal();
  if (saved && saved.state) {
    Object.assign(IMAGES, saved.images || {}); Object.assign(BLOBS, saved.blobs || {});
    S = normalizeState(saved.state); toast('前回の続きから開きました（ステージタブのプリセットで作り直せます）', 3500);
  } else { S = normalizeState(PRESETS.pop.build()); toast('ようこそ！まずはデモ再生を眺めてみてください', 3500); }
  rebuildEverything(); processDirty(); refreshRegistry();
  renderTabs(); buildRemote(); buildVJRemote(); commit(); updateViewBtns();
  const D = S.stage.depth; camera.position.set(0, 5.2, D / 2 + 21); orbit.target.set(0, S.stage.height + 2.5, 0);
  $('#loading').remove();
  frame();
}
// デバッグ・自動化用の窓口（ブラウザのコンソールから触れる）
window.STAGEKOBO = { get state() { return S; }, surfStats: () => ({ ...SURF_STATS, cached: SURF_CACHE.size }), setCue, recallScene, setLive, triggerFx, applyPreset, addAsset, select, exportUnity: buildUnityExport, markDirty, setView, camera, orbit, RT_ASSETS, REG, LT, SCAMS, commit, normalize: (s) => normalizeState(s),
  vj: { VJP, VJRT, VJOUT, VJ_HELD, get engine() { return VJ_MAIN; }, trigger: vjTrigger, fade: vjFade, preset: (i) => { vjApplyPreset(VJ_PRESETS[i]); buildVJRemote(); }, open: toggleVJRemote, full: vjEnterFull, exitFull: vjExitFull, openWindow: vjOpenWindow, closeWindow: vjCloseWindow, hold: VJ_HOLD, ui: () => buildVJRemote() } };
init();
