window.studioInterop = (() => {
    let audioCtx = null;
    let targetVuLeft = 0;
    let targetVuRight = 0;
    let vuLeftLevel = 0;
    let vuRightLevel = 0;
    let vuLeftPeak = 0;
    let vuRightPeak = 0;
    let vuLeftPeakTimer = null;
    let vuRightPeakTimer = null;
    let lastVuTime = performance.now();
    let vuLoopRunning = false;

    let vectorOrbitAnimId = null;
    let vectorOrbitStartTime = 0;
    let vectorStateX = 0;
    let vectorStateY = 0;
    let lastOrbitDispatchTime = 0;

    function loadInitialPreferences() {
        return {
            isDark: localStorage.getItem("yamaha_studio_theme") !== "light",
            handheld: localStorage.getItem("yamaha_studio_handheld") === "true",
            model: localStorage.getItem("yamaha_studio_model") || "pss790",
            guideOpen: localStorage.getItem("yamaha_studio_guide_open") === "true"
        };
    }

    function applyBodyPreferences(isDark, handheld, model) {
        document.body.classList.toggle("theme-light", !isDark);
        document.body.classList.toggle("handheld-touch-mode", !!handheld);
        localStorage.setItem("yamaha_studio_theme", isDark ? "dark" : "light");
        localStorage.setItem("yamaha_studio_handheld", handheld ? "true" : "false");
        if (model) {
            localStorage.setItem("yamaha_studio_model", model);
        }
    }

    function saveGuidePreference(open) {
        localStorage.setItem("yamaha_studio_guide_open", open ? "true" : "false");
    }

    function ensureAudioContext() {
        if (!audioCtx) {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (Ctx) audioCtx = new Ctx();
        }
        if (audioCtx && audioCtx.state === "suspended") {
            audioCtx.resume().catch(() => { });
        }
        return audioCtx;
    }

    function playMetronomeClick(isDownbeat) {
        const ctx = ensureAudioContext();
        if (!ctx) return;

        const now = ctx.currentTime;
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = "sine";
        osc.frequency.setValueAtTime(isDownbeat ? 1200 : 850, now);

        gain.gain.setValueAtTime(0.001, now);
        gain.gain.exponentialRampToValueAtTime(isDownbeat ? 0.35 : 0.2, now + 0.004);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.06);

        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start(now);
        osc.stop(now + 0.07);
    }

    function startVuLoop() {
        if (vuLoopRunning) return;
        vuLoopRunning = true;

        function tick(now) {
            const dt = Math.min((now - lastVuTime) / 1000, 0.1);
            lastVuTime = now;

            vuLeftLevel = targetVuLeft > vuLeftLevel ? targetVuLeft : Math.max(0, vuLeftLevel - dt * 18);
            vuRightLevel = targetVuRight > vuRightLevel ? targetVuRight : Math.max(0, vuRightLevel - dt * 18);
            targetVuLeft = Math.max(0, targetVuLeft - dt * 12);
            targetVuRight = Math.max(0, targetVuRight - dt * 12);

            if (vuLeftLevel > vuLeftPeak) {
                vuLeftPeak = vuLeftLevel;
                if (vuLeftPeakTimer) clearTimeout(vuLeftPeakTimer);
                vuLeftPeakTimer = setTimeout(() => { vuLeftPeak = 0; }, 800);
            }
            if (vuRightLevel > vuRightPeak) {
                vuRightPeak = vuRightLevel;
                if (vuRightPeakTimer) clearTimeout(vuRightPeakTimer);
                vuRightPeakTimer = setTimeout(() => { vuRightPeak = 0; }, 800);
            }

            const litL = Math.round(vuLeftLevel);
            const peakL = Math.round(vuLeftPeak);
            const litR = Math.round(vuRightLevel);
            const peakR = Math.round(vuRightPeak);

            for (let i = 0; i < 12; i++) {
                const segL = document.getElementById(`vu-l-${i}`);
                if (segL) {
                    segL.classList.toggle("lit", i < litL);
                    segL.classList.toggle("peak", i === peakL - 1 && peakL > 0);
                }
                const segR = document.getElementById(`vu-r-${i}`);
                if (segR) {
                    segR.classList.toggle("lit", i < litR);
                    segR.classList.toggle("peak", i === peakR - 1 && peakR > 0);
                }
            }

            requestAnimationFrame(tick);
        }

        requestAnimationFrame(tick);
    }

    function feedVuMeter(velocity, channel) {
        startVuLoop();
        if (velocity <= 0) return;
        const normalized = Math.min(velocity / 127.0, 1.0);
        const totalSegments = normalized * 12.0;

        let pan = 0.5;
        if (channel !== undefined && channel !== null && channel !== 15 && channel !== 9) {
            pan = (channel % 2 === 0) ? 0.35 : 0.65;
        }
        targetVuLeft = Math.max(targetVuLeft, Math.min(12, totalSegments * (1 - pan) * 2));
        targetVuRight = Math.max(targetVuRight, Math.min(12, totalSegments * pan * 2));
    }

    function calculateVectorWeights(x, y) {
        const rawA = Math.max(0, y), rawB = Math.max(0, x), rawC = Math.max(0, -y), rawD = Math.max(0, -x);
        const manhattan = Math.abs(x) + Math.abs(y);
        const radius = Math.min(1.0, Math.sqrt(x * x + y * y));
        let dirA = 0.25, dirB = 0.25, dirC = 0.25, dirD = 0.25;
        if (manhattan > 0.0001) {
            dirA = rawA / manhattan;
            dirB = rawB / manhattan;
            dirC = rawC / manhattan;
            dirD = rawD / manhattan;
        }
        return {
            wA: (1 - radius) * 0.25 + radius * dirA,
            wB: (1 - radius) * 0.25 + radius * dirB,
            wC: (1 - radius) * 0.25 + radius * dirC,
            wD: (1 - radius) * 0.25 + radius * dirD
        };
    }

    function drawVectorCanvas(canvasId, x, y) {
        vectorStateX = x;
        vectorStateY = y;
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        const dpr = window.devicePixelRatio || 1;
        const logicalW = 240, logicalH = 240;
        if (canvas.width !== logicalW * dpr || canvas.height !== logicalH * dpr) {
            canvas.width = logicalW * dpr;
            canvas.height = logicalH * dpr;
            canvas.style.width = logicalW + "px";
            canvas.style.height = logicalH + "px";
        }

        ctx.save();
        ctx.scale(dpr, dpr);
        ctx.clearRect(0, 0, logicalW, logicalH);

        const cx = 120, cy = 120, rActive = 96;

        // Outer metallic gimbal collar
        ctx.beginPath();
        ctx.arc(cx, cy, rActive + 12, 0, Math.PI * 2);
        const bezelGrad = ctx.createLinearGradient(cx - rActive, cy - rActive, cx + rActive, cy + rActive);
        bezelGrad.addColorStop(0, "#334155");
        bezelGrad.addColorStop(0.5, "#1e293b");
        bezelGrad.addColorStop(1, "#0f172a");
        ctx.fillStyle = bezelGrad;
        ctx.fill();
        ctx.strokeStyle = "rgba(148, 163, 184, 0.25)";
        ctx.lineWidth = 1.5;
        ctx.stroke();

        // Inner cavity
        ctx.beginPath();
        ctx.arc(cx, cy, rActive, 0, Math.PI * 2);
        const cavityGrad = ctx.createRadialGradient(cx, cy, 6, cx, cy, rActive);
        cavityGrad.addColorStop(0, "#111c33");
        cavityGrad.addColorStop(0.7, "#0c1424");
        cavityGrad.addColorStop(1, "#060a12");
        ctx.fillStyle = cavityGrad;
        ctx.fill();
        ctx.strokeStyle = "rgba(51, 65, 85, 0.8)";
        ctx.lineWidth = 1.2;
        ctx.stroke();

        // Cardinal glows
        const glows = [
            [cx, cy - rActive * 0.7, "6, 182, 212"],
            [cx + rActive * 0.7, cy, "16, 185, 129"],
            [cx, cy + rActive * 0.7, "245, 158, 11"],
            [cx - rActive * 0.7, cy, "244, 63, 94"]
        ];
        glows.forEach(([gx, gy, rgb]) => {
            const g = ctx.createRadialGradient(gx, gy, 4, gx, gy, 50);
            g.addColorStop(0, `rgba(${rgb}, 0.22)`);
            g.addColorStop(1, `rgba(${rgb}, 0)`);
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.arc(gx, gy, 50, 0, Math.PI * 2);
            ctx.fill();
        });

        // Rings & ticks
        [0.25, 0.5, 0.75, 1.0].forEach((r, idx) => {
            ctx.beginPath();
            ctx.arc(cx, cy, rActive * r, 0, Math.PI * 2);
            ctx.strokeStyle = idx === 3 ? "rgba(99, 102, 241, 0.5)" : "rgba(71, 85, 105, 0.3)";
            ctx.lineWidth = idx === 3 ? 1.5 : 0.8;
            ctx.stroke();
        });

        for (let deg = 0; deg < 360; deg += 15) {
            const rad = (deg * Math.PI) / 180;
            const isMajor = deg % 90 === 0;
            const isSemi = deg % 45 === 0;
            const tickLen = isMajor ? 6 : (isSemi ? 4 : 2.5);
            ctx.beginPath();
            ctx.moveTo(cx + Math.cos(rad) * rActive, cy + Math.sin(rad) * rActive);
            ctx.lineTo(cx + Math.cos(rad) * (rActive - tickLen), cy + Math.sin(rad) * (rActive - tickLen));
            ctx.strokeStyle = isMajor ? "rgba(226, 232, 240, 0.7)" : (isSemi ? "rgba(148, 163, 184, 0.5)" : "rgba(100, 116, 139, 0.3)");
            ctx.lineWidth = isMajor ? 1.5 : 1;
            ctx.stroke();
        }

        // Crosshair
        ctx.beginPath();
        ctx.moveTo(cx, cy - rActive); ctx.lineTo(cx, cy - 6);
        ctx.moveTo(cx, cy + 6); ctx.lineTo(cx, cy + rActive);
        ctx.moveTo(cx - rActive, cy); ctx.lineTo(cx - 6, cy);
        ctx.moveTo(cx + 6, cy); ctx.lineTo(cx + rActive, cy);
        ctx.strokeStyle = "rgba(71, 85, 105, 0.4)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(cx, cy, 4, 0, Math.PI * 2);
        ctx.strokeStyle = "rgba(148, 163, 184, 0.6)";
        ctx.stroke();

        // Cardinal letters
        ctx.font = "bold 10px system-ui, sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillStyle = "#22d3ee"; ctx.fillText("A", cx, cy - rActive - 7);
        ctx.fillStyle = "#34d399"; ctx.fillText("B", cx + rActive + 7, cy);
        ctx.fillStyle = "#fbbf24"; ctx.fillText("C", cx, cy + rActive + 7);
        ctx.fillStyle = "#fb7185"; ctx.fillText("D", cx - rActive - 7, cy);

        const puckX = cx + vectorStateX * rActive;
        const puckY = cy - vectorStateY * rActive;

        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(puckX, puckY);
        ctx.strokeStyle = "rgba(99, 102, 241, 0.7)";
        ctx.lineWidth = 2;
        ctx.shadowColor = "#6366f1";
        ctx.shadowBlur = 6;
        ctx.stroke();
        ctx.shadowBlur = 0;

        const w = calculateVectorWeights(vectorStateX, vectorStateY);
        const rCol = Math.round(w.wA * 34 + w.wB * 52 + w.wC * 251 + w.wD * 251);
        const gCol = Math.round(w.wA * 211 + w.wB * 211 + w.wC * 191 + w.wD * 113);
        const bCol = Math.round(w.wA * 238 + w.wB * 153 + w.wC * 36 + w.wD * 133);

        const puckGlow = ctx.createRadialGradient(puckX, puckY, 2, puckX, puckY, 18);
        puckGlow.addColorStop(0, `rgba(${rCol}, ${gCol}, ${bCol}, 0.9)`);
        puckGlow.addColorStop(0.5, `rgba(${rCol}, ${gCol}, ${bCol}, 0.35)`);
        puckGlow.addColorStop(1, `rgba(${rCol}, ${gCol}, ${bCol}, 0)`);
        ctx.fillStyle = puckGlow;
        ctx.beginPath();
        ctx.arc(puckX, puckY, 18, 0, Math.PI * 2);
        ctx.fill();

        ctx.beginPath();
        ctx.arc(puckX, puckY, 10, 0, Math.PI * 2);
        const knobGrad = ctx.createRadialGradient(puckX - 3, puckY - 3, 1, puckX, puckY, 10);
        knobGrad.addColorStop(0, "#f8fafc");
        knobGrad.addColorStop(0.3, "#94a3b8");
        knobGrad.addColorStop(0.75, "#334155");
        knobGrad.addColorStop(1, "#0f172a");
        ctx.fillStyle = knobGrad;
        ctx.fill();
        ctx.strokeStyle = "#cbd5e1";
        ctx.lineWidth = 1.2;
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(puckX, puckY, 6.5, 0, Math.PI * 2);
        ctx.strokeStyle = "rgba(15, 23, 42, 0.7)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(puckX, puckY, 3, 0, Math.PI * 2);
        ctx.fillStyle = `rgb(${rCol}, ${gCol}, ${bCol})`;
        ctx.fill();
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 0.8;
        ctx.stroke();

        ctx.restore();
    }

    function setupVectorTouchpad(canvasId, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || canvas.dataset.bound === "true") return;
        canvas.dataset.bound = "true";

        let dragging = false;
        let lastSend = 0;

        function handlePointer(e, forceSend) {
            const rect = canvas.getBoundingClientRect();
            const scale = (rect.width / 240) * 96;
            let x = (e.clientX - rect.left - rect.width / 2) / Math.max(1, scale);
            let y = (rect.height / 2 - (e.clientY - rect.top)) / Math.max(1, scale);
            const dist = Math.sqrt(x * x + y * y);
            if (dist > 1.0) { x /= dist; y /= dist; }
            stopVectorOrbit();
            drawVectorCanvas(canvasId, x, y);

            const now = performance.now();
            if (forceSend || now - lastSend > 28) {
                lastSend = now;
                dotNetRef.invokeMethodAsync("OnVectorPointerPosition", x, y);
            }
        }

        canvas.addEventListener("pointerdown", (e) => {
            dragging = true;
            try { canvas.setPointerCapture(e.pointerId); } catch { }
            handlePointer(e, true);
        });
        canvas.addEventListener("pointermove", (e) => {
            if (dragging) handlePointer(e, false);
        });
        const stopDrag = (e) => {
            if (!dragging) return;
            dragging = false;
            handlePointer(e, true);
            try { if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId); } catch { }
        };
        canvas.addEventListener("pointerup", stopDrag);
        canvas.addEventListener("pointercancel", stopDrag);
        canvas.addEventListener("dblclick", () => {
            stopVectorOrbit();
            drawVectorCanvas(canvasId, 0, 0);
            dotNetRef.invokeMethodAsync("OnVectorPointerPosition", 0, 0);
        });
    }

    function startVectorOrbit(canvasId, dotNetRef, modeGetter, speedGetter) {
        stopVectorOrbit();
        vectorOrbitStartTime = performance.now();

        function step(now) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) { vectorOrbitAnimId = null; return; }
            const speedSec = parseFloat(document.getElementById(speedGetter)?.value || 30) / 10.0;
            const period = Math.max(0.5, speedSec);
            const theta = ((now - vectorOrbitStartTime) / 1000.0 / period) * (Math.PI * 2);
            const mode = document.getElementById(modeGetter)?.value || "circle";

            let x = 0, y = 0;
            if (mode === "circle") { x = Math.cos(theta) * 0.75; y = Math.sin(theta) * 0.75; }
            else if (mode === "figure8") { x = Math.sin(theta) * 0.8; y = Math.sin(theta * 2) * 0.75; }
            else if (mode === "spiral") { const r = 0.25 + 0.6 * (0.5 + 0.5 * Math.sin(theta * 0.25)); x = Math.cos(theta) * r; y = Math.sin(theta) * r; }
            else if (mode === "pingpong") { const s = Math.sin(theta); x = s * 0.8; y = s * 0.8; }
            else if (mode === "wander") {
                x = Math.max(-0.9, Math.min(0.9, 0.5 * Math.sin(theta) + 0.35 * Math.sin(2.3 * theta + 1.2)));
                y = Math.max(-0.9, Math.min(0.9, 0.5 * Math.cos(0.9 * theta) + 0.35 * Math.cos(1.7 * theta + 2.5)));
            } else {
                vectorOrbitAnimId = null;
                return;
            }

            drawVectorCanvas(canvasId, x, y);
            if (now - lastOrbitDispatchTime > 45) {
                lastOrbitDispatchTime = now;
                dotNetRef.invokeMethodAsync("OnVectorOrbitTick", x, y);
            }
            vectorOrbitAnimId = requestAnimationFrame(step);
        }

        vectorOrbitAnimId = requestAnimationFrame(step);
    }

    function stopVectorOrbit() {
        if (vectorOrbitAnimId) {
            cancelAnimationFrame(vectorOrbitAnimId);
            vectorOrbitAnimId = null;
        }
    }

    function initFmEnvelopeDrag(svgId, isModulator, dotNetRef) {
        const svg = document.getElementById(svgId);
        if (!svg || svg.dataset.bound === "true") return;
        svg.dataset.bound = "true";

        let activeNode = null;
        let lastDragDispatch = 0;

        function getSvgCoords(e) {
            const rect = svg.getBoundingClientRect();
            return {
                x: (e.clientX - rect.left) * (500 / Math.max(1, rect.width)),
                y: (e.clientY - rect.top) * (130 / Math.max(1, rect.height))
            };
        }

        function handleDrag(e, force) {
            if (!activeNode) return;
            const { x, y } = getSvgCoords(e);
            const now = performance.now();
            if (!force && now - lastDragDispatch < 30) return;
            lastDragDispatch = now;
            dotNetRef.invokeMethodAsync("OnEnvelopeNodeDrag", isModulator, activeNode, x, y);
        }

        svg.addEventListener("pointerdown", (e) => {
            const handle = e.target.closest(".env-handle");
            if (!handle) return;
            activeNode = parseInt(handle.dataset.node, 10);
            try { svg.setPointerCapture(e.pointerId); } catch { }
            e.preventDefault();
            handleDrag(e, true);
        });
        svg.addEventListener("pointermove", (e) => {
            if (!activeNode) return;
            e.preventDefault();
            handleDrag(e, false);
        });
        const stop = (e) => {
            if (!activeNode) return;
            activeNode = null;
            try { if (svg.hasPointerCapture(e.pointerId)) svg.releasePointerCapture(e.pointerId); } catch { }
        };
        svg.addEventListener("pointerup", stop);
        svg.addEventListener("pointercancel", stop);
    }

    function initSidebarResizer(resizerId, sidebarId, dotNetRef) {
        const resizer = document.getElementById(resizerId);
        const sidebar = document.getElementById(sidebarId);
        if (!resizer || !sidebar || resizer.dataset.bound === "true") return;
        resizer.dataset.bound = "true";

        let resizing = false;
        let startX = 0;
        let startWidth = 280;

        resizer.addEventListener("pointerdown", (e) => {
            resizing = true;
            startX = e.clientX;
            startWidth = sidebar.getBoundingClientRect().width;
            document.body.classList.add("is-resizing-sidebar");
            try { resizer.setPointerCapture(e.pointerId); } catch { }
            e.preventDefault();
        });
        window.addEventListener("pointermove", (e) => {
            if (!resizing) return;
            const w = Math.max(260, Math.min(650, Math.round(startWidth + (e.clientX - startX))));
            sidebar.style.width = w + "px";
        });
        window.addEventListener("pointerup", (e) => {
            if (!resizing) return;
            resizing = false;
            document.body.classList.remove("is-resizing-sidebar");
            const w = Math.round(sidebar.getBoundingClientRect().width);
            dotNetRef.invokeMethodAsync("OnSidebarWidthChanged", w);
        });
        resizer.addEventListener("dblclick", () => {
            sidebar.style.width = "320px";
            dotNetRef.invokeMethodAsync("OnSidebarWidthChanged", 320);
        });
    }

    function registerGlobalShortcuts(dotNetRef) {
        if (window.__shortcutsRegistered) return;
        window.__shortcutsRegistered = true;

        window.addEventListener("keydown", (e) => {
            const active = document.activeElement;
            const tag = active ? active.tagName.toUpperCase() : "";
            const isInput = tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (active && active.isContentEditable);

            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "b") {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleGlobalShortcut", "Ctrl+B");
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleGlobalShortcut", "Ctrl+S");
                return;
            }
            if (e.key === "?" || e.key === "F1") {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleGlobalShortcut", "?");
                return;
            }
            if (isInput) return;

            if (e.key === " ") { e.preventDefault(); dotNetRef.invokeMethodAsync("HandleGlobalShortcut", "Space"); }
            else if (e.key === "Escape") { e.preventDefault(); dotNetRef.invokeMethodAsync("HandleGlobalShortcut", "Escape"); }
            else if (e.key === "[" || e.key === "]" || e.key === "1" || e.key === "2" || e.key === "3" ||
                     e.key === "ArrowLeft" || e.key === "ArrowRight") {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleGlobalShortcut", e.key);
            }
            else if ("nNpPjJkKlLtTfF".includes(e.key) || e.key === "Enter" || e.key === "ArrowUp" || e.key === "ArrowDown") {
                if (e.key !== "Enter") e.preventDefault();
                dotNetRef.invokeMethodAsync("HandleGlobalShortcut", e.key);
            }
        });
    }

    async function copyToClipboard(text) {
        try {
            await navigator.clipboard.writeText(text || "");
            return true;
        } catch {
            return false;
        }
    }

    return {
        loadInitialPreferences,
        applyBodyPreferences,
        saveGuidePreference,
        playMetronomeClick,
        feedVuMeter,
        drawVectorCanvas,
        setupVectorTouchpad,
        startVectorOrbit,
        stopVectorOrbit,
        initFmEnvelopeDrag,
        initSidebarResizer,
        registerGlobalShortcuts,
        copyToClipboard
    };
})();
