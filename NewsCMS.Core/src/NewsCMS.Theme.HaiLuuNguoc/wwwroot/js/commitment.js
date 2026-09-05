(function () {
    'use strict';

    var endpoints = window.PLEDGE_ENDPOINTS || {};
    var form = document.getElementById('pledge-form');
    var card = document.querySelector('[data-pledge-card]');
    if (!form || !card) return;

    /* â”€â”€ Signature pad (draw) â”€â”€ */
    var canvas = document.getElementById('pledge-pad');
    var pad = null;
    if (canvas && window.SignaturePad) {
        pad = new SignaturePad(canvas, {
            backgroundColor: 'rgba(255, 255, 255, 0)',
            penColor: '#01236b',
            minWidth: 1.4,
            maxWidth: 2.8
        });
        resizeCanvas();
        window.addEventListener('resize', resizeCanvas);
    }

    function resizeCanvas() {
        if (!canvas || !pad) return;
        var ratio = Math.max(window.devicePixelRatio || 1, 1);
        var rect = canvas.getBoundingClientRect();
        if (rect.width === 0) return;
        var data = pad.toData();
        canvas.width  = rect.width  * ratio;
        canvas.height = rect.height * ratio;
        canvas.getContext('2d').scale(ratio, ratio);
        pad.clear();
        if (data && data.length) pad.fromData(data);
    }

    var clearBtn = document.querySelector('[data-pledge-clear]');
    if (clearBtn && pad) {
        clearBtn.addEventListener('click', function () { pad.clear(); });
    }

    /* â”€â”€ Auto-signature canvas â”€â”€ */
    var autoCanvas = document.querySelector('[data-pledge-auto-canvas]');
    var displayNameInput = document.getElementById('pledge-display-name');
    var AUTO_FONT = "'Dancing Script', cursive";
    var AUTO_FONT_SIZE = 52;

    function renderAutoSignature(name) {
        if (!autoCanvas) return;
        var ratio = Math.max(window.devicePixelRatio || 1, 1);
        var w = autoCanvas.offsetWidth || 400;
        autoCanvas.width  = w * ratio;
        autoCanvas.height = 120 * ratio;
        var ctx = autoCanvas.getContext('2d');
        ctx.scale(ratio, ratio);
        ctx.clearRect(0, 0, w, 120);

        var text = name && name.trim() ? name.trim() : 'Chá»¯ kÃ½ cá»§a báº¡n';
        ctx.font = 'bold ' + AUTO_FONT_SIZE + 'px ' + AUTO_FONT;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        /* Gradient: ocean blue to teal */
        var grad = ctx.createLinearGradient(w * .2, 0, w * .8, 0);
        grad.addColorStop(0,   '#01236b');
        grad.addColorStop(.5,  '#0b5fa1');
        grad.addColorStop(1,   '#c6f3f9');
        ctx.fillStyle = grad;

        /* Trim font size if text too wide */
        var fs = AUTO_FONT_SIZE;
        ctx.font = 'bold ' + fs + 'px ' + AUTO_FONT;
        while (ctx.measureText(text).width > w * .88 && fs > 18) {
            fs -= 2;
            ctx.font = 'bold ' + fs + 'px ' + AUTO_FONT;
        }
        ctx.fillText(text, w / 2, 64);

        /* Underline flourish */
        var tw = Math.min(ctx.measureText(text).width + 20, w * .8);
        var x0 = (w - tw) / 2;
        ctx.beginPath();
        ctx.moveTo(x0, 88);
        ctx.bezierCurveTo(x0 + tw * .3, 82, x0 + tw * .7, 96, x0 + tw, 88);
        ctx.strokeStyle = '#c6f3f9';
        ctx.lineWidth = 1.5;
        ctx.stroke();
    }

    if (displayNameInput && autoCanvas) {
        displayNameInput.addEventListener('input', function () {
            if (currentTab === 'auto') renderAutoSignature(displayNameInput.value);
        });
    }

    /* â”€â”€ Tab switching â”€â”€ */
    var tabs    = document.querySelectorAll('[data-pledge-tab]');
    var panes   = document.querySelectorAll('[data-pledge-pane]');
    var kindInput = document.querySelector('[data-pledge-kind]');
    var currentTab = 'draw';

    tabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
            currentTab = tab.getAttribute('data-pledge-tab');
            tabs.forEach(function (t) {
                var active = t === tab;
                t.classList.toggle('is-active', active);
                t.setAttribute('aria-selected', active ? 'true' : 'false');
            });
            panes.forEach(function (p) {
                p.hidden = p.getAttribute('data-pledge-pane') !== currentTab;
            });
            if (kindInput) {
                kindInput.value = currentTab === 'upload' ? 'Uploaded' : 'Drawn';
            }
            if (currentTab === 'auto') {
                renderAutoSignature(displayNameInput ? displayNameInput.value : '');
            }
        });
    });

    /* â”€â”€ Upload preview â”€â”€ */
    var uploadInput   = document.querySelector('[data-pledge-upload-input]');
    var uploadPreview = document.querySelector('[data-pledge-upload-preview]');
    if (uploadInput) {
        uploadInput.addEventListener('change', function () {
            var file = uploadInput.files && uploadInput.files[0];
            if (!file || !uploadPreview) return;
            var img = uploadPreview.querySelector('img');
            if (!img) return;
            var reader = new FileReader();
            reader.onload = function (e) {
                img.src = e.target.result;
                uploadPreview.hidden = false;
            };
            reader.readAsDataURL(file);
        });
    }

    /* â”€â”€ Error helpers â”€â”€ */
    var errorEl   = document.querySelector('[data-pledge-error]');
    var submitBtn = document.querySelector('[data-pledge-submit]');
    var formWrap  = document.querySelector('[data-pledge-form-wrap]');
    var signedPanel = document.querySelector('[data-pledge-signed-panel]');

    function showError(msg) {
        if (!errorEl) return;
        errorEl.hidden = false;
        errorEl.textContent = msg;
    }
    function hideError() {
        if (!errorEl) return;
        errorEl.hidden = true;
        errorEl.textContent = '';
    }

    /* â”€â”€ Form submit â”€â”€ */
    var base64Input = document.querySelector('[data-pledge-base64]');

    form.addEventListener('submit', async function (ev) {
        ev.preventDefault();
        hideError();

        var kind = kindInput ? kindInput.value : 'Drawn';

        if (kind === 'Drawn') {
            if (currentTab === 'auto') {
                /* Render latest auto-signature to base64 */
                renderAutoSignature(displayNameInput ? displayNameInput.value : '');
                if (autoCanvas) {
                    if (base64Input) base64Input.value = autoCanvas.toDataURL('image/png');
                }
            } else {
                if (!pad || pad.isEmpty()) {
                    showError('Vui lÃ²ng váº½ chá»¯ kÃ½ trÆ°á»›c khi gá»­i.');
                    return;
                }
                if (base64Input) base64Input.value = pad.toDataURL('image/png');
            }
        } else if (kind === 'Uploaded') {
            if (!uploadInput || !uploadInput.files || !uploadInput.files[0]) {
                showError('Vui lÃ²ng chá»n áº£nh chá»¯ kÃ½.');
                return;
            }
        }

        var formData = new FormData(form);

        submitBtn.disabled = true;
        try {
            var res  = await fetch(endpoints.sign, { method: 'POST', body: formData, credentials: 'same-origin' });
            var data = await res.json();
            if (!res.ok || !data.ok) {
                showError(data.error || 'KhÃ´ng thá»ƒ gá»­i cam káº¿t. Vui lÃ²ng thá»­ láº¡i.');
                return;
            }

            if (formWrap)    formWrap.hidden    = true;
            if (signedPanel) signedPanel.hidden = false;
            card.setAttribute('data-has-signed', 'true');
            if (typeof data.totalCount === 'number') {
                animatePledgeCounter(data.totalCount, {
                    delta: Math.max(1, data.totalCount - pledgeCurrentTotal),
                    showDelta: true
                });
            }

            var nameVal = displayNameInput ? displayNameInput.value.trim() : '';
            if (data.signatureUrl && window.gsap) {
                showSignatureSpotlight(data.signatureUrl, nameVal);
                setTimeout(function () {
                    showThanks();
                    refreshWall(true);
                }, 4300);
            } else {
                showThanks();
                await refreshWall(true);
            }
        } catch (err) {
            showError('Lá»—i máº¡ng. Vui lÃ²ng thá»­ láº¡i.');
        } finally {
            submitBtn.disabled = false;
        }
    });

    /* â”€â”€ Wall â”€â”€ */
    var wallEl      = document.querySelector('[data-pledge-wall]');
    var wallControls = document.querySelector('.pledge-wall-controls');
    var wallViewBtns = document.querySelectorAll('[data-pledge-wall-view]');
    var activePill = wallControls ? wallControls.querySelector('.pledge-active-pill') : null;
    var activeHighlight = wallControls ? wallControls.querySelector('.pledge-active-highlight') : null;
    var activeControlPositions = {
        desktop: {
            flow: { pillX: 284, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 419, highlightY: 88 },
            jelly: { pillX: 578, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 713, highlightY: 88 },
            list: { pillX: 872, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 1007, highlightY: 88 }
        },
        mobile: {
            flow: { pillX: 284, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 419, highlightY: 88 },
            jelly: { pillX: 578, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 713, highlightY: 88 },
            list: { pillX: 872, pillY: 68, pillW: 270, pillH: 104, pillRx: 50, highlightX: 1007, highlightY: 88 }
        }
    };
    var renderedIds = new Set();
    (window.PLEDGE_INITIAL_WALL || []).forEach(function (item) { renderedIds.add(String(item.id)); });
    var counterEl = document.querySelector('[data-pledge-total-counter]');
    var counterValueEl = document.querySelector('[data-pledge-total-value]');
    var counterDeltaEl = document.querySelector('[data-pledge-total-delta]');
    var counterDeltaTimer = null;
    var counterAnimationFrame = null;
    var counterStepTimer = null;
    var pledgeCurrentTotal = parseCounterNumber(
        window.PLEDGE_INITIAL_TOTAL,
        parseCounterNumber(counterEl ? counterEl.getAttribute('data-value') : null, renderedIds.size)
    );
    var pledgeDisplayedTotal = 0;
    var counterOdometerSlots = [];

    function parseCounterNumber(value, fallback) {
        var n = parseInt(value, 10);
        return Number.isFinite(n) && n >= 0 ? n : fallback;
    }

    function formatCounterNumber(value) {
        try {
            return new Intl.NumberFormat('vi-VN').format(value);
        } catch (e) {
            return String(value);
        }
    }

    function buildOdometerDigit(digit) {
        var digitWrap = document.createElement('span');
        digitWrap.className = 'pledge-odo-digit';
        digitWrap.setAttribute('aria-hidden', 'true');

        var reel = document.createElement('span');
        reel.className = 'pledge-odo-reel';
        for (var i = 0; i <= 9; i += 1) {
            var item = document.createElement('span');
            item.textContent = String(i);
            reel.appendChild(item);
        }

        digitWrap.appendChild(reel);
        digitWrap._pledgeReel = reel;
        digitWrap._pledgeDigit = -1;
        setOdometerDigit(digitWrap, digit);
        return digitWrap;
    }

    function setOdometerDigit(slot, digit) {
        digit = Math.max(0, Math.min(9, parseInt(digit, 10) || 0));
        if (slot._pledgeDigit === digit) return;
        slot._pledgeDigit = digit;
        if (slot._pledgeReel) {
            slot._pledgeReel.style.transform = 'translateY(-' + digit + 'em)';
        }
    }

    function renderOdometerNumber(value) {
        if (!counterValueEl) return;

        var formatted = formatCounterNumber(value);
        counterValueEl.setAttribute('aria-label', formatted);

        var currentPattern = counterOdometerSlots.map(function (slot) { return slot.kind; }).join('');
        var nextPattern = formatted.replace(/[0-9]/g, 'd');

        if (currentPattern !== nextPattern) {
            counterValueEl.textContent = '';
            counterOdometerSlots = [];
            for (var i = 0; i < formatted.length; i += 1) {
                var ch = formatted.charAt(i);
                if (/[0-9]/.test(ch)) {
                    var digitSlot = buildOdometerDigit(0);
                    counterValueEl.appendChild(digitSlot);
                    counterOdometerSlots.push({ kind: 'd', node: digitSlot });
                } else {
                    var sep = document.createElement('span');
                    sep.className = 'pledge-odo-separator';
                    sep.setAttribute('aria-hidden', 'true');
                    sep.textContent = ch;
                    counterValueEl.appendChild(sep);
                    counterOdometerSlots.push({ kind: ch, node: sep });
                }
            }
        }

        var digitIndex = 0;
        for (var j = 0; j < formatted.length; j += 1) {
            var digitChar = formatted.charAt(j);
            if (!/[0-9]/.test(digitChar)) continue;
            while (counterOdometerSlots[digitIndex] && counterOdometerSlots[digitIndex].kind !== 'd') {
                digitIndex += 1;
            }
            if (counterOdometerSlots[digitIndex]) {
                setOdometerDigit(counterOdometerSlots[digitIndex].node, digitChar);
            }
            digitIndex += 1;
        }
    }

    function setCounterNumber(value) {
        pledgeDisplayedTotal = Math.max(0, Math.round(value));
        renderOdometerNumber(pledgeDisplayedTotal);
    }

    function bumpCounter() {
        if (!counterEl) return;
        counterEl.classList.remove('is-bumping');
        void counterEl.offsetWidth;
        counterEl.classList.add('is-bumping');
        setTimeout(function () { counterEl.classList.remove('is-bumping'); }, 760);
    }

    function showCounterDelta(delta) {
        if (!counterDeltaEl || delta <= 0) return;
        counterDeltaEl.textContent = '+' + formatCounterNumber(delta) + ' cam káº¿t má»›i';
        counterDeltaEl.hidden = false;
        counterDeltaEl.style.animation = 'none';
        void counterDeltaEl.offsetWidth;
        counterDeltaEl.style.animation = '';
        clearTimeout(counterDeltaTimer);
        counterDeltaTimer = setTimeout(function () {
            counterDeltaEl.hidden = true;
        }, 1450);
    }

    function animatePledgeCounter(nextTotal, options) {
        options = options || {};
        nextTotal = parseCounterNumber(nextTotal, pledgeCurrentTotal);
        var delta = typeof options.delta === 'number' ? options.delta : nextTotal - pledgeCurrentTotal;
        if (nextTotal === pledgeCurrentTotal && !options.force) return;

        pledgeCurrentTotal = nextTotal;
        if (!counterValueEl) return;

        if (delta > 0 && options.showDelta) showCounterDelta(delta);
        bumpCounter();

        var start = pledgeDisplayedTotal;
        if (counterAnimationFrame) {
            cancelAnimationFrame(counterAnimationFrame);
            counterAnimationFrame = null;
        }
        if (counterStepTimer) {
            clearInterval(counterStepTimer);
            counterStepTimer = null;
        }

        if (options.instant) {
            setCounterNumber(nextTotal);
            return;
        }

        var diff = nextTotal - start;
        if (options.initial && Math.abs(diff) > 0 && Math.abs(diff) <= 220) {
            var current = start;
            var step = diff > 0 ? 1 : -1;
            var stepDelay = 70;

            setCounterNumber(current);
            counterStepTimer = setInterval(function () {
                current += step;
                var done = step > 0 ? current >= nextTotal : current <= nextTotal;
                setCounterNumber(done ? nextTotal : current);
                if (done) {
                    clearInterval(counterStepTimer);
                    counterStepTimer = null;
                }
            }, stepDelay);
            return;
        }

        var duration = options.initial
            ? Math.min(5200, Math.max(1800, Math.abs(diff) * 70))
            : Math.min(1400, Math.max(520, Math.abs(diff) * 90));
        var startedAt = performance.now();

        function tick(now) {
            var t = Math.min(1, (now - startedAt) / duration);
            var eased = options.initial ? t : 1 - Math.pow(1 - t, 3);
            var rawValue = start + diff * eased;
            var steppedValue = diff >= 0 ? Math.floor(rawValue) : Math.ceil(rawValue);
            setCounterNumber(t >= 1 ? nextTotal : steppedValue);
            if (t < 1) {
                counterAnimationFrame = requestAnimationFrame(tick);
            } else {
                counterAnimationFrame = null;
                setCounterNumber(nextTotal);
            }
        }
        counterAnimationFrame = requestAnimationFrame(tick);
    }

    function isCounterInView() {
        var target = wallControls || counterEl;
        if (!target || !target.getBoundingClientRect) return true;

        var rect = target.getBoundingClientRect();
        var viewH = window.innerHeight || document.documentElement.clientHeight || 0;
        var viewW = window.innerWidth || document.documentElement.clientWidth || 0;
        return rect.bottom > 0 && rect.right > 0 && rect.top < viewH * 0.88 && rect.left < viewW;
    }

    function startInitialCounterWhenVisible() {
        if (!counterValueEl) return;

        setCounterNumber(0);
        var started = false;
        var target = wallControls || counterEl;

        function start() {
            if (started) return;
            started = true;
            window.removeEventListener('scroll', onViewportChange);
            window.removeEventListener('resize', onViewportChange);
            animatePledgeCounter(pledgeCurrentTotal, { initial: true, force: true });
        }

        function onViewportChange() {
            if (isCounterInView()) start();
        }

        if (target && 'IntersectionObserver' in window) {
            var observer = new IntersectionObserver(function (entries) {
                if (!entries.some(function (entry) { return entry.isIntersecting; })) return;
                observer.disconnect();
                start();
            }, {
                root: null,
                threshold: 0.28,
                rootMargin: '0px 0px -12% 0px'
            });
            observer.observe(target);
            return;
        }

        window.addEventListener('scroll', onViewportChange, { passive: true });
        window.addEventListener('resize', onViewportChange);
        onViewportChange();
    }

    startInitialCounterWhenVisible();

    function setWallView(view, persist) {
        /* Flow mode is temporarily hidden — fall back to jelly */
        if (view === 'flow') view = 'jelly';
        var normalized = view === 'jelly' ? 'jelly' : (view === 'list' ? 'list' : 'jelly');
        var isList = normalized === 'list';
        var isJelly = normalized === 'jelly';
        if (wallEl) {
            wallEl.classList.toggle('is-list-view', isList);
            wallEl.classList.toggle('is-jelly-view', isJelly);
        }
        if (jellyfishModule && typeof jellyfishModule.setActive === 'function') {
            jellyfishModule.setActive(isJelly);
        }
        if (wallControls) wallControls.setAttribute('data-current-view', normalized);
        var controlMode = window.matchMedia && window.matchMedia('(max-width: 900px)').matches ? 'mobile' : 'desktop';
        var pos = activeControlPositions[controlMode][normalized] || activeControlPositions.desktop.flow;
        if (activePill) {
            activePill.setAttribute('x', pos.pillX);
            activePill.setAttribute('y', pos.pillY);
            activePill.setAttribute('width', pos.pillW);
            activePill.setAttribute('height', pos.pillH);
            activePill.setAttribute('rx', pos.pillRx);
        }
        if (activeHighlight) {
            activeHighlight.setAttribute('cx', pos.highlightX);
            activeHighlight.setAttribute('cy', pos.highlightY);
        }
        wallViewBtns.forEach(function (btn) {
            var active = btn.getAttribute('data-pledge-wall-view') === normalized;
            btn.classList.toggle('is-active', active);
            btn.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        if (persist) {
            try { localStorage.setItem('pledge-wall-view', normalized); } catch (e) { /* ignored */ }
        }
    }

    if (wallControls && window.matchMedia) {
        var wallControlMedia = window.matchMedia('(max-width: 900px)');
        var syncActiveControl = function () {
            var current = wallControls.getAttribute('data-current-view');
            var normalized = current === 'jelly' ? 'jelly' : (current === 'list' ? 'list' : 'flow');
            setWallView(normalized, false);
        };
        if (wallControlMedia.addEventListener) {
            wallControlMedia.addEventListener('change', syncActiveControl);
        } else if (wallControlMedia.addListener) {
            wallControlMedia.addListener(syncActiveControl);
        }
    }

    if (wallViewBtns.length) {
        wallViewBtns.forEach(function (btn) {
            btn.addEventListener('click', function () {
                var v = btn.getAttribute('data-pledge-wall-view');
                var normalized = v === 'jelly' ? 'jelly' : (v === 'list' ? 'list' : 'flow');
                setWallView(normalized, true);
            });
        });
    }

    function randomBetween(min, max) { return min + Math.random() * (max - min); }

    function buildCard(item, isNew) {
        /* Depth tier: small=far(top), large=close(bottom) */
        var tier = Math.random();
        var scale, topPct, driftDur, bobDur;
        if (tier < 0.30) {                           /* far */
            scale    = randomBetween(0.54, 0.70);
            topPct   = randomBetween(4,  28);
            driftDur = randomBetween(42, 56);
            bobDur   = randomBetween(6.5, 9.5);
        } else if (tier < 0.68) {                    /* mid */
            scale    = randomBetween(0.73, 0.95);
            topPct   = randomBetween(27, 58);
            driftDur = randomBetween(28, 44);
            bobDur   = randomBetween(4.5, 7.5);
        } else {                                     /* close */
            scale    = randomBetween(0.98, 1.32);
            topPct   = randomBetween(56, 82);
            driftDur = randomBetween(17, 30);
            bobDur   = randomBetween(2.8, 5.2);
        }
        var driftDelay = -(Math.random() * driftDur);
        var bobDelay   = -(Math.random() * bobDur);

        var el = document.createElement('div');
        el.className = 'pledge-sig-item' + (isNew ? ' is-new' : '');
        el.setAttribute('data-pledge-card-id', item.id);
        el.style.cssText =
            '--drift-dur:'   + driftDur.toFixed(1)  + 's;' +
            '--drift-delay:' + driftDelay.toFixed(1) + 's;' +
            '--bob-dur:'     + bobDur.toFixed(1)    + 's;' +
            '--bob-delay:'   + bobDelay.toFixed(1)  + 's;' +
            '--sig-scale:'   + scale.toFixed(3)     + ';' +
            '--sig-top:'     + topPct.toFixed(1)    + '%;';

        el.innerHTML =
            '<div class="pledge-sig-inner">' +
                '<img class="pledge-sig-img" src="' + escapeAttr(item.signatureUrl) + '" alt="Chu ky cua ' + escapeAttr(item.name) + '" loading="lazy" />' +
                '<div class="pledge-sig-meta">' +
                    '<span class="pledge-sig-name">' + escapeText(item.name)    + '</span>' +
                    '<span class="pledge-sig-msg">'  + escapeText(item.message) + '</span>' +
                '</div>' +
            '</div>';
        if (isNew) {
            setTimeout(function () { el.classList.remove('is-new'); }, 1500);
        }
        return el;
    }

    /* â”€â”€ Signature spotlight (GSAP) â”€â”€ */
    function showSignatureSpotlight(sigUrl, sigName) {
        if (!window.gsap) return;
        var overlay = document.createElement('div');
        overlay.className = 'pledge-sig-spotlight';
        overlay.innerHTML =
            '<div class="pledge-spotlight-content">' +
                '<div class="pledge-spotlight-ripple"></div>' +
                '<div class="pledge-spotlight-ripple"></div>' +
                '<div class="pledge-spotlight-ripple"></div>' +
                '<img class="pledge-spotlight-img" src="' + escapeAttr(sigUrl) + '" alt="" />' +
                '<p class="pledge-spotlight-label">' + escapeText(sigName) + '</p>' +
            '</div>';
        document.body.appendChild(overlay);

        var content = overlay.querySelector('.pledge-spotlight-content');
        var ripples = overlay.querySelectorAll('.pledge-spotlight-ripple');

        gsap.set(overlay,  { opacity: 0 });
        gsap.set(content,  { scale: 0.3, opacity: 0 });
        gsap.to(overlay,   { opacity: 1,  duration: 0.45, ease: 'power2.out' });
        gsap.to(content,   { scale: 1, opacity: 1, duration: 0.8, delay: 0.1, ease: 'back.out(1.5)' });

        ripples.forEach(function (r, i) {
            gsap.fromTo(r,
                { scale: 0.55, opacity: 0.65 },
                { scale: 3.4,  opacity: 0,   duration: 2.6, delay: i * 0.6, repeat: -1, ease: 'power1.out' });
        });

        setTimeout(function () {
            gsap.to(content, { scale: 0.12, opacity: 0, duration: 0.65, ease: 'power2.in' });
            gsap.to(overlay, {
                opacity: 0,
                duration: 0.55,
                delay: 0.28,
                onComplete: function () {
                    if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
                }
            });
        }, 3700);
    }

    function escapeText(s) {
        var div = document.createElement('div');
        div.textContent = s == null ? '' : String(s);
        return div.innerHTML;
    }
    function escapeAttr(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    async function refreshWall(highlightNewest) {
        if (!wallEl) return;
        try {
            var res  = await fetch(endpoints.wall, { credentials: 'same-origin' });
            var data = await res.json();
            var items = data.items || [];

            var emptyMsg = wallEl.querySelector('.pledge-wall-empty');
            if (emptyMsg && items.length > 0) emptyMsg.remove();

            var newest = null;
            var addedCount = 0;
            var addedItems = [];
            items.forEach(function (item) {
                if (renderedIds.has(String(item.id))) return;
                var isNew = highlightNewest && !newest;
                var node  = buildCard(item, isNew);
                wallEl.insertBefore(node, wallEl.firstChild);
                renderedIds.add(String(item.id));
                addedCount += 1;
                addedItems.push({ item: item, mine: isNew });
                if (isNew) newest = node;
            });

            /* Sync server-known signatures with jellyfish view (if active) */
            if (jellyfishModule && typeof jellyfishModule.syncItems === 'function') {
                jellyfishModule.syncItems(addedItems);
            }

            if (typeof data.totalCount === 'number') {
                animatePledgeCounter(data.totalCount, {
                    delta: Math.max(0, data.totalCount - pledgeCurrentTotal),
                    showDelta: data.totalCount > pledgeCurrentTotal
                });
            } else if (addedCount > 0) {
                animatePledgeCounter(pledgeCurrentTotal + addedCount, {
                    delta: addedCount,
                    showDelta: true
                });
            }

            if (newest) newest.scrollIntoView({ behavior: 'smooth', block: 'center' });
        } catch (e) { /* keep silent */ }
    }

    setInterval(function () { refreshWall(false); }, 20000);

    /* ─── Jellyfish view ─── */
    var jellyfishModule = (function () {
        var stage   = document.querySelector('[data-pledge-jellyfish-stage]');
        var canvas  = document.querySelector('[data-pledge-jellyfish-canvas]');
        var tooltip = document.querySelector('[data-pledge-jellyfish-tooltip]');
        var tipName = document.querySelector('[data-pledge-jellyfish-tip-name]');
        var tipMsg  = document.querySelector('[data-pledge-jellyfish-tip-msg]');
        var tipImg  = document.querySelector('[data-pledge-jellyfish-tip-img]');
        if (!stage || !canvas) return null;

        var POINTS = window.PLEDGE_JELLY_POINTS || [];
        if (!POINTS.length) return null;

        var ctx = null;
        var dpr = Math.max(window.devicePixelRatio || 1, 1);
        var cssW = 0, cssH = 0;
        var isActive = false;
        var rafId = 0;
        var lastTs = 0;
        var elapsed = 0;
        var assigned = []; /* {item, pointIndex, color, mine, spot} */
        var assignedById = Object.create(null);
        var wallItems = []; /* canonical ordered list of all known signatures */
        var wallItemsById = Object.create(null);
        var hoveredIndex = -1;
        var mouseX = -1, mouseY = -1;
        var autospotTimer = 0;
        var nextMine = false;
        var blinkTimer = 1800 + Math.random() * 2000;
        var blinkAnim = 0;

        /* Pre-calc POINTS bounds (kept for diagnostics, not used in mapping) */
        var minNx = 1, maxNx = 0, minNy = 1, maxNy = 0;
        for (var pi = 0; pi < POINTS.length; pi++) {
            var p = POINTS[pi];
            if (p[0] < minNx) minNx = p[0];
            if (p[0] > maxNx) maxNx = p[0];
            if (p[1] < minNy) minNy = p[1];
            if (p[1] > maxNy) maxNy = p[1];
        }
        /* Jellyfish aspect = bounding box of the source image (width/height).
           The point cloud was generated with ASPECT = 0.9024 (taller than wide),
           exported alongside PLEDGE_JELLY_POINTS. Using the wrong value squashes
           the silhouette horizontally. */
        var JELLY_ASPECT = (typeof window.ASPECT === 'number' && window.ASPECT > 0) ? window.ASPECT : 0.9024;

        /* Seed wallItems with initial server data */
        (window.PLEDGE_INITIAL_WALL || []).forEach(function (item) {
            wallItemsById[String(item.id)] = item;
            wallItems.push(item);
        });

        function setupCanvas() {
            if (!canvas) return;
            var rect = canvas.getBoundingClientRect();
            cssW = Math.max(1, Math.floor(rect.width));
            cssH = Math.max(1, Math.floor(rect.height));
            dpr = Math.max(window.devicePixelRatio || 1, 1);
            canvas.width  = cssW * dpr;
            canvas.height = cssH * dpr;
            canvas.style.width  = cssW + 'px';
            canvas.style.height = cssH + 'px';
            ctx = canvas.getContext('2d');
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        }

        function jellyBox() {
            var padTop = 48, padBot = 64, padX = 36;
            var availH = Math.max(80, cssH - padTop - padBot);
            var availW = Math.max(80, cssW - 2 * padX);
            var jh = availH;
            var jw = jh * JELLY_ASPECT;
            if (jw > availW) {
                jw = availW;
                jh = jw / JELLY_ASPECT;
            }
            var ox = (cssW - jw) / 2;
            var oy = padTop + (availH - jh) / 2;
            return { ox: ox, oy: oy, jw: jw, jh: jh };
        }

        function mapXY(nx, ny) {
            /* Direct [0,1] mapping (matches demo) — POINTS in [~0.05, ~0.94] cluster inside inner area */
            var b = jellyBox();
            return [b.ox + nx * b.jw, b.oy + ny * b.jh];
        }

        function buildColor(r, g, b) {
            var br = Math.min(255, Math.round(r * 0.55 + 120));
            var bg = Math.min(255, Math.round(g * 0.55 + 120));
            var bb = Math.min(255, Math.round(b * 0.55 + 120));
            return 'rgb(' + br + ',' + bg + ',' + bb + ')';
        }

        function pulseSpot(value) { return Math.max(value, 1.4); }

        function jellyTextScale() {
            return Math.max(0.54, Math.min(1, jellyBox().jw / 560));
        }

        function makeEntry(item, pointIndex, mine) {
            var pt = POINTS[pointIndex % POINTS.length];
            var color = buildColor(pt[2], pt[3], pt[4]);
            var entry = {
                item: item,
                pointIndex: pointIndex,
                color: color,
                mine: !!mine,
                ph: Math.random() * 6.28,
                spot: mine ? 1.6 : 0,
                alpha: 0,
                x: 0,
                y: 0,
                w: 110,
                h: 0,
                img: null,
                imgLoaded: false,
                imgFailed: false
            };
            /* Names are the "ink" that forms the silhouette (cheap text).
               The real signature image is loaded lazily by the tooltip on hover,
               so we no longer eagerly fetch hundreds of bitmaps here. */
            return entry;
        }

        function loadSignatureImage(entry) {
            if (!entry || !entry.item || !entry.item.signatureUrl) return;
            var img = new Image();
            img.crossOrigin = 'anonymous';
            img.onload = function () { entry.imgLoaded = true; };
            img.onerror = function () { entry.imgFailed = true; };
            img.src = entry.item.signatureUrl;
            entry.img = img;
        }

        function syncFromWallItems() {
            /* Rebuild `assigned` from `wallItems`, preserving existing entries */
            var seen = Object.create(null);
            for (var j = 0; j < assigned.length; j++) {
                seen[String(assigned[j].item.id)] = assigned[j];
            }
            var next = [];
            for (var i = 0; i < wallItems.length; i++) {
                var item = wallItems[i];
                var id = String(item.id);
                var existing = seen[id];
                if (existing) {
                    existing.item = item;
                    next.push(existing);
                } else {
                    next.push(makeEntry(item, i, false));
                }
            }
            assigned = next;
            assignedById = Object.create(null);
            for (var k = 0; k < assigned.length; k++) {
                assignedById[String(assigned[k].item.id)] = assigned[k];
            }
        }

        function addItem(item, mine) {
            var id = String(item.id);
            if (wallItemsById[id]) {
                /* Update existing */
                wallItemsById[id] = item;
                for (var i = 0; i < wallItems.length; i++) {
                    if (String(wallItems[i].id) === id) { wallItems[i] = item; break; }
                }
                var existing = assignedById[id];
                if (existing) existing.item = item;
                return existing;
            }
            wallItemsById[id] = item;
            wallItems.push(item);
            var pointIndex = wallItems.length - 1;
            var entry = makeEntry(item, pointIndex, mine);
            assignedById[id] = entry;
            assigned.push(entry);
            return entry;
        }

        function pulseNewest() {
            if (!assigned.length) return;
            var last = assigned[assigned.length - 1];
            if (last) last.spot = pulseSpot(last.spot);
        }

        function setActive(active) {
            isActive = !!active;
            if (stage) {
                stage.hidden = !isActive;
                stage.setAttribute('aria-hidden', isActive ? 'false' : 'true');
            }
            if (isActive) {
                setupCanvas();
                syncFromWallItems();
                start();
            } else {
                stop();
                hideTooltip();
            }
        }

        function start() {
            if (rafId) return;
            lastTs = performance.now();
            rafId = requestAnimationFrame(loop);
        }

        function stop() {
            if (rafId) cancelAnimationFrame(rafId);
            rafId = 0;
        }

        function hideTooltip() {
            if (!tooltip) return;
            tooltip.classList.remove('is-visible');
            tooltip.hidden = true;
        }

        function showTooltipAt(x, y, item) {
            if (!tooltip) return;
            tooltip.style.left = x + 'px';
            tooltip.style.top  = y + 'px';
            if (tipName) tipName.textContent = (item && item.name) || '';
            if (tipMsg)  tipMsg.textContent  = (item && item.message) || '';
            if (tipImg) {
                if (item && item.signatureUrl) {
                    if (tipImg.src !== item.signatureUrl) tipImg.src = item.signatureUrl;
                    tipImg.style.display = '';
                } else {
                    tipImg.removeAttribute('src');
                    tipImg.style.display = 'none';
                }
            }
            tooltip.hidden = false;
            requestAnimationFrame(function () { tooltip.classList.add('is-visible'); });
        }

        function onPointerMove(ev) {
            if (!isActive) return;
            var t = ev.touches && ev.touches[0] ? ev.touches[0] : ev;
            var rect = canvas.getBoundingClientRect();
            mouseX = t.clientX - rect.left;
            mouseY = t.clientY - rect.top;
        }
        function onPointerLeave() {
            mouseX = -1; mouseY = -1;
            hoveredIndex = -1;
            hideTooltip();
        }

        if (canvas) {
            canvas.addEventListener('mousemove',   onPointerMove);
            canvas.addEventListener('touchstart',  onPointerMove, { passive: true });
            canvas.addEventListener('touchmove',   onPointerMove, { passive: true });
            canvas.addEventListener('mouseleave',  onPointerLeave);
            canvas.addEventListener('touchend',    onPointerLeave, { passive: true });
        }
        window.addEventListener('resize', function () {
            if (isActive) {
                setupCanvas();
            }
        });

        function autoSpot(dt) {
            autospotTimer -= dt;
            if (autospotTimer > 0) return;
            if (hoveredIndex >= 0) return;
            var candidates = [];
            for (var i = 0; i < assigned.length; i++) {
                if (assigned[i].alpha > 0.5) candidates.push(assigned[i]);
            }
            if (!candidates.length) { autospotTimer = 1500; return; }
            var pick = candidates[(Math.random() * candidates.length) | 0];
            if (pick) pick.spot = Math.max(pick.spot, 1);
            autospotTimer = 2200 + Math.random() * 1500;
        }

        function drawFace(t) {
            var b = jellyBox();
            var jw = b.jw, jh = b.jh;
            var bobX = Math.sin(t * 1.0) * 1.2, bobY = Math.sin(t * 1.2) * 1.6;
            var exL = b.ox + 0.48 * jw + bobX;
            var exR = b.ox + 0.64 * jw + bobX;
            var eyeY = b.oy + 0.32 * jh + bobY;
            var cx = (exL + exR) / 2;
            var rx = 0.020 * jw, ry = 0.030 * jh;
            var blink = blinkAnim > 0 ? Math.max(0.08, Math.abs(blinkAnim - 90) / 90) : 1;
            ctx.save();
            /* Soft halo behind the face so it reads against the dense text */
            var halo = ctx.createRadialGradient(cx, eyeY + ry, 2, cx, eyeY + ry, jw * 0.30);
            halo.addColorStop(0, 'rgba(228,249,255,0.20)');
            halo.addColorStop(1, 'rgba(228,249,255,0)');
            ctx.fillStyle = halo;
            ctx.beginPath(); ctx.arc(cx, eyeY + ry, jw * 0.30, 0, 7); ctx.fill();
            /* Eyebrows */
            ctx.strokeStyle = '#0c1a2b'; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
            ctx.lineWidth = Math.max(2, jw * 0.012);
            [exL, exR].forEach(function (ex) {
                ctx.beginPath();
                ctx.moveTo(ex - rx * 1.2, eyeY - ry * 2.0);
                ctx.quadraticCurveTo(ex, eyeY - ry * 2.7, ex + rx * 1.2, eyeY - ry * 2.0);
                ctx.stroke();
            });
            /* Eyes with highlight + glint */
            [exL, exR].forEach(function (ex) {
                ctx.fillStyle = '#0a1622';
                ctx.beginPath(); ctx.ellipse(ex, eyeY, rx, ry * blink, 0, 0, 7); ctx.fill();
                ctx.fillStyle = 'rgba(255,255,255,0.92)';
                ctx.beginPath(); ctx.ellipse(ex - rx * 0.34, eyeY - ry * 0.36 * blink, rx * 0.30, ry * 0.34 * blink, 0, 0, 7); ctx.fill();
                ctx.fillStyle = 'rgba(255,255,255,0.5)';
                ctx.beginPath(); ctx.arc(ex + rx * 0.30, eyeY + ry * 0.28 * blink, rx * 0.15, 0, 7); ctx.fill();
            });
            /* Smile */
            ctx.strokeStyle = '#0c1a2b'; ctx.lineWidth = Math.max(2.4, jw * 0.016);
            var my = eyeY + ry * 3.0, mw = rx * 1.7;
            ctx.beginPath(); ctx.moveTo(cx - mw, my); ctx.quadraticCurveTo(cx, my + ry * 1.9, cx + mw, my); ctx.stroke();
            ctx.restore();
        }

        function loop(now) {
            rafId = 0;
            if (!isActive || !ctx) return;
            var dt = Math.min(50, now - lastTs);
            lastTs = now;
            elapsed += dt;
            autoSpot(dt);
            blinkTimer -= dt;
            if (blinkTimer <= 0 && blinkAnim <= 0) { blinkAnim = 180; blinkTimer = 2600 + Math.random() * 3200; }
            if (blinkAnim > 0) blinkAnim -= dt;

            var grad = ctx.createRadialGradient(cssW * 0.5, cssH * 0.32, 40, cssW * 0.5, cssH * 0.5, Math.max(cssW, cssH) * 0.85);
            grad.addColorStop(0, '#0b2740');
            grad.addColorStop(1, '#03101f');
            ctx.fillStyle = grad;
            ctx.fillRect(0, 0, cssW, cssH);

            var t = elapsed / 1000;
            var best = null, bestDist = 26 * 26;
            hoveredIndex = -1;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            for (var i = 0; i < assigned.length; i++) {
                var a = assigned[i];
                if (!a) continue;
                var pt = POINTS[a.pointIndex % POINTS.length];
                var xy = mapXY(pt[0], pt[1]);
                /* Gentle drift: bell pulses, tentacles (high ny) sway more — matches demo */
                var ptny = pt[1];
                var tf = Math.pow(ptny, 1.5);
                var swX = Math.sin(t * 1.6 - ptny * 5 + a.ph) * (tf * 16 + 1.5);
                var swY = Math.sin(t * 1.2 + a.ph) * 1.6 + Math.sin(t * 0.7) * 2 * tf;
                var goalX = xy[0] + swX, goalY = xy[1] + swY;
                if (a.x === 0 && a.y === 0) {
                    a.x = goalX; a.y = goalY;
                } else {
                    var k = Math.min(1, 0.085 * dt * 0.06 + 0.085);
                    a.x += (goalX - a.x) * k;
                    a.y += (goalY - a.y) * k;
                }
                if (a.spot > 0) a.spot = Math.max(0, a.spot - dt * 0.0012);
                a.alpha += (1 - a.alpha) * Math.min(1, dt * 0.012);
                if (a.alpha < 0.04) continue;

                var s = a.spot;
                var size = (9 + s * 9) * jellyTextScale();
                ctx.font = (s > 0.05 ? 600 : 500) + ' ' + size.toFixed(1) + 'px "Segoe UI", system-ui, sans-serif';
                ctx.globalAlpha = (0.78 + s * 0.22) * a.alpha;
                if (s > 0.05) {
                    /* Spotlighted name glows in its own colour and brightens to near-white */
                    ctx.shadowColor = a.mine ? 'rgba(255,233,168,0.95)' : a.color;
                    ctx.shadowBlur = 12 * s;
                    ctx.fillStyle = a.mine ? '#ffe9a8' : '#eafcff';
                } else {
                    ctx.shadowBlur = 0;
                    /* Resting colour = sampled from the original jellyfish image (per-point) */
                    ctx.fillStyle = a.color;
                }
                var label = (a.item && a.item.name) ? a.item.name : '';
                ctx.fillText(label, a.x, a.y);
                ctx.shadowBlur = 0;

                if (mouseX >= 0) {
                    var hdx = a.x - mouseX, hdy = a.y - mouseY;
                    var d2 = hdx * hdx + hdy * hdy;
                    if (d2 < bestDist) {
                        bestDist = d2;
                        best = a;
                        hoveredIndex = i;
                    }
                }
            }
            ctx.globalAlpha = 1;

            if (best) best.spot = Math.max(best.spot, 1.4);
            drawFace(t);

            if (best) {
                showTooltipAt(best.x, best.y, best.item);
            } else if (mouseX >= 0) {
                hideTooltip();
            }

            rafId = requestAnimationFrame(loop);
        }

        setInterval(function () {
            if (!isActive || !assigned.length) return;
            var pick = assigned[(Math.random() * assigned.length) | 0];
            if (pick) pick.spot = Math.max(pick.spot, 1);
        }, 4200);

        return {
            setActive: setActive,
            addItem: function (item, mine) { return addItem(item, mine); },
            syncItems: function (added) {
                if (!added || !added.length) return;
                added.forEach(function (entry) {
                    var e = addItem(entry.item, !!entry.mine);
                    if (e && entry.mine) e.spot = pulseSpot(e.spot);
                });
            },
            pulseNewest: pulseNewest,
            ensureAssignment: syncFromWallItems,
            sync: syncFromWallItems
        };
    })();

    /* Pre-populate jellyfish module on load */
    if (jellyfishModule) {
        jellyfishModule.sync();
    }

    /* Initialize the wall view (must run after jellyfishModule is defined) */
    if (wallViewBtns.length) {
        var savedWallView = 'jelly';
        try { savedWallView = localStorage.getItem('pledge-wall-view') || 'jelly'; } catch (e) { savedWallView = 'jelly'; }
        /* Flow mode is temporarily hidden — fall back to jelly if saved */
        if (savedWallView === 'flow') savedWallView = 'jelly';
        var initialView = savedWallView === 'jelly' ? 'jelly' : (savedWallView === 'list' ? 'list' : 'jelly');
        setWallView(initialView, false);
    }

    /* â”€â”€ Thanks modal â”€â”€ */
    var thanks = document.querySelector('[data-pledge-thanks]');
    function showThanks() { if (thanks) thanks.hidden = false; }
    function hideThanks() { if (thanks) thanks.hidden = true; }

    var thanksClose = document.querySelector('[data-pledge-thanks-close]');
    if (thanksClose) thanksClose.addEventListener('click', hideThanks);

    var thanksSee = document.querySelector('[data-pledge-thanks-see]');
    if (thanksSee) thanksSee.addEventListener('click', hideThanks);

    var thanksShare = document.querySelector('[data-pledge-thanks-share]');
    if (thanksShare) {
        thanksShare.addEventListener('click', async function () {
            var shareData = {
                title: 'Cam káº¿t Háº£i LÆ°u NgÆ°á»£c',
                text: 'TÃ´i vá»«a kÃ½ cam káº¿t giáº£m nhá»±a cÃ¹ng Háº£i LÆ°u NgÆ°á»£c. Báº¡n cÅ©ng tham gia nhÃ©!',
                url: window.location.origin + '/cam-ket'
            };
            try {
                if (navigator.share) {
                    await navigator.share(shareData);
                } else if (navigator.clipboard) {
                    await navigator.clipboard.writeText(shareData.url);
                    thanksShare.textContent = 'ÄÃ£ sao chÃ©p link!';
                    setTimeout(function () { thanksShare.textContent = 'Chia sáº» cam káº¿t'; }, 2500);
                }
            } catch (e) { /* user cancelled */ }
        });
    }
})();
