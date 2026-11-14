import '@testing-library/jest-dom/vitest';

if (typeof globalThis.CSS === 'undefined') {
  (globalThis as Record<string, unknown>).CSS = {
    escape: (value: string) => value.replace(/[^a-zA-Z0-9_-]/g, '\\$&'),
  };
} else if (typeof globalThis.CSS.escape !== 'function') {
  globalThis.CSS.escape = (value: string) => value.replace(/[^a-zA-Z0-9_-]/g, '\\$&');
}
