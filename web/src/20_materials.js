
/* =====================================================================
   マテリアル / シェーダー / 生成テクスチャ
   ===================================================================== */
function stdMat(o = {}) {
  const m = new THREE.MeshStandardMaterial({
    color: o.color ?? '#888888', roughness: o.roughness ?? 0.6, metalness: o.metalness ?? 0.1,
    side: o.side ?? THREE.FrontSide, map: o.map ?? null, flatShading: !!o.flat,
    transparent: !!o.transparent, opacity: o.opacity ?? 1,
  });
  m.envMapIntensity = o.env ?? 0.3;
  if (o.emissive) { m.emissive = new THREE.Color(o.emissive); m.emissiveIntensity = o.ei ?? 1; }
  if (o.emissiveMap) m.emissiveMap = o.emissiveMap;
  if (o.surf) applySurface(m, o.surf, { tile: o.tile, ns: o.ns, args: o.args, ao: o.ao });   // 質感（25_surfaces.js）
  return m;
}
function glowMat(hex, s = 2, o = {}) {
  const m = new THREE.MeshBasicMaterial({
    color: col(hex, s), fog: o.fog ?? true, transparent: !!(o.transparent || o.additive), opacity: o.opacity ?? 1,
    blending: o.additive ? THREE.AdditiveBlending : THREE.NormalBlending, depthWrite: !o.additive,
    side: o.side ?? THREE.FrontSide,
  });
  // Unity インポーター用：色とHDRの強さを名前に残す（GLBの色は0〜1に丸められるため）
  m.name = `SKGlow_${new THREE.Color(hex).getHexString()}_x${s.toFixed(2)}` + ((o.transparent || o.additive) ? `_o${(o.opacity ?? 1).toFixed(2)}` : '') + (o.additive ? '_add' : '');
  return m;
}
const metalMat = (hex = '#b8bcc8') => stdMat({ color: hex, metalness: 0.85, roughness: 0.32, env: 0.7, surf: 'alu' });
// よく使う質感の組み合わせ
const paintMat = (hex, o = {}) => stdMat({ color: hex, roughness: 0.45, metalness: 0.45, env: 0.5, surf: 'powder', ...o });   // 灯体・機材の筐体
const cabMat = (hex, o = {}) => stdMat({ color: hex, roughness: 0.75, metalness: 0.05, env: 0.25, surf: 'tolex', ...o });     // スピーカーの外装

/* ---------- 体積ビーム（フェイクボリューメトリック：VRCでも同じ方式が定番） ---------- */
const BEAM_VS = /* glsl */`
varying float vY; varying vec3 vN; varying vec3 vV; varying vec3 vWP;
void main(){
  vY = uv.y;
  vec4 mv = modelViewMatrix * vec4(position,1.0);
  vN = normalize(normalMatrix * normal);
  vV = normalize(-mv.xyz);
  vWP = (modelMatrix * vec4(position,1.0)).xyz;
  gl_Position = projectionMatrix * mv;
}`;
const BEAM_FS = /* glsl */`
uniform vec3 uColor; uniform float uInt; uniform float uLen; uniform float uHaze; uniform float uEndFade; uniform float uTime;
varying float vY; varying vec3 vN; varying vec3 vV; varying vec3 vWP;
float h3(vec3 p){ return fract(sin(dot(p, vec3(12.9898,78.233,37.719)))*43758.5453); }
void main(){
  float d = vY * uLen;
  float along = exp(-d * 0.05) * smoothstep(0.0, 0.35, d);
  along *= mix(1.0, 1.0 - smoothstep(0.75, 1.0, vY), uEndFade);
  float edge = pow(abs(dot(normalize(vN), normalize(vV))), 1.6);
  float camFade = smoothstep(0.6, 5.0, length(vWP - cameraPosition));
  float dust = 0.85 + 0.15 * sin(vWP.y * 3.0 + uTime * 0.7 + vWP.x * 2.0);
  float a = uInt * uHaze * along * edge * camFade * dust * 0.28;
  gl_FragColor = vec4(uColor, clamp(a, 0.0, 4.0));
}`;
const beamGeoCache = new Map();
function beamGeometry(rad0 = 0.012) {
  const key = rad0.toFixed(4);
  if (beamGeoCache.has(key)) return beamGeoCache.get(key);
  // 単位長さ1・先端半径1の円錐（scale で 長さ/太さ を決める）
  const g = new THREE.CylinderGeometry(1, rad0, 1, 28, 1, true);
  g.translate(0, 0.5, 0);
  g.userData.shared = true;
  beamGeoCache.set(key, g);
  return g;
}
function beamMat() {
  return new THREE.ShaderMaterial({
    uniforms: { uColor: { value: new THREE.Color(1, 1, 1) }, uInt: { value: 1 }, uLen: { value: 20 }, uHaze: { value: 0.7 }, uEndFade: { value: 1 }, uTime: { value: 0 } },
    vertexShader: BEAM_VS, fragmentShader: BEAM_FS, transparent: true, depthWrite: false,
    blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
  });
}
/* 床に落ちる光だまり */
const SPOT_FS = /* glsl */`
uniform vec3 uColor; uniform float uInt; varying vec2 vUv;
void main(){ float r = length(vUv-0.5)*2.0; float a = smoothstep(1.0, 0.0, r); a = a*a*(0.6+0.4*smoothstep(0.75,0.35,r)); gl_FragColor = vec4(uColor, a*uInt); }`;
const SIMPLE_VS = /* glsl */`varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.0); }`;
const spotGeo = new THREE.PlaneGeometry(1, 1).rotateX(-Math.PI / 2); spotGeo.userData.shared = true;
function spotMat() {
  return new THREE.ShaderMaterial({ uniforms: { uColor: { value: new THREE.Color(1, 1, 1) }, uInt: { value: 1 } },
    vertexShader: SIMPLE_VS, fragmentShader: SPOT_FS, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    polygonOffset: true, polygonOffsetFactor: -4, polygonOffsetUnits: -4 });
}

/* ---------- VJ映像パターン（LEDモニター / 背景LEDパネル共通） ---------- */
const PATTERN_LIST = [
  ['triangles', '三角コンフェッティ'], ['stripes', 'パステル虹ストライプ'], ['galaxy', '銀河・星雲'], ['tunnel', '三角トンネル'],
  ['blobs', 'ゆめかわ流体'], ['bars', 'イコライザー'], ['dots', 'ポップドット'], ['sparkle', 'キラキラ星'],
];
const PATTERN_IDS = Object.fromEntries(PATTERN_LIST.map((p, i) => [p[0], i]));
const PATTERN_GLSL = /* glsl */`
float h21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
float h11(float n){ return fract(sin(n*127.1)*43758.5453); }
float vnoise(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
  return mix(mix(h21(i),h21(i+vec2(1.0,0.0)),f.x), mix(h21(i+vec2(0.0,1.0)),h21(i+vec2(1.0,1.0)),f.x), f.y); }
vec3 hsv(float h,float s,float v){ vec3 k = clamp(abs(mod(h*6.0+vec3(0.0,4.0,2.0),6.0)-3.0)-1.0,0.0,1.0); return v*mix(vec3(1.0),k,s); }
mat2 rot2(float a){ float c=cos(a), s=sin(a); return mat2(c,-s,s,c); }
float sdTri(vec2 p, float r){ const float k = 1.7320508; p.x = abs(p.x) - r; p.y = p.y + r/k;
  if(p.x + k*p.y > 0.0) p = vec2(p.x - k*p.y, -k*p.x - p.y)/2.0; p.x -= clamp(p.x, -2.0*r, 0.0); return -length(p)*sign(p.y); }
vec3 vjPattern(int id, vec2 uv, float t, float beat, vec3 c1, vec3 c2, vec3 c3, float aspect){
  vec2 q = vec2(uv.x*aspect, uv.y);
  vec2 p = (uv-0.5)*vec2(aspect,1.0);
  float pulse = exp(-fract(beat)*4.0);
  vec3 col = vec3(0.0);
  if(id==0){
    col = mix(c1*0.45, c1, uv.y);
    for(int i=0;i<22;i++){ float fi=float(i);
      vec2 c = vec2(h11(fi*3.1)*aspect, fract(h11(fi+11.0)+t*0.035*(0.4+h11(fi+5.0)))*1.3-0.15);
      float s = 0.025+0.05*h11(fi+2.0);
      float d = sdTri(rot2(t*(h11(fi+9.0)-0.5)*1.6 + fi)*(q-c), s);
      float m = mod(fi,3.0);
      vec3 ci = m<1.0 ? c2 : (m<2.0 ? c3 : mix(c1,vec3(1.0),0.65));
      col = mix(col, ci, smoothstep(0.004,0.0,d)*0.9);
    }
    col *= 1.0 + 0.12*pulse;
  } else if(id==1){
    float s = (q.x + q.y*0.7)*2.2 - t*0.25;
    col = hsv(fract(s*0.2), 0.32, 1.0);
    vec2 g = floor(q*10.0);
    float m = h21(g + floor(t*1.5));
    col = mix(col, mix(c1,c2,h21(g+3.0)), step(0.72,m)*0.55);
    vec2 sg = fract(q*10.0)-0.5; float sp = h21(g+7.0);
    float star = max(smoothstep(0.03,0.0,abs(sg.x))*smoothstep(0.35,0.0,abs(sg.y)), smoothstep(0.03,0.0,abs(sg.y))*smoothstep(0.35,0.0,abs(sg.x)));
    col += vec3(1.0)*star*step(0.85,sp)*(0.5+0.5*sin(t*4.0+sp*30.0));
    col = mix(col, c3, 0.12);
  } else if(id==2){
    vec2 pp = p*2.0;
    float n = vnoise(pp*2.0+t*0.05)*0.6 + vnoise(pp*5.0-t*0.03)*0.4;
    col = mix(vec3(0.004,0.006,0.03), c1*0.7, smoothstep(0.35,0.95,n));
    col += c2*0.55*smoothstep(0.55,1.0,vnoise(pp*3.0+7.0+t*0.04));
    vec2 sg = floor(q*70.0); float st = h21(sg); float tw = 0.5+0.5*sin(t*3.0+st*50.0);
    col += c3*step(0.985, st)*tw*1.6*smoothstep(0.45,0.0,length(fract(q*70.0)-0.5));
  } else if(id==3){
    vec2 pp = p; pp.y += 0.08;
    float d = sdTri(pp, 0.06);
    float k = d*7.0 - beat*0.5;
    float band = fract(k);
    float line = smoothstep(0.32,0.18,abs(band-0.5));
    vec3 bc = mod(floor(k),2.0)<1.0 ? c1 : c2;
    col = mix(c1*0.03, bc, line*(0.55+0.45*pulse));
    vec2 g = fract(q*48.0)-0.5; col *= 0.75 + 0.25*smoothstep(0.5,0.2,length(g));
  } else if(id==4){
    vec2 pp = p*1.6;
    float a = vnoise(pp*1.5 + vec2(t*0.1,-t*0.07));
    float b = vnoise(pp*2.3 - vec2(t*0.08,t*0.05) + 4.0);
    col = mix(c1, c2, smoothstep(0.3,0.7,a));
    col = mix(col, c3, smoothstep(0.55,0.8,b)*0.7);
    col = mix(col, vec3(1.0), 0.15);
    float l = abs(fract(a*6.0)-0.5); col = mix(col, vec3(1.0), smoothstep(0.035,0.0,l)*0.35);
  } else if(id==5){
    float n = 24.0; float bx = floor(uv.x*n);
    float h = 0.12 + 0.78*pow(h11(bx*1.7 + floor(beat*2.0)*13.0),1.4)*(0.55+0.45*pulse);
    float on = step(uv.y, h) * step(0.14, fract(uv.x*n));
    float seg = step(0.22, fract(uv.y*28.0));
    col = vec3(0.004) + mix(c1, c2, uv.y/max(h,0.01))*on*seg;
    col += c3*step(abs(uv.y-h-0.02),0.012)*step(0.14, fract(uv.x*n));
  } else if(id==6){
    vec3 bg = mix(c1, c2, clamp(uv.y + 0.12*sin(t*0.5+uv.x*3.0),0.0,1.0));
    vec2 g = q*12.0; vec2 f = fract(g)-0.5;
    float r = 0.2+0.1*sin(beat*3.14159 + floor(g.x)*0.5 + floor(g.y)*0.3);
    float dd = smoothstep(r, r-0.05, length(f));
    col = mix(bg*0.85, mix(c3, vec3(1.0), 0.3), dd*0.75);
  } else {
    col = c1*0.07*(1.0-uv.y*0.6);
    for(int L=0; L<3; L++){ float fl=float(L);
      float N = 9.0 + fl*7.0; vec2 g = q*N + vec2(fl*3.7, t*0.03*(fl+1.0)); vec2 id = floor(g); vec2 f = fract(g)-0.5;
      float rnd = h21(id+fl*11.0);
      float tw = pow(0.5+0.5*sin(t*2.2+rnd*40.0),4.0);
      float star = max(smoothstep(0.035,0.0,abs(f.x))*smoothstep(0.4,0.0,abs(f.y)), smoothstep(0.035,0.0,abs(f.y))*smoothstep(0.4,0.0,abs(f.x)));
      star += smoothstep(0.12,0.0,length(f))*0.8;
      col += mix(c3, c2, h21(id+3.0))*star*tw*step(0.9,rnd)*1.8;
    }
  }
  return col;
}`;

const LED_FS = /* glsl */`
uniform sampler2D uTex; uniform float uSrc; uniform int uPat; uniform vec2 uDots; uniform float uGap; uniform float uRound;
uniform float uBright; uniform float uTime; uniform float uBeat; uniform float uAspect; uniform float uFlip; uniform float uLed;
uniform vec3 uC1; uniform vec3 uC2; uniform vec3 uC3; uniform vec3 uTint;
uniform vec4 uTexRect; uniform float uTexLod;
varying vec2 vUv;
${PATTERN_GLSL}
void main(){
  vec2 uv = vUv; if(uFlip>0.5) uv.x = 1.0-uv.x;
  vec2 g = uv*uDots;
  vec2 suv = uLed>0.5 ? (floor(g)+0.5)/uDots : uv;
  vec3 c;
  // uTexRect：映像のどの部分を映すか（VJ 映像を画面の縦横比に合わせて切り抜く・複数画面でつなげる）
  // uTexLod：ドット1個ぶんの平均色を取る（細かい模様がチラつかない）
  if(uSrc<0.5) c = textureLod(uTex, uTexRect.xy + suv*uTexRect.zw, uLed>0.5 ? uTexLod : 0.0).rgb;
  else if(uSrc<1.5) c = vjPattern(uPat, suv, uTime, uBeat, uC1, uC2, uC3, uAspect);
  else c = vec3(0.0);
  c *= uTint * uBright;
  if(uLed>0.5){
    vec2 f = fract(g)-0.5;
    float d = uRound>0.5 ? length(f) : max(abs(f.x),abs(f.y));
    float r = 0.5*(1.0-uGap);
    float w = fwidth(g.x) + fwidth(g.y);
    float m = 1.0 - smoothstep(r - w*0.6, r + w*0.6, d);
    float cov = uRound>0.5 ? 3.14159*r*r : 4.0*r*r;
    m = mix(m, cov, smoothstep(0.35, 1.0, w));
    c = c*m*(uRound>0.5 ? 1.25 : 1.0) + vec3(0.004)*(1.0-m);
  }
  gl_FragColor = vec4(c, 1.0);
}`;
const BLACK_TEX = new THREE.DataTexture(new Uint8Array([0, 0, 0, 255]), 1, 1); BLACK_TEX.needsUpdate = true;
function ledMat(o = {}) {
  return new THREE.ShaderMaterial({
    uniforms: {
      uTex: { value: BLACK_TEX }, uSrc: { value: 1 }, uPat: { value: PATTERN_IDS[o.pattern] ?? 0 },
      uDots: { value: new THREE.Vector2(o.dotsX ?? 96, o.dotsY ?? 54) }, uGap: { value: o.gap ?? 0.35 }, uRound: { value: o.round === false ? 0 : 1 },
      uBright: { value: o.bright ?? 1.2 }, uTime: { value: 0 }, uBeat: { value: 0 }, uAspect: { value: o.aspect ?? 1.78 },
      uFlip: { value: 0 }, uLed: { value: o.led === false ? 0 : 1 },
      uC1: { value: col(o.c1 ?? '#8fd3ff') }, uC2: { value: col(o.c2 ?? '#ff8ad8') }, uC3: { value: col(o.c3 ?? '#ffffff') },
      uTint: { value: col(o.tint ?? '#ffffff') },
      uTexRect: { value: new THREE.Vector4(0, 0, 1, 1) }, uTexLod: { value: 0 },
    },
    vertexShader: SIMPLE_VS, fragmentShader: LED_FS, fog: false,
  });
}
// アニメーション更新対象の LED マテリアル（背景パネル等）
const LIVE_LED_MATS = new Set();

/* ---------- 空 ---------- */
const SKY_VS = /* glsl */`varying vec3 vDir; void main(){ vDir = normalize(position); gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.0); }`;
const SKY_FS = /* glsl */`
uniform vec3 uTop; uniform vec3 uHor; uniform vec3 uBot; uniform float uStars; uniform float uClouds; uniform float uTime;
varying vec3 vDir;
float h31(vec3 p){ return fract(sin(dot(p, vec3(127.1,311.7,74.7)))*43758.5453); }
float n2(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
  float a=fract(sin(dot(i,vec2(127.1,311.7)))*43758.5453), b=fract(sin(dot(i+vec2(1,0),vec2(127.1,311.7)))*43758.5453);
  float c=fract(sin(dot(i+vec2(0,1),vec2(127.1,311.7)))*43758.5453), d=fract(sin(dot(i+vec2(1,1),vec2(127.1,311.7)))*43758.5453);
  return mix(mix(a,b,f.x),mix(c,d,f.x),f.y); }
float fbm(vec2 p){ float s=0.0, a=0.5; for(int i=0;i<5;i++){ s+=a*n2(p); p*=2.03; a*=0.5; } return s; }
void main(){
  vec3 d = normalize(vDir); float y = d.y;
  vec3 c = y>0.0 ? mix(uHor, uTop, pow(clamp(y,0.0,1.0), 0.55)) : mix(uHor, uBot, pow(clamp(-y,0.0,1.0), 0.35));
  if(uStars>0.0 && y>0.0){
    vec3 sp = d*260.0; vec3 id = floor(sp); float h = h31(id);
    float s = step(0.9965, h) * smoothstep(0.5, 0.0, length(fract(sp)-0.5)) * (0.5+0.5*sin(uTime*2.0+h*90.0));
    c += vec3(0.9,0.95,1.0)*s*uStars*2.5*smoothstep(0.0,0.25,y);
  }
  if(uClouds>0.0 && y>0.0){
    vec2 p = d.xz/(y+0.12)*1.2 + vec2(uTime*0.006, 0.0);
    float n = fbm(p);
    c = mix(c, vec3(1.0), smoothstep(0.52,0.78,n)*uClouds*smoothstep(0.0,0.25,y));
  }
  gl_FragColor = vec4(c,1.0);
}`;
function skyMat(o) {
  return new THREE.ShaderMaterial({
    uniforms: { uTop: { value: col(o.top) }, uHor: { value: col(o.hor) }, uBot: { value: col(o.bot) }, uStars: { value: o.stars ?? 0 }, uClouds: { value: o.clouds ?? 0 }, uTime: { value: 0 } },
    vertexShader: SKY_VS, fragmentShader: SKY_FS, side: THREE.BackSide, depthWrite: false, fog: false,
  });
}

/* ---------- キャンバス生成テクスチャ ---------- */
const canvasTexCache = new Map();
function canvasTex(key, w, h, draw, o = {}) {
  if (canvasTexCache.has(key)) return canvasTexCache.get(key);
  const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
  draw(cv.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = o.linear ? THREE.NoColorSpace : THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 4;
  if (o.nearest) { t.magFilter = THREE.NearestFilter; }
  t.userData.shared = true;
  canvasTexCache.set(key, t);
  return t;
}
const texCheck = (a, b) => canvasTex('check' + a + b, 128, 128, (g, w, h) => {
  g.fillStyle = a; g.fillRect(0, 0, w, h); g.fillStyle = b; g.fillRect(0, 0, w / 2, h / 2); g.fillRect(w / 2, h / 2, w / 2, h / 2);
  g.strokeStyle = 'rgba(255,255,255,0.18)'; g.lineWidth = 2; g.strokeRect(0, 0, w, h);
});
const texGridLines = () => canvasTex('gridlines', 256, 256, (g, w, h) => {
  g.fillStyle = '#000'; g.fillRect(0, 0, w, h); g.strokeStyle = '#fff'; g.lineWidth = 5; g.strokeRect(0, 0, w, h);
  g.strokeStyle = 'rgba(255,255,255,0.25)'; g.lineWidth = 1; g.strokeRect(w * .25, h * .25, w * .5, h * .5);
});
const texStarry = () => canvasTex('starry', 512, 512, (g, w, h) => {
  g.fillStyle = '#000'; g.fillRect(0, 0, w, h);
  const r = mulberry32(7);
  for (let i = 0; i < 1400; i++) { const a = r(); g.fillStyle = `rgba(255,255,255,${0.15 + a * 0.85})`; const s = a > 0.97 ? 2.2 : 1.1; g.fillRect(r() * w, r() * h, s, s); }
});
const texWood = (c) => canvasTex('wood' + c, 256, 256, (g, w, h) => {
  g.fillStyle = c; g.fillRect(0, 0, w, h);
  const r = mulberry32(3);
  for (let i = 0; i < 8; i++) { g.fillStyle = `rgba(0,0,0,${0.05 + r() * 0.15})`; g.fillRect(0, i * 32, w, 32); g.fillStyle = 'rgba(0,0,0,0.5)'; g.fillRect(0, i * 32, w, 1.5); }
  for (let i = 0; i < 300; i++) { g.fillStyle = `rgba(255,255,255,${r() * 0.05})`; g.fillRect(r() * w, r() * h, 20 + r() * 60, 1); }
});
const texPanel = (c) => canvasTex('panel' + c, 128, 128, (g, w, h) => {
  g.fillStyle = c; g.fillRect(0, 0, w, h); g.fillStyle = 'rgba(0,0,0,0.45)'; g.fillRect(0, 0, 2, h); g.fillRect(0, 0, w, 2);
  g.fillStyle = 'rgba(255,255,255,0.05)'; g.fillRect(4, 4, w - 8, h * 0.3);
});
const texGrille = () => canvasTex('grille', 128, 128, (g, w, h) => {
  g.fillStyle = '#16161c'; g.fillRect(0, 0, w, h); g.fillStyle = '#26262e';
  for (let y = 0; y < h; y += 6) for (let x = (y / 6 % 2) * 3; x < w; x += 6) { g.beginPath(); g.arc(x, y, 1.6, 0, TAU); g.fill(); }
});
const texConcrete = (c) => canvasTex('conc' + c, 256, 256, (g, w, h) => {
  g.fillStyle = c; g.fillRect(0, 0, w, h); const r = mulberry32(11);
  for (let i = 0; i < 3000; i++) { g.fillStyle = `rgba(${r() > .5 ? '255,255,255' : '0,0,0'},${r() * 0.06})`; g.fillRect(r() * w, r() * h, 2, 2); }
});
const texGrass = (c) => canvasTex('grass' + c, 256, 256, (g, w, h) => {
  g.fillStyle = c; g.fillRect(0, 0, w, h); const r = mulberry32(5);
  for (let i = 0; i < 5000; i++) { const l = r(); g.fillStyle = `rgba(${l > .5 ? '220,255,160' : '0,40,0'},${0.08 + r() * 0.12})`; g.fillRect(r() * w, r() * h, 1, 3 + r() * 4); }
});
const texSoft = () => canvasTex('soft', 64, 64, (g, w, h) => {
  const gr = g.createRadialGradient(32, 32, 0, 32, 32, 32); gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.35, 'rgba(255,255,255,0.45)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
}, { linear: true });
const texSpark = () => canvasTex('spark4', 64, 64, (g, w, h) => {
  const gr = g.createRadialGradient(32, 32, 0, 32, 32, 10); gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = gr; g.fillRect(0, 0, w, h);
  g.fillStyle = 'rgba(255,255,255,0.9)'; g.beginPath(); g.moveTo(32, 0); g.lineTo(35, 29); g.lineTo(64, 32); g.lineTo(35, 35); g.lineTo(32, 64); g.lineTo(29, 35); g.lineTo(0, 32); g.lineTo(29, 29); g.closePath(); g.fill();
}, { linear: true });

/* 文字テクスチャ（ネオン風） */
function textTexture(text, o = {}) {
  const key = 'txt|' + text + '|' + (o.color || '') + '|' + (o.style || '') + '|' + (o.bg || '');
  if (canvasTexCache.has(key)) return canvasTexCache.get(key);
  const font = `bold 150px "Hiragino Maru Gothic ProN","Arial Rounded MT Bold","Yu Gothic UI",sans-serif`;
  const m = document.createElement('canvas').getContext('2d'); m.font = font;
  const tw = Math.max(200, Math.ceil(m.measureText(text || ' ').width) + 120);
  const cv = document.createElement('canvas'); cv.width = Math.min(4096, tw); cv.height = 256;
  const g = cv.getContext('2d');
  if (o.bg) { g.fillStyle = o.bg; g.fillRect(0, 0, cv.width, cv.height); }
  g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle';
  const c = o.color || '#ffffff';
  if (o.style === 'neon') {
    g.shadowColor = c; g.shadowBlur = 28; g.lineWidth = 9; g.strokeStyle = c; g.strokeText(text, cv.width / 2, 132);
    g.shadowBlur = 8; g.fillStyle = '#ffffff'; g.fillText(text, cv.width / 2, 132);
  } else { g.fillStyle = c; g.fillText(text, cv.width / 2, 132); }
  const t = new THREE.CanvasTexture(cv); t.colorSpace = THREE.SRGBColorSpace; t.userData.shared = true; t.userData.aspect = cv.width / cv.height;
  canvasTexCache.set(key, t);
  return t;
}
/* 画像（dataURL）→ テクスチャ */
function imageTexture(id) {
  if (!id || !IMAGES[id]) return null;
  if (TEXCACHE.has(id)) return TEXCACHE.get(id);
  const t = new THREE.TextureLoader().load(IMAGES[id], (tx) => { tx.userData.aspect = tx.image.width / tx.image.height; markDirty('backdrop'); markDirty('assets'); });
  t.colorSpace = THREE.SRGBColorSpace; t.userData.shared = true; t.userData.aspect = 1.6;
  TEXCACHE.set(id, t);
  return t;
}

/* LED床（動的キャンバス） */
const ledFloor = { cv: null, tex: null, last: -1 };
function ledFloorTexture() {
  if (!ledFloor.tex) {
    ledFloor.cv = document.createElement('canvas'); ledFloor.cv.width = 128; ledFloor.cv.height = 128;
    ledFloor.tex = new THREE.CanvasTexture(ledFloor.cv); ledFloor.tex.colorSpace = THREE.SRGBColorSpace;
    ledFloor.tex.wrapS = ledFloor.tex.wrapT = THREE.RepeatWrapping; ledFloor.tex.userData.shared = true;
  }
  return ledFloor.tex;
}
function updateLedFloor(beat, colors) {
  if (!ledFloor.tex) return;
  const step = Math.floor(beat * 2);
  if (step === ledFloor.last) return;
  ledFloor.last = step;
  const g = ledFloor.cv.getContext('2d'); const n = 8, s = 128 / n;
  g.fillStyle = '#000'; g.fillRect(0, 0, 128, 128);
  for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
    const r = hash1(x * 31 + y * 17 + step * 7.3);
    const c = colors[Math.floor(r * 3) % 3];
    const on = r > 0.35;
    g.fillStyle = on ? '#' + c.getHexString() : '#0a0a14';
    g.fillRect(x * s + 1.5, y * s + 1.5, s - 3, s - 3);
  }
  ledFloor.tex.needsUpdate = true;
}
