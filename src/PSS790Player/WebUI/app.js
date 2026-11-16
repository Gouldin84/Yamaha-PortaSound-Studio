// PSS-790 Modern UI Application Logic & MIDI Studio Engine

let state = {
    activeTab: "player",
    devices: [],
    inputDevices: [],
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
    }))
};

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
    document.getElementById("viewPlayer").classList.toggle("hidden", !isPlayer);
    document.getElementById("viewRecorder").classList.toggle("hidden", isPlayer);

    const tabPlayer = document.getElementById("tabBtnPlayer");
    const tabRec = document.getElementById("tabBtnRecorder");

    if (isPlayer) {
        tabPlayer.className = "tab-btn active flex items-center space-x-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold text-white transition-all";
        tabRec.className = "tab-btn flex items-center space-x-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold text-slate-400 hover:text-white transition-all";
    } else {
        tabPlayer.className = "tab-btn flex items-center space-x-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold text-slate-400 hover:text-white transition-all";
        tabRec.className = "tab-btn active flex items-center space-x-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold text-white transition-all";
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
            renderDeviceSelect();
            renderInputDeviceSelect();
            renderLibrarySelect();
            renderLibraryTree();
            renderMixer();
            renderRecorderChannels();
            initSidebarState();
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

        case "file_loaded":
            state.currentFile = msg.data.fileName;
            state.currentFilePath = msg.data.filePath;
            state.selectedSongPath = msg.data.filePath;
            state.durationSeconds = msg.data.durationSeconds || 0;
            document.getElementById("currentSongTitle").textContent = msg.data.fileName;
            document.getElementById("totalTimeDisplay").textContent = msg.data.durationFormatted || "00:00";
            document.getElementById("timeScrubber").max = state.durationSeconds;
            document.getElementById("timeScrubber").value = 0;
            document.getElementById("fileStatusBadge").textContent = "Loaded";

            // Format Badge Rendering
            const badgeContainer = document.getElementById("formatBadgeContainer");
            if (badgeContainer) {
                if (msg.data.format === "pss790") {
                    badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-emerald-950 text-emerald-400 border border-emerald-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-emerald-500/20" title="Converted for Yamaha PSS-790. Direct passthrough active."><span>⚡</span><span>PSS-790 Ready</span></span>`;
                } else {
                    badgeContainer.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-indigo-950 text-indigo-400 border border-indigo-800/80 font-semibold text-[11px] flex items-center space-x-1 shadow-sm shadow-indigo-500/20" title="General MIDI file. Real-time auto-remapping active."><span>🎵</span><span>GM (Auto-Remapped)</span></span>`;
                }
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
                    }
                });
                renderMixer();
            }
            highlightActiveSongInTree();
            break;

        case "channel_activity":
            updateChannelLed(msg.data.channel, msg.data.isActive, msg.data.velocity);
            break;

        case "playback_state":
            state.isPlaying = msg.data.isPlaying;
            state.isPaused = msg.data.isPaused;
            state.currentSeconds = msg.data.currentSeconds || 0;
            updatePlaybackUi();
            highlightActiveSongInTree();
            break;

        // --- Recorder Events ---
        case "recorder_state":
            updateRecorderState(msg.data.state, msg.data.countInBeat, msg.data.bpm);
            break;

        case "recorder_tick":
            updateRecorderTick(msg.data);
            break;

        case "recorder_input_activity":
            updateRecorderChannelActivity(msg.data.channel, msg.data.isActive, msg.data.velocity);
            break;

        case "recorder_take_ready":
            showTakeReadyCard(msg.data);
            break;

        case "take_saved":
            showToast(`Saved to ${msg.data.isPss790 ? 'PSS-790 Midi' : 'GM Midi'}: ${msg.data.fileName}`);
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

        case "notification":
            showToast(msg.data.message);
            break;

        case "error":
            showToast(msg.data.message, true);
            break;
    }
}

// --- DOM Rendering & Device Selectors ---

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

function renderInputDeviceSelect(preferredDevice) {
    const sel = document.getElementById("recorderInputDeviceSelect");
    const dot = document.getElementById("inputStatusDot");
    sel.innerHTML = "";

    if (state.inputDevices.length === 0) {
        sel.innerHTML = `<option value="" disabled selected>No Input Ports Detected</option>`;
        if (dot) dot.className = "w-2.5 h-2.5 rounded-full bg-slate-600";
        return;
    }

    if (dot) dot.className = "w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse";

    state.inputDevices.forEach((dev, idx) => {
        const opt = document.createElement("option");
        opt.value = dev;
        opt.textContent = dev;
        opt.className = "bg-slate-900 text-white";
        if (preferredDevice ? dev === preferredDevice : idx === 0) opt.selected = true;
        sel.appendChild(opt);
    });
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
    } catch (e) {}

    // Auto-expand all folders on first run if not already set
    if (state.expandedFolders.size === 0 && state.library) {
        ["pssTree", "gmTree"].forEach(key => {
            const tree = state.library[key];
            if (tree && tree.folders) {
                tree.folders.forEach(f => {
                    state.expandedFolders.add(`${tree.format}:${f.name}`);
                });
            }
        });
    }

    updateSidebarVisibility();
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
    renderLibraryTree();
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

        let filteredFolders = [];
        let filteredRootFiles = [];

        if (query) {
            if (tree.folders) {
                tree.folders.forEach(f => {
                    const folderMatches = f.name.toLowerCase().includes(query);
                    const matchingFiles = (f.files || []).filter(file =>
                        folderMatches || file.fileName.toLowerCase().includes(query)
                    );
                    if (matchingFiles.length > 0) {
                        filteredFolders.push({
                            name: f.name,
                            files: matchingFiles,
                            count: matchingFiles.length
                        });
                    }
                });
            }
            if (tree.files) {
                filteredRootFiles = tree.files.filter(f => f.fileName.toLowerCase().includes(query));
            }
        } else {
            filteredFolders = tree.folders || [];
            filteredRootFiles = tree.files || [];
        }

        const catSongCount = filteredFolders.reduce((acc, f) => acc + f.files.length, 0) + filteredRootFiles.length;
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
        catHeader.addEventListener("click", () => toggleCategory(cat.key));
        catGroup.appendChild(catHeader);

        // Category Content (Folders + Root Files)
        if (!isCatCollapsed) {
            const catContent = document.createElement("div");
            catContent.className = "pl-1.5 pt-1 space-y-1";

            // Render Subfolders
            filteredFolders.forEach(folder => {
                const folderKey = `${cat.key}:${folder.name}`;
                const isFolderExpanded = query.length > 0 || state.expandedFolders.has(folderKey);

                const folderGroup = document.createElement("div");
                folderGroup.className = "tree-folder-group space-y-0.5";

                const folderHeader = document.createElement("div");
                folderHeader.className = "flex items-center justify-between px-2 py-1 rounded-md text-slate-400 hover:text-white hover:bg-slate-800/60 cursor-pointer transition select-none text-[11px]";
                folderHeader.innerHTML = `
                    <div class="flex items-center space-x-1.5 truncate">
                        <span class="text-[8px] text-slate-500 transform transition-transform">${isFolderExpanded ? '▼' : '▶'}</span>
                        <span class="text-amber-400 text-xs">📁</span>
                        <span class="font-medium truncate text-slate-300">${escapeHtml(folder.name)}</span>
                    </div>
                    <span class="text-[9px] font-mono text-slate-500 ml-1">(${folder.files.length})</span>
                `;
                folderHeader.addEventListener("click", () => toggleFolder(folderKey));
                folderGroup.appendChild(folderHeader);

                if (isFolderExpanded) {
                    const folderContent = document.createElement("div");
                    folderContent.className = "pl-3 space-y-0.5 border-l border-slate-800/80 ml-2.5";
                    folder.files.forEach(file => {
                        state.visibleSongList.push(file);
                        folderContent.appendChild(createSongItemElement(file));
                    });
                    folderGroup.appendChild(folderContent);
                }

                catContent.appendChild(folderGroup);
            });

            // Render Category Root Files
            if (filteredRootFiles.length > 0) {
                const rootGroup = document.createElement("div");
                rootGroup.className = "space-y-0.5 pt-0.5";
                filteredRootFiles.forEach(file => {
                    state.visibleSongList.push(file);
                    rootGroup.appendChild(createSongItemElement(file));
                });
                catContent.appendChild(rootGroup);
            }

            catGroup.appendChild(catContent);
        }

        container.appendChild(catGroup);
    });

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

    const div = document.createElement("div");
    div.className = `tree-song-item flex items-center justify-between px-2 py-1 rounded-lg border border-transparent cursor-pointer select-none text-[11px] text-slate-300 transition-all ${isSelected ? 'selected' : ''} ${isPlaying ? 'playing' : ''}`;
    div.dataset.fullPath = file.fullPath;
    div.dataset.fileName = file.fileName;
    div.title = `${file.fileName}\nFolder: ${file.subFolder || '(Root)'}\nClick: Load & inspect\nDouble-click: Play immediately`;

    div.innerHTML = `
        <div class="flex items-center space-x-1.5 min-w-0 flex-1">
            <span class="song-icon text-[11px] shrink-0 ${isPlaying ? 'text-cyan-400 animate-pulse' : (isSelected ? 'text-cyan-400' : 'text-slate-500')}">
                ${isPlaying ? '🔊' : (isSelected ? '▶' : '🎵')}
            </span>
            <span class="song-title truncate font-medium">${escapeHtml(file.fileName)}</span>
        </div>
        <span class="song-badge text-[9px] font-bold font-mono px-1 py-0.2 rounded shrink-0 ml-1.5 ${isPss ? 'bg-emerald-950 text-emerald-400 border border-emerald-800/60' : 'bg-indigo-950 text-indigo-400 border border-indigo-800/60'}">
            ${isPss ? 'PSS' : 'GM'}
        </span>
    `;

    div.addEventListener("click", () => {
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();
        sendToHost("load_midi_metadata", { path: file.fullPath });
    });

    div.addEventListener("dblclick", () => {
        state.selectedSongPath = file.fullPath;
        highlightActiveSongInTree();
        sendToHost("load_midi_and_play", { path: file.fullPath });
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
    sendToHost("load_midi_metadata", { path: song.fullPath });

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
    const grid = document.getElementById("mixerGrid");
    grid.innerHTML = "";

    state.channels.forEach(ch => {
        const card = document.createElement("div");
        const isDrum = ch.channel === 15;

        card.className = `glass-card p-3 rounded-2xl flex flex-col justify-between space-y-3 transition-all duration-200 ${
            ch.hasNotes ? 'border-cyan-500/30' : 'border-slate-800'
        }`;

        card.innerHTML = `
            <div class="flex items-center justify-between border-b border-slate-800 pb-2">
                <span class="text-xs font-bold ${isDrum ? 'text-amber-400' : 'text-cyan-400'}">CH ${ch.channelDisplay}</span>
                <span id="led-${ch.channel}" class="meter-led w-2.5 h-2.5 rounded-full ${isDrum ? 'bg-amber-950' : 'bg-emerald-950'} border border-slate-700"></span>
            </div>

            <div class="space-y-1">
                <div class="text-[11px] font-medium text-slate-300 truncate" title="${ch.detectedInstrument}">
                    ${ch.detectedInstrument}
                </div>
                <div class="text-[10px] text-slate-500">
                    ${isDrum ? 'Chart 1 Fixed' : (ch.defaultPssVoiceName ? `Auto: ${ch.defaultPssVoiceName}` : 'Auto-Mapped')}
                </div>
            </div>

            <!-- Voice Override Combobox -->
            <div class="space-y-1">
                <label class="text-[10px] font-semibold text-slate-400 uppercase">Voice Override</label>
                <select id="voice-override-${ch.channel}" ${isDrum ? 'disabled' : ''} class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2 py-1 text-xs text-white focus:outline-none focus:border-cyan-500 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed">
                    <option value="" ${ch.selectedVoice === null ? 'selected' : ''}>Auto (GM Map)</option>
                    ${state.voices.map(v => `
                        <option value="${v.number}" ${ch.selectedVoice === v.number ? 'selected' : ''} class="bg-slate-900 text-white">
                            ${v.number.toString().padStart(2, '0')}: ${v.name}
                        </option>
                    `).join('')}
                </select>
            </div>

            <!-- Fader & Controls -->
            <div class="flex items-center justify-between space-x-2 pt-1">
                <div class="flex-1 space-y-1">
                    <input type="range" id="vol-${ch.channel}" min="0" max="127" value="${ch.volume}" class="w-full h-1.5 bg-slate-800">
                    <div class="flex justify-between text-[9px] text-slate-500 font-mono">
                        <span>VOL</span>
                        <span id="vol-val-${ch.channel}">${ch.volume}</span>
                    </div>
                </div>

                <div class="flex space-x-1">
                    <button id="mute-${ch.channel}" class="w-6 h-6 rounded-md text-[10px] font-bold ${ch.isMuted ? 'bg-rose-600 text-white' : 'bg-slate-800 text-slate-400 hover:text-white'} transition">M</button>
                    <button id="solo-${ch.channel}" class="w-6 h-6 rounded-md text-[10px] font-bold ${ch.isSolo ? 'bg-amber-500 text-slate-950' : 'bg-slate-800 text-slate-400 hover:text-white'} transition">S</button>
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
                btnMute.className = `w-6 h-6 rounded-md text-[10px] font-bold ${ch.isMuted ? 'bg-rose-600 text-white' : 'bg-slate-800 text-slate-400 hover:text-white'} transition`;
                sendToHost("set_channel_mute", { channel: ch.channel, mute: ch.isMuted });
            });
        }

        const btnSolo = card.querySelector(`#solo-${ch.channel}`);
        if (btnSolo) {
            btnSolo.addEventListener("click", () => {
                ch.isSolo = !ch.isSolo;
                btnSolo.className = `w-6 h-6 rounded-md text-[10px] font-bold ${ch.isSolo ? 'bg-amber-500 text-slate-950' : 'bg-slate-800 text-slate-400 hover:text-white'} transition`;
                sendToHost("set_channel_solo", { channel: ch.channel, solo: ch.isSolo });
            });
        }
    });
}

function updateChannelLed(channel, isActive, velocity) {
    const led = document.getElementById(`led-${channel}`);
    if (!led) return;

    const isDrum = channel === 15;
    if (isActive && velocity > 0) {
        led.classList.add(isDrum ? 'drum-active' : 'active');
    } else {
        led.classList.remove(isDrum ? 'drum-active' : 'active');
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

        card.innerHTML = `
            <div class="flex items-center space-x-2">
                <span id="rec-led-${ch.channel}" class="inbound-led w-3 h-3 rounded-full bg-slate-800 border border-slate-700"></span>
                <div>
                    <div class="text-[11px] font-bold ${ch.isDrum ? 'text-amber-400' : 'text-slate-300'}">CH ${ch.channelDisplay}</div>
                    <div id="rec-note-${ch.channel}" class="text-[10px] font-mono text-slate-500">-</div>
                </div>
            </div>
            <span id="rec-vel-${ch.channel}" class="text-[10px] font-mono text-slate-600">0</span>
        `;

        grid.appendChild(card);
    });
}

function updateRecorderChannelActivity(channel, isActive, velocity) {
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

    if (isActive && velocity > 0) {
        if (ticker) {
            ticker.textContent = `Inbound: Ch ${channel + 1} • Velocity ${velocity}`;
            ticker.className = "text-xs font-mono text-rose-400 truncate max-w-md";
        }
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

    if (state.isPlaying) {
        playIcon.classList.add("hidden");
        pauseIcon.classList.remove("hidden");
    } else {
        playIcon.classList.remove("hidden");
        pauseIcon.classList.add("hidden");
    }

    if (scrubber && !scrubber.matches(":active")) {
        scrubber.value = state.currentSeconds;
    }

    if (currTime) {
        const mins = Math.floor(state.currentSeconds / 60);
        const secs = Math.floor(state.currentSeconds % 60);
        currTime.textContent = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
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

// Primary Hardware Output Select
document.getElementById("deviceSelect").addEventListener("change", (e) => {
    sendToHost("select_device", { deviceName: e.target.value });
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
    sendToHost("seek", { seconds: parseFloat(e.target.value) });
});

// Speed Controls
document.querySelectorAll(".speed-btn").forEach(btn => {
    btn.addEventListener("click", () => {
        document.querySelectorAll(".speed-btn").forEach(b => {
            b.className = "speed-btn px-2.5 py-1 text-xs font-semibold rounded-lg text-slate-400 hover:text-white transition";
        });
        btn.className = "speed-btn px-2.5 py-1 text-xs font-semibold rounded-lg bg-cyan-500 text-slate-950 shadow-sm transition";
        state.speed = parseFloat(btn.dataset.speed);
        sendToHost("set_speed", { speed: state.speed });
    });
});

document.getElementById("btnLoop").addEventListener("click", () => {
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

// --- MIDI Studio / Recorder Event Listeners ---

document.getElementById("recorderInputDeviceSelect").addEventListener("change", (e) => {
    sendToHost("select_input_device", { deviceName: e.target.value });
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
    document.getElementById("btnStartBatch").disabled = true;
    sendToHost("start_batch", { sourceDir: src, targetDir: tgt });
});

// Trigger initial handshake and keyboard shortcuts
window.addEventListener("DOMContentLoaded", () => {
    setupKeyboardShortcuts();
    initSidebarState();
    sendToHost("get_initial_data");
});
