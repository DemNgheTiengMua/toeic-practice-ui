const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../src/WebMVC/wwwroot/js/exams');
const previewSource = fs.readFileSync(path.join(root, 'preview.js'), 'utf8');
const navigationSource = fs.readFileSync(path.join(root, 'navigation.js'), 'utf8');

function control(order) {
  const classes = new Set();
  return {
    hidden: false,
    disabled: false,
    attributes: {},
    getAttribute(name) { return name === 'data-question-id' ? String(10 + order) : String(order); },
    setAttribute(name, value) { this.attributes[name] = value; },
    classList: {
      add: name => classes.add(name),
      remove: name => classes.delete(name),
      toggle(name, on) { if (on) classes.add(name); else classes.delete(name); },
      contains: name => classes.has(name)
    }
  };
}

// A sitting as navigation.js sees it. `preview` flips the one flag #158 added.
function sitting({ preview }) {
  const cells = [control(1), control(2)];
  const chips = [control(1), control(2)];
  const document = {
    querySelectorAll: selector => selector.indexOf('#navigator-grid') === 0 ? cells
      : selector.indexOf('#bottom-palette-chips') === 0 ? chips
      : [],
    getElementById: () => null,
    querySelector: () => null,
    addEventListener() {}
  };
  const window = { ExamSitting: {} };
  vm.runInNewContext(navigationSource, { window, document, console });

  const s = window.ExamSitting;
  s.isPreview = preview;
  s.previewExamId = 42;
  s.phases = [{ skillCode: 'LIST', lockNavigationOneWay: true, questions: [{ displayOrder: 1 }] }];
  s.activePhaseIndex = 0;
  s.currentDisplayOrder = 1;
  s.totalQuestions = 2;
  s.flaggedOnly = false;
  s.localAnswers = { 11: {}, 12: {} };
  s.responseDrafts = {};
  s.questions = [
    { questionId: 11, displayOrder: 1, skillCode: 'LIST', unitNumber: 1, navigationMode: 'FULL_SANDBOX' },
    { questionId: 12, displayOrder: 2, skillCode: 'READ', unitNumber: 1, navigationMode: 'FULL_SANDBOX' }
  ];
  s.questionMap = { 1: s.questions[0], 2: s.questions[1] };
  s.isSystemPacedAuto = () => false;
  s.getQuestionPhaseIndex = () => 0;
  s.activeUnitNumber = 1;
  s.activeGroupId = null;
  return { s, cells, chips };
}

test('a real sitting still disables the question in another skill', () => {
  const { s, cells } = sitting({ preview: false });

  s.updateNavigationForPhase();

  assert.equal(cells[0].disabled, false, 'the current question stays open');
  assert.equal(cells[1].disabled, true, '#149 lock logic: a question in another skill is not enterable');
});

test('a preview lets an admin reach a question in another skill and past a one-way lock', () => {
  const { s, cells, chips } = sitting({ preview: true });

  s.updateNavigationForPhase();

  assert.equal(cells[0].disabled, false);
  assert.equal(cells[1].disabled, false, 'a preview has nothing to protect, so nothing is locked');
  assert.equal(chips[1].disabled, false, 'the bottom palette unlocks with the sidebar');
  assert.equal(chips[1].hidden, false);
  assert.equal(chips[1].attributes['aria-disabled'], 'false');
});

test('the real sitting refuses a jump across skills; the preview allows it', () => {
  const real = sitting({ preview: false });
  const rendered = [];
  real.s.renderQuestion = order => rendered.push(order);
  real.s.goToQuestion(2);
  assert.deepEqual(rendered, [], 'a learner cannot jump into a skill the server has not opened');

  const preview = sitting({ preview: true });
  preview.s.renderQuestion = order => rendered.push(order);
  preview.s.goToQuestion(2);
  assert.deepEqual(rendered, [2], 'the admin must reach question 200 without clicking through');
});

test('the real sitting can still jump forward inside an audio-driven skill', () => {
  // #158 caught this live: `lockOneWay` is declared inside updateNavigationForPhase but read in
  // goToQuestion, so every forward jump from an AUDIO_DRIVEN question threw ReferenceError —
  // palette, sidebar and Next alike. The flag must be derived where it is used.
  const { s } = sitting({ preview: false });
  s.questions[0].navigationMode = 'AUDIO_DRIVEN';
  s.questionMap[1] = s.questions[0];
  const rendered = [];
  s.renderQuestion = order => rendered.push(order);

  assert.doesNotThrow(() => s.goToQuestion(2),
    'a forward jump from an audio-driven question must not throw');
  // It is still refused for the phase reason (question 2 is in another skill) — refused, not crashed.
  assert.deepEqual(rendered, []);
});

test('a forward jump inside the same audio-driven skill lands', () => {
  const { s } = sitting({ preview: false });
  s.questions[0].navigationMode = 'AUDIO_DRIVEN';
  s.questions[1] = Object.assign({}, s.questions[1], { skillCode: 'LIST', navigationMode: 'AUDIO_DRIVEN' });
  s.questionMap[2] = s.questions[1];
  const rendered = [];
  s.renderQuestion = order => rendered.push(order);

  assert.doesNotThrow(() => s.goToQuestion(2), 'no ReferenceError from the phase flag');
  assert.deepEqual(rendered, [2], 'forward inside the open skill still moves');
});

test('the preview URL stays on the admin path, the sitting URL does not move', () => {
  assert.match(navigationSource, /'\/admin\/exams\/' \+ sitting\.previewExamId \+ '\/preview\?q='/,
    'a swap in preview must not rewrite the URL to the learner sitting');
  assert.match(navigationSource, /'\/exams\/take\/' \+ sitting\.attemptId \+ '\?q='/,
    'the learner sitting keeps its own URL');
});

test('a preview never touches a real sitting\'s saved queue or audio key', () => {
  // The live bug this caught: preview attempt id 0 reused `ee_exam_pending_0`, restored a stale
  // queue at boot and fired a real sync, which painted "Offline · Saved locally" over the page
  // and would have written to whatever attempt shared that key.
  const contextSource = fs.readFileSync(path.join(root, 'context.js'), 'utf8');
  const audioSource = fs.readFileSync(path.join(root, 'audio.js'), 'utf8');

  assert.match(contextSource, /sitting\.isPreview\s*\?\s*'ee_exam_preview_' \+ sitting\.previewExamId/,
    'a preview must key its backup on the exam, not the attempt');
  assert.match(contextSource, /sitting\.isPreview \? null : localStorage\.getItem\(sitting\.storageKey\)/,
    'a preview must never restore another page\'s queued answers');
  assert.match(audioSource, /sitting\.isPreview\s*\?\s*'ee_exam_preview_audio_' \+ sitting\.previewExamId/,
    'audio progress must not be shared with a real attempt either');
});

test('preview.js exits at once on a learner sitting', () => {
  const document = { getElementById: () => null, querySelectorAll: () => [], addEventListener() {} };
  const window = { ExamSitting: { isPreview: false }, fetch: () => Promise.resolve('network') };
  vm.runInNewContext(previewSource, { window, document, console, Response, Promise });

  assert.equal(window.ExamSitting.startSpeakingPacerStateMachine, undefined,
    'loading the preview script on a real sitting must not touch the shell');
  assert.equal(window.ExamSitting.isSubmitting, undefined);
});

test('preview.js makes a preview read-only: no submit, no sync, no write to /exams', async () => {
  const document = { getElementById: () => null, querySelectorAll: () => [], addEventListener() {} };
  const window = { ExamSitting: { isPreview: true }, fetch: () => Promise.resolve('network') };
  vm.runInNewContext(previewSource, { window, document, console, Response, Promise });
  const s = window.ExamSitting;

  assert.equal(s.isSubmitting, true, 'the countdown must not auto-submit a preview');
  // The two writers the shell would call on an answer click are inert.
  const answer = await window.fetch('/exams/attempts/0/sync', { method: 'POST' });
  assert.equal(answer.status, 200, 'a write is swallowed, not attempted');
  assert.equal(await window.fetch('/exams/take/0/submit', { method: 'POST' }).then(r => r.ok), true);
  assert.equal(await window.fetch('/api/learner/context').then(r => r === 'network'), true,
    'a read still reaches the real fetch');
});
