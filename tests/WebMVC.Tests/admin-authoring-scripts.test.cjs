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
    fetch: async (url, init) => { requests.push({ url, init }); return { ok: true, json: async () => ({ success: true, questions: [{ index: 1, isValid: true, stem: "Prompt", options: [], issues: [] }], globalIssues: [] }) }; },
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
