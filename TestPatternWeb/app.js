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

// 数値プール (14個)。半分は日付・郵便番号・電話番号など意味のありそうな形にしてある
const NUMBERS = [
  '7', '23', '48', '64', '108', '256', '1987',
  '2026-09-18', '2025-12-31', '1998-04',  // 日付
  '812-0011',                             // 郵便番号
  '03-5218', '090-1234',                  // 電話番号
  '1-23',                                 // 番地
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

// 文字同士の衝突。false にするとすり抜けるようになる。
const COLLIDE = true;
const COLLIDE_GAP = 10;  // 文字の間に残す余白 (px)

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

// 傾けた文字の当たり判定として、回転後の外接矩形の半分の幅・高さを item に持たせる
function updateExtents(item) {
  const rad = (item.tilt * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad));
  const s = Math.abs(Math.sin(rad));
  item.hw = (item.w * c + item.h * s) / 2;
  item.hh = (item.w * s + item.h * c) / 2;
}

function overlaps(a, b) {
  return Math.abs(a.cx - b.cx) < a.hw + b.hw + COLLIDE_GAP &&
         Math.abs(a.cy - b.cy) < a.hh + b.hh + COLLIDE_GAP;
}

// 初期位置は重なりを避けて置く (数回試してだめなら諦める)
function placeItem(item, placed) {
  const spanX = Math.max(0, window.innerWidth - item.hw * 2);
  const spanY = Math.max(0, window.innerHeight - item.hh * 2);
  for (let attempt = 0; attempt < 40; attempt++) {
    item.cx = item.hw + Math.random() * spanX;
    item.cy = item.hh + Math.random() * spanY;
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
      hw: 0,
      hh: 0,
      vx: Math.cos(angle) * s,
      vy: Math.sin(angle) * s,
      tilt: (Math.random() * 2 - 1) * TILT_MAX_DEG,
      tiltDir: Math.random() < 0.5 ? -1 : 1,
      tiltSpeed: TILT_SPEED_DEG_PER_SEC * jitter,
    };
    measure(item);
    updateExtents(item);
    placeItem(item, items);
    draw(item);
    items.push(item);
  }
}

// フォント適用やリサイズで文字サイズが変わるので測り直して速度を画面に合わせ直す
function refresh() {
  const speed = baseSpeed();
  for (const item of items) {
    measure(item);
    updateExtents(item);
    const current = Math.hypot(item.vx, item.vy) || 1;
    const scale = (speed * item.jitter) / current;
    item.vx *= scale;
    item.vy *= scale;
  }
}

// 文字同士の衝突: めり込みの浅い軸で押し戻し、近づいている方向の速度だけ反転する。
// 速度は入れ替えず各自の向きを反転させるだけなので、速さは最初のまま保たれる。
function resolveCollisions() {
  for (let i = 0; i < items.length; i++) {
    for (let j = i + 1; j < items.length; j++) {
      const a = items[i];
      const b = items[j];
      const dx = b.cx - a.cx;
      const dy = b.cy - a.cy;
      const overlapX = a.hw + b.hw + COLLIDE_GAP - Math.abs(dx);
      const overlapY = a.hh + b.hh + COLLIDE_GAP - Math.abs(dy);
      if (overlapX <= 0 || overlapY <= 0) continue;

      if (overlapX < overlapY) {
        const sign = dx >= 0 ? 1 : -1;  // sign > 0 なら b が a の右
        a.cx -= (sign * overlapX) / 2;
        b.cx += (sign * overlapX) / 2;
        if ((b.vx - a.vx) * sign < 0) {  // 近づいているときだけ跳ね返す
          a.vx = -sign * Math.abs(a.vx);
          b.vx = sign * Math.abs(b.vx);
        }
      } else {
        const sign = dy >= 0 ? 1 : -1;  // sign > 0 なら b が a の下
        a.cy -= (sign * overlapY) / 2;
        b.cy += (sign * overlapY) / 2;
        if ((b.vy - a.vy) * sign < 0) {
          a.vy = -sign * Math.abs(a.vy);
          b.vy = sign * Math.abs(b.vy);
        }
      }
    }
  }
}

// 画面の端で反射。文字が画面より大きい場合は中央に寄せる
function clampToStage(item) {
  const maxX = window.innerWidth - item.hw;
  const maxY = window.innerHeight - item.hh;

  if (maxX < item.hw) {
    item.cx = window.innerWidth / 2;
  } else if (item.cx <= item.hw) {
    item.cx = item.hw;
    item.vx = Math.abs(item.vx);
  } else if (item.cx >= maxX) {
    item.cx = maxX;
    item.vx = -Math.abs(item.vx);
  }

  if (maxY < item.hh) {
    item.cy = window.innerHeight / 2;
  } else if (item.cy <= item.hh) {
    item.cy = item.hh;
    item.vy = Math.abs(item.vy);
  } else if (item.cy >= maxY) {
    item.cy = maxY;
    item.vy = -Math.abs(item.vy);
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
    updateExtents(item);
  }

  if (COLLIDE) resolveCollisions();

  // 押し戻しで画面外に出ないよう、端の処理は最後にする
  for (const item of items) {
    clampToStage(item);
    draw(item);
  }

  requestAnimationFrame(frame);
}

createItems();
if (document.fonts) document.fonts.ready.then(refresh);
window.addEventListener('resize', refresh);
requestAnimationFrame(frame);
