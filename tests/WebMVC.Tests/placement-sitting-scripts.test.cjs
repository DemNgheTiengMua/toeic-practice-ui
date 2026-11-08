const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../src/WebMVC/wwwroot/js/placement/take.js'), 'utf8');

// A placement sitting as take.js sees it: #placement-take-dynamic holds one exam-mode player and
// the page-data island. `served` is the question the fragment endpoint answers with; `fragment`
// is how that call goes: 'ok', 'refused' (non-2xx, no redirect) or 'offline' (fetch rejects).
// `answer` is how the answer endpoint goes; `online` is navigator.onLine.
function sitting({ current, served, stored = {}, fragment = 'ok', online = true, answer = 'ok', queued = null }) {
  const log = [];
  const handlers = {};
  const island = page => JSON.stringify({ placementId: 9, questionId: 100 + page.order, displayOrder: page.order,
    totalQuestions: 4, answeredCount: 0, remainingSeconds: 600, isFlagged: false, audioKey: page.audioKey });
  const markup = page => `<script type="application/json" id="placement-take-data">${island(page)}</script>`;
  const parse = html => html.match(/id="placement-take-data">([\s\S]*?)<\/script>/)[1];
  let shown = { player: { dataset: {} }, data: island(current) };
  const show = data => { shown = { player: { dataset: {} }, data }; log.push('swap'); };
  const dynamic = {
    set innerHTML(html) { show(parse(html)); },
    replaceChildren(content) { show(content.data); }
  };
  // #216 / PLC-17: the status placard, the banner and the local queue are the three places an
  // offline save becomes visible. They are elements the script reads, so they are modelled here.
  const status = { textContent: 'Saved', className: 'save-status save-status--saved' };
  const net = { online };
  const banner = { hidden: true };
  const queueStore = new Map();
  if (queued) queueStore.set('ee_placement_queue_9', JSON.stringify(queued));
  const inert = () => ({ textContent: '', style: {} });
  const document = {
    title: '',
    getElementById: id => id === 'placement-take-dynamic' ? dynamic
      : id === 'placement-take-data' ? { textContent: shown.data }
      : id === 'save-status' ? status
      : id === 'placement-offline-banner' ? banner
      : inert(),
    querySelector: selector => selector === '.exam-audio[data-mode="exam"]' ? shown.player
      : selector === '#save-status' ? status
      : selector === '#placement-offline-banner' ? banner
      : null,
    querySelectorAll: () => [],
    addEventListener: (name, handler) => { handlers[name] = handler; },
    createElement: () => {
      const content = { data: null, getElementById: id => id === 'placement-take-data' ? { textContent: content.data } : null };
      return { content, set innerHTML(html) { content.data = parse(html); } };
    }
  };
  const window = {
    location: { href: `http://ee.test/placement/take/9?q=${current.order}`, origin: 'http://ee.test' },
    history: {
      pushState: (_, __, url) => log.push(`push ${url}`),
      replaceState: (_, __, url) => log.push(`replace ${url}`)
    },
    addEventListener: (name, handler) => { handlers[name] = handler; },
    initExamAudio: () => log.push(`init completed=${shown.player.dataset.completed ?? 'unset'}`),
    confirm: () => true
  };
  vm.runInNewContext(source, {
    // take.js logs the failures these tests provoke; keep the test output quiet.
    document, window, URL, URLSearchParams, console: { ...console, error() {} }, setInterval() {}, setTimeout, clearTimeout,
    // #216: take.js reads navigator.onLine through a live getter, so a test can drop the connection
    // between two answers the way a real one does.
    navigator: { get onLine() { return net.online; } },
    localStorage: {
      getItem: key => key === 'ee_placement_audio_9' ? JSON.stringify(stored)
        : queueStore.has(key) ? queueStore.get(key) : null,
      setItem: (key, value) => queueStore.set(key, value),
      removeItem: key => queueStore.delete(key)
    },
    fetch: (url, options) => {
      log.push(`fetch ${url}`);
      if (url.includes('/answers/')) {
        if (answer === 'offline') return Promise.reject(new TypeError('Failed to fetch'));
        if (answer === 'error') return Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) });
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ selectedOptionId: 11, isFlagged: false }) });
      }
      if (fragment === 'offline') return Promise.reject(new TypeError('Failed to fetch'));
      if (fragment === 'refused') return Promise.resolve({ ok: false, json: () => Promise.resolve({}) });
      return Promise.resolve({ ok: true, text: () => Promise.resolve(markup(served)) });
    }
  });
  const settle = () => new Promise(resolve => setImmediate(resolve));
  return {
    log,
    status,
    banner,
    queue: () => { const raw = queueStore.get('ee_placement_queue_9'); return raw ? JSON.parse(raw) : []; },
    player: () => shown.player,
    async answer(optionId) {
      handlers.change({ target: { matches: selector => selector === '.js-option-radio', value: optionId, closest: () => null } });
      await settle();
      await settle();
    },
    fire(name) { handlers[name] && handlers[name](); },
    setOnline(value) { net.online = value; },
    async leavePart(order) {
      const link = { href: `http://ee.test/placement/take/9?q=${order}`, dataset: { confirmLeave: 'Leaving is final.' }, closest: () => dynamic };
      handlers.click({ target: { closest: selector => selector.startsWith('a[href') ? link : null }, preventDefault() {} });
      for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve));
    },
    async back(order) {
      window.location.href = `http://ee.test/placement/take/9?q=${order}`;
      handlers.popstate();
      await settle();
    },
    async next(order) {
      const link = { href: `http://ee.test/placement/take/9?q=${order}`, dataset: {}, closest: () => dynamic };
      handlers.click({ target: { closest: selector => selector.startsWith('a[href') ? link : null }, preventDefault() {} });
      await settle();
    }
  };
}

test('a refused rewind keeps the live single-play player instead of re-rendering it', async () => {
  // Back from Q2 asks for Q1; the one-way section refuses and the server answers with Q2 again.
  const page = sitting({ current: { order: 2, audioKey: 'p2' }, served: { order: 2, audioKey: 'p2' },
    stored: { p2: { completed: true, currentTime: 1.3 } } });
  const live = page.player();

  await page.back(1);

  assert.equal(page.player(), live);
  assert.deepEqual(page.log, ['fetch /placement/take/9/fragment?q=1', 'replace /placement/take/9?q=2']);
});

test('a confirmed move restores stored progress before exam-audio initialises the new player', async () => {
  // Q5 and Q6 share one clip that already played to the end on Q5.
  const page = sitting({ current: { order: 5, audioKey: 'shared' }, served: { order: 6, audioKey: 'shared' },
    stored: { shared: { completed: true, currentTime: 1.3 } } });

  await page.next(6);

  assert.deepEqual(page.log, ['fetch /placement/take/9/fragment?q=6', 'swap',
    'push /placement/take/9?q=6', 'init completed=true']);
});

test('a fragment refused on Back puts the address bar back on the question still on screen', async () => {
  // Back from Q3 asks for Q2 and the server refuses without a redirect (for example the placement
  // expired). The browser already shows ?q=2 while Q3 stays on screen.
  const page = sitting({ current: { order: 3, audioKey: 'p3' }, served: { order: 3, audioKey: 'p3' }, fragment: 'refused' });

  await page.back(2);

  assert.deepEqual(page.log, ['fetch /placement/take/9/fragment?q=2', 'replace /placement/take/9?q=3']);
});

test('a dropped connection on Back puts the address bar back on the question still on screen', async () => {
  const page = sitting({ current: { order: 3, audioKey: 'p3' }, served: { order: 3, audioKey: 'p3' }, fragment: 'offline' });

  await page.back(2);

  assert.deepEqual(page.log, ['fetch /placement/take/9/fragment?q=2', 'replace /placement/take/9?q=3']);
});

// ------------------------------------------------------------------------------------------------
// #216 / PLC-17: offline answers
// ------------------------------------------------------------------------------------------------

test('an answer typed offline stays on the device and the placard says so in both numbers', async () => {
  const page = sitting({ current: { order: 1, audioKey: 'p1' }, served: { order: 1, audioKey: 'p1' },
    online: false, answer: 'offline' });

  await page.answer(11);

  // Nothing was sent, and the queue still holds the answer.
  assert.deepEqual(page.queue().map(item => item.questionId), [101]);
  assert.deepEqual(page.log.filter(line => line.startsWith('fetch')), []);

  // The status names the device, not "saved": a page that claimed success over a full queue is the
  // silent loss this ticket forbids.
  assert.match(page.status.textContent, /Offline/);
  assert.match(page.status.textContent, /1 saved on this device/);
  assert.equal(page.status.className, 'save-status save-status--offline');
  assert.equal(page.banner.hidden, false);
});

test('an answer that fails while online is kept, retried, and never reported as saved', async () => {
  const page = sitting({ current: { order: 1, audioKey: 'p1' }, served: { order: 1, audioKey: 'p1' },
    answer: 'error' });

  await page.answer(11);

  assert.deepEqual(page.queue().map(item => item.questionId), [101]);
  assert.doesNotMatch(page.status.textContent, /^Saved$/);
  assert.equal(page.banner.hidden, false);
});

test('reconnecting flushes the queue and the placard stops saying offline', async () => {
  // A reload of a sitting whose queue already holds an answer, with the connection back.
  const page = sitting({ current: { order: 1, audioKey: 'p1' }, served: { order: 1, audioKey: 'p1' },
    queued: [{ questionId: 101, selectedOptionId: 11, isFlagged: false }] });

  page.setOnline(true);
  page.fire('online');
  for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve));

  assert.deepEqual(page.queue(), []);
  assert.ok(page.log.some(line => line.includes('/answers/101')));
  assert.doesNotMatch(page.status.textContent, /Offline/);
});

test('a part boundary waits for the queue instead of moving on over an unsent answer', async () => {
  const page = sitting({ current: { order: 1, audioKey: 'p1' }, served: { order: 2, audioKey: 'p2' },
    online: false, answer: 'offline' });

  await page.answer(11);
  assert.equal(page.queue().length, 1);

  // The Next control on a locked part: it must not navigate while the queue is full.
  await page.leavePart(2);

  assert.ok(!page.log.some(line => line.startsWith('push')), 'the sitting must not move while answers are unsent');
  assert.match(page.status.textContent, /Waiting for a connection/);
});
