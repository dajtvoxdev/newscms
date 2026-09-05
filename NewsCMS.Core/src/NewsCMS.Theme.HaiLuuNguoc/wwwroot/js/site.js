const nav = document.querySelector('.site-nav');
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
let activeScrollAnimation = 0;

function updateNavState() {
    nav?.classList.toggle('is-scrolled', window.scrollY > 24);
}

window.addEventListener('scroll', updateNavState, { passive: true });
updateNavState();

function closeNav() {
    nav?.classList.remove('is-open');
    nav?.querySelector('.nav-toggle')?.setAttribute('aria-expanded', 'false');
}

function setupNavToggle() {
    const toggle = nav?.querySelector('.nav-toggle');
    if (!toggle) {
        return;
    }

    toggle.addEventListener('click', () => {
        const isOpen = nav.classList.toggle('is-open');
        toggle.setAttribute('aria-expanded', String(isOpen));
        toggle.setAttribute('aria-label', isOpen ? 'Đóng menu' : 'Mở menu');
    });

    nav.querySelectorAll('.nav-links a').forEach((link) => link.addEventListener('click', closeNav));
}

function revealWithoutMotion() {
    document.querySelectorAll('[data-reveal]').forEach((element) => {
        element.classList.add('is-visible');
    });
}

function runFallbackReveal() {
    const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
            if (!entry.isIntersecting) {
                return;
            }

            entry.target.classList.add('is-visible');
            observer.unobserve(entry.target);
        });
    }, { threshold: 0.08, rootMargin: '0px 0px -18% 0px' });

    document.querySelectorAll('[data-reveal]').forEach((element) => observer.observe(element));
}

function initSectionReveal() {
    if (!window.AOS) {
        runFallbackReveal();
        return;
    }

    AOS.init({
        duration: 850,
        easing: 'ease-out-cubic',
        offset: 120,
        once: false,
        mirror: true,
        disable: false
    });

    AOS.refreshHard();
}

function easeInOutCubic(progress) {
    return progress < 0.5 ? 4 * progress * progress * progress : 1 - Math.pow(-2 * progress + 2, 3) / 2;
}

function scrollToTarget(target) {
    const startY = window.scrollY;
    const targetY = Math.max(target.getBoundingClientRect().top + startY - 24, 0);
    const distance = Math.abs(targetY - startY);
    const duration = prefersReducedMotion ? 0 : Math.min(Math.max(distance * 0.65, 520), 1100);
    const animationId = activeScrollAnimation + 1;
    activeScrollAnimation = animationId;

    if (duration === 0) {
        window.scrollTo(0, targetY);
        return;
    }

    const startTime = performance.now();

    function step(now) {
        if (animationId !== activeScrollAnimation) {
            return;
        }

        const progress = Math.min((now - startTime) / duration, 1);
        window.scrollTo(0, startY + (targetY - startY) * easeInOutCubic(progress));

        if (progress < 1) {
            requestAnimationFrame(step);
        }
    }

    requestAnimationFrame(step);
}

function setupSlowAnchorScroll() {
    document.querySelectorAll('a[href*="#"]').forEach((link) => {
        if (link.matches('[data-comic-open]')) {
            return;
        }

        link.addEventListener('click', (event) => {
            const targetId = link.hash;
            const target = targetId ? document.querySelector(targetId) : null;
            if (!target) {
                return;
            }

            event.preventDefault();
            closeNav();
            scrollToTarget(target);
        });
    });
}

function setupComicDialog() {
    const dialog = document.querySelector('[data-comic-dialog]');
    const track = dialog?.querySelector('[data-comic-track]');
    const current = dialog?.querySelector('[data-comic-current]');
    const modeToggle = dialog?.querySelector('[data-comic-mode-toggle]');
    const pages = track ? Array.from(track.querySelectorAll('.comic-page')) : [];

    if (!dialog || !track || !current || pages.length === 0) {
        return;
    }

    let isVerticalMode = false;
    let touchStartX = 0;
    let touchStartY = 0;
    let touchStartIndex = 0;

    const getPageWidth = () => pages[0]?.getBoundingClientRect().width || track.clientWidth;
    const getCurrentIndex = () => Math.min(Math.round(track.scrollLeft / getPageWidth()), pages.length - 1);
    const updateCounter = () => {
        if (!isVerticalMode) {
            current.textContent = String(getCurrentIndex() + 1);
        }
    };
    const scrollByPage = (direction) => {
        if (isVerticalMode) {
            return;
        }

        track.scrollTo({
            left: getPageWidth() * (getCurrentIndex() + direction),
            behavior: prefersReducedMotion ? 'auto' : 'smooth'
        });
    };
    const setMode = (nextIsVertical) => {
        isVerticalMode = nextIsVertical;
        dialog.classList.toggle('is-vertical', isVerticalMode);
        modeToggle?.setAttribute('aria-pressed', String(isVerticalMode));
        if (modeToggle) {
            modeToggle.textContent = isVerticalMode ? 'Đọc ngang' : 'Đọc dọc';
        }
        track.scrollTo({ left: 0, top: 0, behavior: 'auto' });
        updateCounter();
    };

    document.querySelectorAll('[data-comic-open]').forEach((button) => {
        button.addEventListener('click', () => {
            closeNav();
            if (!dialog.open) {
                dialog.showModal();
            }
            track.scrollTo({ left: 0, top: 0, behavior: 'auto' });
            updateCounter();
            dialog.querySelector('[data-comic-close]')?.focus();
        });
    });

    modeToggle?.addEventListener('click', () => setMode(!isVerticalMode));
    dialog.querySelector('[data-comic-close]')?.addEventListener('click', () => dialog.close());
    dialog.querySelector('[data-comic-prev]')?.addEventListener('click', () => scrollByPage(-1));
    dialog.querySelector('[data-comic-next]')?.addEventListener('click', () => scrollByPage(1));
    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) {
            dialog.close();
        }
    });
    track.addEventListener('touchstart', (event) => {
        const touch = event.changedTouches[0];
        touchStartX = touch.clientX;
        touchStartY = touch.clientY;
        touchStartIndex = getCurrentIndex();
    }, { passive: true });
    track.addEventListener('touchend', (event) => {
        if (isVerticalMode) {
            return;
        }

        const touch = event.changedTouches[0];
        const diffX = touchStartX - touch.clientX;
        const diffY = touchStartY - touch.clientY;
        if (Math.abs(diffX) > 60 && Math.abs(diffX) > Math.abs(diffY) * 1.5) {
            track.scrollTo({
                left: getPageWidth() * (touchStartIndex + (diffX > 0 ? 1 : -1)),
                behavior: prefersReducedMotion ? 'auto' : 'smooth'
            });
        }
    }, { passive: true });
    track.addEventListener('scroll', updateCounter, { passive: true });
    track.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            scrollByPage(-1);
        }
        if (event.key === 'ArrowRight') {
            event.preventDefault();
            scrollByPage(1);
        }
    });
}

function setupContestCountdown() {
    document.querySelectorAll('[data-contest-countdown]').forEach((root) => {
        const deadline = new Date(root.dataset.deadline || '').getTime();
        if (!deadline) {
            return;
        }

        const label = root.querySelector('[data-countdown-label]');
        const days = root.querySelector('[data-countdown-days]');
        const hours = root.querySelector('[data-countdown-hours]');
        const minutes = root.querySelector('[data-countdown-minutes]');
        const seconds = root.querySelector('[data-countdown-seconds]');
        const pad = (value) => String(Math.max(0, value)).padStart(2, '0');

        const update = () => {
            const remaining = deadline - Date.now();
            const totalSeconds = Math.max(0, Math.floor(remaining / 1000));

            if (days) days.textContent = pad(Math.floor(totalSeconds / 86400));
            if (hours) hours.textContent = pad(Math.floor((totalSeconds % 86400) / 3600));
            if (minutes) minutes.textContent = pad(Math.floor((totalSeconds % 3600) / 60));
            if (seconds) seconds.textContent = pad(totalSeconds % 60);

            if (remaining <= 0) {
                root.classList.add('is-closed');
                if (label) {
                    label.textContent = 'Thời gian nộp bài vòng 2 đã kết thúc';
                }
                return false;
            }

            return true;
        };

        if (!update()) {
            return;
        }

        const timer = window.setInterval(() => {
            if (!update()) {
                window.clearInterval(timer);
            }
        }, 1000);
    });
}

function setupScrollTop() {
    const btn = document.querySelector('.scroll-top-btn');
    if (!btn) return;

    function onScroll() {
        const show = window.scrollY > 300;
        btn.hidden = !show;
        btn.classList.toggle('is-visible', show);
    }

    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();

    btn.addEventListener('click', function () {
        activeScrollAnimation += 1;

        if (prefersReducedMotion) {
            window.scrollTo(0, 0);
            return;
        }

        const startY = window.scrollY;
        const startTime = performance.now();
        const duration = Math.min(Math.max(startY * 0.45, 420), 900);
        const animationId = activeScrollAnimation;

        (function step(now) {
            if (animationId !== activeScrollAnimation) {
                return;
            }

            const progress = Math.min((now - startTime) / duration, 1);
            window.scrollTo(0, startY * (1 - easeInOutCubic(progress)));
            if (progress < 1) requestAnimationFrame(step);
        })(startTime);
    });
}

function setupAmbientAudio() {
    const audio = document.querySelector('.ambient-audio');
    const toggle = document.querySelector('.ambient-toggle');
    if (!audio || !toggle) {
        return;
    }

    audio.volume = 0.3;
    // Audio must remain silent until the user explicitly activates this button.
    audio.muted = true;
    audio.pause();

    const updateToggle = () => {
        toggle.setAttribute('aria-pressed', String(audio.muted));
        toggle.setAttribute('aria-label', audio.muted ? 'Bật nhạc nền' : 'Tắt nhạc nền');
        toggle.classList.toggle('is-muted', audio.muted);
    };

    toggle.addEventListener('click', () => {
        audio.muted = !audio.muted;
        updateToggle();

        if (!audio.muted) {
            audio.play().catch(() => {});
        } else {
            audio.pause();
        }
    });

    updateToggle();
}

function runGsapMotion() {
    gsap.registerPlugin(ScrollTrigger);

    gsap.set('.current-line', { transformOrigin: '50% 50%' });

    const heroTimeline = gsap.timeline({ defaults: { ease: 'power3.out' } });
    heroTimeline
        .from('.site-nav', { autoAlpha: 0, y: -24, duration: 0.7 })
        .from('.hero-cinema-copy h1', { autoAlpha: 0, y: 74, skewY: 3, duration: 1 }, '-=0.2')
        .from('.hero-lede', { autoAlpha: 0, y: 28, duration: 0.55 }, '-=0.45')
        .from('.hero-actions .button', { autoAlpha: 0, y: 20, stagger: 0.08, duration: 0.45 }, '-=0.3');

    if (document.querySelector('.hero-media-bar')) {
        heroTimeline.from('.hero-media-bar', { autoAlpha: 0, y: 18, duration: 0.5 }, '-=0.35');
    }

    if (document.querySelector('.current-line')) {
        heroTimeline.from('.current-line', { autoAlpha: 0, scale: 0.72, stagger: 0.12, duration: 0.9 }, '-=0.75');
    }

    gsap.to('.current-line-one', {
        rotate: 18,
        scale: 1.08,
        ease: 'none',
        scrollTrigger: {
            trigger: '.hero-shell',
            start: 'top top',
            end: 'bottom top',
            scrub: true
        }
    });

    gsap.to('.current-line-two', {
        rotate: -32,
        scale: 0.94,
        ease: 'none',
        scrollTrigger: {
            trigger: '.hero-shell',
            start: 'top top',
            end: 'bottom top',
            scrub: true
        }
    });

    initSectionReveal();

}

setupNavToggle();
setupSlowAnchorScroll();
setupComicDialog();
setupContestCountdown();
setupAmbientAudio();
setupScrollTop();

if (!prefersReducedMotion && window.gsap && window.ScrollTrigger) {
    runGsapMotion();
} else {
    initSectionReveal();
}
