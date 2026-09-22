// Toast stack behaviour (presentation only). Attached once per ToastHost by appertureToast.attach.
//
//  - Horizontal swipe-to-dismiss with mouse or touch (Pointer Events), with vertical-intent detection so
//    touch users can still scroll the stack.
//  - Reports "holds" (pointer over the stack, focus inside, drag, scroll, tab hidden) to .NET so the
//    dismissal countdowns pause while the operator is interacting. State is recomputed from the DOM on
//    every change, so a missed pointer-leave / blur cannot leave a hold stuck.
//  - Keeps the newest toast in view, fades the edges when older toasts are clipped, and keeps keyboard
//    focus in the stack when a focused toast is dismissed.
//
// All drag visuals are written as CSS custom properties (--toast-dx, --toast-opacity) and data-*
// attributes on elements Blazor never renders those from, so a Blazor re-render cannot fight the gesture.
// Timing, queueing and removal live in ToastService (C#); this file owns no toast state.
(function () {
    'use strict';

    // ---- Tunables (gesture) -------------------------------------------------------------------
    var SLOP_PX = 8;                // movement before the gesture's intent is decided
    var INTENT_RATIO = 1.2;         // horizontal must beat vertical by this factor to be a swipe
    var THRESHOLD_FRACTION = 0.3;   // dismiss distance as a fraction of the toast's width...
    var THRESHOLD_MIN_PX = 72;      // ...bounded so narrow and very wide toasts both feel right
    var THRESHOLD_MAX_PX = 220;
    var FADE_DEADZONE = 0.12;       // fraction of the threshold that moves before fading begins
    var FADE_SPAN = 1.25;           // opacity reaches 0 at this multiple of the threshold
    var MOMENTUM_MS = 250;          // scroll hold lingers this long after the last scroll event
    var NEAR_BOTTOM_PX = 48;

    function clamp(n, lo, hi) { return Math.min(hi, Math.max(lo, n)); }

    // ---- Pure gesture math (also exported for node tests) -------------------------------------

    /** Horizontal distance at which releasing dismisses the toast. */
    function dismissThreshold(widthPx) {
        return clamp(widthPx * THRESHOLD_FRACTION, THRESHOLD_MIN_PX, THRESHOLD_MAX_PX);
    }

    /** 'pending' until the pointer moves past the slop, then 'horizontal' (a swipe) or 'vertical' (leave it to scrolling). */
    function resolveIntent(dx, dy) {
        var ax = Math.abs(dx);
        var ay = Math.abs(dy);
        if (Math.max(ax, ay) < SLOP_PX) return 'pending';
        return ax > ay * INTENT_RATIO ? 'horizontal' : 'vertical';
    }

    /** 1 at rest, easing to 0 with distance; unchanged inside a small dead zone so tiny drags stay readable. Always in [0, 1]. */
    function opacityFor(dx, threshold) {
        var dead = FADE_DEADZONE * threshold;
        var span = FADE_SPAN * threshold - dead;
        return 1 - clamp((Math.abs(dx) - dead) / span, 0, 1);
    }

    function shouldDismiss(dx, threshold) { return Math.abs(dx) >= threshold; }

    var registry = new WeakMap();

    function reducedMotion() {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    function enterDurationMs(stack) {
        try {
            var raw = getComputedStyle(stack).getPropertyValue('--ui-toast-enter-duration').trim();
            var n = parseFloat(raw);
            if (isFinite(n)) return /ms$/.test(raw) ? n : n * 1000;
        } catch (e) { /* fall through */ }
        return 220;
    }

    // ---- Attach -------------------------------------------------------------------------------

    function attach(stack, dotNet) {
        if (!stack) return;
        detach(stack);

        var ctx = {
            stack: stack,
            dotNet: dotNet,
            drag: null,
            dragging: false,
            hover: false,
            touching: false,
            touchIds: {},
            momentum: false,
            momentumTimer: 0,
            lastPointerType: 'mouse',
            atBottom: true,
            returnFocus: null,
            followUntil: 0,
            ignoreScrollUntil: 0,
            stopDrag: null,
            followFrame: 0,
            fadeFrame: 0,
            dragFrame: 0,
            syncTimer: 0,
            reported: {},
            listeners: [],
            observer: null
        };
        registry.set(stack, ctx);

        function on(target, type, handler, options) {
            target.addEventListener(type, handler, options);
            ctx.listeners.push({ target: target, type: type, handler: handler, options: options });
        }

        function cards() { return stack.querySelectorAll('.ui-toast-slot:not([data-phase="exiting"])'); }

        // ---- Holds -------------------------------------------------------------------------
        function computeHolds() {
            var active = document.activeElement;
            return {
                Pointer: (ctx.hover && ctx.lastPointerType !== 'touch') || ctx.touching,
                Focus: !!(active && active !== document.body && stack.contains(active)),
                Drag: ctx.dragging,
                Scroll: ctx.momentum,
                Hidden: document.hidden === true
            };
        }

        function sync() {
            if (!registry.has(stack)) return;
            if (stack.querySelector('.ui-toast-slot') === null) {
                // Nothing left to interact with: any recorded interaction is stale.
                ctx.hover = false; ctx.touching = false; ctx.touchIds = {}; ctx.momentum = false; ctx.dragging = false;
            } else {
                try { ctx.hover = stack.matches(':hover'); } catch (e) { /* keep last */ }
            }
            var next = computeHolds();
            Object.keys(next).forEach(function (key) {
                // .NET starts with every hold released, so "never reported" means false.
                if ((ctx.reported[key] || false) === next[key]) return;
                ctx.reported[key] = next[key];
                if (ctx.dotNet) {
                    try {
                        var p = ctx.dotNet.invokeMethodAsync('SetHold', key, next[key]);
                        if (p && p.catch) p.catch(function () { /* host disposed */ });
                    } catch (e) { /* host disposed */ }
                }
            });
        }

        function syncSoon() {
            clearTimeout(ctx.syncTimer);
            ctx.syncTimer = setTimeout(sync, 0);
        }

        function isHeld() {
            var h = computeHolds();
            return h.Pointer || h.Focus || h.Drag || h.Scroll;
        }

        // ---- Scroll affordances ------------------------------------------------------------
        function updateFades() {
            var max = stack.scrollHeight - stack.clientHeight;
            var top = stack.scrollTop > 1;
            var bottom = max - stack.scrollTop > 1;
            ctx.atBottom = max - stack.scrollTop <= NEAR_BOTTOM_PX;
            if (top) stack.setAttribute('data-fade-top', ''); else stack.removeAttribute('data-fade-top');
            if (bottom) stack.setAttribute('data-fade-bottom', ''); else stack.removeAttribute('data-fade-bottom');
        }

        function scheduleFades() {
            if (ctx.fadeFrame) return;
            ctx.fadeFrame = requestAnimationFrame(function () { ctx.fadeFrame = 0; updateFades(); });
        }

        // Scroll events are async, so programmatic scrolls are recognised by time, not by a flag.
        function toEnd() {
            ctx.ignoreScrollUntil = performance.now() + 100;
            stack.scrollTop = stack.scrollHeight;
        }

        // While a new slot grows, keep pinning to the bottom so the newest toast never opens off-screen.
        function followBottom() {
            if (ctx.followFrame) return;
            var step = function () {
                ctx.followFrame = 0;
                toEnd();
                if (performance.now() < ctx.followUntil) ctx.followFrame = requestAnimationFrame(step);
                else updateFades();
            };
            ctx.followFrame = requestAnimationFrame(step);
        }

        function revealNewest() {
            if (!(ctx.atBottom || !isHeld())) return;
            if (reducedMotion()) {
                requestAnimationFrame(function () { toEnd(); updateFades(); });
                return;
            }
            ctx.followUntil = performance.now() + enterDurationMs(stack) + 60;
            followBottom();
        }

        // ---- Swipe -------------------------------------------------------------------------
        function endDragTracking() {
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
            window.removeEventListener('pointercancel', onPointerCancel);
            if (ctx.dragFrame) { cancelAnimationFrame(ctx.dragFrame); ctx.dragFrame = 0; }
            ctx.drag = null;
            ctx.dragging = false;
        }

        ctx.stopDrag = endDragTracking;

        function applyDrag(drag, dx) {
            drag.card.style.setProperty('--toast-dx', dx + 'px');
            drag.card.style.setProperty('--toast-opacity', String(opacityFor(dx, drag.threshold)));
        }

        function snapBack(card) {
            card.removeAttribute('data-dragging');
            card.style.setProperty('--toast-dx', '0px');
            card.style.setProperty('--toast-opacity', '1');
        }

        function beginTouch(e) {
            ctx.touchIds[e.pointerId] = true;
            ctx.touching = true;
        }

        // A second finger keeps the hold until it too lifts.
        function endTouch(e) {
            if (e.pointerType !== 'touch') return;
            delete ctx.touchIds[e.pointerId];
            ctx.touching = Object.keys(ctx.touchIds).length > 0;
        }

        function onPointerDown(e) {
            var card = e.target.closest && e.target.closest('.ui-toast');
            if (!card || !stack.contains(card)) return;

            ctx.lastPointerType = e.pointerType || 'mouse';
            if (e.pointerType === 'touch') { beginTouch(e); sync(); }

            if (!e.isPrimary) return;
            if (e.pointerType === 'mouse' && e.button !== 0) return;
            if (e.target.closest('.ui-toast__dismiss')) return;
            if (card.closest('[data-phase="exiting"]')) return;
            if (ctx.drag) endDragTracking();

            ctx.drag = {
                card: card,
                id: card.getAttribute('data-toast-id'),
                pointerId: e.pointerId,
                x0: e.clientX,
                y0: e.clientY,
                dx: 0,
                state: 'pending',
                threshold: dismissThreshold(card.getBoundingClientRect().width)
            };
            window.addEventListener('pointermove', onPointerMove, { passive: true });
            window.addEventListener('pointerup', onPointerUp);
            window.addEventListener('pointercancel', onPointerCancel);
        }

        function onPointerMove(e) {
            var drag = ctx.drag;
            if (!drag || e.pointerId !== drag.pointerId) return;

            // The button was released somewhere we never heard about (outside the window, after alt-tab).
            if (e.pointerType === 'mouse' && e.buttons === 0) { releaseDrag(false); return; }

            var dx = e.clientX - drag.x0;
            var dy = e.clientY - drag.y0;

            if (drag.state === 'pending') {
                var intent = resolveIntent(dx, dy);
                if (intent === 'pending') return;
                if (intent === 'vertical') { endDragTracking(); return; } // leave it to scrolling
                drag.state = 'dragging';
                ctx.dragging = true;
                drag.card.setAttribute('data-dragging', '');
                try { drag.card.setPointerCapture(e.pointerId); } catch (err) { /* pointer gone */ }
                try { var sel = window.getSelection && window.getSelection(); if (sel) sel.removeAllRanges(); } catch (err) { /* ignore */ }
                sync();
            }

            drag.dx = dx;
            if (!ctx.dragFrame) {
                ctx.dragFrame = requestAnimationFrame(function () {
                    ctx.dragFrame = 0;
                    if (ctx.drag === drag) applyDrag(drag, drag.dx);
                });
            }
        }

        function releaseDrag(dismissAllowed) {
            var drag = ctx.drag;
            if (!drag) return;
            var wasDragging = drag.state === 'dragging';
            var dx = drag.dx;
            try { drag.card.releasePointerCapture(drag.pointerId); } catch (err) { /* already released */ }
            endDragTracking();
            if (!wasDragging) { sync(); return; }

            if (dismissAllowed && shouldDismiss(dx, drag.threshold)) {
                applyDrag(drag, dx);
                drag.card.removeAttribute('data-dragging');
                var direction = dx < 0 ? 'left' : 'right';
                if (ctx.dotNet) {
                    try {
                        var p = ctx.dotNet.invokeMethodAsync('Swipe', drag.id, direction);
                        if (p && p.catch) p.catch(function () { snapBack(drag.card); });
                    } catch (err) { snapBack(drag.card); }
                }
            } else {
                snapBack(drag.card);
            }
            sync();
        }

        function onPointerUp(e) {
            endTouch(e);
            if (ctx.drag && e.pointerId === ctx.drag.pointerId) releaseDrag(true);
            else sync();
        }

        function onPointerCancel(e) {
            endTouch(e);
            if (ctx.drag && e.pointerId === ctx.drag.pointerId) releaseDrag(false);
            else sync();
        }

        // Capture lost without a pointerup (element removed, browser took over): never leave the card mid-drag.
        // Only the card losing its own capture counts: moving implicit touch capture onto the card also
        // reports a loss, on the previous target.
        function onLostPointerCapture(e) {
            var drag = ctx.drag;
            if (drag && drag.state === 'dragging' && e.pointerId === drag.pointerId && e.target === drag.card) releaseDrag(false);
        }

        // A touch that never becomes a swipe (tap or vertical scroll) still has to release its hold.
        function onStackPointerEnd(e) {
            if (e.pointerType === 'touch') { endTouch(e); syncSoon(); }
        }

        // ---- Hover / focus / scroll / tab visibility ---------------------------------------
        function onPointerOver(e) { ctx.lastPointerType = e.pointerType || 'mouse'; syncSoon(); }
        function onPointerLeave() { ctx.hover = false; syncSoon(); }

        function onFocusIn(e) {
            if (!stack.contains(e.relatedTarget)) ctx.returnFocus = e.relatedTarget || null;
            sync();
        }
        function onFocusOut() { syncSoon(); }

        function onScroll() {
            scheduleFades();
            if (performance.now() < ctx.ignoreScrollUntil) return; // our own follow-the-newest scroll
            ctx.momentum = true;
            clearTimeout(ctx.momentumTimer);
            ctx.momentumTimer = setTimeout(function () { ctx.momentum = false; sync(); }, MOMENTUM_MS);
            sync();
        }

        // Dismissing the toast that has focus must not drop keyboard focus onto <body>. Keyboard only:
        // a keyboard-generated click has detail 0, whereas a mouse or finger click has detail >= 1 (and
        // Chrome focuses a button on mousedown, so activeElement alone cannot tell them apart). Moving
        // focus after a pointer click would leave a Focus hold on a neighbour and pause every countdown.
        function onClickCapture(e) {
            var button = e.target.closest && e.target.closest('.ui-toast__dismiss');
            if (!button || e.detail !== 0 || document.activeElement !== button) return;
            var slot = button.closest('.ui-toast-slot');
            if (!slot) return;

            var next = slot.nextElementSibling;
            var prev = slot.previousElementSibling;
            var neighbour = null;
            [next, prev].some(function (candidate) {
                if (candidate && candidate.getAttribute('data-phase') !== 'exiting') {
                    neighbour = candidate.querySelector('.ui-toast__dismiss');
                }
                return !!neighbour;
            });

            var target = neighbour;
            if (!target && ctx.returnFocus && document.contains(ctx.returnFocus)) target = ctx.returnFocus;
            if (target && typeof target.focus === 'function') {
                try { target.focus(); } catch (err) { /* ignore */ }
            }
        }

        // Pressing on already-selected text would start a native drag-and-drop, which cancels pointer events and the swipe.
        on(stack, 'dragstart', function (e) { e.preventDefault(); });
        on(stack, 'pointerdown', onPointerDown);
        on(stack, 'pointerup', onStackPointerEnd);
        on(stack, 'pointercancel', onStackPointerEnd);
        on(stack, 'lostpointercapture', onLostPointerCapture);
        on(stack, 'pointerover', onPointerOver);
        on(stack, 'pointerleave', onPointerLeave);
        on(stack, 'focusin', onFocusIn);
        on(stack, 'focusout', onFocusOut);
        on(stack, 'scroll', onScroll, { passive: true });
        on(stack, 'click', onClickCapture, true);
        on(stack, 'animationend', scheduleFades);
        on(stack, 'transitionend', scheduleFades);
        on(document, 'visibilitychange', sync);
        on(window, 'resize', scheduleFades);

        if (typeof MutationObserver === 'function') {
            ctx.observer = new MutationObserver(function (records) {
                var added = records.some(function (r) {
                    return Array.prototype.some.call(r.addedNodes, function (n) {
                        return n.nodeType === 1 && n.classList.contains('ui-toast-slot');
                    });
                });
                if (added) {
                    // .NET drops interaction holds when the stack empties; re-assert whatever is true now.
                    ['Pointer', 'Focus', 'Drag', 'Scroll'].forEach(function (k) { delete ctx.reported[k]; });
                    revealNewest();
                }
                scheduleFades();
                syncSoon();
            });
            ctx.observer.observe(stack, { childList: true });
        }

        updateFades();
        if (cards().length) revealNewest();
        sync();
    }

    // ---- Detach -------------------------------------------------------------------------------

    function detach(stack) {
        var ctx = stack && registry.get(stack);
        if (!ctx) return;
        registry.delete(stack);

        ctx.listeners.forEach(function (l) { l.target.removeEventListener(l.type, l.handler, l.options); });
        ctx.listeners = [];
        if (ctx.observer) { ctx.observer.disconnect(); ctx.observer = null; }
        clearTimeout(ctx.momentumTimer);
        clearTimeout(ctx.syncTimer);
        if (ctx.followFrame) cancelAnimationFrame(ctx.followFrame);
        if (ctx.fadeFrame) cancelAnimationFrame(ctx.fadeFrame);
        if (ctx.dragFrame) cancelAnimationFrame(ctx.dragFrame);
        if (ctx.drag) {
            ctx.drag.card.removeAttribute('data-dragging');
            try { ctx.drag.card.releasePointerCapture(ctx.drag.pointerId); } catch (e) { /* already released */ }
        }
        // The window listeners exist only while a press is being tracked; drop any that are left.
        if (ctx.stopDrag) ctx.stopDrag();
        stack.removeAttribute('data-fade-top');
        stack.removeAttribute('data-fade-bottom');
        ctx.dotNet = null;
    }

    var api = {
        attach: attach,
        detach: detach,
        math: {
            dismissThreshold: dismissThreshold,
            resolveIntent: resolveIntent,
            opacityFor: opacityFor,
            shouldDismiss: shouldDismiss
        }
    };

    if (typeof window !== 'undefined') window.appertureToast = api;
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
})();
