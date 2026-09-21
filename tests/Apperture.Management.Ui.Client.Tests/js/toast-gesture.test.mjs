// Pure gesture math from wwwroot/js/toast-host.js (no DOM). Run with Node's built-in runner:
//   node --test tests/Apperture.Management.Ui.Client.Tests/js/toast-gesture.test.mjs
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { math } = require('../../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/toast-host.js');

test('threshold scales with toast width but stays within sensible bounds', () => {
  assert.equal(math.dismissThreshold(100), 72);    // very narrow: floor
  assert.equal(math.dismissThreshold(374), 374 * 0.3); // phone
  assert.equal(math.dismissThreshold(672), 672 * 0.3); // widest toast the layout produces
  assert.equal(math.dismissThreshold(1000), 220);  // cap for anything wider
});

test('a small movement is undecided; then horizontal, vertical or diagonal intent is resolved', () => {
  assert.equal(math.resolveIntent(3, 2), 'pending');
  assert.equal(math.resolveIntent(20, 3), 'horizontal');
  assert.equal(math.resolveIntent(-20, 3), 'horizontal');
  assert.equal(math.resolveIntent(4, 30), 'vertical');
  assert.equal(math.resolveIntent(15, 15), 'vertical', 'a 45-degree drag is left to scrolling');
});

test('dismissal is symmetric and triggers exactly at the threshold', () => {
  const t = 100;
  assert.equal(math.shouldDismiss(99, t), false);
  assert.equal(math.shouldDismiss(100, t), true);
  assert.equal(math.shouldDismiss(-100, t), true);
  assert.equal(math.shouldDismiss(-99, t), false);
});

test('opacity is unchanged in the dead zone, falls monotonically, and is always within 0..1', () => {
  const t = 100;
  assert.equal(math.opacityFor(0, t), 1);
  assert.equal(math.opacityFor(10, t), 1, 'tiny drags stay fully readable');
  let last = 1;
  for (let dx = 0; dx <= 400; dx += 5) {
    const o = math.opacityFor(dx, t);
    assert.ok(o >= 0 && o <= 1, `out of range at ${dx}`);
    assert.ok(o <= last + 1e-9, `not monotonic at ${dx}`);
    last = o;
  }
  assert.equal(math.opacityFor(-60, t), math.opacityFor(60, t), 'left and right fade alike');
  assert.equal(math.opacityFor(10_000, t), 0);
});
