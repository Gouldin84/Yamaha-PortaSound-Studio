// PSS-790 Modern UI Application Logic & MIDI Studio Engine

let state = {
    activeTab: "player",
    devices: [],
    inputDevices: [],
    selectedDevice: null,
    selectedInputDevice: null,
    voices: [],
    activeModel: localStorage.getItem("yamaha_studio_model") || "pss790",
    pss790Voices: [],
    pss680Voices: [],
    pss680Drums: [],
    library: { gmTree: null, pssTree: null, gmFiles: [], pssFiles: [], flatFiles: [], hasFiles: false, baseDir: "" },
    currentFile: null,
    currentFilePath: null,
    isPlaying: false,
    isPaused: false,
    durationSeconds: 0,
    currentSeconds: 0,
    speed: 1.0,
    isLooping: false,
    polyphonyGuard: true,
    sysexViewMode: "designer",
    fmVoices: [],
    vectorPresets: [],
    vectorQuadActive: false,
    selectedVectorPresetKey: null,
    // Jukebox & Quick-Pick Tree State
    isSidebarCollapsed: false,
    selectedSongPath: null,
    searchQuery: "",
    expandedFolders: new Set(),
    collapsedCategories: new Set(),
    visibleSongList: [],
    favorites: new Set(),
    collapsedFavorites: new Set(),
    sidebarWidth: 280,
    isAutoFitSidebar: true,
    // Multi-Selection State
    isSelectMode: false,
    selectedSongs: new Set(),
    lastCheckedSongPath: null,
    channels: Array.from({ length: 16 }, (_, i) => ({
        channel: i,
        channelDisplay: i + 1,
        isDrum: i === 15,
        hasNotes: false,
        detectedInstrument: i === 15 ? "Percussion Map (Ch 16)" : "Default Piano",
        selectedVoice: null,
        volume: 100,
        isMuted: false,
        isSolo: false,
        octaveShift: 0
    })),
    // Master Pitch & Transpose
    masterTranspose: 0,
    pitchBendMode: "auto",
    activeFmBank: 0,
    // Studio / Recorder State
    recorderState: "idle",
    recorderBpm: 120,
    lastBeat: 0,
    recorderChannels: Array.from({ length: 16 }, (_, i) => ({
        channel: i,
        channelDisplay: i + 1,
        isDrum: i === 15,
        isActive: false,
        velocity: 0,
        lastNote: "-"
    })),
    // SysEx Vault State
    sysexLibrary: [],
    selectedSysexDump: null,
    isSysexCapturing: false,
    isSysexTransmitting: false,
    sysexSearchQuery: "",
    lastInspectedHex: "",
    theme: "dark",
    // Studio Recordings Vault & Live Hardware Metering
    activeInboundNotes: new Map(),
    vuLeftLevel: 0,
    vuRightLevel: 0,
    vuLeftPeak: 0,
    vuRightPeak: 0,
    vuLeftPeakTimer: null,
    vuRightPeakTimer: null,
    recordingsLibrary: [],
    recordingsSearchQuery: "",
    activePreviewRecordingPath: null,
    initialDataReceived: false,
    // Handheld Navigation State
    autoAdvance: localStorage.getItem("pss_auto_advance") !== "false",
    handheldMode: localStorage.getItem("pss_handheld_mode") === "true"
};

// --- Theme Management ---
function applyTheme(theme) {
    state.theme = theme;
    const isLight = theme === "light";
    document.body.classList.toggle("theme-light", isLight);
    const icon = document.getElementById("themeToggleIcon");
    const text = document.getElementById("themeToggleText");
    const btn = document.getElementById("btnThemeToggle");
    if (icon) icon.textContent = isLight ? "🌙" : "☀️";
    if (text) text.textContent = isLight ? "Dark" : "Light";
    if (btn) btn.title = `Switch to ${isLight ? "Dark" : "Light"} Studio Theme (currently ${isLight ? "Light" : "Dark"})`;
    try {
        localStorage.setItem("pss790_theme", theme);
    } catch (e) { }
}

function toggleTheme() {
    const next = state.theme === "light" ? "dark" : "light";
    applyTheme(next);
    showToast(`Switched to ${next === "light" ? "Light" : "Dark"} Studio Theme`);
}

function initTheme() {
    try {
        const saved = localStorage.getItem("pss790_theme");
        if (saved === "light" || saved === "dark") {
            applyTheme(saved);
        } else {
            applyTheme("dark");
        }
    } catch (e) {
        applyTheme("dark");
    }
}

// --- Web Audio Metronome Click ---
let audioCtx = null;
function playMetronomeClick(isHigh) {
    const chk = document.getElementById("chkMetronome");
    if (!chk || !chk.checked) return;
    try {
        if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        if (audioCtx.state === 'suspended') audioCtx.resume();
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.frequency.value = isHigh ? 1200 : 800;
        gain.gain.setValueAtTime(0.2, audioCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.04);
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.start();
        osc.stop(audioCtx.currentTime + 0.04);
    } catch (e) { }
}

const NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
function getNoteName(noteNumber) {
    const octave = Math.floor(noteNumber / 12) - 1;
    const name = NOTE_NAMES[noteNumber % 12];
    return `${name}${octave}`;
}

// --- Favourites Management Helpers ---
function normalizeFavoriteKey(path) {
    if (!path) return "";
    let clean = String(path).replace(/\\/g, "/");
    clean = clean.replace(/^.*?\/(GM Midi|PSS-790 Midi|PSS-680 Midi)\//i, "");
    clean = clean.replace(/^(GM Midi|PSS-790 Midi|PSS-680 Midi)\//i, "");
    return clean.toLowerCase();
}

function isSongFavorite(path) {
    if (!path || !state.favorites || state.favorites.size === 0) return false;
    const key = normalizeFavoriteKey(path);
    const fileName = String(path).split(/[\/\\]/).pop().toLowerCase();
    for (const fav of state.favorites) {
        const normFav = normalizeFavoriteKey(fav);
        if (normFav === key || normFav.endsWith("/" + key) || key.endsWith("/" + normFav) || normFav === fileName) {
            return true;
        }
    }
    return false;
}

function updateCurrentSongFavoriteButton() {
    const btn = document.getElementById("btnToggleFavoriteCurrent");
    const star = document.getElementById("starIconCurrent");
    if (!btn || !star) return;

    if (!state.currentFilePath) {
        btn.classList.add("opacity-40", "cursor-not-allowed");
        btn.title = "No song loaded";
        star.textContent = "☆";
        star.className = "text-slate-500";
        return;
    }

    btn.classList.remove("opacity-40", "cursor-not-allowed");
    const isFav = isSongFavorite(state.currentFilePath);
    if (isFav) {
        star.textContent = "★";
        star.className = "text-amber-400 font-bold text-sm";
        btn.title = "Remove currently playing song from Favourites (F)";
    } else {
        star.textContent = "☆";
        star.className = "text-slate-400 hover:text-amber-400 text-sm";
        btn.title = "Add currently playing song to Favourites (F)";
    }
}

// --- Tab Switching ---
function switchTab(tab) {
    state.activeTab = tab;
    const isPlayer = tab === "player";
    const isRecorder = tab === "recorder";
    const isSysex = tab === "sysex";

    const viewPlayer = document.getElementById("viewPlayer");
    const viewRecorder = document.getElementById("viewRecorder");
    const viewSysex = document.getElementById("viewSysex");

    if (viewPlayer) viewPlayer.classList.toggle("hidden", !isPlayer);
    if (viewRecorder) viewRecorder.classList.toggle("hidden", !isRecorder);
    if (viewSysex) viewSysex.classList.toggle("hidden", !isSysex);

    const tabPlayer = document.getElementById("tabBtnPlayer");
    const tabRec = document.getElementById("tabBtnRecorder");
    const tabSysex = document.getElementById("tabBtnSysex");

    const activeCls = "tab-btn active flex items-center space-x-1 sm:space-x-1.5 px-2.5 sm:px-3 py-1 sm:py-1.5 rounded-md text-xs font-semibold text-white transition-all";
    const inactiveCls = "tab-btn flex items-center space-x-1 sm:space-x-1.5 px-2.5 sm:px-3 py-1 sm:py-1.5 rounded-md text-xs font-semibold text-slate-400 hover:text-white transition-all";

    if (tabPlayer) tabPlayer.className = isPlayer ? activeCls : inactiveCls;
    if (tabRec) tabRec.className = isRecorder ? activeCls : inactiveCls;
    if (tabSysex) tabSysex.className = isSysex ? activeCls : inactiveCls;

    if (isSysex) {
        renderSysExLibrary();
        updateSysexDeckVisibility();
    }
    if (isRecorder) {
        renderRecordingsLibrary();
        sendToHost("get_recordings_library");
    }
}

// --- Interop with C# Host ---
let bridgeListenerAttached = false;
const pendingHostMessages = [];
let bridgePollInterval = null;

function setupBridgeListener() {
    if (bridgeListenerAttached) return;
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            handleHostMessage(event.data);
        });
        bridgeListenerAttached = true;
    }
}

function flushPendingMessages() {
    if (window.chrome && window.chrome.webview) {
        while (pendingHostMessages.length > 0) {
            const item = pendingHostMessages.shift();
            try {
                window.chrome.webview.postMessage({ action: item.action, ...item.data });
            } catch (e) {
                console.error("WebView2 flush message error:", e);
            }
        }
    }
}

function sendToHost(action, data = {}) {
    setupBridgeListener();
    if (window.chrome && window.chrome.webview) {
        try {
            window.chrome.webview.postMessage({ action, ...data });
        } catch (e) {
            console.error("WebView2 postMessage failed:", e);
        }
    } else {
        pendingHostMessages.push({ action, data });
        ensureBridgeConnected();
    }
}

function ensureBridgeConnected(onReady) {
    setupBridgeListener();
    if (window.chrome && window.chrome.webview) {
        flushPendingMessages();
        if (bridgePollInterval) {
            clearInterval(bridgePollInterval);
            bridgePollInterval = null;
        }
        if (onReady) onReady();
        return;
    }

    if (!bridgePollInterval) {
        let attempts = 0;
        bridgePollInterval = setInterval(() => {
            attempts++;
            if (window.chrome && window.chrome.webview) {
                clearInterval(bridgePollInterval);
                bridgePollInterval = null;
                setupBridgeListener();
                flushPendingMessages();
                if (onReady) onReady();
            } else if (attempts > 120) {
                clearInterval(bridgePollInterval);
                bridgePollInterval = null;
                console.warn("WebView2 bridge detection timed out after 3s.");
            }
        }, 25);
    }
}

setupBridgeListener();
ensureBridgeConnected();

function handleHostMessage(msg) {
    if (typeof msg === "string") {
        try {
            msg = JSON.parse(msg);
        } catch (e) {
            console.error("Failed to parse host message JSON:", e);
            return;
        }
    }
    if (!msg || !msg.type) return;

    switch (msg.type) {
        case "initial_data":
            state.initialDataReceived = true;
            state.devices = msg.data.devices || [];
            state.inputDevices = msg.data.inputDevices || [];
            state.pss790Voices = msg.data.pss790Voices || msg.data.voices || [];
            state.pss680Voices = msg.data.pss680Voices || [];
            state.pss680Drums = msg.data.pss680Drums || [];
            state.library = msg.data.library || { gmFiles: [], pssFiles: [], flatFiles: [], hasFiles: false };
            applyActiveModel(state.activeModel, false);
            if (msg.data.recorderBpm) {
                state.recorderBpm = msg.data.recorderBpm;
                const bpmIn = document.getElementById("recorderBpmInput");
                const bpmSlide = document.getElementById("recorderBpmSlider");
                if (bpmIn) bpmIn.value = state.recorderBpm;
                if (bpmSlide) bpmSlide.value = state.recorderBpm;
            }
            if (msg.data.masterTranspose !== undefined) {
                state.masterTranspose = msg.data.masterTranspose;
                updateMasterTransposeDisplay();
            }
            if (msg.data.pitchBendMode) {
                state.pitchBendMode = msg.data.pitchBendMode;
            }
            if (msg.data.midiThru !== undefined) {
                const chk = document.getElementById("chkMidiThru");
                if (chk) chk.checked = msg.data.midiThru;
            }
            if (msg.data.defaultBatchSource) {
                const bSrc = document.getElementById("batchSourceDir");
                if (bSrc) bSrc.value = msg.data.defaultBatchSource;
            }
            if (msg.data.defaultBatchTarget790 || msg.data.defaultBatchTarget) {
                const bTgt790 = document.getElementById("batchTarget790Dir");
                if (bTgt790) bTgt790.value = msg.data.defaultBatchTarget790 || msg.data.defaultBatchTarget;
            }
            if (msg.data.defaultBatchTarget680 || msg.data.defaultBatchTarget) {
                const bTgt680 = document.getElementById("batchTarget680Dir");
                if (bTgt680) bTgt680.value = msg.data.defaultBatchTarget680 || msg.data.defaultBatchTarget;
            }
            if (msg.data.sysexLibrary) {
                state.sysexLibrary = msg.data.sysexLibrary;
                renderSysExLibrary();
            }
            if (msg.data.fmVoiceLibrary) {
                state.fmVoices = msg.data.fmVoiceLibrary;
                renderFmVoiceLibrary();
            }
            if (msg.data.vectorLibrary) {
                state.vectorPresets = msg.data.vectorLibrary;
                renderVectorPresetLibrary();
            }
            initVectorVoiceDropdowns();
            if (msg.data.recordingsLibrary) {
                handleRecordingsLibraryUpdated(msg.data.recordingsLibrary);
            }
            if (msg.data.favorites) {
                state.favorites = new Set(msg.data.favorites);
            }
            updateCurrentSongFavoriteButton();
            renderDeviceSelect(msg.data.defaultDevice);
            renderInputDeviceSelect(msg.data.defaultInputDevice);
            renderLibrarySelect();
            renderLibraryTree();
            renderMixer();
            renderRecorderChannels();
            initSidebarState();
            renderHealthBadge(null);
            break;

        case "favorites_updated":
            if (msg.data.favorites) state.favorites = new Set(msg.data.favorites);
            if (msg.data.toastMessage) showToast(msg.data.toastMessage);
            updateCurrentSongFavoriteButton();
            renderLibraryTree();
            break;

        case "devices_updated":
            if (msg.data.devices) state.devices = msg.data.devices;
            if (msg.data.inputDevices) state.inputDevices = msg.data.inputDevices;
            renderDeviceSelect(msg.data.currentOutputDevice);
            renderInputDeviceSelect(msg.data.currentInputDevice);
            if (msg.data.toastMessage) {
                showToast(msg.data.toastMessage);
            }
            break;

        case "input_devices_updated":
            state.inputDevices = msg.data.inputDevices || [];
            renderInputDeviceSelect(msg.data.currentDevice);
            break;

        case "library_updated":
            state.library = msg.data || { gmFiles: [], pssFiles: [], flatFiles: [], hasFiles: false };
            if (msg.data && msg.data.favorites) {
                state.favorites = new Set(msg.data.favorites);
            }
            updateCurrentSongFavoriteButton();
            renderLibrarySelect();
            renderLibraryTree();
            break;

        case "channels_updated":
            if (msg.data.channels) {
                msg.data.channels.forEach(chInfo => {
                    const ch = state.channels[chInfo.channel];
                    if (ch) {
                        ch.hasNotes = chInfo.hasNotes;
                        ch.isDrum = chInfo.isDrum;
                        ch.detectedInstrument = chInfo.detectedInstrument;
                        ch.defaultPssVoice = chInfo.defaultPssVoice;
                        ch.defaultPssVoiceName = chInfo.defaultPssVoiceName;
                        ch.selectedVoice = chInfo.voiceOverride !== undefined ? chInfo.voiceOverride : null;
                    }
                });
                renderMixer();
            }
            break;

        case "playback_format_updated":
            renderFormatBadge(msg.data.format, msg.data.isGmPassthrough);
            if (msg.data.channels) {
                msg.data.channels.forEach(chInfo => {
                    const ch = state.channels[chInfo.channel];
                    if (ch) {
                        ch.hasNotes = chInfo.hasNotes;
                        ch.isDrum = chInfo.isDrum;
                        ch.detectedInstrument = chInfo.detectedInstrument;
                        ch.defaultPssVoice = chInfo.defaultPssVoice;
                        ch.defaultPssVoiceName = chInfo.defaultPssVoiceName;
                        ch.selectedVoice = chInfo.voiceOverride !== undefined ? chInfo.voiceOverride : null;
                    }
                });
                renderMixer();
            }
            break;

        case "file_loaded":
            state.currentFile = msg.data.fileName;
            state.currentFilePath = msg.data.filePath;
            state.selectedSongPath = msg.data.filePath;
            state.durationSeconds = msg.data.durationSeconds || 0;
            updateCurrentSongFavoriteButton();
            document.getElementById("currentSongTitle").textContent = msg.data.fileName;
            document.getElementById("totalTimeDisplay").textContent = msg.data.durationFormatted || "00:00";
            document.getElementById("timeScrubber").max = state.durationSeconds;
            document.getElementById("timeScrubber").value = 0;
            updatePlaybackUi();

            // Format Badge Rendering
            renderFormatBadge(msg.data.format, msg.data.isGmPassthrough);

            // Health Diagnostics Badge Rendering
            state.currentHealth = msg.data.health;
            renderHealthBadge(msg.data.health);

            if (msg.data.masterTranspose !== undefined) {
                state.masterTranspose = msg.data.masterTranspose;
                updateMasterTransposeDisplay();
            }

            // Update channel analysis & restored voice overrides
            if (msg.data.channels) {
                msg.data.channels.forEach(chInfo => {
                    const ch = state.channels[chInfo.channel];
                    if (ch) {
                        ch.hasNotes = chInfo.hasNotes;
                        ch.isDrum = chInfo.isDrum;
                        ch.detectedInstrument = chInfo.detectedInstrument;
                        ch.defaultPssVoice = chInfo.defaultPssVoice;
                        ch.defaultPssVoiceName = chInfo.defaultPssVoiceName;
                        ch.selectedVoice = chInfo.voiceOverride !== undefined ? chInfo.voiceOverride : null;
                        ch.octaveShift = chInfo.octaveShift !== undefined ? chInfo.octaveShift : 0;
                    }
                });
                renderMixer();
            }
            highlightActiveSongInTree();
            break;

        case "master_transpose_changed":
            state.masterTranspose = msg.data.transpose;
            updateMasterTransposeDisplay();
            break;

        case "channel_octave_changed":
            if (state.channels[msg.data.channel]) {
                state.channels[msg.data.channel].octaveShift = msg.data.octaveShift;
                renderMixer();
            }
            break;

        case "fm_active_bank_changed":
            state.activeFmBank = msg.data.bank;
            updateActiveFmBankDisplay(msg.data.bank, msg.data.voiceNumber);
            break;

        case "fm_voice_dump_received":
            updateFmVoiceUI(msg.data);
            break;

        case "channel_activity":
            updateChannelLed(msg.data.channel, msg.data.isActive, msg.data.velocity);
            if (state.activeTab === "recorder" && msg.data.isActive) {
                feedVuMeter(msg.data.velocity, msg.data.channel);
            }
            break;

        case "playback_state":
            const playStateChanged = state.isPlaying !== msg.data.isPlaying || state.isPaused !== msg.data.isPaused;
            state.isPlaying = msg.data.isPlaying;
            state.isPaused = msg.data.isPaused;
            state.currentSeconds = msg.data.currentSeconds || 0;
            if (msg.data.totalSeconds && msg.data.totalSeconds > 0) {
                state.durationSeconds = msg.data.totalSeconds;
            }
            updatePlaybackUi();
            if (playStateChanged) {
                highlightActiveSongInTree();
                if (!state.isPlaying) {
                    clearAllChannelLeds();
                    if (state.activePreviewRecordingPath) {
                        handleRecordingPreviewStopped();
                    }
                }
            }
            break;

        case "playback_finished":
            if (state.autoAdvance && !state.isLooping) {
                setTimeout(() => {
                    if (!state.isPlaying) {
                        playNextSong();
                    }
                }, 350);
            }
            break;

        // --- Recorder Events ---
        case "recorder_state":
            updateRecorderState(msg.data.state, msg.data.countInBeat, msg.data.bpm);
            break;

        case "recorder_tick":
            updateRecorderTick(msg.data);
            break;

        case "recorder_input_activity":
            updateRecorderChannelActivity(msg.data.channel, msg.data.isActive, msg.data.velocity, msg.data.noteNumber);
            break;

        case "recorder_take_ready":
            showTakeReadyCard(msg.data);
            break;

        case "take_saved":
            const targetFolder = msg.data.targetFolder || (msg.data.isPss790 ? (state.activeModel === 'pss680' ? 'PSS-680 Midi' : 'PSS-790 Midi') : 'GM Midi');
            showToast(`Saved to ${targetFolder}: ${msg.data.fileName}`);
            break;

        case "recordings_library_updated":
            handleRecordingsLibraryUpdated(msg.data.recordings);
            break;

        case "recording_preview_started":
            handleRecordingPreviewStarted(msg.data.filePath);
            break;

        case "recording_preview_stopped":
            handleRecordingPreviewStopped();
            break;

        case "linked_file_checked":
            handleLinkedFileChecked(msg.data);
            break;

        case "switch_tab":
            if (msg.data.tab) switchTab(msg.data.tab);
            break;

        case "batch_progress":
            updateBatchProgress(msg.data);
            break;

        case "batch_complete":
            showToast(`Batch conversion complete! Converted ${msg.data.convertedCount} files to: ${msg.data.targetDirectory}`);
            document.getElementById("batchProgressContainer").classList.add("hidden");
            document.getElementById("btnStartBatch").disabled = false;
            break;

        case "audit_status":
            document.getElementById("batchProgressContainer").classList.remove("hidden");
            document.getElementById("batchCurrentFile").textContent = msg.data.message || "Auditing...";
            break;

        case "audit_progress":
            document.getElementById("batchProgressContainer").classList.remove("hidden");
            const aCur = msg.data.current;
            const aTot = msg.data.total;
            const aPct = aTot > 0 ? Math.round((aCur / aTot) * 100) : 0;
            document.getElementById("batchCurrentFile").textContent = `Auditing file ${aCur} of ${aTot}...`;
            document.getElementById("batchPercent").textContent = `${aPct}%`;
            document.getElementById("batchProgressBar").style.width = `${aPct}%`;
            break;

        case "health_audit_result":
            document.getElementById("batchProgressContainer").classList.add("hidden");
            handleHealthAuditResult(msg.data);
            break;

        case "library_sync_analysis":
            handleLibrarySyncAnalysis(msg.data);
            break;

        case "sync_progress":
            updateSyncProgress(msg.data);
            break;

        case "sync_completed":
            handleSyncCompleted(msg.data);
            break;

        case "sysex_capture_progress":
            updateSysexCaptureProgress(msg.data);
            break;

        case "sysex_dump_captured":
            handleSysexDumpCaptured(msg.data);
            break;

        case "sysex_transmit_progress":
            updateSysexTransmitProgress(msg.data);
            break;

        case "sysex_library_updated":
            state.sysexLibrary = msg.data || [];
            renderSysExLibrary();
            break;

        case "sysex_dump_inspected":
            showSysexInspectModal(msg.data);
            break;

        case "fm_voice_library_updated":
            state.fmVoices = msg.data.voices || [];
            renderFmVoiceLibrary();
            break;

        case "fm_voice_loaded":
            if (msg.data) {
                updateFmVoiceUI(msg.data);
            }
            break;

        case "vector_library_updated":
            state.vectorPresets = msg.data.presets || [];
            renderVectorPresetLibrary();
            break;

        case "vector_preset_loaded":
            if (msg.data) {
                updateVectorPresetUI(msg.data);
            }
            break;

        case "model_changed":
            if (msg.data && msg.data.model) {
                applyActiveModel(msg.data.model, false);
            }
            break;

        case "notification":
            showToast(msg.data.message);
            break;

        case "error":
            showToast(msg.data.message, true);
            break;
    }
}

// --- DOM Rendering & Device Selectors ---

function renderFormatBadge(format, isGmPassthrough) {
    const badgeContainer = document.getElementById("formatBadgeContainer");
    if (!badgeContainer) return;

    const is680 = format === "pss680" || (format === "pss" && state.activeModel === "pss680") || (format !== "gm" && state.activeModel === "pss680" && format !== "rec");
    const pssName = is680 ? "PSS-680" : "PSS-790";

    if (format === "rec") {
        const recModel = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-rose-950 text-rose-400 border border-rose-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-rose-500/20" title="Yamaha ${recModel} Studio Recording. Direct native hardware passthrough active."><span>⏺</span><span>${recModel} Recording</span></span>`;
    } else if (format === "pss790" || format === "pss680" || format === "pss") {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-emerald-950 text-emerald-400 border border-emerald-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-emerald-500/20" title="Converted for Yamaha ${pssName}. Direct passthrough active."><span>⚡</span><span>${pssName} Ready</span></span>`;
    } else if (isGmPassthrough) {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-blue-950 text-blue-400 border border-blue-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-blue-500/20" title="Playing original unmodified General MIDI file directly through Microsoft GS Wavetable Synth for Windows reference."><span>🎹</span><span>Windows GS Synth (Original GM Passthrough)</span></span>`;
    } else {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-indigo-950 text-indigo-400 border border-indigo-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-indigo-500/20" title="General MIDI file. Real-time auto-remapping to ${pssName} active."><span>🎵</span><span>GM (Auto-Remapped)</span></span>`;
    }
}

function renderDeviceSelect(preferredDevice) {
    const sel = document.getElementById("deviceSelect");
    const dot = document.getElementById("outputStatusDot");
    if (!sel) return;

    const currentVal = preferredDevice || sel.value || state.selectedDevice;
    sel.innerHTML = "";

    if (!state.devices || state.devices.length === 0) {
        sel.innerHTML = `<option value="" disabled selected>No MIDI Ports Detected</option>`;
        if (dot) dot.className = "w-2 h-2 rounded-full bg-slate-600 shrink-0";
        state.selectedDevice = null;
        return;
    }

    if (dot) dot.className = "w-2 h-2 rounded-full bg-emerald-500 animate-pulse-slow shrink-0";

    let matchFound = false;
    state.devices.forEach((dev, idx) => {
        const opt = document.createElement("option");
        opt.value = dev;
        opt.textContent = dev;
        opt.className = "bg-slate-900 text-white";
        if (dev === currentVal) {
            opt.selected = true;
            matchFound = true;
            state.selectedDevice = dev;
        }
        sel.appendChild(opt);
    });

    if (!matchFound && state.devices.length > 0) {
        sel.options[0].selected = true;
        state.selectedDevice = state.devices[0];
    }
}

function renderInputDeviceSelect(preferredDevice) {
    const sel = document.getElementById("recorderInputDeviceSelect");
    const dot = document.getElementById("inputStatusDot");
    if (!sel) return;

    const currentVal = preferredDevice || sel.value || state.selectedInputDevice;
    sel.innerHTML = "";

    if (!state.inputDevices || state.inputDevices.length === 0) {
        sel.innerHTML = `<option value="" disabled selected>No Input Ports Detected</option>`;
        if (dot) dot.className = "w-2.5 h-2.5 rounded-full bg-slate-600";
        state.selectedInputDevice = null;
        return;
    }

    if (dot) dot.className = "w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse";

    let matchFound = false;
    state.inputDevices.forEach((dev, idx) => {
        const opt = document.createElement("option");
        opt.value = dev;
        opt.textContent = dev;
        opt.className = "bg-slate-900 text-white";
        if (dev === currentVal) {
            opt.selected = true;
            matchFound = true;
            state.selectedInputDevice = dev;
        }
        sel.appendChild(opt);
    });

    if (!matchFound && state.inputDevices.length > 0) {
        sel.options[0].selected = true;
        state.selectedInputDevice = state.inputDevices[0];
    }
}

function renderLibrarySelect() {
    const sel = document.getElementById("librarySelect");
    sel.innerHTML = "";

    const lib = state.library;
    const hasGm = lib.gmFiles && lib.gmFiles.length > 0;
    const hasPss = lib.pssFiles && lib.pssFiles.length > 0;
    const is680 = state.activeModel === "pss680";
    const pssGroupLabel = is680 ? "⚡ PSS-680 Midi (Converted - Ready)" : "⚡ PSS-790 Midi (Converted - Ready)";

    if (!hasGm && !hasPss) {
        sel.innerHTML = `<option value="" selected disabled>Library Empty (Click 'Open Folder')</option>`;
        return;
    }

    sel.innerHTML = `<option value="" selected disabled>Select from Library...</option>`;

    if (hasGm) {
        const gmGroup = document.createElement("optgroup");
        gmGroup.label = "🎵 GM Midi (Needs Conversion)";
        gmGroup.className = "bg-slate-900 text-indigo-400 font-bold";
        lib.gmFiles.forEach(f => {
            const opt = document.createElement("option");
            opt.value = f.fullPath;
            opt.textContent = `🎵 ${f.fileName}`;
            opt.className = "bg-slate-900 text-slate-200 font-normal";
            gmGroup.appendChild(opt);
        });
        sel.appendChild(gmGroup);
    }

    if (hasPss) {
        const pssGroup = document.createElement("optgroup");
        pssGroup.label = pssGroupLabel;
        pssGroup.className = "bg-slate-900 text-emerald-400 font-bold";
        lib.pssFiles.forEach(f => {
            const opt = document.createElement("option");
            opt.value = f.fullPath;
            opt.textContent = `⚡ ${f.fileName}`;
            opt.className = "bg-slate-900 text-slate-200 font-normal";
            pssGroup.appendChild(opt);
        });
        sel.appendChild(pssGroup);
    }
}

function escapeHtml(str) {
    if (!str) return "";
    return String(str)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

function initSidebarState() {
    try {
        const saved = localStorage.getItem("pss790_sidebar_collapsed");
        if (saved !== null) {
            state.isSidebarCollapsed = saved === "true";
        }

        const savedExpanded = localStorage.getItem("pss790_expanded_folders");
        if (savedExpanded) {
            const arr = JSON.parse(savedExpanded);
            state.expandedFolders = new Set(arr);
        } else {
            // By default on initial run, expand root songs so library is immediately browseable,
            // but keep custom subfolders collapsed for clean organization
            state.expandedFolders = new Set(["gm:__root__", "pss790:__root__"]);
        }

        const savedWidth = localStorage.getItem("pss790_sidebar_width");
        if (savedWidth) {
            const w = parseInt(savedWidth, 10);
            if (!isNaN(w) && w >= 260 && w <= 720) {
                state.sidebarWidth = w;
                const sidebar = document.getElementById("quickPickSidebar");
                if (sidebar) sidebar.style.width = `${w}px`;
            }
        }

        const savedAutoFit = localStorage.getItem("pss790_sidebar_autofit");
        if (savedAutoFit !== null) {
            state.isAutoFitSidebar = savedAutoFit === "true";
        }
    } catch (e) {
        state.expandedFolders = new Set(["gm:__root__", "pss790:__root__"]);
    }

    updateSidebarVisibility();
    updateCollapseAllButton();
}

function autoFitSidebarWidth(force = false) {
    if (state.isSidebarCollapsed) return;
    if (!force && !state.isAutoFitSidebar) return;
    const sidebar = document.getElementById("quickPickSidebar");
    if (!sidebar) return;

    const baseMin = 280;
    const maxAllowed = Math.min(760, Math.floor(window.innerWidth * 0.55));

    // If no songs are rendered/visible or library empty, smoothly shrink to baseMin
    if (!state.visibleSongList || state.visibleSongList.length === 0) {
        sidebar.style.width = `${baseMin}px`;
        state.sidebarWidth = baseMin;
        return;
    }

    // Measure the longest visible title and folder names with an offscreen canvas
    const canvas = document.createElement("canvas");
    const ctx = canvas.getContext("2d");
    ctx.font = "600 11px Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";

    let maxTextWidth = 100;

    for (const file of state.visibleSongList) {
        const w = ctx.measureText(file.fileName || "").width;
        if (w > maxTextWidth) maxTextWidth = w;
    }

    // Also check visible folder group labels
    document.querySelectorAll(".tree-folder-group > div:first-child span.font-medium").forEach(el => {
        const text = el.textContent || "";
        const w = ctx.measureText(text).width;
        if (w > maxTextWidth) maxTextWidth = w;
    });

    let maxFolderDepth = 0;
    for (const file of state.visibleSongList) {
        if (file.subFolder) {
            const depth = (file.subFolder.split(/[\\\/]/).filter(Boolean).length) || 1;
            if (depth > maxFolderDepth) maxFolderDepth = depth;
        }
    }
    const folderIndentAllowance = Math.min(maxFolderDepth * 16, 64);
    const requiredTotalWidth = Math.ceil(maxTextWidth + 215 + folderIndentAllowance);

    // Bidirectional clamping:
    // Automatically expands when long titles are visible, and automatically shrinks down to baseline (280px) when collapsed!
    const targetWidth = Math.max(baseMin, Math.min(requiredTotalWidth, maxAllowed));

    sidebar.style.width = `${targetWidth}px`;
    state.sidebarWidth = targetWidth;
}

function formatSongTitleHtml(fileName, subFolder) {
    if (!fileName) return "";
    return `<span class="song-title truncate font-medium">${escapeHtml(fileName)}</span>`;
}

function saveExpandedFolders() {
    try {
        localStorage.setItem("pss790_expanded_folders", JSON.stringify(Array.from(state.expandedFolders)));
    } catch (e) {}
    updateCollapseAllButton();
}

function updateCollapseAllButton() {
    const btn = document.getElementById("btnCollapseAll");
    if (!btn) return;
    const hasExpanded = state.expandedFolders.size > 0;
    btn.textContent = hasExpanded ? "⊟" : "⊞";
    btn.title = hasExpanded ? "Collapse All Folders" : "Expand All Folders";
}

function toggleSidebar() {
    state.isSidebarCollapsed = !state.isSidebarCollapsed;
    try {
        localStorage.setItem("pss790_sidebar_collapsed", state.isSidebarCollapsed);
    } catch (e) {}
    updateSidebarVisibility();
}

function updateSidebarVisibility() {
    const sidebar = document.getElementById("quickPickSidebar");
    if (sidebar) {
        sidebar.classList.toggle("sidebar-collapsed", state.isSidebarCollapsed);
    }
    const btnToggle = document.getElementById("btnToggleSidebar");
    if (btnToggle) {
        if (!state.isSidebarCollapsed) {
            btnToggle.classList.add("bg-cyan-500/20", "text-cyan-400", "border-cyan-500/40");
        } else {
            btnToggle.classList.remove("bg-cyan-500/20", "text-cyan-400", "border-cyan-500/40");
        }
    }
}

function toggleFolder(folderKey) {
    if (folderKey && folderKey.includes("__fav_")) {
        if (!state.collapsedFavorites) state.collapsedFavorites = new Set();
        if (state.collapsedFavorites.has(folderKey)) {
            state.collapsedFavorites.delete(folderKey);
        } else {
            state.collapsedFavorites.add(folderKey);
        }
        renderLibraryTree();
        return;
    }
    if (state.expandedFolders.has(folderKey)) {
        state.expandedFolders.delete(folderKey);
    } else {
        state.expandedFolders.add(folderKey);
    }
    saveExpandedFolders();
    renderLibraryTree();
}

function toggleCollapseAllFolders() {
    if (state.expandedFolders.size > 0) {
        state.expandedFolders.clear();
    } else if (state.library) {
        const addAllKeys = (node, catKey) => {
            state.expandedFolders.add(`${catKey}:${node.relativePath || node.name}`);
            if (node.subFolders && node.subFolders.length > 0) {
                node.subFolders.forEach(sub => addAllKeys(sub, catKey));
            }
        };

        const categories = [
            { tree: state.library.pssTree, key: "pss790" },
            { tree: state.library.gmTree, key: "gm" }
        ];

        categories.forEach(({ tree, key }) => {
            if (tree) {
                state.expandedFolders.add(`${key}:__root__`);
                if (tree.subFolders) {
                    tree.subFolders.forEach(f => addAllKeys(f, key));
                }
            }
        });
    }
    saveExpandedFolders();
    renderLibraryTree();
}

function updateBatchBar() {
    const bar = document.getElementById("sidebarBatchBar");
    const badge = document.getElementById("batchCountBadge");
    const btnToggle = document.getElementById("btnToggleSelectMode");
    const count = state.selectedSongs.size;

    if (badge) badge.textContent = `${count} selected`;

    if (btnToggle) {
        if (state.isSelectMode || count > 0) {
            btnToggle.classList.add("bg-cyan-500/20", "text-cyan-400", "border", "border-cyan-500/40");
        } else {
            btnToggle.classList.remove("bg-cyan-500/20", "text-cyan-400", "border", "border-cyan-500/40");
        }
    }

    if (bar) {
        if (state.isSelectMode || count > 0) {
            bar.classList.remove("hidden");
        } else {
            bar.classList.add("hidden");
        }
    }
}

function toggleSelectMode() {
    state.isSelectMode = !state.isSelectMode;
    if (!state.isSelectMode) {
        state.selectedSongs.clear();
        state.lastCheckedSongPath = null;
    }
    updateBatchBar();
    renderLibraryTree();
}

function clearSelection() {
    state.selectedSongs.clear();
    state.lastCheckedSongPath = null;
    state.isSelectMode = false;
    updateBatchBar();
    renderLibraryTree();
}

function selectAllVisible() {
    if (state.selectedSongs.size === state.visibleSongList.length && state.visibleSongList.length > 0) {
        state.selectedSongs.clear();
    } else {
        state.visibleSongList.forEach(file => state.selectedSongs.add(file.fullPath));
    }
    state.isSelectMode = state.selectedSongs.size > 0;
    updateBatchBar();
    renderLibraryTree();
}

function attachDropTarget(targetEl, targetCategory, targetSubFolder, targetFullPath) {
    targetEl.addEventListener("dragover", (e) => {
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = "move";
        targetEl.classList.add("drag-target-active", "ring-2", "ring-cyan-400", "bg-cyan-950/60");
    });

    targetEl.addEventListener("dragleave", (e) => {
        e.stopPropagation();
        targetEl.classList.remove("drag-target-active", "ring-2", "ring-cyan-400", "bg-cyan-950/60");
    });

    targetEl.addEventListener("drop", (e) => {
        e.preventDefault();
        e.stopPropagation();
        targetEl.classList.remove("drag-target-active", "ring-2", "ring-cyan-400", "bg-cyan-950/60");

        try {
            const raw = e.dataTransfer.getData("application/json");
            if (!raw) return;
            const data = JSON.parse(raw);

            if (data.type === "songs" && Array.isArray(data.paths) && data.paths.length > 0) {
                sendToHost("batch_move_files", {
                    sourcePaths: data.paths,
                    targetCategory: targetCategory,
                    targetSubFolder: targetSubFolder,
                    alsoMoveLinked: true
                });
                clearSelection();
            } else if (data.type === "folder" && data.fullPath) {
                // Prevent dropping folder onto itself or into its own subfolder
                if (data.fullPath.toLowerCase() === targetFullPath.toLowerCase() ||
                    targetFullPath.toLowerCase().startsWith(data.fullPath.toLowerCase() + "\\")) {
                    showToast("Cannot move a folder into itself.", true);
                    return;
                }
                sendToHost("move_folder", {
                    sourceFolderPath: data.fullPath,
                    targetCategory: targetCategory,
                    targetParentSubFolder: targetSubFolder
                });
            }
        } catch (err) {
            console.error("Drop error:", err);
        }
    });
}

function toggleCategory(catKey) {
    if (state.collapsedCategories.has(catKey)) {
        state.collapsedCategories.delete(catKey);
    } else {
        state.collapsedCategories.add(catKey);
    }
    renderLibraryTree();
}

function clearSearchFilter() {
    state.searchQuery = "";
    const input = document.getElementById("sidebarSearchInput");
    const btnClear = document.getElementById("btnClearSearch");
    if (input) input.value = "";
    if (btnClear) btnClear.classList.add("hidden");
    renderLibraryTree();
}

let activeContextTarget = null;
let activeModalAction = null;

function showContextMenu(e, target) {
    e.preventDefault();
    e.stopPropagation();

    activeContextTarget = target;
    const menu = document.getElementById("libraryContextMenu");
    if (!menu) return;

    const itemNewFolder = document.getElementById("ctxItemNewFolder");
    const itemMove = document.getElementById("ctxItemMove");
    const itemRename = document.getElementById("ctxItemRename");
    const itemReveal = document.getElementById("ctxItemReveal");
    const itemFavorite = document.getElementById("ctxItemFavorite");
    const itemFavoriteText = document.getElementById("ctxItemFavoriteText");
    const separator = document.getElementById("ctxSeparator");
    const itemDelete = document.getElementById("ctxItemDelete");

    if (target.type === "song") {
        itemNewFolder.classList.add("hidden");
        itemMove.classList.remove("hidden");
        itemRename.classList.remove("hidden");
        itemReveal.classList.remove("hidden");
        if (itemFavorite) {
            itemFavorite.classList.remove("hidden");
            const isFav = isSongFavorite(target.fullPath || target.name);
            if (itemFavoriteText) {
                itemFavoriteText.textContent = isFav ? "Remove from Favourites" : "Add to Favourites";
            }
        }
        separator.classList.remove("hidden");
        itemDelete.classList.remove("hidden");
    } else if (target.type === "folder") {
        if (itemFavorite) itemFavorite.classList.add("hidden");
        itemNewFolder.classList.remove("hidden");
        itemMove.classList.add("hidden");
        itemRename.classList.remove("hidden");
        itemReveal.classList.remove("hidden");
        separator.classList.remove("hidden");
        itemDelete.classList.remove("hidden");
    } else if (target.type === "category" || target.type === "empty") {
        if (itemFavorite) itemFavorite.classList.add("hidden");
        itemNewFolder.classList.remove("hidden");
        itemMove.classList.add("hidden");
        itemRename.classList.add("hidden");
        itemReveal.classList.remove("hidden");
        separator.classList.add("hidden");
        itemDelete.classList.add("hidden");
    }

    menu.classList.remove("hidden");
    const menuWidth = 195;
    const menuHeight = menu.offsetHeight || 165;

    let posX = e.clientX;
    let posY = e.clientY;

    if (posX + menuWidth > window.innerWidth) {
        posX = Math.max(10, window.innerWidth - menuWidth - 10);
    }
    if (posY + menuHeight > window.innerHeight) {
        posY = Math.max(10, window.innerHeight - menuHeight - 10);
    }

    menu.style.left = `${posX}px`;
    menu.style.top = `${posY}px`;
}

function hideContextMenu() {
    const menu = document.getElementById("libraryContextMenu");
    if (menu) menu.classList.add("hidden");
}

function openActionModal(actionType, target) {
    activeModalAction = { actionType, target };

    const modal = document.getElementById("fileActionModal");
    const icon = document.getElementById("actionModalIcon");
    const title = document.getElementById("actionModalTitle");
    const desc = document.getElementById("actionModalDesc");
    const inputContainer = document.getElementById("actionModalInputContainer");
    const inputLabel = document.getElementById("actionModalInputLabel");
    const input = document.getElementById("actionModalInput");
    const moveContainer = document.getElementById("actionModalMoveContainer");
    const linkedContainer = document.getElementById("actionModalLinkedDeleteContainer");
    const chkLinked = document.getElementById("chkAlsoDeleteLinked");
    const btnConfirm = document.getElementById("btnConfirmActionModal");

    if (!modal) return;

    inputContainer.classList.remove("hidden");
    moveContainer.classList.add("hidden");
    linkedContainer.classList.add("hidden");
    if (chkLinked) chkLinked.checked = false;

    btnConfirm.className = "px-5 py-2 rounded-xl bg-gradient-to-r from-cyan-600 to-cyan-500 hover:from-cyan-500 hover:to-cyan-400 text-xs font-semibold text-white shadow-lg shadow-cyan-500/20 transition";
    btnConfirm.textContent = "Confirm";

    if (actionType === "new_folder") {
        icon.textContent = "📁";
        title.textContent = "Create New Folder";
        const isPss = target.category === "pss790" || target.category === "pss680";
        const catLabel = isPss ? (state.activeModel === "pss680" ? "PSS-680 Midi" : "PSS-790 Midi") : "GM Midi";
        desc.textContent = target.type === "folder"
            ? `Create a new subfolder inside "${target.name}":`
            : `Create a new folder in ${catLabel}:`;
        inputLabel.textContent = "Folder Name:";
        input.value = "";
        input.placeholder = "e.g. Doom, Monkey Island, Soundtracks";
        modal.classList.remove("hidden");
        setTimeout(() => input.focus(), 50);
    } else if (actionType === "rename") {
        icon.textContent = "✏️";
        const isFolder = target.type === "folder";
        title.textContent = isFolder ? "Rename Folder" : "Rename Song";
        desc.textContent = isFolder
            ? `Enter a new name for folder "${target.name}":`
            : `Enter a new song title for "${target.name}":`;
        inputLabel.textContent = isFolder ? "Folder Name:" : "Song Title:";

        let initialVal = target.name || "";
        if (!isFolder) {
            initialVal = initialVal.replace(/\.(midi?|MID|MIDI)$/i, "");
        }
        input.value = initialVal;
        input.placeholder = "New name";
        modal.classList.remove("hidden");
        setTimeout(() => {
            input.focus();
            input.select();
        }, 50);
    } else if (actionType === "move") {
        icon.textContent = "📦";
        title.textContent = "Move Song to Folder";
        desc.textContent = `Select destination folder for "${target.name}":`;
        inputContainer.classList.add("hidden");
        moveContainer.classList.remove("hidden");
        populateMoveFolderSelect(target.category, target.subFolder);

        const linkedMoveContainer = document.getElementById("actionModalLinkedMoveContainer");
        if (linkedMoveContainer) linkedMoveContainer.classList.add("hidden");

        modal.classList.remove("hidden");

        // Check for matching counterpart file across mirrored library
        sendToHost("check_linked_file", { filePath: target.fullPath, isFolder: false });
    } else if (actionType === "batch_move") {
        icon.textContent = "📦";
        title.textContent = `Move ${target.paths.length} Songs to Folder`;
        desc.textContent = `Select destination folder for ${target.paths.length} selected songs:`;
        inputContainer.classList.add("hidden");
        moveContainer.classList.remove("hidden");

        let batchCategory = "gm";
        if (target.paths && target.paths.length > 0) {
            const firstP = target.paths[0].toLowerCase();
            if (firstP.includes("pss-680 midi")) batchCategory = "pss680";
            else if (firstP.includes("pss-790 midi")) batchCategory = "pss790";
        }
        target.category = batchCategory;
        populateMoveFolderSelect(batchCategory, "");

        const linkedMoveContainer = document.getElementById("actionModalLinkedMoveContainer");
        if (linkedMoveContainer) linkedMoveContainer.classList.add("hidden");

        modal.classList.remove("hidden");

        sendToHost("check_linked_file", { filePaths: target.paths, isFolder: false });
    } else if (actionType === "delete") {
        icon.textContent = "🗑️";
        const isFolder = target.type === "folder";
        title.textContent = isFolder ? "Delete Folder" : "Delete Song";
        desc.textContent = isFolder
            ? `Are you sure you want to permanently delete the folder "${target.name}" and all files inside it?`
            : `Are you sure you want to permanently delete "${target.name}"?`;
        inputContainer.classList.add("hidden");

        const deleteLabel = document.querySelector("#actionModalLinkedDeleteContainer span");
        if (deleteLabel) {
            deleteLabel.textContent = isFolder ? "Also delete mirrored folder in counterpart library" : `Also delete matching ${state.activeModel === "pss680" ? "PSS-680" : "PSS-790"} version`;
        }

        btnConfirm.className = "px-5 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 active:bg-rose-700 text-xs font-bold text-white shadow-lg shadow-rose-600/30 transition";
        btnConfirm.textContent = "Delete Forever";

        const delContainer = document.getElementById("actionModalLinkedDeleteContainer");
        if (delContainer) delContainer.classList.add("hidden");

        modal.classList.remove("hidden");

        sendToHost("check_linked_file", { filePath: target.fullPath, isFolder: isFolder });
    } else if (actionType === "batch_delete") {
        icon.textContent = "🗑️";
        title.textContent = `Delete ${target.paths.length} Songs`;
        desc.textContent = `Are you sure you want to permanently delete these ${target.paths.length} selected songs?`;
        inputContainer.classList.add("hidden");

        const deleteLabel = document.querySelector("#actionModalLinkedDeleteContainer span");
        if (deleteLabel) {
            deleteLabel.textContent = `Also delete matching ${state.activeModel === "pss680" ? "PSS-680" : "PSS-790"} versions in mirrored library`;
        }

        btnConfirm.className = "px-5 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 active:bg-rose-700 text-xs font-bold text-white shadow-lg shadow-rose-600/30 transition";
        btnConfirm.textContent = `Delete All ${target.paths.length} Forever`;

        const delContainer = document.getElementById("actionModalLinkedDeleteContainer");
        if (delContainer) delContainer.classList.add("hidden");

        modal.classList.remove("hidden");

        sendToHost("check_linked_file", { filePaths: target.paths, isFolder: false });
    }
}

function populateMoveFolderSelect(category, currentSubFolder) {
    const sel = document.getElementById("actionModalFolderSelect");
    if (!sel) return;
    sel.innerHTML = "";

    const isPss = category === "pss790" || category === "pss680";
    const tree = isPss ? (state.activeModel === "pss680" ? (state.library.pss680Tree || state.library.pssTree) : (state.library.pss790Tree || state.library.pssTree)) : state.library.gmTree;
    const catLabel = isPss ? (state.activeModel === "pss680" ? "PSS-680 Midi" : "PSS-790 Midi") : "GM Midi";

    const rootOpt = document.createElement("option");
    rootOpt.value = "";
    rootOpt.textContent = `📁 (Root) - ${catLabel}`;
    if (!currentSubFolder) rootOpt.selected = true;
    sel.appendChild(rootOpt);

    const folderList = (tree && (tree.allFolders || tree.folders)) || [];
    folderList.forEach(f => {
        const opt = document.createElement("option");
        opt.value = f.name;
        opt.textContent = `📁 ${f.name}`;
        if (currentSubFolder && currentSubFolder.toLowerCase() === f.name.toLowerCase()) {
            opt.selected = true;
        }
        sel.appendChild(opt);
    });
}

function handleLinkedFileChecked(data) {
    if (!activeModalAction || !activeModalAction.target) return;
    const target = activeModalAction.target;
    const modelTag = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";

    if (data.isBatch) {
        if (activeModalAction.actionType === "batch_delete" && data.hasLinked) {
            const container = document.getElementById("actionModalLinkedDeleteContainer");
            const label = document.getElementById("actionModalLinkedPathLabel");
            const chk = document.getElementById("chkAlsoDeleteLinked");

            if (container && label && chk) {
                label.textContent = `${data.linkedCount} matching ${modelTag} version(s) found in mirrored library`;
                chk.checked = true;
                container.classList.remove("hidden");
            }
        } else if (activeModalAction.actionType === "batch_move" && data.hasLinked) {
            const container = document.getElementById("actionModalLinkedMoveContainer");
            const label = document.getElementById("actionModalLinkedMovePathLabel");
            const chk = document.getElementById("chkAlsoMoveLinked");

            if (container && label && chk) {
                label.textContent = `${data.linkedCount} matching ${modelTag} version(s) found in mirrored library`;
                chk.checked = true;
                container.classList.remove("hidden");
            }
        }
        return;
    }

    if (activeModalAction.actionType === "delete" && target.fullPath === data.filePath && data.hasLinked) {
        const container = document.getElementById("actionModalLinkedDeleteContainer");
        const label = document.getElementById("actionModalLinkedPathLabel");
        const chk = document.getElementById("chkAlsoDeleteLinked");

        if (container && label && chk) {
            target.linkedPath = data.linkedPath;
            label.textContent = data.linkedName || data.linkedPath;
            chk.checked = true;
            container.classList.remove("hidden");
        }
    } else if (activeModalAction.actionType === "move" && target.fullPath === data.filePath && data.hasLinked) {
        const container = document.getElementById("actionModalLinkedMoveContainer");
        const label = document.getElementById("actionModalLinkedMovePathLabel");
        const chk = document.getElementById("chkAlsoMoveLinked");

        if (container && label && chk) {
            target.linkedPath = data.linkedPath;
            label.textContent = data.linkedName || data.linkedPath;
            chk.checked = true;
            container.classList.remove("hidden");
        }
    }
}

function openAboutModal() {
    const modal = document.getElementById("aboutModal");
    if (modal) modal.classList.remove("hidden");
}

function closeAboutModal() {
    const modal = document.getElementById("aboutModal");
    if (modal) modal.classList.add("hidden");
}

function openShortcutsModal() {
    const modal = document.getElementById("shortcutsModal");
    if (modal) modal.classList.remove("hidden");
}

function closeShortcutsModal() {
    const modal = document.getElementById("shortcutsModal");
    if (modal) modal.classList.add("hidden");
}

function closeActionModal() {
    const modal = document.getElementById("fileActionModal");
    if (modal) modal.classList.add("hidden");
    activeModalAction = null;
}

function confirmActionModal() {
    if (!activeModalAction) return;

    const { actionType, target } = activeModalAction;
    const input = document.getElementById("actionModalInput");
    const folderSelect = document.getElementById("actionModalFolderSelect");
    const chkLinked = document.getElementById("chkAlsoDeleteLinked");
    const chkMoveLinked = document.getElementById("chkAlsoMoveLinked");

    if (actionType === "new_folder") {
        const val = input.value.trim();
        if (!val) {
            showToast("Please enter a folder name.", true);
            return;
        }
        const parentSub = target.type === "folder" ? (target.folderName || "") : "";
        sendToHost("create_folder", {
            category: target.category || "gm",
            parentSubFolder: parentSub,
            folderName: val
        });
    } else if (actionType === "rename") {
        const val = input.value.trim();
        if (!val) {
            showToast("Please enter a name.", true);
            return;
        }
        sendToHost("rename_item", {
            fullPath: target.fullPath,
            newName: val,
            isFolder: target.type === "folder"
        });
    } else if (actionType === "move") {
        const selectedSubFolder = folderSelect ? folderSelect.value : "";
        const alsoMove = chkMoveLinked && chkMoveLinked.checked && !!target.linkedPath;
        sendToHost("move_file", {
            sourcePath: target.fullPath,
            targetCategory: target.category || "gm",
            targetSubFolder: selectedSubFolder,
            alsoMoveLinked: alsoMove,
            linkedPath: target.linkedPath || null
        });
    } else if (actionType === "batch_move") {
        const selectedSubFolder = folderSelect ? folderSelect.value : "";
        const alsoMove = chkMoveLinked && chkMoveLinked.checked;
        sendToHost("batch_move_files", {
            sourcePaths: target.paths,
            targetCategory: target.category || "gm",
            targetSubFolder: selectedSubFolder,
            alsoMoveLinked: alsoMove
        });
        clearSelection();
    } else if (actionType === "delete") {
        const alsoDelete = chkLinked && chkLinked.checked && !!target.linkedPath;
        sendToHost("delete_item", {
            fullPath: target.fullPath,
            isFolder: target.type === "folder",
            alsoDeleteLinked: alsoDelete,
            linkedPath: target.linkedPath || null
        });
    } else if (actionType === "batch_delete") {
        const alsoDelete = chkLinked && chkLinked.checked;
        sendToHost("batch_delete_files", {
            filePaths: target.paths,
            alsoDeleteLinked: alsoDelete
        });
        clearSelection();
    }

    closeActionModal();
}

function filterFolderNode(folder, query) {
    const nameMatches = folder.name.toLowerCase().includes(query);

    let matchingFiles = [];
    if (folder.files) {
        matchingFiles = folder.files.filter(f => nameMatches || f.fileName.toLowerCase().includes(query));
    }

    let matchingSubFolders = [];
    if (folder.subFolders) {
        folder.subFolders.forEach(sub => {
            const filteredSub = filterFolderNode(sub, query);
            if (filteredSub) {
                matchingSubFolders.push(filteredSub);
            }
        });
    }

    if (nameMatches || matchingFiles.length > 0 || matchingSubFolders.length > 0) {
        const totalMatching = matchingFiles.length + matchingSubFolders.reduce((acc, s) => acc + s.totalCount, 0);
        return {
            name: folder.name,
            fullPath: folder.fullPath,
            relativePath: folder.relativePath,
            files: matchingFiles,
            subFolders: matchingSubFolders,
            directCount: matchingFiles.length,
            totalCount: totalMatching
        };
    }
    return null;
}

function renderFolderNode(folder, depth, catKey, query) {
    const isFavFolder = !!folder.isVirtualFavorites;
    const folderKey = `${catKey}:${folder.relativePath || folder.name}`;
    const isFolderExpanded = isFavFolder
        ? (query.length > 0 || !state.collapsedFavorites || !state.collapsedFavorites.has(folderKey))
        : (query.length > 0 || state.expandedFolders.has(folderKey));

    const folderGroup = document.createElement("div");
    folderGroup.className = isFavFolder ? "tree-folder-group space-y-0.5 mb-1" : "tree-folder-group space-y-0.5";

    const padPx = depth * 12;

    const folderHeader = document.createElement("div");
    if (isFavFolder) {
        folderHeader.className = "flex items-center justify-between px-2 py-1 rounded-md text-amber-300 hover:text-amber-200 bg-amber-950/25 hover:bg-amber-950/45 border border-amber-800/40 cursor-pointer transition select-none text-[11px] shadow-sm";
    } else {
        folderHeader.className = "flex items-center justify-between px-2 py-1 rounded-md text-slate-400 hover:text-white hover:bg-slate-800/60 cursor-pointer transition select-none text-[11px]";
    }
    if (padPx > 0) {
        folderHeader.style.paddingLeft = `${padPx + 8}px`;
    }

    const count = folder.totalCount !== undefined ? folder.totalCount : (folder.files ? folder.files.length : 0);
    if (count === 0 && !query) {
        return document.createDocumentFragment();
    }

    if (isFavFolder) {
        folderHeader.innerHTML = `
            <div class="flex items-center space-x-1.5 truncate">
                <span class="text-[8px] text-amber-400 transform transition-transform">${isFolderExpanded ? '▼' : '▶'}</span>
                <span class="text-amber-400 text-xs">⭐</span>
                <span class="font-bold truncate text-amber-300">Favourites</span>
            </div>
            <span class="text-[9px] font-mono text-amber-400/90 font-bold ml-1">(${count})</span>
        `;
        folderHeader.title = `Favourites (${count} song${count === 1 ? '' : 's'})\nPinned for quick access • Click to expand/collapse`;
    } else {
        folderHeader.innerHTML = `
            <div class="flex items-center space-x-1.5 truncate">
                <span class="text-[8px] text-slate-500 transform transition-transform">${isFolderExpanded ? '▼' : '▶'}</span>
                <span class="text-amber-400 text-xs">📁</span>
                <span class="font-medium truncate text-slate-300">${escapeHtml(folder.name)}</span>
            </div>
            <span class="text-[9px] font-mono text-slate-500 ml-1">(${count})</span>
        `;
        folderHeader.title = `Folder: ${folder.name} (${count} song${count === 1 ? '' : 's'})\nClick to expand/collapse • Right-click for folder actions • Drag to move folder`;
    }

    if (!isFavFolder) {
        folderHeader.draggable = true;
        folderHeader.addEventListener("dragstart", (e) => {
            e.stopPropagation();
            e.dataTransfer.setData("application/json", JSON.stringify({
                type: "folder",
                fullPath: folder.fullPath,
                name: folder.name,
                category: catKey,
                relativePath: folder.relativePath || folder.name
            }));
            e.dataTransfer.effectAllowed = "move";
            folderHeader.classList.add("opacity-50");
        });
        folderHeader.addEventListener("dragend", () => {
            folderHeader.classList.remove("opacity-50");
            document.querySelectorAll(".drag-target-active").forEach(el => {
                el.classList.remove("drag-target-active", "ring-2", "ring-cyan-400", "bg-cyan-950/60");
            });
        });

        attachDropTarget(folderHeader, catKey, folder.relativePath || folder.name, folder.fullPath);
    }

    folderHeader.addEventListener("click", () => toggleFolder(folderKey));
    if (!isFavFolder) {
        folderHeader.addEventListener("contextmenu", (e) => {
            showContextMenu(e, {
                type: "folder",
                category: catKey,
                folderName: folder.relativePath || folder.name,
                fullPath: folder.fullPath,
                name: folder.name
            });
        });
    }
    folderGroup.appendChild(folderHeader);

    if (isFolderExpanded) {
        const folderContent = document.createElement("div");
        folderContent.className = "space-y-0.5 border-l border-slate-800/80 ml-2.5";

        // 1. Render child subfolders
        if (folder.subFolders && folder.subFolders.length > 0) {
            folder.subFolders.forEach(sub => {
                folderContent.appendChild(renderFolderNode(sub, depth + 1, catKey, query));
            });
        }

        // 2. Render direct files
        if (folder.files && folder.files.length > 0) {
            folder.files.forEach(file => {
                state.visibleSongList.push(file);
                const fileEl = createSongItemElement(file);
                if (padPx > 0) {
                    fileEl.style.paddingLeft = `${padPx + 10}px`;
                }
                folderContent.appendChild(fileEl);
            });
        }

        folderGroup.appendChild(folderContent);
    }

    return folderGroup;
}

function renderLibraryTree() {
    const container = document.getElementById("treeContainer");
    const countBadge = document.getElementById("sidebarSongCount");
    if (!container) return;

    state.visibleSongList = [];

    const lib = state.library;
    const flatFiles = lib.flatFiles || [];
    const totalFiles = flatFiles.length;

    if (countBadge) {
        countBadge.textContent = `${totalFiles} song${totalFiles === 1 ? '' : 's'}`;
    }

    if (!lib.hasFiles || totalFiles === 0) {
        container.innerHTML = `
            <div class="text-center py-8 px-3 text-slate-500 space-y-2">
                <div class="text-2xl">📂</div>
                <div class="text-xs font-semibold text-slate-400">Library Empty</div>
                <p class="text-[10px] text-slate-500">Put .mid files in Music\\Midi\\GM Midi, PSS-790 Midi, or PSS-680 Midi</p>
                <button id="btnTreeOpenFolder" class="mt-2 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs text-cyan-400 font-medium transition border border-slate-700">Open Music Folder</button>
            </div>
        `;
        const btn = document.getElementById("btnTreeOpenFolder");
        if (btn) btn.addEventListener("click", () => sendToHost("open_explorer"));
        return;
    }

    const query = (state.searchQuery || "").toLowerCase();
    container.innerHTML = "";

    // Categories to render: Native PSS Ready first, then GM Midi
    const is680 = state.activeModel === "pss680";
    const nativeTree = is680 ? (lib.pss680Tree || lib.pssTree) : (lib.pss790Tree || lib.pssTree);
    const nativeTitle = is680 ? "PSS-680 Ready" : "PSS-790 Ready";
    const nativeKey = is680 ? "pss680" : "pss790";

    const categories = [
        { tree: nativeTree, title: nativeTitle, badge: "⚡", key: nativeKey, colorClass: "text-emerald-400" },
        { tree: lib.gmTree, title: "GM (Auto-Remap)", badge: "🎵", key: "gm", colorClass: "text-indigo-400" }
    ];

    let totalRenderedSongs = 0;

    categories.forEach(cat => {
        const tree = cat.tree;
        if (!tree) return;

        let filteredSubFolders = [];
        let filteredRootFiles = [];

        const sourceSubFolders = tree.subFolders || [];
        const sourceRootFiles = tree.files || [];

        if (query) {
            sourceSubFolders.forEach(f => {
                const filtered = filterFolderNode(f, query);
                if (filtered) {
                    filteredSubFolders.push(filtered);
                }
            });
            filteredRootFiles = sourceRootFiles.filter(f => f.fileName.toLowerCase().includes(query));
        } else {
            filteredSubFolders = [...sourceSubFolders];
            filteredRootFiles = [...sourceRootFiles];
        }

        // Virtual Favourites Folder: Pin at top of category if any favorited songs exist
        function collectAllCategoryFiles(node) {
            let files = [];
            if (!node) return files;
            if (node.files && node.files.length > 0) files.push(...node.files);
            if (node.subFolders && node.subFolders.length > 0) {
                node.subFolders.forEach(sub => files.push(...collectAllCategoryFiles(sub)));
            }
            return files;
        }

        const allCatFiles = collectAllCategoryFiles(tree);
        let favFiles = allCatFiles.filter(f => isSongFavorite(f.fullPath || f.relativePath || f.fileName));
        if (query) {
            favFiles = favFiles.filter(f => f.fileName.toLowerCase().includes(query));
        }

        if (favFiles.length > 0) {
            const favFolder = {
                name: "Favourites",
                isVirtualFavorites: true,
                fullPath: `__fav_${cat.key}__`,
                relativePath: `__fav_${cat.key}__`,
                files: favFiles,
                subFolders: [],
                directCount: favFiles.length,
                totalCount: favFiles.length
            };
            filteredSubFolders.unshift(favFolder);
        }

        const catSongCount = filteredSubFolders.reduce((acc, f) => {
            if (f.isVirtualFavorites) return acc;
            return acc + (f.totalCount !== undefined ? f.totalCount : (f.files ? f.files.length : 0));
        }, 0) + filteredRootFiles.length;
        if (catSongCount === 0) return;

        totalRenderedSongs += catSongCount;

        // Category Group Container
        const catGroup = document.createElement("div");
        catGroup.className = "tree-category-group mb-2 select-none";

        const isCatCollapsed = state.collapsedCategories.has(cat.key) && !query;

        // Category Header
        const catHeader = document.createElement("div");
        catHeader.className = "flex items-center justify-between px-2 py-1.5 rounded-lg bg-slate-900/80 hover:bg-slate-800/80 cursor-pointer border border-slate-800/80 transition-all text-xs";
        catHeader.innerHTML = `
            <div class="flex items-center space-x-1.5 font-bold ${cat.colorClass}">
                <span class="text-[9px] text-slate-400 transform transition-transform">${isCatCollapsed ? '▶' : '▼'}</span>
                <span>${cat.badge} ${cat.title}</span>
            </div>
            <span class="text-[10px] font-mono text-slate-500 font-medium">(${catSongCount})</span>
        `;
        catHeader.title = `${cat.title} (${catSongCount} songs)\nClick to expand/collapse • Right-click for options • Drag songs here to move to library root`;
        catHeader.addEventListener("click", () => toggleCategory(cat.key));
        attachDropTarget(catHeader, cat.key, "", tree.fullPath);
        catHeader.addEventListener("contextmenu", (e) => {
            showContextMenu(e, {
                type: "category",
                category: cat.key,
                fullPath: tree.fullPath,
                name: cat.title
            });
        });
        catGroup.appendChild(catHeader);

        // Category Content (Folders + Root Files)
        if (!isCatCollapsed) {
            const catContent = document.createElement("div");
            catContent.className = "pl-1.5 pt-1 space-y-1";

            // Render Subfolders recursively
            filteredSubFolders.forEach(folder => {
                catContent.appendChild(renderFolderNode(folder, 0, cat.key, query));
            });

            // Render Category Root Files (grouped in collapsible folder)
            if (filteredRootFiles.length > 0) {
                const rootKey = `${cat.key}:__root__`;
                const isRootExpanded = query.length > 0 || state.expandedFolders.has(rootKey);

                const rootGroup = document.createElement("div");
                rootGroup.className = "tree-folder-group space-y-0.5 pt-0.5";

                const rootHeader = document.createElement("div");
                rootHeader.className = "flex items-center justify-between px-2 py-1 rounded-md text-slate-400 hover:text-white hover:bg-slate-800/60 cursor-pointer transition select-none text-[11px]";
                rootHeader.innerHTML = `
                    <div class="flex items-center space-x-1.5 truncate">
                        <span class="text-[8px] text-slate-500 transform transition-transform">${isRootExpanded ? '▼' : '▶'}</span>
                        <span class="text-amber-400 text-xs">📁</span>
                        <span class="font-medium truncate text-slate-300 italic">(Root Library Songs)</span>
                    </div>
                    <span class="text-[9px] font-mono text-slate-500 ml-1">(${filteredRootFiles.length})</span>
                `;
                rootHeader.title = `Root Library Songs (${filteredRootFiles.length} song${filteredRootFiles.length === 1 ? '' : 's'})\nClick to expand/collapse • Drag songs here to move to root`;
                rootHeader.addEventListener("click", () => toggleFolder(rootKey));
                attachDropTarget(rootHeader, cat.key, "", tree.fullPath);
                rootHeader.addEventListener("contextmenu", (e) => {
                    showContextMenu(e, {
                        type: "folder",
                        category: cat.key,
                        folderName: "",
                        fullPath: tree.fullPath,
                        name: "(Root Library)"
                    });
                });
                rootGroup.appendChild(rootHeader);

                if (isRootExpanded) {
                    const rootContent = document.createElement("div");
                    rootContent.className = "pl-3 space-y-0.5 border-l border-slate-800/80 ml-2.5";
                    filteredRootFiles.forEach(file => {
                        state.visibleSongList.push(file);
                        rootContent.appendChild(createSongItemElement(file));
                    });
                    rootGroup.appendChild(rootContent);
                }

                catContent.appendChild(rootGroup);
            }

            catGroup.appendChild(catContent);
        }

        container.appendChild(catGroup);
    });

    container.oncontextmenu = (e) => {
        if (e.target === container || e.target.id === "treeContainer") {
            showContextMenu(e, {
                type: "empty",
                category: "gm",
                fullPath: state.library.baseDir,
                name: "Library"
            });
        }
    };

    if (totalRenderedSongs === 0 && query) {
        container.innerHTML = `
            <div class="text-center py-8 px-3 text-slate-500 space-y-2">
                <div class="text-xl">🔍</div>
                <div class="text-xs font-semibold text-slate-400">No matching songs</div>
                <p class="text-[10px] text-slate-500">No songs match "${escapeHtml(query)}"</p>
                <button id="btnTreeClearSearch" class="mt-2 px-3 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs text-cyan-400 font-medium transition border border-slate-700">Clear Filter</button>
            </div>
        `;
        const btnClear = document.getElementById("btnTreeClearSearch");
        if (btnClear) btnClear.addEventListener("click", clearSearchFilter);
    }

    // Bidirectionally auto-fit sidebar width to visible file & folder content
    autoFitSidebarWidth();
}

function createSongItemElement(file) {
    const isSelected = file.fullPath === state.selectedSongPath;
    const isPlaying = file.fullPath === state.currentFilePath && state.isPlaying;
    const isRec = file.format === "rec";
    const is680 = file.format === "pss680" || (file.format !== "gm" && file.format !== "rec" && state.activeModel === "pss680");
    const isPss = file.format === "pss790" || is680;
    const isChecked = state.selectedSongs.has(file.fullPath);
    const isFav = isSongFavorite(file.fullPath || file.relativePath || file.fileName);
    const chkVisibility = (state.isSelectMode || state.selectedSongs.size > 0 || isChecked) ? 'inline-block' : 'hidden';

    let badgeClass = 'bg-indigo-950 text-indigo-400 border border-indigo-800/60';
    let badgeLabel = 'GM';
    let badgeTitle = 'Standard General MIDI file (auto-remapped on the fly)';

    if (isRec) {
        badgeClass = 'bg-rose-950 text-rose-400 border border-rose-800/60';
        badgeLabel = 'REC';
        badgeTitle = `Native ${state.activeModel === 'pss680' ? 'Yamaha PSS-680' : 'Yamaha PSS-790'} Studio Recording (direct hardware passthrough)`;
    } else if (isPss) {
        badgeClass = 'bg-emerald-950 text-emerald-400 border border-emerald-800/60';
        badgeLabel = is680 ? '680' : 'PSS';
        badgeTitle = `Native ${is680 ? 'Yamaha PSS-680' : 'Yamaha PSS-790'} converted file (plays via direct hardware passthrough)`;
    }

    const div = document.createElement("div");
    div.className = `group tree-song-item flex items-center justify-between px-2 py-1 rounded-lg border border-transparent cursor-pointer select-none text-[11px] text-slate-300 transition-all ${isSelected ? 'selected' : ''} ${isPlaying ? 'playing' : ''} ${isChecked ? 'bg-cyan-950/40 border-cyan-800/50' : ''}`;
    div.dataset.fullPath = file.fullPath;
    div.dataset.fileName = file.fileName;
    div.title = "";

    div.draggable = true;

    div.innerHTML = `
        <div class="flex items-center space-x-1.5 min-w-0 flex-1">
            <input type="checkbox" class="song-checkbox w-3.5 h-3.5 rounded text-cyan-500 bg-slate-800 border-slate-700 cursor-pointer mr-0.5 accent-cyan-500 shrink-0 ${chkVisibility}" ${isChecked ? 'checked' : ''} title="Select song for batch moving or deletion">
            <button type="button" class="btn-song-star shrink-0 text-[11px] px-0.5 hover:scale-125 transition ${isFav ? 'text-amber-400 font-bold' : 'text-slate-600 hover:text-amber-400'}" title="${isFav ? 'Remove from Favourites (F)' : 'Add to Favourites (F)'}">
                ${isFav ? '★' : '☆'}
            </button>
            <span class="song-icon text-[11px] shrink-0 ${isPlaying ? 'text-cyan-400 animate-pulse' : (isSelected ? 'text-cyan-400' : 'text-slate-500')}">
                ${isPlaying ? '🔊' : (isSelected ? '▶' : '🎵')}
            </span>
            ${formatSongTitleHtml(file.fileName, file.subFolder)}
        </div>
        <span class="song-badge text-[9px] font-bold font-mono px-1 py-0.2 rounded shrink-0 ml-1.5 ${badgeClass}" title="${badgeTitle}">
            ${badgeLabel}
        </span>
    `;

    const starBtn = div.querySelector(".btn-song-star");
    if (starBtn) {
        starBtn.addEventListener("click", (e) => {
            e.stopPropagation();
            sendToHost("toggle_favorite", { path: file.fullPath });
        });
    }

    const chk = div.querySelector(".song-checkbox");
    if (chk) {
        chk.addEventListener("click", (e) => {
            e.stopPropagation();
            if (e.shiftKey && state.lastCheckedSongPath && state.visibleSongList.length > 0) {
                const idx1 = state.visibleSongList.findIndex(f => f.fullPath === state.lastCheckedSongPath);
                const idx2 = state.visibleSongList.findIndex(f => f.fullPath === file.fullPath);
                if (idx1 !== -1 && idx2 !== -1) {
                    const start = Math.min(idx1, idx2);
                    const end = Math.max(idx1, idx2);
                    for (let i = start; i <= end; i++) {
                        state.selectedSongs.add(state.visibleSongList[i].fullPath);
                    }
                }
            } else {
                if (chk.checked) {
                    state.selectedSongs.add(file.fullPath);
                } else {
                    state.selectedSongs.delete(file.fullPath);
                }
            }
            state.lastCheckedSongPath = file.fullPath;
            if (state.selectedSongs.size > 0) {
                state.isSelectMode = true;
            }
            updateBatchBar();
            renderLibraryTree();
        });
    }

    div.addEventListener("dragstart", (e) => {
        e.stopPropagation();
        const draggedPaths = state.selectedSongs.has(file.fullPath)
            ? Array.from(state.selectedSongs)
            : [file.fullPath];

        e.dataTransfer.setData("application/json", JSON.stringify({
            type: "songs",
            paths: draggedPaths,
            category: file.format
        }));
        e.dataTransfer.effectAllowed = "move";
        div.classList.add("opacity-50");
    });

    div.addEventListener("dragend", () => {
        div.classList.remove("opacity-50");
        document.querySelectorAll(".drag-target-active").forEach(el => {
            el.classList.remove("drag-target-active", "ring-2", "ring-cyan-400", "bg-cyan-950/60");
        });
    });

    div.addEventListener("click", (e) => {
        if (e.target.classList.contains("song-checkbox") || e.target.closest(".btn-song-star")) return;
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();

        if (state.isPlaying) {
            // When already playing, clicking another song switches to it and plays immediately from 00:00!
            if (file.fullPath !== state.currentFilePath) {
                sendToHost("load_midi_and_play", { path: file.fullPath });
            }
        } else {
            // Idle/stopped: load metadata onto player deck ready to play
            sendToHost("load_midi_metadata", { path: file.fullPath });
        }
    });

    div.addEventListener("dblclick", (e) => {
        if (e.target.classList.contains("song-checkbox") || e.target.closest(".btn-song-star")) return;
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();
        sendToHost("load_midi_and_play", { path: file.fullPath });
    });

    div.addEventListener("contextmenu", (e) => {
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();
        showContextMenu(e, {
            type: "song",
            category: file.format,
            file: file,
            fullPath: file.fullPath,
            name: file.fileName,
            subFolder: file.subFolder
        });
    });

    return div;
}

function highlightActiveSongInTree() {
    const items = document.querySelectorAll(".tree-song-item");
    items.forEach(el => {
        const path = el.dataset.fullPath;
        const isSelected = path === state.selectedSongPath;
        const isPlayingThis = path === state.currentFilePath && state.isPlaying;

        el.classList.toggle("selected", isSelected);
        el.classList.toggle("playing", isPlayingThis);

        const iconEl = el.querySelector(".song-icon");
        if (iconEl) {
            if (isPlayingThis) {
                iconEl.textContent = "🔊";
                iconEl.className = "song-icon text-[11px] shrink-0 text-cyan-400 animate-pulse";
            } else if (isSelected) {
                iconEl.textContent = "▶";
                iconEl.className = "song-icon text-[11px] shrink-0 text-cyan-400";
            } else {
                iconEl.textContent = "🎵";
                iconEl.className = "song-icon text-[11px] shrink-0 text-slate-500";
            }
        }
    });
}

function navigateSongList(direction) {
    if (!state.visibleSongList || state.visibleSongList.length === 0) return;

    let currentIndex = state.visibleSongList.findIndex(f => f.fullPath === state.selectedSongPath);
    let targetIndex;
    if (currentIndex === -1) {
        targetIndex = direction > 0 ? 0 : state.visibleSongList.length - 1;
    } else {
        targetIndex = currentIndex + direction;
        if (targetIndex < 0) targetIndex = 0;
        if (targetIndex >= state.visibleSongList.length) targetIndex = state.visibleSongList.length - 1;
    }

    selectSongByIndex(targetIndex);
}

let metadataDebounceTimer = null;

function selectSongByIndex(index) {
    if (!state.visibleSongList || index < 0 || index >= state.visibleSongList.length) return;
    const song = state.visibleSongList[index];
    state.selectedSongPath = song.fullPath;
    highlightActiveSongInTree();

    const el = document.querySelector(`.tree-song-item[data-full-path="${CSS.escape(song.fullPath)}"]`);
    if (el) {
        el.scrollIntoView({ block: "nearest", behavior: "auto" });
    }

    // When stopped: debounce metadata update by 100ms so moving across rows is instantaneous and never lags
    // When playing: DO NOT call load_midi_metadata while browsing! Protects uninterrupted playback!
    if (!state.isPlaying) {
        if (metadataDebounceTimer) clearTimeout(metadataDebounceTimer);
        metadataDebounceTimer = setTimeout(() => {
            metadataDebounceTimer = null;
            if (!state.isPlaying && state.selectedSongPath === song.fullPath) {
                sendToHost("load_midi_metadata", { path: song.fullPath });
            }
        }, 100);
    }
}

function playSongByIndex(index) {
    if (!state.visibleSongList || index < 0 || index >= state.visibleSongList.length) return;
    const song = state.visibleSongList[index];
    state.selectedSongPath = song.fullPath;
    highlightActiveSongInTree();
    sendToHost("load_midi_and_play", { path: song.fullPath });

    const el = document.querySelector(`.tree-song-item[data-full-path="${CSS.escape(song.fullPath)}"]`);
    if (el) {
        el.scrollIntoView({ block: "nearest", behavior: "auto" });
    }
}

function playNextSong() {
    if (!state.visibleSongList || state.visibleSongList.length === 0) return;
    let idx = state.visibleSongList.findIndex(f => f.fullPath === (state.currentFilePath || state.selectedSongPath));
    if (idx === -1) idx = 0;
    else idx = (idx + 1) % state.visibleSongList.length;

    playSongByIndex(idx);
}

function playPrevSong() {
    if (!state.visibleSongList || state.visibleSongList.length === 0) return;
    let idx = state.visibleSongList.findIndex(f => f.fullPath === (state.currentFilePath || state.selectedSongPath));
    if (idx === -1) idx = state.visibleSongList.length - 1;
    else idx = (idx - 1 + state.visibleSongList.length) % state.visibleSongList.length;

    playSongByIndex(idx);
}

function toggleAutoAdvance(explicit) {
    state.autoAdvance = explicit !== undefined ? explicit : !state.autoAdvance;
    try {
        localStorage.setItem("pss_auto_advance", state.autoAdvance ? "true" : "false");
    } catch { }
    updateAutoAdvanceUi();
    showToast(`Continuous Auto-Advance: ${state.autoAdvance ? 'Enabled (Next track plays automatically)' : 'Disabled'}`);
}

function updateAutoAdvanceUi() {
    const btn = document.getElementById("btnAutoAdvance");
    if (!btn) return;
    if (state.autoAdvance) {
        btn.className = "px-2 py-1 rounded-lg bg-cyan-500/25 text-cyan-400 border border-cyan-500/50 text-[11px] font-semibold transition flex items-center space-x-1 shadow-sm ring-1 ring-cyan-500/30 active:scale-95";
    } else {
        btn.className = "px-2 py-1 rounded-lg bg-slate-800/80 hover:bg-slate-700 text-[11px] font-medium text-slate-400 hover:text-white border border-slate-700/60 transition flex items-center space-x-1 active:scale-95";
    }
}

function toggleHandheldMode(explicit) {
    state.handheldMode = explicit !== undefined ? explicit : !state.handheldMode;
    document.body.classList.toggle("handheld-touch-mode", state.handheldMode);
    try {
        localStorage.setItem("pss_handheld_mode", state.handheldMode ? "true" : "false");
    } catch { }
    updateHandheldModeUi();
    showToast(`Handheld Touch Targets: ${state.handheldMode ? 'Enlarged for 7" Asus ROG Ally' : 'Standard Compact'}`);
}

function updateHandheldModeUi() {
    const btn = document.getElementById("btnToggleHandheldMode");
    const modalBtnText = document.getElementById("modalHandheldBtnText");
    if (btn) {
        if (state.handheldMode) {
            btn.className = "flex items-center space-x-1 px-2 sm:px-2.5 py-1 rounded-lg bg-amber-500/25 text-amber-300 border border-amber-500/50 text-xs font-semibold shadow-sm ring-1 ring-amber-500/30 transition active:scale-95 shrink-0";
        } else {
            btn.className = "flex items-center space-x-1 px-2 sm:px-2.5 py-1 rounded-lg bg-slate-800/80 hover:bg-slate-700 text-xs font-medium text-slate-300 hover:text-white border border-slate-700/60 transition active:scale-95 shrink-0";
        }
    }
    if (modalBtnText) {
        modalBtnText.textContent = state.handheldMode ? "Disable Handheld Touch Targets" : "Enable Handheld Touch Targets";
    }
}

function initHandheldMode() {
    if (state.handheldMode) {
        document.body.classList.add("handheld-touch-mode");
    }
    updateHandheldModeUi();
    updateAutoAdvanceUi();
}

function collapseCurrentFolder() {
    if (!state.selectedSongPath) return;
    const song = (state.visibleSongList || []).find(f => f.fullPath === state.selectedSongPath);
    if (!song || !song.subFolder) return;
    const is680 = state.activeModel === "pss680";
    const catKey = (song.format === "pss680" || song.format === "pss790") ? song.format : (is680 ? "pss680" : "gm");
    const folderKey = `${catKey}:${song.subFolder}`;
    if (state.expandedFolders && state.expandedFolders.has(folderKey)) {
        state.expandedFolders.delete(folderKey);
        saveExpandedFolders();
        renderLibraryTree();
    }
}

function expandCurrentFolder() {
    if (!state.selectedSongPath) return;
    const song = (state.visibleSongList || []).find(f => f.fullPath === state.selectedSongPath);
    if (!song || !song.subFolder) return;
    const is680 = state.activeModel === "pss680";
    const catKey = (song.format === "pss680" || song.format === "pss790") ? song.format : (is680 ? "pss680" : "gm");
    const folderKey = `${catKey}:${song.subFolder}`;
    if (state.expandedFolders && !state.expandedFolders.has(folderKey)) {
        state.expandedFolders.add(folderKey);
        saveExpandedFolders();
        renderLibraryTree();
    }
}

function setupKeyboardShortcuts() {
    window.addEventListener("keydown", (e) => {
        const activeTag = document.activeElement ? document.activeElement.tagName.toLowerCase() : "";
        const isInputFocused = activeTag === "input" || activeTag === "textarea" || activeTag === "select";

        // Dismiss context menu on Escape
        const ctxMenu = document.getElementById("libraryContextMenu");
        if (ctxMenu && !ctxMenu.classList.contains("hidden") && e.key === "Escape") {
            e.preventDefault();
            hideContextMenu();
            return;
        }

        // About modal keyboard shortcuts
        const aboutModal = document.getElementById("aboutModal");
        if (aboutModal && !aboutModal.classList.contains("hidden")) {
            if (e.key === "Escape") {
                e.preventDefault();
                closeAboutModal();
                return;
            }
            return;
        }

        // Action modal keyboard shortcuts
        const actionModal = document.getElementById("fileActionModal");
        if (actionModal && !actionModal.classList.contains("hidden")) {
            if (e.key === "Escape") {
                e.preventDefault();
                closeActionModal();
                return;
            }
            if (e.key === "Enter" && document.activeElement && document.activeElement.id === "actionModalInput") {
                e.preventDefault();
                confirmActionModal();
                return;
            }
            return;
        }

        // Shortcuts modal keyboard shortcuts
        const shortcutsModal = document.getElementById("shortcutsModal");
        if (shortcutsModal && !shortcutsModal.classList.contains("hidden")) {
            if (e.key === "Escape") {
                e.preventDefault();
                closeShortcutsModal();
                return;
            }
            return;
        }

        // Global shortcut: ? or F1 to open DAW hotkeys modal
        if (!isInputFocused && (e.key === "?" || e.key === "F1")) {
            e.preventDefault();
            if (shortcutsModal && !shortcutsModal.classList.contains("hidden")) {
                closeShortcutsModal();
            } else {
                openShortcutsModal();
            }
            return;
        }

        // Global shortcut: Ctrl+F to focus library search
        if ((e.ctrlKey || e.metaKey) && (e.key === "f" || e.key === "F")) {
            e.preventDefault();
            const searchInput = document.getElementById("sidebarSearchInput");
            if (searchInput) {
                if (state.sidebarCollapsed) {
                    toggleSidebar();
                }
                searchInput.focus();
                searchInput.select();
            }
            return;
        }

        // Global shortcut: Ctrl+B to toggle quick-pick sidebar anywhere
        if ((e.ctrlKey || e.metaKey) && (e.key === "b" || e.key === "B")) {
            e.preventDefault();
            toggleSidebar();
            return;
        }

        // Global shortcut: 1, 2, 3 to switch main tabs, L to toggle loop
        if (!isInputFocused && !e.ctrlKey && !e.altKey && !e.metaKey) {
            if (e.key === "1") {
                e.preventDefault();
                switchTab("player");
                return;
            } else if (e.key === "2") {
                e.preventDefault();
                switchTab("recorder");
                return;
            } else if (e.key === "3") {
                e.preventDefault();
                switchTab("sysex");
                return;
            } else if (e.key === "l" || e.key === "L") {
                e.preventDefault();
                const loopBtn = document.getElementById("btnLoop");
                if (loopBtn) loopBtn.click();
                return;
            } else if (e.key === "f" || e.key === "F") {
                e.preventDefault();
                const songToToggle = state.selectedSongPath || state.currentFilePath;
                if (songToToggle) {
                    sendToHost("toggle_favorite", { path: songToToggle });
                } else {
                    showToast("Select or play a song first to toggle favourite");
                }
                return;
            }
        }

        // If user is focused on an input element
        if (isInputFocused) {
            if (document.activeElement && document.activeElement.id === "sidebarSearchInput") {
                if (e.key === "ArrowDown") {
                    e.preventDefault();
                    if (state.visibleSongList && state.visibleSongList.length > 0) {
                        selectSongByIndex(0);
                    }
                } else if (e.key === "Enter") {
                    e.preventDefault();
                    if (state.visibleSongList && state.visibleSongList.length > 0) {
                        playSongByIndex(0);
                    }
                } else if (e.key === "Escape") {
                    e.preventDefault();
                    clearSearchFilter();
                    document.activeElement.blur();
                }
            }
            return;
        }

        // Media player shortcuts
        switch (e.key) {
            case " ": // Space: Play/Pause
                e.preventDefault();
                if (state.isPlaying) {
                    sendToHost("pause");
                } else {
                    sendToHost("play");
                }
                break;

            case "Escape": // Esc: Panic / Stop
                e.preventDefault();
                sendToHost("stop");
                break;

            case "ArrowDown":
            case "j":
            case "J": // Next song in visible list
                e.preventDefault();
                navigateSongList(1);
                break;

            case "ArrowUp":
            case "k":
            case "K": // Previous song in visible list
                e.preventDefault();
                navigateSongList(-1);
                break;

            case "PageDown":
                e.preventDefault();
                navigateSongList(8);
                break;

            case "PageUp":
                e.preventDefault();
                navigateSongList(-8);
                break;

            case "Home":
                e.preventDefault();
                selectSongByIndex(0);
                break;

            case "End":
                e.preventDefault();
                if (state.visibleSongList && state.visibleSongList.length > 0) {
                    selectSongByIndex(state.visibleSongList.length - 1);
                }
                break;

            case "[": // Transpose pitch down 1 semitone
                e.preventDefault();
                document.getElementById("btnTransposeDown")?.click();
                break;

            case "]": // Transpose pitch up 1 semitone
                e.preventDefault();
                document.getElementById("btnTransposeUp")?.click();
                break;

            case "Enter": // Play selected song
                e.preventDefault();
                if (state.selectedSongPath) {
                    sendToHost("load_midi_and_play", { path: state.selectedSongPath });
                }
                break;

            case "n":
            case "N": // Skip to Next Track
                e.preventDefault();
                playNextSong();
                break;

            case "p":
            case "P": // Skip to Previous Track
                e.preventDefault();
                playPrevSong();
                break;

            case "t":
            case "T": // Toggle Handheld Touch Mode
                e.preventDefault();
                toggleHandheldMode();
                break;

            case "ArrowLeft": // Seek backward 5s (or collapse folder when stopped)
                e.preventDefault();
                if (state.isPlaying && !e.altKey) {
                    sendToHost("seek", { seconds: Math.max(0, state.currentSeconds - 5) });
                } else {
                    collapseCurrentFolder();
                }
                break;

            case "ArrowRight": // Seek forward 5s (or expand folder when stopped)
                e.preventDefault();
                if (state.isPlaying && !e.altKey) {
                    sendToHost("seek", { seconds: Math.min(state.durationSeconds, state.currentSeconds + 5) });
                } else {
                    expandCurrentFolder();
                }
                break;
        }
    });
}

function renderMixer() {
    clearAllChannelLeds();
    const grid = document.getElementById("mixerGrid");
    grid.innerHTML = "";

    const vecQuadBox = document.getElementById("vectorQuadControls");
    const btnVecQuad = document.getElementById("btnToggleVectorQuad");
    const selVecPreset = document.getElementById("selMixerVectorPreset");

    if (vecQuadBox) {
        if (state.activeModel === "pss790") {
            vecQuadBox.classList.remove("hidden");
            if (selVecPreset && selVecPreset.options.length <= 1) {
                Object.entries(VECTOR_PRESETS).forEach(([k, p]) => {
                    const opt = document.createElement("option");
                    opt.value = k;
                    opt.textContent = p.name;
                    selVecPreset.appendChild(opt);
                });
            }
            if (btnVecQuad) {
                if (state.vectorQuadActive) {
                    btnVecQuad.className = "px-2 py-0.5 rounded text-[10px] font-bold bg-indigo-600 text-white shadow-sm ring-1 ring-indigo-400 transition";
                    btnVecQuad.textContent = "ON";
                    selVecPreset?.classList.remove("hidden");
                    if (state.selectedVectorPresetKey && selVecPreset) {
                        selVecPreset.value = state.selectedVectorPresetKey;
                    }
                } else {
                    btnVecQuad.className = "px-2 py-0.5 rounded text-[10px] font-bold bg-slate-800 text-slate-400 hover:text-white border border-slate-700 transition";
                    btnVecQuad.textContent = "OFF";
                    selVecPreset?.classList.add("hidden");
                }

                btnVecQuad.onclick = () => {
                    state.vectorQuadActive = !state.vectorQuadActive;
                    if (state.vectorQuadActive && !state.selectedVectorPresetKey) {
                        state.selectedVectorPresetKey = "aha_take_on_me";
                    }
                    applyVectorQuadPreset(state.vectorQuadActive ? state.selectedVectorPresetKey : null);
                    renderMixer();
                };
            }

            if (selVecPreset) {
                selVecPreset.onchange = (e) => {
                    if (e.target.value) {
                        state.selectedVectorPresetKey = e.target.value;
                        applyVectorQuadPreset(state.selectedVectorPresetKey);
                        renderMixer();
                    }
                };
            }
        } else {
            vecQuadBox.classList.add("hidden");
        }
    }

    state.channels.forEach(ch => {
        const card = document.createElement("div");
        const isDrum = ch.channel === 15;
        const is680 = state.activeModel === "pss680";
        const isVectorQuad = !is680 && state.vectorQuadActive && ch.channel < 4;
        const quadPoleNames = ["A (North)", "B (East)", "C (South)", "D (West)"];

        const topAccent = isDrum
            ? 'border-t-2 border-t-amber-400'
            : (isVectorQuad ? 'border-t-2 border-t-indigo-400' : (ch.hasNotes ? 'border-t-2 border-t-cyan-400' : 'border-t-2 border-t-slate-800/80'));

        const cardBorder = isDrum
            ? 'border-2 border-amber-500/50 bg-gradient-to-b from-amber-950/30 via-slate-900/70 to-slate-950/80 shadow-md ring-1 ring-amber-500/30'
            : (isVectorQuad
                ? 'border-2 border-indigo-500/80 bg-indigo-950/30 ring-1 ring-indigo-500/30 shadow-md'
                : (ch.hasNotes ? 'border-cyan-500/25 bg-slate-900/60 shadow-sm' : 'border-slate-800/80 opacity-90'));

        card.className = `glass-card channel-strip-card p-2 rounded-xl flex flex-col justify-between space-y-1.5 transition-all duration-200 ${topAccent} ${cardBorder}`;

        const muteClass = ch.isMuted
            ? 'btn-console-m muted bg-rose-600 text-white shadow-sm shadow-rose-500/50 ring-1 ring-rose-400/80'
            : 'btn-console-m bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50';

        const soloClass = ch.isSolo
            ? 'btn-console-s soloed bg-amber-500 text-slate-950 shadow-sm shadow-amber-500/50 ring-1 ring-amber-300'
            : 'btn-console-s bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50';

        const drumSubtitle = is680 ? "32 Drum Fixed" : "Chart 1 Fixed";
        const drumTooltip = is680 ? "Channel 16 percussion fixed to 32 PSS-680 hand percussion instruments" : "Channel 16 rhythm fixed to Chart 1 drums";
        const chHeaderTitle = isDrum ? (is680 ? "Drums / Percussion (32 Hand Percussion)" : "Drums / Percussion (Chart 1)") : "Melody / Accompaniment";
        const overrideTitle = is680
            ? `Voice Override: Force Channel ${ch.channelDisplay} to play one of the FM presets (00-99), Digital Synthesizer banks (100-104), or FM library voices`
            : `Voice Override: Force Channel ${ch.channelDisplay} to play one of the 100 built-in Yamaha PSS-790 AWM preset voices`;
        const selectTitle = isDrum ? drumTooltip : `Override Channel ${ch.channelDisplay} voice`;

        const drumBadgeHtml = isDrum
            ? `<span class="px-1 py-0.5 rounded text-[8px] font-extrabold bg-amber-500/20 text-amber-300 border border-amber-500/40 uppercase">🥁 PERCUSSION</span>`
            : '';

        const quadBadgeHtml = isVectorQuad
            ? `<span class="px-1 py-0.5 rounded text-[8px] font-bold bg-indigo-900/90 text-indigo-300 border border-indigo-700/60 uppercase">Quad ${quadPoleNames[ch.channel]}</span>`
            : (ch.selectedVoice >= 100 && is680
                ? `<span class="px-1 py-0.5 rounded text-[8px] font-bold bg-cyan-950 text-cyan-300 border border-cyan-800/60 uppercase">FM Bank ${ch.selectedVoice - 99}</span>`
                : '');

        let voiceOptionsHtml = `<option value="" ${ch.selectedVoice === null ? 'selected' : ''}>Auto (GM Map)</option>`;
        if (is680) {
            const factoryVoices = state.voices.filter(v => v.number < 100);
            const userBanks = state.voices.filter(v => v.number >= 100);

            voiceOptionsHtml += `
                <optgroup label="FM Factory Presets (00-99)">
                    ${factoryVoices.map(v => `<option value="${v.number}" ${ch.selectedVoice === v.number ? 'selected' : ''} class="bg-slate-900 text-white">${v.number.toString().padStart(2, '0')}: ${v.name}</option>`).join('')}
                </optgroup>
                <optgroup label="Digital Synthesizer (User Banks)">
                    ${userBanks.map(v => `<option value="${v.number}" ${ch.selectedVoice === v.number ? 'selected' : ''} class="bg-indigo-950 text-indigo-200 font-semibold">${v.number}: ${v.name}</option>`).join('')}
                </optgroup>
                <optgroup label="FM Library Presets (Upload & Play)">
                    ${Object.entries(FM_PRESETS).filter(([k]) => k !== 'init').map(([k, p]) => `
                        <option value="fm:${k}" ${ch.selectedFmPresetKey === k ? 'selected' : ''} class="bg-cyan-950 text-cyan-200">
                            ⚡ ${p.name}
                        </option>
                    `).join('')}
                </optgroup>
            `;
        } else {
            voiceOptionsHtml += state.voices.map(v => `
                <option value="${v.number}" ${ch.selectedVoice === v.number ? 'selected' : ''} class="bg-slate-900 text-white">
                    ${v.number.toString().padStart(2, '0')}: ${v.name}
                </option>
            `).join('');
        }

        card.innerHTML = `
            <div class="flex items-center justify-between border-b border-slate-800 pb-1">
                <div class="flex items-center space-x-1">
                    <span class="text-[11px] font-bold ${isDrum ? 'text-amber-400' : (isVectorQuad ? 'text-indigo-400' : 'text-cyan-400')}" title="Channel ${ch.channelDisplay} (${chHeaderTitle})">CH ${ch.channelDisplay}</span>
                    ${drumBadgeHtml}
                    ${quadBadgeHtml}
                </div>
                <span id="led-${ch.channel}" class="meter-led w-2.5 h-2.5 rounded-full ${isDrum ? 'bg-amber-950' : (isVectorQuad ? 'bg-indigo-950' : 'bg-emerald-950')} border border-slate-700" title="Signal Activity Meter: Flashes when Channel ${ch.channelDisplay} receives note-on events"></span>
            </div>

            <div class="space-y-0.5">
                <div class="text-[10px] font-medium text-slate-200 truncate" title="${ch.detectedInstrument}">
                    ${ch.detectedInstrument}
                </div>
                <div class="text-[9px] text-slate-400 truncate" title="${isDrum ? drumTooltip : (ch.defaultPssVoiceName ? `Mapped to Voice: ${ch.defaultPssVoiceName}` : 'Auto-Mapped preset')}">
                    ${isDrum ? drumSubtitle : (ch.defaultPssVoiceName ? `Auto: ${ch.defaultPssVoiceName}` : 'Auto-Mapped')}
                </div>
            </div>

            <!-- Voice Override Combobox -->
            <div class="space-y-0.5">
                <label class="text-[9px] font-semibold text-slate-400 uppercase tracking-tight" title="${overrideTitle}">Voice Override</label>
                <select id="voice-override-${ch.channel}" ${isDrum ? 'disabled' : ''} class="w-full bg-slate-900 border border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-white focus:outline-none focus:border-cyan-500 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed" title="${selectTitle}">
                    ${voiceOptionsHtml}
                </select>
            </div>

            ${!isDrum ? `
            <!-- Channel Octave Transpose -->
            <div class="flex items-center justify-between bg-slate-950/70 rounded px-1.5 py-0.5 border border-slate-800/80">
                <span class="text-[8px] font-bold text-slate-400 uppercase" title="Channel Octave Shift">Octave</span>
                <div class="flex items-center space-x-1">
                    <button type="button" class="btn-ch-oct-down w-4 h-4 rounded bg-slate-800 hover:bg-cyan-600 text-[10px] font-bold text-slate-300 hover:text-white flex items-center justify-center transition active:scale-90" data-channel="${ch.channel}" title="Shift Down 1 Octave (-12 st)">-</button>
                    <span class="font-mono text-[9px] font-bold ${(ch.octaveShift || 0) !== 0 ? 'text-amber-400' : 'text-slate-400'} min-w-[20px] text-center select-none" title="Current octave shift">${(ch.octaveShift || 0) > 0 ? '+' + ((ch.octaveShift || 0) / 12) : ((ch.octaveShift || 0) < 0 ? ((ch.octaveShift || 0) / 12) : '0')}</span>
                    <button type="button" class="btn-ch-oct-up w-4 h-4 rounded bg-slate-800 hover:bg-cyan-600 text-[10px] font-bold text-slate-300 hover:text-white flex items-center justify-center transition active:scale-90" data-channel="${ch.channel}" title="Shift Up 1 Octave (+12 st)">+</button>
                </div>
            </div>
            ` : ''}

            <!-- Fader & Controls -->
            <div class="flex items-center justify-between space-x-1.5 pt-0.5">
                <div class="flex-1 space-y-0.5">
                    <input type="range" id="vol-${ch.channel}" min="0" max="127" value="${ch.volume}" class="w-full h-1 bg-slate-800" title="Channel ${ch.channelDisplay} Volume (CC 7): Adjust playback level (0-127)">
                    <div class="flex justify-between text-[8px] text-slate-500 font-mono">
                        <span title="MIDI Channel Volume (CC 7)">VOL</span>
                        <span id="vol-val-${ch.channel}" title="Current volume level (0-127)">${ch.volume}</span>
                    </div>
                </div>

                <div class="flex space-x-1">
                    <button id="mute-${ch.channel}" class="w-5 h-5 rounded text-[9px] font-bold ${muteClass}" title="Mute Channel ${ch.channelDisplay}: Silence this channel during playback">M</button>
                    <button id="solo-${ch.channel}" class="w-5 h-5 rounded text-[9px] font-bold ${soloClass}" title="Solo Channel ${ch.channelDisplay}: Isolate this channel and silence all other channels">S</button>
                </div>
            </div>
        `;

        grid.appendChild(card);

        // Attach channel events
        const voiceSelect = card.querySelector(`#voice-override-${ch.channel}`);
        if (voiceSelect) {
            voiceSelect.addEventListener("change", (e) => {
                const valStr = e.target.value;
                if (valStr.startsWith("fm:")) {
                    const presetKey = valStr.substring(3);
                    const preset = FM_PRESETS[presetKey];
                    if (preset) {
                        ch.selectedFmPresetKey = presetKey;
                        ch.selectedVoice = 100; // Bank 1
                        sendToHost("override_voice_fm_preset", {
                            channel: ch.channel,
                            bank: 0,
                            voice: preset
                        });
                        renderMixer();
                    }
                } else {
                    ch.selectedFmPresetKey = null;
                    const val = valStr === "" ? null : parseInt(valStr, 10);
                    ch.selectedVoice = val;
                    sendToHost("override_voice", { channel: ch.channel, voice: val });
                    renderMixer();
                }
            });
        }

        const volSlider = card.querySelector(`#vol-${ch.channel}`);
        if (volSlider) {
            volSlider.addEventListener("input", (e) => {
                const vol = parseInt(e.target.value);
                ch.volume = vol;
                card.querySelector(`#vol-val-${ch.channel}`).textContent = vol;
                sendToHost("set_channel_volume", { channel: ch.channel, volume: vol });
            });
        }

        const btnMute = card.querySelector(`#mute-${ch.channel}`);
        if (btnMute) {
            btnMute.addEventListener("click", () => {
                ch.isMuted = !ch.isMuted;
                btnMute.className = `w-5 h-5 rounded text-[9px] font-bold ${ch.isMuted ? 'btn-console-m muted bg-rose-600 text-white shadow-sm shadow-rose-500/50 ring-1 ring-rose-400/80' : 'btn-console-m bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50'}`;
                sendToHost("set_channel_mute", { channel: ch.channel, mute: ch.isMuted });
            });
        }

        const btnSolo = card.querySelector(`#solo-${ch.channel}`);
        if (btnSolo) {
            btnSolo.addEventListener("click", () => {
                ch.isSolo = !ch.isSolo;
                btnSolo.className = `w-5 h-5 rounded text-[9px] font-bold ${ch.isSolo ? 'btn-console-s soloed bg-amber-500 text-slate-950 shadow-sm shadow-amber-500/50 ring-1 ring-amber-300' : 'btn-console-s bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50'}`;
                sendToHost("set_channel_solo", { channel: ch.channel, solo: ch.isSolo });
            });
        }

        const btnOctDown = card.querySelector(".btn-ch-oct-down");
        if (btnOctDown) {
            btnOctDown.addEventListener("click", () => {
                const cur = ch.octaveShift || 0;
                const next = Math.max(-24, cur - 12);
                ch.octaveShift = next;
                sendToHost("set_channel_octave", { channel: ch.channel, octaveShift: next });
                renderMixer();
            });
        }

        const btnOctUp = card.querySelector(".btn-ch-oct-up");
        if (btnOctUp) {
            btnOctUp.addEventListener("click", () => {
                const cur = ch.octaveShift || 0;
                const next = Math.min(24, cur + 12);
                ch.octaveShift = next;
                sendToHost("set_channel_octave", { channel: ch.channel, octaveShift: next });
                renderMixer();
            });
        }
    });
}

function updateMasterTransposeDisplay() {
    const lbl = document.getElementById("lblMasterTranspose");
    if (!lbl) return;
    const t = state.masterTranspose || 0;
    lbl.textContent = t > 0 ? `+${t}` : `${t}`;
    lbl.className = `font-mono text-[10px] font-bold min-w-[26px] text-center cursor-pointer select-none transition ${t !== 0 ? 'text-amber-400 font-extrabold' : 'text-cyan-400'}`;
}

const channelLedTimers = {};
const channelLedLastOn = {};

function updateChannelLed(channel, isActive, velocity) {
    const led = document.getElementById(`led-${channel}`);
    if (!led) return;

    const isDrum = channel === 15;
    const activeClass = isDrum ? 'drum-active' : 'active';

    if (isActive && velocity > 0) {
        if (channelLedTimers[channel]) {
            clearTimeout(channelLedTimers[channel]);
            delete channelLedTimers[channel];
        }
        channelLedLastOn[channel] = performance.now();
        led.classList.add(activeClass);
    } else {
        const lastOn = channelLedLastOn[channel] || 0;
        const elapsed = performance.now() - lastOn;
        const minHoldMs = 120; // 120ms peak hold for crisp visible pulse

        if (channelLedTimers[channel]) {
            clearTimeout(channelLedTimers[channel]);
            delete channelLedTimers[channel];
        }

        if (elapsed < minHoldMs) {
            channelLedTimers[channel] = setTimeout(() => {
                led.classList.remove(activeClass);
                delete channelLedTimers[channel];
            }, minHoldMs - elapsed);
        } else {
            led.classList.remove(activeClass);
        }
    }
}

function clearAllChannelLeds() {
    for (let ch = 0; ch < 16; ch++) {
        if (channelLedTimers[ch]) {
            clearTimeout(channelLedTimers[ch]);
            delete channelLedTimers[ch];
        }
        const led = document.getElementById(`led-${ch}`);
        if (led) {
            led.classList.remove('active', 'drum-active');
        }
    }
}

// --- Recorder UI Rendering & Events ---

function renderRecorderChannels() {
    const grid = document.getElementById("recorderChannelGrid");
    if (!grid) return;
    grid.innerHTML = "";

    state.recorderChannels.forEach(ch => {
        const card = document.createElement("div");
        card.id = `rec-card-${ch.channel}`;
        card.className = "glass-card p-3 rounded-xl border border-slate-800/80 flex items-center justify-between space-x-2 transition-all duration-150";
        card.title = `Inbound MIDI Channel ${ch.channelDisplay}${ch.isDrum ? ' (Drums / Percussion)' : ' (Melody / Harmonic)'}`;

        card.innerHTML = `
            <div class="flex items-center space-x-2">
                <span id="rec-led-${ch.channel}" class="inbound-led w-3 h-3 rounded-full bg-slate-800 border border-slate-700" title="Inbound Signal LED: Glows rose when MIDI messages arrive on Channel ${ch.channelDisplay}"></span>
                <div>
                    <div class="text-[11px] font-bold ${ch.isDrum ? 'text-amber-400' : 'text-slate-300'}" title="MIDI Channel ${ch.channelDisplay}">CH ${ch.channelDisplay}</div>
                    <div id="rec-note-${ch.channel}" class="text-[10px] font-mono text-slate-500" title="Last MIDI note sounding on Channel ${ch.channelDisplay}">-</div>
                </div>
            </div>
            <span id="rec-vel-${ch.channel}" class="text-[10px] font-mono text-slate-600" title="Last note strike velocity (0-127)">0</span>
        `;

        grid.appendChild(card);
    });
}

// --- Musical Pitch & Real-Time Live Chord Analyzer ---
function noteToPitch(noteNumber) {
    if (noteNumber === undefined || noteNumber === null || noteNumber < 0 || noteNumber > 127) return "-";
    const octave = Math.floor(noteNumber / 12) - 1;
    const note = NOTE_NAMES[noteNumber % 12];
    return `${note}${octave}`;
}

const CHORD_DEFINITIONS = [
    { name: "Maj", intervals: [0, 4, 7] },
    { name: "Min", intervals: [0, 3, 7] },
    { name: "7", intervals: [0, 4, 7, 10] },
    { name: "Maj7", intervals: [0, 4, 7, 11] },
    { name: "Min7", intervals: [0, 3, 7, 10] },
    { name: "Dim", intervals: [0, 3, 6] },
    { name: "Dim7", intervals: [0, 3, 6, 9] },
    { name: "m7b5", intervals: [0, 3, 6, 10] },
    { name: "Aug", intervals: [0, 4, 8] },
    { name: "Sus4", intervals: [0, 5, 7] },
    { name: "Sus2", intervals: [0, 2, 7] },
    { name: "6", intervals: [0, 4, 7, 9] },
    { name: "Min6", intervals: [0, 3, 7, 9] },
    { name: "Add9", intervals: [0, 2, 4, 7] },
    { name: "9", intervals: [0, 2, 4, 7, 10] },
    { name: "Maj9", intervals: [0, 2, 4, 7, 11] },
    { name: "Min9", intervals: [0, 2, 3, 7, 10] }
];

const INTERVAL_NAMES = {
    0: "Unison / Octave",
    1: "Minor 2nd",
    2: "Major 2nd",
    3: "Minor 3rd",
    4: "Major 3rd",
    5: "Perfect 4th",
    6: "Tritone",
    7: "Perfect 5th",
    8: "Minor 6th",
    9: "Major 6th",
    10: "Minor 7th",
    11: "Major 7th"
};

function analyzeInboundChords() {
    const chordDisplay = document.getElementById("liveChordDisplay");
    const notesDisplay = document.getElementById("liveChordNotesDisplay");
    const notesLabel = document.getElementById("liveChordNotesLabel");
    if (!chordDisplay || !notesDisplay) return;

    const notes = Array.from(state.activeInboundNotes.keys()).sort((a, b) => a - b);
    if (notes.length === 0) {
        chordDisplay.textContent = "STANDBY";
        chordDisplay.className = "text-xl sm:text-2xl font-black font-mono tracking-tight text-emerald-400 truncate opacity-60";
        notesDisplay.textContent = "--";
        if (notesLabel) notesLabel.textContent = "Active Notes";
        return;
    }

    chordDisplay.className = "text-xl sm:text-2xl font-black font-mono tracking-tight text-emerald-400 truncate";
    const notePitches = notes.map(noteToPitch);
    notesDisplay.textContent = notePitches.join(" ");

    if (notes.length === 1) {
        chordDisplay.textContent = notePitches[0];
        if (notesLabel) notesLabel.textContent = `MIDI Note #${notes[0]}`;
        return;
    }

    if (notes.length === 2) {
        const semitones = (notes[1] - notes[0]) % 12;
        const intervalName = INTERVAL_NAMES[semitones] || "Interval";
        chordDisplay.textContent = `${notePitches[0]} - ${notePitches[1]}`;
        if (notesLabel) notesLabel.textContent = intervalName;
        return;
    }

    // 3 or more notes: detect chord root, quality, and inversion
    const pitchClasses = Array.from(new Set(notes.map(n => n % 12)));
    const bassPitchClass = notes[0] % 12;

    let bestMatch = null;

    for (const root of pitchClasses) {
        const rootName = NOTE_NAMES[root];
        const intervals = pitchClasses.map(p => (p - root + 12) % 12).sort((a, b) => a - b);

        for (const def of CHORD_DEFINITIONS) {
            if (def.intervals.length === intervals.length &&
                def.intervals.every((v, i) => v === intervals[i])) {
                let chordName = `${rootName} ${def.name}`;
                if (bassPitchClass !== root) {
                    chordName += ` / ${NOTE_NAMES[bassPitchClass]}`;
                }
                bestMatch = { name: chordName, root: rootName, formula: def.name };
                break;
            }
        }
        if (bestMatch) break;
    }

    if (bestMatch) {
        chordDisplay.textContent = bestMatch.name;
        if (notesLabel) notesLabel.textContent = `Formula: ${bestMatch.formula}`;
    } else {
        chordDisplay.textContent = notePitches.slice(0, 4).join(" ");
        if (notesLabel) notesLabel.textContent = "Compound Chord";
    }
}

// --- Stereo Master Peak Level VU Meters ---
let targetVuLeft = 0;
let targetVuRight = 0;
let lastVuTime = performance.now();

function initVuMeters() {
    const leftLadder = document.getElementById("recVuLeft");
    const rightLadder = document.getElementById("recVuRight");
    if (!leftLadder || !rightLadder) return;

    leftLadder.innerHTML = "";
    rightLadder.innerHTML = "";

    for (let i = 0; i < 12; i++) {
        let colorClass = "green";
        if (i >= 10) colorClass = "red";
        else if (i >= 7) colorClass = "amber";

        const segL = document.createElement("div");
        segL.className = `vu-segment ${colorClass}`;
        segL.id = `vu-l-${i}`;
        leftLadder.appendChild(segL);

        const segR = document.createElement("div");
        segR.className = `vu-segment ${colorClass}`;
        segR.id = `vu-r-${i}`;
        rightLadder.appendChild(segR);
    }

    requestAnimationFrame(updateVuMeterPhysics);
}

function updateVuMeterPhysics(currentTime) {
    const dt = Math.min((currentTime - lastVuTime) / 1000, 0.1);
    lastVuTime = currentTime;

    // Instant attack, exponential decay (~350ms decay half-life)
    if (targetVuLeft > state.vuLeftLevel) {
        state.vuLeftLevel = targetVuLeft;
    } else {
        state.vuLeftLevel = Math.max(0, state.vuLeftLevel - (dt * 18));
    }

    if (targetVuRight > state.vuRightLevel) {
        state.vuRightLevel = targetVuRight;
    } else {
        state.vuRightLevel = Math.max(0, state.vuRightLevel - (dt * 18));
    }

    targetVuLeft = Math.max(0, targetVuLeft - (dt * 12));
    targetVuRight = Math.max(0, targetVuRight - (dt * 12));

    // Peak hold logic
    if (state.vuLeftLevel > state.vuLeftPeak) {
        state.vuLeftPeak = state.vuLeftLevel;
        if (state.vuLeftPeakTimer) clearTimeout(state.vuLeftPeakTimer);
        state.vuLeftPeakTimer = setTimeout(() => { state.vuLeftPeak = 0; }, 800);
    }

    if (state.vuRightLevel > state.vuRightPeak) {
        state.vuRightPeak = state.vuRightLevel;
        if (state.vuRightPeakTimer) clearTimeout(state.vuRightPeakTimer);
        state.vuRightPeakTimer = setTimeout(() => { state.vuRightPeak = 0; }, 800);
    }

    const litCountL = Math.round(state.vuLeftLevel);
    const peakIndexL = Math.round(state.vuLeftPeak);
    const litCountR = Math.round(state.vuRightLevel);
    const peakIndexR = Math.round(state.vuRightPeak);

    for (let i = 0; i < 12; i++) {
        const segL = document.getElementById(`vu-l-${i}`);
        if (segL) {
            const isLit = i < litCountL;
            const isPeak = i === (peakIndexL - 1) && peakIndexL > 0;
            segL.classList.toggle("lit", isLit);
            segL.classList.toggle("peak", isPeak);
        }

        const segR = document.getElementById(`vu-r-${i}`);
        if (segR) {
            const isLit = i < litCountR;
            const isPeak = i === (peakIndexR - 1) && peakIndexR > 0;
            segR.classList.toggle("lit", isLit);
            segR.classList.toggle("peak", isPeak);
        }
    }

    requestAnimationFrame(updateVuMeterPhysics);
}

function feedVuMeter(velocity, channel) {
    if (velocity <= 0) return;
    const normalized = Math.min(velocity / 127.0, 1.0);
    const totalSegments = normalized * 12.0;

    let pan = 0.5; // center
    if (channel !== undefined) {
        if (channel === 15 || channel === 9) {
            pan = 0.5; // Drums center
        } else if (channel % 2 === 0) {
            pan = 0.35; // Panned slightly left
        } else {
            pan = 0.65; // Panned slightly right
        }
    }

    const leftAmp = totalSegments * (1 - pan) * 2;
    const rightAmp = totalSegments * pan * 2;

    targetVuLeft = Math.max(targetVuLeft, Math.min(12, leftAmp));
    targetVuRight = Math.max(targetVuRight, Math.min(12, rightAmp));
}

function updateRecorderChannelActivity(channel, isActive, velocity, noteNumber) {
    const led = document.getElementById(`rec-led-${channel}`);
    const noteLabel = document.getElementById(`rec-note-${channel}`);
    const velLabel = document.getElementById(`rec-vel-${channel}`);
    const card = document.getElementById(`rec-card-${channel}`);
    const ticker = document.getElementById("recorderLiveTicker");

    if (led) {
        if (isActive && velocity > 0) {
            led.classList.add("active");
            if (card) card.style.borderColor = "rgba(244, 63, 94, 0.4)";
        } else {
            led.classList.remove("active");
            if (card) card.style.borderColor = "";
        }
    }

    if (velLabel) velLabel.textContent = velocity;

    if (noteNumber !== undefined && noteNumber !== null) {
        const pitchName = noteToPitch(noteNumber);
        if (isActive && velocity > 0) {
            state.activeInboundNotes.set(noteNumber, { channel, velocity, time: Date.now() });
            if (noteLabel) {
                noteLabel.textContent = pitchName;
                noteLabel.className = "text-[10px] font-mono font-bold text-rose-400";
            }
            feedVuMeter(velocity, channel);
        } else {
            state.activeInboundNotes.delete(noteNumber);
            if (noteLabel) {
                noteLabel.className = "text-[10px] font-mono text-slate-500";
            }
        }
        analyzeInboundChords();
    }

    if (isActive && velocity > 0) {
        if (ticker) {
            const pitchStr = (noteNumber !== undefined && noteNumber !== null) ? ` • Pitch ${noteToPitch(noteNumber)}` : "";
            ticker.textContent = `Inbound: Ch ${channel + 1}${pitchStr} • Velocity ${velocity}`;
            ticker.className = "text-xs font-mono text-rose-400 truncate max-w-md";
        }
    }
}

// --- Studio Recordings Vault (Tab 2 Exclusive) ---

function handleRecordingsLibraryUpdated(recordings) {
    state.recordingsLibrary = recordings || [];
    const countBadge = document.getElementById("recordingsCountBadge");
    if (countBadge) {
        const count = state.recordingsLibrary.length;
        countBadge.textContent = `${count} ${count === 1 ? 'Take' : 'Takes'}`;
    }
    renderRecordingsLibrary();
}

function handleRecordingPreviewStarted(filePath) {
    state.activePreviewRecordingPath = filePath;
    renderRecordingsLibrary();
}

function handleRecordingPreviewStopped() {
    state.activePreviewRecordingPath = null;
    renderRecordingsLibrary();
}

function renderRecordingsLibrary() {
    const container = document.getElementById("recordingsListContainer");
    const emptyState = document.getElementById("recordingsEmptyState");
    if (!container || !emptyState) return;

    container.innerHTML = "";

    const query = (state.recordingsSearchQuery || "").trim().toLowerCase();
    const filtered = state.recordingsLibrary.filter(rec => {
        if (!query) return true;
        return (rec.fileName && rec.fileName.toLowerCase().includes(query)) ||
               (rec.createdAtFormatted && rec.createdAtFormatted.toLowerCase().includes(query));
    });

    if (filtered.length === 0) {
        emptyState.classList.remove("hidden");
        container.classList.add("hidden");
        return;
    }

    emptyState.classList.add("hidden");
    container.classList.remove("hidden");

    filtered.forEach(rec => {
        const isPreviewing = state.activePreviewRecordingPath &&
            state.activePreviewRecordingPath.toLowerCase() === rec.filePath.toLowerCase();

        const card = document.createElement("div");
        card.className = `take-vault-card p-3 rounded-xl flex flex-wrap items-center justify-between gap-3 ${isPreviewing ? 'is-previewing' : ''}`;

        card.innerHTML = `
            <div class="flex items-center space-x-3 min-w-0">
                <div class="w-8 h-8 rounded-lg ${isPreviewing ? 'bg-cyan-500/20 text-cyan-400' : 'bg-slate-800 text-rose-400'} flex items-center justify-center shrink-0 font-mono text-xs">
                    ${isPreviewing ? '▶' : '⏺'}
                </div>
                <div class="min-w-0">
                    <div class="flex items-center space-x-2">
                        <span class="text-xs font-bold text-white truncate max-w-xs sm:max-w-md">${escapeHtml(rec.fileName)}</span>
                        ${isPreviewing ? '<span class="px-1.5 py-0.2 rounded text-[9px] font-semibold bg-cyan-950 text-cyan-400 border border-cyan-800/60 animate-pulse">Auditioning</span>' : ''}
                    </div>
                    <div class="flex items-center space-x-3 text-[11px] text-slate-400 mt-0.5">
                        <span>${rec.createdAtFormatted || ''}</span>
                        <span>&bull;</span>
                        <span class="font-mono text-cyan-400 font-semibold">${rec.durationFormatted || '00:00'}</span>
                        <span>&bull;</span>
                        <span class="font-mono text-emerald-400">${rec.noteCount || 0} notes</span>
                        <span>&bull;</span>
                        <span class="text-slate-500">${rec.fileSizeFormatted || ''}</span>
                    </div>
                </div>
            </div>

            <!-- Action Buttons -->
            <div class="flex items-center space-x-1.5 shrink-0">
                <!-- Preview / Stop Toggle -->
                <button onclick="togglePreviewRecording('${encodeURIComponent(rec.filePath)}')" title="${isPreviewing ? 'Stop Preview' : 'Audition Take'}" class="px-2.5 py-1 rounded-lg ${isPreviewing ? 'bg-rose-600 hover:bg-rose-500 text-white' : 'bg-slate-800 hover:bg-cyan-600 text-slate-200 hover:text-white'} text-[11px] font-semibold border border-slate-700 transition active:scale-95 flex items-center space-x-1">
                    <span>${isPreviewing ? '⏹ Stop' : '▶ Preview'}</span>
                </button>

                <!-- Export to GM Library (Reverse-remapped) -->
                <button onclick="exportRecordingToGm('${encodeURIComponent(rec.filePath)}', '${escapeHtml(rec.fileName)}')" title="Export to General MIDI Library: Reverse-remaps to standard GM and publishes to GM Midi folder" class="px-2.5 py-1 rounded-lg bg-indigo-950/80 hover:bg-indigo-900 text-[11px] font-semibold text-indigo-300 hover:text-white border border-indigo-800/60 transition active:scale-95 flex items-center space-x-1">
                    <span>🎵 GM Export</span>
                </button>

                <!-- Export to PSS Library (Native) -->
                <button onclick="exportRecordingToPss('${encodeURIComponent(rec.filePath)}', '${escapeHtml(rec.fileName)}')" title="Export to ${state.activeModel === 'pss680' ? 'PSS-680' : 'PSS-790'} Library: Stores in native PSS Midi folder for immediate hardware playback" class="px-2.5 py-1 rounded-lg bg-emerald-950/80 hover:bg-emerald-900 text-[11px] font-semibold text-emerald-300 hover:text-white border border-emerald-800/60 transition active:scale-95 flex items-center space-x-1">
                    <span>⚡ PSS Export</span>
                </button>

                <!-- Rename Take -->
                <button onclick="promptRenameRecording('${encodeURIComponent(rec.filePath)}', '${escapeHtml(rec.fileName)}')" title="Rename Take" class="p-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-white border border-slate-700/60 transition active:scale-95">
                    <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"/>
                    </svg>
                </button>

                <!-- Delete Take -->
                <button onclick="confirmDeleteRecording('${encodeURIComponent(rec.filePath)}', '${escapeHtml(rec.fileName)}')" title="Delete Take" class="p-1 rounded-lg bg-slate-800 hover:bg-rose-900/60 text-slate-400 hover:text-rose-300 border border-slate-700/60 transition active:scale-95">
                    <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"/>
                    </svg>
                </button>
            </div>
        `;

        container.appendChild(card);
    });
}

function togglePreviewRecording(encodedPath) {
    const filePath = decodeURIComponent(encodedPath);
    if (state.activePreviewRecordingPath && state.activePreviewRecordingPath.toLowerCase() === filePath.toLowerCase()) {
        sendToHost("stop_recording_preview");
        handleRecordingPreviewStopped();
    } else {
        sendToHost("preview_recording", { filePath: filePath });
    }
}

function exportRecordingToGm(encodedPath, fileName) {
    const filePath = decodeURIComponent(encodedPath);
    sendToHost("export_recording_to_gm", { filePath: filePath });
    showToast(`Exporting ${fileName} to General MIDI Library...`);
}

function exportRecordingToPss(encodedPath, fileName) {
    const filePath = decodeURIComponent(encodedPath);
    sendToHost("export_recording_to_pss", { filePath: filePath });
    const pssName = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";
    showToast(`Exporting ${fileName} to ${pssName} Library...`);
}

function promptRenameRecording(encodedPath, oldName) {
    const filePath = decodeURIComponent(encodedPath);
    const baseName = oldName.replace(/\.midi?$/i, '');
    const newName = prompt("Enter new filename for recording take:", baseName);
    if (newName && newName.trim() && newName.trim() !== baseName) {
        sendToHost("rename_recording", { filePath: filePath, newName: newName.trim() });
    }
}

function confirmDeleteRecording(encodedPath, fileName) {
    const filePath = decodeURIComponent(encodedPath);
    if (confirm(`Are you sure you want to delete recording take "${fileName}"? This cannot be undone.`)) {
        sendToHost("delete_recording", { filePath: filePath });
    }
}

function updateRecorderState(recState, countInBeat, bpm) {
    state.recorderState = recState;
    const btnRecord = document.getElementById("btnRecord");
    const recText = document.getElementById("recordButtonText");
    const recDot = document.getElementById("recordIconDot");
    const banner = document.getElementById("countInBanner");
    const countInNum = document.getElementById("countInNumber");
    const pill = document.getElementById("recordingPill");
    const statusLabel = document.getElementById("recStatusLabel");

    if (recState === "countin") {
        if (banner) banner.classList.remove("hidden");
        if (countInNum) countInNum.textContent = countInBeat;
        if (statusLabel) statusLabel.textContent = `Count-In (${countInBeat}/4)`;
        if (pill) pill.classList.remove("hidden");
        playMetronomeClick(countInBeat === 1);
    } else if (recState === "recording") {
        if (banner) banner.classList.add("hidden");
        if (btnRecord) {
            btnRecord.className = "flex items-center space-x-3 px-8 py-4 rounded-2xl bg-rose-600 text-white font-bold text-sm shadow-xl record-btn-pulsing transition-all duration-200 hover:scale-105 active:scale-95";
        }
        if (recText) recText.textContent = "Stop Recording";
        if (recDot) recDot.className = "w-4 h-4 rounded-sm bg-white shadow-sm"; // Square stop symbol
        if (pill) pill.classList.remove("hidden");
        if (statusLabel) statusLabel.textContent = "Recording Live";
        document.getElementById("takeSummaryCard").classList.add("hidden");
    } else { // Idle or Stopped
        if (banner) banner.classList.add("hidden");
        if (btnRecord) {
            btnRecord.className = "flex items-center space-x-3 px-8 py-4 rounded-2xl bg-rose-600 hover:bg-rose-500 active:bg-rose-700 text-white font-bold text-sm shadow-xl shadow-rose-600/30 transition-all duration-200 hover:scale-105 active:scale-95";
        }
        if (recText) recText.textContent = "Start Recording";
        if (recDot) recDot.className = "w-4 h-4 rounded-full bg-white shadow-sm"; // Circle record symbol
        if (pill) pill.classList.add("hidden");
        if (statusLabel) statusLabel.textContent = recState === "stopped" ? "Take Completed" : "Idle";
    }
}

function updateRecorderTick(data) {
    const timerDisplay = document.getElementById("recorderTimerDisplay");
    const beatDisplay = document.getElementById("recorderBeatDisplay");
    const noteCounter = document.getElementById("recNoteCounter");
    const eventCounter = document.getElementById("recEventCounter");
    const metroLed = document.getElementById("metroLed");

    if (timerDisplay) {
        const totalSec = data.elapsedSeconds;
        const mins = Math.floor(totalSec / 60);
        const secs = Math.floor(totalSec % 60);
        const tenths = Math.floor((totalSec % 1) * 10);
        timerDisplay.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}.${tenths}`;
    }

    if (beatDisplay) {
        beatDisplay.textContent = `BAR ${data.bar.toString().padStart(2, '0')} : ${data.beat}`;
    }

    if (noteCounter) noteCounter.textContent = data.noteCount;
    if (eventCounter) eventCounter.textContent = data.eventCount;

    // Metronome Click & Visual Pulse on Beat Change
    if (data.beat !== state.lastBeat) {
        state.lastBeat = data.beat;
        playMetronomeClick(data.beat === 1);

        if (metroLed) {
            metroLed.style.backgroundColor = data.beat === 1 ? "#f59e0b" : "#06b6d4";
            metroLed.style.boxShadow = data.beat === 1 ? "0 0 12px #f59e0b" : "0 0 10px #06b6d4";
            setTimeout(() => {
                metroLed.style.backgroundColor = "";
                metroLed.style.boxShadow = "";
            }, 80);
        }
    }
}

function showTakeReadyCard(stats) {
    const card = document.getElementById("takeSummaryCard");
    card.classList.remove("hidden");

    document.getElementById("takeSummaryDuration").textContent = stats.durationFormatted || "00:00";
    document.getElementById("takeSummaryNotes").textContent = stats.noteCount || 0;
    document.getElementById("takeSummaryEvents").textContent = stats.eventCount || 0;

    // Set Default Take Name
    const now = new Date();
    const pad = (n) => n.toString().padStart(2, '0');
    const autoTitle = `Take_${now.getFullYear()}${pad(now.getMonth()+1)}${pad(now.getDate())}_${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
    document.getElementById("takeFilenameInput").value = autoTitle;

    // Enable Preview Buttons
    document.getElementById("btnPreviewTake").disabled = false;
    document.getElementById("btnStopPreviewTake").disabled = false;
}

function updatePlaybackUi() {
    const playIcon = document.getElementById("playIcon");
    const pauseIcon = document.getElementById("pauseIcon");
    const scrubber = document.getElementById("timeScrubber");
    const currTime = document.getElementById("currentTimeDisplay");
    const totalTime = document.getElementById("totalTimeDisplay");

    if (state.isPlaying) {
        playIcon.classList.add("hidden");
        pauseIcon.classList.remove("hidden");
    } else {
        playIcon.classList.remove("hidden");
        pauseIcon.classList.add("hidden");
    }

    if (scrubber && !scrubber.matches(":active")) {
        if (state.durationSeconds > 0) {
            scrubber.max = state.durationSeconds;
        }
        scrubber.value = state.currentSeconds;
    }

    if (currTime) {
        const mins = Math.floor(state.currentSeconds / 60);
        const secs = Math.floor(state.currentSeconds % 60);
        currTime.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }

    if (totalTime && state.durationSeconds > 0) {
        const mins = Math.floor(state.durationSeconds / 60);
        const secs = Math.floor(state.durationSeconds % 60);
        totalTime.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }

    const statusBadge = document.getElementById("fileStatusBadge");
    if (statusBadge) {
        if (state.isPlaying) {
            statusBadge.className = "px-2 py-0.5 rounded-full bg-emerald-950/80 text-emerald-400 border border-emerald-800/60 font-semibold text-[10px] cursor-help flex items-center space-x-1 shadow-sm shadow-emerald-950/30";
            statusBadge.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span><span>Playing</span>`;
        } else if (state.isPaused) {
            statusBadge.className = "px-2 py-0.5 rounded-full bg-amber-950/80 text-amber-400 border border-amber-800/60 font-semibold text-[10px] cursor-help flex items-center space-x-1 shadow-sm shadow-amber-950/30";
            statusBadge.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-amber-400"></span><span>Paused</span>`;
        } else if (state.currentFile) {
            statusBadge.className = "px-2 py-0.5 rounded-full bg-slate-800 text-slate-300 font-medium text-[10px] cursor-help flex items-center space-x-1 border border-slate-700/60";
            statusBadge.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-slate-400"></span><span>Ready</span>`;
        } else {
            statusBadge.className = "px-2 py-0.5 rounded-full bg-slate-800 text-slate-400 font-medium text-[10px] cursor-help flex items-center space-x-1 border border-slate-700/60";
            statusBadge.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-slate-500"></span><span>Idle</span>`;
        }
    }
}

function updateBatchProgress(data) {
    const container = document.getElementById("batchProgressContainer");
    container.classList.remove("hidden");
    document.getElementById("batchCurrentFile").textContent = data.fileName;
    document.getElementById("batchPercent").textContent = `${data.percent}%`;
    document.getElementById("batchProgressBar").style.width = `${data.percent}%`;
}

function showToast(message, isError = false) {
    const toast = document.getElementById("notificationToast");
    const msg = document.getElementById("notificationMessage");
    msg.textContent = message;

    if (isError) {
        toast.className = "fixed bottom-6 right-6 z-50 px-4 py-3 rounded-xl bg-slate-900 border border-rose-500/80 text-xs text-rose-200 shadow-2xl flex items-center space-x-2 transition-all duration-300";
    } else {
        toast.className = "fixed bottom-6 right-6 z-50 px-4 py-3 rounded-xl bg-slate-900 border border-cyan-500/40 text-xs text-white shadow-2xl flex items-center space-x-2 transition-all duration-300";
    }

    toast.classList.remove("translate-y-20", "opacity-0");
    setTimeout(() => {
        toast.classList.add("translate-y-20", "opacity-0");
    }, 4000);
}

// --- Event Listeners Initialization ---

// Workspace Tabs
document.getElementById("tabBtnPlayer").addEventListener("click", () => switchTab("player"));
document.getElementById("tabBtnRecorder").addEventListener("click", () => switchTab("recorder"));
document.getElementById("tabBtnSysex")?.addEventListener("click", () => switchTab("sysex"));

// Primary Hardware Output Select
document.getElementById("deviceSelect").addEventListener("change", (e) => {
    state.selectedDevice = e.target.value;
    sendToHost("select_device", { deviceName: e.target.value });
});

document.getElementById("deviceSelect")?.addEventListener("focus", () => {
    sendToHost("rescan_devices");
});

document.getElementById("btnRefreshDevices")?.addEventListener("click", () => {
    sendToHost("rescan_devices");
    showToast("Rescanning MIDI ports...");
});

// Library Selector
document.getElementById("librarySelect").addEventListener("change", (e) => {
    if (e.target.value) {
        sendToHost("load_file", { filePath: e.target.value });
    }
});

// Browse File
document.getElementById("btnBrowse").addEventListener("click", () => {
    sendToHost("browse_file");
});

// Open Explorer Folder
document.getElementById("btnOpenFolder").addEventListener("click", () => {
    sendToHost("open_explorer");
});

// Transport Controls (Player)
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
    const val = parseFloat(e.target.value);
    const currTime = document.getElementById("currentTimeDisplay");
    if (currTime) {
        const mins = Math.floor(val / 60);
        const secs = Math.floor(val % 60);
        currTime.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
});

document.getElementById("timeScrubber").addEventListener("change", (e) => {
    sendToHost("seek", { seconds: parseFloat(e.target.value) });
});

// Timeline Scrubber Hover Tooltip
const scrubberEl = document.getElementById("timeScrubber");
const scrubTooltip = document.getElementById("scrubTooltip");
if (scrubberEl && scrubTooltip) {
    const updateScrubTooltip = (e) => {
        if (!state.durationSeconds || state.durationSeconds <= 0) return;
        const rect = scrubberEl.getBoundingClientRect();
        const clientX = e.clientX !== undefined ? e.clientX : (e.touches && e.touches[0] ? e.touches[0].clientX : 0);
        const frac = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
        const targetSec = frac * state.durationSeconds;
        const mins = Math.floor(targetSec / 60);
        const secs = Math.floor(targetSec % 60);
        scrubTooltip.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
        scrubTooltip.style.left = `${(frac * 100).toFixed(1)}%`;
        scrubTooltip.classList.remove("hidden");
    };

    scrubberEl.addEventListener("mouseenter", () => {
        if (state.durationSeconds > 0) scrubTooltip.classList.remove("hidden");
    });
    scrubberEl.addEventListener("mousemove", updateScrubTooltip);
    scrubberEl.addEventListener("mouseleave", () => {
        scrubTooltip.classList.add("hidden");
    });
}

// Speed Controls
document.querySelectorAll(".speed-btn").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".speed-btn").forEach(b => {
            b.className = "speed-btn px-1.5 py-0.5 text-[11px] font-semibold rounded text-slate-400 hover:text-white transition active:scale-95";
        });
        btn.className = "speed-btn px-1.5 py-0.5 text-[11px] font-semibold rounded bg-cyan-500 text-slate-950 shadow-sm transition active:scale-95";
        state.speed = parseFloat(btn.dataset.speed);
        sendToHost("set_speed", { speed: state.speed });
    });
});

document.getElementById("btnLoop").addEventListener("click", () => {
    state.isLooping = !state.isLooping;
    const btn = document.getElementById("btnLoop");
    if (state.isLooping) {
        btn.className = "px-2 py-1 rounded-lg bg-cyan-500/25 text-cyan-400 border border-cyan-500/50 text-[11px] font-semibold transition flex items-center space-x-1 shadow-sm ring-1 ring-cyan-500/30 active:scale-95";
    } else {
        btn.className = "px-2 py-1 rounded-lg bg-slate-800/80 hover:bg-slate-700 text-[11px] font-medium text-slate-400 hover:text-white border border-slate-700/60 transition flex items-center space-x-1 active:scale-95";
    }
    sendToHost("set_loop", { loop: state.isLooping });
});

document.getElementById("btnPrevTrack")?.addEventListener("click", playPrevSong);
document.getElementById("btnNextTrack")?.addEventListener("click", playNextSong);
document.getElementById("btnAutoAdvance")?.addEventListener("click", () => toggleAutoAdvance());

// Master Transpose Controls
const btnTransDown = document.getElementById("btnTransposeDown");
if (btnTransDown) {
    btnTransDown.addEventListener("click", () => {
        const cur = state.masterTranspose || 0;
        const next = Math.max(-12, cur - 1);
        state.masterTranspose = next;
        updateMasterTransposeDisplay();
        sendToHost("set_master_transpose", { transpose: next });
    });
}

const btnTransUp = document.getElementById("btnTransposeUp");
if (btnTransUp) {
    btnTransUp.addEventListener("click", () => {
        const cur = state.masterTranspose || 0;
        const next = Math.min(12, cur + 1);
        state.masterTranspose = next;
        updateMasterTransposeDisplay();
        sendToHost("set_master_transpose", { transpose: next });
    });
}

const lblTrans = document.getElementById("lblMasterTranspose");
if (lblTrans) {
    lblTrans.addEventListener("click", () => {
        state.masterTranspose = 0;
        updateMasterTransposeDisplay();
        sendToHost("set_master_transpose", { transpose: 0 });
    });
}

document.getElementById("polyphonyGuardToggle").addEventListener("change", (e) => {
    state.polyphonyGuard = e.target.checked;
    sendToHost("toggle_polyphony_guard", { enabled: state.polyphonyGuard });
    showToast(`Smart Polyphony Guard: ${state.polyphonyGuard ? 'Enabled' : 'Disabled'}`);
});

document.getElementById("btnSaveRemapped").addEventListener("click", () => {
    sendToHost("save_remapped");
});

document.getElementById("btnResetOverrides")?.addEventListener("click", () => {
    sendToHost("reset_voice_overrides");
});

document.getElementById("btnSoloDrums")?.addEventListener("click", () => {
    const drumCh = state.channels[15];
    if (!drumCh) return;
    const isSoloed = drumCh.isSolo;
    state.channels.forEach(ch => {
        if (ch.channel === 15) {
            ch.isSolo = !isSoloed;
            ch.isMuted = false;
            sendToHost("set_channel_mute", { channel: ch.channel, mute: false });
        } else {
            ch.isSolo = false;
            ch.isMuted = !isSoloed;
            sendToHost("set_channel_mute", { channel: ch.channel, mute: !isSoloed });
        }
    });
    renderMixer();
    showToast(!isSoloed ? "Soloed Percussion (Channel 16)" : "Restored full mixer playback");
});

document.getElementById("btnMuteMelodic")?.addEventListener("click", () => {
    const melodicChannels = state.channels.filter(ch => ch.channel !== 15);
    const allMuted = melodicChannels.every(ch => ch.isMuted);
    melodicChannels.forEach(ch => {
        ch.isMuted = !allMuted;
        sendToHost("set_channel_mute", { channel: ch.channel, mute: !allMuted });
    });
    renderMixer();
    showToast(!allMuted ? "Muted Melodic Channels 1-15 (Drums only)" : "Unmuted Melodic Channels");
});

document.getElementById("btnUnmuteAll")?.addEventListener("click", () => {
    state.channels.forEach(ch => {
        ch.isMuted = false;
        ch.isSolo = false;
        sendToHost("set_channel_mute", { channel: ch.channel, mute: false });
    });
    renderMixer();
    showToast("Unmuted all 16 channels");
});

document.getElementById("btnResetAllFaders")?.addEventListener("click", () => {
    state.channels.forEach(ch => {
        ch.volume = 100;
        sendToHost("set_channel_volume", { channel: ch.channel, volume: 100 });
    });
    renderMixer();
    showToast("Reset all 16 channel faders to 100");
});

// --- Quick-Pick Sidebar Event Listeners ---

const btnToggleSidebar = document.getElementById("btnToggleSidebar");
if (btnToggleSidebar) {
    btnToggleSidebar.addEventListener("click", toggleSidebar);
}

const btnCloseSidebar = document.getElementById("btnCloseSidebar");
if (btnCloseSidebar) {
    btnCloseSidebar.addEventListener("click", toggleSidebar);
}

const btnCollapseAll = document.getElementById("btnCollapseAll");
if (btnCollapseAll) {
    btnCollapseAll.addEventListener("click", toggleCollapseAllFolders);
}

const btnAutoFitSidebar = document.getElementById("btnAutoFitSidebar");
if (btnAutoFitSidebar) {
    btnAutoFitSidebar.addEventListener("click", () => {
        state.isAutoFitSidebar = true;
        try {
            localStorage.setItem("pss790_sidebar_autofit", "true");
        } catch (e) {}
        autoFitSidebarWidth(true);
        showToast("Library width auto-fitted to content");
    });
}

function initSidebarResizer() {
    const resizer = document.getElementById("sidebarResizer");
    const sidebar = document.getElementById("quickPickSidebar");
    if (!resizer || !sidebar) return;

    let isDragging = false;
    let startX = 0;
    let startWidth = 280;

    resizer.addEventListener("mousedown", (e) => {
        if (e.button !== 0) return; // Only left mouse button
        isDragging = true;
        startX = e.clientX;
        startWidth = sidebar.getBoundingClientRect().width;

        document.body.classList.add("is-resizing-sidebar");
        e.preventDefault();
    });

    window.addEventListener("mousemove", (e) => {
        if (!isDragging) return;
        const dx = e.clientX - startX;
        const minW = 260;
        const maxW = Math.min(720, Math.floor(window.innerWidth * 0.55));
        const newWidth = Math.max(minW, Math.min(startWidth + dx, maxW));

        sidebar.style.width = `${newWidth}px`;
        state.sidebarWidth = newWidth;
    });

    window.addEventListener("mouseup", () => {
        if (!isDragging) return;
        isDragging = false;
        document.body.classList.remove("is-resizing-sidebar");

        // When user manually drags to a preferred width, preserve it and pause auto-fit until requested
        state.isAutoFitSidebar = false;
        try {
            localStorage.setItem("pss790_sidebar_width", state.sidebarWidth);
            localStorage.setItem("pss790_sidebar_autofit", "false");
        } catch (e) {}
    });

    // Double-click divider: Resume auto-fit and immediately auto-fit to content!
    resizer.addEventListener("dblclick", () => {
        state.isAutoFitSidebar = true;
        try {
            localStorage.setItem("pss790_sidebar_autofit", "true");
        } catch (e) {}
        autoFitSidebarWidth(true);
        showToast("Library width auto-fitted to content");
    });
}

const btnToggleSelectMode = document.getElementById("btnToggleSelectMode");
if (btnToggleSelectMode) {
    btnToggleSelectMode.addEventListener("click", toggleSelectMode);
}

document.getElementById("btnBatchSelectAll")?.addEventListener("click", selectAllVisible);
document.getElementById("btnBatchClear")?.addEventListener("click", clearSelection);

document.getElementById("btnBatchMove")?.addEventListener("click", () => {
    if (state.selectedSongs.size > 0) {
        openActionModal("batch_move", { paths: Array.from(state.selectedSongs) });
    }
});

document.getElementById("btnBatchDelete")?.addEventListener("click", () => {
    if (state.selectedSongs.size > 0) {
        openActionModal("batch_delete", { paths: Array.from(state.selectedSongs) });
    }
});

const sidebarSearchInput = document.getElementById("sidebarSearchInput");
const btnClearSearch = document.getElementById("btnClearSearch");

if (sidebarSearchInput) {
    sidebarSearchInput.addEventListener("input", (e) => {
        state.searchQuery = e.target.value.trim();
        if (state.searchQuery.length > 0) {
            if (btnClearSearch) btnClearSearch.classList.remove("hidden");
        } else {
            if (btnClearSearch) btnClearSearch.classList.add("hidden");
        }
        renderLibraryTree();
    });
}

if (btnClearSearch) {
    btnClearSearch.addEventListener("click", () => {
        clearSearchFilter();
    });
}

const btnSidebarRefresh = document.getElementById("btnSidebarRefresh");
if (btnSidebarRefresh) {
    btnSidebarRefresh.addEventListener("click", () => {
        sendToHost("refresh_library");
        showToast("Scanning MIDI library...");
    });
}

// --- Library Context Menu & Action Modal Event Listeners ---

document.getElementById("ctxItemNewFolder")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget) openActionModal("new_folder", activeContextTarget);
});

document.getElementById("ctxItemMove")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget) openActionModal("move", activeContextTarget);
});

document.getElementById("ctxItemRename")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget) openActionModal("rename", activeContextTarget);
});

document.getElementById("ctxItemReveal")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget && activeContextTarget.fullPath) {
        sendToHost("reveal_in_explorer", { fullPath: activeContextTarget.fullPath });
    }
});

document.getElementById("ctxItemFavorite")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget && (activeContextTarget.fullPath || activeContextTarget.name)) {
        sendToHost("toggle_favorite", { path: activeContextTarget.fullPath || activeContextTarget.name });
    }
});

document.getElementById("btnToggleFavoriteCurrent")?.addEventListener("click", () => {
    if (state.currentFilePath) {
        sendToHost("toggle_favorite", { path: state.currentFilePath });
    } else {
        showToast("No song currently loaded to favourite");
    }
});

document.getElementById("ctxItemDelete")?.addEventListener("click", () => {
    hideContextMenu();
    if (activeContextTarget) openActionModal("delete", activeContextTarget);
});

document.getElementById("btnCloseActionModal")?.addEventListener("click", closeActionModal);
document.getElementById("btnCancelActionModal")?.addEventListener("click", closeActionModal);
document.getElementById("btnConfirmActionModal")?.addEventListener("click", confirmActionModal);

document.getElementById("fileActionModal")?.addEventListener("click", (e) => {
    if (e.target.id === "fileActionModal") closeActionModal();
});

// About & Legal Notices Modal Event Listeners
document.getElementById("btnAbout")?.addEventListener("click", openAboutModal);
document.getElementById("btnCloseAboutModal")?.addEventListener("click", closeAboutModal);
document.getElementById("btnDismissAboutModal")?.addEventListener("click", closeAboutModal);
document.getElementById("aboutModal")?.addEventListener("click", (e) => {
    if (e.target.id === "aboutModal") closeAboutModal();
});

// DAW Keyboard Shortcuts Modal Event Listeners
document.getElementById("btnShortcuts")?.addEventListener("click", openShortcutsModal);
document.getElementById("btnCloseShortcutsModal")?.addEventListener("click", closeShortcutsModal);
document.getElementById("btnDismissShortcutsModal")?.addEventListener("click", closeShortcutsModal);
document.getElementById("shortcutsModal")?.addEventListener("click", (e) => {
    if (e.target.id === "shortcutsModal") closeShortcutsModal();
});
document.getElementById("btnToggleHandheldMode")?.addEventListener("click", () => toggleHandheldMode());
document.getElementById("btnModalToggleHandheld")?.addEventListener("click", () => toggleHandheldMode());

// Dismiss context menu on click outside, blur, or resize
document.addEventListener("click", (e) => {
    const menu = document.getElementById("libraryContextMenu");
    if (menu && !menu.contains(e.target)) {
        hideContextMenu();
    }
});
window.addEventListener("blur", hideContextMenu);
window.addEventListener("resize", hideContextMenu);

// --- MIDI Studio / Recorder Event Listeners ---

document.getElementById("recorderInputDeviceSelect").addEventListener("change", (e) => {
    state.selectedInputDevice = e.target.value;
    sendToHost("select_input_device", { deviceName: e.target.value });
});

document.getElementById("recorderInputDeviceSelect")?.addEventListener("focus", () => {
    sendToHost("rescan_devices");
});

document.getElementById("btnRefreshInputs").addEventListener("click", () => {
    sendToHost("refresh_input_devices");
});

document.getElementById("chkMidiThru").addEventListener("change", (e) => {
    sendToHost("set_midi_thru", { enabled: e.target.checked });
    showToast(`MIDI Thru Forward: ${e.target.checked ? 'Active (Caution: Loopback)' : 'Disabled'}`);
});

// BPM Slider & Input sync
const bpmInput = document.getElementById("recorderBpmInput");
const bpmSlider = document.getElementById("recorderBpmSlider");

bpmInput.addEventListener("change", (e) => {
    let bpm = parseInt(e.target.value);
    if (isNaN(bpm)) bpm = 120;
    bpm = Math.max(40, Math.min(240, bpm));
    bpmInput.value = bpm;
    bpmSlider.value = bpm;
    state.recorderBpm = bpm;
});

bpmSlider.addEventListener("input", (e) => {
    bpmInput.value = e.target.value;
    state.recorderBpm = parseInt(e.target.value);
});

// Record Button
document.getElementById("btnRecord").addEventListener("click", () => {
    if (state.recorderState === "recording" || state.recorderState === "countin") {
        sendToHost("stop_recording");
    } else {
        const countIn = document.getElementById("chkCountIn").checked;
        sendToHost("start_recording", { bpm: state.recorderBpm, countIn: countIn });
    }
});

// Audition Preview
document.getElementById("btnPreviewTake").addEventListener("click", () => {
    sendToHost("preview_take");
});

document.getElementById("btnStopPreviewTake").addEventListener("click", () => {
    sendToHost("stop_preview");
});

// Take Actions
document.getElementById("btnSaveTakePss790").addEventListener("click", () => {
    const fn = document.getElementById("takeFilenameInput").value.trim() || "Take_Recording";
    sendToHost("save_take", { fileName: fn, isNativePss: true });
});

document.getElementById("btnSaveTakeGm").addEventListener("click", () => {
    const fn = document.getElementById("takeFilenameInput").value.trim() || "Take_Recording";
    sendToHost("save_take", { fileName: fn, isNativePss: false });
});

document.getElementById("btnSendTakeToPlayer").addEventListener("click", () => {
    const fn = document.getElementById("takeFilenameInput").value.trim() || "Recorded Take";
    sendToHost("send_take_to_player", { title: fn });
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
    const src = document.getElementById("batchSourceDir")?.value || "";
    const target790Dir = document.getElementById("batchTarget790Dir")?.value || "";
    const target680Dir = document.getElementById("batchTarget680Dir")?.value || "";

    const convert790 = document.getElementById("chkBatchEnable790")?.checked ?? true;
    const convert680 = document.getElementById("chkBatchEnable680")?.checked ?? true;

    // PSS-790 Safeguards
    const polyphonyGuard790 = document.getElementById("chkBatchPolyphony790")?.checked ?? true;
    const reverbBoost790 = document.getElementById("chkBatchReverb790")?.checked ?? true;
    const velocityNorm790 = document.getElementById("chkBatchVelocityNorm790")?.checked ?? true;
    const sustain790 = document.getElementById("chkBatchSustain790")?.checked ?? true;
    const expressionFold790 = document.getElementById("chkBatchExpressionFold790")?.checked ?? true;
    const pitchTune790 = document.getElementById("chkBatchPitchTune790")?.checked ?? true;
    const preRoll790 = document.getElementById("chkBatchPreRoll790")?.checked ?? true;

    // PSS-680 Safeguards
    const polyphonyGuard680 = document.getElementById("chkBatchPolyphony680")?.checked ?? true;
    const cleanControllers680 = document.getElementById("chkBatchCleanControllers680")?.checked ?? true;
    const fmVelocity680 = document.getElementById("chkBatchFmVelocity680")?.checked ?? true;
    const drums680 = document.getElementById("chkBatchDrums680")?.checked ?? true;
    const sustain680 = document.getElementById("chkBatchSustain680")?.checked ?? true;
    const expressionFold680 = document.getElementById("chkBatchExpressionFold680")?.checked ?? true;
    const pitchTune680 = document.getElementById("chkBatchPitchTune680")?.checked ?? true;

    if (!convert790 && !convert680) {
        showToast("Please enable at least one target keyboard (PSS-790 or PSS-680).");
        return;
    }

    document.getElementById("btnStartBatch").disabled = true;
    sendToHost("start_batch", {
        sourceDir: src,
        target790Dir,
        target680Dir,
        convert790,
        convert680,
        polyphonyGuard790,
        reverbBoost790,
        velocityNorm790,
        sustain790,
        expressionFold790,
        pitchTune790,
        preRoll790,
        polyphonyGuard680,
        cleanControllers680,
        fmVelocity680,
        drums680,
        sustain680,
        expressionFold680,
        pitchTune680
    });
});

// Library Sync Modal Handlers
const syncModal = document.getElementById("syncModal");
const btnSidebarSync = document.getElementById("btnSidebarSync");

function openSyncModal() {
    if (!syncModal) return;
    syncModal.classList.remove("hidden");
    const banner = document.getElementById("syncStatusBanner");
    if (banner) banner.className = "p-3 rounded-xl border flex items-center space-x-3 bg-slate-900/80 border-slate-800 text-xs text-slate-300";
    document.getElementById("syncStatusIcon").textContent = "🔍";
    document.getElementById("syncStatusHeadline").textContent = "Analyzing library folders...";
    document.getElementById("syncStatusSubtext").textContent = "Comparing files across GM Midi, PSS-790 Midi, and PSS-680 Midi...";
    document.getElementById("syncProgressContainer")?.classList.add("hidden");
    const btnStart = document.getElementById("btnStartSync");
    if (btnStart) {
        btnStart.disabled = false;
        btnStart.textContent = "Align & Sync Now";
    }
    sendToHost("analyze_library_sync");
}

function closeSyncModal() {
    if (!syncModal) return;
    syncModal.classList.add("hidden");
    sendToHost("cancel_library_sync");
}

function handleLibrarySyncAnalysis(data) {
    const missingEl = document.getElementById("syncMetricMissing");
    const outdatedEl = document.getElementById("syncMetricOutdated");
    const orphansEl = document.getElementById("syncMetricOrphans");
    const headline = document.getElementById("syncStatusHeadline");
    const subtext = document.getElementById("syncStatusSubtext");
    const icon = document.getElementById("syncStatusIcon");
    const banner = document.getElementById("syncStatusBanner");
    const btnStart = document.getElementById("btnStartSync");
    const xgBanner = document.getElementById("syncXgBanner");
    const xgText = document.getElementById("syncXgBannerSubtext");

    if (missingEl) missingEl.textContent = data.missingCount || 0;
    if (outdatedEl) outdatedEl.textContent = data.outdatedCount || 0;
    if (orphansEl) orphansEl.textContent = data.orphanCount || 0;

    if (xgBanner) {
        if (data.xgUpgradesAvailable && data.xgUpgradesAvailable > 0) {
            xgBanner.classList.remove("hidden");
            if (xgText) {
                xgText.textContent = `${data.xgUpgradesAvailable} legacy converted Yamaha XG song(s) will be upgraded to the latest conversion engine (extended percussion & SFX).`;
            }
        } else {
            xgBanner.classList.add("hidden");
        }
    }

    if (data.isInSync) {
        if (icon) icon.textContent = "✅";
        if (headline) headline.textContent = "Library folders are in 100% parity!";
        if (subtext) subtext.textContent = `All ${data.totalGm} GM songs are matched and synchronized across both PSS-790 and PSS-680 libraries with 0 orphans.`;
        if (banner) banner.className = "p-3 rounded-xl border flex items-center space-x-3 bg-emerald-950/40 border-emerald-500/40 text-xs text-emerald-200";
        if (btnStart) btnStart.textContent = "Re-Verify / Force Sync";
    } else {
        if (icon) icon.textContent = "⚡";
        let titleParts = [];
        if (data.missingCount > 0) titleParts.push(`${data.missingCount} missing`);
        if (data.outdatedCount > 0) {
            if (data.xgUpgradesAvailable && data.xgUpgradesAvailable > 0) {
                titleParts.push(`${data.outdatedCount} outdated (${data.xgUpgradesAvailable} XG upgrades)`);
            } else {
                titleParts.push(`${data.outdatedCount} outdated`);
            }
        }
        if (data.orphanCount > 0) titleParts.push(`${data.orphanCount} orphan`);
        if (titleParts.length === 0) titleParts.push("Updates needed");

        if (headline) headline.textContent = titleParts.join(", ") + " files";
        if (subtext) subtext.textContent = "Review options below and click Align & Sync to synchronize both PSS-790 and PSS-680 libraries.";
        if (banner) banner.className = "p-3 rounded-xl border flex items-center space-x-3 bg-cyan-950/40 border-cyan-500/40 text-xs text-cyan-200";
        if (btnStart) btnStart.textContent = "Align & Sync Now";
    }
}

function updateSyncProgress(data) {
    const container = document.getElementById("syncProgressContainer");
    const currentEl = document.getElementById("syncCurrentFile");
    const percentEl = document.getElementById("syncPercent");
    const barEl = document.getElementById("syncProgressBar");

    if (container) container.classList.remove("hidden");
    if (currentEl) currentEl.textContent = `${data.action}: ${data.fileName} (${data.current}/${data.total})`;
    if (percentEl) percentEl.textContent = `${data.percent}%`;
    if (barEl) barEl.style.width = `${data.percent}%`;
}

function handleSyncCompleted(data) {
    const container = document.getElementById("syncProgressContainer");
    const btnStart = document.getElementById("btnStartSync");
    if (container) container.classList.add("hidden");
    if (btnStart) btnStart.disabled = false;
    if (syncModal) syncModal.classList.add("hidden");

    let toast = `Dual Sync complete! ${data.convertedCount} converted, ${data.updatedCount} updated, ${data.removedOrphansCount} orphans cleaned`;
    if (data.prunedDirectoriesCount && data.prunedDirectoriesCount > 0) {
        toast += `, ${data.prunedDirectoriesCount} empty folders pruned`;
    }
    toast += ` across both PSS-790 and PSS-680 libraries.`;
    showToast(toast);
}

btnSidebarSync?.addEventListener("click", openSyncModal);
document.getElementById("btnCloseSyncModal")?.addEventListener("click", closeSyncModal);
document.getElementById("btnCancelSyncModal")?.addEventListener("click", closeSyncModal);

document.getElementById("btnStartSync")?.addEventListener("click", () => {
    const convertMissing = document.getElementById("chkSyncMissing")?.checked ?? true;
    const updateOutdated = document.getElementById("chkSyncOutdated")?.checked ?? true;
    const removeOrphans = document.getElementById("chkSyncOrphans")?.checked ?? true;
    const pruneEmpty = document.getElementById("chkSyncPruneEmpty")?.checked ?? true;
    const forceReconvertAll = document.getElementById("chkSyncForceAll")?.checked ?? false;

    const btnStart = document.getElementById("btnStartSync");
    if (btnStart) btnStart.disabled = true;

    const container = document.getElementById("syncProgressContainer");
    const currentEl = document.getElementById("syncCurrentFile");
    const percentEl = document.getElementById("syncPercent");
    const barEl = document.getElementById("syncProgressBar");

    if (container) container.classList.remove("hidden");
    if (currentEl) currentEl.textContent = "Starting library sync...";
    if (percentEl) percentEl.textContent = "0%";
    if (barEl) barEl.style.width = "0%";

    sendToHost("execute_library_sync", {
        convertMissing,
        updateOutdated,
        removeOrphans,
        pruneEmpty,
        forceReconvertAll
    });
});

// ==========================================
// MIDI Health Inspector & Diagnostics Handlers
// ==========================================

function renderHealthBadge(health) {
    const container = document.getElementById("healthBadgeContainer");
    if (!container) return;
    if (!health) {
        container.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-slate-800 text-slate-400 border border-slate-700 font-semibold text-[11px] flex items-center space-x-1.5" title="Song Health Status: Load a song to inspect polyphony voice load, pitch bend range, and sustain health"><span class="w-1.5 h-1.5 rounded-full bg-slate-500"></span><span>Health: Standby</span></span>`;
        return;
    }

    let badgeClass = "bg-emerald-950/80 text-emerald-300 border-emerald-600/60 hover:border-emerald-400 shadow-emerald-500/20";
    let dotClass = "bg-emerald-400";
    let icon = "✅";

    if (health.badgeType === "warning") {
        badgeClass = "bg-amber-950/80 text-amber-300 border-amber-600/60 hover:border-amber-400 shadow-amber-500/20";
        dotClass = "bg-amber-400 animate-pulse";
        icon = "⚠️";
    } else if (health.badgeType === "autofixed") {
        badgeClass = "bg-sky-950/80 text-sky-300 border-sky-600/60 hover:border-sky-400 shadow-sky-500/20";
        dotClass = "bg-sky-400";
        icon = "🛡️";
    }

    container.innerHTML = `
        <button id="btnOpenHealthModal" class="px-2 py-0.5 rounded-full ${badgeClass} border font-semibold text-[11px] flex items-center space-x-1.5 shadow-sm transition active:scale-95 cursor-pointer" title="${health.badgeTooltip || 'Click to view detailed MIDI health diagnostics and hardware safeguard audit'}">
            <span class="w-1.5 h-1.5 rounded-full ${dotClass}"></span>
            <span>${icon} ${health.badgeLabel}</span>
            <svg class="w-3 h-3 opacity-70" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
            </svg>
        </button>
    `;

    document.getElementById("btnOpenHealthModal")?.addEventListener("click", () => {
        openHealthDiagnosticsModal(health);
    });
}

function openHealthDiagnosticsModal(health) {
    if (!health) return;
    const modal = document.getElementById("songHealthModal");
    if (!modal) return;

    document.getElementById("healthModalSongTitle").textContent = state.currentFile || "Current Song";

    const is680 = state.activeModel === "pss680";
    const maxVoices = is680 ? 12 : 28;
    const safeVoices = is680 ? 10 : 24;
    const modelName = is680 ? "Yamaha PSS-680" : "Yamaha PSS-790";

    const titleEl = document.getElementById("healthPolyphonyTitle");
    if (titleEl) titleEl.textContent = `${modelName} Polyphony Load`;

    const peak = health.peakPolyphony || 0;
    const peakVoicesEl = document.getElementById("healthPeakVoices");
    const barEl = document.getElementById("healthPolyphonyBar");
    const barContainer = document.getElementById("healthPolyphonyBarContainer");
    const descEl = document.getElementById("healthPolyphonyDescription");

    const voiceTitle = `Peak concurrent note count vs. ${maxVoices}-voice hardware limit of the ${modelName}${is680 ? " (Mode 99)" : ""}`;
    if (peakVoicesEl) {
        peakVoicesEl.textContent = `${peak} / ${maxVoices} Voices`;
        peakVoicesEl.title = voiceTitle;
    }
    if (barContainer) {
        barContainer.title = voiceTitle;
    }

    const percent = Math.min(100, Math.round((peak / maxVoices) * 100));
    if (barEl) barEl.style.width = `${percent}%`;

    if (peak <= safeVoices) {
        if (barEl) barEl.className = "bg-gradient-to-r from-emerald-500 to-cyan-500 h-full rounded-full transition-all duration-300";
        if (peakVoicesEl) peakVoicesEl.className = "font-mono font-bold text-emerald-400";
        if (descEl) descEl.textContent = `Safe peak polyphony. No hardware voice-stealing will occur on ${modelName}${is680 ? " (Mode 99)" : ""}.`;
    } else if (peak <= maxVoices) {
        if (barEl) barEl.className = "bg-gradient-to-r from-cyan-500 to-amber-500 h-full rounded-full transition-all duration-300";
        if (peakVoicesEl) peakVoicesEl.className = "font-mono font-bold text-amber-400";
        if (descEl) descEl.textContent = `Moderate polyphony near the ${maxVoices}-voice hardware limit. Smooth playback guaranteed.`;
    } else {
        if (barEl) barEl.className = "bg-gradient-to-r from-amber-500 to-rose-500 h-full rounded-full transition-all duration-300";
        if (peakVoicesEl) peakVoicesEl.className = "font-mono font-bold text-rose-400";
        if (descEl) descEl.textContent = `High polyphony (${peak} voices). ${is680 ? "Mode 99 " : ""}Polyphony Guard active to preserve lead melody & bass.`;
    }

    const listEl = document.getElementById("healthProtectionsList");
    if (health.autoFixes && health.autoFixes.length > 0) {
        listEl.innerHTML = health.autoFixes.map(fix => `
            <div class="flex items-start space-x-2 bg-slate-900/90 p-2 rounded-lg border border-slate-800">
                <span class="text-sky-400 shrink-0">🛡️</span>
                <span class="text-slate-200">${fix}</span>
            </div>
        `).join("");
    } else {
        listEl.innerHTML = `
            <div class="flex items-center space-x-2 bg-slate-900/90 p-2 rounded-lg border border-slate-800 text-slate-400">
                <span class="text-emerald-400">✅</span>
                <span>Standard General MIDI. No custom repairs or alterations required.</span>
            </div>
        `;
    }

    document.getElementById("healthActiveChannels").textContent = health.activeChannels || 0;
    document.getElementById("healthTotalNotes").textContent = (health.totalNotes || 0).toLocaleString();

    const pitchEl = document.getElementById("healthPitchStatus");
    if (health.microBends > 0 || health.danglingBends > 0) {
        pitchEl.textContent = "Auto-Centered & Tuned";
        pitchEl.className = "font-bold text-sky-400 text-sm";
    } else {
        pitchEl.textContent = "Clean / Neutral";
        pitchEl.className = "font-bold text-emerald-400 text-sm";
    }

    const sustainEl = document.getElementById("healthSustainStatus");
    if (health.stuckPedals > 0) {
        sustainEl.textContent = `${health.stuckPedals} Stuck Pedal(s) Capped`;
        sustainEl.className = "font-bold text-amber-400 text-sm";
    } else {
        sustainEl.textContent = "Natural / Standard";
        sustainEl.className = "font-bold text-emerald-400 text-sm";
    }

    modal.classList.remove("hidden");
}

function handleHealthAuditResult(data) {
    const container = document.getElementById("auditResultsContainer");
    if (!container) return;
    container.classList.remove("hidden");

    document.getElementById("auditSummaryCount").textContent = `${data.totalFiles} files audited`;
    document.getElementById("auditFixesCount").textContent = data.filesWithFixes || 0;
    document.getElementById("auditInferredCount").textContent = (data.filesInferred || 0) + (data.filesXg || 0);
    document.getElementById("auditStuckPedalCount").textContent = data.filesStuckPedal || 0;
    document.getElementById("auditPolyphonyCount").textContent = data.filesHighPolyphony || 0;

    const listEl = document.getElementById("auditFilesList");
    if (listEl && data.files) {
        listEl.innerHTML = data.files.slice(0, 50).map(f => {
            let pillClass = "bg-emerald-950/80 text-emerald-400 border-emerald-800";
            if (f.badgeType === "warning") pillClass = "bg-amber-950/80 text-amber-300 border-amber-800";
            else if (f.autoFixesCount > 0) pillClass = "bg-sky-950/80 text-sky-300 border-sky-800";

            return `
                <div class="flex items-center justify-between bg-slate-900/90 p-1.5 rounded-lg border border-slate-800 text-[11px] hover:border-slate-700 transition">
                    <span class="truncate max-w-[240px] text-slate-200 font-medium" title="${f.fileName}">${f.fileName}</span>
                    <div class="flex items-center space-x-1.5 shrink-0">
                        <span class="font-mono text-[10px] text-slate-400">${f.peakPolyphony}v</span>
                        <span class="px-1.5 py-0.2 rounded text-[10px] font-semibold border ${pillClass}">${f.badgeLabel}</span>
                    </div>
                </div>
            `;
        }).join("");

        if (data.files.length > 50) {
            listEl.innerHTML += `<div class="text-[10px] text-slate-500 text-center pt-1">...and ${data.files.length - 50} more files (all verified)</div>`;
        }
    }
}

document.getElementById("btnRunHealthAudit")?.addEventListener("click", () => {
    const src = document.getElementById("batchSourceDir").value;
    document.getElementById("batchProgressContainer").classList.remove("hidden");
    document.getElementById("batchCurrentFile").textContent = "Starting library health audit...";
    document.getElementById("batchPercent").textContent = "0%";
    document.getElementById("batchProgressBar").style.width = "0%";
    sendToHost("run_health_audit", { directory: src });
});

document.getElementById("btnCloseHealthModal")?.addEventListener("click", () => {
    document.getElementById("songHealthModal")?.classList.add("hidden");
});

document.getElementById("btnDismissHealthModal")?.addEventListener("click", () => {
    document.getElementById("songHealthModal")?.classList.add("hidden");
});

// ==========================================
// SysEx Vault & Bulk Dump Engine Frontend
// ==========================================

function formatBytes(bytes) {
    if (bytes === 0 || !bytes || isNaN(bytes)) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + " " + sizes[i];
}

function renderSysExLibrary(dumps) {
    if (dumps) state.sysexLibrary = dumps;
    const listEl = document.getElementById("sysexLibraryList");
    const countEl = document.getElementById("sysexLibraryCount");
    if (!listEl) return;

    let items = state.sysexLibrary || [];
    if (state.sysexSearchQuery) {
        const q = state.sysexSearchQuery.toLowerCase();
        items = items.filter(d => 
            (d.fileName && d.fileName.toLowerCase().includes(q)) ||
            (d.manufacturer && d.manufacturer.toLowerCase().includes(q)) ||
            (d.createdTime && d.createdTime.toLowerCase().includes(q))
        );
    }

    if (countEl) countEl.textContent = `${items.length} dump${items.length === 1 ? '' : 's'}`;

    if (items.length === 0) {
        listEl.innerHTML = `
            <div class="p-8 text-center text-slate-500 text-xs flex flex-col items-center justify-center space-y-2">
                <svg class="w-8 h-8 text-slate-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4m0 5c0 2.21-3.582 4-8 4s-8-1.79-8-4"/>
                </svg>
                <p>${state.sysexSearchQuery ? 'No SysEx dumps match your search.' : 'No SysEx bulk dumps in library. Click "Listen for Bulk Dump" and press MEMORY BULK DUMP on your PSS-790.'}</p>
            </div>
        `;
        return;
    }

    listEl.innerHTML = items.map((dump) => {
        const isSelected = state.selectedSysexDump && state.selectedSysexDump.filePath === dump.filePath;
        const isYamaha = dump.manufacturer && dump.manufacturer.toLowerCase().includes("yamaha");
        const mfrBadgeClass = isYamaha 
            ? "bg-indigo-950/80 text-indigo-300 border-indigo-700/60" 
            : "bg-slate-800 text-slate-300 border-slate-700";

        return `
            <div class="p-3 rounded-xl border ${isSelected ? 'bg-indigo-950/30 border-indigo-500/80 shadow-lg shadow-indigo-950/40' : 'bg-slate-900/80 border-slate-800 hover:border-slate-700'} transition-all space-y-2 group">
                <div class="flex items-start justify-between gap-2">
                    <div class="min-w-0 flex-1">
                        <div class="flex items-center space-x-2">
                            <span class="text-xs text-indigo-400 font-mono">💾</span>
                            <span class="text-xs font-bold text-white truncate font-mono" title="${dump.fileName}">${dump.fileName}</span>
                        </div>
                        <div class="flex items-center space-x-2 text-[10px] text-slate-400 mt-1 flex-wrap gap-y-1">
                            <span class="px-1.5 py-0.2 rounded font-semibold border ${mfrBadgeClass}">${dump.manufacturer || 'SysEx'}</span>
                            <span class="font-mono text-slate-300">${dump.formattedSize || formatBytes(dump.byteSize)}</span>
                            <span class="font-mono text-cyan-400">${dump.packetCount} pkt${dump.packetCount === 1 ? '' : 's'}</span>
                            <span class="text-slate-500">${dump.createdTime || ''}</span>
                        </div>
                    </div>
                    <button class="btn-select-sysex shrink-0 px-2.5 py-1 rounded-lg ${isSelected ? 'bg-indigo-600 text-white font-semibold' : 'bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white'} text-[11px] border border-slate-700/60 transition active:scale-95" data-filepath="${dump.filePath}" title="${isSelected ? 'This dump is loaded into the staging area for transmission' : 'Select this dump to stage and transmit to your synthesizer'}">
                        ${isSelected ? '✓ Selected' : 'Select'}
                    </button>
                </div>

                <div class="flex items-center justify-between border-t border-slate-800/80 pt-2 text-[11px]">
                    <button class="btn-inspect-sysex flex items-center space-x-1 text-slate-400 hover:text-cyan-300 transition" data-filepath="${dump.filePath}" title="Open hex viewer to inspect binary SysEx packets, headers, and checksums">
                        <svg class="w-3 h-3 text-cyan-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/>
                            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/>
                        </svg>
                        <span>Inspect Hex</span>
                    </button>
                    <div class="flex items-center space-x-2">
                        <button class="btn-rename-sysex text-slate-400 hover:text-slate-200 transition" data-filepath="${dump.filePath}" data-filename="${dump.fileName}" title="Rename this SysEx dump file">
                            Rename
                        </button>
                        <span class="text-slate-700">•</span>
                        <button class="btn-delete-sysex text-rose-400/80 hover:text-rose-300 transition" data-filepath="${dump.filePath}" data-filename="${dump.fileName}" title="Permanently delete this SysEx dump file from disk">
                            Delete
                        </button>
                    </div>
                </div>
            </div>
        `;
    }).join("");

    listEl.querySelectorAll(".btn-select-sysex").forEach(btn => {
        btn.addEventListener("click", () => {
            const fp = btn.getAttribute("data-filepath");
            const d = state.sysexLibrary.find(x => x.filePath === fp);
            if (d) selectSysexDumpForTransmit(d);
        });
    });

    listEl.querySelectorAll(".btn-inspect-sysex").forEach(btn => {
        btn.addEventListener("click", () => {
            const fp = btn.getAttribute("data-filepath");
            sendToHost("inspect_sysex_dump", { filePath: fp });
        });
    });

    listEl.querySelectorAll(".btn-rename-sysex").forEach(btn => {
        btn.addEventListener("click", () => {
            const fp = btn.getAttribute("data-filepath");
            const fn = btn.getAttribute("data-filename");
            const newName = prompt(`Enter new filename for SysEx dump:`, fn);
            if (newName && newName.trim() && newName.trim() !== fn) {
                sendToHost("rename_sysex_dump", { filePath: fp, newFileName: newName.trim() });
            }
        });
    });

    listEl.querySelectorAll(".btn-delete-sysex").forEach(btn => {
        btn.addEventListener("click", () => {
            const fp = btn.getAttribute("data-filepath");
            const fn = btn.getAttribute("data-filename");
            if (confirm(`Permanently delete SysEx dump "${fn}"?`)) {
                sendToHost("delete_sysex_dump", { filePath: fp });
            }
        });
    });
}

function selectSysexDumpForTransmit(dump) {
    state.selectedSysexDump = dump;
    const titleEl = document.getElementById("sysexSelectedDumpTitle");
    const sizeEl = document.getElementById("sysexSelectedDumpSize");
    const pktsEl = document.getElementById("sysexSelectedDumpPackets");
    const mfrEl = document.getElementById("sysexSelectedDumpMfr");
    const btnTransmit = document.getElementById("btnTransmitSysex");

    if (titleEl) titleEl.textContent = dump.fileName;
    if (sizeEl) sizeEl.textContent = dump.formattedSize || formatBytes(dump.byteSize);
    if (pktsEl) pktsEl.textContent = `${dump.packetCount} packets`;
    if (mfrEl) mfrEl.textContent = dump.manufacturer || "Generic SysEx";
    if (btnTransmit) btnTransmit.removeAttribute("disabled");

    renderSysExLibrary();
}

function startSysexCapture() {
    state.isSysexCapturing = true;
    sendToHost("start_sysex_capture");
    const btnStart = document.getElementById("btnStartSysexCapture");
    const btnStop = document.getElementById("btnStopSysexCapture");
    const badge = document.getElementById("sysexCaptureStatusBadge");
    const pulse = document.getElementById("sysexCapturePulse");

    if (btnStart) btnStart.classList.add("hidden");
    if (btnStop) btnStop.classList.remove("hidden");
    if (badge) {
        badge.textContent = "Listening...";
        badge.className = "px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-950 text-amber-400 border border-amber-800 animate-pulse";
    }
    if (pulse) pulse.className = "w-2.5 h-2.5 rounded-full bg-amber-400 animate-ping";
    const captureModel = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";
    showToast(`SysEx Capture active: waiting for ${captureModel} Memory Bulk Dump...`);
}

function stopSysexCapture() {
    state.isSysexCapturing = false;
    sendToHost("stop_sysex_capture");
    const btnStart = document.getElementById("btnStartSysexCapture");
    const btnStop = document.getElementById("btnStopSysexCapture");
    const badge = document.getElementById("sysexCaptureStatusBadge");
    const pulse = document.getElementById("sysexCapturePulse");

    if (btnStart) btnStart.classList.remove("hidden");
    if (btnStop) btnStop.classList.add("hidden");
    if (badge) {
        badge.textContent = "Idle";
        badge.className = "px-2 py-0.5 rounded-full text-[10px] font-semibold bg-slate-800 text-slate-400 border border-slate-700";
    }
    if (pulse) pulse.className = "w-2.5 h-2.5 rounded-full bg-slate-600";
}

function updateSysexCaptureProgress(data) {
    const pktsEl = document.getElementById("sysexCapturePackets");
    const bytesEl = document.getElementById("sysexCaptureBytes");
    const badge = document.getElementById("sysexCaptureStatusBadge");
    const pulse = document.getElementById("sysexCapturePulse");

    if (pktsEl) pktsEl.textContent = data.packetCount || 0;
    if (bytesEl) bytesEl.textContent = formatBytes(data.byteCount || 0);

    if (data.status) {
        if (badge) badge.textContent = data.status;
    }
    if (data.packetCount > 0 && pulse) {
        pulse.className = "w-2.5 h-2.5 rounded-full bg-emerald-400 animate-pulse";
    }
}

function handleSysexDumpCaptured(data) {
    stopSysexCapture();
    showToast(`Saved SysEx dump: ${data.fileName} (${formatBytes(data.totalBytes)})`);
    sendToHost("get_sysex_library");
    selectSysexDumpForTransmit({
        fileName: data.fileName,
        filePath: data.filePath,
        byteSize: data.totalBytes,
        packetCount: data.totalPackets,
        manufacturer: data.manufacturer,
        formattedSize: formatBytes(data.totalBytes)
    });
}

function startSysexTransmit() {
    if (!state.selectedSysexDump) {
        showToast("Please select a SysEx dump from the library first.", true);
        return;
    }

    const delayEl = document.getElementById("sysexPacketDelaySelect");
    const delay = delayEl ? (parseInt(delayEl.value) || 30) : 30;

    const restoreModel = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";
    const confirmed = confirm(
        `Restore "${state.selectedSysexDump.fileName}" to Yamaha ${restoreModel}?\n\n` +
        `WARNING: This will overwrite keyboard memory registers.\n` +
        `Pacing delay: ${delay}ms per packet.\n\n` +
        `Ensure your ${restoreModel} is powered on and connected to MIDI OUT.`
    );
    if (!confirmed) return;

    state.isSysexTransmitting = true;
    const btnTransmit = document.getElementById("btnTransmitSysex");
    const btnCancel = document.getElementById("btnCancelSysexTransmit");
    const progressContainer = document.getElementById("sysexTransmitProgressContainer");

    if (btnTransmit) btnTransmit.classList.add("hidden");
    if (btnCancel) btnCancel.classList.remove("hidden");
    if (progressContainer) progressContainer.classList.remove("hidden");

    sendToHost("transmit_sysex_dump", {
        filePath: state.selectedSysexDump.filePath,
        packetDelayMs: delay
    });
}

function cancelSysexTransmit() {
    sendToHost("cancel_sysex_transmit");
}

function updateSysexTransmitProgress(data) {
    const progressContainer = document.getElementById("sysexTransmitProgressContainer");
    const statusText = document.getElementById("sysexTransmitStatusText");
    const percentEl = document.getElementById("sysexTransmitPercent");
    const progressBar = document.getElementById("sysexTransmitProgressBar");
    const btnTransmit = document.getElementById("btnTransmitSysex");
    const btnCancel = document.getElementById("btnCancelSysexTransmit");

    if (statusText) {
        statusText.textContent = `Transmitting packet ${data.currentPacket}/${data.totalPackets} (${formatBytes(data.currentBytes)} / ${formatBytes(data.totalBytes)})`;
    }
    if (percentEl) percentEl.textContent = `${data.percent}%`;
    if (progressBar) progressBar.style.width = `${data.percent}%`;

    if (data.isCompleted) {
        state.isSysexTransmitting = false;
        if (btnTransmit) btnTransmit.classList.remove("hidden");
        if (btnCancel) btnCancel.classList.add("hidden");
        const completedModel = state.activeModel === "pss680" ? "PSS-680" : "PSS-790";
        showToast(`SysEx bulk dump successfully restored to Yamaha ${completedModel}!`);
        setTimeout(() => {
            if (!state.isSysexTransmitting && progressContainer) progressContainer.classList.add("hidden");
        }, 2000);
    } else if (data.isCancelled) {
        state.isSysexTransmitting = false;
        if (btnTransmit) btnTransmit.classList.remove("hidden");
        if (btnCancel) btnCancel.classList.add("hidden");
        showToast("SysEx transmission cancelled.");
        if (progressContainer) progressContainer.classList.add("hidden");
    }
}

function showSysexInspectModal(data) {
    if (data.error) {
        showToast(data.error, true);
        return;
    }

    state.lastInspectedHex = data.hexDump || "";
    const modal = document.getElementById("sysexInspectModal");
    const titleEl = document.getElementById("sysexInspectTitle");
    const bytesEl = document.getElementById("sysexInspectBytes");
    const pktsEl = document.getElementById("sysexInspectPackets");
    const mfrEl = document.getElementById("sysexInspectMfr");
    const contentEl = document.getElementById("sysexInspectHexContent");

    if (titleEl) titleEl.textContent = data.fileName || "SysEx Dump";
    if (bytesEl) bytesEl.textContent = data.formattedSize || formatBytes(data.byteSize);
    if (pktsEl) pktsEl.textContent = `${data.packetCount} packets`;
    if (mfrEl) mfrEl.textContent = data.manufacturer || "Generic SysEx";
    if (contentEl) contentEl.textContent = data.hexDump || "No binary content";

    if (modal) modal.classList.remove("hidden");
}

// SysEx Vault Event Listeners
document.getElementById("btnStartSysexCapture")?.addEventListener("click", startSysexCapture);
document.getElementById("btnStopSysexCapture")?.addEventListener("click", stopSysexCapture);
document.getElementById("btnTransmitSysex")?.addEventListener("click", startSysexTransmit);
document.getElementById("btnCancelSysexTransmit")?.addEventListener("click", cancelSysexTransmit);

document.getElementById("sysexSearchInput")?.addEventListener("input", (e) => {
    state.sysexSearchQuery = e.target.value;
    renderSysExLibrary();
});

document.getElementById("btnImportSysex")?.addEventListener("click", () => {
    document.getElementById("inputImportSysex")?.click();
});

document.getElementById("inputImportSysex")?.addEventListener("change", (e) => {
    const file = e.target.files && e.target.files[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = function(evt) {
        const arrayBuf = evt.target.result;
        const bytes = new Uint8Array(arrayBuf);
        let binary = "";
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        const b64 = btoa(binary);
        sendToHost("import_sysex_dump", { fileName: file.name, base64Data: b64 });
    };
    reader.readAsArrayBuffer(file);
    e.target.value = "";
});

document.getElementById("btnRefreshSysexLibrary")?.addEventListener("click", () => {
    sendToHost("get_sysex_library");
});

document.getElementById("btnQuickGmOn")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "gm_on" });
    showToast("Sent GM System On (F0 7E 7F 09 01 F7)");
});

document.getElementById("btnQuickXgOn")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "xg_on" });
    showToast("Sent Yamaha XG System On (F0 43 10 4C ... F7)");
});

document.getElementById("btnQuickMasterVol")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "master_volume_max" });
    showToast("Sent Master Volume Full (127)");
});

document.getElementById("btnQuickAllReset")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "all_reset" });
    showToast("Sent All Notes Off & Reset Controllers");
});

document.getElementById("btnOpenSysExFolder")?.addEventListener("click", () => {
    sendToHost("open_sysex_folder");
});

// PSS-680 SysEx Hardware Controls
document.getElementById("btnQuickMode99")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "mode_99" });
    showToast("PSS-680 Mode 99: Press [MIDI MODE] then [+] on keyboard until display shows 'c.99'.");
});

document.getElementById("btnQuickReqVoice")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "req_voice_dump" });
    showToast("Sent Request FM Voice Bank Dump (Format 00H)");
});

document.getElementById("btnQuickReqMelody")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "req_melody_dump" });
    showToast("Sent Request Melody Memory Dump (Format 01H)");
});

document.getElementById("btnQuickReqChord")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "req_chord_dump" });
    showToast("Sent Request Chord Memory Dump (Format 02H)");
});

document.getElementById("btnQuickReqRhythm")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "req_rhythm_dump" });
    showToast("Sent Request Custom Drummer Dump (Format 03H)");
});

document.getElementById("btnQuickMasterVol680")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "master_volume_max" });
    showToast("Sent Master Volume Full (127)");
});

document.getElementById("btnQuickAllReset680")?.addEventListener("click", () => {
    sendToHost("send_quick_sysex", { command: "all_reset" });
    showToast("Sent All Notes Off & Reset Controllers");
});

document.getElementById("btnOpenSysExFolder680")?.addEventListener("click", () => {
    sendToHost("open_sysex_folder");
});

// Hex Inspect Modal Dismiss & Copy
document.getElementById("btnCloseSysexInspectModal")?.addEventListener("click", () => {
    document.getElementById("sysexInspectModal")?.classList.add("hidden");
});

document.getElementById("btnDismissSysexInspectModal")?.addEventListener("click", () => {
    document.getElementById("sysexInspectModal")?.classList.add("hidden");
});

document.getElementById("btnCopySysexHex")?.addEventListener("click", () => {
    if (state.lastInspectedHex) {
        navigator.clipboard.writeText(state.lastInspectedHex).then(() => {
            showToast("SysEx hex dump copied to clipboard!");
        }).catch(() => {
            showToast("Failed to copy hex to clipboard.", true);
        });
    }
});

// Light / Dark Theme Toggle
document.getElementById("btnThemeToggle")?.addEventListener("click", toggleTheme);

// Studio Recordings Vault Controls
document.getElementById("recordingsSearchInput")?.addEventListener("input", (e) => {
    state.recordingsSearchQuery = e.target.value;
    renderRecordingsLibrary();
});

document.getElementById("btnRefreshRecordings")?.addEventListener("click", () => {
    sendToHost("get_recordings_library");
    showToast("Refreshing recordings library...");
});

document.getElementById("btnOpenRecordingsFolder")?.addEventListener("click", () => {
    sendToHost("open_recordings_folder");
});

// Expose handleHostMessage to global window so C# can invoke it directly via ExecuteScriptAsync
window.handleHostMessage = handleHostMessage;

// Model Selector & State Management
function applyActiveModel(modelKey, syncHost = true) {
    state.activeModel = modelKey;
    localStorage.setItem("yamaha_studio_model", modelKey);

    const is680 = modelKey === "pss680";
    if (is680 && state.pss680Voices && state.pss680Voices.length > 0) {
        state.voices = state.pss680Voices;
    } else if (!is680 && state.pss790Voices && state.pss790Voices.length > 0) {
        state.voices = state.pss790Voices;
    }

    const icon = document.getElementById("activeModelIcon");
    const title = document.getElementById("activeModelTitle");
    const badge = document.getElementById("activeModelBadge");
    const check790 = document.getElementById("check-pss790");
    const check680 = document.getElementById("check-pss680");

    if (icon) icon.src = is680 ? "pss680_icon.png" : "app_icon.png";
    if (title) title.textContent = is680 ? "PSS-680 Studio" : "PSS-790 Studio";
    if (badge) {
        badge.textContent = is680 ? "2-Op FM Engine" : "AWM Engine";
        badge.className = is680
            ? "px-1.5 py-0.2 text-[9px] font-semibold uppercase tracking-wider rounded-full bg-amber-950 text-amber-400 border border-amber-800/60 hidden md:inline-block"
            : "px-1.5 py-0.2 text-[9px] font-semibold uppercase tracking-wider rounded-full bg-cyan-950 text-cyan-400 border border-cyan-800/60 hidden md:inline-block";
    }
    const activeSub = document.getElementById("activeModelSubtitle");
    if (activeSub) {
        activeSub.textContent = is680
            ? "PSS-680 • 12-Voice 2-Op FM • Mode 99"
            : "PSS-790 • 28-Voice AWM PCM • Vector Synth";
    }

    if (check790) check790.classList.toggle("hidden", is680);
    if (check680) check680.classList.toggle("hidden", !is680);

    // Toggle SysEx toolbars
    const sysex790 = document.getElementById("sysexControlsPss790");
    const sysex680 = document.getElementById("sysexControlsPss680");
    if (sysex790) sysex790.classList.toggle("hidden", is680);
    if (sysex680) sysex680.classList.toggle("hidden", !is680);

    // Update SysEx Vault Title
    const sysexTitle = document.getElementById("sysexVaultHeaderTitle");
    const sysexSubtitle = document.getElementById("sysexVaultHeaderSubtitle");
    const designerPillIcon = document.getElementById("sysexDesignerPillIcon");
    const designerPillLabel = document.getElementById("sysexDesignerPillLabel");

    if (sysexTitle) sysexTitle.textContent = is680 ? "Yamaha PSS-680 FM Sound Designer & SysEx Vault" : "Yamaha PSS-790 Vector Synthesizer & SysEx Vault";
    if (sysexSubtitle) sysexSubtitle.textContent = is680 
        ? "Design 2-op FM voices, access 18 hidden parameters, and manage memory backups" 
        : "Real-time 2D 4-voice vector synthesis, LFO orbit automation, and memory backups";
    if (designerPillIcon) designerPillIcon.textContent = is680 ? "🎛️" : "🕹️";
    if (designerPillLabel) designerPillLabel.textContent = is680 ? "FM Sound Designer" : "Vector Synthesizer";

    updateSysexDeckVisibility();

    // Update Recorder Save button label & description
    const lblSave = document.getElementById("lblSaveTakePss");
    if (lblSave) lblSave.textContent = is680 ? "Save Native (PSS-680 Ready)" : "Save Native (PSS-790 Ready)";
    const descSave = document.getElementById("descSaveTakePss");
    if (descSave) descSave.textContent = is680 ? "Direct 1:1 capture with native marker to PSS-680 Midi folder. Zero translation needed." : "Direct 1:1 capture with native marker to PSS-790 Midi folder. Zero translation needed.";

    // Update Mixer Subtitle
    const mixerSubtitle = document.getElementById("mixerSubtitle");
    if (mixerSubtitle) mixerSubtitle.innerHTML = is680 ? "Live Voice Overrides (00-99 FM Presets) &bull; Channel 16 Percussion (32 Hand Percussion Instruments)" : "Live Voice Overrides (Chart 2) &bull; Channel 16 Percussion (Chart 1)";

    // Update Polyphony Guard tooltip across container, toggle checkbox, and label
    const polyGuardTooltip = is680
        ? "Smart Polyphony Guard: Restricts concurrent voices to 12 (Sound Source Mode 99), dynamically dropping quiet background notes to protect lead melody and bass from hardware voice-stealing"
        : "Smart Polyphony Guard: Restricts concurrent voices to 28, dynamically dropping quiet background notes to protect lead melody and bass from hardware voice-stealing";

    const polyContainer = document.getElementById("polyphonyGuardContainer");
    const polyToggle = document.getElementById("polyphonyGuardToggle");
    const polyLabel = document.getElementById("polyphonyGuardLabel");
    if (polyContainer) polyContainer.title = polyGuardTooltip;
    if (polyToggle) polyToggle.title = polyGuardTooltip;
    if (polyLabel) polyLabel.title = polyGuardTooltip;

    // Update Player Workspace Tab tooltip
    const tabPlayer = document.getElementById("tabBtnPlayer");
    if (tabPlayer) {
        tabPlayer.title = is680
            ? "Player Workspace: Load, preview, and play back MIDI files with real-time Yamaha PSS-680 remapping"
            : "Player Workspace: Load, preview, and play back MIDI files with real-time Yamaha PSS-790 remapping";
    }

    // Update Export Active Song button tooltip
    const btnSaveRemapped = document.getElementById("btnSaveRemapped");
    if (btnSaveRemapped) {
        btnSaveRemapped.title = is680
            ? "Export Remapped MIDI: Save active song converted for native Yamaha PSS-680 playback"
            : "Export Remapped MIDI: Save active song converted for native Yamaha PSS-790 playback";
    }

    // Update Studio Workspace GM Export button and description
    const btnSaveTakeGm = document.getElementById("btnSaveTakeGm");
    if (btnSaveTakeGm) {
        btnSaveTakeGm.title = is680
            ? "Export General MIDI: Reverse-remaps Channel 16 drums to Channel 10 and PSS-680 FM presets to GM programs for DAW or PC use"
            : "Export General MIDI: Reverse-remaps Channel 16 drums to Channel 10 and PSS-790 voices to GM programs for DAW or PC use";
    }
    const descSaveTakeGm = document.getElementById("descSaveTakeGm");
    if (descSaveTakeGm) {
        descSaveTakeGm.innerHTML = is680
            ? "Reverse remaps Ch 16 &rarr; 10 drums, 32 Hand Percussion notes, and FM presets (00-99) to standard GM."
            : "Reverse remaps Ch 16 &rarr; 10 drums, Chart 1 notes, and Chart 2 voices to standard GM.";
    }

    // Update Loop Guard badge tooltip
    const loopGuard = document.getElementById("loopGuardBadge");
    if (loopGuard) {
        loopGuard.title = is680
            ? "Loop Guard: Hardware safety circuit preventing MIDI feedback loops between PSS-680 OUT and IN"
            : "Loop Guard: Hardware safety circuit preventing MIDI feedback loops between PSS-790 OUT and IN";
    }

    // Update SysEx Vault tooltips & controls
    const btnCapture = document.getElementById("btnStartSysexCapture");
    if (btnCapture) {
        btnCapture.title = is680
            ? "Listen for Bulk Dump: Arm listener to capture internal memory transmitted from the PSS-680 (FM voices, melody, chord, or drum patterns)"
            : "Listen for Bulk Dump: Arm listener to capture internal memory transmitted from the PSS-790 MEMORY BULK DUMP button";
    }

    const delayDesc = document.getElementById("sysexPacketDelayDesc");
    if (delayDesc) {
        delayDesc.textContent = is680
            ? "Prevents PSS-680 serial buffer overflow"
            : "Prevents PSS-790 <bu full> overflow";
    }
    const delaySelect = document.getElementById("sysexPacketDelaySelect");
    if (delaySelect) {
        delaySelect.title = is680
            ? "Paced Packet Delay: Inter-packet delay interval to prevent hardware buffer overrun on PSS-680 (31,250 baud buffer)"
            : "Paced Packet Delay: Inter-packet delay interval to prevent hardware buffer overrun ('bu full' error on PSS-790 LCD)";
    }

    const btnTransmit = document.getElementById("btnTransmitSysex");
    if (btnTransmit) {
        btnTransmit.title = is680
            ? "Transmit SysEx: Send the selected bulk dump snapshot back into the PSS-680 to restore its memory banks"
            : "Transmit SysEx: Send the selected bulk dump snapshot back into the PSS-790 to restore its memory banks";
        const btnTransmitSpan = btnTransmit.querySelector("span");
        if (btnTransmitSpan) btnTransmitSpan.textContent = is680 ? "Transmit to PSS-680" : "Transmit to PSS-790";
    }

    // Update Batch Remapper Health Audit Polyphony Card tooltip & label
    const auditCard = document.getElementById("auditPolyphonyCard");
    if (auditCard) {
        auditCard.title = is680
            ? "Songs with note bursts exceeding 12 simultaneous voices protected by Polyphony Guard"
            : "Songs with note bursts exceeding 28 simultaneous voices protected by Polyphony Guard";
    }
    const auditLabel = document.getElementById("auditPolyphonyLabel");
    if (auditLabel) {
        auditLabel.textContent = is680 ? ">12 Voices" : ">28 Voices";
    }

    // Re-render library so category title and tree immediately switch to PSS-680 Ready vs PSS-790 Ready
    renderLibrarySelect();
    renderLibraryTree();

    // Re-render mixer so voice override dropdowns update to active model's presets
    renderMixer();

    if (syncHost) {
        sendToHost("set_active_model", { model: modelKey });
    }
}

// =========================================================================
// TAB 3: SOUND DESIGNER & VECTOR SYNTHESIZER WORKSTATION
// =========================================================================

function updateSysexDeckVisibility() {
    const is680 = state.activeModel === "pss680";
    const mode = state.sysexViewMode || "designer";

    const fmDeck = document.getElementById("deckFmSoundDesigner");
    const vecDeck = document.getElementById("deckVectorSynthesizer");
    const dumpsDeck = document.getElementById("deckBulkDumps");

    const btnDesigner = document.getElementById("btnSysexViewDesigner");
    const btnDumps = document.getElementById("btnSysexViewDumps");

    if (mode === "designer") {
        if (dumpsDeck) dumpsDeck.classList.add("hidden");
        if (is680) {
            if (fmDeck) fmDeck.classList.remove("hidden");
            if (vecDeck) vecDeck.classList.add("hidden");
        } else {
            if (vecDeck) vecDeck.classList.remove("hidden");
            if (fmDeck) fmDeck.classList.add("hidden");
            if (typeof drawVectorCanvas === "function") {
                requestAnimationFrame(drawVectorCanvas);
            }
        }

        if (btnDesigner) {
            btnDesigner.className = "px-2.5 sm:px-3 py-1 rounded-lg text-xs font-bold text-white bg-indigo-600 shadow-md transition active:scale-95 flex items-center space-x-1.5";
        }
        if (btnDumps) {
            btnDumps.className = "px-2.5 sm:px-3 py-1 rounded-lg text-xs font-semibold text-slate-400 hover:text-white transition active:scale-95 flex items-center space-x-1.5";
        }
    } else {
        if (fmDeck) fmDeck.classList.add("hidden");
        if (vecDeck) vecDeck.classList.add("hidden");
        if (dumpsDeck) dumpsDeck.classList.remove("hidden");

        if (btnDesigner) {
            btnDesigner.className = "px-2.5 sm:px-3 py-1 rounded-lg text-xs font-semibold text-slate-400 hover:text-white transition active:scale-95 flex items-center space-x-1.5";
        }
        if (btnDumps) {
            btnDumps.className = "px-2.5 sm:px-3 py-1 rounded-lg text-xs font-bold text-white bg-indigo-600 shadow-md transition active:scale-95 flex items-center space-x-1.5";
        }
    }
}

function initSysexViewSwitcher() {
    const btnDesigner = document.getElementById("btnSysexViewDesigner");
    const btnDumps = document.getElementById("btnSysexViewDumps");

    if (btnDesigner) {
        btnDesigner.addEventListener("click", () => {
            state.sysexViewMode = "designer";
            updateSysexDeckVisibility();
        });
    }
    if (btnDumps) {
        btnDumps.addEventListener("click", () => {
            state.sysexViewMode = "dumps";
            updateSysexDeckVisibility();
        });
    }
}

// -------------------------------------------------------------------------
// PSS-680: FM SOUND DESIGNER
// -------------------------------------------------------------------------
let selectedFmBank = 0;
let selectedModWave = 0;
let selectedCarWave = 0;
let fmLiveSendTimer = null;

const FM_PRESETS = {
    "init": {
        "name": "Init Sine (2-Op)",
        "feedback": 0,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 0,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 20,
            "releaseRate": 6,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 0,
            "decay2Rate": 0,
            "sustainReleaseRate": 5,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 80,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 20,
            "releaseRate": 6,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 0,
            "decay2Rate": 0,
            "sustainReleaseRate": 5,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "aha_take_on_me": {
        "name": "Take On Me Lead",
        "feedback": 6,
        "pitchModulationSensitivity": 3,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 35,
        "vibrato": true,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 76,
            "waveform": 1,
            "attackRate": 62,
            "decay1Rate": 18,
            "releaseRate": 7,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 4,
            "decay2Rate": 4,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 2,
            "attackRate": 61,
            "decay1Rate": 14,
            "releaseRate": 7,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 2,
            "decay2Rate": 3,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "van_halen_jump": {
        "name": "Jump OB Brass",
        "feedback": 6,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 74,
            "waveform": 1,
            "attackRate": 57,
            "decay1Rate": 22,
            "releaseRate": 6,
            "fineDetune": 3,
            "coarseDetune": false,
            "decay1Level": 3,
            "decay2Rate": 4,
            "sustainReleaseRate": 6,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 1,
            "attackRate": 55,
            "decay1Rate": 16,
            "releaseRate": 6,
            "fineDetune": -2,
            "coarseDetune": false,
            "decay1Level": 1,
            "decay2Rate": 3,
            "sustainReleaseRate": 6,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "dx7_tine_ep": {
        "name": "DX7 Ballad Tine EP",
        "feedback": 1,
        "pitchModulationSensitivity": 3,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 35,
        "vibrato": true,
        "sustain": true,
        "modulator": {
            "multiplier": 14,
            "panelLevel": 68,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 52,
            "releaseRate": 7,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 14,
            "decay2Rate": 12,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 9,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 16,
            "releaseRate": 6,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 10,
            "decay2Rate": 3,
            "sustainReleaseRate": 6,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        }
    },
    "tx81z_lately_bass": {
        "name": "LatelyBass / Solid Bass",
        "feedback": 6,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 75,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 48,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 12,
            "decay2Rate": 4,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 24,
            "releaseRate": 8,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 2,
            "decay2Rate": 3,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "eurythmics_sweet_dreams": {
        "name": "Sweet Dreams Pulse",
        "feedback": 5,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 2,
            "panelLevel": 71,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 36,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 5,
            "decay2Rate": 4,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 28,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 3,
            "decay2Rate": 3,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "new_order_blue_monday": {
        "name": "Blue Monday Octaver",
        "feedback": 3,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 77,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 46,
            "releaseRate": 8,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 0,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 34,
            "releaseRate": 7,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "danger_zone_slap": {
        "name": "Danger Zone FM Slap",
        "feedback": 4,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 3,
            "panelLevel": 81,
            "waveform": 1,
            "attackRate": 63,
            "decay1Rate": 50,
            "releaseRate": 9,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 13,
            "decay2Rate": 12,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 30,
            "releaseRate": 8,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 14,
            "decay2Rate": 10,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "axel_f_brass": {
        "name": "Axel F Brass Lead",
        "feedback": 4,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 2,
            "panelLevel": 70,
            "waveform": 2,
            "attackRate": 62,
            "decay1Rate": 38,
            "releaseRate": 8,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 6,
            "decay2Rate": 4,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 62,
            "decay1Rate": 26,
            "releaseRate": 7,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 4,
            "decay2Rate": 3,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "daft_punk_hbfs": {
        "name": "Harder Better Lead",
        "feedback": 5,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 3,
            "panelLevel": 72,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 32,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 4,
            "decay2Rate": 4,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 2,
            "attackRate": 62,
            "decay1Rate": 22,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 2,
            "decay2Rate": 3,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "stranger_things_arp": {
        "name": "Stranger Things Arp Bass",
        "feedback": 6,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 76,
            "waveform": 1,
            "attackRate": 63,
            "decay1Rate": 44,
            "releaseRate": 8,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 7,
            "decay2Rate": 4,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 36,
            "releaseRate": 7,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 5,
            "decay2Rate": 3,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "vangelis_blade_runner_pad": {
        "name": "Blade Runner CS-80 Pad",
        "feedback": 5,
        "pitchModulationSensitivity": 3,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 35,
        "vibrato": true,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 69,
            "waveform": 2,
            "attackRate": 18,
            "decay1Rate": 12,
            "releaseRate": 3,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 3,
            "decay2Rate": 4,
            "sustainReleaseRate": 3,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 16,
            "decay1Rate": 10,
            "releaseRate": 3,
            "fineDetune": -2,
            "coarseDetune": false,
            "decay1Level": 1,
            "decay2Rate": 3,
            "sustainReleaseRate": 3,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "toto_africa_pad": {
        "name": "Africa Polysynth Pad",
        "feedback": 4,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 71,
            "waveform": 1,
            "attackRate": 28,
            "decay1Rate": 16,
            "releaseRate": 4,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 5,
            "decay2Rate": 4,
            "sustainReleaseRate": 4,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 25,
            "decay1Rate": 14,
            "releaseRate": 4,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 2,
            "decay2Rate": 3,
            "sustainReleaseRate": 4,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "enya_orinoco_pad": {
        "name": "Orinoco Shimmer Pad",
        "feedback": 2,
        "pitchModulationSensitivity": 3,
        "amplitudeModulationSensitivity": 1,
        "vibratoDelayTime": 35,
        "vibrato": true,
        "sustain": true,
        "modulator": {
            "multiplier": 4,
            "panelLevel": 59,
            "waveform": 0,
            "attackRate": 24,
            "decay1Rate": 14,
            "releaseRate": 3,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 4,
            "decay2Rate": 4,
            "sustainReleaseRate": 3,
            "levelKeyScalingHigh": 9,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": true
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 22,
            "decay1Rate": 12,
            "releaseRate": 3,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 1,
            "decay2Rate": 3,
            "sustainReleaseRate": 3,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "wham_last_christmas_bell": {
        "name": "Last Christmas FM Bell",
        "feedback": 1,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 7,
            "panelLevel": 72,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 24,
            "releaseRate": 5,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 5,
            "levelKeyScalingHigh": 9,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 12,
            "releaseRate": 4,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 4,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "phil_collins_air_bell": {
        "name": "In the Air Glass Chime",
        "feedback": 2,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 5,
            "panelLevel": 65,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 28,
            "releaseRate": 4,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 14,
            "decay2Rate": 12,
            "sustainReleaseRate": 4,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 16,
            "releaseRate": 4,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 12,
            "decay2Rate": 10,
            "sustainReleaseRate": 4,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "madonna_fm_marimba": {
        "name": "80s DX7 Wood Marimba",
        "feedback": 0,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 4,
            "panelLevel": 70,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 56,
            "releaseRate": 8,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 34,
            "releaseRate": 8,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        }
    },
    "sonic_steel_drum": {
        "name": "Green Hill Steel Drum",
        "feedback": 4,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 3,
            "panelLevel": 71,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 38,
            "releaseRate": 7,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 26,
            "releaseRate": 7,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "gated_fm_snare": {
        "name": "80s Gated Noise Snare",
        "feedback": 7,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 3,
            "panelLevel": 77,
            "waveform": 1,
            "attackRate": 63,
            "decay1Rate": 46,
            "releaseRate": 13,
            "fineDetune": 3,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 13,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 44,
            "releaseRate": 13,
            "fineDetune": -3,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 13,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "electro_808_fm_kick": {
        "name": "Planet Rock Sub Kick",
        "feedback": 3,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 79,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 60,
            "releaseRate": 10,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 10,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 0,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 34,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "electro_metallic_cowbell": {
        "name": "Blue Monday Metallic Clank",
        "feedback": 5,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": false,
        "modulator": {
            "multiplier": 7,
            "panelLevel": 74,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 48,
            "releaseRate": 12,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 12,
            "sustainReleaseRate": 12,
            "levelKeyScalingHigh": 3,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 5,
            "panelLevel": 92,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 42,
            "releaseRate": 12,
            "fineDetune": -2,
            "coarseDetune": false,
            "decay1Level": 15,
            "decay2Rate": 10,
            "sustainReleaseRate": 12,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "sonic_knuckles_sky_sanctuary": {
        "name": "Sky Sanctuary Crystalline Lead",
        "feedback": 3,
        "pitchModulationSensitivity": 3,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 35,
        "vibrato": true,
        "sustain": true,
        "modulator": {
            "multiplier": 3,
            "panelLevel": 70,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 42,
            "releaseRate": 7,
            "fineDetune": 1,
            "coarseDetune": false,
            "decay1Level": 11,
            "decay2Rate": 12,
            "sustainReleaseRate": 7,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 18,
            "releaseRate": 6,
            "fineDetune": -1,
            "coarseDetune": false,
            "decay1Level": 4,
            "decay2Rate": 3,
            "sustainReleaseRate": 6,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "sonic2_chemical_plant_bass": {
        "name": "Chemical Plant Slap Bass",
        "feedback": 6,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 2,
            "panelLevel": 74,
            "waveform": 1,
            "attackRate": 63,
            "decay1Rate": 52,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 11,
            "decay2Rate": 4,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 1,
            "panelLevel": 92,
            "waveform": 0,
            "attackRate": 63,
            "decay1Rate": 26,
            "releaseRate": 9,
            "fineDetune": 0,
            "coarseDetune": false,
            "decay1Level": 2,
            "decay2Rate": 3,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    },
    "sor2_go_straight_bass": {
        "name": "Go Straight Gritty House Bass",
        "feedback": 7,
        "pitchModulationSensitivity": 0,
        "amplitudeModulationSensitivity": 0,
        "vibratoDelayTime": 0,
        "vibrato": false,
        "sustain": true,
        "modulator": {
            "multiplier": 1,
            "panelLevel": 85,
            "waveform": 3,
            "attackRate": 63,
            "decay1Rate": 44,
            "releaseRate": 9,
            "fineDetune": 2,
            "coarseDetune": false,
            "decay1Level": 8,
            "decay2Rate": 4,
            "sustainReleaseRate": 9,
            "levelKeyScalingHigh": 6,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 1,
            "amplitudeModulationEnable": false
        },
        "carrier": {
            "multiplier": 0,
            "panelLevel": 92,
            "waveform": 2,
            "attackRate": 63,
            "decay1Rate": 28,
            "releaseRate": 8,
            "fineDetune": -2,
            "coarseDetune": false,
            "decay1Level": 3,
            "decay2Rate": 3,
            "sustainReleaseRate": 8,
            "levelKeyScalingHigh": 0,
            "levelKeyScalingLow": 0,
            "rateKeyScaling": 0,
            "amplitudeModulationEnable": false
        }
    }
};

function collectFmVoiceFromUI() {
    return {
        name: document.getElementById("fmVoiceName")?.value.trim() || "Custom FM",
        bankNumber: selectedFmBank,
        feedback: parseInt(document.getElementById("fmFeedback")?.value || 0, 10),
        pitchModulationSensitivity: parseInt(document.getElementById("fmPitchModSens")?.value || 0, 10),
        amplitudeModulationSensitivity: parseInt(document.getElementById("fmAmpModSens")?.value || 0, 10),
        vibratoDelayTime: parseInt(document.getElementById("fmVibratoDelay")?.value || 0, 10),
        vibrato: !!document.getElementById("fmVibrato")?.checked,
        sustain: !!document.getElementById("fmSustain")?.checked,
        modulator: {
            multiplier: parseInt(document.getElementById("fmModMul")?.value || 1, 10),
            panelLevel: parseInt(document.getElementById("fmModTl")?.value || 50, 10),
            waveform: selectedModWave,
            attackRate: parseInt(document.getElementById("fmModAr")?.value || 63, 10),
            decay1Rate: parseInt(document.getElementById("fmModD1r")?.value || 25, 10),
            releaseRate: parseInt(document.getElementById("fmModRr")?.value || 6, 10),
            fineDetune: parseInt(document.getElementById("fmModFine")?.value || 0, 10),
            coarseDetune: !!document.getElementById("fmModCoarse")?.checked,
            decay1Level: parseInt(document.getElementById("fmModD1l")?.value || 0, 10),
            decay2Rate: parseInt(document.getElementById("fmModD2r")?.value || 0, 10),
            sustainReleaseRate: parseInt(document.getElementById("fmModSrr")?.value || 5, 10),
            levelKeyScalingHigh: parseInt(document.getElementById("fmModLksHi")?.value || 0, 10),
            levelKeyScalingLow: parseInt(document.getElementById("fmModLksLo")?.value || 0, 10),
            rateKeyScaling: parseInt(document.getElementById("fmModRks")?.value || 0, 10),
            amplitudeModulationEnable: !!document.getElementById("fmModAm")?.checked
        },
        carrier: {
            multiplier: parseInt(document.getElementById("fmCarMul")?.value || 1, 10),
            panelLevel: parseInt(document.getElementById("fmCarTl")?.value || 80, 10),
            waveform: selectedCarWave,
            attackRate: parseInt(document.getElementById("fmCarAr")?.value || 63, 10),
            decay1Rate: parseInt(document.getElementById("fmCarD1r")?.value || 20, 10),
            releaseRate: parseInt(document.getElementById("fmCarRr")?.value || 6, 10),
            fineDetune: parseInt(document.getElementById("fmCarFine")?.value || 0, 10),
            coarseDetune: !!document.getElementById("fmCarCoarse")?.checked,
            decay1Level: parseInt(document.getElementById("fmCarD1l")?.value || 0, 10),
            decay2Rate: parseInt(document.getElementById("fmCarD2r")?.value || 0, 10),
            sustainReleaseRate: parseInt(document.getElementById("fmCarSrr")?.value || 5, 10),
            levelKeyScalingHigh: parseInt(document.getElementById("fmCarLksHi")?.value || 0, 10),
            levelKeyScalingLow: parseInt(document.getElementById("fmCarLksLo")?.value || 0, 10),
            rateKeyScaling: parseInt(document.getElementById("fmCarRks")?.value || 0, 10),
            amplitudeModulationEnable: !!document.getElementById("fmCarAm")?.checked
        }
    };
}

function sendFmVoiceLiveThrottled() {
    const chkLive = document.getElementById("chkFmLiveSend");
    if (!chkLive || !chkLive.checked) return;

    const statusPill = document.getElementById("fmLiveStatusPill");
    if (statusPill) {
        statusPill.textContent = "Updating...";
        statusPill.className = "text-[9px] font-bold px-1.5 py-0.5 rounded bg-amber-950 text-amber-400 border border-amber-800/60 transition";
    }

    if (fmLiveSendTimer) clearTimeout(fmLiveSendTimer);
    fmLiveSendTimer = setTimeout(() => {
        const voice = collectFmVoiceFromUI();
        sendToHost("transmit_fm_voice", { voice, silent: true });
        if (statusPill) {
            statusPill.textContent = "Synced";
            statusPill.className = "text-[9px] font-bold px-1.5 py-0.5 rounded bg-emerald-950 text-emerald-400 border border-emerald-800/60 transition";
        }
    }, 120);
}

function updateActiveFmBankDisplay(bank, voiceNumber) {
    selectedFmBank = Math.max(0, Math.min(4, bank));
    const voiceNum = voiceNumber || (100 + selectedFmBank);
    const lbl = document.getElementById("lblActiveFmBank");
    if (lbl) {
        lbl.textContent = `Bank ${selectedFmBank + 1} (Prog ${voiceNum})`;
    }
    document.querySelectorAll("#fmBankPills .fm-bank-btn").forEach(btn => {
        const b = parseInt(btn.dataset.bank, 10);
        if (b === selectedFmBank) {
            btn.className = "fm-bank-btn active px-2 py-0.5 rounded text-[10px] font-bold bg-indigo-600 text-white border border-indigo-400 shadow-sm transition";
        } else {
            btn.className = "fm-bank-btn px-2 py-0.5 rounded text-[10px] font-bold bg-slate-800 text-slate-300 hover:text-white border border-slate-700 transition";
        }
    });
}

function setSliderAndVal(sliderId, valId, val, formatter) {
    if (val === undefined || val === null) return;
    const slider = document.getElementById(sliderId);
    const label = document.getElementById(valId);
    if (slider) slider.value = val;
    if (label) label.textContent = formatter ? formatter(val) : val;
}

function renderFmEnvelopeSvg(svgId, ar, d1r, d1l, d2r, rr, strokeColor, fillColor) {
    const svg = document.getElementById(svgId);
    if (!svg) return;

    // ViewBox: 0 0 200 60
    const yZero = 50;
    const yPeak = 6;
    const height = yZero - yPeak; // 44 px

    const normAr = Math.min(63, Math.max(0, parseInt(ar, 10) || 0));
    const normD1r = Math.min(63, Math.max(0, parseInt(d1r, 10) || 0));
    const normD1l = Math.min(15, Math.max(0, parseInt(d1l, 10) || 0));
    const normD2r = Math.min(31, Math.max(0, parseInt(d2r, 10) || 0));
    const normRr = Math.min(15, Math.max(0, parseInt(rr, 10) || 0));

    // Attack segment width (4px to 45px)
    const attWidth = Math.max(4, Math.round(45 * (1 - (normAr / 63)) + 4));

    // D1 segment width (8px to 50px)
    const d1Width = Math.max(8, Math.round(50 * (1 - (normD1r / 63)) + 8));

    // D1 level Y: level 15 is peak (yPeak), level 0 is yZero
    const d1LevelFraction = normD1l / 15;
    const d1Y = yZero - (d1LevelFraction * height);

    // D2 segment width (8px to 45px)
    const d2Width = Math.max(8, Math.round(45 * (1 - (normD2r / 31)) + 8));
    const d2LevelFraction = d1LevelFraction * 0.5;
    const d2Y = yZero - (d2LevelFraction * height);

    // Release segment width (6px to 45px)
    const rrWidth = Math.max(6, Math.round(45 * (1 - (normRr / 15)) + 6));

    const p0 = { x: 8, y: yZero };
    const p1 = { x: p0.x + attWidth, y: yPeak };
    const p2 = { x: p1.x + d1Width, y: d1Y };
    const p3 = { x: p2.x + d2Width, y: d2Y };
    const p4 = { x: Math.min(192, p3.x + rrWidth), y: yZero };

    const gradId = `${svgId}_grad`;
    svg.innerHTML = `
        <defs>
            <linearGradient id="${gradId}" x1="0%" y1="0%" x2="0%" y2="100%">
                <stop offset="0%" stop-color="${strokeColor}" stop-opacity="0.4" />
                <stop offset="100%" stop-color="${strokeColor}" stop-opacity="0.02" />
            </linearGradient>
        </defs>
        <!-- Grid baseline & guides -->
        <line x1="8" y1="${yZero}" x2="192" y2="${yZero}" stroke="#334155" stroke-width="1" stroke-dasharray="2,2" />
        <line x1="8" y1="${yPeak}" x2="192" y2="${yPeak}" stroke="#1e293b" stroke-width="1" stroke-dasharray="2,2" />
        <!-- Key Off vertical marker -->
        <line x1="${p3.x}" y1="4" x2="${p3.x}" y2="54" stroke="#475569" stroke-width="1" stroke-dasharray="1,2" />
        <text x="${p3.x + 2}" y="13" fill="#64748b" font-size="7" font-family="monospace">KEY OFF</text>
        <!-- Area fill under envelope curve -->
        <path d="M ${p0.x} ${p0.y} L ${p1.x} ${p1.y} L ${p2.x} ${p2.y} L ${p3.x} ${p3.y} L ${p4.x} ${p4.y} Z" fill="url(#${gradId})" />
        <!-- Curve stroke -->
        <path d="M ${p0.x} ${p0.y} L ${p1.x} ${p1.y} L ${p2.x} ${p2.y} L ${p3.x} ${p3.y} L ${p4.x} ${p4.y}" fill="none" stroke="${strokeColor}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
        <!-- Stage Nodes -->
        <circle cx="${p1.x}" cy="${p1.y}" r="2" fill="${strokeColor}" />
        <circle cx="${p2.x}" cy="${p2.y}" r="1.5" fill="${strokeColor}" />
        <circle cx="${p3.x}" cy="${p3.y}" r="1.5" fill="${strokeColor}" />
        <!-- Phase Labels -->
        <text x="${Math.max(10, (p0.x + p1.x)/2 - 3)}" y="48" fill="${strokeColor}" font-size="7" font-weight="bold" font-family="monospace">A</text>
        <text x="${(p1.x + p2.x)/2 - 4}" y="48" fill="#94a3b8" font-size="7" font-family="monospace">D1</text>
        <text x="${(p2.x + p3.x)/2 - 4}" y="48" fill="#94a3b8" font-size="7" font-family="monospace">D2</text>
        <text x="${Math.min(185, (p3.x + p4.x)/2 - 3)}" y="48" fill="#94a3b8" font-size="7" font-family="monospace">R</text>
    `;
}

function updateFmEnvelopes() {
    // Modulator
    const mAr = document.getElementById("fmModAr")?.value || 63;
    const mD1r = document.getElementById("fmModD1r")?.value || 25;
    const mD1l = document.getElementById("fmModD1l")?.value || 0;
    const mD2r = document.getElementById("fmModD2r")?.value || 0;
    const mRr = document.getElementById("fmModRr")?.value || 6;
    renderFmEnvelopeSvg("svgFmModEnv", mAr, mD1r, mD1l, mD2r, mRr, "#22d3ee", "#0891b2");

    // Carrier
    const cAr = document.getElementById("fmCarAr")?.value || 63;
    const cD1r = document.getElementById("fmCarD1r")?.value || 20;
    const cD1l = document.getElementById("fmCarD1l")?.value || 0;
    const cD2r = document.getElementById("fmCarD2r")?.value || 0;
    const cRr = document.getElementById("fmCarRr")?.value || 6;
    renderFmEnvelopeSvg("svgFmCarEnv", cAr, cD1r, cD1l, cD2r, cRr, "#10b981", "#059669");
}

function updateFmVoiceUI(voice) {
    if (!voice) return;
    if (voice.name) {
        const nameEl = document.getElementById("fmVoiceName");
        if (nameEl) nameEl.value = voice.name;
    }

    if (voice.bankNumber !== undefined) {
        updateActiveFmBankDisplay(voice.bankNumber, 100 + voice.bankNumber);
    }

    // Modulator
    if (voice.modulator) {
        const m = voice.modulator;
        setSliderAndVal("fmModMul", "fmModMulVal", m.multiplier, (v) => `${v} (${v == 0 ? '0.5x' : v + '.0x'})`);
        setSliderAndVal("fmModTl", "fmModTlVal", m.panelLevel);
        setSliderAndVal("fmModAr", "fmModArVal", m.attackRate);
        setSliderAndVal("fmModD1r", "fmModD1rVal", m.decay1Rate);
        setSliderAndVal("fmModRr", "fmModRrVal", m.releaseRate);
        setSliderAndVal("fmModFine", "fmModFineVal", m.fineDetune);
        setSliderAndVal("fmModD1l", "fmModD1lVal", m.decay1Level);
        setSliderAndVal("fmModD2r", "fmModD2rVal", m.decay2Rate);
        setSliderAndVal("fmModSrr", "fmModSrrVal", m.sustainReleaseRate);
        setSliderAndVal("fmModLksHi", "fmModLksHiVal", m.levelKeyScalingHigh);
        setSliderAndVal("fmModLksLo", "fmModLksLoVal", m.levelKeyScalingLow);
        setSliderAndVal("fmModRks", "fmModRksVal", m.rateKeyScaling);

        const chkCoarse = document.getElementById("fmModCoarse");
        if (chkCoarse) chkCoarse.checked = !!m.coarseDetune;
        const chkAm = document.getElementById("fmModAm");
        if (chkAm) chkAm.checked = !!m.amplitudeModulationEnable;

        if (m.waveform !== undefined) {
            selectedModWave = m.waveform;
            document.querySelectorAll("#fmModWaveGroup .fm-mod-wave").forEach(btn => {
                const w = parseInt(btn.dataset.wave, 10);
                if (w === selectedModWave) {
                    btn.className = "fm-mod-wave active py-1 rounded text-[9px] font-bold bg-cyan-600 text-white border border-cyan-400 transition";
                } else {
                    btn.className = "fm-mod-wave py-1 rounded text-[9px] font-bold bg-slate-800 text-slate-300 hover:text-white border border-slate-700 transition";
                }
            });
        }
    }

    // Carrier
    if (voice.carrier) {
        const c = voice.carrier;
        setSliderAndVal("fmCarMul", "fmCarMulVal", c.multiplier, (v) => `${v} (${v == 0 ? '0.5x' : v + '.0x'})`);
        setSliderAndVal("fmCarTl", "fmCarTlVal", c.panelLevel);
        setSliderAndVal("fmCarAr", "fmCarArVal", c.attackRate);
        setSliderAndVal("fmCarD1r", "fmCarD1rVal", c.decay1Rate);
        setSliderAndVal("fmCarRr", "fmCarRrVal", c.releaseRate);
        setSliderAndVal("fmCarFine", "fmCarFineVal", c.fineDetune);
        setSliderAndVal("fmCarD1l", "fmCarD1lVal", c.decay1Level);
        setSliderAndVal("fmCarD2r", "fmCarD2rVal", c.decay2Rate);
        setSliderAndVal("fmCarSrr", "fmCarSrrVal", c.sustainReleaseRate);
        setSliderAndVal("fmCarLksHi", "fmCarLksHiVal", c.levelKeyScalingHigh);
        setSliderAndVal("fmCarLksLo", "fmCarLksLoVal", c.levelKeyScalingLow);
        setSliderAndVal("fmCarRks", "fmCarRksVal", c.rateKeyScaling);

        const chkCoarse = document.getElementById("fmCarCoarse");
        if (chkCoarse) chkCoarse.checked = !!c.coarseDetune;
        const chkAm = document.getElementById("fmCarAm");
        if (chkAm) chkAm.checked = !!c.amplitudeModulationEnable;

        if (c.waveform !== undefined) {
            selectedCarWave = c.waveform;
            document.querySelectorAll("#fmCarWaveGroup .fm-car-wave").forEach(btn => {
                const w = parseInt(btn.dataset.wave, 10);
                if (w === selectedCarWave) {
                    btn.className = "fm-car-wave active py-1 rounded text-[9px] font-bold bg-emerald-600 text-white border border-emerald-400 transition";
                } else {
                    btn.className = "fm-car-wave py-1 rounded text-[9px] font-bold bg-slate-800 text-slate-300 hover:text-white border border-slate-700 transition";
                }
            });
        }
    }

    // Global
    setSliderAndVal("fmFeedback", "fmFeedbackVal", voice.feedback);
    setSliderAndVal("fmVibratoDelay", "fmVibratoDelayVal", voice.vibratoDelayTime, (v) => `${v} ms`);
    setSliderAndVal("fmPitchModSens", "fmPitchModSensVal", voice.pitchModulationSensitivity);
    setSliderAndVal("fmAmpModSens", "fmAmpModSensVal", voice.amplitudeModulationSensitivity);

    const chkVib = document.getElementById("fmVibrato");
    if (chkVib) chkVib.checked = !!voice.vibrato;
    const chkSus = document.getElementById("fmSustain");
    if (chkSus) chkSus.checked = !!voice.sustain;

    sendFmVoiceLiveThrottled();
    updateFmEnvelopes();
}

function randomizeFmVoice() {
    const randInt = (min, max) => Math.floor(Math.random() * (max - min + 1)) + min;
    const randChoice = (arr) => arr[randInt(0, arr.length - 1)];

    const voice = {
        name: `FM Rand ${randInt(10, 99)}`,
        bankNumber: selectedFmBank,
        feedback: randChoice([0, 1, 2, 3, 4, 5]),
        pitchModulationSensitivity: randChoice([0, 1, 2, 3]),
        amplitudeModulationSensitivity: randChoice([0, 0, 1]),
        vibratoDelayTime: randInt(0, 50),
        vibrato: Math.random() > 0.6,
        sustain: Math.random() > 0.5,
        modulator: {
            multiplier: randChoice([1, 1, 2, 2, 3, 4, 5, 7, 11]),
            panelLevel: randInt(25, 85),
            waveform: randChoice([0, 0, 1, 2, 3]),
            attackRate: randInt(35, 63),
            decay1Rate: randInt(10, 45),
            releaseRate: randInt(4, 9),
            fineDetune: randChoice([-1, 0, 0, 0, 1]),
            coarseDetune: Math.random() > 0.85,
            decay1Level: randInt(0, 6),
            decay2Rate: randInt(0, 15),
            sustainReleaseRate: randInt(3, 7),
            levelKeyScalingHigh: randChoice([0, 0, 2, 4]),
            levelKeyScalingLow: 0,
            rateKeyScaling: randChoice([0, 1]),
            amplitudeModulationEnable: Math.random() > 0.8
        },
        carrier: {
            multiplier: randChoice([1, 1, 1, 2, 3, 4]),
            panelLevel: randInt(70, 95),
            waveform: randChoice([0, 0, 0, 1, 2]),
            attackRate: randInt(40, 63),
            decay1Rate: randInt(12, 35),
            releaseRate: randInt(4, 8),
            fineDetune: 0,
            coarseDetune: false,
            decay1Level: randInt(0, 4),
            decay2Rate: randInt(0, 10),
            sustainReleaseRate: randInt(4, 7),
            levelKeyScalingHigh: randChoice([0, 0, 2]),
            levelKeyScalingLow: 0,
            rateKeyScaling: randChoice([0, 1]),
            amplitudeModulationEnable: false
        }
    };
    updateFmVoiceUI(voice);
}

function applyFmPreset(key) {
    if (!key || !FM_PRESETS[key]) return;
    const template = JSON.parse(JSON.stringify(FM_PRESETS[key]));
    template.bankNumber = selectedFmBank;
    updateFmVoiceUI(template);
}

function renderFmVoiceLibrary() {
    const countBadge = document.getElementById("fmVoiceCountBadge");
    const listEl = document.getElementById("fmPatchList");
    const filter = (document.getElementById("fmSearchInput")?.value || "").toLowerCase().trim();

    if (!listEl) return;
    const voices = state.fmVoices || [];
    if (countBadge) countBadge.textContent = `${voices.length} ${voices.length === 1 ? 'patch' : 'patches'}`;

    const filtered = filter 
        ? voices.filter(v => (v.name || v.fileName || "").toLowerCase().includes(filter))
        : voices;

    if (filtered.length === 0) {
        listEl.innerHTML = `
            <div class="p-6 text-center text-slate-500 text-[11px]">
                ${filter ? 'No patches match your filter.' : 'No saved FM patches. Edit parameters and click "Save Patch" above!'}
            </div>
        `;
        return;
    }

    listEl.innerHTML = filtered.map(v => {
        const patchName = v.name || (v.fileName ? v.fileName.replace(/\.syx$/i, "") : "Custom FM");
        const bankNum = (v.bankNumber !== undefined ? v.bankNumber + 1 : 1);
        const dateStr = v.date || "";
        return `
            <div class="p-2 rounded-lg bg-slate-900/90 hover:bg-slate-800/90 border border-slate-800 transition flex items-center justify-between group">
                <div class="min-w-0 flex-1 pr-2">
                    <div class="flex items-center space-x-1.5">
                        <span class="text-xs font-bold text-white truncate" title="${patchName}">${patchName}</span>
                        <span class="px-1 py-0.2 rounded text-[8px] font-bold bg-indigo-950 text-indigo-300 border border-indigo-800/60">B${bankNum}</span>
                    </div>
                    <div class="text-[9px] text-slate-400 truncate">${dateStr ? dateStr + ' • ' : ''}72 bytes SysEx</div>
                </div>
                <div class="flex items-center space-x-1 shrink-0">
                    <button type="button" data-file="${v.fileName || ''}" data-path="${v.filePath || ''}" class="btn-load-fm px-1.5 py-0.5 rounded text-[10px] font-semibold bg-cyan-950/80 hover:bg-cyan-900 text-cyan-300 border border-cyan-800/60 transition active:scale-95" title="Load into FM Sound Designer">Load</button>
                    <button type="button" data-path="${v.filePath || ''}" class="btn-del-fm p-1 rounded text-[10px] text-slate-500 hover:text-rose-400 hover:bg-slate-800 transition" title="Delete patch">🗑️</button>
                </div>
            </div>
        `;
    }).join("");

    listEl.querySelectorAll(".btn-load-fm").forEach(btn => {
        btn.addEventListener("click", () => {
            const filePath = btn.dataset.path;
            const fileName = btn.dataset.file;
            sendToHost("load_fm_voice_patch", { filePath, fileName });
        });
    });

    listEl.querySelectorAll(".btn-del-fm").forEach(btn => {
        btn.addEventListener("click", () => {
            const filePath = btn.dataset.path;
            if (confirm("Are you sure you want to delete this FM voice patch?")) {
                sendToHost("delete_fm_voice_patch", { filePath });
            }
        });
    });
}

function initFmSynthEditor() {
    // Bank pills
    document.querySelectorAll("#fmBankPills .fm-bank-btn").forEach(btn => {
        btn.addEventListener("click", () => {
            selectedFmBank = parseInt(btn.dataset.bank, 10);
            updateActiveFmBankDisplay(selectedFmBank, 100 + selectedFmBank);
            sendToHost("select_fm_bank", { bank: selectedFmBank });
            sendFmVoiceLiveThrottled();
        });
    });

    // Modulator Waveform buttons
    document.querySelectorAll("#fmModWaveGroup .fm-mod-wave").forEach(btn => {
        btn.addEventListener("click", () => {
            selectedModWave = parseInt(btn.dataset.wave, 10);
            document.querySelectorAll("#fmModWaveGroup .fm-mod-wave").forEach(b => {
                const w = parseInt(b.dataset.wave, 10);
                if (w === selectedModWave) {
                    b.className = "fm-mod-wave active py-1 rounded text-[9px] font-bold bg-cyan-600 text-white border border-cyan-400 transition";
                } else {
                    b.className = "fm-mod-wave py-1 rounded text-[9px] font-bold bg-slate-800 text-slate-300 hover:text-white border border-slate-700 transition";
                }
            });
            sendFmVoiceLiveThrottled();
        });
    });

    // Carrier Waveform buttons
    document.querySelectorAll("#fmCarWaveGroup .fm-car-wave").forEach(btn => {
        btn.addEventListener("click", () => {
            selectedCarWave = parseInt(btn.dataset.wave, 10);
            document.querySelectorAll("#fmCarWaveGroup .fm-car-wave").forEach(b => {
                const w = parseInt(b.dataset.wave, 10);
                if (w === selectedCarWave) {
                    b.className = "fm-car-wave active py-1 rounded text-[9px] font-bold bg-emerald-600 text-white border border-emerald-400 transition";
                } else {
                    b.className = "fm-car-wave py-1 rounded text-[9px] font-bold bg-slate-800 text-slate-300 hover:text-white border border-slate-700 transition";
                }
            });
            sendFmVoiceLiveThrottled();
        });
    });

    // Sliders
    const sliderDefs = [
        { id: "fmModMul", valId: "fmModMulVal", fmt: (v) => `${v} (${v == 0 ? '0.5x' : v + '.0x'})` },
        { id: "fmModTl", valId: "fmModTlVal" },
        { id: "fmModAr", valId: "fmModArVal" },
        { id: "fmModD1r", valId: "fmModD1rVal" },
        { id: "fmModRr", valId: "fmModRrVal" },
        { id: "fmModFine", valId: "fmModFineVal" },
        { id: "fmModD1l", valId: "fmModD1lVal" },
        { id: "fmModD2r", valId: "fmModD2rVal" },
        { id: "fmModSrr", valId: "fmModSrrVal" },
        { id: "fmModLksHi", valId: "fmModLksHiVal" },
        { id: "fmModLksLo", valId: "fmModLksLoVal" },
        { id: "fmModRks", valId: "fmModRksVal" },

        { id: "fmCarMul", valId: "fmCarMulVal", fmt: (v) => `${v} (${v == 0 ? '0.5x' : v + '.0x'})` },
        { id: "fmCarTl", valId: "fmCarTlVal" },
        { id: "fmCarAr", valId: "fmCarArVal" },
        { id: "fmCarD1r", valId: "fmCarD1rVal" },
        { id: "fmCarRr", valId: "fmCarRrVal" },
        { id: "fmCarFine", valId: "fmCarFineVal" },
        { id: "fmCarD1l", valId: "fmCarD1lVal" },
        { id: "fmCarD2r", valId: "fmCarD2rVal" },
        { id: "fmCarSrr", valId: "fmCarSrrVal" },
        { id: "fmCarLksHi", valId: "fmCarLksHiVal" },
        { id: "fmCarLksLo", valId: "fmCarLksLoVal" },
        { id: "fmCarRks", valId: "fmCarRksVal" },

        { id: "fmFeedback", valId: "fmFeedbackVal" },
        { id: "fmVibratoDelay", valId: "fmVibratoDelayVal", fmt: (v) => `${v} ms` },
        { id: "fmPitchModSens", valId: "fmPitchModSensVal" },
        { id: "fmAmpModSens", valId: "fmAmpModSensVal" }
    ];

    sliderDefs.forEach(def => {
        const slider = document.getElementById(def.id);
        const label = document.getElementById(def.valId);
        if (slider) {
            slider.addEventListener("input", () => {
                if (label) label.textContent = def.fmt ? def.fmt(slider.value) : slider.value;
                sendFmVoiceLiveThrottled();
                if (def.id.startsWith("fmMod") || def.id.startsWith("fmCar")) {
                    updateFmEnvelopes();
                }
            });
        }
    });

    // Checkboxes
    ["fmModCoarse", "fmModAm", "fmCarCoarse", "fmCarAm", "fmVibrato", "fmSustain"].forEach(id => {
        const chk = document.getElementById(id);
        if (chk) {
            chk.addEventListener("change", () => {
                sendFmVoiceLiveThrottled();
            });
        }
    });

    // Presets dropdown
    const selPresets = document.getElementById("selFmPresets");
    if (selPresets) {
        selPresets.addEventListener("change", () => {
            applyFmPreset(selPresets.value);
            selPresets.value = "";
        });
    }

    // Randomize button
    const btnRand = document.getElementById("btnFmRandomize");
    if (btnRand) {
        btnRand.addEventListener("click", randomizeFmVoice);
    }

    // Audition Note
    const btnAud = document.getElementById("btnFmAudition");
    if (btnAud) {
        btnAud.addEventListener("click", () => {
            sendToHost("audition_fm_voice_note", { note: 60, bank: selectedFmBank, isChord: false });
        });
    }

    document.querySelectorAll(".btn-fm-audition-note").forEach(btn => {
        btn.addEventListener("click", () => {
            const note = parseInt(btn.dataset.note || "60", 10);
            const isChord = btn.dataset.chord === "true";
            sendToHost("audition_fm_voice_note", { note, bank: selectedFmBank, isChord });
        });
    });

    // Transmit to Synth
    const btnTrans = document.getElementById("btnFmTransmit");
    if (btnTrans) {
        btnTrans.addEventListener("click", () => {
            const voice = collectFmVoiceFromUI();
            sendToHost("transmit_fm_voice", { voice, silent: false });
        });
    }

    // Save Patch
    const btnSave = document.getElementById("btnFmSave");
    if (btnSave) {
        btnSave.addEventListener("click", () => {
            const voice = collectFmVoiceFromUI();
            sendToHost("save_fm_voice_patch", { voice });
        });
    }

    // Search filter
    const searchIn = document.getElementById("fmSearchInput");
    if (searchIn) {
        searchIn.addEventListener("input", renderFmVoiceLibrary);
    }

    renderFmVoiceLibrary();
    updateFmEnvelopes();
}

// -------------------------------------------------------------------------
// PSS-790: VECTOR SYNTHESIZER
// -------------------------------------------------------------------------
let vectorState = {
    x: 0.0,
    y: 0.0,
    voiceA: 0,
    voiceB: 34,
    voiceC: 16,
    voiceD: 56,
    orbitMode: "manual",
    orbitSpeed: 3.0,
    isOrbiting: false,
    animFrameId: null,
    orbitStartTime: null,
    lastVolumeSendTime: 0
};

const VECTOR_PRESETS = {
    "aha_take_on_me": {
        "name": "Take On Me Vector Lead",
        "voiceA": 65,
        "voiceB": 65,
        "voiceC": 0,
        "voiceD": 7,
        "posX": 0.38,
        "posY": 0.88,
        "orbitMode": "wander",
        "orbitSpeed": 1.4
    },
    "van_halen_jump": {
        "name": "Jump OB Vector Brass",
        "voiceA": 65,
        "voiceB": 66,
        "voiceC": 65,
        "voiceD": 74,
        "posX": 0.12,
        "posY": 0.69,
        "orbitMode": "pingpong",
        "orbitSpeed": 4.3
    },
    "dx7_tine_ep": {
        "name": "Ballad Tine & Crystal EP",
        "voiceA": 1,
        "voiceB": 7,
        "voiceC": 2,
        "voiceD": 74,
        "posX": -0.19,
        "posY": 0.44,
        "orbitMode": "wander",
        "orbitSpeed": 1.7
    },
    "haddaway_lately_bass": {
        "name": "What Is Love Club Bass",
        "voiceA": 91,
        "voiceB": 84,
        "voiceC": 6,
        "voiceD": 92,
        "posX": -0.12,
        "posY": 0.56,
        "orbitMode": "wander",
        "orbitSpeed": 1.2
    },
    "eurythmics_sweet_dreams": {
        "name": "Sweet Dreams Vector Pulse",
        "voiceA": 71,
        "voiceB": 91,
        "voiceC": 11,
        "voiceD": 34,
        "posX": 0.25,
        "posY": 0.5,
        "orbitMode": "manual",
        "orbitSpeed": 3.0
    },
    "new_order_blue_monday": {
        "name": "Blue Monday Choir & Bass",
        "voiceA": 92,
        "voiceB": 64,
        "voiceC": 84,
        "voiceD": 81,
        "posX": 0.19,
        "posY": 0.44,
        "orbitMode": "wander",
        "orbitSpeed": 1.6
    },
    "danger_zone_slap": {
        "name": "Danger Zone Slap & Brass",
        "voiceA": 84,
        "voiceB": 65,
        "voiceC": 86,
        "voiceD": 15,
        "posX": 0.12,
        "posY": 0.62,
        "orbitMode": "wander",
        "orbitSpeed": 1.2
    },
    "axel_f_brass": {
        "name": "Axel F Marimba-Brass Lead",
        "voiceA": 66,
        "voiceB": 43,
        "voiceC": 71,
        "voiceD": 98,
        "posX": 0.31,
        "posY": 0.56,
        "orbitMode": "wander",
        "orbitSpeed": 1.2
    },
    "daft_punk_hbfs": {
        "name": "Harder Better Talk-Lead",
        "voiceA": 72,
        "voiceB": 81,
        "voiceC": 6,
        "voiceD": 71,
        "posX": 0.44,
        "posY": 0.5,
        "orbitMode": "circle",
        "orbitSpeed": 1.9
    },
    "stranger_things_arp": {
        "name": "Stranger Things Analog Vector",
        "voiceA": 91,
        "voiceB": 75,
        "voiceC": 65,
        "voiceD": 74,
        "posX": 0.25,
        "posY": 0.44,
        "orbitMode": "pingpong",
        "orbitSpeed": 3.8
    },
    "vangelis_blade_runner_pad": {
        "name": "Blade Runner CS-80 Vector Pad",
        "voiceA": 65,
        "voiceB": 74,
        "voiceC": 35,
        "voiceD": 76,
        "posX": 0.19,
        "posY": 0.31,
        "orbitMode": "pingpong",
        "orbitSpeed": 7.5
    },
    "toto_africa_pad": {
        "name": "Africa Kalimba & Polysynth",
        "voiceA": 75,
        "voiceB": 43,
        "voiceC": 66,
        "voiceD": 74,
        "posX": -0.12,
        "posY": 0.38,
        "orbitMode": "wander",
        "orbitSpeed": 2.0
    },
    "enya_orinoco_pad": {
        "name": "Orinoco Pizzicato & Choir",
        "voiceA": 36,
        "voiceB": 64,
        "voiceC": 37,
        "voiceD": 82,
        "posX": 0.12,
        "posY": 0.25,
        "orbitMode": "pingpong",
        "orbitSpeed": 5.0
    },
    "wham_last_christmas_bell": {
        "name": "Last Christmas Sleigh & Bell",
        "voiceA": 40,
        "voiceB": 43,
        "voiceC": 42,
        "voiceD": 7,
        "posX": 0.19,
        "posY": 0.5,
        "orbitMode": "wander",
        "orbitSpeed": 2.1
    },
    "phil_collins_air_bell": {
        "name": "In the Air Glass & Atmosphere",
        "voiceA": 93,
        "voiceB": 82,
        "voiceC": 7,
        "voiceD": 64,
        "posX": 0.31,
        "posY": 0.38,
        "orbitMode": "pingpong",
        "orbitSpeed": 6.0
    },
    "madonna_fm_marimba": {
        "name": "80s Wood Marimba & Vibe",
        "voiceA": 43,
        "voiceB": 7,
        "voiceC": 98,
        "voiceD": 91,
        "posX": 0.12,
        "posY": 0.56,
        "orbitMode": "manual",
        "orbitSpeed": 2.5
    },
    "sonic_steel_drum": {
        "name": "Green Hill Steel Drum Ensemble",
        "voiceA": 41,
        "voiceB": 43,
        "voiceC": 7,
        "voiceD": 52,
        "posX": 0.19,
        "posY": 0.75,
        "orbitMode": "wander",
        "orbitSpeed": 1.5
    },
    "gated_fm_snare": {
        "name": "80s Gated Timpani & Metal Hit",
        "voiceA": 95,
        "voiceB": 99,
        "voiceC": 76,
        "voiceD": 92,
        "posX": 0.12,
        "posY": 0.31,
        "orbitMode": "wander",
        "orbitSpeed": 1.0
    },
    "planet_rock_orch_hit": {
        "name": "Planet Rock Fairlight Hit",
        "voiceA": 99,
        "voiceB": 52,
        "voiceC": 95,
        "voiceD": 64,
        "posX": 0.19,
        "posY": 0.69,
        "orbitMode": "manual",
        "orbitSpeed": 1.9
    },
    "electro_metallic_cowbell": {
        "name": "Blue Monday Metallic Clank",
        "voiceA": 40,
        "voiceB": 76,
        "voiceC": 98,
        "voiceD": 41,
        "posX": 0.38,
        "posY": 0.25,
        "orbitMode": "manual",
        "orbitSpeed": 1.2
    },
    "sonic_knuckles_sky_sanctuary": {
        "name": "Sky Sanctuary Harp & Crystal",
        "voiceA": 37,
        "voiceB": 7,
        "voiceC": 5,
        "voiceD": 34,
        "posX": 0.25,
        "posY": 0.56,
        "orbitMode": "wander",
        "orbitSpeed": 1.7
    },
    "sonic2_chemical_plant_bass": {
        "name": "Chemical Plant Slap & Synth",
        "voiceA": 84,
        "voiceB": 92,
        "voiceC": 71,
        "voiceD": 73,
        "posX": 0.31,
        "posY": 0.62,
        "orbitMode": "wander",
        "orbitSpeed": 1.1
    },
    "sor2_go_straight_bass": {
        "name": "Go Straight Rave Stab & Bass",
        "voiceA": 92,
        "voiceB": 65,
        "voiceC": 9,
        "voiceD": 99,
        "posX": 0.19,
        "posY": 0.56,
        "orbitMode": "wander",
        "orbitSpeed": 1.3
    },
    "outrun_magical_sound_marimba": {
        "name": "Magical Sound Steel & Marimba",
        "voiceA": 43,
        "voiceB": 41,
        "voiceC": 29,
        "voiceD": 65,
        "posX": 0.38,
        "posY": 0.5,
        "orbitMode": "manual",
        "orbitSpeed": 2.1
    },
    "thunder_force_iv_guitar": {
        "name": "Metal Squad Vector Shred",
        "voiceA": 15,
        "voiceB": 15,
        "voiceC": 65,
        "voiceD": 73,
        "posX": 0.25,
        "posY": 0.75,
        "orbitMode": "pingpong",
        "orbitSpeed": 2.7
    },
    "toejam_earl_funk_bass": {
        "name": "Big Earl Slap & Clavinet",
        "voiceA": 84,
        "voiceB": 6,
        "voiceC": 85,
        "voiceD": 45,
        "posX": 0.19,
        "posY": 0.44,
        "orbitMode": "wander",
        "orbitSpeed": 1.4
    },
    "castlevania_bloodlines_harpsichord": {
        "name": "Reincarnated Gothic Harpsichord",
        "voiceA": 5,
        "voiceB": 37,
        "voiceC": 34,
        "voiceD": 64,
        "posX": 0.12,
        "posY": 0.69,
        "orbitMode": "wander",
        "orbitSpeed": 1.8
    },
    "phantasy_star_iv_brass": {
        "name": "Algol Battle Vector Brass",
        "voiceA": 52,
        "voiceB": 65,
        "voiceC": 50,
        "voiceD": 34,
        "posX": 0.25,
        "posY": 0.62,
        "orbitMode": "wander",
        "orbitSpeed": 1.6
    },
    "after_burner_ii_lead": {
        "name": "Final Take Off Arcade Lead",
        "voiceA": 65,
        "voiceB": 15,
        "voiceC": 65,
        "voiceD": 44,
        "posX": 0.31,
        "posY": 0.56,
        "orbitMode": "pingpong",
        "orbitSpeed": 2.5
    },
    "shinobi_iii_shakuhachi": {
        "name": "Whirlwind Shakuhachi & Koto",
        "voiceA": 81,
        "voiceB": 81,
        "voiceC": 37,
        "voiceD": 70,
        "posX": -0.19,
        "posY": 0.69,
        "orbitMode": "wander",
        "orbitSpeed": 1.4
    }
};

function calculateVectorWeights(x, y) {
    const rawA = Math.max(0, y);
    const rawB = Math.max(0, x);
    const rawC = Math.max(0, -y);
    const rawD = Math.max(0, -x);

    const manhattan = Math.abs(x) + Math.abs(y);
    const radius = Math.min(1.0, Math.sqrt(x * x + y * y));

    let dirA = 0.25, dirB = 0.25, dirC = 0.25, dirD = 0.25;
    if (manhattan > 0.0001) {
        dirA = rawA / manhattan;
        dirB = rawB / manhattan;
        dirC = rawC / manhattan;
        dirD = rawD / manhattan;
    }

    const wA = (1.0 - radius) * 0.25 + radius * dirA;
    const wB = (1.0 - radius) * 0.25 + radius * dirB;
    const wC = (1.0 - radius) * 0.25 + radius * dirC;
    const wD = (1.0 - radius) * 0.25 + radius * dirD;

    return { wA, wB, wC, wD };
}

function drawVectorCanvas() {
    const canvas = document.getElementById("vecCanvas");
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const w = canvas.width;
    const h = canvas.height;
    const cx = w / 2;
    const cy = h / 2;
    const scale = w * 0.4;

    ctx.clearRect(0, 0, w, h);

    // Subtle dark gradient background
    const bgGrad = ctx.createRadialGradient(cx, cy, 10, cx, cy, w * 0.5);
    bgGrad.addColorStop(0, "rgba(30, 41, 59, 0.6)");
    bgGrad.addColorStop(1, "rgba(10, 15, 29, 0.95)");
    ctx.fillStyle = bgGrad;
    ctx.fillRect(0, 0, w, h);

    // Grid circles
    ctx.strokeStyle = "rgba(71, 85, 105, 0.35)";
    ctx.lineWidth = 1;
    [0.25, 0.5, 0.75, 1.0].forEach(r => {
        ctx.beginPath();
        ctx.arc(cx, cy, scale * r, 0, Math.PI * 2);
        ctx.stroke();
    });

    // Crosshairs
    ctx.beginPath();
    ctx.moveTo(cx, cy - scale);
    ctx.lineTo(cx, cy + scale);
    ctx.moveTo(cx - scale, cy);
    ctx.lineTo(cx + scale, cy);
    ctx.stroke();

    // Cardinal Labels
    ctx.font = "bold 10px Inter, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";

    ctx.fillStyle = "#22d3ee"; // Cyan - Voice A
    ctx.fillText("A", cx, cy - scale - 8);

    ctx.fillStyle = "#34d399"; // Emerald - Voice B
    ctx.fillText("B", cx + scale + 8, cy);

    ctx.fillStyle = "#fbbf24"; // Amber - Voice C
    ctx.fillText("C", cx, cy + scale + 8);

    ctx.fillStyle = "#fb7185"; // Rose - Voice D
    ctx.fillText("D", cx - scale - 8, cy);

    // Puck coordinate
    const puckX = cx + vectorState.x * scale;
    const puckY = cy - vectorState.y * scale;

    // Line from center
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.lineTo(puckX, puckY);
    ctx.strokeStyle = "rgba(99, 102, 241, 0.6)";
    ctx.lineWidth = 2;
    ctx.stroke();

    // Puck outer glow
    const puckGrad = ctx.createRadialGradient(puckX, puckY, 2, puckX, puckY, 14);
    puckGrad.addColorStop(0, "rgba(99, 102, 241, 1)");
    puckGrad.addColorStop(0.5, "rgba(56, 189, 248, 0.7)");
    puckGrad.addColorStop(1, "rgba(99, 102, 241, 0)");
    ctx.fillStyle = puckGrad;
    ctx.beginPath();
    ctx.arc(puckX, puckY, 14, 0, Math.PI * 2);
    ctx.fill();

    // Puck center dot
    ctx.fillStyle = "#ffffff";
    ctx.beginPath();
    ctx.arc(puckX, puckY, 4, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = "#4f46e5";
    ctx.lineWidth = 1.5;
    ctx.stroke();
}

function setVectorPosition(x, y, notifyHost = true) {
    vectorState.x = Math.max(-1.0, Math.min(1.0, x));
    vectorState.y = Math.max(-1.0, Math.min(1.0, y));

    const weights = calculateVectorWeights(vectorState.x, vectorState.y);

    const coordX = document.getElementById("vecCoordX");
    const coordY = document.getElementById("vecCoordY");
    const volA = document.getElementById("vecVolA");
    const volB = document.getElementById("vecVolB");
    const volC = document.getElementById("vecVolC");
    const volD = document.getElementById("vecVolD");

    if (coordX) coordX.textContent = (vectorState.x >= 0 ? "+" : "") + vectorState.x.toFixed(2);
    if (coordY) coordY.textContent = (vectorState.y >= 0 ? "+" : "") + vectorState.y.toFixed(2);
    if (volA) volA.textContent = Math.round(weights.wA * 100) + "%";
    if (volB) volB.textContent = Math.round(weights.wB * 100) + "%";
    if (volC) volC.textContent = Math.round(weights.wC * 100) + "%";
    if (volD) volD.textContent = Math.round(weights.wD * 100) + "%";

    const m1 = document.getElementById("meterVecCh1");
    const m2 = document.getElementById("meterVecCh2");
    const m3 = document.getElementById("meterVecCh3");
    const m4 = document.getElementById("meterVecCh4");

    if (m1) m1.style.height = Math.round(weights.wA * 100) + "%";
    if (m2) m2.style.height = Math.round(weights.wB * 100) + "%";
    if (m3) m3.style.height = Math.round(weights.wC * 100) + "%";
    if (m4) m4.style.height = Math.round(weights.wD * 100) + "%";

    drawVectorCanvas();

    if (notifyHost) {
        const now = performance.now();
        if (now - vectorState.lastVolumeSendTime > 25) {
            vectorState.lastVolumeSendTime = now;
            sendToHost("set_vector_volumes", {
                volA: Math.round(weights.wA * 127),
                volB: Math.round(weights.wB * 127),
                volC: Math.round(weights.wC * 127),
                volD: Math.round(weights.wD * 127)
            });
        }
    }
}

function setupVectorTouchpad() {
    const canvas = document.getElementById("vecCanvas");
    if (!canvas) return;

    let isDragging = false;

    function handlePointer(e) {
        const rect = canvas.getBoundingClientRect();
        const clientX = e.clientX !== undefined ? e.clientX : (e.touches && e.touches[0] ? e.touches[0].clientX : 0);
        const clientY = e.clientY !== undefined ? e.clientY : (e.touches && e.touches[0] ? e.touches[0].clientY : 0);

        const mouseX = clientX - rect.left;
        const mouseY = clientY - rect.top;
        const cX = rect.width / 2;
        const cY = rect.height / 2;
        const scale = rect.width * 0.4;

        let x = (mouseX - cX) / scale;
        let y = (cY - mouseY) / scale;

        if (vectorState.isOrbiting) {
            stopVectorOrbit();
        }

        setVectorPosition(x, y, true);
    }

    canvas.addEventListener("pointerdown", (e) => {
        isDragging = true;
        canvas.setPointerCapture(e.pointerId);
        handlePointer(e);
    });

    canvas.addEventListener("pointermove", (e) => {
        if (!isDragging) return;
        handlePointer(e);
    });

    const stopDragging = (e) => {
        if (!isDragging) return;
        isDragging = false;
        try {
            if (canvas.hasPointerCapture(e.pointerId)) {
                canvas.releasePointerCapture(e.pointerId);
            }
        } catch { }
    };

    canvas.addEventListener("pointerup", stopDragging);
    canvas.addEventListener("pointercancel", stopDragging);
}

function startVectorOrbit() {
    if (vectorState.isOrbiting) return;
    const mode = document.getElementById("selVecOrbitMode")?.value || "circle";
    if (mode === "manual") return;

    vectorState.isOrbiting = true;
    vectorState.orbitStartTime = performance.now();

    const btnIcon = document.getElementById("vecOrbitBtnIcon");
    const btnLabel = document.getElementById("vecOrbitBtnLabel");
    const badge = document.getElementById("vecOrbitStatusBadge");
    if (btnIcon) btnIcon.textContent = "⏸";
    if (btnLabel) btnLabel.textContent = "Pause Vector Orbit";
    if (badge) {
        badge.textContent = "Orbiting";
        badge.className = "px-1.5 py-0.5 rounded text-[9px] font-bold bg-indigo-950/80 text-indigo-300 border border-indigo-700/60";
    }

    function step(now) {
        if (!vectorState.isOrbiting) return;
        const elapsed = (now - vectorState.orbitStartTime) / 1000.0;
        const speedVal = parseFloat(document.getElementById("vecOrbitSpeed")?.value || 30);
        const period = Math.max(0.5, speedVal / 10.0);
        const theta = (elapsed / period) * (Math.PI * 2);

        const currentMode = document.getElementById("selVecOrbitMode")?.value || "circle";
        let x = 0, y = 0;

        switch (currentMode) {
            case "circle":
                x = Math.cos(theta) * 0.75;
                y = Math.sin(theta) * 0.75;
                break;
            case "figure8":
                x = Math.sin(theta) * 0.8;
                y = Math.sin(theta * 2) * 0.75;
                break;
            case "spiral": {
                const r = 0.25 + 0.6 * (0.5 + 0.5 * Math.sin(theta * 0.25));
                x = Math.cos(theta) * r;
                y = Math.sin(theta) * r;
                break;
            }
            case "pingpong": {
                const s = Math.sin(theta);
                x = s * 0.8;
                y = s * 0.8;
                break;
            }
            case "wander":
                x = Math.max(-0.9, Math.min(0.9, 0.5 * Math.sin(theta) + 0.35 * Math.sin(2.3 * theta + 1.2)));
                y = Math.max(-0.9, Math.min(0.9, 0.5 * Math.cos(0.9 * theta) + 0.35 * Math.cos(1.7 * theta + 2.5)));
                break;
            default:
                stopVectorOrbit();
                return;
        }

        setVectorPosition(x, y, true);
        vectorState.animFrameId = requestAnimationFrame(step);
    }

    vectorState.animFrameId = requestAnimationFrame(step);
}

function stopVectorOrbit() {
    vectorState.isOrbiting = false;
    if (vectorState.animFrameId) {
        cancelAnimationFrame(vectorState.animFrameId);
        vectorState.animFrameId = null;
    }
    const btnIcon = document.getElementById("vecOrbitBtnIcon");
    const btnLabel = document.getElementById("vecOrbitBtnLabel");
    const badge = document.getElementById("vecOrbitStatusBadge");
    if (btnIcon) btnIcon.textContent = "▶";
    if (btnLabel) btnLabel.textContent = "Start Vector Orbit";
    if (badge) {
        badge.textContent = "Manual";
        badge.className = "px-1.5 py-0.5 rounded text-[9px] font-bold bg-slate-800 text-slate-400 border border-slate-700";
    }
}

function initVectorVoiceDropdowns() {
    const list = state.pss790Voices && state.pss790Voices.length > 0 ? state.pss790Voices : (state.voices || []);
    if (!list || list.length === 0) return;

    ["vecVoiceA", "vecVoiceB", "vecVoiceC", "vecVoiceD"].forEach((id, idx) => {
        const select = document.getElementById(id);
        if (!select) return;
        const currentVal = parseInt(select.value, 10);
        select.innerHTML = list.map(v => 
            `<option value="${v.number}">${v.number.toString().padStart(2, '0')}: ${v.name}</option>`
        ).join("");

        if (!isNaN(currentVal) && currentVal >= 0) {
            select.value = currentVal;
        } else {
            const defaults = [0, 34, 16, 56];
            select.value = defaults[idx] !== undefined ? defaults[idx] : 0;
        }
    });
}

function onVectorVoiceChanged() {
    const vA = parseInt(document.getElementById("vecVoiceA")?.value || 0, 10);
    const vB = parseInt(document.getElementById("vecVoiceB")?.value || 34, 10);
    const vC = parseInt(document.getElementById("vecVoiceC")?.value || 16, 10);
    const vD = parseInt(document.getElementById("vecVoiceD")?.value || 56, 10);
    vectorState.voiceA = vA;
    vectorState.voiceB = vB;
    vectorState.voiceC = vC;
    vectorState.voiceD = vD;
    sendToHost("set_vector_voices", { voiceA: vA, voiceB: vB, voiceC: vC, voiceD: vD });
}

function applyVectorPreset(key) {
    if (!key || !VECTOR_PRESETS[key]) return;
    const template = JSON.parse(JSON.stringify(VECTOR_PRESETS[key]));
    updateVectorPresetUI(template);
}

function updateVectorPresetUI(preset) {
    if (!preset) return;
    if (preset.name) {
        const nameEl = document.getElementById("vecPresetName");
        if (nameEl) nameEl.value = preset.name;
    }
    if (preset.voiceA !== undefined) {
        const sel = document.getElementById("vecVoiceA");
        if (sel) sel.value = preset.voiceA;
        vectorState.voiceA = preset.voiceA;
    }
    if (preset.voiceB !== undefined) {
        const sel = document.getElementById("vecVoiceB");
        if (sel) sel.value = preset.voiceB;
        vectorState.voiceB = preset.voiceB;
    }
    if (preset.voiceC !== undefined) {
        const sel = document.getElementById("vecVoiceC");
        if (sel) sel.value = preset.voiceC;
        vectorState.voiceC = preset.voiceC;
    }
    if (preset.voiceD !== undefined) {
        const sel = document.getElementById("vecVoiceD");
        if (sel) sel.value = preset.voiceD;
        vectorState.voiceD = preset.voiceD;
    }

    sendToHost("set_vector_voices", {
        voiceA: vectorState.voiceA,
        voiceB: vectorState.voiceB,
        voiceC: vectorState.voiceC,
        voiceD: vectorState.voiceD
    });

    if (preset.orbitMode) {
        const selMode = document.getElementById("selVecOrbitMode");
        if (selMode) selMode.value = preset.orbitMode;
    }
    if (preset.orbitSpeed !== undefined) {
        const spdInput = document.getElementById("vecOrbitSpeed");
        const spdVal = document.getElementById("vecOrbitSpeedVal");
        if (spdInput) spdInput.value = Math.round(preset.orbitSpeed * 10);
        if (spdVal) spdVal.textContent = (preset.orbitSpeed).toFixed(1) + "s";
        vectorState.orbitSpeed = preset.orbitSpeed;
    }

    const posX = preset.posX !== undefined ? preset.posX : 0.0;
    const posY = preset.posY !== undefined ? preset.posY : 0.0;
    setVectorPosition(posX, posY, true);

    if (preset.orbitMode && preset.orbitMode !== "manual") {
        startVectorOrbit();
    } else {
        stopVectorOrbit();
    }
}

function renderVectorPresetLibrary() {
    const countBadge = document.getElementById("vecPresetCountBadge");
    const listEl = document.getElementById("vecPresetList");

    if (!listEl) return;
    const presets = state.vectorPresets || [];
    if (countBadge) countBadge.textContent = `${presets.length} ${presets.length === 1 ? 'preset' : 'presets'}`;

    if (presets.length === 0) {
        listEl.innerHTML = `
            <div class="p-3 text-center text-slate-500 text-[10px]">
                No custom vector presets. Click "Save Preset" to store your favorite blends!
            </div>
        `;
        return;
    }

    listEl.innerHTML = presets.map(p => {
        const presetName = p.name || (p.fileName ? p.fileName.replace(/\.json$/i, "") : "Vector Preset");
        const dateStr = p.date || "";
        return `
            <div class="p-1.5 rounded-lg bg-slate-900/90 hover:bg-slate-800/90 border border-slate-800 transition flex items-center justify-between group">
                <div class="min-w-0 flex-1 pr-1.5">
                    <span class="text-[11px] font-bold text-white truncate block" title="${presetName}">${presetName}</span>
                    <span class="text-[9px] text-slate-400 block">${dateStr}</span>
                </div>
                <div class="flex items-center space-x-1 shrink-0">
                    <button type="button" data-path="${p.filePath || ''}" data-file="${p.fileName || ''}" class="btn-load-vec px-1.5 py-0.5 rounded text-[10px] font-semibold bg-indigo-950/80 hover:bg-indigo-900 text-indigo-300 border border-indigo-800/60 transition active:scale-95" title="Load Vector Preset">Load</button>
                    <button type="button" data-path="${p.filePath || ''}" class="btn-del-vec p-1 rounded text-[10px] text-slate-500 hover:text-rose-400 hover:bg-slate-800 transition" title="Delete preset">🗑️</button>
                </div>
            </div>
        `;
    }).join("");

    listEl.querySelectorAll(".btn-load-vec").forEach(btn => {
        btn.addEventListener("click", () => {
            const filePath = btn.dataset.path;
            const fileName = btn.dataset.file;
            sendToHost("load_vector_preset", { filePath, fileName });
        });
    });

    listEl.querySelectorAll(".btn-del-vec").forEach(btn => {
        btn.addEventListener("click", () => {
            const filePath = btn.dataset.path;
            if (confirm("Are you sure you want to delete this vector preset?")) {
                sendToHost("delete_vector_preset", { filePath });
            }
        });
    });
}

function initVectorSynthesizer() {
    setupVectorTouchpad();
    initVectorVoiceDropdowns();

    ["vecVoiceA", "vecVoiceB", "vecVoiceC", "vecVoiceD"].forEach(id => {
        const sel = document.getElementById(id);
        if (sel) {
            sel.addEventListener("change", onVectorVoiceChanged);
        }
    });

    const selPresets = document.getElementById("selVecPresets");
    if (selPresets) {
        selPresets.addEventListener("change", () => {
            applyVectorPreset(selPresets.value);
            selPresets.value = "";
        });
    }

    const spdSlider = document.getElementById("vecOrbitSpeed");
    const spdVal = document.getElementById("vecOrbitSpeedVal");
    if (spdSlider) {
        spdSlider.addEventListener("input", () => {
            const val = (parseFloat(spdSlider.value) / 10.0).toFixed(1) + "s";
            if (spdVal) spdVal.textContent = val;
            vectorState.orbitSpeed = parseFloat(spdSlider.value) / 10.0;
        });
    }

    const selOrbitMode = document.getElementById("selVecOrbitMode");
    if (selOrbitMode) {
        selOrbitMode.addEventListener("change", () => {
            vectorState.orbitMode = selOrbitMode.value;
            if (selOrbitMode.value === "manual") {
                stopVectorOrbit();
            } else if (vectorState.isOrbiting) {
                vectorState.orbitStartTime = performance.now();
            }
        });
    }

    const btnToggleOrbit = document.getElementById("btnVecToggleOrbit");
    if (btnToggleOrbit) {
        btnToggleOrbit.addEventListener("click", () => {
            if (vectorState.isOrbiting) {
                stopVectorOrbit();
            } else {
                startVectorOrbit();
            }
        });
    }

    const btnAud = document.getElementById("btnVecAudition");
    if (btnAud) {
        btnAud.addEventListener("click", () => {
            sendToHost("audition_vector_chord", { note: 60 });
        });
    }

    const btnSave = document.getElementById("btnVecSave");
    if (btnSave) {
        btnSave.addEventListener("click", () => {
            const pName = document.getElementById("vecPresetName")?.value.trim() || "Vector Preset";
            const vA = parseInt(document.getElementById("vecVoiceA")?.value || 0, 10);
            const vB = parseInt(document.getElementById("vecVoiceB")?.value || 34, 10);
            const vC = parseInt(document.getElementById("vecVoiceC")?.value || 16, 10);
            const vD = parseInt(document.getElementById("vecVoiceD")?.value || 56, 10);
            const orbitMode = document.getElementById("selVecOrbitMode")?.value || "manual";
            const orbitSpeed = parseFloat(document.getElementById("vecOrbitSpeed")?.value || 30) / 10.0;

            sendToHost("save_vector_preset", {
                name: pName,
                voiceA: vA,
                voiceB: vB,
                voiceC: vC,
                voiceD: vD,
                posX: vectorState.x,
                posY: vectorState.y,
                orbitMode: orbitMode,
                orbitSpeed: orbitSpeed
            });
        });
    }

    setVectorPosition(0, 0, false);
    renderVectorPresetLibrary();
}

function initModelSelector() {
    const btn = document.getElementById("modelSelectorBtn");
    const menu = document.getElementById("modelDropdownMenu");
    const chevron = document.getElementById("modelDropdownChevron");

    if (!btn || !menu) return;

    function openMenu() {
        menu.classList.remove("hidden");
        requestAnimationFrame(() => {
            menu.classList.remove("opacity-0", "scale-95");
            menu.classList.add("opacity-100", "scale-100");
        });
        btn.setAttribute("aria-expanded", "true");
        if (chevron) chevron.classList.add("rotate-180");
    }

    function closeMenu() {
        menu.classList.remove("opacity-100", "scale-100");
        menu.classList.add("opacity-0", "scale-95");
        setTimeout(() => {
            menu.classList.add("hidden");
        }, 150);
        btn.setAttribute("aria-expanded", "false");
        if (chevron) chevron.classList.remove("rotate-180");
    }

    btn.addEventListener("click", (e) => {
        e.stopPropagation();
        const isOpen = !menu.classList.contains("hidden");
        if (isOpen) closeMenu();
        else openMenu();
    });

    document.addEventListener("click", (e) => {
        if (!menu.contains(e.target) && !btn.contains(e.target)) {
            closeMenu();
        }
    });

    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && !menu.classList.contains("hidden")) {
            closeMenu();
        }
    });

    menu.querySelectorAll(".model-option-btn").forEach(optBtn => {
        optBtn.addEventListener("click", () => {
            const modelKey = optBtn.dataset.model;
            applyActiveModel(modelKey);
            closeMenu();
        });
    });

    const savedModel = localStorage.getItem("yamaha_studio_model") || "pss790";
    applyActiveModel(savedModel);
}

// Trigger initial handshake and keyboard shortcuts
function startApp() {
    initTheme();
    setupKeyboardShortcuts();
    initSidebarState();
    initSidebarResizer();
    initVuMeters();
    renderRecordingsLibrary();
    initModelSelector();
    initHandheldMode();

    // Sound Designer & SysEx Workstation initializers
    initSysexViewSwitcher();
    initFmSynthEditor();
    initVectorSynthesizer();
    updateSysexDeckVisibility();

    // Immediately render initial mixer strips, device dropdowns, and song tree so the UI is never blank
    renderMixer();
    renderDeviceSelect(state.selectedDevice);
    renderInputDeviceSelect(state.selectedInputDevice);
    renderLibrarySelect();
    renderLibraryTree();

    sendToHost("get_initial_data");

    // Watchdog fallback: if initial_data has not arrived after 600ms, retry handshake
    let watchdogCount = 0;
    const watchdogInterval = setInterval(() => {
        if (state.initialDataReceived || watchdogCount >= 5) {
            clearInterval(watchdogInterval);
            return;
        }
        watchdogCount++;
        console.log(`Initial data watchdog trigger #${watchdogCount}, re-requesting get_initial_data...`);
        sendToHost("get_initial_data");
    }, 600);
}

if (document.readyState === "loading") {
    window.addEventListener("DOMContentLoaded", startApp);
} else {
    startApp();
}
