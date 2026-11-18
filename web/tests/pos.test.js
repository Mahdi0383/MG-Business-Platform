import test from 'node:test';
import assert from 'node:assert/strict';
import { applySaleToInventory, calculateCartTotal, nextOrderIdentifier, validateCartAgainstInventory } from '../src/pos.js';

const catalog = [
  { id: 1, name: 'Coffee', price: 120, stock: 5, sold: 2 },
  { id: 2, name: 'Cake', price: 80, stock: 3, sold: 4 },
];

test('validates cart quantities against current stock', () => {
  assert.equal(validateCartAgainstInventory([{ id: 1, qty: 2 }], catalog), true);
  assert.equal(validateCartAgainstInventory([{ id: 1, qty: 6 }], catalog), false);
  assert.equal(validateCartAgainstInventory([{ id: 9, qty: 1 }], catalog), false);
  assert.equal(validateCartAgainstInventory([{ id: 1, qty: 0 }], catalog), false);
  assert.equal(validateCartAgainstInventory([], catalog), false);
});

test('calculates sale total from current catalog prices, not stale cart prices', () => {
  assert.equal(calculateCartTotal([{ id: 1, qty: 2, price: 1 }], catalog), 240);
  assert.equal(calculateCartTotal([{ id: 1, qty: 6 }], catalog), null);
});

test('applies sale inventory changes immutably and updates sold counts', () => {
  const cart = [{ id: 1, qty: 2 }, { id: 2, qty: 1 }];
  const updated = applySaleToInventory(cart, catalog);
  assert.deepEqual(updated, [
    { id: 1, name: 'Coffee', price: 120, stock: 3, sold: 4 },
    { id: 2, name: 'Cake', price: 80, stock: 2, sold: 5 },
  ]);
  assert.equal(catalog[0].stock, 5);
  assert.equal(applySaleToInventory([{ id: 1, qty: 99 }], catalog), null);
});

test('generates the next order id without colliding with existing ids', () => {
  assert.equal(nextOrderIdentifier([{ id: 1049 }, { id: 1053 }, { id: 1050 }]), 1054);
  assert.equal(nextOrderIdentifier([]), 1049);
  assert.equal(nextOrderIdentifier([{ id: 'invalid' }]), null);
});

test('rejects duplicate product lines and ambiguous duplicate catalog ids', () => {
  const duplicateLines = [{ id: 1, qty: 2 }, { id: 1, qty: 1 }];
  assert.equal(validateCartAgainstInventory(duplicateLines, catalog), false);
  assert.equal(calculateCartTotal(duplicateLines, catalog), null);
  assert.equal(applySaleToInventory(duplicateLines, catalog), null);

  const duplicateCatalog = [...catalog, { ...catalog[0] }];
  assert.equal(validateCartAgainstInventory([{ id: 1, qty: 1 }], duplicateCatalog), false);
});

test('rejects arithmetic overflow instead of producing invalid totals or inventory', () => {
  const expensiveCatalog = [{ id: 3, price: Number.MAX_VALUE, stock: 3, sold: 0 }];
  assert.equal(calculateCartTotal([{ id: 3, qty: 2 }], expensiveCatalog), null);

  const overflowSoldCatalog = [{ id: 4, price: 10, stock: 3, sold: Number.MAX_SAFE_INTEGER }];
  assert.equal(applySaleToInventory([{ id: 4, qty: 1 }], overflowSoldCatalog), null);
});

test('rejects order identifiers that would exceed JavaScript safe integers', () => {
  assert.equal(nextOrderIdentifier([{ id: Number.MAX_SAFE_INTEGER }]), null);
});
