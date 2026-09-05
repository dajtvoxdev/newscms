(function () {
    const maxChatMessages = 100;
    const storageKey = "keobia2026-profile";
    const publicKeyStorageKey = "keobia2026-public-key";
    const shell = document.querySelector(".canvas-shell");
    const heroToastStack = document.getElementById("heroToastStack");
    const toastStack = document.getElementById("toastStack");
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    let heroToastTimer = 0;
    const cropperState = {
        cropper: null,
        objectUrl: "",
        pendingAvatar: ""
    };
    const chatState = {
        isOpen: false,
        emojiOpen: false,
        pendingImageFile: null,
        pendingImageUrl: "",
        unread: 0
    };
    const historyState = {
        matchId: "",
        matchLabel: "",
        choice: ""
    };
    const analysisRequests = new Map();

    if (!shell) {
        return;
    }

    const currentUnitCode = shell.dataset.currentUnit === "peanut" ? "peanut" : "beer";

    function getUnitMeta(unitCode) {
        return unitCode === "peanut"
            ? { code: "peanut", short: "gói", full: "gói lạc", emoji: "🥜" }
            : { code: "beer", short: "cốc", full: "cốc bia", emoji: "🍺" };
    }

    function getCardUnit(card) {
        return getUnitMeta(card && card.dataset.unitCode === "peanut" ? "peanut" : "beer");
    }

    function formatUnitCount(value, unitCode, useFullLabel) {
        const unit = getUnitMeta(unitCode);
        return `${formatStatNumber(value)} ${useFullLabel ? unit.full : unit.short}`;
    }

    function formatUnitBreakdown(beerCups, peanutPacks) {
        const parts = [];
        const beer = Math.max(0, Number(beerCups) || 0);
        const peanut = Math.max(0, Number(peanutPacks) || 0);
        if (beer > 0) parts.push(`${formatStatNumber(beer)} cốc bia`);
        if (peanut > 0) parts.push(`${formatStatNumber(peanut)} gói lạc`);
        return parts.length > 0 ? parts.join(" + ") : "0 cốc bia + 0 gói lạc";
    }

    // Global client reset: bump KeoBia:ClientResetToken in config to force every visitor
    // to drop their cached profile/publicKey and re-authenticate with Telegram.
    const clientResetToken = shell.getAttribute("data-client-reset") || "";
    if (clientResetToken) {
        try {
            if (localStorage.getItem("keobia2026-reset") !== clientResetToken) {
                localStorage.removeItem(storageKey);
                localStorage.removeItem(publicKeyStorageKey);
                localStorage.setItem("keobia2026-reset", clientResetToken);
            }
        } catch (e) {
            /* localStorage unavailable */
        }
    }

    const savedProfile = loadProfile();
    const shouldOpenProfileOnStart = !savedProfile;

    const state = {
        profile: savedProfile || getDefaultProfile(),
        selected: {},
        cups: {},
        weights: {},
        myPicks: {},
        hopeStars: 0,
        devilStars: 0,
        playerStats: getDefaultPlayerStats(),
        playerHistory: [],
        feed: [],
        chat: []
    };
    const realtimeState = {
        connection: null,
        seenJoinKeys: new Set(savedProfile && savedProfile.playerId ? [String(savedProfile.playerId)] : []),
        seenActivityIds: new Set(),
        seenChatIds: new Set()
    };

    function loadProfile() {
        try {
            const raw = localStorage.getItem(storageKey);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function getDefaultAvatar() {
        const firstAvatar = document.querySelector("[data-avatar-option]");
        return firstAvatar ? firstAvatar.getAttribute("data-avatar") : "";
    }

    function getDefaultProfile() {
        return {
            publicKey: getPublicKey(),
            name: "MinhBia18",
            avatar: getDefaultAvatar()
        };
    }

    function getDefaultPlayerStats() {
        return {
            totalBets: 0,
            totalCups: 0,
            wonCups: 0,
            lostCups: 0,
            pendingCups: 0,
            correctBets: 0,
            wrongBets: 0,
            paidBeerCups: 0,
            quizRewardCups: 0,
            quizPenaltyCups: 0,
            correctScorePenaltyCups: 0,
            correctScoreRewardCups: 0,
            outstandingBeerCups: 0,
            outstandingPeanutPacks: 0,
            paidLegacyBeerCups: 0,
            paidPeanutPacks: 0,
            totalBeerCups: 0,
            totalPeanutPacks: 0,
            pendingBeerCups: 0,
            pendingPeanutPacks: 0,
            promoBeerCups: 0,
            promoPeanutPacks: 0,
            hopeStarBeerCupEffect: 0,
            hopeStarPeanutPackEffect: 0,
            devilStarBeerCupEffect: 0,
            devilStarPeanutPackEffect: 0,
            quizRewardBeerCups: 0,
            quizRewardPeanutPacks: 0,
            quizPenaltyBeerCups: 0,
            quizPenaltyPeanutPacks: 0,
            correctScorePenaltyBeerCups: 0,
            correctScorePenaltyPeanutPacks: 0,
            correctScoreRewardBeerCups: 0,
            correctScoreRewardPeanutPacks: 0
        };
    }

    function getPublicKey() {
        let key = localStorage.getItem(publicKeyStorageKey);
        if (!key) {
            key = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
            localStorage.setItem(publicKeyStorageKey, key);
        }
        return key;
    }

    function isInlineAvatar(value) {
        return typeof value === "string" && value.startsWith("data:image/");
    }

    function normalizeAvatarForPayload(value) {
        return isInlineAvatar(value) ? null : value;
    }

    async function uploadAvatarDataUrl(dataUrl) {
        const avatarResponse = await fetch(dataUrl);
        const avatarBlob = await avatarResponse.blob();
        if (!avatarBlob || avatarBlob.size === 0) {
            throw new Error("Ảnh avatar bị rỗng.");
        }

        const ext = avatarBlob.type === "image/jpeg"
            ? "jpg"
            : avatarBlob.type === "image/webp"
                ? "webp"
                : "png";
        const formData = new FormData();
        formData.append("file", avatarBlob, `keobia-avatar.${ext}`);

        const response = await fetch("/keobia/avatar", {
            method: "POST",
            body: formData
        });

        if (response.ok) {
            const payload = await response.json().catch(() => ({}));
            if (payload && payload.location) {
                return payload.location;
            }
        }

        let message = "Không táº£i Ä‘Æ°á»£c avatar lên server.";
        try {
            const payload = await response.json();
            if (payload && payload.error) {
                message = payload.error;
            }
        } catch {
            // Keep default error message.
        }

        throw new Error(message);
    }

    async function uploadChatImageFile(file) {
        if (!file) {
            throw new Error("Chưa chọn ảnh chat.");
        }

        const formData = new FormData();
        formData.append("file", file, file.name || "keobia-chat.png");

        const response = await fetch("/keobia/chat-image", {
            method: "POST",
            body: formData
        });

        if (response.ok) {
            const payload = await response.json().catch(() => ({}));
            if (payload && payload.location) {
                return payload.location;
            }
        }

        let message = "Không tải được ảnh chat lên server.";
        try {
            const payload = await response.json();
            if (payload && payload.error) {
                message = payload.error;
            }
        } catch {
            // Keep default message.
        }

        throw new Error(message);
    }

    function persistProfileLocally(profile) {
        state.profile = profile;
        if (profile && profile.playerId) {
            realtimeState.seenJoinKeys.add(String(profile.playerId));
        }
        localStorage.setItem(storageKey, JSON.stringify(profile));
        localStorage.setItem(publicKeyStorageKey, profile.publicKey);
        renderProfile();
        void checkUnitTransitionNotice(true);
    }

    async function saveProfile(profile, options = {}) {
        const nextProfile = {
            ...profile,
            publicKey: profile.publicKey || getPublicKey()
        };
        const claimExisting = Boolean(options.claimExisting);
        const claimPlayerId = options.claimPlayerId || null;

        try {
            if (isInlineAvatar(nextProfile.avatar)) {
                nextProfile.avatar = await uploadAvatarDataUrl(nextProfile.avatar);
            }
        } catch (error) {
            showToast("Chưa tải được avatar", error.message || "Ảnh crop chưa được lưu lên server.");
            return;
        }

        try {
            const response = await postJson("/keobia/profile", {
                publicKey: nextProfile.publicKey,
                displayName: nextProfile.name,
                avatarUrl: normalizeAvatarForPayload(nextProfile.avatar),
                claimExisting,
                claimPlayerId
            });

            if (response && response.requiresClaimConfirmation && response.claimCandidate) {
                const confirmed = window.confirm(
                    `${response.claimCandidate.displayName} đã tồn tại.\n\nCó phải bạn là người chơi này không?\n\nChọn OK để nhận lại hồ sơ cũ, hoặc Cancel để đặt tên khác.`
                );

                if (!confirmed) {
                    showToast("Tên đã tồn tại", "Hãy chọn một tên khác hoặc xác nhận đúng hồ sơ của bạn.");
                    return;
                }

                await saveProfile(nextProfile, {
                    claimExisting: true,
                    claimPlayerId: response.claimCandidate.playerId
                });
                return;
            }

            const resolvedProfile = {
                publicKey: response && response.publicKey ? response.publicKey : nextProfile.publicKey,
                playerId: response && response.playerId ? response.playerId : nextProfile.playerId,
                name: response && response.displayName ? response.displayName : nextProfile.name,
                avatar: response && response.avatarUrl ? response.avatarUrl : nextProfile.avatar,
                telegramId: (response && response.telegramUserId) || nextProfile.telegramId || null,
                telegramUsername: (response && response.telegramUsername) || nextProfile.telegramUsername || null
            };

            persistProfileLocally(resolvedProfile);
            void refreshMyPredictions();
            void refreshPlayerHistory();
            resetCropper();
            closeModal("profile");

            if (response && response.isClaimedPlayer) {
                showToast("Đã nhận lại hồ sơ", `${resolvedProfile.name} đã được gán lại trên thiết bị này.`);
                return;
            }

            showToast("Đã cập nhật hồ sơ", `${resolvedProfile.name} đã sẵn sàng dự đoán.`);
        } catch (error) {
            showToast("Chưa tải được lên server", error.message || "Hồ sơ chưa được cập nhật trên server.");
        }
    }

    // Called by the Telegram Login Widget (data-onauth) with the signed user payload.
    window.onTelegramAuth = async function (user) {
        if (!user || !user.id) {
            return;
        }
        const fields = {};
        Object.keys(user).forEach((key) => {
            const value = user[key];
            if (value !== undefined && value !== null && value !== "") {
                fields[key] = String(value);
            }
        });

        const profile = state.profile || getDefaultProfile();
        try {
            const response = await postJson("/keobia/telegram", {
                publicKey: profile.publicKey || getPublicKey(),
                displayName: profile.name,
                avatarUrl: normalizeAvatarForPayload(profile.avatar),
                telegram: fields
            });

            const next = {
                ...profile,
                publicKey: response.publicKey || profile.publicKey,
                playerId: response.playerId || profile.playerId,
                name: response.displayName || profile.name,
                avatar: response.avatarUrl || profile.avatar,
                telegramId: response.telegramUserId || null,
                telegramUsername: response.telegramUsername || null
            };
            persistProfileLocally(next);
            void refreshPlayerHistory();
            showToast(
                "Đã xác thực Telegram",
                next.telegramUsername ? `@${next.telegramUsername} đã sẵn sàng đặt kèo.` : "Bạn đã có thể đặt kèo.");
        } catch (error) {
            showToast("Xác thực Telegram thất bại", error.message || "Vui lòng thử lại.");
        }
    };

    // Apply a server-confirmed Telegram link (shared by widget + OIDC popup).
    function applyTelegramLinkResult(response) {
        const profile = state.profile || getDefaultProfile();
        const next = {
            ...profile,
            publicKey: response.publicKey || profile.publicKey,
            playerId: response.playerId || profile.playerId,
            name: response.displayName || profile.name,
            avatar: response.avatarUrl || profile.avatar,
            telegramId: response.telegramUserId || null,
            telegramUsername: response.telegramUsername || null
        };
        persistProfileLocally(next);
        void refreshPlayerHistory();
        showToast(
            "Đã xác thực Telegram",
            next.telegramUsername ? `@${next.telegramUsername} đã sẵn sàng đặt kèo.` : "Bạn đã có thể đặt kèo.");
    }

    // OIDC redirect login opened in a popup; the callback page posts the result back.
    function openTelegramOidcLogin() {
        const profile = state.profile || getDefaultProfile();
        if (!profile.name) {
            showToast("Cần tên hiển thị", "Hãy đặt tên hiển thị trong hồ sơ trước khi đăng nhập Telegram.");
            return;
        }
        const params = new URLSearchParams({
            publicKey: profile.publicKey || getPublicKey(),
            displayName: profile.name,
            avatarUrl: normalizeAvatarForPayload(profile.avatar) || ""
        });
        const w = 520;
        const h = 640;
        const left = Math.max(0, Math.round((window.screen.width - w) / 2));
        const top = Math.max(0, Math.round((window.screen.height - h) / 2));
        window.open(
            "/keobia/telegram/login?" + params.toString(),
            "keobia-telegram-oidc",
            `width=${w},height=${h},left=${left},top=${top}`);
    }

    window.addEventListener("message", (event) => {
        if (event.origin !== window.location.origin) {
            return;
        }
        const data = event.data;
        if (!data || data.source !== "keobia-telegram-oidc") {
            return;
        }
        if (data.ok) {
            applyTelegramLinkResult(data);
        } else {
            showToast("Xác thực Telegram thất bại", data.error || "Vui lòng thử lại.");
        }
    });

    document.addEventListener("click", (event) => {
        const trigger = event.target.closest && event.target.closest("[data-tg-oidc-login]");
        if (!trigger) {
            return;
        }
        event.preventDefault();
        openTelegramOidcLogin();
    });

    async function ensureResolvedProfile(actionLabel) {
        const profile = state.profile;
        if (!profile || !profile.name) {
            showToast("Thiếu hồ sơ", `Hãy lưu hồ sơ người chơi trước khi ${actionLabel}.`);
            openModal("profile");
            return null;
        }

        const requestProfile = (publicKey, claimExisting = false, claimPlayerId = null) => postJson("/keobia/profile", {
            publicKey,
            displayName: profile.name,
            avatarUrl: normalizeAvatarForPayload(profile.avatar),
            claimExisting,
            claimPlayerId
        });

        let response;
        let workingPublicKey = profile.publicKey || getPublicKey();
        try {
            try {
                response = await requestProfile(workingPublicKey);
            } catch (error) {
                const message = String(error && error.message ? error.message : "");
                if (!message.includes("đã tồn tại")) {
                    throw error;
                }

                workingPublicKey = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
                response = await requestProfile(workingPublicKey);
            }

            if (response && response.requiresClaimConfirmation && response.claimCandidate) {
                const confirmed = window.confirm(
                    `${response.claimCandidate.displayName} đã tồn tại.\n\nCó phải bạn là người chơi này không?\n\nChọn OK để nhận lại hồ sơ cũ, hoặc Cancel để đặt tên khác.`
                );

                if (!confirmed) {
                    showToast("Tên đã tồn tại", "Hãy mở hồ sơ và chọn tên khác hoặc xác nhận đúng hồ sơ của bạn.");
                    openModal("profile");
                    return null;
                }

                response = await requestProfile(workingPublicKey, true, response.claimCandidate.playerId);
            }
        } catch (error) {
            showToast("Chưa xác nhận được hồ sơ", error.message || "Vui lòng mở hồ sơ người chơi và thử lại.");
            openModal("profile");
            return null;
        }

        if (!response || !response.playerId) {
            showToast("Chưa xác nhận được hồ sơ", "Vui lòng mở hồ sơ người chơi và thử lại.");
            openModal("profile");
            return null;
        }

        const resolvedProfile = {
            publicKey: response.publicKey || workingPublicKey,
            playerId: response.playerId,
            name: response.displayName || profile.name,
            avatar: response.avatarUrl || profile.avatar,
            telegramId: response.telegramUserId || profile.telegramId || null,
            telegramUsername: response.telegramUsername || profile.telegramUsername || null
        };

        persistProfileLocally(resolvedProfile);
        return resolvedProfile;
    }

    function normalizePercents(weights) {
        const total = weights.home + weights.draw + weights.away;
        const raw = [
            { key: "home", value: (weights.home / total) * 100 },
            { key: "draw", value: (weights.draw / total) * 100 },
            { key: "away", value: (weights.away / total) * 100 }
        ];
        const rounded = Object.fromEntries(raw.map((item) => [item.key, Math.floor(item.value)]));
        let remainder = 100 - (rounded.home + rounded.draw + rounded.away);
        raw
            .sort((a, b) => (b.value - Math.floor(b.value)) - (a.value - Math.floor(a.value)))
            .forEach((item) => {
                if (remainder > 0) {
                    rounded[item.key] += 1;
                    remainder -= 1;
                }
            });
        return rounded;
    }

    function getChoiceLabel(card, choice) {
        if (choice === "home") {
            return card.dataset.homeName;
        }
        if (choice === "away") {
            return card.dataset.awayName;
        }
        return "Hòa";
    }

    function getChoiceClass(choice) {
        if (choice === "away") {
            return "choice-away";
        }
        if (choice === "draw") {
            return "choice-draw";
        }
        return "choice-home";
    }

    function getCropperConstructor() {
        const exported = window.Cropper;
        if (typeof exported === "function") {
            return exported;
        }
        if (exported && typeof exported.default === "function") {
            return exported.default;
        }
        if (exported && typeof exported.Cropper === "function") {
            return exported.Cropper;
        }
        return null;
    }

    function destroyCropperInstance(cropper) {
        if (!cropper) {
            return;
        }
        if (typeof cropper.destroy === "function") {
            cropper.destroy();
            return;
        }

        const cropperCanvas = typeof cropper.getCropperCanvas === "function" ? cropper.getCropperCanvas() : null;
        if (cropperCanvas) {
            cropperCanvas.remove();
        }
    }

    function resetCropper() {
        destroyCropperInstance(cropperState.cropper);
        cropperState.cropper = null;

        if (cropperState.objectUrl) {
            URL.revokeObjectURL(cropperState.objectUrl);
            cropperState.objectUrl = "";
        }

        const cropperShell = shell.querySelector("[data-cropper-shell]");
        const image = shell.querySelector("[data-cropper-image]");
        const upload = shell.querySelector("[data-avatar-upload]");

        if (cropperShell) {
            cropperShell.hidden = true;
        }
        if (image) {
            image.onload = null;
            image.onerror = null;
            image.removeAttribute("style");
            image.removeAttribute("src");
        }
        if (upload) {
            upload.value = "";
        }
    }

    function setPreviewAvatar(avatar) {
        const preview = shell.querySelector("[data-profile-preview]");
        if (preview && avatar) {
            preview.setAttribute("src", avatar);
        }
    }

    function setPendingAvatar(avatar) {
        cropperState.pendingAvatar = avatar;
        setPreviewAvatar(avatar);

        shell.querySelectorAll("[data-avatar-option]").forEach((option) => {
            option.classList.toggle("is-selected", option.getAttribute("data-avatar") === avatar);
        });
    }

    function renderProfile() {
        const profile = state.profile || getDefaultProfile();
        cropperState.pendingAvatar = profile.avatar;

        document.querySelectorAll("[data-player-name]").forEach((node) => {
            node.textContent = profile.name;
        });
        document.querySelectorAll("[data-player-avatar]").forEach((node) => {
            node.setAttribute("src", profile.avatar);
        });
        document.querySelectorAll("[data-profile-preview]").forEach((node) => {
            node.setAttribute("src", profile.avatar);
        });
        document.querySelectorAll("[data-player-input]").forEach((node) => {
            node.value = profile.name;
        });
        document.querySelectorAll("[data-avatar-option]").forEach((option) => {
            option.classList.toggle("is-selected", option.getAttribute("data-avatar") === profile.avatar);
        });
        renderPlayerRank(profile);
        renderTelegramStatus(profile);
        document.querySelectorAll("[data-stop-playing]").forEach((button) => {
            const canStopPlaying = Boolean(profile.playerId) && !profile.isBlocked;
            button.hidden = !canStopPlaying;
            button.disabled = !canStopPlaying;
        });
        renderPlayerStats();
    }

    function renderPlayerRank(profile) {
        const username = profile && profile.telegramUsername ? String(profile.telegramUsername).trim() : "";
        document.querySelectorAll("[data-player-rank]").forEach((node) => {
            node.classList.toggle("is-telegram", Boolean(username));
        });
        document.querySelectorAll("[data-player-rank-text]").forEach((node) => {
            node.textContent = username ? `@${username}` : "Hạng Bia Thủ";
        });
    }

    function renderTelegramStatus(profile) {
        const verified = Boolean(profile && profile.telegramId);
        document.querySelectorAll("[data-tg-status]").forEach((node) => {
            node.setAttribute("data-tg-state", verified ? "verified" : "none");
            const icon = node.querySelector(".material-symbols-rounded");
            if (icon) {
                icon.textContent = verified ? "verified" : "cancel";
            }
            const text = node.querySelector("[data-tg-status-text]");
            if (text) {
                text.textContent = verified
                    ? (profile.telegramUsername ? `Đã xác thực: @${profile.telegramUsername}` : "Đã xác thực Telegram")
                    : "Chưa xác thực Telegram";
            }
        });
        // Already verified → no need to show the login button / hint anymore.
        document.querySelectorAll("[data-tg-login]").forEach((node) => {
            node.hidden = verified;
        });
    }

    function formatStatNumber(value) {
        const number = Number(value || 0);
        if (!Number.isFinite(number)) {
            return "0";
        }

        return new Intl.NumberFormat("vi-VN").format(number);
    }

    function renderPlayerStats() {
        const stats = {
            ...getDefaultPlayerStats(),
            ...(state.playerStats || {})
        };
        document.querySelectorAll("[data-player-stat]").forEach((node) => {
            const key = node.getAttribute("data-player-stat");
            node.textContent = formatStatNumber(stats[key]);
        });
    }

    // Keep the currently running Razor view in sync until its next deployment.
    function syncContributionCopy() {
        document.querySelectorAll(".mc-unit-badge").forEach((badge) => badge.remove());

        const profileStats = [
            ["outstandingBeerCups", "paidLegacyBeerCups", "Cốc bia đã nộp"],
            ["outstandingPeanutPacks", "paidPeanutPacks", "Gói lạc đã nộp"]
        ];

        profileStats.forEach(([legacyKey, paidKey, label]) => {
            document.querySelectorAll(`[data-player-stat="${legacyKey}"]`).forEach((node) => {
                node.setAttribute("data-player-stat", paidKey);
                const labelNode = node.parentElement && node.parentElement.querySelector(".hp-lbl");
                if (labelNode) labelNode.textContent = label;
            });
        });

        [
            ["[data-lossboard-total-beer]", "cốc bia"],
            ["[data-lossboard-total-peanut]", "gói lạc"]
        ].forEach(([selector, label]) => {
            document.querySelectorAll(selector).forEach((total) => {
                const labelNode = total.nextElementSibling;
                if (labelNode) labelNode.textContent = label;
            });
        });
    }

    function getApiData(response) {
        return response && response.data && typeof response.data === "object"
            ? response.data
            : response;
    }

    function applyShareBeerResult(response, fromPublicKey) {
        const data = getApiData(response);
        if (!data) {
            return null;
        }

        if (Number.isFinite(Number(data.fromNewLostCups))) {
            if (state.profile && state.profile.publicKey === fromPublicKey) {
                state.playerStats = {
                    ...getDefaultPlayerStats(),
                    ...(state.playerStats || {}),
                    lostCups: Number(data.fromNewLostCups),
                    outstandingBeerCups: Number(data.fromLostBeerCups) || 0,
                    outstandingPeanutPacks: Number(data.fromLostPeanutPacks) || 0
                };
                renderPlayerStats();
            }
        }

        // A gift changes the outstanding balance, not the historical total lost.
        return data;
    }

    function handleShareBeer(payload) {
        if (!payload) {
            return;
        }

        applyShareBeerResult(payload, payload.fromPublicKey);
    }

    function applyPlayerHistoryPayload(data) {
        const summary = {
            ...getDefaultPlayerStats(),
            ...((data && data.summary) || {})
        };
        state.playerStats = summary;
        state.playerHistory = Array.isArray(data && data.items) ? data.items : [];
        renderPlayerStats();
        return {
            ...data,
            summary,
            items: state.playerHistory
        };
    }

    async function refreshPlayerHistory(options = {}) {
        const publicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
        if (!publicKey) {
            state.playerStats = getDefaultPlayerStats();
            state.playerHistory = [];
            renderPlayerStats();
            if (options.renderModal) {
                renderPlayerHistory({ summary: state.playerStats, items: [], isOwnHistory: true });
            }
            return null;
        }

        try {
            const data = await postJson("/keobia/player-history", { publicKey });
            const payload = applyPlayerHistoryPayload(data);
            if (options.renderModal) {
                renderPlayerHistory({ ...payload, isOwnHistory: true });
            }
            return payload;
        } catch (error) {
            state.playerStats = getDefaultPlayerStats();
            state.playerHistory = [];
            renderPlayerStats();
            if (options.renderModal) {
                renderPlayerHistory({ summary: state.playerStats, items: [], error: error.message, isOwnHistory: true });
            }
            return null;
        }
    }

    // Adjust the "X ngÆ°á»i Ä‘áº·t â€¦" community counter for a match/outcome by delta.
    function bumpBettorCount(matchId, choice, delta) {
        document
            .querySelectorAll(`.match-card[data-match-id="${matchId}"] [data-bettors="${choice}"]`)
            .forEach((node) => {
                const current = Number(node.textContent) || 0;
                node.textContent = String(Math.max(0, current + delta));
            });
    }

    function renderMatches() {
        document.querySelectorAll(".match-card").forEach((card) => {
            const id = card.dataset.matchId;
            const percents = normalizePercents(state.weights[id]);
            const unit = getCardUnit(card);

            card.querySelectorAll("[data-meter]").forEach((bar) => {
                const key = bar.getAttribute("data-meter");
                bar.style.width = `${percents[key]}%`;
            });

            card.querySelectorAll("[data-pct]").forEach((node) => {
                const key = node.getAttribute("data-pct");
                node.textContent = `${percents[key]}%`;
            });

            card.querySelectorAll("[data-choice-button]").forEach((button) => {
                const choice = button.getAttribute("data-choice");
                const myCups = Number((state.myPicks[id] && state.myPicks[id][choice]) || 0);
                const badge = button.querySelector("[data-my-pick]");
                button.classList.toggle("sel", choice === state.selected[id]);
                button.classList.toggle("has-my-pick", myCups > 0);
                if (badge) {
                    var starType = state.myPicks[id] ? state.myPicks[id].starType : null;
                    var starIcon = starType === "hope" ? " ⭐" : starType === "devil" ? " 😈" : "";
                    var scoreText = state.myPicks[id] && state.myPicks[id].predictedHomeScore !== undefined
                        ? ` · ${state.myPicks[id].predictedHomeScore}-${state.myPicks[id].predictedAwayScore}`
                        : "";
                    badge.hidden = myCups <= 0;
                    badge.textContent = myCups > 0 ? `Bạn ${myCups} ${unit.short}${starIcon}${scoreText}` : "";
                }
            });

        });
    }

    // Toggle vote-lock UI from each card's kickoff time. Re-run on a timer so a
    // card crossing the 20-minute boundary while the page is open locks live.
    function refreshLocks() {
        const now = Date.now();
        const DAY = 86400000;
        const LOCK = 1200000;
        document.querySelectorAll(".match-card[data-kickoff]").forEach((card) => {
            const ko = Date.parse(card.dataset.kickoff || "");
            if (Number.isNaN(ko)) {
                return;
            }
            const vState = now < ko - DAY ? "pending" : now >= ko - LOCK ? "locked" : "open";
            card.classList.toggle("mc-pending", vState === "pending");
            card.classList.toggle("mc-locked", vState === "locked");
            const label = card.querySelector("[data-mc-lock-text]");
            if (label) {
                label.textContent = vState === "pending" ? "Chưa mở kèo" : "Đã khóa kèo";
            }
        });
    }

    function isCardLocked(card) {
        return card.classList.contains("mc-locked") || card.classList.contains("mc-pending");
    }

    function usesAiWeights(matchId) {
        const card = document.querySelector(`.match-card[data-match-id="${matchId}"]`);
        if (!card) {
            return false;
        }

        return Number(card.dataset.aiHome) > 0
            || Number(card.dataset.aiDraw) > 0
            || Number(card.dataset.aiAway) > 0;
    }

    function renderFeed() {
        const content = state.feed.map((item) => `
            <article class="notif">
                <span class="notif-ava">
                    <img src="${escapeHtml(item.avatar)}" alt="" />
                    <span class="material-symbols-rounded notif-badge">${escapeHtml(item.badge || "sports_bar")}</span>
                </span>
                <span class="notif-main">
                    <span class="notif-line">${escapeHtml(item.text)}</span>
                    <span class="notif-sub">
                        <span class="choice ${escapeHtml(getChoiceClass(item.choice))}">${escapeHtml(item.choiceLabel || "Dự Ä‘oán")}</span>
                        <span class="notif-time">${escapeHtml(item.time)}</span>
                    </span>
                </span>
            </article>
        `).join("");

        document.querySelectorAll("[data-feed-list]").forEach((list) => {
            list.innerHTML = content;
        });
    }

    function prependFeedItem(item) {
        const id = item && item.id ? String(item.id) : "";
        if (id) {
            if (realtimeState.seenActivityIds.has(id)) {
                return false;
            }
            realtimeState.seenActivityIds.add(id);
        }

        state.feed.unshift(item);
        state.feed = state.feed.slice(0, 100);
        renderFeed();
        return true;
    }

    function removeFeedItems(ids) {
        const values = Array.isArray(ids) ? ids.map((id) => String(id)).filter(Boolean) : [];
        if (values.length === 0) {
            return false;
        }

        const idSet = new Set(values);
        const before = state.feed.length;
        state.feed = state.feed.filter((item) => !idSet.has(String(item.id || "")));
        values.forEach((id) => realtimeState.seenActivityIds.delete(id));
        if (state.feed.length !== before) {
            renderFeed();
            return true;
        }

        return false;
    }

    function handleActivityAdded(payload) {
        if (!payload || !payload.text) {
            return;
        }

        prependFeedItem({
            id: payload.id ? String(payload.id) : "",
            avatar: payload.avatarUrl || getDefaultAvatar(),
            text: payload.text,
            time: "Vừa xong",
            choice: payload.choice || "home",
            choiceLabel: payload.choiceLabel || "Dự đoán",
            badge: payload.badge || "sports_bar"
        });
    }

    function handleActivityRemoved(payload) {
        if (!payload) {
            return;
        }

        const ids = Array.isArray(payload.ids)
            ? payload.ids
            : payload.id
                ? [payload.id]
                : [];
        removeFeedItems(ids);
    }

    function mergeMyPick(matchId, choice, cups) {
        if (!matchId || !choice || !Number.isFinite(Number(cups))) {
            return;
        }

        state.myPicks[matchId] = state.myPicks[matchId] || {};
        state.myPicks[matchId][choice] = Number(state.myPicks[matchId][choice] || 0) + Number(cups);
    }

    function removeMyPick(matchId, choice, cups) {
        if (!matchId || !choice || !Number.isFinite(Number(cups)) || !state.myPicks[matchId]) {
            return;
        }

        const next = Math.max(0, Number(state.myPicks[matchId][choice] || 0) - Number(cups));
        if (next > 0) {
            state.myPicks[matchId][choice] = next;
            return;
        }

        delete state.myPicks[matchId][choice];
        if (Object.keys(state.myPicks[matchId]).length === 0) {
            delete state.myPicks[matchId];
        }
    }

    function setMyPickScore(matchId, item) {
        if (!matchId || !item || !state.myPicks[matchId]) return;
        const home = item.predictedHomeScore;
        const away = item.predictedAwayScore;
        if (home === null || home === undefined || away === null || away === undefined) return;
        state.myPicks[matchId].predictedHomeScore = Number(home);
        state.myPicks[matchId].predictedAwayScore = Number(away);
        state.myPicks[matchId].correctScoreOdds = item.correctScoreOdds || null;
    }

    async function refreshMyPredictions() {
        const publicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
        if (!publicKey) {
            return;
        }

        try {
            const data = await postJson("/keobia/my-predictions", { publicKey });
            state.myPicks = {};
            (data.items || []).forEach((item) => {
                mergeMyPick(String(item.matchId), item.choice, Number(item.cups));
                if (item.starType && state.myPicks[String(item.matchId)]) {
                    state.myPicks[String(item.matchId)].starType = item.starType;
                }
                setMyPickScore(String(item.matchId), item);
            });
            state.hopeStars = Number(data.hopeStars) || 0;
            state.devilStars = Number(data.devilStars) || 0;
            renderMatches();
            renderStarItems();
        } catch {
            // Keep the local view usable if the summary request fails.
        }
    }

    function renderStarItems() {
        document.querySelectorAll("[data-match-items]").forEach(function(container) {
            var hopeBtn = container.querySelector('[data-star-type="hope"]');
            var devilBtn = container.querySelector('[data-star-type="devil"]');
            if (hopeBtn) {
                var hc = Math.max(0, state.hopeStars || 0);
                hopeBtn.setAttribute("data-star-count", hc);
                hopeBtn.disabled = hc <= 0;
                hopeBtn.querySelector(".mc-item-badge").textContent = hc > 0 ? "x" + hc : "0";
                hopeBtn.classList.toggle("mc-item--hope", true);
            }
            if (devilBtn) {
                var dc = Math.max(0, state.devilStars || 0);
                devilBtn.setAttribute("data-star-count", dc);
                devilBtn.disabled = dc <= 0;
                devilBtn.querySelector(".mc-item-badge").textContent = dc > 0 ? "x" + dc : "0";
                devilBtn.classList.toggle("mc-item--devil", true);
            }
        });
    }

    function formatHistoryTime(value) {
        const raw = String(value || "");
        const date = new Date(/[zZ]|[+-]\d{2}:?\d{2}$/.test(raw) ? raw : `${raw}Z`);
        if (Number.isNaN(date.getTime())) {
            return "Vừa xong";
        }

        return date.toLocaleString("vi-VN", {
            timeZone: "Asia/Ho_Chi_Minh",
            day: "2-digit",
            month: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            hour12: false
        });
    }

    function getCupLogIconLabel(type) {
        switch (type) {
            case "wrong_bet": return "Thua";
            case "correct_bet": return "Dung";
            case "missed_match": return "Miss";
            case "missed_credit": return "KM";
            case "shared_beer": return "Tang";
            case "received_beer": return "Nhan";
            case "used_hope_star": return "SHV";
            case "used_devil_star": return "SMQ";
            case "hope_star_win": return "+SHV";
            case "hope_star_lose": return "-SHV";
            case "devil_star_win": return "+SMQ";
            case "devil_star_lose": return "-SMQ";
            case "admin_adjust": return "Admin";
            case "quiz_correct": return "Quiz";
            case "quiz_wrong": return "Quiz sai";
            case "correct_score": return "Tỉ số";
            default: return "-";
        }
    }

    function getCupLogReason(log) {
        if (log.reason) return log.reason;
        const unit = getUnitMeta(log.unitCode);
        switch (log.changeType) {
            case "wrong_bet": return "Kèo sai";
            case "missed_match": return "Không tham gia";
            case "missed_credit": return "Khuyến mãi nhà cái";
            case "shared_beer": return `Tặng ${unit.full}`;
            case "received_beer": return `Nhận ${unit.full}`;
            case "used_hope_star": return "Dùng Ngôi Sao Hi Vọng";
            case "used_devil_star": return "Dùng Ngôi Sao Ma Quỷ";
            case "quiz_correct": return "Trả lời đúng";
            case "quiz_wrong": return "Trả lời sai";
            case "correct_score": return log.cups < 0 ? "Đúng tỉ số" : "Sai tỉ số";
            case "admin_adjust": return "Điều chỉnh";
            default: return `Thay đổi ${unit.full}`;
        }
    }

    function getPlayerHistoryResult(item) {
        if (!item || !item.isSettled) {
            return { text: "Chờ kết quả", className: "pending" };
        }

        return item.isCorrect === true
            ? { text: "Đúng dự đoán", className: "win" }
            : { text: "Mất cược", className: "lost" };
    }

    function renderPlayerHistory(data) {
        const payload = data || {};
        const isOwnHistory = payload.isOwnHistory === true;
        const summary = {
            ...getDefaultPlayerStats(),
            ...(payload.summary || {})
        };
        const totalBets = document.querySelector("[data-player-history-total-bets]");
        const totalBeerEl = document.querySelector("[data-player-history-total-beer]");
        const totalPeanutEl = document.querySelector("[data-player-history-total-peanut]");
        const pendingBeerEl = document.querySelector("[data-player-history-pending-beer]");
        const pendingPeanutEl = document.querySelector("[data-player-history-pending-peanut]");
        const wrongCupsEl = document.querySelector("[data-player-history-wrong-cups]");
        const missedEl = document.querySelector("[data-player-history-missed]");
        const starHopeBeerEl = document.querySelector("[data-player-history-star-hope-beer]");
        const starHopePeanutEl = document.querySelector("[data-player-history-star-hope-peanut]");
        const starDevilBeerEl = document.querySelector("[data-player-history-star-devil-beer]");
        const starDevilPeanutEl = document.querySelector("[data-player-history-star-devil-peanut]");
        const promoBeerEl = document.querySelector("[data-player-history-promo-beer]");
        const promoPeanutEl = document.querySelector("[data-player-history-promo-peanut]");
        const paidLegacyBeerEl = document.querySelector("[data-player-history-paid-legacy-beer]");
        const paidPeanutEl = document.querySelector("[data-player-history-paid-peanut]");
        const quizRewardBeerEl = document.querySelector("[data-player-history-quiz-reward-beer]");
        const quizRewardPeanutEl = document.querySelector("[data-player-history-quiz-reward-peanut]");
        const quizPenaltyBeerEl = document.querySelector("[data-player-history-quiz-penalty-beer]");
        const quizPenaltyPeanutEl = document.querySelector("[data-player-history-quiz-penalty-peanut]");
        const correctScorePenaltyBeerEl = document.querySelector("[data-player-history-correct-score-penalty-beer]");
        const correctScorePenaltyPeanutEl = document.querySelector("[data-player-history-correct-score-penalty-peanut]");
        const correctScoreRewardBeerEl = document.querySelector("[data-player-history-correct-score-reward-beer]");
        const correctScoreRewardPeanutEl = document.querySelector("[data-player-history-correct-score-reward-peanut]");
        const outstandingBeerEl = document.querySelector("[data-player-history-outstanding-beer]");
        const outstandingPeanutEl = document.querySelector("[data-player-history-outstanding-peanut]");
        const list = document.querySelector("[data-player-history-list]");

        const cupLogsData = Array.isArray(payload.cupLogs) ? payload.cupLogs : [];
        const wrongCupsCount = summary.wrongBets || 0;

        if (totalBets) totalBets.textContent = formatStatNumber(summary.totalBets);
        if (totalBeerEl) totalBeerEl.textContent = formatStatNumber(summary.totalBeerCups);
        if (totalPeanutEl) totalPeanutEl.textContent = formatStatNumber(summary.totalPeanutPacks);
        if (wrongCupsEl) wrongCupsEl.textContent = formatStatNumber(wrongCupsCount);
        if (pendingBeerEl) pendingBeerEl.textContent = formatStatNumber(summary.pendingBeerCups);
        if (pendingPeanutEl) pendingPeanutEl.textContent = formatStatNumber(summary.pendingPeanutPacks);
        if (missedEl) missedEl.textContent = formatStatNumber(summary.missedMatches);
        if (starHopeBeerEl) starHopeBeerEl.textContent = formatStatNumber(summary.hopeStarBeerCupEffect);
        if (starHopePeanutEl) starHopePeanutEl.textContent = formatStatNumber(summary.hopeStarPeanutPackEffect);
        if (starDevilBeerEl) starDevilBeerEl.textContent = formatStatNumber(summary.devilStarBeerCupEffect);
        if (starDevilPeanutEl) starDevilPeanutEl.textContent = formatStatNumber(summary.devilStarPeanutPackEffect);
        if (promoBeerEl) promoBeerEl.textContent = formatStatNumber(summary.promoBeerCups);
        if (promoPeanutEl) promoPeanutEl.textContent = formatStatNumber(summary.promoPeanutPacks);
        if (paidLegacyBeerEl) paidLegacyBeerEl.textContent = formatStatNumber(summary.paidLegacyBeerCups);
        if (paidPeanutEl) paidPeanutEl.textContent = formatStatNumber(summary.paidPeanutPacks);
        if (quizRewardBeerEl) quizRewardBeerEl.textContent = formatStatNumber(summary.quizRewardBeerCups);
        if (quizRewardPeanutEl) quizRewardPeanutEl.textContent = formatStatNumber(summary.quizRewardPeanutPacks);
        if (quizPenaltyBeerEl) quizPenaltyBeerEl.textContent = formatStatNumber(summary.quizPenaltyBeerCups);
        if (quizPenaltyPeanutEl) quizPenaltyPeanutEl.textContent = formatStatNumber(summary.quizPenaltyPeanutPacks);
        if (correctScorePenaltyBeerEl) correctScorePenaltyBeerEl.textContent = formatStatNumber(summary.correctScorePenaltyBeerCups);
        if (correctScorePenaltyPeanutEl) correctScorePenaltyPeanutEl.textContent = formatStatNumber(summary.correctScorePenaltyPeanutPacks);
        if (correctScoreRewardBeerEl) correctScoreRewardBeerEl.textContent = formatStatNumber(summary.correctScoreRewardBeerCups);
        if (correctScoreRewardPeanutEl) correctScoreRewardPeanutEl.textContent = formatStatNumber(summary.correctScoreRewardPeanutPacks);
        if (outstandingBeerEl) outstandingBeerEl.textContent = formatStatNumber(summary.outstandingBeerCups);
        if (outstandingPeanutEl) outstandingPeanutEl.textContent = formatStatNumber(summary.outstandingPeanutPacks);
        if (!list) {
            return;
        }

        if (payload.loading) {
            list.innerHTML = `<div class="history-empty">Đang tải lịch sử của bạn...</div>`;
            return;
        }

        if (payload.error) {
            list.innerHTML = `<div class="history-empty">${escapeHtml(payload.error)}</div>`;
            return;
        }

        const items = Array.isArray(payload.items) ? payload.items : [];
        if (items.length === 0) {
            list.innerHTML = `<div class="history-empty">Bạn chưa có lượt dự đoán nào.</div>`;
            return;
        }

        list.innerHTML = items.map((item) => {
            const isMissed = item.isMissed === true;
            const result = getPlayerHistoryResult(item);
            const score = item.homeScore !== null && item.homeScore !== undefined && item.awayScore !== null && item.awayScore !== undefined
                ? `${item.homeScore}-${item.awayScore}`
                : "";
            const resultText = item.resultLabel
                ? `Kết quả: ${item.resultLabel}${score ? ` (${score})` : ""}`
                : "Chưa có kết quả trận";
            const canDelete = !isMissed && isOwnHistory && item.canDelete === true;
            const lockReason = item.deleteLockedReason || "Chỉ được xoá trước thời điểm bóng đá bắt đầu ít nhất 20 phút.";
            const lockNote = !isMissed && isOwnHistory && !canDelete && !item.isSettled && item.deleteLockedReason
                ? `<small class="history-lock" title="${escapeHtml(lockReason)}">Đã khóa xoá</small>`
                : "";
            const scoreBadge = renderHistoryScoreBadge(item);
            const unit = getUnitMeta(item.unitCode);

            if (isMissed) {
                return `
                    <article class="history-item player-history-item player-history-item--missed">
                        <span class="history-main">
                            <strong>${escapeHtml(item.matchLabel || "Trận đấu")}</strong>
                            <span>
                                <span class="choice choice--missed">Không tham gia</span>
                                <span class="history-result history-result--missed">Kèo miss</span>
                            </span>
                            <small>${escapeHtml(resultText)}</small>
                        </span>
                        <span class="history-side">
                            <time>${escapeHtml(formatHistoryTime(item.createdAt))}</time>
                        </span>
                    </article>
                `;
            }

            return `
                <article class="history-item player-history-item">
                    <span class="history-main">
                        <strong>${escapeHtml(item.matchLabel || "Trận đấu")}</strong>
                        <span>
                            <span class="choice ${escapeHtml(getChoiceClass(item.choice))}">${escapeHtml(item.choiceLabel || "Du doan") + (item.starType === "hope" ? " ⭐" : item.starType === "devil" ? " 😈" : "")}</span>
                            <span class="history-cups">${escapeHtml(String(item.cups || 0))} ${escapeHtml(unit.full)}</span>
                            ${scoreBadge}
                            <span class="history-result history-result--${escapeHtml(result.className)}">${escapeHtml(result.text)}</span>
                        </span>
                        <small>${escapeHtml(resultText)}</small>
                    </span>
                    <span class="history-side">
                        <time>${escapeHtml(formatHistoryTime(item.createdAt))}</time>
                        ${canDelete
                            ? `<button type="button" class="history-delete" data-delete-player-bet="${escapeHtml(String(item.id || ""))}"><span class="material-symbols-rounded">delete</span>Xoá</button>`
                            : lockNote}
                    </span>
                </article>
            `;
        }).join("");

        var cuplogList = document.querySelector("[data-cuplog-history-list]");
        if (cuplogList && cupLogsData.length > 0) {
            cuplogList.innerHTML = cupLogsData.map(function(log) {
                var isPositive = log.cups > 0;
                var isNegative = log.cups < 0;
                var iconLabel = getCupLogIconLabel(log.changeType);
                var reason = getCupLogReason(log);
                var sign = isPositive ? "+" : "";
                var colorClass = isPositive ? "cuplog--positive" : isNegative ? "cuplog--negative" : "cuplog--neutral";
                var unit = getUnitMeta(log.unitCode);
                var cupsText = log.cups !== 0 ? (sign + log.cups + " " + unit.short) : "sử dụng";
                var balanceText = formatUnitBreakdown(log.balanceAfterBeerCups, log.balanceAfterPeanutPacks);
                return '<article class="history-item cuplog-item ' + colorClass + '">' +
                    '<span class="history-main">' +
                        '<strong>' + escapeHtml(reason) + '</strong>' +
                        '<span>' +
                            '<span class="cuplog-badge ' + colorClass + '">' + escapeHtml(iconLabel) + '</span>' +
                            '<span class="cuplog-cups">' + escapeHtml(cupsText) + '</span>' +
                            '<span class="cuplog-balance">Tổng còn thiếu: ' + escapeHtml(balanceText) + '</span>' +
                        '</span>' +
                    '</span>' +
                    '<span class="history-side">' +
                        '<time>' + escapeHtml(formatHistoryTime(log.createdAt)) + '</time>' +
                    '</span>' +
                '</article>';
            }).join("");
        } else if (cuplogList) {
            cuplogList.innerHTML = '<div class="history-empty">Chưa có lịch sử cốc bia hoặc gói lạc.</div>';
        }

        var tabs = document.querySelectorAll("[data-history-tab]");
        tabs.forEach(function(tab) {
            tab.onclick = function() {
                var target = tab.getAttribute("data-history-tab");
                tabs.forEach(function(t) { t.classList.remove("is-active"); });
                tab.classList.add("is-active");
                var betsList = document.querySelector("[data-player-history-list]");
                var logsList = document.querySelector("[data-cuplog-history-list]");
                if (betsList) betsList.style.display = target === "bets" ? "" : "none";
                if (logsList) logsList.style.display = target === "cuplogs" ? "" : "none";
            };
        });
    }

        async function deletePlayerHistoryBet(betId) {
        if (!betId) return;
        var publicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
        if (!publicKey) {
            showToast("Chưa xoá được", "Bạn cần có hồ sơ người chơi.");
            return;
        }
        try {
            var data = await postJson("/keobia/delete-bet", { publicKey: publicKey, betId: betId });
            var cups = Number(data.cups || 0);
            var matchId = data.matchId ? String(data.matchId) : "";
            var choice = data.choice || "";
            if (matchId && choice && cups > 0) {
                var hadPick = state.myPicks[matchId] && Number(state.myPicks[matchId][choice]) > 0;
                removeMyPick(matchId, choice, cups);
                var stillHasPick = state.myPicks[matchId] && Number(state.myPicks[matchId][choice]) > 0;
                if (hadPick && !stillHasPick) bumpBettorCount(matchId, choice, -1);
            }
            removeFeedItems(data.activityIds || []);
            if (data.history) {
                var payload = applyPlayerHistoryPayload(data.history);
                renderPlayerHistory({ summary: payload.summary, items: payload.items, cupLogs: payload.cupLogs, isOwnHistory: true });
            } else {
                await refreshPlayerHistory({ renderModal: true });
            }
            renderMatches();
            void refreshMyPredictions();
            showToast("Đã xoá", "Lượt dự đoán đã được gỡ.");
        } catch (err) {
            showToast("Không xoá được", err.message || "Vui lòng thử lại.");
        }
    }

    function openPlayerHistory() {
        openModal("player-history");
        const modal = shell.querySelector('[data-modal="player-history"]');
        const title = modal ? modal.querySelector("[data-player-history-title]") : null;
        if (title) title.textContent = "Dự đoán và kết quả của bạn";
        renderPlayerHistory({
            summary: state.playerStats || getDefaultPlayerStats(),
            items: state.playerHistory || [],
            loading: true,
            isOwnHistory: true
        });
        void refreshPlayerHistory({ renderModal: true });
    }

    async function openLossboardPlayerHistory(publicKey, playerName) {
        openModal("player-history");
        const modal = shell.querySelector('[data-modal="player-history"]');
        const title = modal ? modal.querySelector("[data-player-history-title]") : null;
        if (title) title.textContent = playerName;
        renderPlayerHistory({
            summary: getDefaultPlayerStats(),
            items: [],
            loading: true
        });
        try {
            const data = await postJson("/keobia/player-history", { publicKey });
            const summary = {
                ...getDefaultPlayerStats(),
                ...((data && data.summary) || {})
            };
            const items = Array.isArray(data && data.items) ? data.items : [];
            const cupLogs = Array.isArray(data && data.cupLogs) ? data.cupLogs : [];
            if (title) title.textContent = playerName;
            renderPlayerHistory({ summary, items, cupLogs });
        } catch (error) {
            renderPlayerHistory({
                summary: getDefaultPlayerStats(),
                items: [],
                error: error.message
            });
        }
    }

    function renderHistoryScoreBadge(item) {
        const home = item.predictedHomeScore;
        const away = item.predictedAwayScore;
        if (home === null || home === undefined || away === null || away === undefined) return "";
        const resultHome = item.resultHomeScore;
        const resultAway = item.resultAwayScore;
        if (resultHome === null || resultHome === undefined || resultAway === null || resultAway === undefined) {
            return `<span class="history-score history-score--pending">TS 90p ${escapeHtml(String(home))}-${escapeHtml(String(away))} · chờ</span>`;
        }
        const exact = Number(home) === Number(resultHome) && Number(away) === Number(resultAway);
        const unit = getUnitMeta(item.unitCode);
        const text = exact ? `đúng +3 ${unit.short}` : `sai -1 ${unit.short}`;
        const className = exact ? "history-score--win" : "history-score--lost";
        return `<span class="history-score ${className}">TS 90p ${escapeHtml(String(home))}-${escapeHtml(String(away))} · ${text}</span>`;
    }

    function renderPredictionHistory(data) {
        const title = shell.querySelector("[data-history-title]");
        const entries = shell.querySelector("[data-history-total-entries]");
        const cups = shell.querySelector("[data-history-total-cups]");
        const unitLabel = shell.querySelector("[data-history-unit]");
        const list = shell.querySelector("[data-history-list]");
        if (!title || !entries || !cups || !list) {
            return;
        }

        title.textContent = data.matchLabel || "Lịch sử dự đoán";
        entries.textContent = String(data.totalEntries || 0);
        cups.textContent = String(data.totalCups || 0);
        const historyUnit = getUnitMeta(data.unitCode);
        if (unitLabel) unitLabel.textContent = historyUnit.short;

        const items = Array.isArray(data.items) ? data.items : [];
        if (items.length === 0) {
            list.innerHTML = `<div class="history-empty">Chưa có ai gửi dự đoán cho trận này.</div>`;
            return;
        }

        list.innerHTML = items.map((item) => {
            const itemUnit = getUnitMeta(item.unitCode || data.unitCode);
            return `
            <article class="history-item">
                <span class="history-avatar">
                    <img src="${escapeHtml(item.avatarUrl || getDefaultAvatar())}" alt="" />
                </span>
                <span class="history-main">
                    <strong>${escapeHtml(item.playerName || "Bia thủ")}</strong>
                    <span>
                        <span class="choice ${escapeHtml(getChoiceClass(item.choice))}">${escapeHtml(item.choiceLabel || "Du doan") + (item.starType === "hope" ? " ⭐" : item.starType === "devil" ? " 😈" : "")}</span>
                        <span class="history-cups">${escapeHtml(String(item.cups || 0))} ${escapeHtml(itemUnit.full)}</span>
                        ${renderHistoryScoreBadge(item)}
                    </span>
                </span>
                <time>${escapeHtml(formatHistoryTime(item.createdAt))}</time>
            </article>
        `;
        }).join("");
    }

    function getChoiceTextFromCard(card, choice) {
        const node = card.querySelector(`[data-choice-button][data-choice="${choice}"] .pred-main`);
        return node ? node.textContent.trim() : getChoiceLabel(card, choice);
    }

    function setHistoryFilters(card, activeChoice = "") {
        historyState.choice = activeChoice || "";
        const home = shell.querySelector("[data-history-filter-home]");
        const away = shell.querySelector("[data-history-filter-away]");
        if (home) home.textContent = getChoiceTextFromCard(card, "home");
        if (away) away.textContent = getChoiceTextFromCard(card, "away");

        const allowsDraw = card.dataset.allowsDraw !== "false";

        shell.querySelectorAll("[data-history-filter]").forEach((button) => {
            const choice = button.getAttribute("data-history-filter") || "";
            if (choice === "draw") {
                button.hidden = !allowsDraw;
                if (!allowsDraw && historyState.choice === "draw") {
                    historyState.choice = "";
                }
            }
            button.classList.toggle("is-active", choice === historyState.choice);
        });
    }

    function setPredictionHistoryLoading(card) {
        renderPredictionHistory({
            matchLabel: `${card.dataset.homeName} vs ${card.dataset.awayName}`,
            totalEntries: 0,
            totalCups: 0,
            unitCode: card.dataset.unitCode,
            items: []
        });
        const list = shell.querySelector("[data-history-list]");
        if (list) {
            list.innerHTML = `<div class="history-empty">Đang tải lịch sử dự đoán...</div>`;
        }
    }

    async function requestPredictionHistory(card, choice = "") {
        historyState.matchId = card.dataset.matchId;
        historyState.matchLabel = `${card.dataset.homeName} vs ${card.dataset.awayName}`;
        setHistoryFilters(card, choice);
        setPredictionHistoryLoading(card);

        try {
            const data = await postJson("/keobia/match-history", {
                matchId: historyState.matchId,
                choice: historyState.choice || null
            });
            renderPredictionHistory(data);
        } catch (error) {
            const list = shell.querySelector("[data-history-list]");
            if (list) {
                list.innerHTML = `<div class="history-empty">${escapeHtml(error.message || "Không tải được lịch sử dự đoán.")}</div>`;
            }
        }
    }

    function openPredictionHistory(card) {
        openModal("prediction-history");
        void requestPredictionHistory(card, "");
    }

    function formatChatTimeLabel(value) {
        if (!value) {
            return "Vá»«a xong";
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return "Vá»«a xong";
        }

        const now = new Date();
        const sameDay = date.getFullYear() === now.getFullYear()
            && date.getMonth() === now.getMonth()
            && date.getDate() === now.getDate();
        const hours = String(date.getHours()).padStart(2, "0");
        const minutes = String(date.getMinutes()).padStart(2, "0");
        if (sameDay) {
            return `${hours}:${minutes}`;
        }

        const day = String(date.getDate()).padStart(2, "0");
        const month = String(date.getMonth() + 1).padStart(2, "0");
        return `${day}/${month} ${hours}:${minutes}`;
    }

    function syncChatUnread() {
        const badge = shell.querySelector("[data-chat-unread]");
        const status = shell.querySelector("[data-chat-launcher-status]");
        if (!badge) {
            return;
        }

        badge.textContent = String(chatState.unread);
        badge.hidden = chatState.unread < 1;
        if (status) {
            status.textContent = chatState.unread > 0
                ? `${chatState.unread} tin nhắn mới`
                : "";
        }
    }

    function setChatLauncherState() {
        const launcher = shell.querySelector("[data-chat-toggle]");
        const popup = shell.querySelector("[data-community-chat]");
        if (!launcher || !popup) {
            return;
        }

        launcher.setAttribute("aria-expanded", chatState.isOpen ? "true" : "false");
        launcher.classList.toggle("is-active", chatState.isOpen);
        popup.hidden = !chatState.isOpen;
    }

    function closeChatEmojiPanel() {
        chatState.emojiOpen = false;
        const panel = shell.querySelector("[data-chat-emoji-panel]");
        if (panel) {
            panel.hidden = true;
        }
    }

    function openCommunityChat() {
        chatState.isOpen = true;
        chatState.unread = 0;
        syncChatUnread();
        setChatLauncherState();
        closeChatEmojiPanel();
        renderChat(true);
    }

    function closeCommunityChat() {
        chatState.isOpen = false;
        setChatLauncherState();
        closeChatEmojiPanel();
    }

    function resetPendingChatImage() {
        if (chatState.pendingImageUrl) {
            URL.revokeObjectURL(chatState.pendingImageUrl);
            chatState.pendingImageUrl = "";
        }

        chatState.pendingImageFile = null;

        const preview = shell.querySelector("[data-chat-image-preview]");
        const previewImage = shell.querySelector("[data-chat-preview-image]");
        const previewName = shell.querySelector("[data-chat-preview-name]");
        const previewSize = shell.querySelector("[data-chat-preview-size]");
        const upload = shell.querySelector("[data-chat-image-upload]");

        if (preview) {
            preview.hidden = true;
        }
        if (previewImage) {
            previewImage.setAttribute("src", "");
        }
        if (previewName) {
            previewName.textContent = "áº¢nh chat";
        }
        if (previewSize) {
            previewSize.textContent = "Đang chờ gửi";
        }
        if (upload) {
            upload.value = "";
        }
    }

    function renderPendingChatImage(file) {
        const preview = shell.querySelector("[data-chat-image-preview]");
        const previewImage = shell.querySelector("[data-chat-preview-image]");
        const previewName = shell.querySelector("[data-chat-preview-name]");
        const previewSize = shell.querySelector("[data-chat-preview-size]");

        if (!preview || !previewImage || !previewName || !previewSize || !file) {
            return;
        }

        preview.hidden = false;
        previewImage.setAttribute("src", chatState.pendingImageUrl);
        previewName.textContent = file.name || "áº¢nh chat";
        previewSize.textContent = `${Math.max(1, Math.round(file.size / 1024))} KB`;
    }

    function setPendingChatImage(file) {
        resetPendingChatImage();
        chatState.pendingImageFile = file;
        chatState.pendingImageUrl = URL.createObjectURL(file);
        renderPendingChatImage(file);
    }

    function updateChatCounter() {
        const input = shell.querySelector("[data-chat-input]");
        const counter = shell.querySelector("[data-chat-counter]");
        if (!input || !counter) {
            return;
        }

        counter.textContent = `${input.value.length}/600`;
    }

    function normalizeChatItem(payload) {
        if (!payload || !payload.id) {
            return null;
        }

        return {
            id: String(payload.id),
            playerId: payload.playerId ? String(payload.playerId) : "",
            publicKey: payload.publicKey ? String(payload.publicKey) : "",
            playerName: payload.playerName || "Bia thủ mới",
            avatar: payload.avatarUrl || getDefaultAvatar(),
            message: payload.message || "",
            imageUrl: payload.imageUrl || "",
            time: payload.time || formatChatTimeLabel(payload.createdAt),
            createdAt: payload.createdAt || new Date().toISOString()
        };
    }

    function renderChat(shouldScrollToBottom = false) {
        const list = shell.querySelector("[data-chat-list]");
        if (!list) {
            return;
        }

        const nearBottom = (list.scrollHeight - list.scrollTop - list.clientHeight) < 48;
        if (state.chat.length === 0) {
            list.innerHTML = `<div class="community-chat-popup__empty">Chưa có tin nhắn nào. Hãy mở lời trước.</div>`;
        } else {
            const ownPublicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
            list.innerHTML = state.chat.map((item) => {
                const isOwn = item.publicKey && item.publicKey === ownPublicKey;
                const textBlock = item.message
                    ? `<div class="community-chat-popup__text">${escapeHtml(item.message)}</div>`
                    : "";
                const imageBlock = item.imageUrl
                    ? `<div class="community-chat-popup__image"><img src="${escapeHtml(item.imageUrl)}" alt="áº¢nh chat của ${escapeHtml(item.playerName)}" loading="lazy" /></div>`
                    : "";

                return `
                    <article class="community-chat-popup__item${isOwn ? " is-own" : ""}">
                        <div class="community-chat-popup__meta">
                            <img src="${escapeHtml(item.avatar)}" alt="" />
                            <strong>${escapeHtml(isOwn ? "Báº¡n" : item.playerName)}</strong>
                            <span>${escapeHtml(item.time)}</span>
                        </div>
                        <div class="community-chat-popup__bubble">
                            ${textBlock}
                            ${imageBlock}
                        </div>
                    </article>`;
            }).join("");
        }

        if (shouldScrollToBottom || nearBottom) {
            list.scrollTop = list.scrollHeight;
        }
    }

    function appendChatMessage(payload, options = {}) {
        const item = normalizeChatItem(payload);
        if (!item) {
            return;
        }

        if (realtimeState.seenChatIds.has(item.id)) {
            return;
        }

        realtimeState.seenChatIds.add(item.id);
        state.chat.push(item);
        if (state.chat.length > maxChatMessages) {
            state.chat = state.chat.slice(state.chat.length - maxChatMessages);
        }

        const ownPublicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
        if (!options.silent && !chatState.isOpen && item.publicKey !== ownPublicKey) {
            chatState.unread += 1;
            syncChatUnread();
        }

        renderChat(Boolean(options.scrollToBottom));
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function showToast(title, text) {
        if (!toastStack) {
            return;
        }

        const toast = document.createElement("div");
        toast.className = "toast";
        toast.innerHTML = `<strong>${escapeHtml(title)}</strong><span>${escapeHtml(text)}</span>`;
        toastStack.appendChild(toast);

        if (!reduceMotion.matches && navigator.vibrate) {
            navigator.vibrate(10);
        }

        window.setTimeout(() => {
            toast.remove();
        }, 3000);
    }

    function showHeroToast(title, text, options) {
        if (!heroToastStack) {
            showToast(title, text);
            return;
        }

        window.clearTimeout(heroToastTimer);
        heroToastStack.replaceChildren();

        const settings = options || {};
        const toast = document.createElement("div");
        toast.className = "hero-toast";

        const avatarUrl = settings.avatarUrl || "";
        const avatarMarkup = avatarUrl
            ? `<img class="hero-toast__avatar" src="${escapeHtml(avatarUrl)}" alt="${escapeHtml(title)}" />`
            : `<div class="hero-toast__avatar hero-toast__avatar--placeholder"><span class="material-symbols-rounded">sports_bar</span></div>`;

        toast.innerHTML = `
            <div class="hero-toast__content">
                <span class="hero-toast__badge">
                    <span class="material-symbols-rounded">celebration</span>
                    ${escapeHtml(settings.badge || "Người chơi mới")}
                </span>
                <div class="hero-toast__body">
                    ${avatarMarkup}
                    <div class="hero-toast__copy">
                        <strong>${escapeHtml(title)}</strong>
                        <span>${escapeHtml(text)}</span>
                    </div>
                </div>
            </div>`;

        heroToastStack.appendChild(toast);

        if (!reduceMotion.matches && navigator.vibrate) {
            navigator.vibrate([18, 30, 18]);
        }

        heroToastTimer = window.setTimeout(() => {
            toast.classList.add("is-leaving");
            window.setTimeout(() => {
                if (toast.parentNode === heroToastStack) {
                    toast.remove();
                }
            }, 360);
        }, 4200);
    }

    function getJoinEventKey(payload) {
        if (!payload) {
            return "";
        }

        if (payload.playerId) {
            return String(payload.playerId);
        }

        return payload.publicKey ? String(payload.publicKey) : "";
    }

    function prependJoinActivity(payload) {
        prependFeedItem({
            id: payload.publicKey ? `join-${payload.publicKey}` : "",
            avatar: payload.avatarUrl || getDefaultAvatar(),
            text: `${payload.displayName} đã vào bàn vui.`,
            time: "Vừa xong",
            choice: "draw",
            choiceLabel: "Chào mừng",
            badge: "waving_hand"
        });
    }

    function handlePlayerJoined(payload) {
        if (!payload || !payload.displayName) {
            return;
        }

        const joinKey = getJoinEventKey(payload);
        if (joinKey && realtimeState.seenJoinKeys.has(joinKey)) {
            return;
        }

        if (joinKey) {
            realtimeState.seenJoinKeys.add(joinKey);
        }

        prependJoinActivity(payload);

        const ownPublicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
        if (payload.publicKey && payload.publicKey === ownPublicKey) {
            return;
        }

        showHeroToast(`Chào mừng ${payload.displayName}`, "vừa gia nhập bàn vui. Không khí bắt đầu nóng lên rồi.", {
            avatarUrl: payload.avatarUrl,
            badge: "Chào mừng gia nhập"
        });
    }

    function handleChatMessage(payload) {
        if (!payload || !payload.id) {
            return;
        }

        appendChatMessage(payload, {
            scrollToBottom: chatState.isOpen
        });
    }

    async function startRealtime(connection) {
        try {
            await connection.start();
            console.info("KeoBia realtime connected.");
        } catch (error) {
            console.warn("KeoBia realtime connection failed.", error);
            window.setTimeout(() => {
                void startRealtime(connection);
            }, 5000);
        }
    }

    function initRealtime() {
        if (realtimeState.connection) {
            return;
        }

        if (!window.signalR || !window.signalR.HubConnectionBuilder) {
            console.warn("SignalR client library is missing on window.signalR.");
            return;
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl("/keobia/hub")
            .withAutomaticReconnect()
            .build();

        connection.on("playerJoined", handlePlayerJoined);
        connection.on("activityAdded", handleActivityAdded);
        connection.on("activityRemoved", handleActivityRemoved);
        connection.on("chatMessage", handleChatMessage);
        connection.on("share-beer", handleShareBeer);
        connection.onclose(() => {
            console.warn("KeoBia realtime disconnected. Retrying...");
            window.setTimeout(() => {
                void startRealtime(connection);
            }, 5000);
        });

        realtimeState.connection = connection;
        void startRealtime(connection);
    }

    async function postJson(url, payload) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });
        if (response.ok) {
            return response.json().catch(() => ({}));
        }

        let message = "Máy chủ lỗi thao tác.";
        try {
            const data = await response.json();
            if (data && data.error) {
                message = data.error;
            }
        } catch {
            // Keep default message.
        }
        throw new Error(message);
    }

    function openModal(name) {
        document.querySelectorAll(`[data-modal="${name}"]`).forEach((modal) => {
            modal.classList.add("is-open");
            modal.setAttribute("aria-hidden", "false");
        });
    }

    function closeModal(name) {
        document.querySelectorAll(`[data-modal="${name}"]`).forEach((modal) => {
            modal.classList.remove("is-open");
            modal.setAttribute("aria-hidden", "true");
        });
    }

    let unitTransitionCheckedPublicKey = "";

    async function checkUnitTransitionNotice(force = false) {
        const profile = state.profile;
        // The server owns Telegram verification; old cached profiles may not contain telegramId.
        if (!profile || !profile.publicKey) {
            return;
        }
        if (!force && unitTransitionCheckedPublicKey === profile.publicKey) {
            return;
        }

        const publicKey = profile.publicKey;
        try {
            const data = await postJson("/keobia/unit-transition/notice", {
                publicKey
            });
            // Only cache a successful response so a transient failure is retried.
            unitTransitionCheckedPublicKey = publicKey;
            if (data && data.shouldShow) {
                openModal("unit-transition");
            }
        } catch {
            if (unitTransitionCheckedPublicKey === publicKey) {
                unitTransitionCheckedPublicKey = "";
            }
        }
    }

    async function confirmStopPlaying(options = {}) {
        const profile = state.profile;
        if (!profile || !profile.publicKey) {
            showToast("Chưa dừng được", "Bạn cần lưu hồ sơ người chơi trước.");
            return;
        }

        if (profile.isBlocked) {
            showToast("Đã dừng", "Tài khoản này đã không tham gia các kèo tiếp theo.");
            return;
        }

        if (!window.confirm(
            "Dừng tại đây sẽ khóa tài khoản khỏi các kèo tiếp theo, hủy các kèo chưa diễn ra và không thể hoàn tác tại đây. Bạn chắc chắn chứ?"
        )) {
            return;
        }

        const modal = options.modalName
            ? document.querySelector(`[data-modal="${options.modalName}"]`)
            : null;
        const buttons = modal ? Array.from(modal.querySelectorAll("button")) : [];
        buttons.forEach((button) => { button.disabled = true; });
        try {
            const data = await postJson("/keobia/unit-transition/decision", {
                publicKey: profile.publicKey,
                stopPlaying: true
            });

            if (data && data.isBlocked) {
                state.profile = { ...profile, isBlocked: true };
                localStorage.setItem(storageKey, JSON.stringify(state.profile));
                if (options.modalName) {
                    closeModal(options.modalName);
                }
                showToast("Đã dừng lại", "Tài khoản đã dừng tham gia các kèo tiếp theo.");
                window.setTimeout(() => window.location.reload(), 1200);
                return;
            }

            showToast("Chưa dừng được", "Hệ thống chưa ghi nhận yêu cầu dừng lại.");
        } catch (error) {
            showToast("Chưa ghi nhận được", error.message || "Vui lòng thử lại.");
        } finally {
            buttons.forEach((button) => { button.disabled = false; });
        }
    }

    async function submitUnitTransitionDecision(stopPlaying) {
        if (stopPlaying) {
            await confirmStopPlaying({ modalName: "unit-transition" });
            return;
        }

        const profile = state.profile;
        if (!profile || !profile.publicKey) {
            closeModal("unit-transition");
            return;
        }

        const modal = document.querySelector('[data-modal="unit-transition"]');
        const buttons = modal ? Array.from(modal.querySelectorAll("button")) : [];
        buttons.forEach((button) => { button.disabled = true; });
        try {
            await postJson("/keobia/unit-transition/decision", {
                publicKey: profile.publicKey,
                stopPlaying: false
            });
            closeModal("unit-transition");
            showToast("Tiếp tục chơi", "Các kèo mới từ vòng tứ kết sẽ dùng gói lạc.");
        } catch (error) {
            showToast("Chưa ghi nhận được", error.message || "Vui lòng thử lại.");
        } finally {
            buttons.forEach((button) => { button.disabled = false; });
        }
    }

    function closestFromEventTarget(target, selector) {
        const element = target instanceof Element ? target : target && target.parentElement;
        return element ? element.closest(selector) : null;
    }

    function askCorrectScore(card, choice) {
        let oddsMap = null;
        try { oddsMap = card.dataset.correctScoreOdds ? JSON.parse(card.dataset.correctScoreOdds) : null; } catch { oddsMap = null; }
        const modal = shell.querySelector('[data-modal="score-prediction"]');
        if (!modal) return Promise.resolve(null);
        const homeInput = modal.querySelector("[data-score-home]");
        const awayInput = modal.querySelector("[data-score-away]");
        const title = modal.querySelector("[data-score-match]");
        const homeLabel = modal.querySelector("[data-score-home-label]");
        const awayLabel = modal.querySelector("[data-score-away-label]");
        const rule = modal.querySelector("[data-score-rule]");
        const scoreList = modal.querySelector("[data-score-odds-list]");
        const picksList = modal.querySelector("[data-score-picks-list]");
        const homeName = card.querySelector('[data-choice="home"] .pred-main')?.textContent?.trim() || "Đội nhà";
        const awayName = card.querySelector('[data-choice="away"] .pred-main')?.textContent?.trim() || "Đội khách";
        if (title) title.textContent = `${homeName} vs ${awayName}`;
        if (homeLabel) homeLabel.textContent = homeName;
        if (awayLabel) awayLabel.textContent = awayName;
        const unit = getCardUnit(card);
        if (rule) rule.textContent = `Chỉ tính kết quả 90 phút. Đoán sai mất 1 ${unit.full}, đoán đúng được giảm 3 ${unit.full}.`;
        if (homeInput) homeInput.value = "";
        if (awayInput) awayInput.value = "";
        if (scoreList) {
            let exactOdds = Object.entries(oddsMap || {})
                .filter(([key, value]) => /^\d{1,2}-\d{1,2}$/.test(key) && Number.isFinite(Number(value)))
                .filter(([key]) => isScoreConsistentWithChoice(key, choice))
                .sort((a, b) => Number(a[1]) - Number(b[1]) || a[0].localeCompare(b[0]))
                .slice(0, 8);
            if (exactOdds.length === 0) {
                exactOdds = getDefaultScoreSuggestions(choice).map((score, index) => [score, index]);
            }
            scoreList.innerHTML = exactOdds.map(([score]) => `<button type="button" class="score-odd-chip" data-score-pick="${score}">${score}</button>`).join("")
                + (exactOdds.length > 0 ? '<button type="button" class="score-odd-chip score-odd-chip--other" data-score-other>Khác</button>' : '');
        }
        renderScorePicksList(picksList, null);
        void loadScorePicks(card.dataset.matchId, picksList);

        return new Promise((resolve) => {
            const cleanup = () => {
                modal.querySelectorAll("[data-score-skip]").forEach((el) => el.removeEventListener("click", skip));
                const confirm = modal.querySelector("[data-score-confirm]");
                if (confirm) confirm.removeEventListener("click", submit);
                modal.querySelectorAll("[data-score-pick]").forEach((el) => el.removeEventListener("click", pickScore));
                modal.querySelectorAll("[data-score-other]").forEach((el) => el.removeEventListener("click", useOther));
                closeModal("score-prediction");
            };
            const skip = () => { cleanup(); resolve(null); };
            const pickScore = (event) => {
                const score = event.currentTarget.dataset.scorePick || "";
                const [home, away] = score.split("-").map(Number);
                if (homeInput) homeInput.value = String(home);
                if (awayInput) awayInput.value = String(away);
                submit();
            };
            const useOther = () => {
                if (homeInput) homeInput.value = "";
                if (awayInput) awayInput.value = "";
                homeInput && homeInput.focus();
            };
            const submit = () => {
                const home = Number(homeInput && homeInput.value);
                const away = Number(awayInput && awayInput.value);
                if (!Number.isInteger(home) || !Number.isInteger(away) || home < 0 || away < 0 || home > 20 || away > 20) {
                    showToast("Tỉ số chưa hợp lệ", "Nhập đủ hai tỉ số từ 0 đến 20. Tỉ số chỉ tính 90 phút.");
                    return;
                }
                if (!isScoreConsistentWithChoice(`${home}-${away}`, choice)) {
                    showToast("Tỉ số không khớp kèo", getScoreChoiceWarning(choice));
                    return;
                }
                cleanup();
                resolve({ predictedHomeScore: home, predictedAwayScore: away, correctScoreOdds: null });
            };
            modal.querySelectorAll("[data-score-skip]").forEach((el) => el.addEventListener("click", skip));
            modal.querySelectorAll("[data-score-pick]").forEach((el) => el.addEventListener("click", pickScore));
            modal.querySelectorAll("[data-score-other]").forEach((el) => el.addEventListener("click", useOther));
            const confirm = modal.querySelector("[data-score-confirm]");
            if (confirm) confirm.addEventListener("click", submit);
            openModal("score-prediction");
            setTimeout(() => homeInput && homeInput.focus(), 40);
        });
    }

    function isScoreConsistentWithChoice(score, choice) {
        const parts = String(score || "").split("-").map(Number);
        if (parts.length !== 2 || !Number.isInteger(parts[0]) || !Number.isInteger(parts[1])) return false;
        const [home, away] = parts;
        if (choice === "home") return home > away;
        if (choice === "draw") return home === away;
        if (choice === "away") return home < away;
        return false;
    }

    function getScoreChoiceWarning(choice) {
        if (choice === "home") return "Bạn đã chọn đội nhà thắng, tỉ số dự đoán phải là đội nhà thắng.";
        if (choice === "draw") return "Bạn đã chọn hòa, tỉ số dự đoán phải là hòa.";
        if (choice === "away") return "Bạn đã chọn đội khách thắng, tỉ số dự đoán phải là đội khách thắng.";
        return "Tỉ số dự đoán phải khớp cửa đã chọn.";
    }

    function getDefaultScoreSuggestions(choice) {
        if (choice === "home") return ["1-0", "2-0", "2-1", "3-1", "3-2", "4-1"];
        if (choice === "draw") return ["0-0", "1-1", "2-2", "3-3"];
        if (choice === "away") return ["0-1", "0-2", "1-2", "1-3", "2-3", "1-4"];
        return [];
    }

    function renderScorePicksList(container, items) {
        if (!container) return;
        if (!Array.isArray(items)) {
            container.innerHTML = '<div class="score-pick-row"><strong>Đang tải người đã chọn tỉ số...</strong></div>';
            return;
        }

        const scoreItems = items.filter((item) => item.predictedHomeScore !== null && item.predictedHomeScore !== undefined && item.predictedAwayScore !== null && item.predictedAwayScore !== undefined);
        if (scoreItems.length === 0) {
            container.innerHTML = '<div class="score-pick-row"><strong>Chưa ai chọn tỉ số.</strong></div>';
            return;
        }

        container.innerHTML = scoreItems.map((item) => `<div class="score-pick-row"><strong>${escapeHtml(item.playerName || "Bia thủ")}</strong><em>${escapeHtml(String(item.predictedHomeScore))}-${escapeHtml(String(item.predictedAwayScore))}</em></div>`).join("");
    }

    async function loadScorePicks(matchId, container) {
        if (!matchId || !container) return;
        try {
            const data = await postJson("/keobia/match-history", { matchId, choice: null });
            renderScorePicksList(container, Array.isArray(data.items) ? data.items : []);
        } catch {
            container.innerHTML = '<div class="score-pick-row"><strong>Không tải được danh sách tỉ số.</strong></div>';
        }
    }

    var shareBeerTarget = "";
    var shareBeerName = "";
    var selectedStarType = null;

    function openShareBeerModal(btn) {
        if (!btn) {
            return;
        }

        shareBeerTarget = btn.getAttribute("data-share-beer") || "";
        shareBeerName = btn.getAttribute("data-share-beer-name") || "";

        var toKey = document.getElementById("shareBeerToKey");
        var target = document.getElementById("shareBeerTarget");
        var cups = document.getElementById("shareBeerCups");

        if (toKey) toKey.value = shareBeerTarget;
        if (target) target.textContent = shareBeerName;
        if (cups) cups.value = "1";

        document.querySelectorAll(".share-beer-preset").forEach(function(el) {
            el.classList.toggle("is-active", el.getAttribute("data-cups") === "1");
        });

        closeModal("player-history");
        openModal("share-beer");
    }

    window.openShareBeerModal = openShareBeerModal;

    function openProfileForNewVisitor() {
        if (!shouldOpenProfileOnStart) {
            return;
        }

        window.requestAnimationFrame(() => openModal("profile"));
    }

    function openProfileEditor() {
        resetCropper();
        renderProfile();
        openModal("profile");

        window.requestAnimationFrame(() => {
            const input = shell.querySelector("[data-player-input]");
            if (input) {
                input.focus();
                input.select();
            }
        });
    }

    async function submitBet(card) {
        const id = card.dataset.matchId;
        refreshLocks();
        if (isCardLocked(card)) {
            const pending = card.classList.contains("mc-pending");
            showToast(
                pending ? "Chưa mở kèo" : "Đã khóa kèo",
                pending
                    ? "Trận này mở kèo trước giờ đá 1 ngày."
                    : "Kèo đã khóa trước giờ bóng đá 20 phút.");
            return;
        }
        const choice = state.selected[id];
        if (!choice) {
            showToast("Chưa chọn kèo", "Hãy chọn 1 trong 3 cửa: chủ nhà thắng, hoà, hoặc khách thắng.");
            return;
        }
        const cups = 1;
        var starType = selectedStarType;

        const oldChoices = state.myPicks[id] ? Object.keys(state.myPicks[id]) : [];
        const oldChoice = oldChoices.length > 0 ? oldChoices[0] : null;
        const isChange = oldChoice && oldChoice !== choice;

        if (isChange && !starType && state.myPicks[id] && state.myPicks[id].starType) {
            var keepStar = window.confirm("Bạn đang dùng item cho lựa chọn cũ. Áp dụng item cho lựa chọn mới không?");
            if (keepStar) {
                starType = state.myPicks[id].starType;
            }
        }

        selectedStarType = null;
        shell.querySelectorAll("[data-star-type]").forEach(function(b) { b.classList.remove("is-active"); });
        const choiceLabel = getChoiceLabel(card, choice) + (starType === "hope" ? " ⭐Hi vọng" : starType === "devil" ? " 😈Ma quỷ" : "");
        const activeProfile = await ensureResolvedProfile("gửi dự đoán");
        if (!activeProfile) {
            return;
        }
        if (!activeProfile.telegramId) {
            showToast("Cần xác thực Telegram", "Hãy xác thực Telegram trong hồ sơ trước khi đặt kèo.");
            openModal("profile");
            return;
        }

        const scorePrediction = await askCorrectScore(card, choice);

        let response;
        try {
            response = await postJson("/keobia/bet", {
                publicKey: activeProfile.publicKey || getPublicKey(),
                displayName: activeProfile.name,
                avatarUrl: normalizeAvatarForPayload(activeProfile.avatar),
                matchId: id,
                choice,
                cups,
                starType: starType || null,
                predictedHomeScore: scorePrediction ? scorePrediction.predictedHomeScore : null,
                predictedAwayScore: scorePrediction ? scorePrediction.predictedAwayScore : null,
                correctScoreOdds: scorePrediction ? scorePrediction.correctScoreOdds : null
            });
        } catch (error) {
            showToast("Chưa gởi được dự đoán", error.message || "Vui lòng thử lại.");
            return;
        }

        mergeMyPick(id, choice, cups);
        if (starType) {
            state.myPicks[id] = state.myPicks[id] || {};
            state.myPicks[id].starType = starType;
        }
        setMyPickScore(id, scorePrediction);
        bumpBettorCount(id, choice, 1);

        state.selected[id] = choice;
        renderMatches();

        if (response && response.activity) {
            handleActivityAdded(response.activity);
        }

        if (response && typeof response.hopeStars !== "undefined") {
            state.hopeStars = Number(response.hopeStars) || 0;
        }
        if (response && typeof response.devilStars !== "undefined") {
            state.devilStars = Number(response.devilStars) || 0;
        }
        renderStarItems();

        void refreshMyPredictions();
        void refreshPlayerHistory();
        showToast(isChange ? "Đã đổi kèo" : "Đã gởi dự đoán", `${activeProfile.name} đã chọn ${choiceLabel}${scorePrediction ? ` · tỉ số 90p ${scorePrediction.predictedHomeScore}-${scorePrediction.predictedAwayScore}` : ""}.`);
    }

    function setAnalysisChat({ title, content, notice, loading = false }) {
        const chat = shell.querySelector("[data-analysis-chat]");
        const titleNode = shell.querySelector("[data-analysis-title]");
        const contentNode = shell.querySelector("[data-analysis-content]");
        const noticeNode = shell.querySelector("[data-analysis-notice]");

        if (!chat || !titleNode || !contentNode || !noticeNode) {
            return;
        }

        chat.hidden = false;
        chat.classList.toggle("is-loading", loading);
        titleNode.textContent = title || "Đang phân tích";
        contentNode.textContent = content || "";
        const visibleNotice = typeof notice === "string" ? notice.trim() : "";
        noticeNode.textContent = visibleNotice;
        noticeNode.hidden = !visibleNotice;
    }

    function applyAnalysisProbability(matchId, probability) {
        if (!matchId || !probability) {
            return;
        }

        const next = {
            home: Number(probability.home),
            draw: Number(probability.draw),
            away: Number(probability.away)
        };

        if (!Number.isFinite(next.home) || !Number.isFinite(next.draw) || !Number.isFinite(next.away)) {
            return;
        }

        state.weights[matchId] = next;
        document.querySelectorAll(".match-card").forEach((card) => {
            if (card.dataset.matchId !== matchId) {
                return;
            }

            card.dataset.homeWeight = String(next.home);
            card.dataset.drawWeight = String(next.draw);
            card.dataset.awayWeight = String(next.away);
            card.dataset.aiHome = String(next.home);
            card.dataset.aiDraw = String(next.draw);
            card.dataset.aiAway = String(next.away);
        });

        renderMatches();
    }

    async function requestExpertAnalysis(card) {
        const matchId = card.dataset.matchId;
        if (analysisRequests.has(matchId)) {
            return analysisRequests.get(matchId);
        }

        const task = runExpertAnalysisRequest(card);
        analysisRequests.set(matchId, task);
        try {
            await task;
        } finally {
            analysisRequests.delete(matchId);
        }
    }

    async function runExpertAnalysisRequest(card) {
        const title = `${card.dataset.homeName} vs ${card.dataset.awayName}`;
        setAnalysisChat({
            title,
            content: "AI đang đọc dữ liệu trận đấu và tạo phân tích ngắn...",
            notice: "Đang kết nối dữ liệu phân tích.",
            loading: true
        });

        try {
            const data = await postJson("/keobia/analysis", { matchId: card.dataset.matchId, force: false });
            setAnalysisChat({
                title: data.title || title,
                content: data.content || "Chưa có nội dung phân tích cho trận này.",
                notice: data.notice || "",
                loading: false
            });
            applyAnalysisProbability(card.dataset.matchId, data.probability);
        } catch (error) {
            setAnalysisChat({
                title,
                content: error.message || "Không lấy được phân tích lúc này.",
                notice: "Vui lòng thử lại sau.",
                loading: false
            });
        }
    }

    async function sendCommunityChat() {
        const input = shell.querySelector("[data-chat-input]");
        const button = shell.querySelector("[data-chat-send]");
        if (!input) {
            return;
        }

        const activeProfile = await ensureResolvedProfile("nhận vào chat cộng đồng");
        if (!activeProfile) {
            return;
        }

        const message = input.value.trim();
        if (!message && !chatState.pendingImageFile) {
            showToast("Chưa có nội dung", "Hãy nhập tin nhắn hoặc chọn một ảnh để gửi.");
            return;
        }

        if (button) {
            button.disabled = true;
        }

        try {
            let imageUrl = null;
            if (chatState.pendingImageFile) {
                imageUrl = await uploadChatImageFile(chatState.pendingImageFile);
            }

            const response = await postJson("/keobia/chat", {
                publicKey: activeProfile.publicKey || getPublicKey(),
                displayName: activeProfile.name,
                avatarUrl: normalizeAvatarForPayload(activeProfile.avatar),
                message,
                imageUrl
            });

            if (response && response.message) {
                appendChatMessage(response.message, {
                    scrollToBottom: true,
                    silent: true
                });
            }

            input.value = "";
            updateChatCounter();
            resetPendingChatImage();
            closeChatEmojiPanel();
            renderChat(true);
        } catch (error) {
            showToast("Chưa gởi được chat", error.message || "Vui lòng thử lại.");
        } finally {
            if (button) {
                button.disabled = false;
            }
        }
    }

    function initCommunityChat() {
        setChatLauncherState();
        syncChatUnread();
        updateChatCounter();
        renderChat();
        resetPendingChatImage();
        closeChatEmojiPanel();

        const launcher = shell.querySelector("[data-chat-toggle]");
        const close = shell.querySelector("[data-chat-close]");
        const emojiToggle = shell.querySelector("[data-chat-emoji-toggle]");
        const emojiPanel = shell.querySelector("[data-chat-emoji-panel]");
        const input = shell.querySelector("[data-chat-input]");
        const upload = shell.querySelector("[data-chat-image-upload]");
        const removeImage = shell.querySelector("[data-chat-image-remove]");
        const send = shell.querySelector("[data-chat-send]");

        if (launcher) {
            launcher.addEventListener("click", openCommunityChat);
        }

        if (close) {
            close.addEventListener("click", closeCommunityChat);
        }

        if (emojiToggle && emojiPanel) {
            emojiToggle.addEventListener("click", () => {
                chatState.emojiOpen = !chatState.emojiOpen;
                emojiPanel.hidden = !chatState.emojiOpen;
            });
        }

        shell.querySelectorAll("[data-chat-emoji]").forEach((button) => {
            button.addEventListener("click", () => {
                if (!input) {
                    return;
                }

                const emoji = button.getAttribute("data-chat-emoji") || "";
                const start = Number.isInteger(input.selectionStart) ? input.selectionStart : input.value.length;
                const end = Number.isInteger(input.selectionEnd) ? input.selectionEnd : input.value.length;
                input.value = `${input.value.slice(0, start)}${emoji}${input.value.slice(end)}`;
                input.focus();
                const cursor = start + emoji.length;
                input.setSelectionRange(cursor, cursor);
                updateChatCounter();
            });
        });

        if (input) {
            input.addEventListener("input", updateChatCounter);
            input.addEventListener("keydown", (event) => {
                if (event.key === "Enter" && !event.shiftKey) {
                    event.preventDefault();
                    void sendCommunityChat();
                }
            });
        }

        if (upload) {
            upload.addEventListener("change", () => {
                const file = upload.files && upload.files[0];
                if (!file) {
                    return;
                }

                if (!file.type.startsWith("image/")) {
                    showToast("Sai định dạng", "Chỉ hỗ trợ tải ảnh vào chat cộng đồng.");
                    upload.value = "";
                    return;
                }

                if (file.size > 6 * 1024 * 1024) {
                    showToast("Ảnh quá lớn", "Ảnh chat cần nhỏ hơn 6MB.");
                    upload.value = "";
                    return;
                }

                setPendingChatImage(file);
                openCommunityChat();
            });
        }

        if (removeImage) {
            removeImage.addEventListener("click", resetPendingChatImage);
        }

        if (send) {
            send.addEventListener("click", () => {
                void sendCommunityChat();
            });
        }
    }

    function initState() {
        document.querySelectorAll(".match-card").forEach((card) => {
            const id = card.dataset.matchId;
            // The meter always shows the static AI prediction (the server falls back to
            // the base prior when no analysis exists), so it never moves when bets land.
            state.weights[id] = {
                home: Number(card.dataset.aiHome) || 0,
                draw: Number(card.dataset.aiDraw) || 0,
                away: Number(card.dataset.aiAway) || 0
            };
            state.cups[id] = Number(card.dataset.defaultCups) || 1;
            state.selected[id] = "home";
        });

        state.feed = Array.from(document.querySelectorAll("[data-feed-seed]")).map((node) => ({
            id: node.getAttribute("data-feed-id") || "",
            avatar: node.getAttribute("data-avatar") || getDefaultAvatar(),
            text: node.getAttribute("data-feed-seed") || "",
            time: node.getAttribute("data-feed-time") || "Vá»«a xong",
            choice: node.getAttribute("data-feed-choice") || "home",
            choiceLabel: node.getAttribute("data-feed-choice-label") || "Dự Ä‘oán",
            badge: node.getAttribute("data-feed-badge") || "sports_bar"
        }));
        realtimeState.seenActivityIds = new Set(state.feed.map((item) => item.id).filter(Boolean));

        state.chat = Array.from(document.querySelectorAll("[data-chat-seed-id]"))
            .map((node) => normalizeChatItem({
                id: node.getAttribute("data-chat-seed-id"),
                playerId: node.getAttribute("data-chat-seed-player-id"),
                publicKey: node.getAttribute("data-chat-seed-public-key"),
                playerName: node.getAttribute("data-chat-seed-player-name"),
                avatarUrl: node.getAttribute("data-chat-seed-avatar"),
                message: node.getAttribute("data-chat-seed-message"),
                imageUrl: node.getAttribute("data-chat-seed-image"),
                time: node.getAttribute("data-chat-seed-time"),
                createdAt: node.getAttribute("data-chat-seed-created-at")
            }))
            .filter(Boolean)
            .slice(-maxChatMessages);

        realtimeState.seenChatIds = new Set(state.chat.map((item) => item.id));
    }

    function initAvatarUpload() {
        const upload = shell.querySelector("[data-avatar-upload]");
        const applyButton = shell.querySelector("[data-cropper-apply]");
        const cancelButton = shell.querySelector("[data-cropper-cancel]");

        if (upload) {
            upload.addEventListener("change", () => {
                const file = upload.files && upload.files[0];
                if (!file) {
                    return;
                }

                if (!file.type.startsWith("image/")) {
                    showToast("Không đúng định dạng", "Hãy chọn một file ảnh để làm avatar.");
                    upload.value = "";
                    return;
                }

                const CropperCtor = getCropperConstructor();
                if (!CropperCtor) {
                    showToast("Không tải được CropperJS", "Bạn vẫn có thể dùng avatar mẫu, hoặc thử tải lại trang.");
                    upload.value = "";
                    return;
                }

                resetCropper();

                const cropperShell = shell.querySelector("[data-cropper-shell]");
                const cropperFrame = shell.querySelector("[data-cropper-frame]");
                const image = shell.querySelector("[data-cropper-image]");

                if (!cropperShell || !cropperFrame || !image) {
                    return;
                }

                cropperShell.hidden = false;
                const objectUrl = URL.createObjectURL(file);
                cropperState.objectUrl = objectUrl;

                let initialized = false;
                const initializeCropper = () => {
                    if (initialized || cropperState.objectUrl !== objectUrl) {
                        return;
                    }
                    initialized = true;

                    window.requestAnimationFrame(() => {
                        if (cropperState.objectUrl !== objectUrl) {
                            return;
                        }
                        destroyCropperInstance(cropperState.cropper);
                        cropperState.cropper = new CropperCtor(image, {
                            aspectRatio: 1,
                            viewMode: 1,
                            dragMode: "move",
                            autoCropArea: 0.82,
                            background: false,
                            responsive: true,
                            restore: false,
                            guides: true,
                            center: true,
                            cropBoxMovable: true,
                            cropBoxResizable: true,
                            toggleDragModeOnDblclick: false
                        });
                    });
                };

                image.onload = initializeCropper;
                image.onerror = () => {
                    if (cropperState.objectUrl !== objectUrl) {
                        return;
                    }
                    resetCropper();
                    showToast("Không đọc được ảnh", "Hãy thử chọn một ảnh JPG, PNG hoặc WebP khác.");
                };
                image.src = objectUrl;

                if (image.complete && image.naturalWidth > 0) {
                    initializeCropper();
                }
            });
        }

        if (applyButton) {
            applyButton.addEventListener("click", async () => {
                if (!cropperState.cropper || typeof cropperState.cropper.getCroppedCanvas !== "function") {
                    showToast("Chưa crop được ảnh", "Hãy kéo vùng chọn 1:1 hoặc chọn lại ảnh.");
                    return;
                }

                try {
                    const canvas = cropperState.cropper.getCroppedCanvas({
                        width: 320,
                        height: 320,
                        imageSmoothingEnabled: true,
                        imageSmoothingQuality: "high"
                    });

                    if (!canvas) {
                        throw new Error("Canvas unavailable");
                    }

                    setPendingAvatar(canvas.toDataURL("image/png"));
                    resetCropper();
                    showToast("Đã crop avatar", "Ảnh đại diện sẽ dùng tỉ lệ 1:1.");
                } catch {
                    showToast("Không crop được ảnh", "Ảnh này có thể quá lớn hoặc trình duyệt không hỗ trợ canvas.");
                }
            });
        }

        if (cancelButton) {
            cancelButton.addEventListener("click", resetCropper);
        }
    }

    function normalizeFilterText(value) {
        return (value || "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase()
            .trim();
    }

    function initScheduleFilters() {
        const filters = shell.querySelector("[data-schedule-filters]");
        if (!filters) {
            return;
        }

        const search = filters.querySelector("[data-schedule-search]");
        const stage = filters.querySelector("[data-schedule-stage]");
        const status = filters.querySelector("[data-schedule-status]");
        const hot = filters.querySelector("[data-schedule-hot]");
        const reset = filters.querySelector("[data-schedule-reset]");
        const rows = Array.from(shell.querySelectorAll("[data-schedule-row]"));
        const count = shell.querySelector("[data-schedule-count]");
        const empty = shell.querySelector("[data-schedule-empty]");

        const apply = () => {
            const query = normalizeFilterText(search && search.value);
            const selectedStage = stage && stage.value ? stage.value : "";
            const selectedStatus = status && status.value ? status.value : "";
            const hotOnly = Boolean(hot && hot.checked);
            let visible = 0;

            rows.forEach((row) => {
                const teams = normalizeFilterText(`${row.dataset.homeName || ""} ${row.dataset.awayName || ""}`);
                const isFinished = row.dataset.finished === "true";
                const matchesTeam = !query || teams.includes(query);
                const matchesStage = !selectedStage || row.dataset.stage === selectedStage;
                const matchesStatus = !selectedStatus
                    || (selectedStatus === "finished" ? isFinished : !isFinished);
                const matchesHot = !hotOnly || row.dataset.hot === "true";
                const show = matchesTeam && matchesStage && matchesStatus && matchesHot;

                row.hidden = !show;
                if (show) {
                    visible += 1;
                }
            });

            if (count) {
                count.textContent = `Hiển thị ${visible}/${rows.length} trận`;
            }
            if (empty) {
                empty.hidden = visible !== 0;
            }
        };

        if (search) {
            search.addEventListener("input", apply);
        }
        if (stage) {
            stage.addEventListener("change", apply);
        }
        if (status) {
            status.addEventListener("change", apply);
        }
        if (hot) {
            hot.addEventListener("change", apply);
        }
        if (reset) {
            reset.addEventListener("click", () => {
                if (search) search.value = "";
                if (stage) stage.value = "";
                if (status) status.value = "";
                if (hot) hot.checked = false;
                apply();
            });
        }

        apply();
    }

    function initEvents() {
        document.querySelectorAll("[data-open-modal]").forEach((button) => {
            button.addEventListener("click", () => openModal(button.getAttribute("data-open-modal")));
        });

        document.querySelectorAll("[data-player-history-open]").forEach((button) => {
            button.addEventListener("click", openPlayerHistory);
        });

        shell.addEventListener("click", (event) => {
            const button = event.target.closest("[data-delete-player-bet]");
            if (!button || button.disabled) {
                return;
            }

            event.preventDefault();
            void deletePlayerHistoryBet(button.getAttribute("data-delete-player-bet"));
        });

        document.querySelectorAll("[data-profile-open]").forEach((button) => {
            button.addEventListener("click", (event) => {
                event.preventDefault();
                event.stopPropagation();
                openProfileEditor();
            });
        });

        document.querySelectorAll("[data-close-modal]").forEach((button) => {
            button.addEventListener("click", () => {
                const modal = button.closest(".modal");
                if (modal) {
                    modal.classList.remove("is-open");
                    modal.setAttribute("aria-hidden", "true");
                }
            });
        });

        document.querySelectorAll("[data-unit-transition-continue]").forEach((button) => {
            button.addEventListener("click", () => {
                void submitUnitTransitionDecision(false);
            });
        });

        document.querySelectorAll("[data-unit-transition-stop]").forEach((button) => {
            button.addEventListener("click", () => {
                void submitUnitTransitionDecision(true);
            });
        });

        document.querySelectorAll("[data-lossboard-player]").forEach((row) => {
            row.addEventListener("click", (event) => {
                if (closestFromEventTarget(event.target, "[data-share-beer]")) {
                    event.preventDefault();
                    event.stopPropagation();
                    return;
                }

                const publicKey = row.getAttribute("data-lossboard-player");
                const playerName = row.getAttribute("data-lossboard-name") || "Người chơi";
                if (publicKey) {
                    openLossboardPlayerHistory(publicKey, playerName);
                }
            });
        });

        document.querySelectorAll(".match-card").forEach((card) => {
            const id = card.dataset.matchId;

            card.querySelectorAll("[data-choice-button]").forEach((button) => {
                button.addEventListener("click", () => {
                    state.selected[id] = button.getAttribute("data-choice");
                    renderMatches();
                });
            });

            card.querySelectorAll("[data-submit-bet]").forEach((button) => {
                button.addEventListener("click", () => submitBet(card));
            });

            card.querySelectorAll("[data-history-button]").forEach((button) => {
                button.addEventListener("click", () => openPredictionHistory(card));
            });

            card.querySelectorAll("[data-analysis-button]").forEach((button) => {
                button.addEventListener("click", () => requestExpertAnalysis(card));
            });
        });

        shell.querySelectorAll("[data-history-filter]").forEach((button) => {
            button.addEventListener("click", () => {
                if (!historyState.matchId) {
                    return;
                }

                const card = document.querySelector(`.match-card[data-match-id="${historyState.matchId}"]`);
                if (!card) {
                    return;
                }

                void requestPredictionHistory(card, button.getAttribute("data-history-filter") || "");
            });
        });

        shell.querySelectorAll("[data-analysis-close]").forEach((button) => {
            button.addEventListener("click", () => {
                const chat = shell.querySelector("[data-analysis-chat]");
                if (chat) {
                    chat.hidden = true;
                    chat.classList.remove("is-loading");
                }
            });
        });

        shell.querySelectorAll("[data-avatar-option]").forEach((button) => {
            button.addEventListener("click", () => {
                resetCropper();
                setPendingAvatar(button.getAttribute("data-avatar"));
            });
        });

        shell.querySelectorAll("[data-save-profile]").forEach((button) => {
            button.addEventListener("click", () => {
                const input = shell.querySelector("[data-player-input]");
                const name = input && input.value.trim().length >= 2 ? input.value.trim() : "MinhBia18";
                const avatar = cropperState.pendingAvatar || getDefaultAvatar();
                saveProfile({ publicKey: state.profile.publicKey || getPublicKey(), name, avatar });
            });
        });

        shell.querySelectorAll("[data-stop-playing]").forEach((button) => {
            button.addEventListener("click", () => {
                void confirmStopPlaying({ modalName: "profile" });
            });
        });

        initScheduleFilters();
        initAvatarUpload();
    }

    syncContributionCopy();
    initState();
    initEvents();
    initCommunityChat();
    renderProfile();
    void checkUnitTransitionNotice();
    renderMatches();
    refreshLocks();
    setInterval(refreshLocks, 30000);
    renderFeed();
    renderStarItems();
    void refreshMyPredictions();
    void refreshPlayerHistory();
    openProfileForNewVisitor();
    initRealtime();

    shell.addEventListener("click", function(event) {
        var starBtn = event.target.closest("[data-star-type]");
        if (!starBtn) return;
        event.preventDefault();

        var count = parseInt(starBtn.getAttribute("data-star-count") || "0", 10);
        if (count <= 0) {
            var tooltip = document.createElement("div");
            tooltip.className = "star-tooltip";
            tooltip.textContent = "Hết vật phẩm! Chơi thêm để nhận nhé.";
            starBtn.style.position = "relative";
            starBtn.appendChild(tooltip);
            setTimeout(function() { tooltip.remove(); }, 2000);
            return;
        }

        var card = starBtn.closest(".match-card");
        if (card && !state.selected[card.dataset.matchId]) {
            var choices = card.querySelector(".mc-choices");
            if (choices) {
                choices.classList.add("mc-choices--shake");
                setTimeout(function() { choices.classList.remove("mc-choices--shake"); }, 500);
            }
            showToast("Chưa chọn đội", "Hãy chọn đội trước khi dùng vật phẩm.");
            return;
        }

        var type = starBtn.getAttribute("data-star-type");
        var isActive = starBtn.classList.contains("is-active");
        shell.querySelectorAll("[data-match-items] [data-star-type]").forEach(function(b) {
            b.classList.remove("is-active");
        });
        if (isActive) {
            selectedStarType = null;
        } else {
            starBtn.classList.add("is-active");
            selectedStarType = type;
        }
    });

    shell.addEventListener("click", function(event) {
        var btn = closestFromEventTarget(event.target, "[data-share-beer]");
        if (!btn) return;
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
        openShareBeerModal(btn);
    }, true);

    document.querySelectorAll(".share-beer-btn").forEach(function(btn) {
        btn.addEventListener("click", function(event) {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();
            openShareBeerModal(btn);
        });
    });

    var confirmBtn = document.getElementById("shareBeerConfirm");
    if (confirmBtn) {
        confirmBtn.addEventListener("click", async function() {
            var toKey = document.getElementById("shareBeerToKey");
            var cupsInput = document.getElementById("shareBeerCups");
            if (!toKey || !cupsInput) return;
            var publicKey = state.profile && state.profile.publicKey ? state.profile.publicKey : getPublicKey();
            if (!publicKey) {
                showToast("Chưa tặng được", "Bạn cần có hồ sơ người chơi.");
                return;
            }
            confirmBtn.disabled = true;
            try {
                var data = await postJson("/keobia/share-beer", {
                    fromPublicKey: publicKey,
                    toPublicKey: toKey.value,
                    cups: parseInt(cupsInput.value, 10) || 1
                });

                closeModal("share-beer");
                var cups = Number(data.cups) || (parseInt(cupsInput.value, 10) || 1);
                var toName = data.toDisplayName || shareBeerName || "người chơi";
                var lostCups = Number(data.fromNewLostCups);
                var shareUnit = getUnitMeta(data.unitCode || currentUnitCode);
                var suffix = Number.isFinite(lostCups)
                    ? " Bạn đang còn thiếu " + formatUnitBreakdown(data.fromLostBeerCups, data.fromLostPeanutPacks) + "."
                    : "";
                showToast("Đã tặng " + shareUnit.full + "!", "Tặng " + formatStatNumber(cups) + " " + shareUnit.full + " cho " + toName + "." + suffix);
                setTimeout(function() { location.reload(); }, 1500);
            } catch (err) {
                showToast("Không tặng được", err.message || "Vui lòng thử lại.");
            } finally {
                confirmBtn.disabled = false;
            }
        });
    }

    document.addEventListener("click", function(event) {
        var step = event.target.closest("[data-step]");
        if (!step) return;
        var input = document.getElementById("shareBeerCups");
        if (!input) return;
        var delta = parseInt(step.getAttribute("data-step"), 10) || 0;
        var next = Math.max(1, Math.min(99, (parseInt(input.value, 10) || 1) + delta));
        input.value = String(next);
    });

    document.addEventListener("click", function(event) {
        var preset = event.target.closest("[data-cups]");
        if (!preset || !preset.classList.contains("share-beer-preset")) return;
        var cups = parseInt(preset.getAttribute("data-cups"), 10) || 1;
        var input = document.getElementById("shareBeerCups");
        if (input) input.value = String(cups);
        document.querySelectorAll(".share-beer-preset").forEach(function(el) {
            el.classList.toggle("is-active", el === preset);
        });
    });

    function updateNotifTimes() {
        var now = Date.now();
        document.querySelectorAll(".notif-time[data-created-at]").forEach(function(el) {
            var iso = el.getAttribute("data-created-at");
            if (!iso) return;
            var ts = new Date(iso).getTime();
            if (isNaN(ts)) return;
            var diff = Math.floor((now - ts) / 1000);
            if (diff < 60) el.textContent = "vừa xong";
            else if (diff < 3600) el.textContent = Math.floor(diff / 60) + " phút trước";
            else if (diff < 86400) el.textContent = Math.floor(diff / 3600) + " giờ trước";
            else el.textContent = Math.floor(diff / 86400) + " ngày trước";
        });
    }
    updateNotifTimes();
    setInterval(updateNotifTimes, 30000);

    // ---- Quick Q&A (Hỏi đáp nhanh) popup ----
    function initQuizPopup() {
        const dismissKey = "keobia2026-quiz-dismissed";
        function dismissedIds() {
            try { return JSON.parse(sessionStorage.getItem(dismissKey) || "[]"); }
            catch (e) { return []; }
        }
        function dismiss(id) {
            try {
                const list = dismissedIds();
                if (list.indexOf(id) === -1) { list.push(id); sessionStorage.setItem(dismissKey, JSON.stringify(list)); }
            } catch (e) { /* sessionStorage unavailable */ }
        }
        async function postJson(url, body) {
            try {
                const res = await fetch(url, {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(body || {})
                });
                const data = await res.json().catch(() => ({}));
                return { ok: res.ok, data: data };
            } catch (e) { return { ok: false, data: {} }; }
        }
        function esc(s) {
            const d = document.createElement("div");
            d.textContent = s == null ? "" : String(s);
            return d.innerHTML;
        }
        function ownPublicKey() {
            return (state.profile && state.profile.publicKey) || getPublicKey();
        }

        let overlay = null;
        function card() { return overlay ? overlay.querySelector(".quiz-card") : null; }
        function ensureOverlay() {
            if (overlay) return overlay;
            overlay = document.createElement("div");
            overlay.className = "quiz-modal";
            overlay.innerHTML = '<div class="quiz-card" role="dialog" aria-modal="true"></div>';
            document.body.appendChild(overlay);
            return overlay;
        }
        function closeOverlay() { if (overlay) { overlay.remove(); overlay = null; } }

        function renderResults(host, q) {
            const quizUnit = getUnitMeta(q.unitCode || currentUnitCode);
            const penaltyText = (q.penaltyCups || 0) > 0 ? 'sai mất ' + (q.penaltyCups || 0) + ' ' + quizUnit.short : 'sai không mất gì';
            const total = q.choices.reduce(function (a, c) { return a + (c.votes || 0); }, 0) || 1;
            const rows = q.choices.map(function (c) {
                const pct = Math.round(((c.votes || 0) / total) * 100);
                const mine = q.myChoiceKey && q.myChoiceKey.toLowerCase() === c.key.toLowerCase();
                const right = q.correctChoiceKey && q.correctChoiceKey.toLowerCase() === c.key.toLowerCase();
                return '<div class="quiz-result-row' + (right ? ' is-correct' : '') + '">' +
                    '<div class="quiz-result-bar" style="width:' + pct + '%"></div>' +
                    '<span class="quiz-result-label">' + esc(c.label) + (mine ? ' 👈' : '') + (right ? ' ✓' : '') + '</span>' +
                    '<span class="quiz-result-count">' + (c.votes || 0) + '</span></div>';
            }).join("");
            const voters = (q.voters || []).map(function (v) {
                const choice = (q.choices || []).find(function (c) { return c.key.toLowerCase() === String(v.choiceKey || '').toLowerCase(); });
                return '<span class="quiz-voter">' + esc(v.name) + ' → <b>' + esc(choice ? choice.label : v.choiceKey) + '</b></span>';
            }).join("");
            host.innerHTML =
                '<button class="quiz-close" type="button" aria-label="Đóng">×</button>' +
                '<div class="quiz-eyebrow">📊 Kết quả · đúng được giảm ' + (q.rewardCups || 0) + ' ' + quizUnit.short + ' · ' + penaltyText + '</div>' +
                '<h3 class="quiz-question">' + esc(q.text) + '</h3>' +
                '<div class="quiz-results">' + rows + '</div>' +
                (voters ? '<div class="quiz-voters">' + voters + '</div>' : '<p class="quiz-note">Chưa ai bình chọn.</p>') +
                '<button class="quiz-next" type="button">Tiếp tục →</button>';
        }

        function renderQuestion(host, q, onVoted, onSkip) {
            const linked = !!(state.profile && state.profile.telegramId);
            const quizUnit = getUnitMeta(q.unitCode || currentUnitCode);
            const penaltyText = (q.penaltyCups || 0) > 0 ? 'sai mất ' + (q.penaltyCups || 0) + ' ' + quizUnit.short : 'sai không mất gì';
            const buttons = q.choices.map(function (c) {
                return '<button class="quiz-choice" type="button" data-key="' + esc(c.key) + '">' + esc(c.label) + '</button>';
            }).join("");
            host.innerHTML =
                '<button class="quiz-close" type="button" aria-label="Đóng">×</button>' +
                '<div class="quiz-eyebrow">❓ Câu hỏi nhanh · đúng được giảm ' + (q.rewardCups || 0) + ' ' + quizUnit.short + ' · ' + penaltyText + '</div>' +
                '<h3 class="quiz-question">' + esc(q.text) + '</h3>' +
                (linked ? '' : '<p class="quiz-note">Cần đăng nhập Telegram để bình chọn.</p>') +
                '<div class="quiz-choices">' + buttons + '</div>';
            host.querySelector(".quiz-close").onclick = onSkip;
            host.querySelectorAll(".quiz-choice").forEach(function (btn) {
                btn.onclick = async function () {
                    if (!(state.profile && state.profile.telegramId)) {
                        openTelegramOidcLogin();
                        showToast("Đăng nhập Telegram", "Đăng nhập xong rồi chọn lại đáp án nhé.");
                        return;
                    }
                    host.querySelectorAll(".quiz-choice").forEach(function (b) { b.disabled = true; });
                    const r = await postJson("/keobia/quiz/vote", {
                        publicKey: ownPublicKey(),
                        questionId: q.id,
                        choiceKey: btn.dataset.key
                    });
                    if (!r.ok || !r.data || !r.data.ok) {
                        showToast("Không bình chọn được", (r.data && r.data.error) || "Vui lòng thử lại.");
                        host.querySelectorAll(".quiz-choice").forEach(function (b) { b.disabled = false; });
                        return;
                    }
                    onVoted(r.data.question);
                };
            });
        }

        function run(queue, index) {
            if (index >= queue.length) { closeOverlay(); return; }
            const q = queue[index];
            ensureOverlay();
            const host = card();
            function next() { dismiss(q.id); run(queue, index + 1); }
            function showResults(updated) {
                postJson("/keobia/quiz/votes", { questionId: q.id }).then(function (res) {
                    const merged = (res.ok && res.data && res.data.ok)
                        ? { text: q.text, rewardCups: res.data.rewardCups, penaltyCups: res.data.penaltyCups, unitCode: res.data.unitCode || q.unitCode, choices: res.data.choices, voters: res.data.voters, myChoiceKey: updated.myChoiceKey, correctChoiceKey: res.data.correctChoiceKey }
                        : updated;
                    renderResults(host, merged);
                    host.querySelector(".quiz-close").onclick = next;
                    host.querySelector(".quiz-next").onclick = next;
                });
            }
            if (q.hasVoted) { showResults(q); return; }
            renderQuestion(host, q, showResults, next);
        }

        (async function () {
            const r = await postJson("/keobia/quiz/active", { publicKey: ownPublicKey() });
            if (!r.ok || !r.data || !r.data.ok || !Array.isArray(r.data.questions)) return;
            const skip = dismissedIds();
            const queue = r.data.questions.filter(function (q) { return skip.indexOf(q.id) === -1; });
            if (queue.length) run(queue, 0);
        })();
    }
    initQuizPopup();

})();
