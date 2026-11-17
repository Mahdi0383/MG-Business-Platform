import test from 'node:test';
import assert from 'node:assert/strict';
import { readJsonStorage, writeJsonStorage } from '../src/storage.js';

function createStorage(initial = {}) {
  const values = new Map(Object.entries(initial));
  return {
    getItem(key) { return values.has(key) ? values.get(key) : null; },
    setItem(key, value) { values.set(key, String(value)); },
    values,
  };
}

test('readJsonStorage returns the fallback when the key is missing', () => {
  const fallback = [{ id: 1 }];
  assert.equal(readJsonStorage('items', fallback, createStorage()), fallback);
});

test('readJsonStorage parses valid JSON', () => {
  const storage = createStorage({ items: '[{"id":2}]' });
  assert.deepEqual(readJsonStorage('items', [], storage), [{ id: 2 }]);
});

test('readJsonStorage preserves malformed data before returning the fallback', () => {
  const storage = createStorage({ items: '{not-json' });
  assert.deepEqual(readJsonStorage('items', [], storage), []);
  assert.equal(storage.getItem('items:corrupt-backup'), '{not-json');
  assert.equal(storage.getItem('items'), '{not-json');
});

test('readJsonStorage does not replace an existing recovery copy', () => {
  const storage = createStorage({ items: '{broken', 'items:corrupt-backup': 'earlier-copy' });
  readJsonStorage('items', [], storage);
  assert.equal(storage.getItem('items:corrupt-backup'), 'earlier-copy');
});

test('readJsonStorage falls back when storage access throws', () => {
  const fallback = [];
  const storage = { getItem() { throw new Error('blocked'); } };
  assert.equal(readJsonStorage('items', fallback, storage), fallback);
});

test('writeJsonStorage persists JSON and reports success', () => {
  const storage = createStorage();
  assert.equal(writeJsonStorage('items', [{ id: 3 }], storage), true);
  assert.equal(storage.getItem('items'), '[{"id":3}]');
});

test('writeJsonStorage reports unavailable or quota-limited storage', () => {
  assert.equal(writeJsonStorage('items', [], { setItem() { throw new Error('quota'); } }), false);
  assert.equal(writeJsonStorage('items', [], null), false);
});

test('writeJsonStorage reports serialization failures', () => {
  const cyclic = {};
  cyclic.self = cyclic;
  assert.equal(writeJsonStorage('items', cyclic, createStorage()), false);
});

test('readValidatedJsonStorage accepts data that satisfies its schema', async () => {
  const { readValidatedJsonStorage } = await import('../src/storage.js');
  const expected = [{ id: 1, name: 'Coffee', price: 120 }];
  const storage = createStorage({ products: JSON.stringify(expected) });
  assert.deepEqual(readValidatedJsonStorage('products', [], value =>
    Array.isArray(value) && value.every(item => Number.isInteger(item.id)
      && typeof item.name === 'string' && Number.isFinite(item.price) && item.price >= 0), storage), expected);
});

test('readValidatedJsonStorage preserves valid JSON with an invalid shape for recovery', async () => {
  const { readValidatedJsonStorage } = await import('../src/storage.js');
  const raw = JSON.stringify({ products: 'not-an-array' });
  const storage = createStorage({ products: raw });
  const fallback = [{ id: 0, name: 'safe fallback' }];
  assert.equal(readValidatedJsonStorage('products', fallback, Array.isArray, storage), fallback);
  assert.equal(storage.getItem('products:corrupt-backup'), raw);
  assert.equal(storage.getItem('products'), raw);
});

test('readValidatedJsonStorage preserves invalid array entries instead of rendering them', async () => {
  const { readValidatedJsonStorage } = await import('../src/storage.js');
  const raw = JSON.stringify([{ id: 1, name: 'valid' }, null]);
  const storage = createStorage({ products: raw });
  const fallback = [];
  assert.equal(readValidatedJsonStorage('products', fallback,
    value => Array.isArray(value) && value.every(item => item && typeof item.name === 'string'), storage), fallback);
  assert.equal(storage.getItem('products:corrupt-backup'), raw);
});

test('readValidatedJsonStorage safely rejects a throwing validator', async () => {
  const { readValidatedJsonStorage } = await import('../src/storage.js');
  const raw = '[1]';
  const storage = createStorage({ items: raw });
  const fallback = [];
  assert.equal(readValidatedJsonStorage('items', fallback, () => { throw new Error('invalid'); }, storage), fallback);
  assert.equal(storage.getItem('items:corrupt-backup'), raw);
});

test('writeJsonStorage reports values that JSON cannot serialize', () => {
  assert.equal(writeJsonStorage('items', undefined, createStorage()), false);
});
