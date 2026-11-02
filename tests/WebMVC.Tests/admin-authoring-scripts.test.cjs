const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, readdirSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const assets = join(__dirname, '../../src/WebMVC/wwwroot/js/admin');

function element() {
  const handlers = {}, attributes = {}, classes = new Set();
  return {
    value: '', textContent: '', innerHTML: '', style: {}, dataset: {}, disabled: false, children: [],
    classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) },
    addEventListener(type, fn) { (handlers[type] ||= []).push(fn); },
    fire(type, event = {}) { for (const fn of handlers[type] || []) fn.call(this, event); },
    getAttribute: key => attributes[key], setAttribute: (key, value) => attributes[key] = value,
    querySelector: () => null, querySelectorAll: () => [],
    appendChild(child) { this.children.push(child); if (child.selected || this.value === '') this.value = String(child.value); }
  };
}
function page(asset, options = {}) {
  const elements = new Map();
  const get = id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); };
  const document = element();
  document.getElementById = get;
  document.querySelector = selector => selector.includes('__RequestVerificationToken') ? { value: 'csrf' } : null;
  document.querySelectorAll = () => [];
  document.createElement = () => element();
  const requests = [];
  const context = {
    document, URLSearchParams, console,
    location: { search: '', reload() { this.reloaded = true; } },
    setTimeout() { return 1; }, clearTimeout() {}, alert() {}, confirm: () => true,
    addEventListener() {}, scrollTo() {},
    FormData: class { append() {} },
    fetch: async (url, init) => { requests.push({ url, init }); return { ok: true, json: async () => ({ success: true, questions: [{ index: 1, isValid: true, stem: "Prompt", options: [], issues: [] }], globalIssues: [], canPublish: false, totalQuestions: 0, expectedQuestions: 0, blockingErrorCount: 1, warningCount: 0, issues: [] }) }; },
    ...options
  };
  context.window = context;
  get('studio-csrf-holder').dataset.configuration = JSON.stringify({
    ExamId: 42, CurrentSectionId: 7,
    Sections: [{ SectionId: 7, Name: 'Listening "quoted"', Modality: 'Listening', ClusterSize: 3 }],
    IngestSections: [{ SectionId: 7, Code: 'P3', Name: 'Conversations', OptionCount: 4, ClusterSize: 3 }]
  });
  vm.runInNewContext(readFileSync(join(assets, asset + '.js'), 'utf8'), context);
  return { get, document, requests, context };
}
const flush = () => new Promise(resolve => setImmediate(resolve));

test('all extracted admin assets parse as standalone JavaScript', () => {
  for (const file of readdirSync(assets).filter(x => x.endsWith('.js')))
    new vm.Script(readFileSync(join(assets, file), 'utf8'), { filename: file });
});
test('studio reads section labels and quick ingest configuration from encoded page data', () => {
  const calls = [];
  const p = page('exam-studio', { QuickIngestModal: { open: (...args) => calls.push(args) } });
  p.context.selectSection(7);
  assert.equal(p.get('ws-section-name').textContent, 'Listening "quoted"');
  assert.equal(p.get('ws-section-modality').textContent, 'Listening');
  p.get('btn-studio-quick-ingest').fire('click');
  assert.equal(calls[0][0], 42);
  assert.equal(calls[0][1][0].Code, 'P3');
  assert.equal(calls[0][2], 7);
});
test('studio publishes through MVC with anti-forgery and reloads on success', async () => {
  const p = page('exam-studio');
  p.get('btn-publish-exam').fire('click');
  await flush();
  assert.equal(p.requests[0].url, '/admin/exams/42/publish');
  assert.equal(p.requests[0].init.method, 'POST');
  assert.equal(p.requests[0].init.headers.RequestVerificationToken, 'csrf');
  assert.equal(p.context.location.reloaded, true);
});
test('quick ingest retains parse, commit, blueprint selection and MVC anti-forgery', async () => {
  const p = page('quick-ingest', { setTimeout: callback => callback() });
  let committed = false;
  p.context.QuickIngestModal.open(42, [{ SectionId: 7, Code: 'P2', Name: 'Responses', OptionCount: 3 }], 7, () => committed = true);
  assert.equal(p.get('quick-ingest-modal').style.display, 'flex');
  assert.match(p.get('qi-bp-guide').innerHTML, /Rapid Audio Answer-Key/);
  p.get('qi-raw-text').value = '1. A\n2. C';
  p.get('qi-parse-btn').fire('click');
  await flush();
  assert.equal(p.requests[0].url, '/admin/exams/42/quick-ingest/parse');
  assert.equal(p.requests[0].init.headers.RequestVerificationToken, 'csrf');
  assert.deepEqual(JSON.parse(p.requests[0].init.body), { rawText: '1. A\n2. C', sectionId: 7 });
  assert.equal(p.get('quick-ingest-commit').disabled, false);
  p.get('quick-ingest-commit').fire('click');
  await flush();
  assert.equal(p.requests[1].url, '/admin/exams/42/quick-ingest/commit');
  assert.equal(p.requests[1].init.headers.RequestVerificationToken, 'csrf');
  assert.equal(committed, true);
  assert.equal(p.get('quick-ingest-modal').style.display, 'none');
});
test('wizard step navigation still changes the bound step and visible panel', () => {
  const p = page('certificate-wizard');
  p.context.goToStep(2);
  assert.equal(p.get('currentStep').value, 2);
  assert.equal(p.get('step-2').style.display, 'block');
});
test('the two slice buttons store the player position as whole milliseconds', async () => {
  // #159: the author marks the slice on the player instead of typing milliseconds. The stored
  // value must be the position they heard, floored — a rounded-up start would begin after it.
  const p = page('exam-studio');
  const card = p.get('group-card-42');
  card.setAttribute('data-section-id', '7');
  const byField = { AudioStartMs: p.get('start-ms-42'), AudioEndMs: p.get('end-ms-42') };
  card.querySelector = selector => byField[(/data-field="([^"]+)"/.exec(selector) || [])[1]] || null;

  const audio = p.get('slicer-audio-7');
  audio.duration = 300;
  audio.currentTime = 12.3456;

  p.context.markGroupSliceStart(42);
  assert.equal(p.get('start-ms-42').value, '12345');

  audio.currentTime = 47.9;
  p.context.markGroupSliceEnd(42);
  await flush();
  assert.equal(p.get('end-ms-42').value, '47900');

  const saved = p.requests.find(r => r.url === '/admin/exams/42/studio/group/42');
  assert.equal(saved.init.method, 'PUT');
  const payload = JSON.parse(saved.init.body);
  assert.equal(payload.audioStartMs, 12345);
  assert.equal(payload.audioEndMs, 47900);
  assert.equal(saved.init.headers.RequestVerificationToken, 'csrf');
});
test('an end marked before the start is refused and nothing is saved', async () => {
  // CK_QGroups_AudioSlice rejects start >= end, so the button refuses in place instead of
  // letting the group save fail on a constraint.
  const p = page('exam-studio');
  const card = p.get('group-card-42');
  card.setAttribute('data-section-id', '7');
  const audio = p.get('slicer-audio-7');
  audio.duration = 300;

  audio.currentTime = 30;
  p.context.markGroupSliceStart(42);
  audio.currentTime = 12;
  p.context.markGroupSliceEnd(42);
  await flush();

  assert.equal(p.requests.filter(r => r.url.includes('/studio/group/')).length, 0);
  assert.match(p.get('slice-status-42').textContent, /End must come after start/);
  assert.equal(p.get('slice-status-42').hidden, false);
});
test('a structurally locked exam shows the #157 refusal message, not a bare HTTP 400', async () => {
  // #159 rebuilt the studio's add-question path. The structural lock still refuses — but the
  // author must be told why, with the message that names the finished attempts (#157).
  const p = page('exam-studio', {
    fetch: async (url) => {
      if (url.endsWith('/clusters')) {
        return {
          ok: false, status: 400,
          json: async () => ({
            message: "This exam's structure is locked: 1 learner attempt has already finished it.",
            code: 'EXAM_HAS_FINISHED_ATTEMPTS'
          })
        };
      }
      return { ok: true, json: async () => ({ success: true }) };
    }
  });

  p.context.createInitialCluster(7, 'single', 1);
  await flush();

  assert.equal(p.requests.filter(r => r.url === '/admin/exams/42/reorder').length, 0);
  assert.match(p.get('autosave-status').textContent, /structure is locked/);
  assert.match(p.get('autosave-status').textContent, /learner attempt has already finished it/);
});
test('a rejected upload shows the API message instead of failing silently', async () => {
  // #159: a wrong file type used to leave the old path in place with no sign of the refusal.
  const p = page('exam-studio', {
    fetch: async (url) => {
      if (url.endsWith('/media')) {
        return { ok: false, status: 400, json: async () => ({ message: "File format '.pdf' is not supported." }) };
      }
      return { ok: true, json: async () => ({ success: true }) };
    }
  });
  const input = Object.assign(element(), { files: [{ name: 'notes.pdf', size: 10 }] });

  p.context.uploadGroupAudioFile(input, 42);
  await flush();

  const status = p.get('slice-status-42');
  assert.match(status.textContent, /File format '\.pdf' is not supported/);
  assert.equal(status.getAttribute('data-state'), 'error');
  assert.equal(p.get('audio-path-42').value, '', 'a rejected file must not overwrite the stored path');
});
