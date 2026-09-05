(function () {
  'use strict';

  var meta = function (name) {
    var el = document.querySelector('meta[name="' + name + '"]');
    return el ? el.getAttribute('content') : '';
  };

  var mindUrl    = meta('ar-mind-url');
  var videoUrl   = meta('ar-video-url');
  var videoW     = parseFloat(meta('ar-video-width'))  || 1.0;
  var videoH     = parseFloat(meta('ar-video-height')) || 0.5625;
  var slug       = meta('ar-slug');
  var fallbackUrl = meta('ar-fallback-url');

  var permCard   = document.getElementById('ar-permission-card');
  var sceneWrap  = document.getElementById('ar-scene-container');
  var exitBtn    = document.getElementById('ar-exit-btn');
  var hud        = document.getElementById('ar-hud');
  var errorCard  = document.getElementById('ar-error-card');
  var startBtn   = document.getElementById('ar-start-btn');

  // Feature detection — redirect if unsupported
  if (!window.isSecureContext) {
    window.location.replace(fallbackUrl);
    return;
  }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    window.location.replace(fallbackUrl);
    return;
  }

  // iOS < 13 check
  var ua = navigator.userAgent;
  var iosMatch = ua.match(/iP(hone|od|ad).*OS (\d+)/);
  if (iosMatch && parseInt(iosMatch[2], 10) < 13) {
    window.location.replace(fallbackUrl);
    return;
  }

  var pingFired = false;
  var targetPlayTimer = 0;
  var TARGET_STABLE_DELAY_MS = 300;
  var preferredCameraConstraints = {
    audio: false,
    video: {
      facingMode: { ideal: 'environment' },
      width: { ideal: 1280 },
      height: { ideal: 720 },
      frameRate: { ideal: 30 }
    }
  };

  function fireViewPing() {
    if (pingFired) return;
    pingFired = true;
    fetch('/ar/' + slug + '/view-ping', { method: 'POST' }).catch(function () {});
  }

  function showError() {
    if (permCard) permCard.hidden = true;
    if (sceneWrap) sceneWrap.hidden = true;
    if (errorCard) errorCard.hidden = false;
  }

  // MindAR 1.2.5 requests only facingMode=environment, which lets mobile
  // browsers pick a low-resolution stream. Temporarily enrich that request
  // with HD preferences while MindAR opens its camera.
  function startMindAr(arSystem) {
    var mediaDevices = navigator.mediaDevices;
    var originalGetUserMedia = mediaDevices.getUserMedia;
    var patched = false;

    function restore() {
      if (!patched) return;
      try { mediaDevices.getUserMedia = originalGetUserMedia; } catch (e) {}
      patched = false;
    }

    try {
      var preferredGetUserMedia = function (constraints) {
        var requestedVideo = constraints && constraints.video && typeof constraints.video === 'object'
          ? constraints.video
          : {};
        return originalGetUserMedia.call(mediaDevices, {
          audio: constraints ? constraints.audio === true : false,
          video: Object.assign({}, preferredCameraConstraints.video, requestedVideo, {
            width: preferredCameraConstraints.video.width,
            height: preferredCameraConstraints.video.height,
            frameRate: preferredCameraConstraints.video.frameRate
          })
        });
      };
      mediaDevices.getUserMedia = preferredGetUserMedia;
      patched = mediaDevices.getUserMedia === preferredGetUserMedia;
    } catch (e) {
      patched = false;
    }

    var result;
    try {
      result = arSystem.start();
    } catch (e) {
      restore();
      throw e;
    }

    if (result && typeof result.then === 'function') {
      return result.then(function (value) {
        restore();
        return value;
      }, function (error) {
        restore();
        throw error;
      });
    }

    restore();
    return result;
  }

  function buildArScene() {
    var sceneEl = document.createElement('a-scene');
    sceneEl.setAttribute('mindar-image', 'imageTargetSrc: ' + mindUrl + '; autoStart: false; uiLoading: no; uiError: no; uiScanning: no');
    sceneEl.setAttribute('color-space', 'sRGB');
    sceneEl.setAttribute('renderer', 'colorManagement: true; physicallyCorrectLights: false');
    sceneEl.setAttribute('vr-mode-ui', 'enabled: false');
    sceneEl.setAttribute('device-orientation-permission-ui', 'enabled: false');

    // Assets
    var assetsEl = document.createElement('a-assets');
    var videoEl = document.createElement('video');
    videoEl.id = 'arvid';
    videoEl.src = videoUrl;
    videoEl.setAttribute('preload', 'metadata');
    videoEl.setAttribute('playsinline', '');
    videoEl.setAttribute('webkit-playsinline', '');
    videoEl.setAttribute('muted', '');
    videoEl.muted = true; // Safari honors the property, not just the attribute
    videoEl.setAttribute('loop', '');
    videoEl.setAttribute('crossorigin', 'anonymous');
    videoEl.load();
    assetsEl.appendChild(videoEl);
    sceneEl.appendChild(assetsEl);

    // Camera
    var camEl = document.createElement('a-camera');
    camEl.setAttribute('position', '0 0 0');
    camEl.setAttribute('look-controls', 'enabled: false');
    sceneEl.appendChild(camEl);

    // Target entity
    var targetEl = document.createElement('a-entity');
    targetEl.setAttribute('mindar-image-target', 'targetIndex: 0');

    var planeEl = document.createElement('a-video');
    planeEl.setAttribute('src', '#arvid');
    planeEl.setAttribute('width', String(videoW));
    planeEl.setAttribute('height', String(videoH));
    planeEl.setAttribute('position', '0 0 0');
    targetEl.appendChild(planeEl);
    sceneEl.appendChild(targetEl);

    sceneWrap.appendChild(sceneEl);

    // Events
    sceneEl.addEventListener('loaded', function () {
      var arSystem = sceneEl.systems['mindar-image-system'];
      if (!arSystem) { showError(); return; }

      var revealScene = function () {
        if (permCard) permCard.hidden = true;
        sceneWrap.hidden = false;
        if (exitBtn) exitBtn.hidden = false;
        if (hud) hud.hidden = false;
      };

      // arSystem.start() returns a Promise. Safari can reject it (camera
      // busy/denied) — a bare try/catch only sees sync throws, so an
      // unhandled rejection would leave the UI stuck on "Đang tải AR...".
      var startResult;
      try {
        startResult = startMindAr(arSystem);
      } catch (e) {
        showError();
        return;
      }
      if (startResult && typeof startResult.then === 'function') {
        startResult.then(revealScene).catch(function () { showError(); });
      } else {
        revealScene();
      }
    });

    targetEl.addEventListener('targetFound', function () {
      window.clearTimeout(targetPlayTimer);
      targetPlayTimer = window.setTimeout(function () {
        videoEl.play().catch(function () {});
      }, TARGET_STABLE_DELAY_MS);
      fireViewPing();
    });

    targetEl.addEventListener('targetLost', function () {
      window.clearTimeout(targetPlayTimer);
      targetPlayTimer = 0;
      videoEl.pause();
    });

    sceneEl.addEventListener('arError', function () {
      showError();
    });
  }

  function loadVendorScripts(callback) {
    var vendorBase = '/_content/NewsCMS.Theme.HaiLuuNguoc/js/vendor/';
    var aframeSrc = vendorBase + 'aframe.min.js';
    var mindarSrc = vendorBase + 'mindar-image-aframe.prod.js';

    function loadScript(src, next) {
      var s = document.createElement('script');
      s.src = src;
      s.onload = next;
      s.onerror = function () {
        // CDN fallback
        var cdnSrc = src.includes('aframe')
          ? 'https://aframe.io/releases/1.5.0/aframe.min.js'
          : 'https://cdn.jsdelivr.net/npm/mind-ar@1.2.5/dist/mindar-image-aframe.prod.js';
        var s2 = document.createElement('script');
        s2.src = cdnSrc;
        s2.onload = next;
        s2.onerror = function () { showError(); };
        document.head.appendChild(s2);
      };
      document.head.appendChild(s);
    }

    loadScript(aframeSrc, function () {
      loadScript(mindarSrc, callback);
    });
  }

  if (startBtn) {
    startBtn.addEventListener('click', function () {
      startBtn.disabled = true;
      startBtn.textContent = 'Đang tải AR...';

      // Match MindAR's own request (rear camera) so iOS Safari doesn't
      // re-prompt / switch cameras between the probe and MindAR.
      navigator.mediaDevices.getUserMedia(preferredCameraConstraints)
        .then(function (stream) {
          // Release the probe stream, then wait a beat before MindAR opens
          // its own camera. iOS Safari throws NotReadableError if a new
          // getUserMedia races a just-stopped track (Chrome releases the
          // camera instantly, Safari does not).
          stream.getTracks().forEach(function (t) { t.stop(); });
          loadVendorScripts(function () {
            setTimeout(function () {
              try {
                buildArScene();
              } catch (e) {
                showError();
              }
            }, 300);
          });
        })
        .catch(function () {
          window.location.replace(fallbackUrl);
        });
    });
  }
})();
