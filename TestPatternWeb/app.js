// ---------------------------------------------------------------------------
// 設定: ここだけ書き換えれば内容と動きを調整できる
// ---------------------------------------------------------------------------

// 単語プール (42語)
const WORDS = [
  'APPLE', 'BREAD', 'BRIDGE', 'CAMERA', 'CHAIR', 'CLOUD', 'DOCTOR', 'DREAM',
  'ENGINE', 'FLAME', 'FOREST', 'GARDEN', 'GLASS', 'GREEN', 'HONEY', 'HOUSE',
  'JUICE', 'KNIFE', 'LEMON', 'LIGHT', 'MARKET', 'MOUSE', 'MUSIC', 'NIGHT',
  'OCEAN', 'ORANGE', 'PAPER', 'PIANO', 'QUIET', 'RIVER', 'ROBOT', 'SILVER',
  'SNOW', 'STONE', 'TABLE', 'TIGER', 'TRAIN', 'VOICE', 'WATER', 'WINDOW',
  'YELLOW', 'ZEBRA',
];

// 数値プール (14個)
const NUMBERS = [
  '7', '12', '23', '48', '64', '99', '108', '256',
  '372', '640', '1024', '1987', '2026', '4815',
];

const PICK_COUNT = 3;    // 1画面に表示する個数
const NUMBER_COUNT = 1;  // そのうち数値にする個数。残りは必ず単語から選ぶ

// 速度: 画面の短辺に対する 1 秒あたりの移動量 (%)。小さいほどゆっくり。
const SPEED_PERCENT_PER_SEC = 5.25;
const SPEED_JITTER = 0.2;  // 個体ごとの速度ばらつき (±20%)

// 傾き: ±TILT_MAX_DEG の範囲で傾き、端まで行くと折り返してゆっくり揺れ続ける。
// TILT_SPEED_DEG_PER_SEC を 0 にすると最初の角度のまま固定になる。
const TILT_MAX_DEG = 35;
const TILT_SPEED_DEG_PER_SEC = 3;

// ---------------------------------------------------------------------------

const stage = document.getElementById('stage');
const items = [];

function shuffled(list) {
  const a = list.slice();
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

// 数値を NUMBER_COUNT 個、残りを単語から選ぶ (全部数値になることはない)
function pickLabels() {
  const numbers = shuffled(NUMBERS).slice(0, NUMBER_COUNT);
  const words = shuffled(WORDS).slice(0, PICK_COUNT - numbers.length);
  return shuffled(numbers.concat(words));
}

function baseSpeed() {
  const short = Math.min(window.innerWidth, window.innerHeight);
  return (short * SPEED_PERCENT_PER_SEC) / 100;
}

// 軸に近すぎる角度 (真横・真縦) は単調なので引き直す
function randomDirection() {
  let angle;
  do {
    angle = Math.random() * Math.PI * 2;
  } while (Math.abs(Math.sin(angle)) < 0.25 || Math.abs(Math.cos(angle)) < 0.25);
  return angle;
}

// 傾けた文字が画面からはみ出さないよう、回転後の外接矩形の半分の幅・高さを求める
function halfExtents(item) {
  const rad = (item.tilt * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad));
  const s = Math.abs(Math.sin(rad));
  return {
    hw: (item.w * c + item.h * s) / 2,
    hh: (item.w * s + item.h * c) / 2,
  };
}

function overlaps(a, b) {
  const pad = 8;
  const ea = halfExtents(a);
  const eb = halfExtents(b);
  return Math.abs(a.cx - b.cx) < ea.hw + eb.hw + pad &&
         Math.abs(a.cy - b.cy) < ea.hh + eb.hh + pad;
}

// 初期位置は重なりを避けて置く (数回試してだめなら諦める)
function placeItem(item, placed) {
  const { hw, hh } = halfExtents(item);
  const spanX = Math.max(0, window.innerWidth - hw * 2);
  const spanY = Math.max(0, window.innerHeight - hh * 2);
  for (let attempt = 0; attempt < 40; attempt++) {
    item.cx = hw + Math.random() * spanX;
    item.cy = hh + Math.random() * spanY;
    if (!placed.some((other) => overlaps(item, other))) break;
  }
}

// transform の影響を受けない素の文字サイズを測る
function measure(item) {
  item.w = item.el.offsetWidth;
  item.h = item.el.offsetHeight;
}

function draw(item) {
  item.el.style.transform =
    `translate3d(${item.cx}px, ${item.cy}px, 0) rotate(${item.tilt}deg) translate(-50%, -50%)`;
}

function createItems() {
  const speed = baseSpeed();
  for (const label of pickLabels()) {
    const el = document.createElement('div');
    el.className = 'item';
    el.textContent = label;
    stage.appendChild(el);

    const angle = randomDirection();
    const jitter = 1 + (Math.random() * 2 - 1) * SPEED_JITTER;
    const s = speed * jitter;
    const item = {
      el,
      jitter,
      cx: 0,
      cy: 0,
      w: 0,
      h: 0,
      vx: Math.cos(angle) * s,
      vy: Math.sin(angle) * s,
      tilt: (Math.random() * 2 - 1) * TILT_MAX_DEG,
      tiltDir: Math.random() < 0.5 ? -1 : 1,
      tiltSpeed: TILT_SPEED_DEG_PER_SEC * jitter,
    };
    measure(item);
    placeItem(item, items);
    draw(item);
    items.push(item);
  }
}

// フォント適用やリサイズで文字サイズが変わるので測り直して画面内に収める
function refresh() {
  const speed = baseSpeed();
  for (const item of items) {
    measure(item);
    const current = Math.hypot(item.vx, item.vy) || 1;
    const scale = (speed * item.jitter) / current;
    item.vx *= scale;
    item.vy *= scale;
  }
}

let last = performance.now();

function frame(now) {
  const dt = Math.min((now - last) / 1000, 0.05);  // タブ復帰時の飛びを抑える
  last = now;

  for (const item of items) {
    // 傾きも ±TILT_MAX_DEG の間を往復させる
    item.tilt += item.tiltDir * item.tiltSpeed * dt;
    if (item.tilt <= -TILT_MAX_DEG) {
      item.tilt = -TILT_MAX_DEG;
      item.tiltDir = 1;
    } else if (item.tilt >= TILT_MAX_DEG) {
      item.tilt = TILT_MAX_DEG;
      item.tiltDir = -1;
    }

    item.cx += item.vx * dt;
    item.cy += item.vy * dt;

    // 端に来たら反射。文字が画面より大きい場合は中央に寄せる
    const { hw, hh } = halfExtents(item);
    const maxX = window.innerWidth - hw;
    const maxY = window.innerHeight - hh;

    if (maxX < hw) {
      item.cx = window.innerWidth / 2;
    } else if (item.cx <= hw) {
      item.cx = hw;
      item.vx = Math.abs(item.vx);
    } else if (item.cx >= maxX) {
      item.cx = maxX;
      item.vx = -Math.abs(item.vx);
    }

    if (maxY < hh) {
      item.cy = window.innerHeight / 2;
    } else if (item.cy <= hh) {
      item.cy = hh;
      item.vy = Math.abs(item.vy);
    } else if (item.cy >= maxY) {
      item.cy = maxY;
      item.vy = -Math.abs(item.vy);
    }

    draw(item);
  }

  requestAnimationFrame(frame);
}

createItems();
if (document.fonts) document.fonts.ready.then(refresh);
window.addEventListener('resize', refresh);
requestAnimationFrame(frame);
