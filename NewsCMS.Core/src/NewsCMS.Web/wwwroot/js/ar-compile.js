// Auto-generates the MindAR .mind target file in the browser so admins never touch it.
// Compiles as soon as a new target image is chosen (with live progress), uploads the
// result through the media pipeline, and fills the hidden MindFileUrl. By submit time
// it's already done, so saving is instant.
(function () {
  'use strict';

  var form = document.querySelector('[data-ar-form]');
  if (!form) return;

  var targetHidden = form.querySelector('input[name="Input.TargetImageUrl"]');
  var mindHidden   = form.querySelector('input[name="Input.MindFileUrl"]');
  var status       = document.getElementById('ar-compile-status');
  var submitBtn    = form.querySelector('[type="submit"]');
  if (!targetHidden || !mindHidden) return;

  var targetPicker    = targetHidden.closest('.media-picker');
  var targetFileInput = targetPicker ? targetPicker.querySelector('input[type="file"]') : null;

  var VENDOR = '/_content/NewsCMS.Theme.HaiLuuNguoc/js/vendor/';
  var compilerPromise = null;
  var state = 'idle';      // idle | running | done | error
  var pendingSubmit = false;

  function setStatus(msg, kind) {
    if (!status) return;
    status.textContent = msg || '';
    status.classList.remove('text-red-600', 'text-emerald-600', 'text-slate-500');
    status.classList.add(kind === 'error' ? 'text-red-600' : kind === 'ok' ? 'text-emerald-600' : 'text-slate-500');
  }

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = src; s.onload = resolve;
      s.onerror = function () { reject(new Error('Không tải được ' + src)); };
      document.head.appendChild(s);
    });
  }

  // The MindAR compiler lives in the A-Frame bundle, which needs AFRAME present first.
  function loadCompiler() {
    if (window.MINDAR && window.MINDAR.IMAGE && window.MINDAR.IMAGE.Compiler) return Promise.resolve();
    if (compilerPromise) return compilerPromise;
    compilerPromise = (async function () {
      if (!window.AFRAME) await loadScript(VENDOR + 'aframe.min.js');
      await loadScript(VENDOR + 'mindar-image-aframe.prod.js');
      if (!(window.MINDAR && window.MINDAR.IMAGE && window.MINDAR.IMAGE.Compiler))
        throw new Error('Thư viện biên dịch AR không khả dụng.');
    })();
    return compilerPromise;
  }

  function toImage(src) {
    return new Promise(function (resolve, reject) {
      var img = new Image();
      img.crossOrigin = 'anonymous';
      img.onload = function () { resolve(img); };
      img.onerror = function () { reject(new Error('Không đọc được ảnh target.')); };
      img.src = src;
    });
  }

  function uploadMind(file) {
    return new Promise(function (resolve, reject) {
      if (!window.uploadManager || typeof window.uploadManager.enqueue !== 'function') {
        reject(new Error('Trình tải media chưa sẵn sàng.')); return;
      }
      window.uploadManager.enqueue(file, {
        folderId: '',
        onDone: function (r) { (r && r.location) ? resolve(r.location) : reject(new Error('Tải .mind thất bại.')); },
        onError: function () { reject(new Error('Tải .mind thất bại.')); }
      });
    });
  }

  // imageSource: a File (from the picker) or a URL string (fallback at submit).
  async function compile(imageSource) {
    state = 'running';
    if (submitBtn) submitBtn.disabled = true;
    setStatus('Đang tải thư viện…');
    await loadCompiler();
    var src = (typeof imageSource === 'string') ? imageSource : URL.createObjectURL(imageSource);
    try {
      var img = await toImage(src);
      var compiler = new window.MINDAR.IMAGE.Compiler();
      await compiler.compileImageTargets([img], function (p) {
        setStatus('Đang tạo điểm nhận diện AR… ' + Math.round(p) + '%');
      });
      var buffer = await compiler.exportData();
      setStatus('Đang lưu điểm nhận diện…');
      var location = await uploadMind(new File([buffer], 'target.mind', { type: 'application/octet-stream' }));
      mindHidden.value = location;
      state = 'done';
      setStatus('Đã tạo điểm nhận diện ✓', 'ok');
    } finally {
      if (typeof imageSource !== 'string') URL.revokeObjectURL(src);
      if (submitBtn) submitBtn.disabled = false;
    }
  }

  function runCompile(imageSource) {
    compile(imageSource)
      .then(function () {
        if (pendingSubmit) { pendingSubmit = false; submitForm(); }
      })
      .catch(function (err) {
        state = 'error';
        if (submitBtn) submitBtn.disabled = false;
        setStatus((err && err.message) || 'Lỗi tạo điểm nhận diện AR.', 'error');
      });
  }

  function submitForm() {
    if (typeof form.requestSubmit === 'function') form.requestSubmit(); else form.submit();
  }

  // Compile immediately when the admin picks a new target image file.
  if (targetFileInput) {
    targetFileInput.addEventListener('change', function (e) {
      var file = e.target.files && e.target.files[0];
      if (file) runCompile(file);
    });
  }

  // The media-library modal sets the hidden URL rather than a local file input.
  // A newly selected target invalidates the prior .mind file and must compile from
  // its public URL before the edit form can be saved.
  targetHidden.addEventListener('media-library:selected', function (e) {
    var url = e.detail && e.detail.url;
    if (!url || url === form.dataset.originalTarget) return;

    mindHidden.value = '';
    runCompile(url);
  });

  form.addEventListener('submit', function (ev) {
    if (state === 'done') return;                 // already compiled → save
    if (!targetHidden.value && !(targetFileInput && targetFileInput.files[0])) return; // server validates missing image

    // Still compiling → wait, then auto-submit when ready.
    if (state === 'running') {
      ev.preventDefault();
      pendingSubmit = true;
      setStatus('Đang tạo điểm nhận diện, sẽ tự lưu khi xong…');
      return;
    }

    // Mind already present (Edit, unchanged target) → save as-is.
    if (mindHidden.value) return;

    // Target chosen via media library (no file event) but not yet compiled → compile from URL.
    if (targetHidden.value) {
      ev.preventDefault();
      pendingSubmit = true;
      runCompile(targetHidden.value);
    }
  });
})();
