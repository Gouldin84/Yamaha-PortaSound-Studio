// PSS-790 Modern UI Application Logic & MIDI Studio Engine

let state = {
    activeTab: "player",
    devices: [],
    inputDevices: [],
    selectedDevice: null,
    selectedInputDevice: null,
    voices: [],
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
    // Jukebox & Quick-Pick Tree State
    isSidebarCollapsed: false,
    selectedSongPath: null,
    searchQuery: "",
    expandedFolders: new Set(),
    collapsedCategories: new Set(),
    visibleSongList: [],
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
        isSolo: false
    })),
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
    activePreviewRecordingPath: null
};

// --- Theme Management ---
function applyTheme(theme) {
    state.theme = theme;
    const isLight = theme === "light";
    document.body.classList.toggle("theme-light", isLight);
    const icon = document.getElementById("themeToggleIcon");
    const text = document.getElementById("themeToggleText");
    if (icon) icon.textContent = isLight ? "🌙" : "☀️";
    if (text) text.textContent = isLight ? "Dark" : "Light";
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
    }
    if (isRecorder) {
        renderRecordingsLibrary();
        sendToHost("get_recordings_library");
    }
}

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
            state.inputDevices = msg.data.inputDevices || [];
            state.voices = msg.data.voices || [];
            state.library = msg.data.library || { gmFiles: [], pssFiles: [], flatFiles: [], hasFiles: false };
            if (msg.data.recorderBpm) {
                state.recorderBpm = msg.data.recorderBpm;
                document.getElementById("recorderBpmInput").value = state.recorderBpm;
                document.getElementById("recorderBpmSlider").value = state.recorderBpm;
            }
            if (msg.data.midiThru !== undefined) {
                document.getElementById("chkMidiThru").checked = msg.data.midiThru;
            }
            if (msg.data.defaultBatchSource) document.getElementById("batchSourceDir").value = msg.data.defaultBatchSource;
            if (msg.data.defaultBatchTarget) document.getElementById("batchTargetDir").value = msg.data.defaultBatchTarget;
            if (msg.data.sysexLibrary) {
                state.sysexLibrary = msg.data.sysexLibrary;
                renderSysExLibrary();
            }
            if (msg.data.recordingsLibrary) {
                handleRecordingsLibraryUpdated(msg.data.recordingsLibrary);
            }
            renderDeviceSelect(msg.data.defaultDevice);
            renderInputDeviceSelect(msg.data.defaultInputDevice);
            renderLibrarySelect();
            renderLibraryTree();
            renderMixer();
            renderRecorderChannels();
            initSidebarState();
            renderHealthBadge(null);
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
                    }
                });
                renderMixer();
            }
            highlightActiveSongInTree();
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
            showToast(`Saved to ${msg.data.isPss790 ? 'PSS-790 Midi' : 'GM Midi'}: ${msg.data.fileName}`);
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

    if (format === "pss790") {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-emerald-950 text-emerald-400 border border-emerald-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-emerald-500/20" title="Converted for Yamaha PSS-790. Direct passthrough active."><span>⚡</span><span>PSS-790 Ready</span></span>`;
    } else if (isGmPassthrough) {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-blue-950 text-blue-400 border border-blue-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-blue-500/20" title="Playing original unmodified General MIDI file directly through Microsoft GS Wavetable Synth for Windows reference."><span>🎹</span><span>Windows GS Synth (Original GM Passthrough)</span></span>`;
    } else {
        badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-indigo-950 text-indigo-400 border border-indigo-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-indigo-500/20" title="General MIDI file. Real-time auto-remapping active."><span>🎵</span><span>GM (Auto-Remapped)</span></span>`;
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
        pssGroup.label = "⚡ PSS-790 Midi (Converted - Ready)";
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
    } catch (e) {
        state.expandedFolders = new Set(["gm:__root__", "pss790:__root__"]);
    }

    updateSidebarVisibility();
    updateCollapseAllButton();
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
    const separator = document.getElementById("ctxSeparator");
    const itemDelete = document.getElementById("ctxItemDelete");

    if (target.type === "song") {
        itemNewFolder.classList.add("hidden");
        itemMove.classList.remove("hidden");
        itemRename.classList.remove("hidden");
        itemReveal.classList.remove("hidden");
        separator.classList.remove("hidden");
        itemDelete.classList.remove("hidden");
    } else if (target.type === "folder") {
        itemNewFolder.classList.remove("hidden");
        itemMove.classList.add("hidden");
        itemRename.classList.remove("hidden");
        itemReveal.classList.remove("hidden");
        separator.classList.remove("hidden");
        itemDelete.classList.remove("hidden");
    } else if (target.type === "category" || target.type === "empty") {
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
        const catLabel = target.category === "pss790" ? "PSS-790 Midi" : "GM Midi";
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
        populateMoveFolderSelect("gm", "");

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
            deleteLabel.textContent = isFolder ? "Also delete mirrored folder in counterpart library" : "Also delete matching PSS-790 version";
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
            deleteLabel.textContent = "Also delete matching PSS-790 versions in mirrored library";
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

    const tree = category === "pss790" ? state.library.pssTree : state.library.gmTree;
    const catLabel = category === "pss790" ? "PSS-790 Midi" : "GM Midi";

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

    if (data.isBatch) {
        if (activeModalAction.actionType === "batch_delete" && data.hasLinked) {
            const container = document.getElementById("actionModalLinkedDeleteContainer");
            const label = document.getElementById("actionModalLinkedPathLabel");
            const chk = document.getElementById("chkAlsoDeleteLinked");

            if (container && label && chk) {
                label.textContent = `${data.linkedCount} matching PSS-790 version(s) found in mirrored library`;
                chk.checked = true;
                container.classList.remove("hidden");
            }
        } else if (activeModalAction.actionType === "batch_move" && data.hasLinked) {
            const container = document.getElementById("actionModalLinkedMoveContainer");
            const label = document.getElementById("actionModalLinkedMovePathLabel");
            const chk = document.getElementById("chkAlsoMoveLinked");

            if (container && label && chk) {
                label.textContent = `${data.linkedCount} matching PSS-790 version(s) found in mirrored library`;
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
            targetCategory: "gm",
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
    const folderKey = `${catKey}:${folder.relativePath || folder.name}`;
    const isFolderExpanded = query.length > 0 || state.expandedFolders.has(folderKey);

    const folderGroup = document.createElement("div");
    folderGroup.className = "tree-folder-group space-y-0.5";

    const padPx = depth * 12;

    const folderHeader = document.createElement("div");
    folderHeader.className = "flex items-center justify-between px-2 py-1 rounded-md text-slate-400 hover:text-white hover:bg-slate-800/60 cursor-pointer transition select-none text-[11px]";
    if (padPx > 0) {
        folderHeader.style.paddingLeft = `${padPx + 8}px`;
    }

    const count = folder.totalCount !== undefined ? folder.totalCount : (folder.files ? folder.files.length : 0);

    folderHeader.innerHTML = `
        <div class="flex items-center space-x-1.5 truncate">
            <span class="text-[8px] text-slate-500 transform transition-transform">${isFolderExpanded ? '▼' : '▶'}</span>
            <span class="text-amber-400 text-xs">📁</span>
            <span class="font-medium truncate text-slate-300">${escapeHtml(folder.name)}</span>
        </div>
        <span class="text-[9px] font-mono text-slate-500 ml-1">(${count})</span>
    `;
    folderHeader.title = `Folder: ${folder.name} (${count} song${count === 1 ? '' : 's'})\nClick to expand/collapse • Right-click for folder actions • Drag to move folder`;

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

    folderHeader.addEventListener("click", () => toggleFolder(folderKey));
    folderHeader.addEventListener("contextmenu", (e) => {
        showContextMenu(e, {
            type: "folder",
            category: catKey,
            folderName: folder.relativePath || folder.name,
            fullPath: folder.fullPath,
            name: folder.name
        });
    });
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
                <p class="text-[10px] text-slate-500">Put .mid files in Music\\Midi\\GM Midi or PSS-790 Midi</p>
                <button id="btnTreeOpenFolder" class="mt-2 px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs text-cyan-400 font-medium transition border border-slate-700">Open Music Folder</button>
            </div>
        `;
        const btn = document.getElementById("btnTreeOpenFolder");
        if (btn) btn.addEventListener("click", () => sendToHost("open_explorer"));
        return;
    }

    const query = (state.searchQuery || "").toLowerCase();
    container.innerHTML = "";

    // Categories to render: PSS-790 Ready first, then GM Midi
    const categories = [
        { tree: lib.pssTree, title: "PSS-790 Ready", badge: "⚡", key: "pss790", colorClass: "text-emerald-400" },
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
            filteredSubFolders = sourceSubFolders;
            filteredRootFiles = sourceRootFiles;
        }

        const catSongCount = filteredSubFolders.reduce((acc, f) => acc + (f.totalCount !== undefined ? f.totalCount : (f.files ? f.files.length : 0)), 0) + filteredRootFiles.length;
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
}

function createSongItemElement(file) {
    const isSelected = file.fullPath === state.selectedSongPath;
    const isPlaying = file.fullPath === state.currentFilePath && state.isPlaying;
    const isPss = file.format === "pss790";
    const isChecked = state.selectedSongs.has(file.fullPath);
    const isTouch = window.matchMedia('(pointer: coarse)').matches || ('ontouchstart' in window);
    const chkVisibility = (isTouch || state.isSelectMode || state.selectedSongs.size > 0 || isChecked) ? 'inline-block' : 'hidden group-hover:inline-block';

    const div = document.createElement("div");
    div.className = `group tree-song-item flex items-center justify-between px-2 py-1 rounded-lg border border-transparent cursor-pointer select-none text-[11px] text-slate-300 transition-all ${isSelected ? 'selected' : ''} ${isPlaying ? 'playing' : ''} ${isChecked ? 'bg-cyan-950/40 border-cyan-800/50' : ''}`;
    div.dataset.fullPath = file.fullPath;
    div.dataset.fileName = file.fileName;
    div.title = `${file.fileName}\nFolder: ${file.subFolder || '(Root)'}\nClick: Load & inspect\nDouble-click: Play immediately\nDrag: Move to folder`;

    div.draggable = true;

    div.innerHTML = `
        <div class="flex items-center space-x-1.5 min-w-0 flex-1">
            <input type="checkbox" class="song-checkbox w-3.5 h-3.5 rounded text-cyan-500 bg-slate-800 border-slate-700 cursor-pointer mr-0.5 accent-cyan-500 shrink-0 ${chkVisibility}" ${isChecked ? 'checked' : ''} title="Select song for batch moving or deletion">
            <span class="song-icon text-[11px] shrink-0 ${isPlaying ? 'text-cyan-400 animate-pulse' : (isSelected ? 'text-cyan-400' : 'text-slate-500')}" title="${isPlaying ? 'Currently playing' : (isSelected ? 'Currently selected' : 'MIDI song file')}">
                ${isPlaying ? '🔊' : (isSelected ? '▶' : '🎵')}
            </span>
            <span class="song-title truncate font-medium">${escapeHtml(file.fileName)}</span>
        </div>
        <span class="song-badge text-[9px] font-bold font-mono px-1 py-0.2 rounded shrink-0 ml-1.5 ${isPss ? 'bg-emerald-950 text-emerald-400 border border-emerald-800/60' : 'bg-indigo-950 text-indigo-400 border border-indigo-800/60'}" title="${isPss ? 'Native Yamaha PSS-790 converted file (plays via direct hardware passthrough)' : 'Standard General MIDI file (auto-remapped on the fly)'}">
            ${isPss ? 'PSS' : 'GM'}
        </span>
    `;

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
        if (e.target.classList.contains("song-checkbox")) return;
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();
        sendToHost("load_midi_metadata", { path: file.fullPath });
    });

    div.addEventListener("dblclick", (e) => {
        if (e.target.classList.contains("song-checkbox")) return;
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

function selectSongByIndex(index) {
    if (!state.visibleSongList || index < 0 || index >= state.visibleSongList.length) return;
    const song = state.visibleSongList[index];
    state.selectedSongPath = song.fullPath;
    highlightActiveSongInTree();
    if (!state.isPlaying) {
        sendToHost("load_midi_metadata", { path: song.fullPath });
    }

    const el = document.querySelector(`.tree-song-item[data-full-path="${CSS.escape(song.fullPath)}"]`);
    if (el) {
        el.scrollIntoView({ block: "nearest", behavior: "smooth" });
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
        el.scrollIntoView({ block: "nearest", behavior: "smooth" });
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

        // Global shortcut: Ctrl+B to toggle quick-pick sidebar anywhere
        if ((e.ctrlKey || e.metaKey) && (e.key === "b" || e.key === "B")) {
            e.preventDefault();
            toggleSidebar();
            return;
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

            case "ArrowDown": // Next song in visible list
                e.preventDefault();
                navigateSongList(1);
                break;

            case "ArrowUp": // Previous song in visible list
                e.preventDefault();
                navigateSongList(-1);
                break;

            case "Enter": // Play selected song
                e.preventDefault();
                if (state.selectedSongPath) {
                    sendToHost("load_midi_and_play", { path: state.selectedSongPath });
                }
                break;

            case "ArrowLeft": // Seek backward 5s
                e.preventDefault();
                sendToHost("seek", { seconds: Math.max(0, state.currentSeconds - 5) });
                break;

            case "ArrowRight": // Seek forward 5s
                e.preventDefault();
                sendToHost("seek", { seconds: Math.min(state.durationSeconds, state.currentSeconds + 5) });
                break;
        }
    });
}

function renderMixer() {
    clearAllChannelLeds();
    const grid = document.getElementById("mixerGrid");
    grid.innerHTML = "";

    state.channels.forEach(ch => {
        const card = document.createElement("div");
        const isDrum = ch.channel === 15;

        const topAccent = isDrum
            ? 'border-t-2 border-t-amber-400'
            : (ch.hasNotes ? 'border-t-2 border-t-cyan-400' : 'border-t-2 border-t-slate-800/80');

        card.className = `glass-card channel-strip-card p-2 rounded-xl flex flex-col justify-between space-y-1.5 transition-all duration-200 ${topAccent} ${
            ch.hasNotes ? 'border-cyan-500/25 bg-slate-900/60 shadow-sm' : 'border-slate-800/80 opacity-90'
        }`;

        const muteClass = ch.isMuted
            ? 'btn-console-m muted bg-rose-600 text-white shadow-sm shadow-rose-500/50 ring-1 ring-rose-400/80'
            : 'btn-console-m bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50';

        const soloClass = ch.isSolo
            ? 'btn-console-s soloed bg-amber-500 text-slate-950 shadow-sm shadow-amber-500/50 ring-1 ring-amber-300'
            : 'btn-console-s bg-slate-800/90 text-slate-400 hover:text-white hover:bg-slate-700/80 border border-slate-700/50';

        card.innerHTML = `
            <div class="flex items-center justify-between border-b border-slate-800 pb-1">
                <span class="text-[11px] font-bold ${isDrum ? 'text-amber-400' : 'text-cyan-400'}" title="Channel ${ch.channelDisplay} (${isDrum ? 'Drums / Percussion (Chart 1)' : 'Melody / Accompaniment'})">CH ${ch.channelDisplay}</span>
                <span id="led-${ch.channel}" class="meter-led w-2.5 h-2.5 rounded-full ${isDrum ? 'bg-amber-950' : 'bg-emerald-950'} border border-slate-700" title="Signal Activity Meter: Flashes when Channel ${ch.channelDisplay} receives note-on events"></span>
            </div>

            <div class="space-y-0.5">
                <div class="text-[10px] font-medium text-slate-200 truncate" title="${ch.detectedInstrument}">
                    ${ch.detectedInstrument}
                </div>
                <div class="text-[9px] text-slate-400 truncate" title="${isDrum ? 'Channel 10 rhythm fixed to Chart 1 drums' : (ch.defaultPssVoiceName ? `Mapped to AWM Voice: ${ch.defaultPssVoiceName}` : 'Auto-Mapped preset')}">
                    ${isDrum ? 'Chart 1 Fixed' : (ch.defaultPssVoiceName ? `Auto: ${ch.defaultPssVoiceName}` : 'Auto-Mapped')}
                </div>
            </div>

            <!-- Voice Override Combobox -->
            <div class="space-y-0.5">
                <label class="text-[9px] font-semibold text-slate-400 uppercase tracking-tight" title="Voice Override: Force Channel ${ch.channelDisplay} to play one of the 100 built-in Yamaha PSS-790 AWM preset voices">Voice Override</label>
                <select id="voice-override-${ch.channel}" ${isDrum ? 'disabled' : ''} class="w-full bg-slate-900 border border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-white focus:outline-none focus:border-cyan-500 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed" title="${isDrum ? 'Channel 10 Drums use fixed Yamaha Chart 1 rhythm mappings' : `Override Channel ${ch.channelDisplay} preset voice (00-99)`}">
                    <option value="" ${ch.selectedVoice === null ? 'selected' : ''}>Auto (GM Map)</option>
                    ${state.voices.map(v => `
                        <option value="${v.number}" ${ch.selectedVoice === v.number ? 'selected' : ''} class="bg-slate-900 text-white">
                            ${v.number.toString().padStart(2, '0')}: ${v.name}
                        </option>
                    `).join('')}
                </select>
            </div>

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
                const val = e.target.value === "" ? null : parseInt(e.target.value);
                ch.selectedVoice = val;
                sendToHost("override_voice", { channel: ch.channel, voice: val });
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
    });
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
const NOTE_NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];

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

                <!-- Export to PSS-790 Library (Native) -->
                <button onclick="exportRecordingToPss('${encodeURIComponent(rec.filePath)}', '${escapeHtml(rec.fileName)}')" title="Export to PSS-790 Library: Stores in PSS-790 Midi folder for immediate hardware playback" class="px-2.5 py-1 rounded-lg bg-emerald-950/80 hover:bg-emerald-900 text-[11px] font-semibold text-emerald-300 hover:text-white border border-emerald-800/60 transition active:scale-95 flex items-center space-x-1">
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
    showToast(`Exporting ${fileName} to PSS-790 Library...`);
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

document.getElementById("polyphonyGuardToggle").addEventListener("change", (e) => {
    state.polyphonyGuard = e.target.checked;
    sendToHost("toggle_polyphony_guard", { enabled: state.polyphonyGuard });
    showToast(`Smart Polyphony Guard: ${state.polyphonyGuard ? 'Enabled' : 'Disabled'}`);
});

document.getElementById("btnSaveRemapped").addEventListener("click", () => {
    sendToHost("save_remapped");
});

document.getElementById("btnResetOverrides").addEventListener("click", () => {
    sendToHost("reset_voice_overrides");
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
    const src = document.getElementById("batchSourceDir").value;
    const tgt = document.getElementById("batchTargetDir").value;
    const emulateSustain = document.getElementById("chkBatchSustain")?.checked ?? true;
    const neutralizePitch = document.getElementById("chkBatchPitchTune")?.checked ?? true;
    const polyphonyGuard = document.getElementById("chkBatchPolyphony")?.checked ?? true;
    const reverbBoost = document.getElementById("chkBatchReverb")?.checked ?? true;
    const foldExpression = document.getElementById("chkBatchExpressionFold")?.checked ?? true;
    const normalizeVelocities = document.getElementById("chkBatchVelocityNorm")?.checked ?? true;
    document.getElementById("btnStartBatch").disabled = true;
    sendToHost("start_batch", {
        sourceDir: src,
        targetDir: tgt,
        emulateSustain,
        neutralizePitch,
        polyphonyGuard,
        reverbBoost,
        foldExpression,
        normalizeVelocities
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
    document.getElementById("syncStatusSubtext").textContent = "Comparing files in GM Midi and PSS-790 Midi...";
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

    if (missingEl) missingEl.textContent = data.missingCount || 0;
    if (outdatedEl) outdatedEl.textContent = data.outdatedCount || 0;
    if (orphansEl) orphansEl.textContent = data.orphanCount || 0;

    if (data.isInSync) {
        if (icon) icon.textContent = "✅";
        if (headline) headline.textContent = "Library folders are in 100% parity!";
        if (subtext) subtext.textContent = `All ${data.totalGm} GM songs are matched and optimized in PSS-790 format with 0 orphans.`;
        if (banner) banner.className = "p-3 rounded-xl border flex items-center space-x-3 bg-emerald-950/40 border-emerald-500/40 text-xs text-emerald-200";
        if (btnStart) btnStart.textContent = "Re-Verify / Force Sync";
    } else {
        if (icon) icon.textContent = "⚡";
        if (headline) headline.textContent = `${data.missingCount} missing, ${data.outdatedCount} outdated, ${data.orphanCount} orphan files`;
        if (subtext) subtext.textContent = "Review options below and click Align & Sync to resolve differences.";
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

    showToast(`Sync complete! ${data.convertedCount} converted, ${data.updatedCount} updated, ${data.removedOrphansCount} orphans cleaned.`);
}

btnSidebarSync?.addEventListener("click", openSyncModal);
document.getElementById("btnCloseSyncModal")?.addEventListener("click", closeSyncModal);
document.getElementById("btnCancelSyncModal")?.addEventListener("click", closeSyncModal);

document.getElementById("btnStartSync")?.addEventListener("click", () => {
    const convertMissing = document.getElementById("chkSyncMissing")?.checked ?? true;
    const updateOutdated = document.getElementById("chkSyncOutdated")?.checked ?? true;
    const removeOrphans = document.getElementById("chkSyncOrphans")?.checked ?? true;
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

    const peak = health.peakPolyphony || 0;
    const peakVoicesEl = document.getElementById("healthPeakVoices");
    const barEl = document.getElementById("healthPolyphonyBar");
    const descEl = document.getElementById("healthPolyphonyDescription");

    peakVoicesEl.textContent = `${peak} / 28 Voices`;
    const percent = Math.min(100, Math.round((peak / 28) * 100));
    barEl.style.width = `${percent}%`;

    if (peak <= 24) {
        barEl.className = "bg-gradient-to-r from-emerald-500 to-cyan-500 h-full rounded-full transition-all duration-300";
        peakVoicesEl.className = "font-mono font-bold text-emerald-400";
        descEl.textContent = "Safe peak polyphony. No hardware voice-stealing will occur on Yamaha PSS-790.";
    } else if (peak <= 28) {
        barEl.className = "bg-gradient-to-r from-cyan-500 to-amber-500 h-full rounded-full transition-all duration-300";
        peakVoicesEl.className = "font-mono font-bold text-amber-400";
        descEl.textContent = "Moderate polyphony near the 28-voice hardware limit. Smooth playback guaranteed.";
    } else {
        barEl.className = "bg-gradient-to-r from-amber-500 to-rose-500 h-full rounded-full transition-all duration-300";
        peakVoicesEl.className = "font-mono font-bold text-rose-400";
        descEl.textContent = `High polyphony (${peak} voices). Polyphony Guard & hardware voice-stealing active to preserve melody.`;
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
    showToast("SysEx Capture active: waiting for PSS-790 Memory Bulk Dump...");
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

    const confirmed = confirm(
        `Restore "${state.selectedSysexDump.fileName}" to Yamaha PSS-790?\n\n` +
        `WARNING: This will overwrite keyboard Song Memory and Vector Voice registers.\n` +
        `Pacing delay: ${delay}ms per packet.\n\n` +
        `Ensure your PSS-790 is powered on and connected to MIDI OUT.`
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
        showToast("SysEx bulk dump successfully restored to Yamaha PSS-790!");
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

// Trigger initial handshake and keyboard shortcuts
window.addEventListener("DOMContentLoaded", () => {
    initTheme();
    setupKeyboardShortcuts();
    initSidebarState();
    initVuMeters();
    renderRecordingsLibrary();
    sendToHost("get_initial_data");
});
