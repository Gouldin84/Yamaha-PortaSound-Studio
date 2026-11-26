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
    let vectorTrailDecayId = null;
    let activeVectorCanvasId = null;

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
        if (activeVectorCanvasId && document.getElementById(activeVectorCanvasId)) {
            drawVectorCanvas(activeVectorCanvasId, vectorStateX, vectorStateY, false);
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

    let vectorBowlCache = null;
    let vectorBowlCacheKey = "";
    const vectorParticles = [];
    let vectorMeshPhase = 0;

    // Precomputed 108-angle LUT for 60fps 3D outer harmonic wave mesh + radial swell halo
    const SWELL_STEPS = 108;
    const swellLut = Array.from({ length: SWELL_STEPS }, (_, i) => {
        const th = (i / SWELL_STEPS) * Math.PI * 2;
        const c = Math.cos(th), s = Math.sin(th);
        const kA = Math.pow(Math.max(0, -s), 1.35);
        const kB = Math.pow(Math.max(0, c), 1.35);
        const kC = Math.pow(Math.max(0, s), 1.35);
        const kD = Math.pow(Math.max(0, -c), 1.35);
        const kSum = kA + kB + kC + kD || 1;
        return {
            th, c, s, kA, kB, kC, kD,
            rCol: Math.round((kA * 34 + kB * 16 + kC * 245 + kD * 244) / kSum),
            gCol: Math.round((kA * 211 + kB * 205 + kC * 158 + kD * 63) / kSum),
            bCol: Math.round((kA * 238 + kB * 129 + kC * 11 + kD * 94) / kSum)
        };
    });

    function colorForAngle(theta) {
        const kA = Math.max(0, -Math.sin(theta));
        const kB = Math.max(0, Math.cos(theta));
        const kC = Math.max(0, Math.sin(theta));
        const kD = Math.max(0, -Math.cos(theta));
        const sum = kA + kB + kC + kD || 1;
        return [
            Math.round((kA * 34 + kB * 16 + kC * 245 + kD * 244) / sum),
            Math.round((kA * 211 + kB * 205 + kC * 158 + kD * 63) / sum),
            Math.round((kA * 238 + kB * 129 + kC * 11 + kD * 94) / sum)
        ];
    }

    function spawnVectorParticles(deltaMove, w) {
        const count = Math.min(4, Math.max(1, Math.ceil(deltaMove * 24)));
        for (let i = 0; i < count; i++) {
            if (vectorParticles.length >= 44) vectorParticles.shift();
            const pick = Math.random();
            let baseAng = -Math.PI / 2;
            if (pick > w.wA && pick <= w.wA + w.wB) baseAng = 0;
            else if (pick > w.wA + w.wB && pick <= w.wA + w.wB + w.wC) baseAng = Math.PI / 2;
            else if (pick > w.wA + w.wB + w.wC) baseAng = Math.PI;
            const ang = baseAng + (Math.random() - 0.5) * 1.15;
            vectorParticles.push({
                angle: ang,
                rOff: 111 + Math.random() * 14,
                vr: 0.42 + Math.random() * 0.78,
                va: (Math.random() - 0.5) * 0.022,
                z: 0.5 + Math.random() * 0.9,
                life: 1.0,
                rgb: colorForAngle(ang)
            });
        }
    }

    function getVectorBowlCache(isLight, dpr) {
        const key = `${isLight ? "L" : "D"}:${dpr}`;
        if (vectorBowlCache && vectorBowlCacheKey === key) return vectorBowlCache;

        const logicalW = 288, logicalH = 288;
        const off = document.createElement("canvas");
        off.width = logicalW * dpr;
        off.height = logicalH * dpr;
        const ctx = off.getContext("2d");
        ctx.save();
        ctx.scale(dpr, dpr);

        const cx = 144, cy = 144, rActive = 84;

        // 1. Exterior Cast Drop Shadow beneath Machined Flange
        const outerDrop = ctx.createRadialGradient(cx + 3, cy + 7, rActive * 0.72, cx + 3, cy + 7, rActive + 30);
        outerDrop.addColorStop(0, isLight ? "rgba(15, 23, 42, 0.30)" : "rgba(2, 6, 16, 0.80)");
        outerDrop.addColorStop(0.65, isLight ? "rgba(15, 23, 42, 0.14)" : "rgba(2, 6, 16, 0.38)");
        outerDrop.addColorStop(1, "rgba(0, 0, 0, 0)");
        ctx.fillStyle = outerDrop;
        ctx.beginPath();
        ctx.arc(cx + 3, cy + 7, rActive + 30, 0, Math.PI * 2);
        ctx.fill();

        // 2. Sculpted Chamfered Hardware Bezel Flange
        ctx.beginPath();
        ctx.arc(cx, cy, rActive + 24, 0, Math.PI * 2);
        const flangeGrad = ctx.createLinearGradient(cx - rActive - 20, cy - rActive - 20, cx + rActive + 20, cy + rActive + 20);
        if (isLight) {
            flangeGrad.addColorStop(0, "#ffffff");
            flangeGrad.addColorStop(0.35, "#e6edf6");
            flangeGrad.addColorStop(0.75, "#c2cfdf");
            flangeGrad.addColorStop(1, "#94a6be");
        } else {
            flangeGrad.addColorStop(0, "#55688c");
            flangeGrad.addColorStop(0.38, "#34425c");
            flangeGrad.addColorStop(0.78, "#222c40");
            flangeGrad.addColorStop(1, "#141c2b");
        }
        ctx.fillStyle = flangeGrad;
        ctx.fill();
        ctx.strokeStyle = isLight ? "rgba(255, 255, 255, 0.95)" : "rgba(255, 255, 255, 0.22)";
        ctx.lineWidth = 1.5;
        ctx.stroke();

        // Corner Hex Mounting Screws
        [45, 135, 225, 315].forEach(deg => {
            const rad = (deg * Math.PI) / 180;
            const sx = cx + Math.cos(rad) * (rActive + 16);
            const sy = cy + Math.sin(rad) * (rActive + 16);
            ctx.beginPath();
            ctx.arc(sx, sy, 3.6, 0, Math.PI * 2);
            ctx.fillStyle = isLight ? "#94a3b8" : "#111827";
            ctx.fill();
            ctx.beginPath();
            ctx.arc(sx, sy, 2.7, 0, Math.PI * 2);
            const screwG = ctx.createLinearGradient(sx - 2, sy - 2, sx + 2, sy + 2);
            screwG.addColorStop(0, isLight ? "#ffffff" : "#94a3b8");
            screwG.addColorStop(1, isLight ? "#cbd5e1" : "#334155");
            ctx.fillStyle = screwG;
            ctx.fill();
            ctx.beginPath();
            ctx.moveTo(sx - 1.5, sy - 0.8);
            ctx.lineTo(sx + 1.5, sy + 0.8);
            ctx.strokeStyle = isLight ? "#475569" : "#0f172a";
            ctx.lineWidth = 0.8;
            ctx.stroke();
        });

        // 3. Stepped Deep Parabolic Rim Wall
        ctx.beginPath();
        ctx.arc(cx, cy, rActive + 9, 0, Math.PI * 2);
        const lipWallGrad = ctx.createLinearGradient(cx - rActive, cy - rActive, cx + rActive, cy + rActive);
        if (isLight) {
            lipWallGrad.addColorStop(0, "#7f92ab");
            lipWallGrad.addColorStop(0.55, "#b7c6da");
            lipWallGrad.addColorStop(1, "#f8fafc");
        } else {
            lipWallGrad.addColorStop(0, "#0b111c");
            lipWallGrad.addColorStop(0.55, "#1f2a3f");
            lipWallGrad.addColorStop(1, "#45587a");
        }
        ctx.fillStyle = lipWallGrad;
        ctx.fill();

        // 4. Deep Concave Funnel Bowl
        ctx.save();
        ctx.beginPath();
        ctx.arc(cx, cy, rActive + 2, 0, Math.PI * 2);
        ctx.clip();

        const slopeGrad = ctx.createLinearGradient(cx - rActive * 0.8, cy - rActive * 0.8, cx + rActive * 0.85, cy + rActive * 0.85);
        if (isLight) {
            slopeGrad.addColorStop(0, "#91a3bc");
            slopeGrad.addColorStop(0.32, "#bac8db");
            slopeGrad.addColorStop(0.68, "#dce6f2");
            slopeGrad.addColorStop(1, "#f8fbff");
        } else {
            slopeGrad.addColorStop(0, "#0c1322");
            slopeGrad.addColorStop(0.38, "#182236");
            slopeGrad.addColorStop(0.75, "#25334d");
            slopeGrad.addColorStop(1, "#354769");
        }
        ctx.fillStyle = slopeGrad;
        ctx.fillRect(cx - rActive - 4, cy - rActive - 4, (rActive + 4) * 2, (rActive + 4) * 2);

        const depthWell = ctx.createRadialGradient(cx, cy + 5, 4, cx, cy, rActive + 2);
        if (isLight) {
            depthWell.addColorStop(0, "rgba(15, 23, 42, 0.72)");
            depthWell.addColorStop(0.28, "rgba(30, 41, 59, 0.44)");
            depthWell.addColorStop(0.62, "rgba(71, 85, 105, 0.16)");
            depthWell.addColorStop(1, "rgba(255, 255, 255, 0)");
        } else {
            depthWell.addColorStop(0, "rgba(2, 5, 12, 0.94)");
            depthWell.addColorStop(0.32, "rgba(5, 10, 22, 0.70)");
            depthWell.addColorStop(0.68, "rgba(12, 18, 34, 0.28)");
            depthWell.addColorStop(1, "rgba(0, 0, 0, 0)");
        }
        ctx.fillStyle = depthWell;
        ctx.fillRect(cx - rActive - 4, cy - rActive - 4, (rActive + 4) * 2, (rActive + 4) * 2);

        const overhangShadow = ctx.createRadialGradient(cx + 8, cy + 10, rActive * 0.78, cx, cy, rActive + 4);
        overhangShadow.addColorStop(0, "rgba(0,0,0,0)");
        overhangShadow.addColorStop(1, isLight ? "rgba(15, 23, 42, 0.34)" : "rgba(0, 0, 0, 0.75)");
        ctx.fillStyle = overhangShadow;
        ctx.fillRect(cx - rActive - 4, cy - rActive - 4, (rActive + 4) * 2, (rActive + 4) * 2);

        const glows = [
            [cx, cy - rActive * 0.68, "6, 182, 212"],
            [cx + rActive * 0.68, cy, "16, 185, 129"],
            [cx, cy + rActive * 0.68, "245, 158, 11"],
            [cx - rActive * 0.68, cy, "244, 63, 94"]
        ];
        glows.forEach(([gx, gy, rgb]) => {
            const g = ctx.createRadialGradient(gx, gy, 2, gx, gy, 48);
            g.addColorStop(0, `rgba(${rgb}, ${isLight ? 0.26 : 0.24})`);
            g.addColorStop(1, `rgba(${rgb}, 0)`);
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.arc(gx, gy, 48, 0, Math.PI * 2);
            ctx.fill();
        });

        [0.88, 0.70, 0.52, 0.36].forEach((f, idx) => {
            const lerp = (1 - f) * 0.85;
            const ringY = cy + 5 * lerp;
            const ringR = rActive * f;
            ctx.beginPath();
            ctx.arc(cx, ringY, ringR, 0, Math.PI * 2);
            ctx.strokeStyle = isLight
                ? `rgba(15, 23, 42, ${0.10 + idx * 0.05})`
                : `rgba(148, 163, 184, ${0.12 + idx * 0.03})`;
            ctx.lineWidth = 1;
            ctx.stroke();

            ctx.beginPath();
            ctx.arc(cx, ringY, ringR - 0.8, 0.1 * Math.PI, 0.65 * Math.PI);
            ctx.strokeStyle = isLight ? "rgba(255, 255, 255, 0.45)" : "rgba(255, 255, 255, 0.08)";
            ctx.lineWidth = 0.8;
            ctx.stroke();
        });

        ctx.beginPath();
        ctx.moveTo(cx, cy - rActive); ctx.lineTo(cx, cy + 5);
        ctx.moveTo(cx, cy + rActive); ctx.lineTo(cx, cy + 5);
        ctx.moveTo(cx - rActive, cy); ctx.lineTo(cx, cy + 5);
        ctx.moveTo(cx + rActive, cy); ctx.lineTo(cx, cy + 5);
        ctx.strokeStyle = isLight ? "rgba(15, 23, 42, 0.22)" : "rgba(148, 163, 184, 0.20)";
        ctx.lineWidth = 1;
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(cx, cy + 5, 28, 0, Math.PI * 2);
        const pitFloor = ctx.createRadialGradient(cx, cy + 5, 4, cx, cy + 5, 28);
        pitFloor.addColorStop(0, "#02050c");
        pitFloor.addColorStop(0.65, isLight ? "#1e293b" : "#070c16");
        pitFloor.addColorStop(1, "rgba(0,0,0,0)");
        ctx.fillStyle = pitFloor;
        ctx.fill();

        ctx.restore();

        ctx.font = "900 11px system-ui, sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillStyle = isLight ? "#0284c7" : "#22d3ee"; ctx.fillText("A", cx, cy - rActive - 15);
        ctx.fillStyle = isLight ? "#059669" : "#34d399"; ctx.fillText("B", cx + rActive + 15, cy);
        ctx.fillStyle = isLight ? "#d97706" : "#fbbf24"; ctx.fillText("C", cx, cy + rActive + 15);
        ctx.fillStyle = isLight ? "#e11d48" : "#fb7185"; ctx.fillText("D", cx - rActive - 15, cy);

        ctx.restore();
        vectorBowlCache = off;
        vectorBowlCacheKey = key;
        return off;
    }

    function drawVectorSwellHalo(ctx, cx, cy, rBezel, w, vectorStateX, vectorStateY, rCol, gCol, bCol, isLight, tickParticles) {
        const outerPts = new Array(SWELL_STEPS);
        for (let i = 0; i < SWELL_STEPS; i++) {
            const lut = swellLut[i];
            const lw = w.wA * lut.kA + w.wB * lut.kB + w.wC * lut.kC + w.wD * lut.kD;
            const norm = Math.min(1, lw * 1.18);
            const ripple = Math.sin(lut.th * 5 + vectorMeshPhase) * Math.cos(lut.th * 3 - vectorMeshPhase * 0.7) * (1.2 + norm * 3.8);
            const rOuter = rBezel + 4 + 27 * norm + ripple * 0.45;
            outerPts[i] = {
                x: cx + lut.c * rOuter,
                y: cy + lut.s * rOuter,
                norm,
                rOuter,
                lut
            };
        }

        const buildOuterPath = () => {
            ctx.beginPath();
            const last = outerPts[SWELL_STEPS - 1];
            const first = outerPts[0];
            ctx.moveTo((last.x + first.x) * 0.5, (last.y + first.y) * 0.5);
            for (let i = 0; i < SWELL_STEPS; i++) {
                const p0 = outerPts[i];
                const p1 = outerPts[(i + 1) % SWELL_STEPS];
                ctx.quadraticCurveTo(p0.x, p0.y, (p0.x + p1.x) * 0.5, (p0.y + p1.y) * 0.5);
            }
            ctx.closePath();
        };

        // Layer 1: Subsurface Ambient Bloom Fill under Swell Membrane
        const swellBloom = ctx.createRadialGradient(
            cx + vectorStateX * 20, cy - vectorStateY * 20, rBezel - 4,
            cx, cy, rBezel + 38
        );
        swellBloom.addColorStop(0, `rgba(${rCol}, ${gCol}, ${bCol}, ${isLight ? 0.30 : 0.38})`);
        swellBloom.addColorStop(0.7, `rgba(${rCol}, ${gCol}, ${bCol}, ${isLight ? 0.12 : 0.16})`);
        swellBloom.addColorStop(1, "rgba(0, 0, 0, 0)");
        buildOuterPath();
        ctx.fillStyle = swellBloom;
        ctx.fill();

        // Layer 2: 5-Layer Harmonic Dotted Mesh Membrane (Acoustic Wave Lattice)
        const meshLayers = 5;
        for (let m = 0; m < meshLayers; m++) {
            const frac = (m + 1) / (meshLayers + 0.35);
            const layerAlpha = isLight ? (0.42 + frac * 0.46) : (0.34 + frac * 0.56);
            const dotSize = m === meshLayers - 1 ? 1.85 : (1.1 + frac * 0.55);

            for (let i = 0; i < SWELL_STEPS; i++) {
                const pt = outerPts[i];
                const l = pt.lut;
                const phaseShift = m * 0.65;
                const waveOffset = Math.sin(l.th * 6 - vectorMeshPhase + phaseShift) * Math.cos(l.th * 4 + phaseShift) * (0.8 + pt.norm * 4.2);
                const rDot = (rBezel + 3) + (pt.rOuter - (rBezel + 3)) * frac + waveOffset * (1 - frac * 0.45);
                if (rDot <= rBezel + 1.5) continue;

                const dx = cx + l.c * rDot;
                const dy = cy + l.s * rDot;
                const brightBoost = m === meshLayers - 1 ? 30 : 0;
                const cr = Math.min(255, l.rCol + brightBoost);
                const cg = Math.min(255, l.gCol + brightBoost);
                const cb = Math.min(255, l.bCol + brightBoost);

                ctx.fillStyle = `rgba(${cr}, ${cg}, ${cb}, ${(layerAlpha * (0.42 + pt.norm * 0.58)).toFixed(2)})`;
                ctx.fillRect(dx - dotSize * 0.5, dy - dotSize * 0.5, dotSize, dotSize);
            }
        }

        // Layer 3: Subtle Radial Acoustic Tick Fins (Every 3rd LUT sample = 36 crisp fins)
        for (let i = 0; i < SWELL_STEPS; i += 3) {
            const pt = outerPts[i];
            const l = pt.lut;
            const r0 = rBezel + 3.5;
            const r1 = r0 + 3.5 + 22 * pt.norm;
            const x0 = cx + l.c * r0, y0 = cy + l.s * r0;
            const x1 = cx + l.c * r1, y1 = cy + l.s * r1;
            ctx.beginPath();
            ctx.moveTo(x0, y0);
            ctx.lineTo(x1, y1);
            ctx.strokeStyle = `rgba(${l.rCol}, ${l.gCol}, ${l.bCol}, ${(isLight ? 0.45 + pt.norm * 0.45 : 0.35 + pt.norm * 0.55).toFixed(2)})`;
            ctx.lineWidth = 1.6;
            ctx.lineCap = "round";
            ctx.stroke();
        }

        // Layer 4: Outer Translucent Beveled Glass Ribbon Edge
        buildOuterPath();
        ctx.strokeStyle = `rgba(${rCol}, ${gCol}, ${bCol}, ${isLight ? 0.50 : 0.68})`;
        ctx.lineWidth = 2.8;
        ctx.stroke();

        buildOuterPath();
        ctx.strokeStyle = isLight ? "rgba(255, 255, 255, 0.88)" : "rgba(255, 255, 255, 0.50)";
        ctx.lineWidth = 0.9;
        ctx.stroke();

        // Layer 5: 3D Z-Depth Sweep Bokeh Particles
        for (let i = vectorParticles.length - 1; i >= 0; i--) {
            const p = vectorParticles[i];
            if (tickParticles) {
                p.rOff += p.vr;
                p.angle += p.va;
                p.life -= 0.032;
                if (p.life <= 0) {
                    vectorParticles.splice(i, 1);
                    continue;
                }
            }
            const px = cx + Math.cos(p.angle) * p.rOff;
            const py = cy + Math.sin(p.angle) * p.rOff;
            const rad = (1.6 + p.z * 2.4) * p.life;
            const [pr, pg, pb] = p.rgb;
            const pglow = ctx.createRadialGradient(px, py, 0.2, px, py, rad * 2.1);
            pglow.addColorStop(0, `rgba(255, 255, 255, ${(p.life * 0.92).toFixed(2)})`);
            pglow.addColorStop(0.38, `rgba(${pr}, ${pg}, ${pb}, ${(p.life * 0.72).toFixed(2)})`);
            pglow.addColorStop(1, `rgba(${pr}, ${pg}, ${pb}, 0)`);
            ctx.fillStyle = pglow;
            ctx.beginPath();
            ctx.arc(px, py, rad * 2.1, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    function scheduleVectorDecay(canvasId) {
        if (vectorTrailDecayId) return;
        const stepDecay = () => {
            vectorTrailDecayId = null;
            if (vectorTrail.length === 0 && vectorParticles.length === 0) return;
            drawVectorCanvas(canvasId, vectorStateX, vectorStateY, false);
        };
        vectorTrailDecayId = requestAnimationFrame(stepDecay);
    }

    function drawVectorCanvas(canvasId, x, y, recordTrail = true) {
        activeVectorCanvasId = canvasId;
        const prevX = vectorStateX;
        const prevY = vectorStateY;
        vectorStateX = x;
        vectorStateY = y;
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        const dpr = window.devicePixelRatio || 1;
        const logicalW = 288, logicalH = 288;
        if (canvas.width !== logicalW * dpr || canvas.height !== logicalH * dpr) {
            canvas.width = logicalW * dpr;
            canvas.height = logicalH * dpr;
            canvas.style.width = logicalW + "px";
            canvas.style.height = logicalH + "px";
        }

        const isLight = document.body.classList.contains("theme-light");
        const bowl = getVectorBowlCache(isLight, dpr);

        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        const cx = 144, cy = 144, rActive = 84, rBezel = rActive + 24;
        const w = calculateVectorWeights(vectorStateX, vectorStateY);
        const rCol = Math.round(w.wA * 34 + w.wB * 52 + w.wC * 251 + w.wD * 251);
        const gCol = Math.round(w.wA * 211 + w.wB * 211 + w.wC * 191 + w.wD * 113);
        const bCol = Math.round(w.wA * 238 + w.wB * 153 + w.wC * 36 + w.wD * 133);

        const deltaMove = Math.hypot(x - prevX, y - prevY);
        if (recordTrail && deltaMove > 0.008) {
            vectorMeshPhase += Math.min(0.45, deltaMove * 2.2);
            spawnVectorParticles(deltaMove, w);
        }

        // Draw outer 3D volume swell ring + harmonic dotted mesh BEFORE stamping the gimbal bezel on top
        ctx.save();
        ctx.scale(dpr, dpr);
        drawVectorSwellHalo(ctx, cx, cy, rBezel, w, vectorStateX, vectorStateY, rCol, gCol, bCol, isLight, !recordTrail || deltaMove > 0.004);
        ctx.restore();

        // Draw cached sculpted gimbal flange & recessed dish cleanly in the center
        ctx.drawImage(bowl, 0, 0);
        ctx.scale(dpr, dpr);

        const puckX = cx + vectorStateX * (rActive - 12);
        const puckY = (cy - 2) - vectorStateY * (rActive - 12);

        // 1. Pleated Rubber Gaiter Ribs
        ctx.save();
        ctx.beginPath();
        ctx.arc(cx, cy, rActive + 2, 0, Math.PI * 2);
        ctx.clip();
        [23, 17.5, 12.5].forEach((rr, idx) => {
            const t = (idx + 1) / 4;
            const rx = cx + (puckX - cx) * (t * 0.36);
            const ry = (cy + 5) + (puckY - (cy + 5)) * (t * 0.36);
            ctx.beginPath();
            ctx.arc(rx + 1.5, ry + 2.5, rr + 1, 0, Math.PI * 2);
            ctx.fillStyle = "rgba(0, 0, 0, 0.55)";
            ctx.fill();
            ctx.beginPath();
            ctx.arc(rx, ry, rr, 0, Math.PI * 2);
            const bootG = ctx.createRadialGradient(rx - rr * 0.3, ry - rr * 0.3, 1, rx, ry, rr);
            if (isLight) {
                bootG.addColorStop(0, "#64748b");
                bootG.addColorStop(0.6, "#334155");
                bootG.addColorStop(1, "#0f172a");
            } else {
                bootG.addColorStop(0, "#3b4963");
                bootG.addColorStop(0.6, "#182234");
                bootG.addColorStop(1, "#050810");
            }
            ctx.fillStyle = bootG;
            ctx.fill();
            ctx.strokeStyle = isLight ? "rgba(255,255,255,0.28)" : "rgba(148,163,184,0.24)";
            ctx.lineWidth = 0.8;
            ctx.stroke();
        });
        ctx.restore();

        // 2. Motion Phosphor Trail inside dish
        const nowMs = performance.now();
        const trailLifetimeMs = 300;
        if (recordTrail && (deltaMove > 0.012)) {
            vectorTrail.push({ x: puckX, y: puckY, t: nowMs });
            if (vectorTrail.length > 20) vectorTrail.shift();
        }
        while (vectorTrail.length > 0 && (nowMs - vectorTrail[0].t) > trailLifetimeMs) {
            vectorTrail.shift();
        }
        for (let i = 0; i < vectorTrail.length; i++) {
            const pt = vectorTrail[i];
            const life = Math.max(0, 1.0 - (nowMs - pt.t) / trailLifetimeMs);
            if (life <= 0) continue;
            ctx.beginPath();
            ctx.arc(pt.x, pt.y, 2 + life * 4, 0, Math.PI * 2);
            ctx.fillStyle = `rgba(${rCol}, ${gCol}, ${bCol}, ${(life * 0.42).toFixed(3)})`;
            ctx.fill();
        }
        if (vectorTrail.length > 0 || vectorParticles.length > 0) scheduleVectorDecay(canvasId);

        // 3. Cast Shadow on Bowl beneath Shaft & Cap
        const baseCx = cx + (puckX - cx) * 0.14;
        const baseCy = (cy + 5) + (puckY - (cy + 5)) * 0.14;
        const neckX = puckX;
        const neckY = puckY + 3.5;
        const hatShadowX = puckX + 6 + vectorStateX * 4;
        const hatShadowY = puckY + 11 - vectorStateY * 4;

        ctx.beginPath();
        ctx.moveTo(baseCx + 3, baseCy + 5);
        ctx.lineTo(hatShadowX, hatShadowY);
        ctx.strokeStyle = isLight ? "rgba(15, 23, 42, 0.28)" : "rgba(0, 0, 0, 0.72)";
        ctx.lineWidth = 8.5;
        ctx.lineCap = "round";
        ctx.stroke();

        const hatDrop = ctx.createRadialGradient(hatShadowX, hatShadowY, 5, hatShadowX, hatShadowY, 26);
        hatDrop.addColorStop(0, isLight ? "rgba(15, 23, 42, 0.42)" : "rgba(0, 0, 0, 0.85)");
        hatDrop.addColorStop(0.55, isLight ? "rgba(15, 23, 42, 0.16)" : "rgba(0, 0, 0, 0.40)");
        hatDrop.addColorStop(1, "rgba(0, 0, 0, 0)");
        ctx.fillStyle = hatDrop;
        ctx.beginPath();
        ctx.arc(hatShadowX, hatShadowY, 26, 0, Math.PI * 2);
        ctx.fill();

        // 4. Machined Metallic Base Collar Ring inside Dust Boot
        ctx.beginPath();
        ctx.arc(baseCx, baseCy, 7.2, 0, Math.PI * 2);
        const collarG = ctx.createRadialGradient(baseCx - 2, baseCy - 2, 1, baseCx, baseCy, 7.5);
        if (isLight) {
            collarG.addColorStop(0, "#ffffff");
            collarG.addColorStop(0.55, "#cbd5e1");
            collarG.addColorStop(1, "#475569");
        } else {
            collarG.addColorStop(0, "#cbd5e1");
            collarG.addColorStop(0.5, "#475569");
            collarG.addColorStop(1, "#0f172a");
        }
        ctx.fillStyle = collarG;
        ctx.fill();

        // 5. True 3D CNC Tapered Cylindrical Chrome / Titanium Shaft (Perpendicular Metallic Shading)
        const sdx = neckX - baseCx;
        const sdy = neckY - baseCy;
        const slen = Math.hypot(sdx, sdy);
        if (slen > 1.2) {
            const nx = -sdy / slen;
            const ny = sdx / slen;
            const rBase = 3.6;
            const rTop = 5.4;
            const midX = (baseCx + neckX) * 0.5;
            const midY = (baseCy + neckY) * 0.5;

            const cylGrad = ctx.createLinearGradient(
                midX - nx * rTop, midY - ny * rTop,
                midX + nx * rTop, midY + ny * rTop
            );
            if (isLight) {
                cylGrad.addColorStop(0.00, "#475569");
                cylGrad.addColorStop(0.24, "#cbd5e1");
                cylGrad.addColorStop(0.42, "#ffffff");
                cylGrad.addColorStop(0.68, "#94a3b8");
                cylGrad.addColorStop(0.88, "#64748b");
                cylGrad.addColorStop(1.00, "#334155");
            } else {
                cylGrad.addColorStop(0.00, "#0f172a");
                cylGrad.addColorStop(0.22, "#475569");
                cylGrad.addColorStop(0.42, "#f8fafc");
                cylGrad.addColorStop(0.66, "#64748b");
                cylGrad.addColorStop(0.88, "#334155");
                cylGrad.addColorStop(1.00, "#090f1d");
            }

            ctx.beginPath();
            ctx.moveTo(baseCx - nx * rBase, baseCy - ny * rBase);
            ctx.lineTo(neckX - nx * rTop, neckY - ny * rTop);
            ctx.lineTo(neckX + nx * rTop, neckY + ny * rTop);
            ctx.lineTo(baseCx + nx * rBase, baseCy + ny * rBase);
            ctx.closePath();
            ctx.fillStyle = cylGrad;
            ctx.fill();

            ctx.beginPath();
            ctx.arc(baseCx, baseCy, rBase, 0, Math.PI * 2);
            ctx.fillStyle = cylGrad;
            ctx.fill();
            ctx.beginPath();
            ctx.arc(neckX, neckY, rTop, 0, Math.PI * 2);
            ctx.fillStyle = cylGrad;
            ctx.fill();
        }

        // 6. Luminous Blend Halo beneath Cap
        const collarGlow = ctx.createRadialGradient(puckX, puckY + 2, 10, puckX, puckY + 2, 26);
        collarGlow.addColorStop(0, `rgba(${rCol}, ${gCol}, ${bCol}, 0.72)`);
        collarGlow.addColorStop(0.65, `rgba(${rCol}, ${gCol}, ${bCol}, 0.20)`);
        collarGlow.addColorStop(1, `rgba(${rCol}, ${gCol}, ${bCol}, 0)`);
        ctx.fillStyle = collarGlow;
        ctx.beginPath();
        ctx.arc(puckX, puckY + 2, 26, 0, Math.PI * 2);
        ctx.fill();

        // 7. 3D Machined Metallic Cap (Brushed Chrome Silver vs Smoked Gunmetal Titanium)
        const capR = 20;
        ctx.beginPath();
        ctx.arc(puckX, puckY + 3.2, capR, 0, Math.PI * 2);
        const skirtGrad = ctx.createLinearGradient(puckX - capR, puckY, puckX + capR, puckY + 6);
        if (isLight) {
            skirtGrad.addColorStop(0, "#cbd5e1");
            skirtGrad.addColorStop(0.45, "#64748b");
            skirtGrad.addColorStop(1, "#334155");
        } else {
            skirtGrad.addColorStop(0, "#475569");
            skirtGrad.addColorStop(0.45, "#1e293b");
            skirtGrad.addColorStop(1, "#020617");
        }
        ctx.fillStyle = skirtGrad;
        ctx.fill();

        // LED Accent Trim Line around Cap Waist
        ctx.beginPath();
        ctx.arc(puckX, puckY + 1.4, capR - 0.5, 0, Math.PI * 2);
        ctx.strokeStyle = `rgba(${rCol}, ${gCol}, ${bCol}, 0.90)`;
        ctx.lineWidth = 1.6;
        ctx.stroke();

        // Brushed Metallic Top Rim
        ctx.beginPath();
        ctx.arc(puckX, puckY, capR - 1, 0, Math.PI * 2);
        const domeTop = ctx.createRadialGradient(puckX - 6, puckY - 7, 2, puckX, puckY, capR);
        if (isLight) {
            domeTop.addColorStop(0.0, "#ffffff");
            domeTop.addColorStop(0.42, "#eef2f8");
            domeTop.addColorStop(0.78, "#94a3b8");
            domeTop.addColorStop(1.0, "#475569");
        } else {
            domeTop.addColorStop(0.0, "#e2e8f0");
            domeTop.addColorStop(0.32, "#94a3b8");
            domeTop.addColorStop(0.75, "#334155");
            domeTop.addColorStop(1.0, "#0f172a");
        }
        ctx.fillStyle = domeTop;
        ctx.fill();
        ctx.strokeStyle = isLight ? "rgba(255, 255, 255, 0.95)" : "rgba(255, 255, 255, 0.45)";
        ctx.lineWidth = 1.2;
        ctx.stroke();

        // Lathe-Turned Concave Metallic Thumb Dish
        const dimpleR = 12;
        ctx.beginPath();
        ctx.arc(puckX, puckY, dimpleR, 0, Math.PI * 2);
        const dimpleGrad = ctx.createLinearGradient(puckX - dimpleR, puckY - dimpleR, puckX + dimpleR, puckY + dimpleR);
        if (isLight) {
            dimpleGrad.addColorStop(0.0, "#7c8ea7");
            dimpleGrad.addColorStop(0.45, "#cbd5e1");
            dimpleGrad.addColorStop(1.0, "#ffffff");
        } else {
            dimpleGrad.addColorStop(0.0, "#0f172a");
            dimpleGrad.addColorStop(0.48, "#334155");
            dimpleGrad.addColorStop(1.0, "#94a3b8");
        }
        ctx.fillStyle = dimpleGrad;
        ctx.fill();
        ctx.strokeStyle = isLight ? "rgba(15, 23, 42, 0.25)" : "rgba(0, 0, 0, 0.65)";
        ctx.lineWidth = 1;
        ctx.stroke();

        // Lathe Concentric Grooves + Center Jewel
        [8.5, 5.5].forEach(gr => {
            ctx.beginPath();
            ctx.arc(puckX, puckY, gr, 0, Math.PI * 2);
            ctx.strokeStyle = isLight ? "rgba(15, 23, 42, 0.16)" : "rgba(255, 255, 255, 0.14)";
            ctx.lineWidth = 0.7;
            ctx.stroke();
        });

        ctx.beginPath();
        ctx.arc(puckX, puckY, 2.8, 0, Math.PI * 2);
        const pipGrad = ctx.createRadialGradient(puckX - 0.8, puckY - 0.8, 0.3, puckX, puckY, 3);
        pipGrad.addColorStop(0, "#ffffff");
        pipGrad.addColorStop(0.55, `rgb(${rCol}, ${gCol}, ${bCol})`);
        pipGrad.addColorStop(1, "rgba(15, 23, 42, 0.85)");
        ctx.fillStyle = pipGrad;
        ctx.fill();

        ctx.restore();

        // Sync live coordinate & blend readouts directly in DOM at 60fps without Blazor tree re-renders
        const coordEl = document.getElementById("vecCoordDisplay");
        if (coordEl) {
            const fmt = (n) => (n >= 0 ? "+" : "") + n.toFixed(2);
            coordEl.textContent = `X: ${fmt(vectorStateX)} \u2022 Y: ${fmt(vectorStateY)}`;
        }
        const pcts = [
            Math.round(w.wA * 100),
            Math.round(w.wB * 100),
            Math.round(w.wC * 100),
            Math.round(w.wD * 100)
        ];
        const letters = ["A", "B", "C", "D"];
        for (let i = 0; i < 4; i++) {
            const pEl = document.getElementById(`vecPct${i}`);
            if (pEl) pEl.textContent = `${letters[i]}: ${pcts[i]}%`;
            const bEl = document.getElementById(`vecBar${i}`);
            if (bEl) bEl.style.width = `${pcts[i]}%`;
            const sEl = document.getElementById(`vecSlotPct${i}`);
            if (sEl) sEl.textContent = `${pcts[i]}%`;
        }
    }

    function setupVectorTouchpad(canvasId, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || canvas.dataset.bound === "true") return;
        canvas.dataset.bound = "true";

        let dragging = false;
        let lastSend = 0;
        let syncInFlight = false;
        let rafPending = false;
        let pendingX = 0, pendingY = 0;

        function computeCoords(e) {
            const rect = canvas.getBoundingClientRect();
            const scale = (rect.width / 288) * 72;
            let x = (e.clientX - rect.left - rect.width / 2) / Math.max(1, scale);
            let y = (rect.height / 2 - (e.clientY - rect.top)) / Math.max(1, scale);
            const dist = Math.sqrt(x * x + y * y);
            if (dist > 1.0) { x /= dist; y /= dist; }
            return [x, y];
        }

        function sendToDotNet(x, y, commit) {
            if (!commit && syncInFlight) return;
            syncInFlight = true;
            lastSend = performance.now();
            dotNetRef.invokeMethodAsync("OnVectorPointerPosition", x, y, !!commit)
                .catch(() => { })
                .finally(() => { syncInFlight = false; });
        }

        function paintLoop() {
            rafPending = false;
            if (vectorTrailDecayId) {
                cancelAnimationFrame(vectorTrailDecayId);
                vectorTrailDecayId = null;
            }
            drawVectorCanvas(canvasId, pendingX, pendingY, true);
        }

        function schedulePaint() {
            if (rafPending) return;
            rafPending = true;
            requestAnimationFrame(paintLoop);
        }

        function handlePointer(e, commit) {
            const [x, y] = computeCoords(e);
            pendingX = x;
            pendingY = y;
            stopVectorOrbit();
            if (commit) {
                rafPending = false;
                if (vectorTrailDecayId) {
                    cancelAnimationFrame(vectorTrailDecayId);
                    vectorTrailDecayId = null;
                }
                drawVectorCanvas(canvasId, x, y, true);
                sendToDotNet(x, y, true);
            } else {
                schedulePaint();
                const now = performance.now();
                if (now - lastSend > 55) {
                    sendToDotNet(x, y, false);
                }
            }
        }

        canvas.addEventListener("pointerdown", (e) => {
            dragging = true;
            e.preventDefault();
            try { canvas.setPointerCapture(e.pointerId); } catch { }
            handlePointer(e, false);
        });
        canvas.addEventListener("pointermove", (e) => {
            if (dragging) {
                e.preventDefault();
                handlePointer(e, false);
            }
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
            drawVectorCanvas(canvasId, 0, 0, false);
            sendToDotNet(0, 0, true);
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

    function clearVectorTrail() {
        vectorTrail.length = 0;
        if (activeVectorCanvasId) {
            drawVectorCanvas(activeVectorCanvasId, vectorStateX, vectorStateY, false);
        }
    }

    function stopVectorOrbit() {
        if (vectorOrbitAnimId) {
            cancelAnimationFrame(vectorOrbitAnimId);
            vectorOrbitAnimId = null;
        }
    }

    // Precision Timeline Scrubber (Immune to 10Hz Blazor timer value overwrites)
    const scrubState = { dragging: false, duration: 0, pct: 0, current: 0 };

    function fmtSec(sec) {
        const s = Math.max(0, Math.floor(sec || 0));
        const m = Math.floor(s / 60);
        const rem = s % 60;
        return String(m).padStart(2, "0") + ":" + String(rem).padStart(2, "0");
    }

    function renderScrubberVisuals(pct) {
        const clampedPct = Math.max(0, Math.min(100, pct));
        scrubState.pct = clampedPct;
        const fill = document.getElementById("timeScrubberFill");
        const thumb = document.getElementById("timeScrubberThumb");
        const tip = document.getElementById("timeScrubberTooltip");
        const track = document.getElementById("timeScrubber");
        if (fill) fill.style.width = clampedPct.toFixed(2) + "%";
        if (thumb) thumb.style.left = clampedPct.toFixed(2) + "%";
        if (tip) {
            tip.style.left = clampedPct.toFixed(2) + "%";
            tip.textContent = fmtSec((clampedPct / 100.0) * scrubState.duration);
        }
        if (track) track.setAttribute("aria-valuenow", String(Math.round((clampedPct / 100.0) * scrubState.duration)));
    }

    function syncTimelineScrubber(currentSeconds, durationSeconds) {
        scrubState.duration = Math.max(0, durationSeconds || 0);
        if (scrubState.dragging) return;
        const pct = scrubState.duration > 0.05
            ? Math.min(100, Math.max(0, (currentSeconds / scrubState.duration) * 100.0))
            : 0;
        renderScrubberVisuals(pct);
    }

    function initTimelineScrubber(trackId, dotNetRef) {
        const track = document.getElementById(trackId);
        if (!track || track.dataset.bound === "true") return;
        track.dataset.bound = "true";

        function computeRatio(clientX) {
            const rect = track.getBoundingClientRect();
            if (rect.width <= 1) return 0;
            return Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
        }

        function previewAt(clientX) {
            const ratio = computeRatio(clientX);
            renderScrubberVisuals(ratio * 100.0);
            const curEl = document.getElementById("currentTimeDisplay");
            if (curEl) curEl.textContent = fmtSec(ratio * scrubState.duration);
            return ratio * scrubState.duration;
        }

        track.addEventListener("pointerdown", (e) => {
            if (scrubState.duration <= 0.05) return;
            scrubState.dragging = true;
            track.classList.add("is-scrubbing");
            try { track.setPointerCapture(e.pointerId); } catch { }
            previewAt(e.clientX);
            e.preventDefault();
        });

        track.addEventListener("pointermove", (e) => {
            const ratio = computeRatio(e.clientX);
            const tip = document.getElementById("timeScrubberTooltip");
            if (tip && !scrubState.dragging && scrubState.duration > 0.05) {
                tip.style.left = (ratio * 100.0).toFixed(2) + "%";
                tip.textContent = fmtSec(ratio * scrubState.duration);
            }
            if (scrubState.dragging) {
                previewAt(e.clientX);
            }
        });

        const finishScrub = (e) => {
            if (!scrubState.dragging) return;
            scrubState.dragging = false;
            track.classList.remove("is-scrubbing");
            const targetSec = previewAt(e.clientX);
            try { if (track.hasPointerCapture(e.pointerId)) track.releasePointerCapture(e.pointerId); } catch { }
            dotNetRef.invokeMethodAsync("CommitScrubSeconds", targetSec);
        };
        track.addEventListener("pointerup", finishScrub);
        track.addEventListener("pointercancel", finishScrub);
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
                    const isLight = document.body.classList.contains("theme-light");
                    const acc = bodyStyles.getPropertyValue("--acc").trim() || "#06b6d4";
                    const acc2 = bodyStyles.getPropertyValue("--acc-2").trim() || "#6366f1";

                    // Background horizon grid
                    ctx.strokeStyle = isLight ? "rgba(15, 23, 42, 0.08)" : "rgba(148, 163, 184, 0.08)";
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
                                ctx.fillStyle = isLight ? "#0f172a" : "#f8fafc";
                                ctx.fillRect(bx, pY, barW, 1.8);
                            }

                            // Tiny channel index base tick
                            ctx.shadowBlur = 0;
                            ctx.fillStyle = isLight ? "rgba(15, 23, 42, 0.55)" : "rgba(148, 163, 184, 0.45)";
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
        clearVectorTrail,
        initTimelineScrubber,
        syncTimelineScrubber,
        setupVectorTouchpad,
        startVectorOrbit,
        stopVectorOrbit,
        initFmEnvelopeDrag,
        initSidebarResizer,
        registerGlobalShortcuts,
        copyToClipboard
    };
})();
