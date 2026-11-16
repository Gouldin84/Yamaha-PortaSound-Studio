// PSS-790 Modern UI Application Logic

let state = {
    devices: [],
    voices: [],
    libraryFiles: [],
    currentFile: null,
    isPlaying: false,
    isPaused: false,
    durationSeconds: 0,
    currentSeconds: 0,
    speed: 1.0,
    isLooping: false,
    polyphonyGuard: true,
    channels: Array.from({ length: 16 }, (_, i) => ({
        channel: i,
        channelDisplay: i + 1,
        isDrum: i === 15,
        hasNotes: false,
        detectedInstrument: i === 15 ? "Percussion Map (Ch 16)" : "Default Piano",
        selectedVoice: null, // null = Auto
        volume: 100,
        isMuted: false,
        isSolo: false
    }))
};

// --- Interop with C# Host ---
function sendToHost(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    } else {
        console.warn("WebView2 bridge not found. Action:", action, data);
    }
}

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (event) => {
        handleHostMessage(event.data);
    });
}

function handleHostMessage(msg) {
    if (!msg || !msg.type) return;

    switch (msg.type) {
        case "initial_data":
            state.devices = msg.data.devices || [];
            state.voices = msg.data.voices || [];
            state.libraryFiles = msg.data.libraryFiles || [];
            renderDeviceSelect();
            renderLibrarySelect();
            renderMixer();
            break;

        case "file_loaded":
            state.currentFile = msg.data.fileName;
            state.durationSeconds = msg.data.durationSeconds || 0;
            document.getElementById("currentSongTitle").textContent = msg.data.fileName;
            document.getElementById("totalTimeDisplay").textContent = msg.data.durationFormatted || "00:00";
            document.getElementById("timeScrubber").max = state.durationSeconds;
            document.getElementById("timeScrubber").value = 0;
            document.getElementById("fileStatusBadge").textContent = "Loaded";

            // Update channel analysis
            if (msg.data.channels) {
                msg.data.channels.forEach(chInfo => {
                    const ch = state.channels[chInfo.channel];
                    if (ch) {
                        ch.hasNotes = chInfo.hasNotes;
                        ch.isDrum = chInfo.isDrum;
                        ch.detectedInstrument = chInfo.detectedInstrument;
                        ch.defaultPssVoice = chInfo.defaultPssVoice;
                        ch.defaultPssVoiceName = chInfo.defaultPssVoiceName;
                    }
                });
                renderMixer();
            }
            break;

        case "channel_activity":
            updateChannelLed(msg.data.channel, msg.data.isActive, msg.data.velocity);
            break;

        case "playback_state":
            state.isPlaying = msg.data.isPlaying;
            state.isPaused = msg.data.isPaused;
            state.currentSeconds = msg.data.currentSeconds || 0;
            updatePlaybackUi();
            break;

        case "batch_progress":
            updateBatchProgress(msg.data);
            break;

        case "batch_complete":
            showToast(`Batch conversion complete! Converted ${msg.data.convertedCount} files to: ${msg.data.targetDirectory}`);
            document.getElementById("batchProgressContainer").classList.add("hidden");
            document.getElementById("btnStartBatch").disabled = false;
            break;

        case "notification":
            showToast(msg.data.message);
            break;

        case "error":
            showToast(msg.data.message, true);
            break;
    }
}

// --- DOM Rendering ---

function renderDeviceSelect() {
    const sel = document.getElementById("deviceSelect");
    sel.innerHTML = "";
    if (state.devices.length === 0) {
        sel.innerHTML = `<option value="" disabled selected>No MIDI Ports Detected</option>`;
        return;
    }
    state.devices.forEach((dev, idx) => {
        const opt = document.createElement("option");
        opt.value = dev;
        opt.textContent = dev;
        opt.className = "bg-slate-900 text-white";
        if (idx === 0) opt.selected = true;
        sel.appendChild(opt);
    });
}

function renderLibrarySelect() {
    const sel = document.getElementById("librarySelect");
    sel.innerHTML = `<option value="" selected disabled>Select Sample...</option>`;
    state.libraryFiles.forEach(file => {
        const opt = document.createElement("option");
        opt.value = file;
        opt.textContent = file;
        opt.className = "bg-slate-900 text-white";
        sel.appendChild(opt);
    });
}

function renderMixer() {
    const grid = document.getElementById("mixerGrid");
    grid.innerHTML = "";

    state.channels.forEach(ch => {
        const card = document.createElement("div");
        const isDrum = ch.channel === 15;

        card.className = `glass-card p-3 rounded-2xl flex flex-col justify-between space-y-3 transition-all duration-200 ${
            isDrum ? 'border-amber-500/30 bg-amber-950/10' : ''
        }`;

        // Voice options
        let voiceOptionsHtml = `<option value="auto" ${ch.selectedVoice === null ? 'selected' : ''}>[Auto: ${ch.defaultPssVoiceName || 'GM Mapped'}]</option>`;
        if (!isDrum) {
            state.voices.forEach(v => {
                const voiceVal = v.number;
                const isSelected = ch.selectedVoice === voiceVal;
                voiceOptionsHtml += `<option value="${voiceVal}" ${isSelected ? 'selected' : ''}>${String(v.number).padStart(2, '0')}: ${v.name}</option>`;
            });
        } else {
            voiceOptionsHtml = `<option value="96" selected>96: PERCUSSIONS (Chart 1)</option>`;
        }

        card.innerHTML = `
            <div>
                <!-- Channel Header & LED -->
                <div class="flex items-center justify-between mb-1.5">
                    <div class="flex items-center space-x-1.5">
                        <span class="text-xs font-black tracking-tight ${isDrum ? 'text-amber-400' : 'text-cyan-400'}">CH ${ch.channelDisplay}</span>
                        <span class="text-[9px] font-bold uppercase px-1.5 py-0.2 rounded ${
                            isDrum ? 'bg-amber-950 text-amber-300 border border-amber-800/60' : 'bg-slate-800 text-slate-300'
                        }">${isDrum ? 'Drums' : (ch.hasNotes ? 'Active' : 'Empty')}</span>
                    </div>
                    <!-- Activity LED -->
                    <div id="led-ch-${ch.channel}" class="meter-led w-3 h-3 rounded-full bg-slate-800 border border-slate-700/60 shadow-inner"></div>
                </div>

                <!-- Detected GM Instrument Name -->
                <p class="text-[10px] text-slate-400 truncate font-medium mb-2" title="${ch.detectedInstrument}">
                    ${ch.detectedInstrument}
                </p>

                <!-- Voice Override Selector -->
                <div class="space-y-1">
                    <label class="text-[9px] uppercase font-bold text-slate-500 block">PSS-790 Voice</label>
                    <select data-channel="${ch.channel}" class="voice-select w-full bg-slate-900 border border-slate-800 hover:border-slate-700 rounded-lg px-2 py-1 text-[11px] text-slate-200 focus:outline-none focus:border-cyan-500 cursor-pointer ${isDrum ? 'opacity-70 cursor-not-allowed' : ''}" ${isDrum ? 'disabled' : ''}>
                        ${voiceOptionsHtml}
                    </select>
                </div>
            </div>

            <!-- Fader & Controls Footer -->
            <div class="space-y-2 pt-2 border-t border-slate-800/60">
                <!-- Solo & Mute -->
                <div class="grid grid-cols-2 gap-1.5">
                    <button data-channel="${ch.channel}" class="btn-mute py-1 rounded-lg text-[10px] font-bold border transition ${
                        ch.isMuted ? 'bg-rose-600 text-white border-rose-500' : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white hover:bg-slate-800'
                    }">M</button>
                    <button data-channel="${ch.channel}" class="btn-solo py-1 rounded-lg text-[10px] font-bold border transition ${
                        ch.isSolo ? 'bg-amber-500 text-slate-950 border-amber-400 shadow-sm' : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white hover:bg-slate-800'
                    }">S</button>
                </div>

                <!-- Volume Slider -->
                <div class="space-y-1">
                    <div class="flex items-center justify-between text-[9px] font-mono text-slate-400">
                        <span>VOL</span>
                        <span id="vol-display-${ch.channel}">${ch.volume}</span>
                    </div>
                    <input type="range" data-channel="${ch.channel}" min="0" max="127" value="${ch.volume}" class="vol-slider w-full h-1.5">
                </div>
            </div>
        `;

        grid.appendChild(card);
    });

    attachMixerEvents();
}

function attachMixerEvents() {
    // Voice selection change
    document.querySelectorAll(".voice-select").forEach(sel => {
        sel.addEventListener("change", (e) => {
            const ch = parseInt(e.target.dataset.channel);
            const val = e.target.value;
            const voiceNum = val === "auto" ? null : parseInt(val);
            state.channels[ch].selectedVoice = voiceNum;
            sendToHost("override_voice", { channel: ch, voice: voiceNum });
        });
    });

    // Mute button
    document.querySelectorAll(".btn-mute").forEach(btn => {
        btn.addEventListener("click", (e) => {
            const ch = parseInt(e.target.dataset.channel);
            const isMuted = !state.channels[ch].isMuted;
            state.channels[ch].isMuted = isMuted;
            btn.className = `btn-mute py-1 rounded-lg text-[10px] font-bold border transition ${
                isMuted ? 'bg-rose-600 text-white border-rose-500' : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white hover:bg-slate-800'
            }`;
            sendToHost("set_channel_mute", { channel: ch, mute: isMuted });
        });
    });

    // Solo button
    document.querySelectorAll(".btn-solo").forEach(btn => {
        btn.addEventListener("click", (e) => {
            const ch = parseInt(e.target.dataset.channel);
            const isSolo = !state.channels[ch].isSolo;
            state.channels[ch].isSolo = isSolo;
            btn.className = `btn-solo py-1 rounded-lg text-[10px] font-bold border transition ${
                isSolo ? 'bg-amber-500 text-slate-950 border-amber-400 shadow-sm' : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white hover:bg-slate-800'
            }`;
            sendToHost("set_channel_solo", { channel: ch, solo: isSolo });
        });
    });

    // Volume slider
    document.querySelectorAll(".vol-slider").forEach(slider => {
        slider.addEventListener("input", (e) => {
            const ch = parseInt(e.target.dataset.channel);
            const vol = parseInt(e.target.value);
            state.channels[ch].volume = vol;
            document.getElementById(`vol-display-${ch}`).textContent = vol;
            sendToHost("set_channel_volume", { channel: ch, volume: vol });
        });
    });
}

function updateChannelLed(channel, isActive, velocity) {
    const led = document.getElementById(`led-ch-${channel}`);
    if (!led) return;

    const isDrum = channel === 15;
    const activeClass = isDrum ? 'drum-active' : 'active';

    if (isActive) {
        led.classList.add(activeClass);
    } else {
        led.classList.remove('active', 'drum-active');
    }
}

function updatePlaybackUi() {
    const playIcon = document.getElementById("playIcon");
    const pauseIcon = document.getElementById("pauseIcon");
    const statusBadge = document.getElementById("fileStatusBadge");
    const scrubber = document.getElementById("timeScrubber");
    const currentDisplay = document.getElementById("currentTimeDisplay");

    if (state.isPlaying) {
        playIcon.classList.add("hidden");
        pauseIcon.classList.remove("hidden");
        statusBadge.textContent = "Playing";
        statusBadge.className = "px-2 py-0.5 rounded bg-emerald-950 text-emerald-400 font-semibold border border-emerald-800/60";
    } else if (state.isPaused) {
        playIcon.classList.remove("hidden");
        pauseIcon.classList.add("hidden");
        statusBadge.textContent = "Paused";
        statusBadge.className = "px-2 py-0.5 rounded bg-amber-950 text-amber-400 font-semibold border border-amber-800/60";
    } else {
        playIcon.classList.remove("hidden");
        pauseIcon.classList.add("hidden");
        statusBadge.textContent = state.currentFile ? "Ready" : "Idle";
        statusBadge.className = "px-2 py-0.5 rounded bg-slate-800 text-slate-300 font-medium";
    }

    scrubber.value = state.currentSeconds;
    currentDisplay.textContent = formatTime(state.currentSeconds);
}

function formatTime(seconds) {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
}

function updateBatchProgress(data) {
    const container = document.getElementById("batchProgressContainer");
    container.classList.remove("hidden");

    document.getElementById("batchCurrentFile").textContent = `Converting: ${data.fileName} (${data.current}/${data.total})`;
    document.getElementById("batchPercent").textContent = `${data.percent}%`;
    document.getElementById("batchProgressBar").style.width = `${data.percent}%`;
}

function showToast(message, isError = false) {
    const toast = document.getElementById("notificationToast");
    const msg = document.getElementById("notificationMessage");
    msg.textContent = message;

    toast.className = `fixed bottom-6 right-6 z-50 px-4 py-3 rounded-xl border text-xs text-white shadow-2xl flex items-center space-x-2 transition-all duration-300 ${
        isError ? 'bg-rose-950 border-rose-500/50' : 'bg-slate-900 border-cyan-500/40'
    }`;

    toast.classList.remove("translate-y-20", "opacity-0");
    setTimeout(() => {
        toast.classList.add("translate-y-20", "opacity-0");
    }, 3500);
}

// --- Global UI Event Listeners ---

document.getElementById("deviceSelect").addEventListener("change", (e) => {
    sendToHost("select_device", { deviceName: e.target.value });
});

document.getElementById("btnBrowse").addEventListener("click", () => {
    sendToHost("browse_file");
});

document.getElementById("librarySelect").addEventListener("change", (e) => {
    if (e.target.value) {
        sendToHost("load_file", { filePath: e.target.value });
    }
});

document.getElementById("btnSaveRemapped").addEventListener("click", () => {
    sendToHost("save_remapped");
});

document.getElementById("btnPlayPause").addEventListener("click", () => {
    if (state.isPlaying) {
        sendToHost("pause");
    } else {
        sendToHost("play");
    }
});

document.getElementById("btnStop").addEventListener("click", () => {
    sendToHost("stop");
});

document.getElementById("timeScrubber").addEventListener("input", (e) => {
    const sec = parseFloat(e.target.value);
    document.getElementById("currentTimeDisplay").textContent = formatTime(sec);
});

document.getElementById("timeScrubber").addEventListener("change", (e) => {
    const sec = parseFloat(e.target.value);
    sendToHost("seek", { seconds: sec });
});

document.querySelectorAll(".speed-btn").forEach(btn => {
    btn.addEventListener("click", (e) => {
        const speed = parseFloat(e.target.dataset.speed);
        state.speed = speed;
        document.querySelectorAll(".speed-btn").forEach(b => {
            b.className = "speed-btn px-2.5 py-1 text-xs font-semibold rounded-lg text-slate-400 hover:text-white transition";
        });
        e.target.className = "speed-btn px-2.5 py-1 text-xs font-semibold rounded-lg bg-cyan-500 text-slate-950 shadow-sm transition";
        sendToHost("set_speed", { speed });
    });
});

document.getElementById("btnLoop").addEventListener("click", (e) => {
    state.isLooping = !state.isLooping;
    const btn = document.getElementById("btnLoop");
    if (state.isLooping) {
        btn.className = "px-3 py-2 rounded-xl bg-cyan-500/20 text-cyan-400 border border-cyan-500/40 text-xs font-semibold transition flex items-center space-x-1.5 shadow-sm";
    } else {
        btn.className = "px-3 py-2 rounded-xl bg-slate-800/80 hover:bg-slate-700 text-xs font-medium text-slate-400 hover:text-white border border-slate-700/60 transition flex items-center space-x-1.5";
    }
    sendToHost("set_loop", { loop: state.isLooping });
});

document.getElementById("polyphonyGuardToggle").addEventListener("change", (e) => {
    state.polyphonyGuard = e.target.checked;
    sendToHost("toggle_polyphony_guard", { enabled: state.polyphonyGuard });
    showToast(`Smart Polyphony Guard: ${state.polyphonyGuard ? 'Enabled' : 'Disabled'}`);
});

document.getElementById("btnResetOverrides").addEventListener("click", () => {
    state.channels.forEach(ch => {
        if (!ch.isDrum) {
            ch.selectedVoice = null;
            sendToHost("override_voice", { channel: ch.channel, voice: null });
        }
    });
    renderMixer();
    showToast("Voice overrides reset to automatic GM mapping.");
});

// Batch Modal Handlers
const batchModal = document.getElementById("batchModal");
document.getElementById("btnOpenBatch").addEventListener("click", () => {
    batchModal.classList.remove("hidden");
});

document.getElementById("btnCloseBatch").addEventListener("click", () => {
    batchModal.classList.add("hidden");
});

document.getElementById("btnCancelBatch").addEventListener("click", () => {
    batchModal.classList.add("hidden");
    sendToHost("cancel_batch");
});

document.getElementById("btnStartBatch").addEventListener("click", () => {
    const src = document.getElementById("batchSourceDir").value;
    const tgt = document.getElementById("batchTargetDir").value;
    document.getElementById("btnStartBatch").disabled = true;
    sendToHost("start_batch", { sourceDir: src, targetDir: tgt });
});

// Trigger initial handshake
window.addEventListener("DOMContentLoaded", () => {
    sendToHost("get_initial_data");
});
