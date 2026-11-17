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
            renderHealthBadge(null);
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
            updateRecorderChannelActivity(msg.data.channel, msg.data.isActive, msg.data.velocity);
            break;

        case "recorder_take_ready":
            showTakeReadyCard(msg.data);
            break;

        case "take_saved":
            showToast(`Saved to ${msg.data.isPss790 ? 'PSS-790 Midi' : 'GM Midi'}: ${msg.data.fileName}`);
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
            <input type="checkbox" class="song-checkbox w-3.5 h-3.5 rounded text-cyan-500 bg-slate-800 border-slate-700 cursor-pointer mr-0.5 accent-cyan-500 shrink-0 ${chkVisibility}" ${isChecked ? 'checked' : ''}>
            <span class="song-icon text-[11px] shrink-0 ${isPlaying ? 'text-cyan-400 animate-pulse' : (isSelected ? 'text-cyan-400' : 'text-slate-500')}">
                ${isPlaying ? '🔊' : (isSelected ? '▶' : '🎵')}
            </span>
            <span class="song-title truncate font-medium">${escapeHtml(file.fileName)}</span>
        </div>
        <span class="song-badge text-[9px] font-bold font-mono px-1 py-0.2 rounded shrink-0 ml-1.5 ${isPss ? 'bg-emerald-950 text-emerald-400 border border-emerald-800/60' : 'bg-indigo-950 text-indigo-400 border border-indigo-800/60'}">
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

        card.className = `glass-card p-2 rounded-xl flex flex-col justify-between space-y-1.5 transition-all duration-200 ${
            ch.hasNotes ? 'border-cyan-500/30 bg-slate-900/60' : 'border-slate-800/80'
        }`;

        card.innerHTML = `
            <div class="flex items-center justify-between border-b border-slate-800 pb-1">
                <span class="text-[11px] font-bold ${isDrum ? 'text-amber-400' : 'text-cyan-400'}">CH ${ch.channelDisplay}</span>
                <span id="led-${ch.channel}" class="meter-led w-2 h-2 rounded-full ${isDrum ? 'bg-amber-950' : 'bg-emerald-950'} border border-slate-700"></span>
            </div>

            <div class="space-y-0.5">
                <div class="text-[10px] font-medium text-slate-200 truncate" title="${ch.detectedInstrument}">
                    ${ch.detectedInstrument}
                </div>
                <div class="text-[9px] text-slate-400 truncate">
                    ${isDrum ? 'Chart 1 Fixed' : (ch.defaultPssVoiceName ? `Auto: ${ch.defaultPssVoiceName}` : 'Auto-Mapped')}
                </div>
            </div>

            <!-- Voice Override Combobox -->
            <div class="space-y-0.5">
                <label class="text-[9px] font-semibold text-slate-400 uppercase tracking-tight">Voice Override</label>
                <select id="voice-override-${ch.channel}" ${isDrum ? 'disabled' : ''} class="w-full bg-slate-900 border border-slate-700 rounded px-1.5 py-0.5 text-[10px] text-white focus:outline-none focus:border-cyan-500 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed">
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
                    <input type="range" id="vol-${ch.channel}" min="0" max="127" value="${ch.volume}" class="w-full h-1 bg-slate-800">
                    <div class="flex justify-between text-[8px] text-slate-500 font-mono">
                        <span>VOL</span>
                        <span id="vol-val-${ch.channel}">${ch.volume}</span>
                    </div>
                </div>

                <div class="flex space-x-0.5">
                    <button id="mute-${ch.channel}" class="w-5 h-5 rounded text-[9px] font-bold ${ch.isMuted ? 'bg-rose-600 text-white' : 'bg-slate-800 text-slate-400 hover:text-white'} transition">M</button>
                    <button id="solo-${ch.channel}" class="w-5 h-5 rounded text-[9px] font-bold ${ch.isSolo ? 'bg-amber-500 text-slate-950' : 'bg-slate-800 text-slate-400 hover:text-white'} transition">S</button>
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
    const emulateSustain = document.getElementById("chkBatchSustain")?.checked ?? true;
    const neutralizePitch = document.getElementById("chkBatchPitchTune")?.checked ?? true;
    const polyphonyGuard = document.getElementById("chkBatchPolyphony")?.checked ?? true;
    const reverbBoost = document.getElementById("chkBatchReverb")?.checked ?? true;
    document.getElementById("btnStartBatch").disabled = true;
    sendToHost("start_batch", {
        sourceDir: src,
        targetDir: tgt,
        emulateSustain,
        neutralizePitch,
        polyphonyGuard,
        reverbBoost
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
        container.innerHTML = `<span class="px-2 py-0.5 rounded-full bg-slate-800 text-slate-400 border border-slate-700 font-semibold text-[11px] flex items-center space-x-1.5"><span class="w-1.5 h-1.5 rounded-full bg-slate-500"></span><span>Health: Standby</span></span>`;
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
        <button id="btnOpenHealthModal" class="px-2 py-0.5 rounded-full ${badgeClass} border font-semibold text-[11px] flex items-center space-x-1.5 shadow-sm transition active:scale-95 cursor-pointer" title="${health.badgeTooltip || ''}">
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

// Trigger initial handshake and keyboard shortcuts
window.addEventListener("DOMContentLoaded", () => {
    setupKeyboardShortcuts();
    initSidebarState();
    sendToHost("get_initial_data");
});
