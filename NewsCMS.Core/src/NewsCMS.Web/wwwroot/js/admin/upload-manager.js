/**
 * upload-manager.js
 * Global admin upload manager. Enqueue files for AJAX upload with live progress.
 * Renders a fixed bottom-left tracker visible across all admin pages.
 * Usage: window.uploadManager.enqueue(file, { folderId, onDone, onError })
 *
 * Hai đường upload:
 * - File nhỏ (≤ TUS_THRESHOLD) hoặc thiếu lib tus → XHR 1-request cũ qua /Admin/Media/Upload.
 * - File lớn → tus.io resumable qua /admin/media/tus: chia chunk, retry tự động,
 *   resume sau refresh (localStorage), vượt giới hạn body của Cloudflare Tunnel.
 */
(function () {
    'use strict';

    const UPLOAD_URL = '/Admin/Media/Upload';
    const TUS_ENDPOINT = '/admin/media/tus';
    const TUS_THRESHOLD = 32 * 1024 * 1024;      // > 32MB đi đường tus
    const TUS_CHUNK_SIZE = 64 * 1024 * 1024;     // chunk 64MB — headroom dưới cap ~100MB Cloudflare
    const RETRY_DELAYS = [0, 1000, 3000, 5000, 10000, 30000];
    const STATUS_POLL_INTERVAL = 1500;           // poll TusStatus sau khi PATCH cuối xong
    const STATUS_POLL_MAX = Math.round(5 * 60 * 1000 / STATUS_POLL_INTERVAL);

    let container = null;
    let queue = [];
    let activeCount = 0;
    const MAX_CONCURRENT = 3;

    function getContainer() {
        if (!container) {
            container = document.getElementById('upload-tracker');
        }
        return container;
    }

    function formatSize(bytes) {
        if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
        if (bytes >= 1024) return (bytes / 1024).toFixed(0) + ' KB';
        return bytes + ' B';
    }

    function formatSpeed(bytesPerSecond) {
        if (!bytesPerSecond || bytesPerSecond <= 0) return '';
        return ' · ' + formatSize(bytesPerSecond) + '/s';
    }

    function getAntiforgeryToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    // ---------- localStorage resume cho tus ----------
    const RESUME_PREFIX = 'ncms-tus|';

    function resumeKey(file) {
        return RESUME_PREFIX + file.name + '|' + file.size + '|' + file.lastModified;
    }

    function findResumeUrl(file) {
        try {
            const url = localStorage.getItem(resumeKey(file));
            if (!url) return null;
            // Entry quá ~24h thì bỏ — server cũng đã expire file tus cùng mốc thời gian.
            const ts = parseInt(localStorage.getItem(resumeKey(file) + '|ts') || '0', 10);
            if (ts && Date.now() - ts > 23 * 3600 * 1000) {
                clearResumeEntry(file);
                return null;
            }
            return url;
        } catch (e) { return null; }
    }

    function saveResumeEntry(file, tusUrl) {
        try {
            localStorage.setItem(resumeKey(file), tusUrl);
            localStorage.setItem(resumeKey(file) + '|ts', String(Date.now()));
        } catch (e) { /* storage đầy/bị chặn — resume là tối ưu, không bắt buộc */ }
    }

    function clearResumeEntry(file) {
        try {
            localStorage.removeItem(resumeKey(file));
            localStorage.removeItem(resumeKey(file) + '|ts');
        } catch (e) { /* ignore */ }
    }

    // Quét dọn entry rác quá hạn (tab bị đóng đột ngột trước khi hoàn tất).
    function sweepStaleResumeEntries() {
        try {
            const now = Date.now();
            for (let i = localStorage.length - 1; i >= 0; i--) {
                const key = localStorage.key(i);
                if (!key || !key.startsWith(RESUME_PREFIX) || key.endsWith('|ts')) continue;
                const ts = parseInt(localStorage.getItem(key + '|ts') || '0', 10);
                if (ts && now - ts > 23 * 3600 * 1000) {
                    localStorage.removeItem(key);
                    localStorage.removeItem(key + '|ts');
                }
            }
        } catch (e) { /* ignore */ }
    }

    function createCard(file) {
        const id = 'uc-' + Math.random().toString(36).slice(2);
        const card = document.createElement('div');
        card.id = id;
        card.className = 'upload-card';
        card.innerHTML = `
            <div class="uc-name" title="${file.name}">${file.name}</div>
            <div class="uc-meta">${formatSize(file.size)}</div>
            <div class="uc-bar-wrap"><div class="uc-bar" style="width:0%"></div></div>
            <div class="uc-status"></div>
            <button class="uc-cancel" aria-label="Huỷ">✕</button>
        `;
        return { id, card };
    }

    // ---------- Đường cũ: XHR 1 request ----------
    function uploadFileXhr(file, folderId, onDone, onError, xhr, card, onProgress) {
        const fd = new FormData();
        fd.append('file', file);
        if (folderId) fd.append('folderId', folderId);

        const bar = card.querySelector('.uc-bar');
        const status = card.querySelector('.uc-status');
        const cancelBtn = card.querySelector('.uc-cancel');

        cancelBtn.addEventListener('click', () => {
            xhr.abort();
            card.remove();
        });

        xhr.upload.addEventListener('progress', (e) => {
            if (e.lengthComputable) {
                const pct = Math.round((e.loaded / e.total) * 100);
                bar.style.width = pct + '%';
                status.textContent = pct + '%';
                if (onProgress) onProgress(pct);
            }
        });

        xhr.addEventListener('load', () => {
            activeCount--;
            if (xhr.status >= 200 && xhr.status < 300) {
                let result = null;
                try { result = JSON.parse(xhr.responseText); } catch (e) { /* ignore */ }
                bar.style.width = '100%';
                bar.classList.add('uc-bar-done');
                status.textContent = 'Xong';
                cancelBtn.remove();
                if (onDone) onDone(result);
                setTimeout(() => card.remove(), 2500);
            } else {
                let msg = 'Lỗi tải lên';
                try { msg = JSON.parse(xhr.responseText)?.error || msg; } catch (e) { /* ignore */ }
                bar.classList.add('uc-bar-error');
                status.textContent = msg;
                cancelBtn.textContent = '✕';
                window.toast?.error('Tải lên "' + file.name + '" thất bại: ' + msg);
                if (onError) onError(msg);
            }
            processQueue();
        });

        xhr.addEventListener('error', () => {
            activeCount--;
            bar.classList.add('uc-bar-error');
            status.textContent = 'Lỗi mạng';
            window.toast?.error('Tải lên "' + file.name + '" thất bại: lỗi mạng.');
            if (onError) onError('Network error');
            processQueue();
        });

        xhr.addEventListener('abort', () => {
            activeCount--;
            if (onError) onError('Upload cancelled');
            processQueue();
        });

        xhr.open('POST', UPLOAD_URL);
        xhr.setRequestHeader('RequestVerificationToken', getAntiforgeryToken());
        xhr.send(fd);
    }

    // ---------- Đường mới: tus resumable ----------
    function uploadFileTus(file, folderId, onDone, onError, card, onProgress) {
        const bar = card.querySelector('.uc-bar');
        const status = card.querySelector('.uc-status');
        const cancelBtn = card.querySelector('.uc-cancel');

        let cancelled = false;
        let lastBytes = 0, lastTime = Date.now(), speed = 0;

        const setProgress = function (bytesUploaded, bytesTotal) {
            const now = Date.now();
            if (now - lastTime > 400) {
                speed = (bytesUploaded - lastBytes) * 1000 / (now - lastTime);
                lastBytes = bytesUploaded;
                lastTime = now;
            }
            const pct = bytesTotal ? Math.round(bytesUploaded / bytesTotal * 100) : 0;
            bar.style.width = pct + '%';
            status.textContent = pct + '%' + formatSpeed(speed);
            if (onProgress) onProgress(pct);
        };

        const failCard = function (msg) {
            bar.classList.add('uc-bar-error');
            status.textContent = msg;
            cancelBtn.textContent = '✕';
            window.toast?.error('Tải lên "' + file.name + '" thất bại: ' + msg);
            if (onError) onError(msg);
        };

        cancelBtn.addEventListener('click', () => {
            cancelled = true;
            try { upload.abort(); } catch (e) { /* chưa start */ }
            card.remove();
            activeCount--;
            processQueue();
        });

        // Poll TusStatus: PATCH cuối trả 204 khi byte đã nhận đủ, nhưng server còn
        // tạo Media row (+ poster ffmpeg). Kết quả thật nằm ở handler này.
        const pollStatus = function (tusId, attemptsLeft) {
            if (cancelled) return;
            if (attemptsLeft <= 0) {
                failCard('Hết thời gian chờ xử lý.');
                return;
            }
            fetch('/Admin/Media?handler=TusStatus&tusId=' + encodeURIComponent(tusId), {
                credentials: 'include',
                headers: { 'RequestVerificationToken': getAntiforgeryToken() }
            }).then(function (r) {
                if (r.status === 403) throw new Error('Không có quyền tải lên.');
                return r.json();
            }).then(function (json) {
                if (json.status === 'completed') {
                    clearResumeEntry(file);
                    bar.style.width = '100%';
                    bar.classList.add('uc-bar-done');
                    status.textContent = 'Xong';
                    cancelBtn.remove();
                    if (onDone) onDone(json);
                    setTimeout(() => card.remove(), 2500);
                    processQueue();
                } else if (json.status === 'failed') {
                    clearResumeEntry(file);
                    failCard(json.error || 'Xử lý thất bại.');
                    processQueue();
                } else {
                    setTimeout(() => pollStatus(tusId, attemptsLeft - 1), STATUS_POLL_INTERVAL);
                }
            }).catch(function (err) {
                failCard(err.message || 'Lỗi kiểm tra trạng thái.');
                processQueue();
            });
        };

        const options = {
            endpoint: TUS_ENDPOINT,
            chunkSize: TUS_CHUNK_SIZE,
            retryDelays: RETRY_DELAYS,
            metadata: {
                filename: file.name,
                filetype: file.type || 'application/octet-stream',
                folderId: folderId ? String(folderId) : ''
            },
            onProgress: setProgress,
            onShouldRetry: function (req, retryAttempt) { return !cancelled; },
            onSuccess: function () {
                activeCount--;
                bar.style.width = '100%';
                bar.classList.add('uc-bar-done');
                status.textContent = 'Đang xử lý…';
                if (onProgress) onProgress(100, 'processing');
                // tus URL dạng .../{id} — id cuối chính là TusFileId trong session table.
                const tusId = (upload.url || '').split('/').filter(Boolean).pop();
                if (tusId) {
                    pollStatus(tusId, STATUS_POLL_MAX);
                } else {
                    failCard('Không xác định được phiên upload.');
                }
                processQueue();
            },
            onError: function (error) {
                if (cancelled) return;
                activeCount--;
                let msg = 'Lỗi tải lên';
                if (error && error.originalResponse) {
                    const code = error.originalResponse.getStatus();
                    if (code === 401) msg = 'Phiên đăng nhập đã hết hạn.';
                    else if (code === 403) msg = 'Không có quyền tải lên.';
                    else if (code === 413) msg = 'File vượt quá dung lượng cho phép.';
                    else if (code === 400) msg = 'Loại file không được phép.';
                } else if (error && error.message) {
                    msg = error.message;
                }
                // Giữ localStorage entry: chọn lại đúng file này sẽ resume từ offset đã xong.
                failCard(msg);
                processQueue();
            }
        };

        // Resume từ phiên trước nếu có (refresh page / rớt mạng lâu).
        const savedUrl = findResumeUrl(file);
        if (savedUrl) {
            options.uploadUrl = savedUrl;
        }

        const upload = new tus.Upload(file, options);

        // tus-js-client cũng tự lưu fingerprint qua urlStorage khi có thể; ta tự lưu
        // thêm theo key riêng để chủ động resume sau refresh trang.
        upload.start();
        const watchUrl = setInterval(function () {
            if (cancelled || !upload.url) return;
            saveResumeEntry(file, upload.url);
            clearInterval(watchUrl);
        }, 200);
        setTimeout(function () { clearInterval(watchUrl); }, 60000);
    }

    function processQueue() {
        while (activeCount < MAX_CONCURRENT && queue.length > 0) {
            const item = queue.shift();
            activeCount++;
            if (item.useTus) {
                uploadFileTus(item.file, item.folderId, item.onDone, item.onError, item.card, item.onProgress);
            } else {
                uploadFileXhr(item.file, item.folderId, item.onDone, item.onError, item.xhr, item.card, item.onProgress);
            }
        }
    }

    const uploadManager = {
        enqueue(file, { folderId = null, onDone = null, onError = null, onProgress = null } = {}) {
            const c = getContainer();
            if (!c) {
                // Trang thiếu partial _UploadTracker — báo lỗi thay vì nuốt lặng,
                // nếu không caller đang await promise sẽ treo vĩnh viễn.
                if (onError) onError('Không khởi tạo được trình quản lý tải lên.');
                return;
            }

            const useTus = file.size > TUS_THRESHOLD && typeof window.tus !== 'undefined' && window.tus.Upload;

            if (useTus) sweepStaleResumeEntries();

            const { id, card } = createCard(file);
            c.appendChild(card);

            if (useTus) {
                queue.push({ file, folderId, onDone, onError, onProgress, useTus: true, card });
            } else {
                const xhr = new XMLHttpRequest();
                queue.push({ file, folderId, onDone, onError, onProgress, useTus: false, xhr, card });
            }
            processQueue();
        }
    };

    // Expose globally.
    window.uploadManager = uploadManager;

    // Inject tracker styles (self-contained, no external dependency).
    const style = document.createElement('style');
    style.textContent = `
        #upload-tracker {
            position: fixed; bottom: 20px; left: 20px; z-index: 9999;
            display: flex; flex-direction: column; gap: 8px;
            width: 280px; pointer-events: none;
        }
        .upload-card {
            background: #fff; border: 1px solid #e2e8f0; border-radius: 14px;
            padding: 12px 14px; box-shadow: 0 4px 18px rgba(0,0,0,.12);
            pointer-events: all; position: relative; font-size: 12px;
            font-family: inherit;
        }
        .uc-name { font-weight: 600; color: #1e293b; white-space: nowrap;
            overflow: hidden; text-overflow: ellipsis; max-width: 220px; }
        .uc-meta { color: #94a3b8; margin-bottom: 6px; }
        .uc-bar-wrap { background: #f1f5f9; border-radius: 99px; height: 5px; overflow: hidden; margin-bottom: 4px; }
        .uc-bar { height: 100%; background: #6366f1; border-radius: 99px; transition: width .15s linear; }
        .uc-bar-done { background: #10b981; }
        .uc-bar-error { background: #ef4444; }
        .uc-status { color: #64748b; font-size: 11px; }
        .uc-cancel {
            position: absolute; top: 8px; right: 10px; background: none; border: none;
            cursor: pointer; font-size: 13px; color: #94a3b8; padding: 2px 4px;
            line-height: 1; border-radius: 4px;
        }
        .uc-cancel:hover { color: #ef4444; background: #fee2e2; }
    `;
    document.head.appendChild(style);
})();
