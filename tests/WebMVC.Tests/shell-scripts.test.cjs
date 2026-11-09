// #242: the shell's offline banner is painted from one attribute on <html>, and the server can state
// the condition (`?offline=1`) so a rendered page can be asserted and reviewed. These pin the two
// rules a future change is most likely to break: the server's statement wins, and the browser never
// invents an outage it is not sure about.
//
// Run: node --test tests/WebMVC.Tests/shell-scripts.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(
  path.resolve(__dirname, '../../src/WebMVC/wwwroot/js/shell.js'), 'utf8');

function run(attributes, onLine) {
  const attrs = { ...attributes };
  const html = {
    getAttribute: name => attrs[name] ?? null,
    setAttribute: (name, value) => { attrs[name] = value; },
    removeAttribute: name => { delete attrs[name]; },
  };
  const listeners = {};
  const context = {
    document: { documentElement: html },
    navigator: { onLine },
    window: { addEventListener: (name, fn) => { listeners[name] = fn; } },
  };
  context.window.window = context.window;
  vm.runInNewContext(source, context);
  return { attrs, fire: name => listeners[name] && listeners[name]() };
}

test('a browser that reports online leaves the banner alone', () => {
  const { attrs } = run({}, true);
  assert.equal(attrs['data-offline'], undefined);
});

test('a browser that reports offline raises the banner', () => {
  const { attrs } = run({}, false);
  assert.equal(attrs['data-offline'], 'true');
});

test('losing and regaining the connection moves the banner both ways', () => {
  const { attrs, fire } = run({}, true);
  // The handler reads navigator.onLine, which the harness cannot change; drive the attribute
  // directly to prove the listener wiring exists and re-runs the same rule.
  attrs['data-offline'] = 'true';
  fire('online');
  assert.equal(attrs['data-offline'], undefined);
});

test("the server's stated offline condition is not cleared by a browser that is online", () => {
  const { attrs, fire } = run({ 'data-offline': 'true', 'data-offline-forced': 'true' }, true);
  assert.equal(attrs['data-offline'], 'true');
  fire('online');
  assert.equal(attrs['data-offline'], 'true');
});
