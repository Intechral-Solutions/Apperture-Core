// Browser harness for the toast stack: production CSS + production toast-host.js + the markup the REAL ToastHost
// renders (exported by ToastHostRenderTests), driven through headless Chrome over the DevTools Protocol.
//
//   set APPERTURE_TOAST_FIXTURE_DIR=<dir>
//   dotnet test tests/Apperture.Management.Ui.Client.Tests -c Debug --filter "FullyQualifiedName~Rendered_markup_can_be_exported"
//   node tests/Apperture.Management.Ui.Client.Tests/browser/toast-browser-harness.mjs <dir> [--shots <dir>]
//
// No npm packages: Node 22+ (global WebSocket/fetch) and a local Chrome/Edge (CHROME_PATH overrides discovery).
// The .NET side is stubbed (SetHold / Swipe / Dismiss), so this proves layout, hit-testing, gestures, scrolling and
// CSS/JS/markup agreement. It does NOT exercise Blazor's renderer or JS interop; ToastServiceTests and a manual
// smoke of the real app cover those.
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..', '..');
const clientRoot = path.join(repoRoot, 'src', 'Apperture.ControlPlane', 'Apperture.Management.Ui.Client', 'wwwroot');
const argv = process.argv.slice(2);
const shotsIndex = argv.indexOf('--shots');
const shotsDir = shotsIndex >= 0 ? argv[shotsIndex + 1] : null;
const positional = argv.filter((a, i) => !a.startsWith('--') && !(shotsIndex >= 0 && i === shotsIndex + 1));
const fixtureDir = positional[0] || process.env.APPERTURE_TOAST_FIXTURE_DIR;
if (!fixtureDir || !existsSync(path.join(fixtureDir, 'toast-host.main.html'))) {
  console.error('Fixture not found. Export it first (see the header of this file).');
  process.exit(2);
}
if (shotsDir) mkdirSync(shotsDir, { recursive: true });

const chromePath = [process.env.CHROME_PATH,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome']
  .filter(Boolean).find((p) => existsSync(p));
if (!chromePath) { console.error('No Chrome/Edge found; set CHROME_PATH.'); process.exit(2); }

const tmp = mkdtempSync(path.join(os.tmpdir(), 'toast-harness-'));
const cssUrl = (f) => pathToFileURL(path.join(clientRoot, 'css', f)).href;
const jsUrl = pathToFileURL(path.join(clientRoot, 'js', 'toast-host.js')).href;

function pageFor(variant) {
  const fragment = readFileSync(path.join(fixtureDir, `toast-host.${variant}.html`), 'utf8');
  const file = path.join(tmp, `page-${variant}.html`);
  writeFileSync(file, `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0"><title>Toast harness</title>
<link rel="stylesheet" href="${cssUrl('app.css')}"><link rel="stylesheet" href="${cssUrl('management-ui-primitives.css')}">
<script src="${jsUrl}"></script>
<style>html,body{margin:0;height:100%;background:var(--color-bg);color:var(--color-text-body);font-family:system-ui,sans-serif}
#rail{position:fixed;left:0;top:0;bottom:0;width:4.5rem;background:var(--color-surface);z-index:60}
#rail button{position:absolute;left:1rem;bottom:.5rem}#page{padding:1rem 1rem 1rem 6rem}
#save{position:fixed;right:1rem;bottom:1rem}</style></head><body>
<div id="rail"><button id="rail-config" type="button">cfg</button></div>
<div id="page"><h1>Harness page</h1><p>Page content the toasts float over.</p></div><button id="save">Save</button>
${fragment}
<script>
  window.holds = []; window.swipes = []; window.dismissals = [];
  const stack = document.querySelector('.ui-toast-stack');
  const templates = {};
  document.querySelectorAll('.ui-toast-slot').forEach((slot) => { templates[slot.querySelector('.ui-toast').dataset.severity] = slot.cloneNode(true); });
  if (location.hash !== '#keep') document.querySelectorAll('.ui-toast-slot').forEach((s) => s.remove());
  function leave(slot, exit) {
    if (!slot || slot.dataset.phase === 'exiting') return;
    slot.dataset.phase = 'exiting'; slot.dataset.exit = exit; setTimeout(() => slot.remove(), 300);   // ToastPolicy.ExitDuration
  }
  // Stand-in for ToastHost's [JSInvokable] methods and its @onclick. Everything else is the real DOM, CSS and script.
  const dotNet = { invokeMethodAsync(name, ...a) {
    if (name === 'SetHold') window.holds.push(a);
    if (name === 'Swipe') { window.swipes.push(a); const c = document.querySelector('[data-toast-id="' + a[0] + '"]'); leave(c && c.closest('.ui-toast-slot'), a[1]); }
    return Promise.resolve(); } };
  document.addEventListener('click', (e) => {
    const b = e.target.closest && e.target.closest('.ui-toast__dismiss'); if (!b) return;
    const slot = b.closest('.ui-toast-slot'); window.dismissals.push(slot.querySelector('.ui-toast').dataset.toastId); leave(slot, 'fade');
  });
  let seq = 0;
  window.addToast = (severity, title, message) => {
    const slot = templates[severity].cloneNode(true); const card = slot.querySelector('.ui-toast');
    card.dataset.toastId = 'clone-' + (++seq);
    const body = card.querySelector('.ui-toast__body'); const msg = body.querySelector(':scope > div:last-child'); msg.textContent = message;
    let t = body.querySelector('.ui-toast__title');
    if (title) { if (!t) { t = document.createElement('div'); t.className = 'ui-toast__title'; body.insertBefore(t, msg); } t.textContent = title; } else if (t) t.remove();
    slot.className = 'ui-toast-slot ui-toast-slot--enter'; card.className = 'ui-toast ui-toast--enter';
    stack.appendChild(slot); return card.dataset.toastId;
  };
  window.attachStack = () => window.appertureToast.attach(stack, dotNet);
  window.detachStack = () => window.appertureToast.detach(stack);
  window.attachStack();
</script></body></html>`);
  return pathToFileURL(file).href;
}
const pages = { main: pageFor('main'), auth: existsSync(path.join(fixtureDir, 'toast-host.auth.html')) ? pageFor('auth') : null };

// ---- CDP plumbing ----
const PORT = 9400 + Math.floor(Math.random() * 400);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const results = [];
const check = (name, ok, detail = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`); };
const near = (a, b, t) => Math.abs(a - b) <= t;
const chrome = spawn(chromePath, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${path.join(tmp, 'profile')}`,
  '--no-first-run', '--disable-gpu', '--allow-file-access-from-files', 'about:blank'], { stdio: 'ignore' });
let wsUrl;
for (let i = 0; i < 80 && !wsUrl; i++) { try { wsUrl = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find((t) => t.type === 'page')?.webSocketDebuggerUrl; } catch { await sleep(250); } }
if (!wsUrl) { console.error('Chrome did not start'); process.exit(2); }
const ws = new WebSocket(wsUrl); await new Promise((r) => (ws.onopen = r));
let nextId = 1; const pending = new Map(); const pageErrors = [];
ws.onmessage = (m) => {
  const msg = JSON.parse(m.data);
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
  if (msg.method === 'Runtime.exceptionThrown') pageErrors.push((msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text).slice(0, 200));
};
const send = (method, params = {}) => new Promise((res, rej) => { const id = nextId++; pending.set(id, (m) => (m.error ? rej(new Error(method + ': ' + m.error.message)) : res(m.result))); ws.send(JSON.stringify({ id, method, params })); });
const js = async (expression) => { const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
let runs = 0;
async function load(variant, w, h, { mobile = false, touch = false, reduced = false, dark = false, keep = false } = {}) {
  await send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile });
  await send('Emulation.setTouchEmulationEnabled', { enabled: touch, maxTouchPoints: 1 });
  await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: reduced ? 'reduce' : 'no-preference' }, { name: 'pointer', value: touch ? 'coarse' : 'fine' }] });
  await send('Page.enable'); await send('Runtime.enable');
  await send('Page.navigate', { url: pages[variant] + '?run=' + (++runs) + (keep ? '#keep' : '') });  // unique URL: same-document navigations would not reload
  await sleep(600);
  if (dark) await js(`document.documentElement.classList.add('dark')`);
}
const rect = (sel, i = 0) => js(`(() => { const e = document.querySelectorAll(${JSON.stringify(sel)})[${i}]; if (!e) return null; const r = e.getBoundingClientRect(); return {x:r.x,y:r.y,w:r.width,h:r.height,b:r.bottom,r:r.right,cx:r.x+r.width/2,cy:r.y+r.height/2}; })()`);
const count = (sel) => js(`document.querySelectorAll(${JSON.stringify(sel)}).length`);
const mouse = (type, x, y, extra = {}) => send('Input.dispatchMouseEvent', { type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, ...extra });
const move = (x, y) => send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
async function click(sel, i = 0) { const r = await rect(sel, i); await move(r.cx, r.cy); await mouse('mousePressed', r.cx, r.cy); await mouse('mouseReleased', r.cx, r.cy); }
async function drag(from, to, steps = 8) { await move(from.x, from.y); await mouse('mousePressed', from.x, from.y); for (let i = 1; i <= steps; i++) { await mouse('mouseMoved', from.x + ((to.x - from.x) * i) / steps, from.y + ((to.y - from.y) * i) / steps); await sleep(16); } }
const touch = (type, x, y) => send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' || type === 'touchCancel' ? [] : [{ x, y, id: 1 }] });
async function dragTouch(from, to, steps = 8) { await touch('touchStart', from.x, from.y); for (let i = 1; i <= steps; i++) { await touch('touchMove', from.x + ((to.x - from.x) * i) / steps, from.y + ((to.y - from.y) * i) / steps); await sleep(16); } await touch('touchEnd', to.x, to.y); }
const dragState = (i = 0) => js(`(() => { const e = document.querySelectorAll('.ui-toast')[${i}]; return { dx: e.style.getPropertyValue('--toast-dx'), op: e.style.getPropertyValue('--toast-opacity'), dragging: e.hasAttribute('data-dragging'), translate: getComputedStyle(e).translate, cop: getComputedStyle(e).opacity }; })()`);
async function shot(name) { if (!shotsDir) return; const r = await send('Page.captureScreenshot', { format: 'png' }); writeFileSync(path.join(shotsDir, name + '.png'), Buffer.from(r.data, 'base64')); }
const hitIsInside = (sel, i = 0) => js(`(() => { const t = document.querySelectorAll(${JSON.stringify(sel)})[${i}]; const r = t.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return !!e && (e === t || t.contains(e)); })()`);
const addAll = () => js(`(() => { addToast('success', 'Saved', 'Sign-in policy saved.'); addToast('info', null, 'Two connections need review before the next run.'); addToast('warning', 'Heads up', 'The schedule is disabled, so this integration will not run.'); addToast('error', 'Could not save', 'Validation failed or access denied.'); })()`);

try {
  // ============ A. The markup as production renders it: placement + interactivity ============
  for (const [w, h, expectW, label] of [[1440, 900, 672, 'desktop 1440'], [1920, 1080, 672, 'wide 1920'], [1024, 768, 512, 'tablet 1024']]) {
    await load('main', w, h, { keep: true });
    check(`${label}: production markup has the stack the script attaches to`, (await count('.ui-toast-stack')) === 1 && (await count('.ui-toast-slot')) === 4 && (await js('typeof window.appertureToast')) === 'object');
    await sleep(400);
    const t = await rect('.ui-toast', 3);
    check(`${label}: centred, not flush against either edge`, near(t.cx, w / 2, 2) && t.x > 40 && w - t.r > 40, `x=${t.x} right=${t.r} cx=${t.cx}`);
    check(`${label}: width ${expectW}, bottom-anchored with a margin`, near(t.w, expectW, 2) && t.b <= h - 8 && t.b >= h - 40, `w=${t.w} bottom=${t.b}/${h}`);
    check(`${label}: no horizontal page overflow`, (await js('document.documentElement.scrollWidth - innerWidth')) <= 0);
    const pe = await js(`(() => ({ layer: getComputedStyle(document.querySelector('.ui-toast-layer')).pointerEvents, slot: getComputedStyle(document.querySelector('.ui-toast-slot')).pointerEvents, card: getComputedStyle(document.querySelector('.ui-toast')).pointerEvents }))()`);
    check(`${label}: layer is click-through but slot and card are interactive`, pe.layer === 'none' && pe.slot === 'auto' && pe.card === 'auto', JSON.stringify(pe));
  }
  await load('main', 1440, 900, { keep: true }); await sleep(400);
  {
    for (let i = 0; i < 4; i++) check(`real toast #${i + 1}: card centre and close button are hit-testable`, (await hitIsInside('.ui-toast', i)) && (await hitIsInside('.ui-toast__dismiss', i)));
    await js(`window.__pd = 0; document.querySelectorAll('.ui-toast').forEach(e => e.addEventListener('pointerdown', () => window.__pd++))`);
    const c = await rect('.ui-toast', 3); await move(c.cx, c.cy); await mouse('mousePressed', c.cx, c.cy);
    check('pointerdown reaches the toast', (await js('window.__pd')) === 1);
    await mouse('mouseMoved', c.cx + 60, c.cy + 2); await sleep(40);
    const s = await dragState(3);
    check('horizontal drag changes the card transform and opacity', s.dragging && parseFloat(s.dx) > 40 && s.translate !== '0px' && parseFloat(s.op) < 1, JSON.stringify(s));
    await mouse('mouseReleased', c.cx + 60, c.cy + 2); await sleep(450);
    const b = await dragState(3);
    check('short drag snaps back and nothing was dismissed', !b.dragging && b.dx === '0px' && b.op === '1' && (await js('window.swipes.length')) === 0 && (await count('.ui-toast')) === 4, JSON.stringify(b));
    const c2 = await rect('.ui-toast', 3);
    await drag({ x: c2.cx, y: c2.cy }, { x: c2.cx + 320, y: c2.cy }, 12); await mouse('mouseReleased', c2.cx + 320, c2.cy); await sleep(150);
    check('threshold drag dismisses (direction right)', (await js('window.swipes')).length === 1 && (await js('window.swipes'))[0][1] === 'right');
    await sleep(500);
    check('and the dismissed toast is removed', (await count('.ui-toast')) === 3);
    const closeId = await js(`document.querySelectorAll('.ui-toast')[0].dataset.toastId`);
    await click('.ui-toast__dismiss', 0);
    await sleep(120);
    check('clicking the real close button dismisses that toast', (await js('window.dismissals')).includes(closeId) && (await js(`document.querySelector('.ui-toast-slot').dataset.phase`)) === 'exiting');
    await sleep(500);
    check('and it is removed', (await count('.ui-toast')) === 2);
    await shot('desktop-real-markup');
  }

  // ============ A2. Leaving toasts, real-mouse focus, click-through ============
  await load('main', 1440, 900); await addAll(); await sleep(500);
  {
    const savable = await js(`(() => { const b = document.getElementById('save').getBoundingClientRect(); const e = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2); return !!e && e.id === 'save'; })()`);
    check('page controls under the viewport-wide (empty) stack area stay clickable', savable === true);

    // Leaving toasts must not intercept input while they fade/collapse (~380 ms).
    await click('.ui-toast__dismiss', 0); await sleep(100);
    const ghost = await js(`(() => { const s = document.querySelector('.ui-toast-slot[data-phase="exiting"]'); if (!s) return null; const c = s.querySelector('.ui-toast'); const r = c.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return { pe: getComputedStyle(c).pointerEvents, hit: !!e && s.contains(e) }; })()`);
    check('a leaving toast is click-through (pointer-events none, not hit-testable)', ghost && ghost.pe === 'none' && ghost.hit === false, JSON.stringify(ghost));
    await sleep(450);

    // A real mouse click on X focuses the button (Chrome) - that must not leave a Focus hold behind on a neighbour.
    await js('window.holds.length = 0');
    await click('.ui-toast__dismiss', 1); await sleep(700);
    await move(60, 300); await sleep(300);
    const last = {}; (await js('window.holds')).forEach((h) => { last[h[0]] = h[1]; });
    const inStack = await js(`!!document.activeElement && !!document.activeElement.closest && !!document.activeElement.closest('.ui-toast-stack')`);
    check('after a mouse dismiss no Focus/Pointer/Drag/Scroll hold is left pausing the remaining toasts',
      !last.Focus && !last.Pointer && !last.Drag && !last.Scroll && inStack === false, JSON.stringify(last) + ' focusInStack=' + inStack);
  }

  // ============ B. Narrow layouts and nav-rail clearance (main shell) vs sign-in shell ============
  await load('main', 390, 844, { mobile: true, touch: true, keep: true }); await sleep(400);
  {
    const t = await rect('.ui-toast', 3), rail = await rect('#rail');
    check('390 main shell: stack starts beside the nav rail (its bottom control is not covered)', t.x >= rail.r && near(t.r, 382, 2), `x=${t.x} rail=${rail.r} right=${t.r}`);
    check('390 main shell: rail control still hit-testable through the stack area', await js(`(() => { const b = document.getElementById('rail-config').getBoundingClientRect(); const e = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2); return e && e.id === 'rail-config'; })()`));
    check('390: no horizontal overflow', (await js('document.documentElement.scrollWidth - innerWidth')) <= 0);
  }
  if (pages.auth) {
    await load('auth', 390, 844, { mobile: true, touch: true, keep: true }); await sleep(400);
    const t = await rect('.ui-toast', 3);
    check('390 sign-in shell (no rail): near full width with small margins', near(t.x, 8, 2) && near(t.w, 374, 2), `x=${t.x} w=${t.w}`);
  }

  // ============ C. Stack behaviour with more toasts (clones of real slots) ============
  await load('main', 1440, 900);
  await addAll(); await sleep(500);
  {
    const ys = []; for (let i = 0; i < 4; i++) ys.push((await rect('.ui-toast', i)).y);
    check('several toasts: oldest at the top, newest nearest the bottom', ys.every((y, i) => i === 0 || y > ys[i - 1]), ys.map(Math.round).join('<'));

    // Geometric proof the newest toast's shadow has room before its own clipping wrapper, and that nothing
    // further out (the scroll container) clips that room away — without depending on pixel/screenshot sampling.
    const geo = await js(`(() => {
      const slots = document.querySelectorAll('.ui-toast-slot');
      const last = slots[slots.length - 1], earlier = slots[slots.length - 2];
      const room = (slot) => { const inner = slot.querySelector('.ui-toast-slot__inner'); const card = slot.querySelector('.ui-toast');
        return inner.getBoundingClientRect().height - card.getBoundingClientRect().height; };
      const innerRect = last.querySelector('.ui-toast-slot__inner').getBoundingClientRect();
      const stackRect = document.querySelector('.ui-toast-stack').getBoundingClientRect();
      return { lastRoom: room(last), earlierRoom: room(earlier), innerBottom: innerRect.bottom, stackBottom: stackRect.bottom };
    })()`);
    // Every card's own margin-bottom (the inter-card gap) counts toward "room" too, so compare the newest
    // toast's room against an earlier one's rather than against zero.
    check('the newest toast has meaningfully more room below it (its shadow) than an earlier one (just the card gap)',
      geo.lastRoom - geo.earlierRoom >= 15, JSON.stringify(geo));
    check("that room is not itself clipped away by the scroll container's own edge",
      geo.stackBottom >= geo.innerBottom - 1, JSON.stringify(geo));

    await js(`for (let i = 0; i < 16; i++) addToast('info', 'Import ' + i, 'Batch ' + i + ' completed with 3 warnings that need review.')`); await sleep(700);
    const s = await js(`(() => { const s = document.querySelector('.ui-toast-stack'); return { h: s.getBoundingClientRect().height, sh: s.scrollHeight, ch: s.clientHeight, top: s.scrollTop, max: s.scrollHeight - s.clientHeight, ft: s.hasAttribute('data-fade-top'), fb: s.hasAttribute('data-fade-bottom') }; })()`);
    check('overflow: capped at 36vh, scrollable, pinned to the newest, top fade only', s.h <= 900 * 0.36 + 1 && s.sh > s.ch + 50 && near(s.top, s.max, 2) && s.ft && !s.fb, JSON.stringify(s));
    // The fade is a mask on the scroller, not an overlay: cards under it must still receive input.
    const under = await js(`(() => { const st = document.querySelector('.ui-toast-stack'); const top = st.getBoundingClientRect().top;
      const e = document.elementFromPoint(window.innerWidth / 2, top + 6); const card = e && e.closest('.ui-toast');
      return { fade: st.hasAttribute('data-fade-top'), card: !!card, tag: e ? e.tagName + '.' + e.className : 'none' }; })()`);
    check('cards under the top fade mask are still hit-testable (the mask does not intercept input)', under.fade && under.card, JSON.stringify(under));
    const c = await rect('.ui-toast', 19); await move(c.cx, c.cy);
    await send('Input.dispatchMouseEvent', { type: 'mouseWheel', x: c.cx, y: c.cy, deltaX: 0, deltaY: -250 }); await sleep(300);
    check('wheel over a toast scrolls the stack', (await js(`document.querySelector('.ui-toast-stack').scrollTop`)) < s.top - 100);
    await shot('desktop-overflow');
  }

  // ============ D. Swipe intent, holds, focus, lost release ============
  await load('main', 1440, 900);
  await js(`addToast('info', null, 'Vertical drag must not swipe.')`); await sleep(450);
  {
    const c = await rect('.ui-toast');
    await drag({ x: c.cx, y: c.cy }, { x: c.cx + 6, y: c.cy - 70 }); const v = (await dragState()).dragging; await mouse('mouseReleased', c.cx + 6, c.cy - 70);
    await drag({ x: c.cx, y: c.cy }, { x: c.cx + 60, y: c.cy + 60 }); const d = (await dragState()).dragging; await mouse('mouseReleased', c.cx + 60, c.cy + 60);
    await drag({ x: c.cx, y: c.cy }, { x: c.cx + 60, y: c.cy + 15 }); const h = (await dragState()).dragging; await mouse('mouseReleased', c.cx + 60, c.cy + 15);
    check('vertical and 45-degree drags are not swipes; a shallow diagonal is', v === false && d === false && h === true && (await js('window.swipes.length')) === 0);
    await sleep(450);
    await drag({ x: c.cx, y: c.cy }, { x: c.cx + 40, y: c.cy }, 6);
    await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: c.cx + 50, y: c.cy, buttons: 0 }); await sleep(400);
    const l = await dragState();
    check('a lost mouse release ends the drag and snaps back', !l.dragging && l.dx === '0px', JSON.stringify(l));
    await mouse('mouseReleased', c.cx + 50, c.cy);
  }
  await load('main', 1440, 900);
  await js(`addToast('success', null, 'Hover me.'); addToast('error', null, 'Focus my button.')`); await sleep(450);
  {
    const c = await rect('.ui-toast', 0); await move(c.cx, c.cy); await sleep(150);
    const held = (await js('window.holds')).filter((h) => h[0] === 'Pointer');
    check('mouse hover reports a Pointer hold', held.at(-1)?.[1] === true);
    await move(100, 100); await sleep(150);
    check('leaving releases it', (await js('window.holds')).filter((h) => h[0] === 'Pointer').at(-1)[1] === false);
    await js(`document.querySelectorAll('.ui-toast__dismiss')[1].focus()`); await sleep(80);
    check('focus inside reports a Focus hold', (await js('window.holds')).filter((h) => h[0] === 'Focus').at(-1)?.[1] === true);
    await js(`document.activeElement.blur()`); await sleep(80);
    check('blur releases it', (await js('window.holds')).filter((h) => h[0] === 'Focus').at(-1)[1] === false);
    const before = await js('document.activeElement.tagName'); await js(`addToast('info', null, 'Must not steal focus.')`); await sleep(300);
    check('a new toast does not take focus', (await js('document.activeElement.tagName')) === before);
    await js(`document.querySelectorAll('.ui-toast__dismiss')[1].focus(); document.querySelectorAll('.ui-toast__dismiss')[1].click()`); await sleep(60);
    check('dismissing the focused toast keeps focus on another close button', await js(`document.activeElement.classList.contains('ui-toast__dismiss')`));
    await sleep(500);
    const c9 = await rect('.ui-toast', 0); await move(c9.cx, c9.cy); await sleep(100);
    await js(`document.querySelector('.ui-toast-stack').innerHTML = ''`); await sleep(200);
    check('emptying the stack releases a stuck Pointer hold', (await js('window.holds')).filter((h) => h[0] === 'Pointer').at(-1)[1] === false);
  }

  // ============ E. Motion, touch, contrast, cleanup ============
  await load('main', 1440, 900, { reduced: true });
  await js(`addToast('success', null, 'Reduced motion.')`); await sleep(50);
  {
    const st = await js(`(() => { const e = document.querySelector('.ui-toast'); return { a: getComputedStyle(e).animationName, sa: getComputedStyle(document.querySelector('.ui-toast-slot')).animationName, op: getComputedStyle(e).opacity }; })()`);
    check('reduced motion: no enter animation, full opacity at once', st.a === 'none' && st.sa === 'none' && st.op === '1', JSON.stringify(st));
    await js(`document.querySelector('.ui-toast__dismiss').click()`); await sleep(30);
    check('reduced motion: a leaving toast disappears at once', (await js(`getComputedStyle(document.querySelector('.ui-toast-slot')).display`)) === 'none');
    await sleep(350); await js(`addToast('info', null, 'Drag still works.')`); await sleep(80);
    const r = await rect('.ui-toast'); await drag({ x: r.cx, y: r.cy }, { x: r.cx + 300, y: r.cy }); await mouse('mouseReleased', r.cx + 300, r.cy); await sleep(100);
    check('reduced motion: swipe dismissal still works', (await js('window.swipes.length')) === 1);
  }
  await load('main', 390, 844, { mobile: true, touch: true }); await js(`addToast('info', null, 'Swipe me on touch.')`); await sleep(450);
  {
    const c = await rect('.ui-toast'); await dragTouch({ x: c.cx, y: c.cy }, { x: c.cx + 200, y: c.cy + 4 }, 10); await sleep(150);
    check('touch: horizontal swipe past the threshold dismisses', (await js('window.swipes')).length === 1);
    const coarse = await js(`matchMedia('(pointer: coarse)').matches`);
    if (coarse) {
      await js(`addToast('info', null, 'Coarse pointer sizing.')`); await sleep(300);
      const x = await rect('.ui-toast__dismiss');
      check('coarse pointer: the close button is a 44 px target', x.w >= 43.5 && x.h >= 43.5, `${x.w}x${x.h}`);
    } else console.log('SKIP  coarse-pointer sizing (emulated pointer media unavailable)');
  }
  await load('main', 390, 844, { mobile: true, touch: true });
  await js(`for (let i = 0; i < 14; i++) addToast('info', 'Import ' + i, 'Batch ' + i + ' completed with warnings.')`); await sleep(700);
  {
    const before = await js('document.querySelector(".ui-toast-stack").scrollTop'); const c = await rect('.ui-toast', 13);
    await dragTouch({ x: c.cx, y: c.cy }, { x: c.cx + 3, y: c.cy + 150 }, 12); await sleep(700);
    const after = await js('document.querySelector(".ui-toast-stack").scrollTop');
    check('touch: a vertical drag scrolls the stack and never dismisses', after < before - 20 && (await js('window.swipes.length')) === 0, `scrollTop ${before} -> ${after}`);
  }
  for (const dark of [false, true]) {
    await load('main', 1440, 900, { dark }); await addAll(); await sleep(500);
    const rows = await js(`(() => {
      const cv = document.createElement('canvas'); cv.width = cv.height = 1; const g = cv.getContext('2d', { willReadFrequently: true });
      const rgba = (c) => { g.clearRect(0,0,1,1); g.fillStyle = '#000'; g.fillStyle = c; g.fillRect(0,0,1,1); const d = g.getImageData(0,0,1,1).data; return [d[0], d[1], d[2], d[3] / 255]; };
      const over = (fg, bg) => [0,1,2].map(i => fg[i] * fg[3] + bg[i] * (1 - fg[3]));
      const lum = (c) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]); };
      const ratio = (a, b) => { const l1 = lum(a), l2 = lum(b); return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05); };
      const page = rgba(getComputedStyle(document.body).backgroundColor); const out = [];
      document.querySelectorAll('.ui-toast').forEach((t) => { const bg = over(rgba(getComputedStyle(t).backgroundColor), page); const col = (el) => over(rgba(getComputedStyle(el).color), bg);
        out.push({ sev: t.dataset.severity, body: ratio(col(t.querySelector('.ui-toast__body > div:last-child')), bg), close: ratio(col(t.querySelector('.ui-toast__dismiss')), bg), icon: ratio(col(t.querySelector('.ui-toast__icon')), bg) }); });
      return out; })()`);
    for (const r of rows) check(`${dark ? 'dark' : 'light'} ${r.sev}: text >= 4.5:1, close and severity icon >= 3:1`, r.body >= 4.5 && r.close >= 3 && r.icon >= 3, `${r.body.toFixed(1)} / ${r.close.toFixed(1)} / ${r.icon.toFixed(1)}`);
    await shot(dark ? 'desktop-dark' : 'desktop-light');
  }
  await load('main', 1440, 900); await js(`addToast('info', null, 'After detach nothing reacts.')`); await sleep(400);
  await js('detachStack(); window.holds.length = 0');
  {
    const c = await rect('.ui-toast'); await drag({ x: c.cx, y: c.cy }, { x: c.cx + 300, y: c.cy }); await mouse('mouseReleased', c.cx + 300, c.cy); await sleep(100);
    check('after detach no gesture, hold or swipe is handled', (await js('window.swipes.length')) === 0 && (await js('window.holds.length')) === 0);
  }
  check('no uncaught page errors', pageErrors.length === 0, pageErrors.slice(0, 2).join(' | '));
} catch (e) { console.error('HARNESS ERROR', e); results.push({ name: 'harness', ok: false }); }
finally {
  const failed = results.filter((r) => !r.ok).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  try { ws.close(); } catch { /* ignore */ }
  chrome.kill();
  process.exit(failed ? 1 : 0);
}
