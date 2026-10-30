const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../src/WebMVC/wwwroot/js/placement/take.js'), 'utf8');

// A placement sitting as take.js sees it: #placement-take-dynamic holds one exam-mode player and
// the page-data island. `served` is the question the fragment endpoint answers with; `fragment`
// is how that call goes: 'ok', 'refused' (non-2xx, no redirect) or 'offline' (fetch rejects).
function sitting({ current, served, stored = {}, fragment = 'ok' }) {
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
  const document = {
    title: '',
    getElementById: id => id === 'placement-take-dynamic' ? dynamic
      : id === 'placement-take-data' ? { textContent: shown.data } : null,
    querySelector: selector => selector === '.exam-audio[data-mode="exam"]' ? shown.player : null,
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
    initExamAudio: () => log.push(`init completed=${shown.player.dataset.completed ?? 'unset'}`)
  };
  vm.runInNewContext(source, {
    // take.js logs the failures these tests provoke; keep the test output quiet.
    document, window, URL, console: { ...console, error() {} }, setInterval() {}, setTimeout, clearTimeout,
    localStorage: { getItem: key => key === 'ee_placement_audio_9' ? JSON.stringify(stored) : null, setItem() {}, removeItem() {} },
    fetch: url => {
      log.push(`fetch ${url}`);
      if (fragment === 'offline') return Promise.reject(new TypeError('Failed to fetch'));
      if (fragment === 'refused') return Promise.resolve({ ok: false, json: () => Promise.resolve({}) });
      return Promise.resolve({ ok: true, text: () => Promise.resolve(markup(served)) });
    }
  });
  const settle = () => new Promise(resolve => setImmediate(resolve));
  return {
    log,
    player: () => shown.player,
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
