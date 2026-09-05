(function () {
  'use strict';

  var meta = function (name) {
    var el = document.querySelector('meta[name="' + name + '"]');
    return el ? el.getAttribute('content') : '';
  };

  var masterMind = meta('ar-master-mind');
  var targets = [];
  try {
    targets = JSON.parse(document.getElementById('ar-targets').textContent || '[]');
  } catch (e) { targets = []; }

  var permCard  = document.getElementById('ar-permission-card');
  var sceneWrap = document.getElementById('ar-scene-container');
  var exitBtn   = document.getElementById('ar-exit-btn');
  var hud       = document.getElementById('ar-hud');
  var errorCard = document.getElementById('ar-error-card');
  var startBtn  = document.getElementById('ar-start-btn');
  var preferredCameraConstraints = {
    audio: false,
    video: {
      facingMode: { ideal: 'environment' },
      width: { ideal: 1280 },
      height: { ideal: 720 },
      frameRate: { ideal: 30 }
    }
  };

  // Camera requires a secure context + getUserMedia.
  if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    showError();
    return;
  }
  var iosMatch = navigator.userAgent.match(/iP(hone|od|ad).*OS (\d+)/);
  if (iosMatch && parseInt(iosMatch[2], 10) < 13) { showError(); return; }
  if (!masterMind || targets.length === 0) { showError(); return; }

  var TARGET_STABLE_DELAY_MS = 300;
  var pinged = {};
  function fireViewPing(slug) {
    if (pinged[slug]) return;
    pinged[slug] = true;
    fetch('/ar/' + slug + '/view-ping', { method: 'POST' }).catch(function () {});
  }

  function showError() {
    if (permCard) permCard.hidden = true;
    if (sceneWrap) sceneWrap.hidden = true;
    if (errorCard) errorCard.hidden = false;
  }

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
    sceneEl.setAttribute('mindar-image',
      'imageTargetSrc: ' + masterMind + '; autoStart: false; maxTrack: 1; uiLoading: no; uiError: no; uiScanning: no');
    sceneEl.setAttribute('color-space', 'sRGB');
    sceneEl.setAttribute('renderer', 'colorManagement: true; physicallyCorrectLights: false');
    sceneEl.setAttribute('vr-mode-ui', 'enabled: false');
    sceneEl.setAttribute('device-orientation-permission-ui', 'enabled: false');

    var assetsEl = document.createElement('a-assets');
    var camEl = document.createElement('a-camera');
    camEl.setAttribute('position', '0 0 0');
    camEl.setAttribute('look-controls', 'enabled: false');

    sceneEl.appendChild(assetsEl);
    sceneEl.appendChild(camEl);

    targets.forEach(function (t) {
      var vid = document.createElement('video');
      vid.id = 'arvid-' + t.index;
      vid.src = t.videoUrl;
      vid.setAttribute('preload', 'metadata');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('webkit-playsinline', '');
      vid.setAttribute('muted', '');
      vid.muted = true; // Safari honors the property, not just the attribute
      vid.setAttribute('loop', '');
      vid.setAttribute('crossorigin', 'anonymous');
      vid.load();
      assetsEl.appendChild(vid);

      var entity = document.createElement('a-entity');
      entity.setAttribute('mindar-image-target', 'targetIndex: ' + t.index);

      var plane = document.createElement('a-video');
      plane.setAttribute('src', '#arvid-' + t.index);
      plane.setAttribute('width', String(t.width || 1));
      plane.setAttribute('height', String(t.height || 0.5625));
      plane.setAttribute('position', '0 0 0');
      entity.appendChild(plane);

      var playTimer = 0;
      entity.addEventListener('targetFound', function () {
        window.clearTimeout(playTimer);
        playTimer = window.setTimeout(function () {
          vid.play().catch(function () {});
        }, TARGET_STABLE_DELAY_MS);
        fireViewPing(t.slug);
        if (hud) {
          var title = hud.querySelector('.ar-hud-title');
          if (title) title.textContent = 'Đã nhận diện ✓';
        }
      });
      entity.addEventListener('targetLost', function () {
        window.clearTimeout(playTimer);
        playTimer = 0;
        vid.pause();
      });

      sceneEl.appendChild(entity);
    });

    sceneWrap.appendChild(sceneEl);

    sceneEl.addEventListener('loaded', function () {
      var arSystem = sceneEl.systems['mindar-image-system'];
      if (!arSystem) { showError(); return; }
      var revealScene = function () {
        if (permCard) permCard.hidden = true;
        sceneWrap.hidden = false;
        if (exitBtn) exitBtn.hidden = false;
        if (hud) hud.hidden = false;
      };
      var startResult;
      try { startResult = startMindAr(arSystem); } catch (e) { showError(); return; }
      if (startResult && typeof startResult.then === 'function') {
        startResult.then(revealScene).catch(function () { showError(); });
      } else {
        revealScene();
      }
    });

    sceneEl.addEventListener('arError', function () { showError(); });
  }

  function loadVendorScripts(callback) {
    var vendorBase = '/_content/NewsCMS.Theme.HaiLuuNguoc/js/vendor/';
    function loadScript(src, cdn, next) {
      var s = document.createElement('script');
      s.src = src;
      s.onload = next;
      s.onerror = function () {
        var s2 = document.createElement('script');
        s2.src = cdn;
        s2.onload = next;
        s2.onerror = function () { showError(); };
        document.head.appendChild(s2);
      };
      document.head.appendChild(s);
    }
    loadScript(vendorBase + 'aframe.min.js', 'https://aframe.io/releases/1.5.0/aframe.min.js', function () {
      loadScript(vendorBase + 'mindar-image-aframe.prod.js',
        'https://cdn.jsdelivr.net/npm/mind-ar@1.2.5/dist/mindar-image-aframe.prod.js', callback);
    });
  }

  if (startBtn) {
    startBtn.addEventListener('click', function () {
      startBtn.disabled = true;
      startBtn.textContent = 'Đang tải AR...';
      navigator.mediaDevices.getUserMedia(preferredCameraConstraints)
        .then(function (stream) {
          stream.getTracks().forEach(function (t) { t.stop(); });
          loadVendorScripts(function () {
            try { buildArScene(); } catch (e) { showError(); }
          });
        })
        .catch(function () { showError(); });
    });
  }
})();
