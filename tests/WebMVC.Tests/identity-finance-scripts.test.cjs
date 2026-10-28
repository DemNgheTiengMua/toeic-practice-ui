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
  for (const name of ['checkout/index.js', 'support/report.js', 'admin/complaints/detail.js', 'admin/packages/index.js', 'admin/packages/form.js']) {
    const source = script(name);
    assert.doesNotThrow(() => new vm.Script(source));
    assert.doesNotMatch(source, /@(?:Model|Html|\()/);
  }
});

function checkout(response, payos = false, withForm = true) {
  const nodes = Object.fromEntries(['checkout-form', 'embedded-payment-modal', 'btn-close-embedded', 'btn-direct-checkout',
    'modal-cancel-orderCode', 'embedded-payment-container', 'modal-order-code', 'modal-package-name', 'modal-amount', 'modal-credits'].map(id => [id, element()]));
  const button = element();
  const form = nodes['checkout-form'];
  form.dataset = { packageName: 'Package "<script>" & friends', amount: '199,000 ₫', credits: '5' };
  form.querySelector = selector => selector.includes('__RequestVerificationToken') ? { value: 'csrf' }
    : selector.includes('packageId') ? { value: '2' } : button;
  if (!withForm) delete nodes['checkout-form'];
  const calls = [], alerts = [], gateway = [];
  const document = { getElementById: id => nodes[id] ?? null, addEventListener: (name, fn) => fn() };
  const location = { origin: 'http://localhost:5000', search: '' };
  const window = { location };
  const PayOSCheckout = { usePayOS(config) { gateway.push(config); return { open() {}, exit() {} }; } };
  if (payos) window.PayOSCheckout = PayOSCheckout;
  vm.runInNewContext(script('checkout/index.js'), { document, window, PayOSCheckout, URLSearchParams, FormData,
    console, alert: x => alerts.push(x), fetch: async (...args) => { calls.push(args); return response; } });
  return { nodes, form, button, calls, alerts, gateway, location };
}

test('checkout posts only to MVC with CSRF and redirects to mock checkout', async () => {
  const c = checkout({ ok: true, json: async () => ({ gateway: 'mock', checkoutUrl: '/mock-gateway?orderCode=O1' }) });
  await c.form.handlers.submit({ preventDefault() {} });
  assert.equal(c.calls[0][0], '/checkout');
  assert.equal(c.calls[0][1].method, 'POST');
  assert.equal(c.calls[0][1].body.get('__RequestVerificationToken'), 'csrf');
  assert.equal(c.calls[0][1].body.get('packageId'), '2');
  assert.equal(c.location.href, '/mock-gateway?orderCode=O1');
});

test('checkout keeps the gateway error visible and enables retry', async () => {
  const c = checkout({ ok: false, json: async () => ({ message: 'An unfinished order exists.' }) });
  await c.form.handlers.submit({ preventDefault() {} });
  assert.deepEqual(c.alerts, ['An unfinished order exists.']);
  assert.equal(c.button.disabled, false);
  assert.equal(c.location.href, undefined);
});

test('payOS modal uses decoded data attributes as text and keeps return routes', async () => {
  const c = checkout({ ok: true, json: async () => ({ gateway: 'payos', orderCode: 'O&1', checkoutUrl: 'https://pay.payos.vn/example' }) }, true);
  await c.form.handlers.submit({ preventDefault() {} });
  assert.equal(c.nodes['modal-package-name'].textContent, 'Package "<script>" & friends');
  assert.equal(c.nodes['modal-package-name'].innerHTML, '');
  assert.equal(c.nodes['modal-amount'].textContent, '199,000 ₫');
  assert.equal(c.nodes['modal-credits'].textContent, '+5 credits');
  assert.equal(c.gateway[0].RETURN_URL, 'http://localhost:5000/payment/result?orderCode=O%261');
  assert.equal(c.nodes['embedded-payment-modal'].style.display, 'flex');
  c.nodes['btn-close-embedded'].handlers.click();
  assert.equal(c.nodes['embedded-payment-modal'].style.display, 'none');
  assert.equal(c.button.disabled, false);
});

test('checkout can load blocked state without a form or modal', () => {
  assert.doesNotThrow(() => checkout({}, false, false));
});

test('support category options preserve requested type and switch subjects', () => {
  const nodes = Object.fromEntries(['sr-category', 'sr-type', 'field-booking', 'field-attempt', 'field-order'].map(id => [id, element()]));
  nodes['sr-category'].value = 'exam'; nodes['sr-category'].dataset.currentType = 'technical_error';
  const document = { getElementById: id => nodes[id], createElement: () => element(), addEventListener: (n, fn) => fn() };
  const context = { document };
  vm.runInNewContext(script('support/report.js'), context);
  assert.equal(nodes['field-attempt'].style.display, '');
  assert.equal(nodes['field-booking'].style.display, 'none');
  assert.equal(nodes['sr-type'].children.find(o => o.value === 'technical_error').selected, true);
  nodes['sr-category'].value = 'payment';
  context.updateFormCategories();
  assert.equal(nodes['field-order'].style.display, '');
  assert.equal(nodes['field-attempt'].style.display, 'none');
  assert.equal(nodes['sr-type'].children.at(-1).value, 'payment_issue');
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
