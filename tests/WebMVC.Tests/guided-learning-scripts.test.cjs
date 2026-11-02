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
    'deckProgressLabel', 'btnCheckAnswer', 'microCheckExplanation', 'microCheckVerdict',
    'microCheckCorrectAnswer'].map(id => [id, element()]));
  nodes['card-1'] = element({ 'data-card-id': '11', 'data-card-type': 'rule' });
  // #155: no data-correct any more - the page must not be able to grade itself.
  nodes['card-2'] = element({ 'data-card-id': '22', 'data-card-type': 'micro_check' });
  const options = ['A', 'B'].map(value => {
    const option = element({ 'data-opt': value });
    option.radio = { checked: false, disabled: false, value };
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
    document,
    fetch: (...args) => {
      calls.push(args);
      return Promise.resolve({ ok: true, json: () => Promise.resolve({ isCorrect: true, correctOption: 'B' }) });
    },
    URLSearchParams,
    console, alert: message => alerts.push(message)
  });
  return { nodes, options, calls, alerts };
}

const flush = () => new Promise(resolve => setImmediate(resolve));

test('all extracted scripts parse without Razor tokens', () => {
  for (const name of ['lessons/player.js', 'admin/lessons/preview.js', 'admin/lessons/edit.js',
    'admin/lessons/index.js']) {
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
  nodes.btnPrev.handlers.click();
  assert.equal(nodes['card-1'].style.display, 'block');
});

// #155: grading moved to the server, so the page posts the chosen option and paints the reply. What
// must never come back is the page deciding right from wrong itself - there is no correct option in
// the markup for it to read.
test('the micro-check is posted to the server and the reply decides the colours', async () => {
  const page = player(false, 2);
  page.options[1].radio.checked = true;

  assert.equal(page.nodes['card-2'].attributes['data-correct'], undefined,
    'the card must not carry data-correct: the page cannot grade itself');

  page.nodes.btnCheckAnswer.handlers.click();
  const call = page.calls.find(c => String(c[0]).includes('/micro-check'));
  assert.ok(call, 'the check must reach /lessons/{id}/cards/{cardId}/micro-check');
  assert.equal(call[1].method, 'POST');
  assert.equal(call[1].headers.RequestVerificationToken, 'csrf-token');
  assert.match(String(call[1].body), /selectedOption=B/);

  await flush();
  assert.equal(page.nodes.btnCheckAnswer.disabled, true);
  assert.equal(page.options[1].classList.contains('is-correct'), true);
  assert.equal(page.nodes.btnFinish.style.display, 'inline-block');
  assert.equal(page.nodes.microCheckExplanation.style.display, 'block');
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

