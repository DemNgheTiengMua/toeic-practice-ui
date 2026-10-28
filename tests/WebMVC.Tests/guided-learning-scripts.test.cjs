const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../src/WebMVC/wwwroot/js');
const script = name => fs.readFileSync(path.join(root, name), 'utf8');

function element(attributes = {}) {
  const classes = new Set();
  return {
    dataset: {}, style: {}, handlers: {}, attributes, disabled: false, textContent: '',
    classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x),
      toggle(x, value) { if (value) classes.add(x); else classes.delete(x); } },
    getAttribute(name) { return this.attributes[name] ?? null; },
    setAttribute(name, value) { this.attributes[name] = value; },
    addEventListener(name, handler) { this.handlers[name] = handler; },
    querySelector() { return null; }, appendChild() {}
  };
}

function player(preview = false, initial = 1, completed = false) {
  const deck = element();
  deck.dataset = { lessonId: '7', initialIndex: String(initial), totalCards: '2', completed: String(completed) };
  const nodes = Object.fromEntries(['btnPrev', 'btnNext', 'btnFinish', 'cardTypeBadge', 'cardCounter',
    'deckProgressLabel', 'btnCheckAnswer', 'microCheckExplanation'].map(id => [id, element()]));
  nodes['card-1'] = element({ 'data-card-id': '11', 'data-card-type': 'rule' });
  nodes['card-2'] = element({ 'data-card-id': '22', 'data-card-type': 'micro_check', 'data-correct': 'B' });
  const options = ['A', 'B'].map(value => {
    const option = element({ 'data-opt': value });
    option.radio = { checked: false };
    option.querySelector = () => option.radio;
    return option;
  });
  const calls = [];
  const alerts = [];
  const document = {
    getElementById: id => nodes[id] ?? null,
    querySelector(selector) {
      if (selector === '.lesson-deck') return deck;
      if (selector.includes('__RequestVerificationToken')) return { value: 'csrf-token' };
      if (selector.includes(':checked')) return options.find(o => o.radio.checked)?.radio ?? null;
      if (selector.includes('data-card-type')) return nodes['card-2'];
      return null;
    },
    querySelectorAll: selector => selector === '.micro-check-option' ? options : options.map(o => o.radio)
  };
  vm.runInNewContext(script(preview ? 'admin/lessons/preview.js' : 'lessons/player.js'), {
    document, fetch: (...args) => { calls.push(args); return Promise.resolve({ ok: true }); },
    console, alert: message => alerts.push(message)
  });
  return { nodes, options, calls, alerts };
}

test('all extracted scripts parse without Razor tokens', () => {
  for (const name of ['lessons/player.js', 'admin/lessons/preview.js', 'admin/lessons/edit.js',
    'admin/lessons/index.js', 'dashboard/simulator.js']) {
    assert.doesNotThrow(() => new vm.Script(script(name)));
    assert.doesNotMatch(script(name), /@(?:Model|Json|initialIndex|totalCards)/);
  }
});

test('learner resumes saved card and keeps completion gated by the micro-check', () => {
  const { nodes, calls } = player(false, 2);
  assert.equal(nodes['card-1'].style.display, 'none');
  assert.equal(nodes['card-2'].style.display, 'block');
  assert.equal(nodes.btnFinish.style.display, 'none');
  assert.equal(nodes.cardCounter.textContent, '2 / 2 cards');
  assert.equal(calls.length, 0);
});

test('navigation records the next card through MVC with anti-forgery', () => {
  const { nodes, calls, options, alerts } = player();
  assert.equal(nodes.btnPrev.disabled, true);
  nodes.btnNext.handlers.click();
  assert.equal(calls[0][0], '/lessons/7/cards/22/view');
  assert.equal(calls[0][1].method, 'POST');
  assert.equal(calls[0][1].headers.RequestVerificationToken, 'csrf-token');
  assert.equal(nodes.btnFinish.style.display, 'none');
  nodes.btnCheckAnswer.handlers.click();
  assert.equal(alerts.length, 1);
  options[1].handlers.click();
  nodes.btnCheckAnswer.handlers.click();
  assert.equal(nodes.btnFinish.style.display, 'inline-block');
  assert.equal(nodes.btnCheckAnswer.disabled, true);
  assert.equal(options[1].classList.contains('is-correct'), true);
  assert.equal(options[0].radio.disabled, true);
  nodes.btnPrev.handlers.click();
  assert.equal(nodes['card-1'].style.display, 'block');
});

test('completed learner lesson and admin preview keep their distinct finish behavior', () => {
  const completed = player(false, 2, true);
  assert.equal(completed.nodes.btnFinish.style.display, 'inline-block');
  const preview = player(true);
  preview.nodes.btnNext.handlers.click();
  assert.equal(preview.nodes.btnFinish.style.display, 'inline-block');
  assert.equal(preview.calls.length, 0);
});

test('lesson delete confirmation reads the encoded attribute and submits only after approval', () => {
  let submitted = 0;
  let approved = false;
  let message;
  const form = { dataset: { lessonTitle: '\"<script>bad()</script>' }, submit() { submitted++; } };
  const context = { document: { getElementById: id => id === 'delete-lesson-form' ? form : null,
    addEventListener() {} }, confirm: text => { message = text; return approved; } };
  vm.runInNewContext(script('admin/lessons/edit.js'), context);
  context.confirmDeleteLesson();
  assert.equal(submitted, 0);
  assert.ok(message.includes(form.dataset.lessonTitle));
  approved = true;
  context.confirmDeleteLesson();
  assert.equal(submitted, 1);
});

test('simulator recalculates scores and band classes then resets to baseline', () => {
  const slider = element({ 'data-skill-id': '1', 'data-skill-name': 'Reading', 'data-max-score': '100', 'data-base-score': '20' });
  slider.value = '20';
  const bands = [{ code: 'B1', name: 'Threshold', minTotal: 0, maxTotal: 49 },
    { code: 'B2', name: 'Vantage', minTotal: 50, maxTotal: 100 }];
  const cells = bands.map(b => element({ 'data-band-code': b.code, 'data-max-total': String(b.maxTotal) }));
  const simulator = element({ 'data-target-code': 'B2', 'data-target-min': '50', 'data-bands': JSON.stringify(bands) });
  simulator.querySelectorAll = selector => selector === '[data-sim-slider]' ? [slider] : cells;
  const nodes = Object.fromEntries(['sim-total-score', 'sim-matched-band-code', 'sim-matched-band-name',
    'sim-deficit-badge', 'sim-reset-btn'].map(id => [id, element()]));
  nodes['dashboard-simulator'] = simulator;
  vm.runInNewContext(script('dashboard/simulator.js'), { document: { getElementById: id => nodes[id] ?? null } });
  slider.value = '70';
  slider.handlers.input();
  assert.equal(nodes['sim-total-score'].textContent, '70');
  assert.equal(nodes['sim-matched-band-code'].textContent, 'B2');
  assert.equal(cells[1].classList.contains('is-current'), true);
  assert.equal(cells[0].classList.contains('is-reached'), true);
  assert.equal(nodes['sim-deficit-badge'].getAttribute('data-deficit'), '0');
  nodes['sim-reset-btn'].handlers.click();
  assert.equal(slider.value, '20');
  assert.equal(nodes['sim-matched-band-code'].textContent, 'B1');
  assert.equal(nodes['sim-deficit-badge'].textContent, '-30 pts to B2');
});
