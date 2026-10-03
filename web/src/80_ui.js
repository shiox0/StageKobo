
/* =====================================================================
   状態管理（dirty / Undo / 自動保存）・選択とギズモ・UI
   ===================================================================== */
let S = baseState();
const VIEW = { mode: 'free', tween: null };
const SEL = { id: null };
let currentTab = 'stage';
const LS_KEY = 'stagekobo.v1';
const r3 = v => Math.round(v * 1000) / 1000, r1 = v => Math.round(v * 10) / 10;
const fmt = v => (typeof v === 'number' ? (Math.abs(v) >= 100 ? v.toFixed(0) : Math.round(v * 100) / 100) : v);

/* ---------- dirty ---------- */
const DIRTY = new Set();
function markDirty(k) { DIRTY.add(k); }
function processDirty() {
  if (!DIRTY.size) return;
  const d = new Set(DIRTY); DIRTY.clear();
  if (d.has('quality')) applyQuality();
  if (d.has('venue')) buildVenue();
  if (d.has('stage') || d.has('quality')) { buildStage(); buildGlitter(); d.add('backdrop'); }
  if (d.has('backdrop')) buildBackdrop();
  if (d.has('assets')) rebuildAllAssets();
  else for (const k of d) if (k.startsWith('asset:')) { const inst = findInst(k.slice(6)); if (inst) buildInstance(inst); }
  if (d.has('levels')) applyLightLevels();
  reattachGizmo(); updateSelBox();
}
function rebuildEverything() { ['venue', 'stage', 'backdrop', 'assets', 'levels'].forEach(markDirty); }

/* ---------- Undo / Redo ---------- */
const HIST = { stack: [], idx: -1, max: 100 };
/* 演出の操作（リモコン）は「編集」ではないので Undo 履歴に入れない。履歴は編集内容だけを比べる */
const PERF_KEYS = ['live', 'cue', 'demo', 'liveEdited', 'blackout', 'speedMul', 'monitorMode', 'monitorMode2', 'playing', 'bpm'];
function editSnapshot() {
  const keep = {}; for (const k of PERF_KEYS) { keep[k] = S.lights[k]; delete S.lights[k]; }
  const vj = S.vj; delete S.vj;   // VJ リモコンも演出の操作なので履歴に入れない
  const s = JSON.stringify(S); Object.assign(S.lights, keep); S.vj = vj; return s;
}
function commit() {
  const s = editSnapshot();
  if (HIST.stack[HIST.idx] === s) { scheduleSave(); return; }
  HIST.stack.splice(HIST.idx + 1); HIST.stack.push(s);
  if (HIST.stack.length > HIST.max) HIST.stack.shift();
  HIST.idx = HIST.stack.length - 1; scheduleSave(); updateEditBtns();
}
function restore(str) {
  const perf = {}; for (const k of PERF_KEYS) perf[k] = S.lights[k]; const vj = S.vj;
  S = JSON.parse(str); Object.assign(S.lights, perf); S.vj = vj;   // いま出ている演出（照明・VJ）は戻さない
  S.lights.cue = clamp(S.lights.cue, 0, S.lights.cues.length - 1);
  rebuildEverything(); if (SEL.id && !findInst(SEL.id)) SEL.id = null;
  refreshAllUI(); scheduleSave(); updateEditBtns();
}
function refreshAllUI() { renderTab(); buildRemote(); buildVJRemote(); }
function undo() { if (HIST.idx > 0) { HIST.idx--; restore(HIST.stack[HIST.idx]); toast('元に戻しました'); } }
function redo() { if (HIST.idx < HIST.stack.length - 1) { HIST.idx++; restore(HIST.stack[HIST.idx]); toast('やり直しました'); } }

/* ---------- 自動保存 ---------- */
let saveTimer = 0, lastSave = '';
function scheduleSave() { clearTimeout(saveTimer); saveTimer = setTimeout(saveLocal, 700); }
function saveLocal() {
  try { localStorage.setItem(LS_KEY, JSON.stringify({ v: 1, state: S, images: IMAGES, blobs: BLOBS })); lastSave = '自動保存しました（画像・モデル込み）'; }
  catch (e) {
    try { localStorage.setItem(LS_KEY, JSON.stringify({ v: 1, state: S, images: {}, blobs: {} })); lastSave = '自動保存しました（容量の都合で画像・GLBは除外。JSON保存で全部残せます）'; }
    catch (e2) { lastSave = '自動保存できませんでした（ブラウザの保存領域が使えません）'; }
  }
  const el = $('#saveStatus'); if (el) el.textContent = lastSave;
}
function loadLocal() { try { const s = localStorage.getItem(LS_KEY); return s ? JSON.parse(s) : null; } catch (e) { return null; } }
function normalizeState(raw) {
  const st = deepMerge(baseState(), raw || {});
  st.assets = (st.assets || []).filter(a => a && ASSET_DEFS[a.type]).map(a => makeInst(a.type, a));
  const dc = defaultCues()[0];
  st.lights.cues = (st.lights.cues && st.lights.cues.length ? st.lights.cues : defaultCues()).map(c => deepMerge(dc, c));
  st.lights.cue = clamp(st.lights.cue | 0, 0, st.lights.cues.length - 1);
  // v0.1 には live が無い → 選ばれていたシーンから作る
  st.lights.live = deepMerge(dc, (raw && raw.lights && raw.lights.live) || st.lights.cues[st.lights.cue]);
  if (!(raw && raw.lights && raw.lights.live)) st.lights.liveEdited = false;
  // v0.7 まで：モニターの切り替えは1つ（全部）→ サブも同じにする
  if (raw && raw.lights && raw.lights.monitorMode && !raw.lights.monitorMode2) st.lights.monitorMode2 = raw.lights.monitorMode;
  for (const k of ['cam1', 'cam2']) { const t = Math.round(+st.cams[k].target); st.cams[k].target = isFinite(t) && t >= 0 ? t : -1; }
  // VJ：壊れた値・知らない映像の名前を直す
  const V = st.vj;
  if (!Array.isArray(V.images)) V.images = [];
  if (!Array.isArray(V.slots)) V.slots = [];
  V.slots = Array.from({ length: 8 }, (_, i) => V.slots[i] || null);
  // 合成（v0.6〜0.7）は無くなった → トンネルに
  for (const k of ['a', 'b']) { if (!V[k] || typeof V[k] !== 'object') V[k] = baseVJ()[k]; if (!(V[k].gen in VJ_GEN_ID)) V[k].gen = 'tunnel'; delete V[k].mix; }
  for (const o of V.slots) if (o) for (const k of ['a', 'b']) if (o[k]) { if (!(o[k].gen in VJ_GEN_ID)) o[k].gen = 'tunnel'; delete o[k].mix; }
  V.xf = clamp(+V.xf || 0, 0, 1); V.black = false;   // 開いたときに真っ暗だと迷うので暗転は戻す
  return st;
}
function loadProjectData(data, msg = '読み込みました') {
  if (!data || !data.state) { toast('このファイルは読み込めませんでした'); return; }
  Object.assign(IMAGES, data.images || {}); Object.assign(BLOBS, data.blobs || {});
  TEXCACHE.clear(); glbCache.clear();
  const keepVJ = S.vj;
  S = normalizeState(data.state); SEL.id = null;
  if (!data.state.vj && keepVJ) S.vj = keepVJ;   // v0.4 以前のファイル：VJ の設定はいまのものを残す
  rebuildEverything(); refreshAllUI(); commit(); toast(msg);
}

/* ---------- ギズモ・選択 ---------- */
const tcontrols = new TransformControls(camera, renderer.domElement);
tcontrols.setSize(0.85); scene.add(tcontrols);
let snapOn = false;
function applySnap() { tcontrols.setTranslationSnap(snapOn ? 0.25 : null); tcontrols.setRotationSnap(snapOn ? 15 * DEG : null); tcontrols.setScaleSnap(snapOn ? 0.1 : null); }
tcontrols.addEventListener('dragging-changed', e => { orbit.enabled = !e.value; if (!e.value) commit(); });
tcontrols.addEventListener('objectChange', () => {
  const inst = findInst(SEL.id); const o = tcontrols.object; if (!inst || !o) return;
  inst.pos = [o.position.x, o.position.y, o.position.z].map(r3);
  inst.rot = [o.rotation.x / DEG, o.rotation.y / DEG, o.rotation.z / DEG].map(r1);
  inst.scale = [o.scale.x, o.scale.y, o.scale.z].map(r3);
  layoutInstance(inst); updateSelBox(); refreshInspectorFields();
});
const selBox = new THREE.Box3Helper(new THREE.Box3(), 0x7ff5d6); selBox.visible = false; G.helpers.add(selBox);
function visibleChain(o) { while (o) { if (!o.visible) return false; o = o.parent; } return true; }
function boundsOf(root, out) {
  out.makeEmpty(); const tb = new THREE.Box3(); root.updateMatrixWorld(true);
  root.traverse(o => {
    if (!o.isMesh || o.userData.fx || !visibleChain(o)) return;
    if (o.isInstancedMesh) { o.computeBoundingBox(); tb.copy(o.boundingBox); } else { if (!o.geometry.boundingBox) o.geometry.computeBoundingBox(); tb.copy(o.geometry.boundingBox); }
    out.union(tb.applyMatrix4(o.matrixWorld));
  });
  return out;
}
function updateSelBox() {
  const rt = SEL.id && RT_ASSETS.get(SEL.id);
  if (!rt) { selBox.visible = false; return; }
  boundsOf(rt.root, selBox.box); selBox.visible = !selBox.box.isEmpty();
  if (selBox.visible) selBox.box.expandByScalar(0.05);
}
function reattachGizmo() {
  const inst = SEL.id && findInst(SEL.id); const rt = inst && RT_ASSETS.get(SEL.id);
  if (rt && rt.units[0] && inst.visible !== false) { if (tcontrols.object !== rt.units[0].obj) tcontrols.attach(rt.units[0].obj); }
  else tcontrols.detach();
}
function select(id, fromView = false) {
  SEL.id = id && findInst(id) ? id : null;
  reattachGizmo(); updateSelBox();
  if (SEL.id && fromView && currentTab !== 'assets') { currentTab = 'assets'; renderTabs(); }
  if (currentTab === 'assets' || currentTab === 'cams') renderTab();
}
const raycaster = new THREE.Raycaster(); const ndc = new THREE.Vector2(); let downAt = null;
renderer.domElement.addEventListener('pointerdown', e => { downAt = [e.clientX, e.clientY]; });
renderer.domElement.addEventListener('pointerup', e => {
  if (!downAt || e.button !== 0) return; const mv = Math.hypot(e.clientX - downAt[0], e.clientY - downAt[1]); downAt = null;
  if (mv > 5 || tcontrols.dragging || tcontrols.axis) return;
  if (VIEW.mode !== 'free' || VJOUT.full) return;   // VJ 出力モード中は見えないステージを選ばない
  const r = renderer.domElement.getBoundingClientRect();
  ndc.set((e.clientX - r.left) / r.width * 2 - 1, -(e.clientY - r.top) / r.height * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  const hits = raycaster.intersectObjects(G.assets.children, true);
  for (const h of hits) { if (!visibleChain(h.object)) continue; const id = h.object.userData.instId; if (id) { select(id, true); return; } }
  select(null);
});

/* ---------- アセット操作 ---------- */
function addAsset(type, over = {}) {
  const d = ASSET_DEFS[type];
  const inst = makeInst(type, { pos: d.place ? d.place() : [0, S.stage.height, 0], ...over });
  const same = S.assets.filter(a => a.type === type).length; if (same && !over.name) inst.name = d.label + ' ' + (same + 1);
  S.assets.push(inst); buildInstance(inst); select(inst.id); commit();
  toast(`「${inst.name}」を追加しました（ギズモで動かせます）`);
  return inst;
}
function duplicateAsset(id) {
  const a = findInst(id); if (!a) return;
  const c = deepClone(a); c.id = uid(a.type.slice(0, 4)); c.name = a.name + ' のコピー'; c.pos = [a.pos[0] + 1, a.pos[1], a.pos[2] + 0.5];
  S.assets.splice(S.assets.indexOf(a) + 1, 0, c); buildInstance(c); select(c.id); commit(); toast('複製しました');
}
function deleteAsset(id) {
  const i = S.assets.findIndex(a => a.id === id); if (i < 0) return;
  const nm = S.assets[i].name; disposeInstance(id); S.assets.splice(i, 1);
  if (SEL.id === id) select(null); commit(); renderTab(); toast(`「${nm}」を削除しました（Ctrl+Zで戻せます）`);
}
function dropToFloor(id) {
  const a = findInst(id); if (!a) return;
  a.pos[1] = r3(floorHeightAt(a.pos[0], a.pos[2])); layoutInstance(a); updateSelBox(); refreshInspectorFields(); commit();
}
function focusOn(id) {
  const rt = RT_ASSETS.get(id); if (!rt) return; const b = boundsOf(rt.root, new THREE.Box3()); if (b.isEmpty()) return;
  const c = b.getCenter(V3()); const s = b.getSize(V3()).length();
  tweenView(c.clone().add(V3(0, s * 0.35 + 1, s * 1.1 + 3)), c);
}

/* ---------- 小さなUI部品 ---------- */
function h(tag, props = {}, ...kids) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (k === 'class') e.className = v; else if (k === 'style') e.style.cssText = v; else if (k === 'html') e.innerHTML = v;
    else if (k.startsWith('on') && typeof v === 'function') e.addEventListener(k.slice(2), v); else e.setAttribute(k, v);
  }
  for (const c of kids.flat()) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
}
const OPEN_SECS = {};
function section(parent, title, open = true, key = title) {
  const d = h('details', { class: 'sec' }); d.open = OPEN_SECS[key] ?? open;
  d.addEventListener('toggle', () => { OPEN_SECS[key] = d.open; });
  d.append(h('summary', {}, title)); const b = h('div', { class: 'body' }); d.append(b); parent.append(d); return b;
}
function note(parent, html) { parent.append(h('div', { class: 'note', html })); }
function row(parent, label, ...els) { const r = h('div', { class: 'row' }, h('label', { title: label }, label), ...els); parent.append(r); return r; }
function uiRange(parent, label, obj, key, min, max, step, onInput) {
  const r = h('input', { type: 'range', min, max, step }); r.value = obj[key];
  const n = h('input', { type: 'number', min, max, step }); n.value = fmt(obj[key]);
  r.addEventListener('input', () => { obj[key] = +r.value; n.value = fmt(+r.value); onInput && onInput(); });
  r.addEventListener('change', () => commit());
  n.addEventListener('change', () => { let v = parseFloat(n.value); if (isNaN(v)) v = obj[key]; obj[key] = v; r.value = v; onInput && onInput(); commit(); });
  return row(parent, label, r, n);
}
function uiColor(parent, label, obj, key, onInput) {
  const c = h('input', { type: 'color' }); c.value = obj[key] || '#ffffff'; const hx = h('span', { class: 'hex' }, c.value);
  c.addEventListener('input', () => { obj[key] = c.value; hx.textContent = c.value; onInput && onInput(); });
  c.addEventListener('change', () => commit());
  return row(parent, label, c, hx);
}
function uiSelect(parent, label, obj, key, opts, onChange) {
  const s = h('select'); for (const [v, l] of opts) { const o = h('option', { value: v }, l); if (String(obj[key]) === String(v)) o.selected = true; s.append(o); }
  s.addEventListener('change', () => { const v = s.value; obj[key] = (typeof obj[key] === 'number') ? +v : v; onChange && onChange(); commit(); });
  return row(parent, label, s);
}
function uiCheck(parent, label, obj, key, onChange) {
  const c = h('input', { type: 'checkbox' }); c.checked = !!obj[key];
  c.addEventListener('change', () => { obj[key] = c.checked; onChange && onChange(); commit(); });
  const r = row(parent, label, c);
  const lb = r.querySelector('label'); if (lb) { lb.style.cursor = 'pointer'; lb.addEventListener('click', () => c.click()); }   // 文字を押しても切り替わる
  return r;
}
function uiText(parent, label, obj, key, onChange) {
  const t = h('input', { type: 'text' }); t.value = obj[key] ?? '';
  t.addEventListener('input', () => { obj[key] = t.value; onChange && onChange(true); });
  t.addEventListener('change', () => { onChange && onChange(false); commit(); });
  return row(parent, label, t);
}
function btns(parent, list) { const b = h('div', { class: 'btns' }); for (const [l, fn, cls, title] of list) b.append(h('button', { class: 'btn ' + (cls || ''), onclick: fn, title: title || '' }, l)); parent.append(b); return b; }
function pickFile(accept) {
  return new Promise(res => { const i = h('input', { type: 'file', accept }); i.addEventListener('change', () => res(i.files[0] || null)); i.click(); });
}
const readAsDataURL = f => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(f); });
const readAsText = f => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsText(f); });
async function uploadImage() {
  const f = await pickFile('image/*'); if (!f) return null;
  const id = uid('img'); IMAGES[id] = await readAsDataURL(f); return id;
}
function download(name, data, mime) {
  const blob = data instanceof Blob ? data : new Blob([data], { type: mime });
  const a = h('a', { href: URL.createObjectURL(blob), download: name }); document.body.append(a); a.click();
  setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1500);
}

/* ---------- タブ ---------- */
const TABS = [['stage', 'ステージ'], ['bg', '背景・会場'], ['assets', 'アセット'], ['lights', 'ライト演出'], ['cams', 'カメラ・モニター'], ['io', '保存・書き出し']];
function renderTabs() {
  const nav = $('#tabs'); nav.innerHTML = '';
  for (const [k, l] of TABS) nav.append(h('button', { class: currentTab === k ? 'on' : '', onclick: () => { currentTab = k; renderTabs(); } }, l));
  renderTab();
}
function renderTab() {
  const body = $('#tabBody'); const sc = body.scrollTop; body.innerHTML = ''; INSP.fields = [];
  (TAB_RENDER[currentTab] || (() => {}))(body);
  body.scrollTop = sc;
}
const INSP = { fields: [] };
function refreshInspectorFields() { for (const f of INSP.fields) if (document.activeElement !== f.el) f.el.value = fmt(f.arr[f.i]); }
function xyzRow(parent, label, arr, step, onInput) {
  const w = h('div', { class: 'xyz' });
  ['X', 'Y', 'Z'].forEach((ax, i) => {
    const n = h('input', { type: 'number', step, title: ax }); n.value = fmt(arr[i]);
    n.addEventListener('input', () => { const v = parseFloat(n.value); if (!isNaN(v)) { arr[i] = v; onInput(); } });
    n.addEventListener('change', () => commit());
    INSP.fields.push({ el: n, arr, i }); w.append(n);
  });
  return row(parent, label, w);
}

const TAB_RENDER = {
  stage(body) {
    const st = S.stage; const D = () => markDirty('stage');
    const pre = section(body, 'シーンプリセット（まるごと差し替え）');
    note(pre, '雰囲気ごとのひな形です。押すと<b>今のシーンが置き換わります</b>（Ctrl+Z で戻せます）。');
    const pb = h('div', { class: 'palette' });
    for (const [k, p] of Object.entries(PRESETS)) pb.append(h('button', { class: 'btn', title: p.desc, onclick: () => applyPreset(k) }, p.label));
    pre.append(pb);
    const sh = section(body, '形と大きさ');
    uiSelect(sh, '形', st, 'shape', [['rect', '四角'], ['octagon', '角落とし（八角）'], ['apron', '前に半円の張り出し'], ['round', '半円（D形）']], D);
    uiRange(sh, '幅 (m)', st, 'width', 4, 40, 0.5, D);
    uiRange(sh, '奥行 (m)', st, 'depth', 2, 24, 0.5, D);
    uiRange(sh, '高さ (m)', st, 'height', 0.2, 4, 0.05, D);
    note(sh, '背景はステージの奥に自動で付いてきます。ライトやモニターなどのアセットは絶対位置なので、大きさを変えたら位置を調整してください。');
    const fl = section(body, '床');
    uiSelect(fl, '床の種類', st, 'floor', [['gloss', 'つやつや単色'], ['mirror', '鏡面'], ['checker', 'チェッカー（ひし形）'], ['grid', 'ネオングリッド'], ['led', 'LEDフロア（拍で変化）'], ['starry', 'キラキラ星屑'], ['wood', '木目']], D);
    uiColor(fl, '色1', st, 'floorColor', D); uiColor(fl, '色2（模様・光）', st, 'floorColor2', D);
    uiRange(fl, '模様の大きさ', st, 'tile', 0.25, 4, 0.05, D);
    uiCheck(fl, '床の映り込み', st, 'reflect', D);
    uiRange(fl, '映り込みの強さ', st, 'reflectStrength', 0, 1, 0.01, D);
    note(fl, '映り込みは見た目が良くなる代わりに重くなります。「保存・書き出し」タブの画質「低」では自動でオフになります。');
    const ed = section(body, 'ふちの光・前面', false);
    uiCheck(ed, 'エッジライト', st, 'edge', D); uiColor(ed, 'エッジの色', st, 'edgeColor', D); uiRange(ed, 'エッジの明るさ', st, 'edgeIntensity', 0, 6, 0.05, D);
    uiCheck(ed, '下のふちも光る', st, 'edgeBottom', D);
    uiSelect(ed, '前面の見た目', st, 'front', [['panel', 'パネル'], ['skirt', 'ステージ幕（ひだ）'], ['ledgrid', 'LEDグリッド（光る）'], ['mirror', '鏡面（ヘアライン）'], ['plain', '無地']], D);
    uiColor(ed, '前面の色', st, 'frontColor', D); uiColor(ed, 'グリッドの光', st, 'frontGlow', D); uiColor(ed, '本体（天板の裏など）', st, 'bodyColor', D);
    const sp = section(body, '正面階段', false); const s2 = st.steps;
    uiCheck(sp, '階段をつける', s2, 'on', D); uiSelect(sp, '位置', s2, 'pos', [['center', '中央に1つ'], ['sides', '左右に2つ']], D);
    uiRange(sp, '幅', s2, 'width', 1, 30, 0.1, D); uiRange(sp, '段数', s2, 'count', 1, 12, 1, D); uiRange(sp, '踏み面', s2, 'tread', 0.2, 1, 0.01, D);
    uiRange(sp, '左右の位置', s2, 'sideX', 0, 20, 0.1, D); uiColor(sp, '階段の色', s2, 'color', D);
    uiCheck(sp, 'LEDドット', s2, 'led', D); uiColor(sp, 'LED色1', s2, 'led1', D); uiColor(sp, 'LED色2', s2, 'led2', D);
    const up = section(body, 'ひな壇（奥の上段）', false); const u = st.upper;
    uiCheck(up, 'ひな壇をつける', u, 'on', D); uiRange(up, '幅', u, 'width', 1, 40, 0.1, D); uiRange(up, '奥行', u, 'depth', 0.5, 10, 0.1, D); uiRange(up, '高さ', u, 'height', 0.2, 4, 0.05, D);
    uiRange(up, '階段の幅', u, 'stepWidth', 0.6, 20, 0.1, D); uiRange(up, '階段の段数', u, 'stepCount', 1, 10, 1, D);
    const rw = section(body, '花道・サブステージ', false);
    uiCheck(rw, '花道をつける', st.runway, 'on', D); uiRange(rw, '花道の長さ', st.runway, 'length', 1, 40, 0.5, D); uiRange(rw, '花道の幅', st.runway, 'width', 0.8, 8, 0.1, D);
    uiCheck(rw, 'サブステージ', st.sub, 'on', D); uiSelect(rw, 'サブの形', st.sub, 'shape', [['round', '円'], ['rect', '四角']], D); uiRange(rw, 'サブの大きさ', st.sub, 'size', 2, 16, 0.1, D);
    note(rw, '花道なしでサブステージだけをつけると、離れ小島（センターステージ）になります。');
    const rg = section(body, 'センターサークル', false);
    uiCheck(rg, 'サークルをつける', st.ring, 'on', D); uiRange(rg, '半径', st.ring, 'radius', 0.5, 6, 0.05, D); uiRange(rg, '前後の位置', st.ring, 'z', -10, 10, 0.05, D);
    uiColor(rg, '色', st.ring, 'color', D); uiRange(rg, '明るさ', st.ring, 'intensity', 0, 4, 0.05, D); uiCheck(rg, '内側をぼんやり光らせる', st.ring, 'disc', D);
  },
  bg(body) {
    const v = S.venue, b = S.backdrop; const DV = () => markDirty('venue'), DB = () => markDirty('backdrop');
    const vs = section(body, '会場');
    uiSelect(vs, '会場の種類', v, 'type', VENUE_TYPES, DV);
    uiColor(vs, '会場の色', v, 'color', DV); uiColor(vs, '地面の色', v, 'floorColor', DV);
    uiCheck(vs, '星（天井・夜空）', v, 'stars', DV); uiCheck(vs, '奥のひな壇席', v, 'seats', DV); uiCheck(vs, '遠くの客席の光', v, 'crowdDots', DV);
    const bs = section(body, '背景（バックドロップ）');
    uiSelect(bs, '背景の種類', b, 'type', BACKDROP_TYPES, () => { DB(); renderTab(); });
    uiRange(bs, '幅', b, 'width', 6, 60, 0.5, DB); uiRange(bs, '高さ', b, 'height', 3, 30, 0.5, DB);
    uiRange(bs, '前後の位置', b, 'z', -10, 6, 0.1, DB); uiRange(bs, '全体の拡大', b, 'scale', 0.3, 3, 0.01, DB);
    uiColor(bs, '色1（メイン）', b, 'c1', DB); uiColor(bs, '色2（アクセント）', b, 'c2', DB); uiColor(bs, '色3', b, 'c3', DB);
    uiSelect(bs, 'LEDパネルの映像', b, 'pattern', PATTERN_LIST, DB); uiRange(bs, 'LEDの明るさ', b, 'bright', 0, 4, 0.05, DB);
    btns(bs, [['画像を読み込んで背景にする', async () => { const id = await uploadImage(); if (!id) return; b.image = id; b.type = 'image'; DB(); commit(); renderTab(); toast('背景を画像に差し替えました'); }, 'pri']]);
    note(bs, '<b>差し替えの考え方：</b>背景は「種類」を変えるだけで丸ごと入れ替わります。自分で作ったモデルを置きたいときは「アセット」タブの「自作モデル（GLB）」を使ってください。');
  },
  assets(body) {
    const add = section(body, 'アセットを追加', !SEL.id, 'addAsset');
    note(add, 'ボタンを押すと、それっぽい位置に置かれます。置いたあとは画面上のギズモ（矢印）で動かせます。');
    for (const cat of ASSET_CATS) {
      const defs = Object.values(ASSET_DEFS).filter(d => d.cat === cat); if (!defs.length) continue;
      add.append(h('div', { class: 'catTitle' }, cat));
      const pal = h('div', { class: 'palette' });
      for (const d of defs) {
        if (d.type === 'glb') pal.append(h('button', { class: 'btn', onclick: async () => { const f = await pickFile('.glb,.gltf,model/gltf-binary'); if (!f) return; const id = uid('glb'); BLOBS[id] = { name: f.name, dataURL: await readAsDataURL(f) }; addAsset('glb', { p: { file: id }, name: f.name.replace(/\.(glb|gltf)$/i, '') }); } }, 'GLBを読み込む…'));
        else pal.append(h('button', { class: 'btn', onclick: () => addAsset(d.type) }, d.label));
      }
      add.append(pal);
    }
    const ls = section(body, `配置済み（${S.assets.length}）`, true, 'assetList');
    const list = h('div', { class: 'list' });
    for (const a of S.assets) {
      const eye = h('span', { class: 'eye' + (a.visible === false ? ' off' : ''), title: '表示／非表示' }, a.visible === false ? '○' : '●');
      eye.addEventListener('click', e => { e.stopPropagation(); a.visible = a.visible === false; layoutInstance(a); REG_DIRTY = true; reattachGizmo(); updateSelBox(); commit(); renderTab(); });
      const extra = (a.mirror ? '・左右' : '') + ((a.arr?.n ?? 1) > 1 ? '・×' + a.arr.n : '');
      list.append(h('div', { class: 'item' + (SEL.id === a.id ? ' sel' : ''), onclick: () => select(a.id) }, eye, h('span', { class: 'nm' }, a.name), h('span', { class: 'tag' }, (ASSET_DEFS[a.type]?.cat || '') + extra)));
    }
    if (!S.assets.length) list.append(h('div', { class: 'item' }, 'まだ何も置いていません'));
    ls.append(list);
    const inst = SEL.id && findInst(SEL.id);
    if (inst) renderInspector(body, inst);
  },
  lights(body) {
    const L = S.lights; const LV = () => markDirty('levels');
    const LE = () => { L.liveEdited = true; updateRemote(); };
    note(body, 'ふだんの操作は画面の<b>リモコン</b>でポチポチ切り替えます。このタブでは、いま出ている演出の細かい調整と、<b>シーン</b>（演出の組み合わせを覚えておくメモリー）の管理ができます。');
    const pl = section(body, '再生とテンポ');
    btns(pl, [[L.playing ? '❚❚ 一時停止' : '▶ 再生', () => { togglePlay(); renderTab(); }, 'pri'], ['TAP（拍に合わせて連打）', tapTempo]]);
    uiRange(pl, 'BPM', L, 'bpm', 60, 220, 1, updateRemote);
    uiCheck(pl, 'デモ（シーンを自動で送る）', L, 'demo', updateRemote); uiRange(pl, '何小節ごとに次へ', L, 'demoBars', 1, 16, 1);
    const c = L.live;
    const ce = section(body, `いま出ている演出（ライブ）${L.liveEdited ? '・調整中' : '：シーン' + (L.cue + 1)}`, true, 'liveEdit');
    uiSelect(ce, 'ムービングの動き', c, 'pattern', PATTERN_DEFS, LE);
    uiRange(ce, '速さ（周/小節）', c, 'speed', 0, 4, 0.05, LE); uiRange(ce, '振り幅（度）', c, 'amp', 0, 90, 1, LE); uiRange(ce, '倒す角度（度）', c, 'tilt', 0, 100, 1, LE); uiRange(ce, '扇の広がり（度）', c, 'spread', 0, 90, 1, LE);
    uiCheck(ce, '左右対称に動く', c, 'sym', LE);
    uiSelect(ce, '色の付け方', c, 'colorMode', COLOR_MODES, LE); uiColor(ce, '色1', c, 'c1', LE); uiColor(ce, '色2', c, 'c2', LE); uiColor(ce, '色3', c, 'c3', LE);
    uiSelect(ce, '明るさの変化', c, 'dim', DIM_MODES, LE); uiRange(ce, 'ビームの太さ', c, 'beam', 0.2, 3, 0.05, LE);
    uiSelect(ce, 'レーザー', c, 'laser', LASER_MODES, LE); uiCheck(ce, 'カラーウォッシュ（舞台を色で照らす）', c, 'wash', LE);
    uiSelect(ce, '客席ペンライト', c, 'penlight', PENLIGHT_MODES, LE);
    const fx = new Set((c.fx || '').split(',').filter(Boolean));
    const fxRow = h('div', { class: 'btns' });
    for (const [k, l] of [['spark', 'スパーク'], ['confetti', '紙吹雪'], ['smoke', 'スモーク']]) {
      const cb = h('input', { type: 'checkbox' }); cb.checked = fx.has(k);
      cb.addEventListener('change', () => { cb.checked ? fx.add(k) : fx.delete(k); c.fx = [...fx].join(','); LE(); scheduleSave(); });
      fxRow.append(h('label', { style: 'display:flex;gap:4px;align-items:center;color:#cfd5ff' }, cb, l));
    }
    row(ce, 'シーン呼び出し時の特効', fxRow);
    note(ce, 'ここで変えた内容は「いま」の演出です。残したいときは下の「シーン」に登録・上書きしてください。');
    const cl = section(body, `シーン（${L.cues.length}）`, true, 'sceneList');
    const list = h('div', { class: 'list' });
    L.cues.forEach((q, i) => list.append(h('div', { class: 'cue' + (i === L.cue ? ' on' : ''), title: 'クリックで呼び出し', onclick: () => { recallScene(i); } },
      h('span', { class: 'no' }, i + 1), h('span', { class: 'nm' }, q.name), h('span', { class: 'tag' }, SHORT.pattern[q.pattern] || ''))));
    cl.append(list);
    const cur = L.cues[L.cue];
    if (cur) uiText(cl, '選択中の名前', cur, 'name', (live) => { if (!live) refreshAllUI(); });
    btns(cl, [
      ['＋ いまの演出を新しいシーンに', saveLiveAsScene, 'pri'],
      ['選択中のシーンに上書き', () => { if (!cur) return; L.cues[L.cue] = { ...deepClone(L.live), name: cur.name }; L.liveEdited = false; commit(); refreshAllUI(); toast(`シーン${L.cue + 1}に上書きしました`); }],
      ['↑', () => { if (L.cue > 0) { const q = L.cues.splice(L.cue, 1)[0]; L.cues.splice(L.cue - 1, 0, q); L.cue--; commit(); refreshAllUI(); } }],
      ['↓', () => { if (L.cue < L.cues.length - 1) { const q = L.cues.splice(L.cue, 1)[0]; L.cues.splice(L.cue + 1, 0, q); L.cue++; commit(); refreshAllUI(); } }],
      ['削除', () => { if (L.cues.length <= 1) return toast('シーンは最低1つ必要です'); L.cues.splice(L.cue, 1); L.cue = Math.min(L.cue, L.cues.length - 1); L.liveEdited = true; commit(); refreshAllUI(); }, 'danger']]);
    note(cl, 'シーンは数字キー 1〜9 でも呼び出せます。');
    const gl = section(body, '全体の明るさ・空気感', false);
    uiRange(gl, 'マスター明るさ', L, 'master', 0, 2, 0.01); uiRange(gl, 'ビームの見え方（スモーク量）', L, 'haze', 0, 2, 0.01);
    uiRange(gl, '霧の濃さ', L, 'fog', 0, 0.05, 0.001, LV); uiRange(gl, '環境光', L, 'ambient', 0, 1.5, 0.01, LV); uiRange(gl, 'カラーウォッシュ', L, 'wash', 0, 3, 0.01);
    uiRange(gl, 'グロー（にじみ）', L, 'bloom', 0, 3, 0.01, LV); uiRange(gl, 'グローの広がり', L, 'bloomRadius', 0, 1.5, 0.01, LV); uiRange(gl, 'グローのしきい値', L, 'bloomThreshold', 0, 1.5, 0.01, LV);
    uiRange(gl, '露出', L, 'exposure', 0.2, 2.5, 0.01, LV);
    uiCheck(gl, '空中のキラキラ', L, 'glitter'); uiColor(gl, 'キラキラの色', L, 'glitterColor');
    note(body, '特効（スパーク・紙吹雪・スモーク）は「特効」カテゴリのアセットを置いたときに動きます。<br><b>VRC移植メモ：</b>動きは「拍 → 向き・色・明るさ」の式だけで決まります（PATTERNS / colorAt / dimAt）。Unity インポーターはこの式を Animator に焼き込みます。');
  },
  cams(body) {
    for (const [k, l] of [['cam1', 'ステージカメラ1（赤）'], ['cam2', 'ステージカメラ2（青）']]) {
      const c = S.cams[k]; const sc = section(body, l);
      uiSelect(sc, 'カメラワーク', c, 'mode', CAM_MODES);
      uiRange(sc, '動きの速さ', c, 'speed', 0, 4, 0.05); uiRange(sc, '画角（ズーム）', c, 'fov', 8, 70, 1); uiRange(sc, '距離', c, 'dist', 1, 25, 0.1); uiRange(sc, '高さ', c, 'height', -1, 8, 0.05);
      uiSelect(sc, 'アップで追う人', c, 'target', [[-1, '全員のまん中（ステージ）'], ...REG.performerUnits.map((o, i) => [i, `${i + 1}：${o.userData.perfName || '演者'}の顔`])]);
      btns(sc, [[`${k === 'cam1' ? 'カメラ1' : 'カメラ2'}の視点で見る`, () => setView(k)]]);
    }
    const sh = section(body, '表示・画質');
    uiCheck(sh, 'カメラ本体を表示', S.cams, 'showRig'); uiCheck(sh, '右下にカメラ1のプレビュー', S.cams, 'pip');
    uiSelect(sh, 'ライブ映像の解像度', S.cams, 'res', [['low', '低（軽い）'], ['mid', '中'], ['high', '高']]);
    note(sh, 'ドットLEDに映すので、解像度は低めでも見た目はほとんど変わりません。演者ダミーがいるとカメラはその人たちを追います（引きのショットはステージ全体）。<br><b>アップで追う人</b>を決めると、「アップ」（オートの中のアップも）はその人の顔を胸から上で追います。Unity（UdonSharp 版）では、リモコン・照明卓の「自分をカメラ1／2」を押した人の顔を追います。');
    const asp = (a) => Math.abs(a - 16 / 9) < 0.02 ? '16:9' : a >= 1 ? a.toFixed(2) + ' : 1（横長）' : '1 : ' + (1 / a).toFixed(2) + '（縦長）';
    note(sh, `<b>画角の縦横比</b>：カメラは、そのカメラを映しているモニターの縦横比で撮ります（縦の画角はそのまま）。モニターの幅・高さを変えても<b>映像は伸び縮みせず、映る範囲だけが変わります</b>。同じカメラを形のちがうモニターに映すときは、いちばん横長のモニターに合わせ、ほかのモニターはまん中を切り抜きます。<br>いま：カメラ1 ${asp(SCAMS[0].aspect)}・カメラ2 ${asp(SCAMS[1].aspect)}`);
    const mon = section(body, 'LEDモニター');
    btns(mon, [['＋ 後ろに大きいモニター', () => addAsset('led_monitor', { name: 'メインモニター', pos: [0, 0, -S.stage.depth / 2 - 0.3], p: { w: 9, h: 5, lift: S.stage.height + 1.2, src: 'cam1' } }), 'pri'],
      ['＋ 左右にモニター（ミラー複製）', () => addAsset('led_monitor', { name: 'サイドモニター', pos: [S.stage.width / 2 + 3.5, 0, -1], rot: [0, -18, 0], mirror: true, p: { w: 4.5, h: 6, lift: 1.5, src: 'cam2', frame: 'truss' } }), 'pri']]);
    const list = h('div', { class: 'list' }); const mons = S.assets.filter(a => a.type === 'led_monitor');
    for (const a of mons) list.append(h('div', { class: 'item' + (SEL.id === a.id ? ' sel' : ''), onclick: () => { select(a.id); currentTab = 'assets'; renderTabs(); } }, h('span', { class: 'nm' }, a.name), h('span', { class: 'tag' }, (MON_SOURCES.find(s => s[0] === a.p.src)?.[1].split('（')[0] || '') + (a.mirror ? '・左右' : ''))));
    if (!mons.length) list.append(h('div', { class: 'item' }, 'モニターはまだありません'));
    mon.append(list);
    const vjs = section(body, 'VJ 映像');
    btns(vjs, [['VJリモコンを開く（J）', () => toggleVJRemote(true), 'pink']]);
    note(vjs, '幾何学の映像・画像・文字に、拍に合わせてエフェクト（ズーム・フラッシュ・万華鏡・グリッチ…）をかけます。<br>VJリモコンの「モニター」でカメラ映像のモニターが、「背景LED」で背景の LED パネルが VJ 映像になります。モニターごとに決めるときは「映すもの」を「VJリモコンの映像」に。<br>「全画面」「別窓」で映像だけを出せます（プロジェクター・OBS 用）。<br>Unity インポーター v0.5 でも同じ VJ になります（UdonSharp 版なら全員に同期・ステージ裏の VJ卓で操作）。');
    note(mon, 'モニターを選ぶと「アセット」タブで細かく設定できます。<b>左右ミラー</b>をオンにすると反対側に複製され、<b>並べる個数</b>で横に増やせます。');
  },
  io(body) {
    const pj = section(body, 'プロジェクト');
    uiText(pj, 'ステージ名', S, 'name');
    btns(pj, [['JSONで保存', saveProjectFile, 'pri'], ['JSONを読み込む', async () => { const f = await pickFile('.json,application/json'); if (!f) return; try { loadProjectData(JSON.parse(await readAsText(f)), `「${f.name}」を読み込みました`); } catch (e) { toast('読み込みに失敗しました: ' + e.message); } }],
      ['空のステージから始める', () => applyPreset('blank'), 'danger']]);
    pj.append(h('div', { class: 'note', id: 'saveStatus' }, lastSave || '変更はブラウザに自動保存されます'));
    const ex = section(body, 'Unity / VRChat 向け書き出し');
    const opt = EXPORT_OPTS;
    uiCheck(ex, '会場（床・壁・空）も含める', opt, 'venue'); uiCheck(ex, '観客・イスも含める', opt, 'crowd');
    btns(ex, [['GLB（3Dモデル一式）', exportGLB, 'pri'], ['Unity用JSON（配置・ライト・カメラ）', exportUnityJSON, 'pri'], ['スクリーンショット', () => { SHOT.req = true; }]]);
    note(ex, '<b>GLB と Unity用JSON の両方</b>を書き出して、Unity のメニュー「和室 → すてーじ工房 → インポーター」に入れると、LEDモニター・ビーム・ステージカメラ・特効・リモコン（UdonSharp 版なら全員に同期）・VJ・ステージ裏の操作卓まで組み立て直せます（インポーターの unitypackage を先に取り込んでください。配布している GitHub の Releases にあります。Unity は最初から GLB を読めないので、インポーターの「glTFast を入れる」ボタンで GLB を読めるようにします）。観客を動かすには「観客・イスも含める」にチェック。<br>GLB には形と色、JSON には配置・シーン・リモコンの状態・カメラ設定・モニターの画像/文字が入ります。LED やビームは Unity 側の専用シェーダーで作り直します。');
    const q = section(body, '画質（このPCだけの設定）');
    const qo = { level: QUALITY.level };
    uiSelect(q, '画質', qo, 'level', [['low', '低（映り込みなし・軽い）'], ['mid', '中'], ['high', '高（きれい・重い）']], () => { QUALITY.level = qo.level; try { localStorage.setItem('stagekobo.quality', qo.level); } catch (e) {} markDirty('quality'); });
    const vr = section(body, 'VRChat 移植メモ', false);
    note(vr, [
      '<b>ステージ・背景・アセット</b> → GLB を Unity に取り込み、Unity用JSON で配置を復元（インポーターが自動で組み立て）。',
      '<b>LEDモニター</b> → ステージカメラ（RenderTexture）＋ ドット表示シェーダー。VJ パターンもブラウザと同じ式。カメラの画角の縦横比もモニターに合わせる（映像は伸び縮みしない）。',
      '<b>VJ リモコン</b> → Unity では Custom Render Texture で同じ式を描く。UdonSharp 版なら全員に同期し、ステージ裏の VJ卓（物理スイッチ）で操作。',
      '<b>ビーム・カラーウォッシュ</b> → 円錐メッシュ＋加算シェーダー／床に描く光だまり（本物のライトを使わないので軽い）。',
      '<b>リモコン</b> → UdonSharp 版なら操作が全員に同期。BPM（±・TAP）と動きの速さ（×½/×2）も変えられ、拍はサーバー時刻でそろえる。',
      '<b>ステージカメラ</b> → Animator でカメラワークを再生（オートは 2 小節ごとに切り替え・テンポに追従）。',
      '<b>演者ダミー</b> → ダンスを Animator に焼き込み。本番は実際のプレイヤーに置き換え（ピンスポの狙い先）。',
      '<b>客席</b> → 人とペンライトはシェーダーで拍に合わせて揺れる（1つのメッシュなので軽い）。紙吹雪・スパークは Particle System。',
      '<b>質感</b> → 金属・塗装・布・木・床などの色ムラ／ノーマル／ラフネス・AO はコードで作って GLB に入る（Unity でも同じ見た目。JPEG で軽い）。',
    ].join('<br>'));
  },
};

/* ---------- インスペクター ---------- */
function renderInspector(body, inst) {
  const d = ASSET_DEFS[inst.type];
  const rel = () => { layoutInstance(inst); updateSelBox(); };
  const reb = () => markDirty('asset:' + inst.id);
  const ins = section(body, `選択中：${inst.name}`, true, 'inspector');
  uiText(ins, '名前', inst, 'name', (live) => { if (!live) renderTab(); });
  xyzRow(ins, '位置 (m)', inst.pos, 0.05, rel);
  xyzRow(ins, '回転 (度)', inst.rot, 1, rel);
  const us = { u: inst.scale[0] };
  uiRange(ins, '大きさ（全体）', us, 'u', 0.05, 5, 0.01, () => { inst.scale = [us.u, us.u, us.u]; rel(); refreshInspectorFields(); });
  xyzRow(ins, '大きさ XYZ', inst.scale, 0.05, rel);
  btns(ins, [['床に置く', () => dropToFloor(inst.id)], ['よく見る', () => focusOn(inst.id)], ['複製 (Ctrl+D)', () => duplicateAsset(inst.id)], ['削除', () => deleteAsset(inst.id), 'danger']]);
  const cp = section(body, '左右ミラー・並べる', true, 'mirror');
  uiCheck(cp, '左右ミラー（X=0で対称コピー）', inst, 'mirror', () => { reb(); setTimeout(renderTab, 0); });
  uiRange(cp, '並べる個数', inst.arr, 'n', 1, 20, 1, reb);
  uiRange(cp, '並べる間隔 (m)', inst.arr, 'dx', -10, 10, 0.05, rel);
  note(cp, 'ミラーは位置Xを反転した場所にコピーします（X=0のときは同じ場所に重なります）。並べるはX方向に等間隔で増やします。どちらも元を動かすと一緒に動きます。');
  if ((d.colors || []).length || (d.params || []).length) {
    const ps = section(body, `${d.label} の設定`, true, 'params');
    for (const [k, l] of d.colors || []) uiColor(ps, l, inst, k, reb);
    for (const pr of d.params || []) {
      if (pr.t === 'range') uiRange(ps, pr.label, inst.p, pr.k, pr.min, pr.max, pr.step, reb);
      else if (pr.t === 'select') uiSelect(ps, pr.label, inst.p, pr.k, pr.opts, reb);
      else if (pr.t === 'check') uiCheck(ps, pr.label, inst.p, pr.k, reb);
      else if (pr.t === 'color') uiColor(ps, pr.label, inst.p, pr.k, reb);
      else if (pr.t === 'text') uiText(ps, pr.label, inst.p, pr.k, reb);
      else if (pr.t === 'image') row(ps, pr.label, h('button', { class: 'btn sm', onclick: async () => { const id = await uploadImage(); if (!id) return; inst.p[pr.k] = id; if (inst.type === 'led_monitor') inst.p.src = 'image'; reb(); commit(); renderTab(); } }, inst.p[pr.k] ? '画像を変更…' : '画像を読み込む…'));
    }
  }
}

/* ---------- 視点 ---------- */
function tweenView(pos, target) { VIEW.mode = 'free'; orbit.enabled = true; VIEW.tween = { p0: camera.position.clone(), t0: orbit.target.clone(), p1: pos, t1: target, k: 0 }; updateViewBtns(); }
function setView(key) {
  const st = S.stage, H = st.height, D = st.depth;
  if (key === 'cam1' || key === 'cam2') { VIEW.mode = key; orbit.enabled = false; tcontrols.detach(); updateViewBtns(); return; }
  if (VIEW.mode !== 'free') { const sc = SCAMS[VIEW.mode === 'cam1' ? 0 : 1]; orbit.target.copy(sc.pose.look); camera.fov = 42; camera.updateProjectionMatrix(); }
  VIEW.mode = 'free'; reattachGizmo();
  const V = {
    seat: [V3(0, 5.2, D / 2 + 21), V3(0, H + 2.5, 0)],
    front: [V3(0, H + 1.7, D / 2 + 8), V3(0, H + 2.2, 0)],
    top: [V3(0, 32, D / 2 + 18), V3(0, 0, 2)],
    side: [V3(-20, 8, D / 2 + 16), V3(0, H + 2, 0)],
    stage: [V3(0, H + 1.6, 0), V3(0, H + 1.4, D / 2 + 10)],
  }[key];
  if (V) tweenView(V[0], V[1]);
}
function updateViewTween(dt) {
  const tw = VIEW.tween; if (!tw) return;
  tw.k = Math.min(1, tw.k + dt / 0.6); const e = smooth(tw.k);
  camera.position.lerpVectors(tw.p0, tw.p1, e); orbit.target.lerpVectors(tw.t0, tw.t1, e);
  if (tw.k >= 1) VIEW.tween = null;
}

/* ---------- HUD ---------- */
const viewBtnEls = {};
function buildHUD() {
  const vb = $('#viewBtns');
  for (const [k, l] of [['seat', '客席'], ['front', '正面'], ['side', '斜め'], ['top', '俯瞰'], ['stage', '演者目線'], ['cam1', 'CAM1'], ['cam2', 'CAM2']]) {
    const b = h('button', { class: 'btn sm', onclick: () => setView(k), title: k.startsWith('cam') ? 'ステージカメラの映像を全画面で見る' : '' }, l); viewBtnEls[k] = b; vb.append(b);
  }
  const gb = $('#gizmoBtns');
  for (const [m, l] of [['translate', '移動 W'], ['rotate', '回転 E'], ['scale', '拡縮 R']]) gb.append(h('button', { class: 'btn sm gz', 'data-m': m, onclick: () => setGizmoMode(m) }, l));
  gb.append(h('button', { class: 'btn sm', id: 'snapBtn', onclick: () => { snapOn = !snapOn; applySnap(); $('#snapBtn').classList.toggle('on', snapOn); toast(snapOn ? 'スナップ ON（0.25m / 15°）' : 'スナップ OFF'); } }, 'スナップ'));
  const eb = $('#editBtns');
  eb.append(h('button', { class: 'btn sm', id: 'undoBtn', onclick: undo }, '↶ 元に戻す'), h('button', { class: 'btn sm', id: 'redoBtn', onclick: redo }, '↷ やり直し'));
  setGizmoMode('translate');
}
function setGizmoMode(m) { tcontrols.setMode(m); document.querySelectorAll('.gz').forEach(b => b.classList.toggle('on', b.dataset.m === m)); }
function updateViewBtns() { for (const k in viewBtnEls) viewBtnEls[k].classList.toggle('on', VIEW.mode === k); }
function updateEditBtns() { const u = $('#undoBtn'), r = $('#redoBtn'); if (u) u.disabled = HIST.idx <= 0; if (r) r.disabled = HIST.idx >= HIST.stack.length - 1; if (u) u.style.opacity = u.disabled ? 0.45 : 1; if (r) r.style.opacity = r.disabled ? 0.45 : 1; }
/* ---------- リモコン（ビュー上のオーバーレイ操作パネル） ----------
   動き・色・明るさ・レーザー・特効・カメラを、それぞれ独立したボタンで即切り替える。
   Unity インポーターも同じチャンネル構成（Animator のレイヤー）で焼き込む。 */
const UIPREF = (() => { try { return JSON.parse(localStorage.getItem('stagekobo.ui') || '{}'); } catch (e) { return {}; } })();
function saveUIPref() { try { localStorage.setItem('stagekobo.ui', JSON.stringify(UIPREF)); } catch (e) {} }
const RM = { refs: [] };
const CAM_SHORT = { auto: 'オート', orbit: '周回', dolly: '寄り引き', crane: 'クレーン', truck: '横移動', closeup: 'アップ', audience: '客席', top: '真上', fixed: '固定' };
function rmBtn(label, onClick, test, cls = '', title = '') {
  const b = h('button', { class: 'rb ' + cls, title, onclick: (e) => { e.stopPropagation(); onClick(); } }, label);
  if (test) RM.refs.push({ el: b, test }); return b;
}
function togglePlay() { S.lights.playing = !S.lights.playing; updateRemote(); scheduleSave(); }
function toggleBlackout() { S.lights.blackout = !S.lights.blackout; updateRemote(); scheduleSave(); toast(S.lights.blackout ? '暗転（B でもとに戻ります）' : '暗転を解除'); }
function saveLiveAsScene() {
  const L = S.lights; const q = deepClone(L.live); q.name = 'シーン' + (L.cues.length + 1);
  L.cues.push(q); L.cue = L.cues.length - 1; L.liveEdited = false; commit(); refreshAllUI(); toast(`「${q.name}」として登録しました`);
}
function buildRemote() {
  const r = $('#remote'); if (!r) return; r.innerHTML = ''; RM.refs = [];
  const L = () => S.lights;
  const sep = () => h('span', { class: 'rmSep' });
  const head = h('div', { class: 'rmHead', title: 'ここをドラッグして移動できます' },
    h('span', { class: 'rmGrip' }, '⠿'), h('span', { class: 'rmTitle' }, 'REMOTE'),
    rmBtn('', togglePlay, null, 'play', '再生／一時停止（Space）'),
    h('div', { class: 'beat', id: 'beatDot' }),
    rmBtn('−', () => { S.lights.bpm = Math.max(40, S.lights.bpm - 1); updateRemote(); scheduleSave(); }, null, 'sm'),
    h('span', { class: 'bpm', id: 'bpmTxt' }, ''),
    rmBtn('+', () => { S.lights.bpm = Math.min(240, S.lights.bpm + 1); updateRemote(); scheduleSave(); }, null, 'sm'),
    rmBtn('TAP', tapTempo, null, '', '拍に合わせて4回以上押すとBPMを合わせます'),
    sep(),
    ...[[0.5, '×½'], [1, '×1'], [2, '×2']].map(([v, l]) => rmBtn(l, () => { S.lights.speedMul = v; updateRemote(); scheduleSave(); }, () => (L().speedMul ?? 1) === v, '', '動きの速さ倍率')),
    sep(),
    rmBtn('デモ', () => { S.lights.demo = !S.lights.demo; LT.demoIdx = null; updateRemote(); scheduleSave(); toast(S.lights.demo ? 'デモ：シーンを自動で送ります' : 'デモを止めました'); }, () => L().demo, '', 'シーンを自動で順番に送る'),
    rmBtn('暗転', toggleBlackout, () => L().blackout, 'warn', '照明を全部消す（B）'),
    rmBtn('VJ', () => toggleVJRemote(), () => !!UIPREF.vjOpen, 'vjopen', 'VJリモコンを開く・閉じる（J）：幾何学の映像や画像に、拍に合わせてエフェクトをかける'),
    h('span', { class: 'rmFill' }),
    rmBtn(UIPREF.rmMin ? '▴ 開く' : '▾ たたむ', () => { UIPREF.rmMin = !UIPREF.rmMin; saveUIPref(); buildRemote(); }, null, 'min'));
  const body = h('div', { class: 'rmBody' });
  const sc = h('div', { class: 'rmScenes' }, h('span', { class: 'rmLbl' }, 'シーン'));
  S.lights.cues.forEach((q, i) => sc.append(rmBtn(`${i + 1} ${q.name}`, () => recallScene(i), () => L().cue === i && !L().liveEdited, 'scene' + (L().cue === i ? ' last' : ''), `シーン${i + 1}「${q.name}」を呼び出す${i < 9 ? '（キー ' + (i + 1) + '）' : ''}`)));
  sc.append(rmBtn('＋登録', saveLiveAsScene, null, 'add', 'いまの演出を新しいシーンとして登録'));
  body.append(sc);
  const grid = h('div', { class: 'rmGrid' });
  const sec = (title, ...kids) => grid.append(h('div', { class: 'rmSec' }, h('div', { class: 'rmSecT' }, title), h('div', { class: 'rmBtns' }, ...kids)));
  const brk = () => h('div', { class: 'rmBreak' });
  sec('動き', ...PATTERN_DEFS.map(([k, l]) => rmBtn(SHORT.pattern[k], () => setLive({ pattern: k }), () => L().live.pattern === k, '', l)));
  const sw = PALETTES.map(p => { const b = rmBtn('', () => setLive({ c1: p.c[0], c2: p.c[1], c3: p.c[2] }), () => { const c = L().live; return c.c1 === p.c[0] && c.c2 === p.c[1] && c.c3 === p.c[2]; }, 'sw', p.name);
    b.style.background = `linear-gradient(90deg, ${p.c[0]} 0 34%, ${p.c[1]} 34% 67%, ${p.c[2]} 67%)`; return b; });
  sec('色', ...sw, brk(), ...COLOR_MODES.map(([k, l]) => rmBtn(SHORT.colorMode[k], () => setLive({ colorMode: k }), () => L().live.colorMode === k, '', l)));
  sec('明るさ', ...DIM_MODES.map(([k, l]) => rmBtn(SHORT.dim[k], () => setLive({ dim: k }), () => L().live.dim === k, '', l)), brk(),
    ...[[0.6, '細'], [1, '中'], [1.7, '太']].map(([v, l]) => rmBtn('ビーム' + l, () => setLive({ beam: v }), () => Math.abs((L().live.beam ?? 1) - v) < 0.05)),
    rmBtn('ウォッシュ', () => setLive({ wash: !L().live.wash }), () => !!L().live.wash, '', '舞台を色で照らす'));
  sec('レーザー', ...LASER_MODES.map(([k, l]) => rmBtn(SHORT.laser[k], () => setLive({ laser: k }), () => L().live.laser === k, '', l)));
  sec('特効', rmBtn('スパーク', () => triggerFx('spark'), null, 'fx'), rmBtn('紙吹雪', () => triggerFx('confetti'), null, 'fx'), rmBtn('スモーク', () => triggerFx('smoke'), null, 'fx'), rmBtn('ストロボ', () => triggerFx('strobe'), null, 'fx', '1.5秒間すべての照明が点滅'),
    brk(), h('span', { class: 'rmLbl2' }, 'ペンライト'), ...PENLIGHT_MODES.map(([k, l]) => rmBtn(l.replace('の色', ''), () => setLive({ penlight: k }), () => L().live.penlight === k, '', '観客のペンライト（「キューに合わせる」設定の観客だけ）')));
  const camKey = () => UIPREF.rmCam || 'cam1';
  sec('カメラ', ...['cam1', 'cam2'].map(k => rmBtn(k.toUpperCase(), () => { UIPREF.rmCam = k; saveUIPref(); updateRemote(); }, () => camKey() === k, 'camsel', 'どちらのカメラを操作するか')), brk(),
    ...CAM_MODES.map(([k, l]) => rmBtn(CAM_SHORT[k], () => { S.cams[camKey()].mode = k; updateRemote(); scheduleSave(); if (currentTab === 'cams') renderTab(); }, () => S.cams[camKey()].mode === k, '', l)), brk(),
    ...[['main', 'メイン'], ['sub', 'サブ']].flatMap(([g, l], i) => [...(i ? [brk()] : []), h('span', { class: 'rmLbl2' }, l),
      rmBtn('ライブ映像', () => setMonitorMode(g, 'live'), () => L()[monModeKey(g)] !== 'vj', '', `${l}のモニターにステージカメラの映像を映す`),
      rmBtn('VJ映像', () => setMonitorMode(g, 'vj'), () => L()[monModeKey(g)] === 'vj', '', `${l}のカメラ映像のモニターを VJ リモコンの映像に切り替える（自動：左右ミラーのモニターはサブ）`)]));
  body.append(grid);
  r.append(head, body);
  r.classList.toggle('min', !!UIPREF.rmMin);
  // ドラッグ移動
  head.addEventListener('pointerdown', (e) => {
    if (e.target.closest('button')) return;
    const vr = viewEl.getBoundingClientRect(), rr = r.getBoundingClientRect(); const ox = e.clientX - rr.left, oy = e.clientY - rr.top;
    head.setPointerCapture(e.pointerId);
    const mv = (ev) => { UIPREF.rmPos = { x: ev.clientX - vr.left - ox, y: ev.clientY - vr.top - oy }; placeRemote(); };
    const up = () => { head.removeEventListener('pointermove', mv); head.removeEventListener('pointerup', up); saveUIPref(); };
    head.addEventListener('pointermove', mv); head.addEventListener('pointerup', up);
  });
  placeRemote(); updateRemote();
}
function placeRemote() {
  const r = $('#remote'); if (!r) return;
  const W = viewEl.clientWidth, H = viewEl.clientHeight, w = r.offsetWidth, hh = r.offsetHeight;
  if (UIPREF.rmPos) { r.style.left = clamp(UIPREF.rmPos.x, 0, Math.max(0, W - w)) + 'px'; r.style.top = clamp(UIPREF.rmPos.y, 44, Math.max(44, H - hh)) + 'px'; r.style.bottom = 'auto'; }
  else { r.style.left = Math.max(0, W - w - 10) + 'px'; r.style.top = '52px'; r.style.bottom = 'auto'; }
}
function updateRemote() {
  const L = S.lights; if (!L.live) return;
  for (const ref of RM.refs) ref.el.classList.toggle('on', !!ref.test());
  const pb = $('#remote .rb.play'); if (pb) pb.textContent = L.playing ? '❚❚' : '▶';
  const bt = $('#bpmTxt'); if (bt) bt.textContent = L.bpm;
  document.querySelectorAll('#remote .rb.scene').forEach((el, i) => el.classList.toggle('last', i === L.cue));
}
const updateTransport = updateRemote;
onLiveChanged = (auto) => {
  updateRemote();
  if (currentTab !== 'lights') return;
  const ae = document.activeElement; const editing = ae && $('#tabBody').contains(ae) && /INPUT|SELECT|TEXTAREA/.test(ae.tagName);
  if (!editing) renderTab();  // 入力中は書き換えない（編集中の値が飛ばないように）
};
const taps = [];
function tapTempo() {
  const t = performance.now(); if (taps.length && t - taps[taps.length - 1] > 2000) taps.length = 0; taps.push(t); if (taps.length > 8) taps.shift();
  if (taps.length >= 4) { const iv = (taps[taps.length - 1] - taps[0]) / (taps.length - 1); S.lights.bpm = clamp(Math.round(60000 / iv), 40, 240); LT.beat = Math.round(LT.beat); updateRemote(); scheduleSave(); toast('BPM ' + S.lights.bpm); }
  else toast(`TAP ${taps.length}/4`, 600);
}

/* ---------- キーボード ---------- */
window.addEventListener('keydown', e => {
  const tg = e.target.tagName; if (tg === 'INPUT' || tg === 'SELECT' || tg === 'TEXTAREA') return;
  const k = e.key.toLowerCase(); const mod = e.ctrlKey || e.metaKey;
  if (mod && k === 'z' && !e.shiftKey) { e.preventDefault(); undo(); }
  else if (mod && (k === 'y' || (k === 'z' && e.shiftKey))) { e.preventDefault(); redo(); }
  else if (mod && k === 'd') { e.preventDefault(); if (SEL.id) duplicateAsset(SEL.id); }
  else if (mod && k === 's') { e.preventDefault(); saveProjectFile(); }
  else if (mod) return;
  else if (k === 'w') setGizmoMode('translate'); else if (k === 'e') setGizmoMode('rotate'); else if (k === 'r') setGizmoMode('scale');
  else if (k === 'delete' || k === 'backspace') { if (SEL.id && !VJOUT.full) deleteAsset(SEL.id); }
  else if (k === 'escape') { if (VJOUT.full) vjExitFull(); else if (VIEW.mode !== 'free') setView('seat'); else select(null); }
  else if (k === 'f') { if (SEL.id) focusOn(SEL.id); }
  else if (k === ' ') { e.preventDefault(); togglePlay(); }
  else if (/^[1-9]$/.test(k)) { const i = +k - 1; if (i < S.lights.cues.length) recallScene(i); }
  else if (k === 'b') toggleBlackout();
  else if (k === 'h') $('#hint').classList.toggle('hide');
});

/* ---------- 保存・書き出し ---------- */
const EXPORT_OPTS = { venue: false, crowd: false };
const SHOT = { req: false };
const safeName = () => (S.name || 'stage').replace(/[\\/:*?"<>|\s]+/g, '_');
function saveProjectFile() {
  const used = new Set(); const scan = o => { if (o && typeof o === 'object') for (const v of Object.values(o)) { if (typeof v === 'string' && (IMAGES[v] || BLOBS[v])) used.add(v); else scan(v); } }; scan(S);
  const images = {}, blobs = {}; for (const id of used) { if (IMAGES[id]) images[id] = IMAGES[id]; if (BLOBS[id]) blobs[id] = BLOBS[id]; }
  download(`${safeName()}.stagekobo.json`, JSON.stringify({ format: 'stagekobo', version: 1, app: APP_VERSION, state: S, images, blobs }, null, 1), 'application/json');
  toast('JSONで保存しました');
}
function buildUnityExport() {
  scene.updateMatrixWorld(true);
  const units = []; const p = V3(), q = new THREE.Quaternion(), s = V3();
  const r4 = v => Math.round(v * 1e4) / 1e4;
  const images = {};
  for (const a of S.assets) {
    const rt = RT_ASSETS.get(a.id); if (!rt) continue;
    rt.units.forEach((u, k) => {
      u.obj.matrixWorld.decompose(p, q, s);
      units.push({ assetId: a.id, type: a.type, root: rt.root.name, node: u.obj.name, wrapIndex: k,
        name: `${a.name}${u.mirrored ? ' (R)' : ''}${(a.arr?.n ?? 1) > 1 ? ' #' + (u.idx + 1) : ''}`, mirrored: u.mirrored, index: u.idx, visible: a.visible !== false,
        position: p.toArray().map(r4), rotation: q.toArray().map(r4), scale: s.toArray().map(r4) });
    });
    // モニターの画像・文字は Unity 側でテクスチャにできるよう埋め込む
    if (a.type === 'led_monitor') {
      if (a.p.src === 'image' && a.p.image && IMAGES[a.p.image]) images[a.p.image] = IMAGES[a.p.image];
      if (a.p.src === 'text') { const t = monitorTextTex(a.p.text || ' ', a.p.w / a.p.h, a.p.c1, a.p.c2); try { images['text_' + a.id] = t.image.toDataURL('image/png'); } catch (e) {} }
    }
  }
  // VJ（v0.7〜）：画像バンクと文字のテクスチャ、映像の素・エフェクト・シーンの定義（Unity 側で同じ番号に直すため）
  for (const id of vjImages()) images[id] = IMAGES[id];
  let vjTextAsp = 4;
  if (S.vj.text) { const tx = vjTextTex(S.vj.text, S.vj.textStyle === 'neon' ? 'neon' : 'plain'); try { images.vjtext = tx.image.toDataURL('image/png'); vjTextAsp = tx.userData.aspect || 4; } catch (e) {} }
  const vjDefs = {
    gens: VJ_GENS.map(g => g[0]), gensShort: VJ_GENS.map(g => g[2]), gensFull: VJ_GENS.map(g => g[1]), fx: VJ_FX.map(f => f[0]), fxNames: VJ_FX.map(f => f[1]),
    colorModes: VJ_COLOR_MODES.map(c => c[0]), colorModeNames: VJ_COLOR_MODES.map(c => c[1]), mirrors: VJ_MIRRORS,
    presets: VJ_PRESETS, images: vjImages(), textAsp: vjTextAsp,
  };
  const cams = SCAMS.map((sc, k) => { const c = S.cams[sc.key];
    return { key: sc.key, position: sc.cam.position.toArray().map(r4), lookAt: sc.pose.look.toArray().map(r4), fov: sc.cam.fov, aspect: r4(camAspect(sc.key)), shot: sc.shot,
      target: c.target, faceDist: r4(faceDist(c)), faceFov: r4(faceFov(c)) }; });
  return {
    format: 'stagekobo-unity', version: 2, app: APP_VERSION, exportedAt: new Date().toISOString(), name: S.name,
    coordinateSystem: {
      source: 'three.js 右手系 / Y上 / +Z = 客席側 / 単位 m / rotation はクォータニオン [x,y,z,w]',
      toUnity_flipX: 'position (-x, y, z) / rotation (x, -y, -z, w)',
      toUnity_flipZ: 'position (x, y, -z) / rotation (-x, -y, z, w)  ※UniGLTF の既定（Z反転）と合わせる場合',
      mirroredUnits: 'mirrored=true のユニットは scale.x が負（左右反転）',
      glbNodes: 'GLB の ASSETS/<root>/Wrap_L|Wrap_R/<node> がユニット。灯体は Mount/Pan/Tilt/Lens、パーライトは Head/Lens、レーザーは Aperture',
    },
    timing: { bpm: S.lights.bpm, beatFormula: 'beat = 経過秒 × BPM / 60', bar: '4拍 = 1小節' },
    patternDoc: 'PATTERNS[pattern](cue, beat, i, n, xn, side) → {pan, tilt}（度）。pan=客席(+Z)から+X方向、tilt=床置きは真上/吊りは真下からの倒れ角。i,n=同種灯体の左からの番号/総数、xn=-1..1の横位置。',
    palettes: PALETTES, patterns: PATTERN_DEFS.map(x => x[0]), colorModes: COLOR_MODES.map(x => x[0]), dimModes: DIM_MODES.map(x => x[0]), laserModes: LASER_MODES.map(x => x[0]), camModes: CAM_MODES.map(x => x[0]),
    focus: F.focus.toArray().map(r4), center: F.center.toArray().map(r4), performers: F.performers.map(v => v.toArray().map(r4)), performerHeads: F.heads.map(v => v.toArray().map(r4)),
    state: S, units, stageCameras: cams, images, vjDefs,
  };
}
function exportUnityJSON() { download(`${safeName()}.unity.json`, JSON.stringify(buildUnityExport(), null, 1), 'application/json'); toast('Unity用JSONを書き出しました'); }
function bakeInstanced(im) {
  const lite = im.userData.exportGeo;   // 書き出し専用の軽い形（インデックス付きのまま結合する）
  const base = lite ? lite.clone() : im.geometry.index ? im.geometry.toNonIndexed() : im.geometry.clone();
  const geos = []; const M = new THREE.Matrix4(); const c = new THREE.Color(); const hasCol = !!im.instanceColor;
  let maxC = 1;
  if (hasCol) { maxC = 0; for (let i = 0; i < im.count; i++) { im.getColorAt(i, c); maxC = Math.max(maxC, c.r, c.g, c.b); } if (maxC <= 0) maxC = 1; }
  const pen = im.userData.penlight;   // ペンライト：まっすぐ立てて、UV に（色相・左右・位相, 高さ）を入れる → Unity のシェーダーで揺らす
  const crowd = im.userData.crowd;    // 客席の人：拍で弾む前の高さに戻して、UV.x に位相を入れる
  const P0 = V3(), Q0 = new THREE.Quaternion(), S0 = V3();
  for (let i = 0; i < im.count; i++) {
    im.getMatrixAt(i, M);
    if (pen) { const st = pen.sticks[i]; M.decompose(P0, Q0, S0); if (st) P0.y = st.y; M.compose(P0, Q0.identity(), S0.set(1, 1, 1)); }
    if (crowd) { M.decompose(P0, Q0, S0); P0.y = 0; M.compose(P0, Q0, S0); }
    const g = base.clone().applyMatrix4(M);
    if (pen && g.attributes.uv) {
      const st = pen.sticks[i] || { ph: 0, side: 1, hue: 0 }; const uv = g.attributes.uv, bp = base.attributes.position;
      const hq = clamp(Math.floor((st.hue || 0) * 100), 0, 99);
      for (let k = 0; k < uv.count; k++) uv.setXY(k, hq * 4 + (st.side > 0 ? 2 : 0) + Math.min(0.999, fract(st.ph)), clamp(bp.getY(k) / pen.len, 0, 1));
    }
    if (crowd && g.attributes.uv) { const uv = g.attributes.uv, ph = crowd.ph[i] || 0; for (let k = 0; k < uv.count; k++) uv.setXY(k, ph, 0); }
    if (hasCol) { im.getColorAt(i, c); const n = g.attributes.position.count, arr = new Float32Array(n * 3); for (let k = 0; k < n; k++) { arr[k * 3] = c.r / maxC; arr[k * 3 + 1] = c.g / maxC; arr[k * 3 + 2] = c.b / maxC; } g.setAttribute('color', new THREE.BufferAttribute(arr, 3)); }
    geos.push(g);
  }
  const mat = im.material.clone(); if (hasCol) { mat.vertexColors = true; if (mat.color) mat.color.set(1, 1, 1); }
  // 発光するもの（ペンライト・LEDドット等）は HDR の強さを名前に残す
  if (mat.isMeshBasicMaterial) mat.name = pen ? `SKPenlight_x${maxC.toFixed(2)}_l${pen.len}` : `SKGlowVC_x${maxC.toFixed(2)}`;
  if (crowd && mat.color) mat.name = `SKCrowd_${mat.color.getHexString()}`;
  const m = new THREE.Mesh(mergeGeometries(geos), mat); m.name = im.name || 'Instanced';
  m.position.copy(im.position); m.quaternion.copy(im.quaternion); m.scale.copy(im.scale);
  return m;
}
/* LED（シェーダー）面の設定を仮マテリアル名に埋め込む → Unity 側で同じ見た目の LED マテリアルを作る */
function ledPlaceholderName(o) {
  const u = o.material.uniforms || {};
  if (!u.uDots) return 'LED_unknown';
  const hx = c => c.getHexString();
  const held = VJ_HELD.get(o.material);   // VJ 映像を映している間は、元の設定で書き出す
  return `LED_pat${u.uPat.value}_dx${Math.round(u.uDots.value.x)}_dy${Math.round(u.uDots.value.y)}_gap${Math.round(u.uGap.value * 100)}_rnd${u.uRound.value}_br${Math.round(u.uBright.value * 100)}_led${u.uLed.value}_fl${u.uFlip.value}_c${hx(u.uC1.value)}_c${hx(u.uC2.value)}_c${hx(u.uC3.value)}_src${held ? held.src : u.uSrc.value}`;
}
function exportGLB() {
  const roots = [G.stage, G.backdrop, G.assets]; if (EXPORT_OPTS.venue) roots.push(G.venue);
  const hidden = [], temps = [];
  const hide = o => { if (o.visible) { o.visible = false; hidden.push(o); } };
  if (!EXPORT_OPTS.crowd) for (const a of S.assets) if (a.type === 'audience' || a.type === 'chairs') { const rt = RT_ASSETS.get(a.id); if (rt) hide(rt.root); }
  for (const r of roots) r.traverse(o => { if (o.userData.fx) hide(o); if (o.isLight) hide(o); });
  for (const r of roots) r.traverse(o => { if (o.isInstancedMesh && visibleChain(o)) { const m = bakeInstanced(o); o.parent.add(m); temps.push(m); hide(o); } });
  // シェーダー（LED等）は GLB に入らないので、名前付きの仮マテリアルに差し替えて書き出す → Unity 側で差し替えやすくする
  const swapped = [];
  for (const r of roots) r.traverse(o => { if (o.isMesh && o.material && o.material.isShaderMaterial && !o.userData.fx) { swapped.push([o, o.material]); const pm = new THREE.MeshBasicMaterial({ color: '#202330' }); pm.name = ledPlaceholderName(o); o.material = pm; } });
  // 床の半透明はブラウザの映り込み（Reflector）を見せるためだけのもの → GLB では不透明にする（Unity 側は Reflection Probe で映り込み）
  for (const r of roots) r.traverse(o => { if (o.isMesh && o.material && o.material.userData && o.material.userData.reflectFloor) { swapped.push([o, o.material]); const fm = o.material.clone(); fm.transparent = false; fm.opacity = 1; fm.name = 'StageFloor'; o.material = fm; } });
  const restoreAll = () => { for (const t of temps) { t.parent.remove(t); t.geometry.dispose(); } for (const o of hidden) o.visible = true; for (const [o, m] of swapped) { o.material.dispose(); o.material = m; } };
  toast('GLBを書き出しています…', 4000);
  try {
    new GLTFExporter().parse(roots, (res) => { restoreAll(); download(`${safeName()}.glb`, new Blob([res], { type: 'model/gltf-binary' })); toast('GLBを書き出しました'); },
      (err) => { restoreAll(); toast('GLB書き出しに失敗: ' + (err?.message || err)); }, { binary: true, onlyVisible: true });
  } catch (e) { restoreAll(); toast('GLB書き出しに失敗: ' + e.message); }
}
