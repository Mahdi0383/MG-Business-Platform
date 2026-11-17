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
