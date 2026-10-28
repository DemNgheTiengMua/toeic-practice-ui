const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, readdirSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const assets = join(__dirname, '../../src/WebMVC/wwwroot/js');

function element(attributes = {}) {
  const listeners = {};
  const classes = new Set();
  return {
    dataset: {}, value: '', style: {}, textContent: '', href: '', disabled: false,
    classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
    addEventListener(type, handler) { (listeners[type] ||= []).push(handler); },
    async fire(type, event = {}) {
      event.preventDefault ||= () => { event.prevented = true; };
      for (const handler of listeners[type] || []) await handler(event);
      return event;
    },
    getAttribute: key => attributes[key],
    setAttribute: (key, value) => { attributes[key] = value; },
    submit() { this.submitted = true; }
  };
}
function page(modality, fetch = async () => ({ ok: true }), options = {}) {
  const elements = new Map();
  const get = id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); };
  const doc = element();
  const link = element(); link.href = '/writing/take/12?q=5';
  const close = element();
  const intervals = [];
  const timeouts = [];
  const window = { location: { href: '', reload() { this.reloaded = true; } } };
  const token = { value: 'csrf-token' };
  doc.getElementById = get;
  doc.querySelector = selector => selector.includes('__RequestVerificationToken') ? token : null;
  doc.querySelectorAll = selector => selector.includes('data-question-id') ? [link] : selector === '.js-close-submit-modal' ? [close] : [];
  const context = {
    document: doc, window, fetch, console, FormData, Blob, URL, Date,
    setInterval: callback => { intervals.push(callback); return intervals.length; },
    clearInterval() {}, setTimeout: callback => { timeouts.push(callback); return timeouts.length; }, clearTimeout() {},
    navigator: {}, alert() {}, confirm: () => true, ...options.globals
  };
  const editor = get('writing-editor');
  editor.getAttribute = key => ({ 'data-attempt-id': '12', 'data-question-id': '4', 'data-min-words': '2', 'data-max-words': '5' })[key];
  get('timer-display').getAttribute = () => '125';
  get('practice-data').dataset = { attemptId: '12', questionId: '4', prepSeconds: '10', responseSeconds: '45', remainingSeconds: '125', hasAudio: options.hasAudio ?? 'true' };
  vm.runInNewContext(readFileSync(join(assets, modality, 'take.js'), 'utf8'), context);
  return { get, doc, link, close, window, intervals, timeouts, editor };
}

test('all free-response assets parse as JavaScript', () => {
  for (const area of ['speaking', 'writing'])
    for (const file of readdirSync(join(assets, area)))
      new vm.Script(readFileSync(join(assets, area, file), 'utf8'), { filename: file });
});
test('writing initializes timer, word meter, modal and keyboard dismissal', async () => {
  const p = page('writing');
  assert.equal(p.get('timer-display').textContent, '02:05');
  p.editor.value = 'one two three'; await p.editor.fire('input');
  assert.equal(p.get('word-count').textContent, 3);
  await p.get('btn-open-submit-modal').fire('click');
  assert.equal(p.get('submit-modal').getAttribute('aria-hidden'), 'false');
  await p.doc.fire('keydown', { key: 'Escape' });
  assert.equal(p.get('submit-modal').getAttribute('aria-hidden'), 'true');
});
test('writing saves via MVC with anti-forgery before navigating', async () => {
  const requests = [];
  const p = page('writing', async (url, options) => { requests.push({ url, options }); return { ok: true }; });
  p.editor.value = 'draft'; await p.editor.fire('input');
  await p.link.fire('click');
  assert.equal(requests[0].url, '/writing/save-draft');
  assert.equal(requests[0].options.headers.RequestVerificationToken, 'csrf-token');
  assert.deepEqual(JSON.parse(requests[0].options.body), { attemptId: 12, questionId: 4, responseText: 'draft' });
  assert.equal(p.window.location.href, p.link.href);
});
test('failed writing save blocks submission and keeps the draft editable', async () => {
  const p = page('writing', async () => ({ ok: false }));
  p.editor.value = 'unsaved'; await p.editor.fire('input');
  const event = await p.get('submit-form').fire('submit');
  assert.equal(event.prevented, true);
  assert.equal(p.get('submit-form').submitted, undefined);
  assert.equal(p.get('btn-save-draft').disabled, false);
  assert.equal(p.editor.value, 'unsaved');
});
test('speaking restores saved audio using supplied attempt and question IDs', async () => {
  const p = page('speaking');
  assert.equal(p.get('audio-player').src, '/speaking/audio/12/4');
  assert.equal(p.get('test-timer-val').textContent, '02:05');
  await p.get('btn-open-submit-modal').fire('click');
  assert.equal(p.get('submit-modal').getAttribute('aria-hidden'), 'false');
  await p.doc.fire('keydown', { key: 'Escape' });
  assert.equal(p.get('submit-modal').getAttribute('aria-hidden'), 'true');
});

test('writing waits for an in-flight autosave and persists the latest edit before navigation', async () => {
  let finish;
  const requests = [];
  const p = page('writing', async (url, options) => {
    requests.push(JSON.parse(options.body).responseText);
    if (requests.length === 1) return new Promise(resolve => { finish = resolve; });
    return { ok: true };
  });
  p.editor.value = 'first'; await p.editor.fire('input');
  const saving = p.get('btn-save-draft').fire('click');
  p.editor.value = 'second'; await p.editor.fire('input');
  const navigating = p.link.fire('click');
  assert.equal(p.window.location.href, '');
  finish({ ok: true });
  await Promise.all([saving, navigating]);
  assert.deepEqual(requests, ['first', 'second']);
  assert.equal(p.window.location.href, p.link.href);
});
test('speaking records and uploads through the MVC proxy with ownership IDs and anti-forgery', async () => {
  const requests = [];
  const tracks = [{ stopped: false, stop() { this.stopped = true; } }];
  class Recorder {
    start() { this.state = 'recording'; }
    stop() {
      this.state = 'inactive';
      this.ondataavailable({ data: new Blob(['audio']) });
      this.onstop();
    }
  }
  const p = page('speaking', async (url, options) => {
    requests.push({ url, options }); return { ok: false };
  }, {
    hasAudio: 'false',
    globals: {
      navigator: { mediaDevices: { getUserMedia: async () => ({ getTracks: () => tracks }) } },
      MediaRecorder: Recorder
    }
  });
  await p.get('btn-start-now').fire('click');
  await Promise.resolve();
  await p.get('btn-stop-now').fire('click');
  await p.get('btn-save-continue').fire('click');
  const upload = requests[0];
  assert.equal(upload.url, '/speaking/upload-audio');
  assert.equal(upload.options.body.get('attemptId'), '12');
  assert.equal(upload.options.body.get('questionId'), '4');
  assert.equal(upload.options.body.get('__RequestVerificationToken'), 'csrf-token');
  assert.equal(tracks[0].stopped, true);
  assert.equal(p.get('save-failed-banner').classList.contains('d-none'), false);
});
for (const area of ['speaking', 'writing']) {
  test(`${area} pending screen polls the MVC status route and opens the graded review`, async () => {
    const callbacks = [];
    const window = { location: { href: '' } };
    const requests = [];
    vm.runInNewContext(readFileSync(join(assets, area, 'pending.js'), 'utf8'), {
      document: { querySelector: () => ({ dataset: { attemptId: '12' } }) }, window,
      fetch: async url => { requests.push(url); return { ok: true, json: async () => ({ attemptStatus: 'graded' }) }; },
      setTimeout: callback => callbacks.push(callback)
    });
    await callbacks[0]();
    assert.deepEqual(requests, [`/${area}/status/12`]);
    assert.equal(window.location.href, `/${area}/review/12`);
  });
}

test('writing saves edits made during the final submission save', async () => {
  let finish;
  const requests = [];
  const p = page('writing', async (url, options) => {
    requests.push(JSON.parse(options.body).responseText);
    if (requests.length === 1) return new Promise(resolve => { finish = resolve; });
    return { ok: true };
  });
  p.editor.value = 'first'; await p.editor.fire('input');
  const submitting = p.get('submit-form').fire('submit');
  p.editor.value = 'latest'; await p.editor.fire('input');
  finish({ ok: true });
  await submitting;
  assert.deepEqual(requests, ['first', 'latest']);
  assert.equal(p.get('submit-form').submitted, true);
});
