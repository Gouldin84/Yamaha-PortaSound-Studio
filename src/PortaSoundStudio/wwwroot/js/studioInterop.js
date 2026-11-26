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
    const vectorTrail = [];

    // 60fps Stage Visualizer state
    const stageChannelLevels = new Float32Array(16);
    const stageChannelPeaks = new Float32Array(16);
    const stageChannelHold = new Int32Array(16);
    const stageNotes = [];
    const stagePalette = [
        "#22d3ee", "#34d399", "#a78bfa", "#fbbf24",
        "#38bdf8", "#f472b6", "#818cf8", "#2dd4bf",
        "#60a5fa", "#a3e635", "#c084fc", "#facc15",
        "#fb7185", "#4ade80", "#e879f9", "#fb923c"
    ];
    let stageVizMode = "spectrum";
    let stageIsPlaying = false;
    let stageLoopRunning = false;
    let stageCanvasId = null;
    let stageLastTime = performance.now();
    let stagePhase = 0;

    function loadInitialPreferences() {
        return {
            isDark: localStorage.getItem("yamaha_studio_theme") !== "light",
            handheld: localStorage.getItem("yamaha_studio_handheld") === "true",
            model: localStorage.getItem("yamaha_studio_model") || "pss790",
            guideOpen: localStorage.getItem("yamaha_studio_guide_open") === "true",
            vizMode: localStorage.getItem("yamaha_studio_viz_mode") || "spectrum"
        };
    }

    function applyBodyPreferences(isDark, handheld, model) {
        document.body.classList.toggle("theme-light", !isDark);
        document.body.classList.toggle("handheld-touch-mode", !!handheld);
        document.body.dataset.model = model || "pss790";
        localStorage.setItem("yamaha_studio_theme", isDark ? "dark" : "light");
        localStorage.setItem("yamaha_studio_handheld", handheld ? "true" : "false");
        if (model) {
            localStorage.setItem("yamaha_studio_model", model);
        }
    }

    function saveVisualizerPreference(mode) {
        stageVizMode = mode || "spectrum";
        localStorage.setItem("yamaha_studio_viz_mode", stageVizMode);
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

        vectorTrail.push({ x: puckX, y: puckY });
        if (vectorTrail.length > 26) vectorTrail.shift();

        // Phosphor persistence trail
        for (let i = 0; i < vectorTrail.length - 1; i++) {
            const pt = vectorTrail[i];
            const ratio = (i + 1) / vectorTrail.length;
            ctx.beginPath();
            ctx.arc(pt.x, pt.y, 1.5 + ratio * 3.5, 0, Math.PI * 2);
            ctx.fillStyle = `rgba(99, 102, 241, ${(ratio * 0.45).toFixed(3)})`;
            ctx.fill();
        }

        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(puckX, puckY);
        ctx.strokeStyle = "rgba(99, 102, 241, 0.75)";
        ctx.lineWidth = 2;
        ctx.shadowColor = "#6366f1";
        ctx.shadowBlur = 8;
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

    function initStageVisualizer(canvasId, mode, playing) {
        stageCanvasId = canvasId;
        stageVizMode = mode || "spectrum";
        stageIsPlaying = !!playing;
        if (stageLoopRunning) return;
        stageLoopRunning = true;

        function renderStage(now) {
            const dt = Math.min((now - stageLastTime) / 1000.0, 0.1);
            stageLastTime = now;
            stagePhase += dt * (stageIsPlaying ? 5.2 : 1.6);

            const canvas = stageCanvasId ? document.getElementById(stageCanvasId) : null;
            if (canvas && canvas.offsetParent !== null) {
                const dpr = window.devicePixelRatio || 1;
                const w = Math.max(120, canvas.clientWidth);
                const h = Math.max(40, canvas.clientHeight);
                if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
                    canvas.width = Math.round(w * dpr);
                    canvas.height = Math.round(h * dpr);
                }

                const ctx = canvas.getContext("2d");
                if (ctx) {
                    ctx.save();
                    ctx.scale(dpr, dpr);
                    ctx.clearRect(0, 0, w, h);

                    const bodyStyles = getComputedStyle(document.body);
                    const acc = bodyStyles.getPropertyValue("--acc").trim() || "#06b6d4";
                    const acc2 = bodyStyles.getPropertyValue("--acc-2").trim() || "#6366f1";

                    // Background horizon grid
                    ctx.strokeStyle = "rgba(148, 163, 184, 0.08)";
                    ctx.lineWidth = 1;
                    for (let gy = Math.round(h * 0.28); gy < h; gy += Math.round(h * 0.28)) {
                        ctx.beginPath(); ctx.moveTo(0, gy); ctx.lineTo(w, gy); ctx.stroke();
                    }

                    // Decay channels smoothly at 60fps
                    let energySum = 0;
                    for (let i = 0; i < 16; i++) {
                        stageChannelLevels[i] = Math.max(0.02, stageChannelLevels[i] - dt * 2.1);
                        energySum += stageChannelLevels[i];
                        if (stageChannelLevels[i] >= stageChannelPeaks[i]) {
                            stageChannelPeaks[i] = stageChannelLevels[i];
                            stageChannelHold[i] = 22;
                        } else if (stageChannelHold[i] > 0) {
                            stageChannelHold[i]--;
                        } else {
                            stageChannelPeaks[i] = Math.max(stageChannelLevels[i], stageChannelPeaks[i] - dt * 1.1);
                        }
                    }

                    if (stageVizMode === "waterfall") {
                        for (let i = stageNotes.length - 1; i >= 0; i--) {
                            const n = stageNotes[i];
                            n.x -= dt * (stageIsPlaying ? 0.28 : 0.08);
                            const px = n.x * w;
                            const pw = Math.max(8, n.len * w);
                            const py = 4 + (1 - (Math.max(24, Math.min(108, n.note)) - 24) / 84.0) * (h - 12);
                            ctx.fillStyle = n.color;
                            ctx.shadowColor = n.color;
                            ctx.shadowBlur = 9;
                            ctx.beginPath();
                            ctx.roundRect(px, py, pw, 4.5, 2.2);
                            ctx.fill();
                            if (n.x + n.len < -0.05) stageNotes.splice(i, 1);
                        }
                        if (stageNotes.length === 0) {
                            ctx.fillStyle = "rgba(148, 163, 184, 0.35)";
                            ctx.font = "600 10px ui-monospace, monospace";
                            ctx.textAlign = "center";
                            ctx.fillText(stageIsPlaying ? "WAITING FOR MIDI NOTES..." : "PRESS PLAY TO STREAM NOTE MATRIX", w / 2, h / 2 + 3);
                        }
                        ctx.shadowBlur = 0;
                    } else if (stageVizMode === "scope") {
                        const ampScale = Math.min(1.0, 0.14 + (energySum / 5.5));
                        ctx.beginPath();
                        for (let x = 0; x <= w; x += 2.5) {
                            const nx = x / w;
                            const env = Math.sin(nx * Math.PI);
                            const wave = Math.sin(nx * 24 + stagePhase * 2.2) * 0.55
                                       + Math.sin(nx * 53 - stagePhase * 3.1) * 0.28
                                       + Math.cos(nx * 11 + stagePhase) * 0.17;
                            const y = h / 2 + wave * (h * 0.38) * ampScale * env;
                            if (x === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
                        }
                        ctx.strokeStyle = acc;
                        ctx.shadowColor = acc;
                        ctx.shadowBlur = 11;
                        ctx.lineWidth = 2.1;
                        ctx.stroke();
                        ctx.shadowBlur = 0;
                    } else {
                        // Default: 16-Channel Velocity Spectrum
                        const padX = 10;
                        const slotW = (w - padX * 2) / 16.0;
                        const barW = Math.max(4, slotW - 5);
                        for (let i = 0; i < 16; i++) {
                            const idleRipple = stageIsPlaying ? 0 : (0.04 + 0.03 * Math.sin(stagePhase + i * 0.45));
                            const val = Math.max(idleRipple, stageChannelLevels[i]);
                            const bh = Math.max(3, val * (h - 14));
                            const bx = padX + i * slotW + (slotW - barW) / 2;
                            const by = h - bh - 5;

                            const grad = ctx.createLinearGradient(0, by, 0, h - 5);
                            grad.addColorStop(0, i === 15 ? "#fbbf24" : acc);
                            grad.addColorStop(1, acc2);
                            ctx.fillStyle = grad;
                            ctx.shadowColor = i === 15 ? "#f59e0b" : acc;
                            ctx.shadowBlur = val > 0.25 ? 8 : 0;
                            ctx.beginPath();
                            ctx.roundRect(bx, by, barW, bh, 2.5);
                            ctx.fill();

                            // Floating Peak Cap
                            const pY = h - Math.max(4, stageChannelPeaks[i] * (h - 14)) - 7;
                            if (stageChannelPeaks[i] > 0.08) {
                                ctx.shadowBlur = 0;
                                ctx.fillStyle = "#f8fafc";
                                ctx.fillRect(bx, pY, barW, 1.8);
                            }

                            // Tiny channel index base tick
                            ctx.shadowBlur = 0;
                            ctx.fillStyle = "rgba(148, 163, 184, 0.45)";
                            ctx.font = "700 8px ui-monospace, monospace";
                            ctx.textAlign = "center";
                            ctx.fillText(String(i + 1), bx + barW / 2, h - 0.5);
                        }
                        ctx.shadowBlur = 0;
                    }
                    ctx.restore();
                }
            }

            requestAnimationFrame(renderStage);
        }

        requestAnimationFrame(renderStage);
    }

    function setStageVisualizerState(mode, playing) {
        if (mode) saveVisualizerPreference(mode);
        stageIsPlaying = !!playing;
    }

    function syncStageChannelLevels(velocities) {
        if (!Array.isArray(velocities)) return;
        for (let i = 0; i < Math.min(16, velocities.length); i++) {
            const v = Math.min(1.0, (velocities[i] || 0) / 127.0);
            if (v > stageChannelLevels[i]) {
                stageChannelLevels[i] = v;
            }
        }
    }

    function pushStageNote(channel, velocity, noteNumber) {
        if (channel < 0 || channel >= 16 || velocity <= 0) return;
        const norm = Math.min(1.0, velocity / 127.0);
        stageChannelLevels[channel] = Math.max(stageChannelLevels[channel], norm);
        stageNotes.push({
            x: 1.0,
            note: noteNumber || 60,
            len: 0.06 + norm * 0.09,
            color: stagePalette[channel]
        });
        if (stageNotes.length > 96) stageNotes.shift();
    }

    function drawFmScope(canvasId, modWave, modMult, modLevel, carWave, carMult, feedback) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || canvas.offsetParent === null) return;
        const dpr = window.devicePixelRatio || 1;
        const w = Math.max(120, canvas.clientWidth);
        const h = Math.max(40, canvas.clientHeight);
        if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
            canvas.width = Math.round(w * dpr);
            canvas.height = Math.round(h * dpr);
        }
        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        const bodyStyles = getComputedStyle(document.body);
        const acc = bodyStyles.getPropertyValue("--acc").trim() || "#f59e0b";

        function shapeWave(waveType, phase) {
            const s = Math.sin(phase);
            if (waveType === 1) return Math.sin(phase % (Math.PI * 2)) >= 0 ? s : 0;
            if (waveType === 2) return Math.abs(s);
            if (waveType === 3) return (Math.sin(phase) >= 0 && Math.cos(phase) >= 0) ? Math.abs(s) : 0;
            return s;
        }

        ctx.save();
        ctx.scale(dpr, dpr);
        ctx.clearRect(0, 0, w, h);

        ctx.strokeStyle = "rgba(148, 163, 184, 0.14)";
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(0, h / 2); ctx.lineTo(w, h / 2); ctx.stroke();

        const modIdx = (modLevel / 63.0) * 4.2;
        const fbAmt = (feedback / 7.0) * 1.4;
        const mRatio = Math.max(0.5, modMult === 0 ? 0.5 : modMult);
        const cRatio = Math.max(0.5, carMult === 0 ? 0.5 : carMult);

        let prevMod = 0;
        ctx.beginPath();
        for (let x = 0; x <= w; x += 2) {
            const basePhase = (x / w) * Math.PI * 4.0;
            const modOut = shapeWave(modWave, basePhase * mRatio + prevMod * fbAmt) * modIdx;
            prevMod = modOut * 0.45;
            const carOut = shapeWave(carWave, basePhase * cRatio + modOut);
            const y = h / 2 - carOut * (h * 0.38);
            if (x === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
        }
        ctx.strokeStyle = acc;
        ctx.shadowColor = acc;
        ctx.shadowBlur = 10;
        ctx.lineWidth = 2.2;
        ctx.stroke();
        ctx.restore();
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
        saveVisualizerPreference,
        initStageVisualizer,
        setStageVisualizerState,
        syncStageChannelLevels,
        pushStageNote,
        drawFmScope,
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
