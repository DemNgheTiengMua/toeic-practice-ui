const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = name => fs.readFileSync(path.join(__dirname, '../../src/WebMVC/wwwroot/js', name), 'utf8');
function element() {
  const classes = new Set();
  return { value: '', dataset: {}, textContent: '', innerHTML: '', style: {}, handlers: {}, attributes: {}, children: [],
    classList: { add: n => classes.add(n), remove: n => classes.delete(n), contains: n => classes.has(n) },
    addEventListener(n, f) { this.handlers[n] = f; },
    setAttribute(n, v) { this.attributes[n] = v; }, removeAttribute(n) { delete this.attributes[n]; },
    getAttribute(n) { return this.attributes[n] ?? null; },
    appendChild(child) { this.children.push(child); }, focus() { this.focused = true; },
    querySelector() { return null; }
  };
}

test('all finance/support scripts parse and have no Razor expressions', () => {
  for (const name of ['admin/complaints/detail.js', 'admin/packages/index.js', 'admin/packages/form.js']) {
    const source = script(name);
    assert.doesNotThrow(() => new vm.Script(source));
    assert.doesNotMatch(source, /@(?:Model|Html|\()/);
  }
});

test('complaint resolution prevents blank fields, clears errors, then submits', () => {
  const form = element(), resolution = element(), notes = element();
  for (const input of [resolution, notes]) { input.field = element(); input.closest = () => input.field; }
  const document = { querySelector: () => form, getElementById: id => id === 'cd-resolution' ? resolution : notes };
  vm.runInNewContext(script('admin/complaints/detail.js'), { document });
  let prevented = false;
  form.handlers.submit({ preventDefault() { prevented = true; } });
  assert.equal(prevented, true); assert.equal(resolution.focused, true);
  assert.equal(notes.attributes['aria-invalid'], 'true');
  resolution.value = 'Refunded'; notes.value = 'Verified payment event';
  resolution.handlers.input(); notes.handlers.input();
  assert.equal(resolution.field.classList.contains('field--error'), false);
  prevented = false; form.handlers.submit({ preventDefault() { prevented = true; } });
  assert.equal(prevented, false);
});

test('package edit dialog loads every bound field from the selected package', () => {
  const ids = ['pkg-modal', 'pkg-modal-form', 'modal-pkg-title', 'btn-create-pkg', 'modal-pkg-cancel', 'modal-pkg-name',
    'modal-pkg-credits', 'modal-pkg-price', 'modal-pkg-status', 'modal-pkg-order', 'modal-pkg-features', 'modal-pkg-featured', 'modal-pkg-unit-hint'];
  const nodes = Object.fromEntries(ids.map(id => [id, element()]));
  const button = element(); button.attributes = { 'data-id': '8', 'data-name': 'Starter', 'data-credits': '4', 'data-price': '120000',
    'data-active': 'true', 'data-featured': 'true', 'data-order': '3', 'data-features': 'Never expire' };
  vm.runInNewContext(script('admin/packages/index.js'), { document: { getElementById: id => nodes[id], querySelectorAll: () => [button] } });
  button.handlers.click({ preventDefault() {} });
  assert.equal(nodes['pkg-modal-form'].action, '/admin/packages/edit/8');
  assert.equal(nodes['modal-pkg-name'].value, 'Starter');
  assert.equal(nodes['modal-pkg-featured'].checked, true);
  assert.equal(nodes['modal-pkg-unit-hint'].textContent, 'Unit price: 30,000 ₫/credit');
  assert.equal(nodes['pkg-modal'].style.display, 'flex');
  nodes['modal-pkg-cancel'].handlers.click();
  assert.equal(nodes['pkg-modal'].style.display, 'none');
});
