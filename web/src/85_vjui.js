/* =====================================================================
   VJ リモコン（画面の上に浮かぶ操作パネル）
   - 上：プレビュー（ここだけ透明にして、WebGL で VJ 映像を描く）
   - パッド：Z フラッシュ / X 反転 / C グリッチ / V ズーム / N ストロボ（押している間）/ M 暗転
   - デッキ A・B とクロスフェーダー、映像の素、画像、色、エフェクト、文字、オート、シーン、出力先
   ===================================================================== */
const VJUI = { refs: [], xfEl: null, bpmEl: null, beatEl: null, lastBeat: -1, labelEl: null, prevEl: null };
function vjBtn(label, onClick, test, cls = '', title = '') {
  const b = h('button', { class: 'rb ' + cls, title, onclick: (e) => { e.stopPropagation(); onClick(e); vjUIUpdate(); } }, label);
  if (test) VJUI.refs.push({ el: b, test }); return b;
}
const vjSet = (o) => { Object.assign(S.vj, o); scheduleSave(); };
const vjDeckKey = () => (UIPREF.vjDeck === 'b' ? 'b' : 'a');
const vjDeck = () => S.vj[vjDeckKey()];
const vjShownKey = () => (VJRT.fade ? (VJRT.fade.to > 0.5 ? 'b' : 'a') : (S.vj.xf < 0.5 ? 'a' : 'b'));
const vjTake = () => UIPREF.vjTake !== false;
const VJ_FADE_LENS = [[0, 'カット'], [1, '1拍'], [2, '2拍'], [4, '4拍']];
const vjFadeLen = () => (UIPREF.vjFadeLen ?? 2);

function toggleVJRemote(open) {
  UIPREF.vjOpen = open ?? !UIPREF.vjOpen; if (UIPREF.vjOpen) UIPREF.vjMin = false;
  if (UIPREF.vjOpen && !UIPREF.vjHinted) { UIPREF.vjHinted = true; toast('VJリモコン：映像の素を押すと拍に合わせて切り替わります。パッドはキーボードの Z X C V N M でも叩けます', 6000); }
  saveUIPref(); buildVJRemote(); updateRemote();
}
/* 映像の素を選ぶ。「フェードで切り替え」のときは、見えていない方のデッキに入れて拍に合わせて切り替える */
function vjPickSource(over) {
  const V = S.vj;
  if (vjTake()) {
    const hid = vjShownKey() === 'a' ? 'b' : 'a';
    V[hid] = { ...deepClone(V[vjShownKey()]), ...over };   // 速さ・数・ズームは今のものを引き継ぐ
    if (over.gen === 'image') V[hid].imgAt = Math.ceil(F.beat);
    UIPREF.vjDeck = hid; saveUIPref();
    vjFade(hid === 'b' ? 1 : 0, vjFadeLen());
  } else { Object.assign(V[vjDeckKey()], over); if (over.gen === 'image') V[vjDeckKey()].imgAt = Math.floor(F.beat); scheduleSave(); }
}
function vjApplyPreset(p) {
  const V = S.vj;
  V.a = { ...V.a, speed: 1, count: 6, zoom: 1, img: 0, ...deepClone(p.a) }; V.b = { ...V.b, speed: 1, count: 6, zoom: 1, img: 1, ...deepClone(p.b) };
  for (const k in V.fx) V.fx[k] = false; Object.assign(V.fx, p.fx || {});
  V.pal = p.pal ?? -1; V.colorMode = p.colorMode || 'raw'; V.mirror = p.mirror || 0; V.kaleN = p.kaleN || 6; V.hue = p.hue || 0;
  if (p.imgRate !== undefined) V.imgRate = p.imgRate;
  if ((p.a.gen === 'image' || p.b.gen === 'image') && !vjImages().length) toast('画像バンクが空です。「画像」の＋で画像を追加してください', 3500);
  VJRT.fade = null; V.xf = 0; V.xfAuto = 0; UIPREF.vjDeck = 'a'; saveUIPref(); scheduleSave();
}
const VJ_SLOT_KEYS = ['a', 'b', 'xf', 'pal', 'colorMode', 'hue', 'bright', 'fx', 'rate', 'amt', 'pump', 'mirror', 'kaleN', 'text', 'textOn', 'textAnim', 'textStyle', 'imgRate'];
function vjSaveSlot() {
  const V = S.vj; const i = V.slots.findIndex(x => !x);
  if (i < 0) { toast('シーンがいっぱいです（×で消してから登録してください）'); return; }
  const o = {}; for (const k of VJ_SLOT_KEYS) o[k] = deepClone(V[k]);
  const g = VJ_GENS[VJ_GEN_ID[V[vjShownKey()].gen]]; o.name = (g ? g[2] : 'VJ') + (V.fx.kaleido ? '＋万華鏡' : V.fx.glitch ? '＋グリッチ' : '');
  V.slots[i] = o; scheduleSave(); buildVJRemote(); toast(`VJシーン${i + 1}「${o.name}」を登録しました`);
}
function vjRecallSlot(i) {
  const o = S.vj.slots[i]; if (!o) return;
  for (const k of VJ_SLOT_KEYS) if (o[k] !== undefined) S.vj[k] = deepClone(o[k]);
  S.vj.fx = { ...baseVJ().fx, ...S.vj.fx }; VJRT.fade = null; S.vj.xfAuto = 0;
  for (const k of ['a', 'b']) { delete S.vj[k].mix; if (!(S.vj[k].gen in VJ_GEN_ID)) S.vj[k].gen = 'tunnel'; }   // 合成（v0.6〜0.7）で登録したシーン：合成は無くなったのでトンネルに
  scheduleSave();
}
/* 画像を読み込む：大きい写真は縮める（GPU と自動保存の容量のため） */
function vjAddImages() {
  const inp = h('input', { type: 'file', accept: 'image/*', multiple: true });
  inp.onchange = async () => {
    const files = [...inp.files]; let n = 0;
    for (const f of files) {
      try {
        const url = await readAsDataURL(f);
        const img = await new Promise((ok, ng) => { const im = new Image(); im.onload = () => ok(im); im.onerror = ng; im.src = url; });
        const k = Math.min(1, 1280 / Math.max(img.width, img.height));
        const cv = document.createElement('canvas'); cv.width = Math.max(1, Math.round(img.width * k)); cv.height = Math.max(1, Math.round(img.height * k));
        cv.getContext('2d').drawImage(img, 0, 0, cv.width, cv.height);
        const id = uid('img'); IMAGES[id] = /png|gif|webp/.test(f.type) ? cv.toDataURL('image/png') : cv.toDataURL('image/jpeg', 0.88);
        S.vj.images.push(id); n++;
      } catch (e) { console.warn(e); }
    }
    if (n) { scheduleSave(); buildVJRemote(); toast(`画像を${n}枚追加しました`); }
    else if (files.length) toast('画像を読み込めませんでした');
  };
  inp.click();
}
function vjRemoveImage(id) {
  const V = S.vj; V.images = V.images.filter(x => x !== id);
  const t = VJ_IMG_CACHE.get(id); if (t) { t.dispose(); VJ_IMG_CACHE.delete(id); }
  if (!JSON.stringify(S).includes(id)) delete IMAGES[id];   // ほかで使っていなければ消す（自動保存を軽くする）
  scheduleSave(); buildVJRemote();
}

function buildVJRemote() {
  const r = $('#vjRemote'); if (!r) return;
  document.body.classList.toggle('vjOpen', !!UIPREF.vjOpen);
  r.classList.toggle('hide', !UIPREF.vjOpen);
  r.innerHTML = ''; VJUI.refs = [];
  if (!UIPREF.vjOpen) return;
  const V = () => S.vj;
  const sep = () => h('span', { class: 'rmSep' });
  // ヘッダー
  VJUI.beatEl = h('div', { class: 'beat' }); VJUI.bpmEl = h('span', { class: 'bpm' }, String(S.lights.bpm));
  const head = h('div', { class: 'vjHead', title: 'ここをドラッグして移動できます' },
    h('span', { class: 'rmGrip' }, '⠿'), h('span', { class: 'vjTitle' }, 'VJ'), VJUI.beatEl, VJUI.bpmEl,
    vjBtn('TAP', tapTempo, null, '', '拍に合わせて4回以上押すとBPMを合わせます（照明と共通）'),
    sep(),
    vjBtn('モニター', () => setMonitorMode('all', monAnyVJ() ? 'live' : 'vj'), () => monAnyVJ(), 'out', 'カメラ映像を映しているLEDモニター（メイン・サブ両方）を、VJ映像に切り替える（別々にするときは下の「出力先」）'),
    vjBtn('背景LED', () => { vjSet({ backdrop: !V().backdrop }); if (V().backdrop && !BD_LEDS.length) toast('いまの背景には LED パネルがありません（ステージタブの「背景」で LED のある背景を選んでください）', 4500); }, () => V().backdrop, 'out', '背景の LED パネルに VJ 映像を映す'),
    sep(),
    vjBtn('全画面', () => (VJOUT.full ? vjExitFull() : vjEnterFull()), () => VJOUT.full, '', '映像だけを全画面に出す（Esc で戻る・Tab でリモコンを隠す）'),
    vjBtn('別窓', () => (VJOUT.win ? vjCloseWindow() : vjOpenWindow()), () => !!VJOUT.win, '', '映像だけを別のウィンドウに出す（プロジェクター・OBS 用）'),
    h('span', { class: 'rmFill' }),
    vjBtn(UIPREF.vjMin ? '▴' : '▾', () => { UIPREF.vjMin = !UIPREF.vjMin; saveUIPref(); buildVJRemote(); }, null, 'min', UIPREF.vjMin ? '開く' : 'たたむ'),
    vjBtn('×', () => { if (VJOUT.full) vjExitFull(); toggleVJRemote(false); }, null, 'min', 'VJリモコンを閉じる（J）'));
  // プレビュー（透明の穴：WebGL がここに描く）
  VJUI.labelEl = h('div', { class: 'vjPrevLbl' });
  VJUI.prevEl = h('div', { class: 'vjPrev', title: 'ダブルクリックで全画面', ondblclick: () => vjEnterFull() }, VJUI.labelEl);
  const body = h('div', { class: 'vjBody' });
  const sec = (title, ...kids) => { const s = h('div', { class: 'rmSec' }, h('div', { class: 'rmSecT' }, title), h('div', { class: 'rmBtns' }, ...kids)); body.append(s); return s; };
  const brk = () => h('div', { class: 'rmBreak' });
  const lbl = (t) => h('span', { class: 'rmLbl2' }, t);
  // パッド（押した瞬間に効く）
  const pad = (label, key, down, up, test, cls = '') => {
    const b = h('button', { class: 'rb pad ' + cls, title: `${label}（キー ${key}）` }, label, h('small', {}, key));
    b.addEventListener('pointerdown', (e) => { e.preventDefault(); down(); b.classList.add('hit'); vjUIUpdate(); });
    const rel = () => { b.classList.remove('hit'); if (up) { up(); vjUIUpdate(); } };
    b.addEventListener('pointerup', rel); b.addEventListener('pointerleave', () => { if (b.classList.contains('hit')) rel(); }); b.addEventListener('pointercancel', rel);
    if (test) VJUI.refs.push({ el: b, test });
    return b;
  };
  body.append(h('div', { class: 'vjPads' },
    pad('フラッシュ', 'Z', () => vjTrigger('flash')), pad('反転', 'X', () => vjTrigger('invert')), pad('グリッチ', 'C', () => vjTrigger('glitch')),
    pad('ズーム', 'V', () => vjTrigger('zoom')), pad('ストロボ', 'N', () => { VJ_HOLD.strobe = true; }, () => { VJ_HOLD.strobe = false; }),
    pad('暗転', 'M', () => vjSet({ black: !V().black }), null, () => V().black, 'warn')));
  // デッキ・クロスフェーダー
  const deckBox = (k) => {
    const g = VJ_GENS[VJ_GEN_ID[V()[k].gen]] || VJ_GENS[0];
    const b = vjBtn(h('span', {}, h('b', {}, k.toUpperCase()), ' ' + g[2]), () => { UIPREF.vjDeck = k; saveUIPref(); buildVJRemote(); }, () => vjDeckKey() === k, 'deck', `デッキ${k.toUpperCase()}：${g[1]}（押すとこのデッキを編集）`);
    return b;
  };
  VJUI.xfEl = h('input', { type: 'range', min: 0, max: 1, step: 0.01, value: V().xf, class: 'vjXf', title: 'クロスフェーダー（A ↔ B）' });
  VJUI.xfEl.addEventListener('input', () => { VJRT.fade = null; S.vj.xfAuto = 0; S.vj.xf = +VJUI.xfEl.value; vjUIUpdate(); });
  VJUI.xfEl.addEventListener('change', scheduleSave);
  const deckSec = sec('デッキ', deckBox('a'), VJUI.xfEl, deckBox('b'), brk(),
    vjBtn('◀ A', () => vjFade(0, vjFadeLen()), () => V().xf <= 0.001 && !V().xfAuto, '', 'A に切り替え（拍に合わせて）'),
    vjBtn('⇄', () => vjFade(vjShownKey() === 'a' ? 1 : 0, vjFadeLen()), null, '', 'もう一方に切り替え（拍に合わせて）'),
    vjBtn('B ▶', () => vjFade(1, vjFadeLen()), () => V().xf >= 0.999 && !V().xfAuto, '', 'B に切り替え（拍に合わせて）'),
    lbl('切替'), ...VJ_FADE_LENS.map(([v, l]) => vjBtn(l, () => { UIPREF.vjFadeLen = v; saveUIPref(); }, () => vjFadeLen() === v, 'sm', '切り替えにかける長さ')),
    brk(), lbl('拍で交互'), ...[[0, 'なし'], [1, '1拍'], [2, '2拍'], [4, '4拍']].map(([v, l]) => vjBtn(l, () => { VJRT.fade = null; vjSet({ xfAuto: v }); if (!v) S.vj.xf = Math.round(S.vj.xf); }, () => (V().xfAuto || 0) === v, 'sm', 'A と B を拍ごとにパッと切り替える')));
  deckSec.classList.add('vjDeckSec');
  // 映像の素
  const genSec = sec('映像の素',
    ...VJ_GENS.map(([k, full, short]) => vjBtn(short, () => {
      if (k === 'image' && !vjImages().length) { toast('画像バンクが空です。下の「画像」の＋で追加してください', 3500); }
      if (k === 'text') { if (!S.vj.text) S.vj.text = 'LIVE!'; }
      vjPickSource({ gen: k });
    }, () => vjDeck().gen === k, 'gen' + (k.startsWith('p_') ? ' old' : ''), full)),
    brk(),
    vjBtn('選んだら切り替え', () => { UIPREF.vjTake = !vjTake(); saveUIPref(); buildVJRemote(); }, () => vjTake(), 'sm', 'オン：見えていない方のデッキに入れて、拍に合わせて切り替えます\nオフ：いま選んでいるデッキを直接変えます'),
    brk(), lbl('速さ'), ...[[0.25, '×¼'], [0.5, '×½'], [1, '×1'], [2, '×2'], [4, '×4']].map(([v, l]) => vjBtn(l, () => { vjDeck().speed = v; scheduleSave(); }, () => (vjDeck().speed ?? 1) === v, 'sm', '拍に対する動きの速さ')),
    brk(), lbl('数'), vjBtn('−', () => { vjDeck().count = clamp((vjDeck().count ?? 6) - 1, 1, 12); scheduleSave(); }, null, 'sm', '角の数・本数・細かさ'),
    h('span', { class: 'vjVal', 'data-v': 'count' }), vjBtn('＋', () => { vjDeck().count = clamp((vjDeck().count ?? 6) + 1, 1, 12); scheduleSave(); }, null, 'sm'),
    lbl('　ズーム'), vjBtn('−', () => { vjDeck().zoom = clamp((vjDeck().zoom ?? 1) / 1.25, 0.4, 3); scheduleSave(); }, null, 'sm'),
    h('span', { class: 'vjVal', 'data-v': 'zoom' }), vjBtn('＋', () => { vjDeck().zoom = clamp((vjDeck().zoom ?? 1) * 1.25, 0.4, 3); scheduleSave(); }, null, 'sm'));
  VJUI.genTitle = genSec.querySelector('.rmSecT');
  // 画像
  const ims = vjImages();
  const thumbs = ims.map((id, i) => {
    const b = h('button', { class: 'vjThumb', title: `画像${i + 1}を映す`, onclick: () => { vjPickSource({ gen: 'image', img: i }); vjUIUpdate(); } },
      h('img', { src: IMAGES[id], alt: '' }), h('span', { class: 'x', title: 'バンクから外す', onclick: (e) => { e.stopPropagation(); vjRemoveImage(id); } }, '×'));
    VJUI.refs.push({ el: b, test: () => { const D = S.vj[vjShownKey()]; return D.gen === 'image' && vjImgIndex(D) === i; } });
    return b;
  });
  sec(`画像（${ims.length}枚）`, ...thumbs, vjBtn('＋画像', vjAddImages, null, 'add', '画像を追加（何枚でも。大きい写真は自動で縮めます）'), brk(),
    lbl('拍で次の画像へ'), ...[[0, 'なし'], [1, '1拍'], [2, '2拍'], [4, '4拍'], [8, '8拍']].map(([v, l]) => vjBtn(l, () => vjSet({ imgRate: v }), () => (V().imgRate || 0) === v, 'sm')));
  // 色
  const sw = [vjBtn('照明', () => vjSet({ pal: -1 }), () => V().pal < 0, 'sm', '照明（いまのシーン）と同じ色')].concat(PALETTES.map((p, i) => {
    const b = vjBtn('', () => vjSet({ pal: i }), () => V().pal === i, 'sw', p.name); b.style.background = `linear-gradient(90deg, ${p.c[0]} 0 34%, ${p.c[1]} 34% 67%, ${p.c[2]} 67%)`; return b; }));
  sec('色', ...sw, brk(), lbl('色の付け方'), ...VJ_COLOR_MODES.map(([k, l]) => vjBtn(l, () => vjSet({ colorMode: k }), () => V().colorMode === k, 'sm',
    { raw: '映像の素の色のまま', grad: '明るさに合わせて3色のグラデーションで塗る', duo: '暗い所と明るい所を2色で塗る', mono: '白黒' }[k])),
    brk(), lbl('色が回る'), ...[[0, 'なし'], [0.5, 'ゆっくり'], [2, 'はやい']].map(([v, l]) => vjBtn(l, () => vjSet({ hue: v }), () => (V().hue || 0) === v, 'sm', '拍に合わせて色相が回る')));
  // エフェクト
  sec('エフェクト', ...VJ_FX.map(([k, l, d]) => vjBtn(l, () => { S.vj.fx[k] = !S.vj.fx[k]; scheduleSave(); }, () => !!V().fx[k], 'fxt', d)),
    brk(), lbl('ミラー'), ...VJ_MIRRORS.map((l, i) => vjBtn(l, () => vjSet({ mirror: i }), () => (V().mirror || 0) === i, 'sm')),
    brk(), lbl('万華鏡'), ...[3, 4, 6, 8, 12].map(n => vjBtn(String(n), () => { S.vj.fx.kaleido = true; vjSet({ kaleN: n }); }, () => V().fx.kaleido && V().kaleN === n, 'sm', `万華鏡の折り返し数（${n}）`)),
    brk(), lbl('ドンの間隔'), ...[[0.5, '½拍'], [1, '1拍'], [2, '2拍'], [4, '4拍']].map(([v, l]) => vjBtn(l, () => vjSet({ rate: v }), () => (V().rate || 1) === v, 'sm', 'ズーム・フラッシュ・グリッチなどが効く間隔')),
    brk(), lbl('ドゥン量'), ...[[0, 'なし'], [0.3, '弱'], [0.6, '中'], [1, '強']].map(([v, l]) => vjBtn(l, () => vjSet({ pump: v }), () => Math.abs((V().pump ?? 0.5) - v) < 0.05 || (v === 0.6 && Math.abs((V().pump ?? 0.5) - 0.5) < 0.05), 'sm', '拍ごとに明るさと大きさが脈打つ量')),
    brk(), lbl('FX量'), ...[[0.5, '弱'], [1, '中'], [1.6, '強']].map(([v, l]) => vjBtn(l, () => vjSet({ amt: v }), () => Math.abs((V().amt ?? 1) - v) < 0.05, 'sm', 'エフェクトのかかり具合')),
    lbl('　明るさ'), ...[[0.5, '50%'], [0.75, '75%'], [1, '100%'], [1.3, '130%']].map(([v, l]) => vjBtn(l, () => vjSet({ bright: v }), () => Math.abs((V().bright ?? 1) - v) < 0.05, 'sm')));
  // 文字
  const ti = h('input', { type: 'text', value: V().text || '', placeholder: '映したい文字', class: 'vjText' });
  const applyText = () => { const t = ti.value.slice(0, 40); if (t === S.vj.text) return; vjSet({ text: t }); if (t && !S.vj.textOn && S.vj.a.gen !== 'text' && S.vj.b.gen !== 'text') { S.vj.textOn = true; } vjUIUpdate(); };
  ti.addEventListener('change', applyText); ti.addEventListener('keydown', e => { e.stopPropagation(); if (e.key === 'Enter') { applyText(); ti.blur(); } });
  sec('文字', ti, vjBtn('重ねる', () => vjSet({ textOn: !V().textOn }), () => V().textOn, '', '映像の上に文字を重ねる'), brk(),
    lbl('動き'), ...[['pulse', 'ドン'], ['blink', '点滅'], ['stay', '固定']].map(([k, l]) => vjBtn(l, () => vjSet({ textAnim: k }), () => V().textAnim === k, 'sm')),
    lbl('　字'), ...[['neon', 'ネオン'], ['plain', 'くっきり']].map(([k, l]) => vjBtn(l, () => vjSet({ textStyle: k }), () => V().textStyle === k, 'sm')));
  // オート
  sec('オートVJ', vjBtn('オート', () => { vjSet({ auto: !V().auto }); toast(S.vj.auto ? `オートVJ：${S.vj.autoBars}小節ごとに映像とエフェクトを入れ替えます` : 'オートVJを止めました'); }, () => V().auto, '', '決まった小節ごとに、映像の素とエフェクトを自動で入れ替える'),
    lbl('　間隔'), ...[1, 2, 4, 8].map(n => vjBtn(`${n}小節`, () => vjSet({ autoBars: n }), () => V().autoBars === n, 'sm')));
  // シーン
  const slots = [];
  V().slots.forEach((o, i) => { if (!o) return; slots.push(h('span', { class: 'vjSlot' }, vjBtn(`${i + 1} ${o.name}`, () => vjRecallSlot(i), null, 'scene', `VJシーン${i + 1}「${o.name}」を呼び出す`),
    h('span', { class: 'x', title: '消す', onclick: (e) => { e.stopPropagation(); S.vj.slots[i] = null; scheduleSave(); buildVJRemote(); } }, '×'))); });
  sec('シーン', ...VJ_PRESETS.map(p => vjBtn(p.name, () => vjApplyPreset(p), null, 'preset', `組み込みのシーン「${p.name}」`)), brk(),
    ...slots, vjBtn('＋登録', vjSaveSlot, null, 'add', 'いまの VJ の状態をシーンとして登録'));
  // 出力先
  const nVJMon = REG.monitors.filter(m => m.inst.p?.src === 'vj').length, camMons = REG.monitors.filter(m => m.inst.p?.src === 'cam1' || m.inst.p?.src === 'cam2');
  const nMain = camMons.filter(m => monGroup(m.inst) === 'main').length, nSub = camMons.length - nMain;
  const monRow = (g, l) => [lbl(l), vjBtn('ライブ映像', () => setMonitorMode(g, 'live'), () => S.lights[monModeKey(g)] !== 'vj', 'sm', `${l}のカメラ映像のモニターにステージカメラの映像を映す`),
    vjBtn('VJ映像', () => setMonitorMode(g, 'vj'), () => S.lights[monModeKey(g)] === 'vj', 'sm', `${l}のカメラ映像のモニターを VJ 映像に切り替える`), brk()];
  sec('出力先',
    ...monRow('main', 'メイン'), ...monRow('sub', 'サブ'),
    lbl('背景LED'), vjBtn('映す', () => vjSet({ backdrop: true }), () => V().backdrop, 'sm'), vjBtn('映さない', () => vjSet({ backdrop: false }), () => !V().backdrop, 'sm'), brk(),
    lbl('映し方'), vjBtn('1枚ずつ', () => vjSet({ map: 'same' }), () => V().map !== 'span', 'sm', 'どの画面にも同じ映像（画面の形に合わせて切り抜き）'),
    vjBtn('つなげて1枚', () => vjSet({ map: 'span' }), () => V().map === 'span', 'sm', '全部の画面をつなげて、1枚の大きな映像として映す'), brk(),
    h('div', { class: 'note' }, `カメラ映像のモニター：メイン ${nMain}台・サブ ${nSub}台（それぞれ「VJ映像」で切り替わる）・「VJリモコンの映像」のモニター ${nVJMon}台・背景のLEDパネル ${BD_LEDS.length}枚。`,
      h('br'), 'メインかサブかは、アセットタブでモニターを選んで「リモコンの区分」で決めます（自動：左右ミラーで複製したモニターはサブ）。いつも VJ にするときは「映すもの」を「VJリモコンの映像」に。'));
  const main = h('div', { class: 'vjMain' }, h('div', { class: 'vjLeft' }, VJUI.prevEl, body));
  r.append(head, main);
  r.classList.toggle('min', !!UIPREF.vjMin);
  // ドラッグ移動
  head.addEventListener('pointerdown', (e) => {
    if (e.target.closest('button')) return;
    const vr = viewEl.getBoundingClientRect(), rr = r.getBoundingClientRect(); const ox = e.clientX - rr.left, oy = e.clientY - rr.top;
    head.setPointerCapture(e.pointerId);
    const mv = (ev) => { UIPREF.vjPos = { x: ev.clientX - vr.left - ox, y: ev.clientY - vr.top - oy }; placeVJRemote(); };
    const up = () => { head.removeEventListener('pointermove', mv); head.removeEventListener('pointerup', up); saveUIPref(); };
    head.addEventListener('pointermove', mv); head.addEventListener('pointerup', up);
  });
  placeVJRemote(); vjUIUpdate();
}
function placeVJRemote() {
  const r = $('#vjRemote'); if (!r || r.classList.contains('hide')) return;
  const W = viewEl.clientWidth, H = viewEl.clientHeight, w = r.offsetWidth, hh = r.offsetHeight;
  const top0 = VJOUT.full ? 8 : 52;
  if (UIPREF.vjPos) { r.style.left = clamp(UIPREF.vjPos.x, 0, Math.max(0, W - w)) + 'px'; r.style.top = clamp(UIPREF.vjPos.y, top0, Math.max(top0, H - hh)) + 'px'; }
  else { r.style.left = '10px'; r.style.top = top0 + 'px'; }
}
function vjUIUpdate() {
  if (!UIPREF.vjOpen) return;
  for (const ref of VJUI.refs) ref.el.classList.toggle('on', !!ref.test());
  const D = vjDeck();
  if (VJUI.genTitle) VJUI.genTitle.textContent = `映像の素（デッキ${vjDeckKey().toUpperCase()}${vjTake() ? '・押すと拍で切り替え' : 'を編集'}）`;
  document.querySelectorAll('#vjRemote .vjVal[data-v]').forEach(el => { const k = el.dataset.v; el.textContent = k === 'zoom' ? '×' + (D.zoom ?? 1).toFixed(2).replace(/0$/, '') : String(D.count ?? 6); });
  // デッキの名前（オート・フェードで変わる）
  document.querySelectorAll('#vjRemote .rb.deck').forEach((el, i) => { const k = i ? 'b' : 'a'; const g = VJ_GENS[VJ_GEN_ID[S.vj[k].gen]] || VJ_GENS[0]; const sp = el.querySelector('span'); if (sp) sp.lastChild.textContent = ' ' + g[2]; el.title = `デッキ${k.toUpperCase()}：${g[1]}（押すとこのデッキを編集）`; });
}
/* 毎フレーム：クロスフェーダー・拍のランプ・BPM・プレビューの説明を更新（軽い処理だけ） */
function vjUITick() {
  if (!UIPREF.vjOpen || !VJUI.xfEl) return;
  if (document.activeElement !== VJUI.xfEl && Math.abs(+VJUI.xfEl.value - VJP.xf) > 0.004) VJUI.xfEl.value = VJP.xf;
  const bi = Math.floor(LT.beat); if (bi !== VJUI.lastBeat && VJUI.beatEl) { VJUI.lastBeat = bi; VJUI.beatEl.classList.add('on'); setTimeout(() => VJUI.beatEl && VJUI.beatEl.classList.remove('on'), 90); }
  if (VJUI.bpmEl && VJUI.bpmEl.textContent !== String(S.lights.bpm)) VJUI.bpmEl.textContent = S.lights.bpm;
  if (VJUI.labelEl) {
    const outs = []; if (REG.monitors.some(m => monitorShowsVJ(m.inst))) outs.push('モニター'); if (S.vj.backdrop && BD_LEDS.length) outs.push('背景');
    if (VJOUT.win) outs.push('別窓');
    const txt = (S.vj.black ? '■ 暗転中　' : '') + (outs.length ? '出力：' + outs.join('・') : 'プレビューのみ（上の「モニター」「背景LED」で映す）');
    if (VJUI.labelEl.textContent !== txt) { VJUI.labelEl.textContent = txt; VJUI.labelEl.classList.toggle('warn', !!S.vj.black); }
  }
  if (VJRT.fade || S.vj.xfAuto || S.vj.auto || S.vj.imgRate > 0) { if (!vjUITick.n || ++vjUITick.n > 15) { vjUITick.n = 1; vjUIUpdate(); } }
}
/* プレビューの穴に VJ 映像を描く（メイン画面を描いた後） */
function renderVJPreview() {
  if (!UIPREF.vjOpen || UIPREF.vjMin || !VJ_MAIN || !VJUI.prevEl || !VJUI.prevEl.isConnected) return;
  const cr = renderer.domElement.getBoundingClientRect(), pr = VJUI.prevEl.getBoundingClientRect();
  const x = Math.round(pr.left - cr.left), y = Math.round(cr.bottom - pr.bottom), w = Math.round(pr.width), hh = Math.round(pr.height);
  if (w < 4 || hh < 4 || x + w <= 0 || y + hh <= 0 || x >= cr.width || y >= cr.height) return;
  VJ_MAIN.blit(x, y, w, hh);
}

/* キーボード（パッド）：Z フラッシュ / X 反転 / C グリッチ / V ズーム / N ストロボ（押している間） / M 暗転 / J リモコン / Tab 全画面で隠す */
window.addEventListener('keydown', e => {
  const tg = e.target.tagName; if (tg === 'INPUT' || tg === 'SELECT' || tg === 'TEXTAREA') return;
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  const k = e.key.toLowerCase();
  if (k === 'tab' && VJOUT.full) { e.preventDefault(); document.body.classList.toggle('vjHideUI'); return; }
  if (e.repeat) return;
  if (k === 'z') vjTrigger('flash'); else if (k === 'x') vjTrigger('invert'); else if (k === 'c') vjTrigger('glitch'); else if (k === 'v') vjTrigger('zoom');
  else if (k === 'n') VJ_HOLD.strobe = true;
  else if (k === 'm') { S.vj.black = !S.vj.black; scheduleSave(); }
  else if (k === 'j') toggleVJRemote();
  else return;
  vjUIUpdate();
});
window.addEventListener('keyup', e => { if (e.key.toLowerCase() === 'n') { VJ_HOLD.strobe = false; vjUIUpdate(); } });
window.addEventListener('blur', () => { VJ_HOLD.strobe = false; });
