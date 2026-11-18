#!/usr/bin/env python3
"""
Yamaha PortaSound Studio — Tutorial Video Generator (v2.2.0)
Generates a full-HD (1920x1080, 30 FPS) narrated walkthrough video with fluent
female neural narration (Microsoft Edge Neural TTS: en-GB-SoniaNeural),
high-resolution graphical feature slides, and synchronized chapters.
"""

import os
import re
import sys
import asyncio
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import edge_tts
import imageio_ffmpeg

ROOT_DIR = Path(__file__).resolve().parent.parent
DIST_DIR = ROOT_DIR / "dist"
TEMP_DIR = ROOT_DIR / "temp_video_build"
ASSETS_DIR = ROOT_DIR / "src" / "PSS790Player" / "WebUI"

FONT_REG = "C:\\Windows\\Fonts\\segoeui.ttf"
FONT_BOLD = "C:\\Windows\\Fonts\\segoeuib.ttf"
FONT_MONO = "C:\\Windows\\Fonts\\consola.ttf"

FFMPEG_EXE = imageio_ffmpeg.get_ffmpeg_exe()
VOICE_NAME = "en-GB-SoniaNeural"

# Color Palette (Dark Studio aesthetic)
BG_COLOR = (11, 15, 25)          # Deep slate navy
PANEL_BG = (17, 24, 39)          # Slate-900
PANEL_BORDER = (31, 41, 55)      # Slate-800
TEXT_WHITE = (248, 250, 252)     # Slate-50
TEXT_MUTED = (148, 163, 184)     # Slate-400
CYAN_ACCENT = (56, 189, 248)     # Sky-400
AMBER_ACCENT = (251, 191, 36)    # Amber-400
EMERALD_ACCENT = (52, 211, 153)  # Emerald-400
CARD_BG_790 = (14, 28, 45)       # Cyan tint panel
CARD_BG_680 = (38, 26, 15)       # Amber tint panel
BORDER_790 = (14, 116, 144)      # Cyan border
BORDER_680 = (180, 83, 9)        # Amber border

def get_font(font_type="reg", size=24):
    path = FONT_BOLD if font_type == "bold" else (FONT_MONO if font_type == "mono" else FONT_REG)
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()

def draw_header(draw, title, subtitle, chapter_num, total_chapters):
    # Header container
    draw.rectangle([60, 40, 1860, 140], fill=PANEL_BG, outline=PANEL_BORDER, width=2)
    
    # App Logo / Title
    draw.text((85, 55), "YAMAHA PORTASOUND STUDIO", font=get_font("bold", 26), fill=CYAN_ACCENT)
    badge_text = f"CHAPTER {chapter_num} OF {total_chapters}"
    draw.rectangle([1650, 60, 1835, 95], fill=(30, 41, 59), outline=CYAN_ACCENT, width=1)
    draw.text((1665, 68), badge_text, font=get_font("bold", 15), fill=TEXT_WHITE)
    
    # Section Title & Subtitle
    draw.text((85, 92), title, font=get_font("bold", 34), fill=TEXT_WHITE)
    if subtitle:
        draw.text((85 + draw.textlength(title, font=get_font("bold", 34)) + 25, 100), f"•  {subtitle}", font=get_font("reg", 22), fill=TEXT_MUTED)

def draw_footer(draw, chapter_idx, total_chapters):
    # Bottom progress bar
    draw.rectangle([60, 1020, 1860, 1045], fill=PANEL_BG, outline=PANEL_BORDER, width=1)
    seg_width = (1800 - 10) / total_chapters
    for i in range(total_chapters):
        x1 = 65 + i * seg_width + 2
        x2 = x1 + seg_width - 4
        col = CYAN_ACCENT if i <= chapter_idx else (40, 50, 70)
        draw.rectangle([x1, 1026, x2, 1039], fill=col)
    
    draw.text((65, 990), "Yamaha PortaSound Studio v2.2.0 • Standalone Windows MIDI Workstation", font=get_font("reg", 16), fill=TEXT_MUTED)
    draw.text((1600, 990), "Open Source (MIT) • Gouldin84", font=get_font("mono", 16), fill=CYAN_ACCENT)

def create_slide_1():
    """Intro & Dual Engine Overview"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Welcome to Yamaha PortaSound Studio", "Next-Gen Vintage MIDI Workstation", 1, 7)
    
    # Left Hero Box - PSS-790
    draw.rounded_rectangle([100, 180, 930, 930], radius=20, fill=CARD_BG_790, outline=BORDER_790, width=2)
    # Icon
    icon790 = ASSETS_DIR / "app_icon.png"
    if icon790.exists():
        ico = Image.open(icon790).resize((110, 110))
        img.paste(ico, (140, 220))
    
    draw.text((275, 225), "Yamaha PSS-790", font=get_font("bold", 36), fill=TEXT_WHITE)
    draw.text((275, 275), "AWM Sound Engine (1990 Era)", font=get_font("bold", 20), fill=CYAN_ACCENT)
    
    bullets790 = [
        "28-Voice Polyphonic Sample Playback Tone Generator",
        "100 Preset Voices (Non-linear Chart 2 MIDI Programs)",
        "Fixed Channel 16 Rhythm with Chart 1 Percussion",
        "Onboard Yamaha DSP Digital Reverb Processor (CC 91)",
        "Vector Synth Joystick & 50 Built-in Accompaniment Styles",
        "Dual-Zone Ride Cymbal Wash & Metal Snare Punch Remap"
    ]
    y = 360
    for b in bullets790:
        draw.text((140, y), "✔", font=get_font("bold", 22), fill=CYAN_ACCENT)
        draw.text((180, y), b, font=get_font("reg", 21), fill=TEXT_WHITE)
        y += 85

    # Right Hero Box - PSS-680
    draw.rounded_rectangle([990, 180, 1820, 930], radius=20, fill=CARD_BG_680, outline=BORDER_680, width=2)
    # Icon
    icon680 = ASSETS_DIR / "pss680_icon.png"
    if icon680.exists():
        ico = Image.open(icon680).resize((110, 110))
        img.paste(ico, (1030, 220))
    
    draw.text((1165, 225), "Yamaha PSS-680 / 480 / 780", font=get_font("bold", 36), fill=TEXT_WHITE)
    draw.text((1165, 275), "2-Operator FM Synthesizer (1988 Era)", font=get_font("bold", 20), fill=AMBER_ACCENT)
    
    bullets680 = [
        "12-Voice Polyphony via Sound Source Mode 99",
        "100 FM Preset Voices (#00–#99) + 100 Rhythm Styles",
        "32-Drum Hand Percussion & Custom Drummer (Ch 16, Notes 38–79)",
        "Clean 7-Octave Melodic Boundary (C0=24 to C7=108)",
        "Linear 1–127 Velocity Modulation & Open/Closed Hi-Hat Choking",
        "Deep SysEx Parameter Access for Modulator/Carrier Envelopes"
    ]
    y = 360
    for b in bullets680:
        draw.text((1030, y), "⚡", font=get_font("bold", 22), fill=AMBER_ACCENT)
        draw.text((1070, y), b, font=get_font("reg", 21), fill=TEXT_WHITE)
        y += 85

    draw_footer(draw, 0, 7)
    return img

def create_slide_2():
    """Tab 1: Player & 16-Channel Mixer"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Tab 1: Player & 16-Channel Mixer", "Real-Time GM Voice Remapping & Live Performance Control", 2, 7)
    
    # 4 Feature Cards in a Grid
    cards = [
        ("🎵 Real-Time GM Remapping Engine", [
            "Shifts General MIDI Channel 10 percussion to Channel 16",
            "Moves colliding melodic tracks on Channel 16 to free channels",
            "Translates standard GM Program Changes to keyboard presets",
            "Microsoft GS Wavetable Mode preserves unmodified GM for PC testing"
        ], CYAN_ACCENT),
        ("🎛️ 16-Channel Activity Console", [
            "Live responsive glowing LED meters with chord hold & pulse decay",
            "Hardware Mute & Solo channel isolation with parameter continuity",
            "Smooth volume sliders and dynamic stereo pan positioning",
            "High-precision seek scrubber with timecode and playback loop"
        ], EMERALD_ACCENT),
        ("🛡️ Smart Polyphony Guard", [
            "Protects lead melody, vocals, and basslines from voice stealing",
            "Restricts concurrent voices to 28 on PSS-790 tone generator",
            "Restricts concurrent voices to 12 in Mode 99 on PSS-680",
            "Dynamically culls low-priority incidental background percussion"
        ], AMBER_ACCENT),
        ("💾 Portable Voice Override Store", [
            "Override instrument voices on any channel with live dropdowns",
            "Per-song voice assignments persist automatically in overrides.json",
            "Namespaced isolation between models (pss790:... vs pss680:...)",
            "Moving or renaming files automatically migrates voice memory"
        ], CYAN_ACCENT)
    ]
    
    positions = [
        (100, 180, 930, 530),
        (990, 180, 1820, 530),
        (100, 570, 930, 920),
        (990, 570, 1820, 920)
    ]
    
    for (title, bullets, acc), (x1, y1, x2, y2) in zip(cards, positions):
        draw.rounded_rectangle([x1, y1, x2, y2], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
        draw.text((x1 + 30, y1 + 25), title, font=get_font("bold", 24), fill=acc)
        by = y1 + 75
        for b in bullets:
            draw.text((x1 + 35, by), "•", font=get_font("bold", 20), fill=acc)
            draw.text((x1 + 60, by), b, font=get_font("reg", 18), fill=TEXT_WHITE)
            by += 55

    draw_footer(draw, 1, 7)
    return img

def create_slide_3():
    """Batch Remapper & Exporter"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Dual-Model Side-by-Side Batch Remapper", "Mass Library Conversion with Dedicated Hardware Safeguards", 3, 7)
    
    # Top banner
    draw.rounded_rectangle([100, 170, 1820, 270], radius=12, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
    draw.text((130, 190), "AUTOMATED DUAL-TARGET BATCH CONVERSION", font=get_font("bold", 20), fill=CYAN_ACCENT)
    draw.text((130, 225), "Source: Music\\Midi\\GM Midi   ➡   Converts simultaneously for both physical keyboards in one pass", font=get_font("mono", 18), fill=TEXT_WHITE)
    
    # Left Column: PSS-790 Panel
    draw.rounded_rectangle([100, 300, 930, 920], radius=16, fill=CARD_BG_790, outline=BORDER_790, width=2)
    draw.text((130, 325), "🎹 Yamaha PSS-790 (AWM Engine)", font=get_font("bold", 26), fill=CYAN_ACCENT)
    draw.text((130, 365), "Target: Music\\Midi\\PSS-790 Midi", font=get_font("mono", 17), fill=TEXT_MUTED)
    
    opts790 = [
        ("Smart Polyphony Guard", "Restricts bursts to 28 voices, protecting lead melodies"),
        ("Yamaha DSP Reverb Boost", "Injects CC 91 sends for built-in onboard reverb processor"),
        ("AWM Velocity Sweet Spot", "Scales dynamics (36–118) preventing multisample clipping"),
        ("Sustain Pedal Emulation", "Extends staccato piano notes naturally for envelope match"),
        ("Expression Swell Folding", "Folds CC 11 expression curves directly into CC 7 volume"),
        ("Acoustic Auto-Tune", "Neutralizes micro-pitch detuning drift on acoustic samples")
    ]
    oy = 415
    for name, desc in opts790:
        draw.rectangle([130, oy, 152, oy+22], outline=CYAN_ACCENT, width=2)
        draw.text((134, oy+2), "✓", font=get_font("bold", 16), fill=CYAN_ACCENT)
        draw.text((165, oy), name, font=get_font("bold", 19), fill=TEXT_WHITE)
        draw.text((165, oy + 28), desc, font=get_font("reg", 16), fill=TEXT_MUTED)
        oy += 80

    # Right Column: PSS-680 Panel
    draw.rounded_rectangle([990, 300, 1820, 920], radius=16, fill=CARD_BG_680, outline=BORDER_680, width=2)
    draw.text((1020, 325), "⚡ Yamaha PSS-680 (2-Op FM Engine)", font=get_font("bold", 26), fill=AMBER_ACCENT)
    draw.text((1020, 365), "Target: Music\\Midi\\PSS-680 Midi", font=get_font("mono", 17), fill=TEXT_MUTED)
    
    opts680 = [
        ("Mode 99 12-Voice Guard", "Strict 12-voice ceiling; culls incidental background drums"),
        ("Purge CC 91 & SysEx", "Strips CC 91 and GS/XG SysEx to protect 31,250 baud buffer"),
        ("Linear FM Dynamics", "Preserves full 1–127 velocity scaling for FM operator depth"),
        ("32 Hand Percussion Remap", "Ch 16 pads (Notes 38–79) with Open/Closed Hi-Hat choke"),
        ("7-Octave Boundary Clamping", "Folds melodic notes cleanly into physical envelope (24–108)"),
        ("Pitch Bend Auto-Centering", "Eliminates fractional pitch drift preventing FM dissonances")
    ]
    oy = 415
    for name, desc in opts680:
        draw.rectangle([1020, oy, 1042, oy+22], outline=AMBER_ACCENT, width=2)
        draw.text((1024, oy+2), "✓", font=get_font("bold", 16), fill=AMBER_ACCENT)
        draw.text((1055, oy), name, font=get_font("bold", 19), fill=TEXT_WHITE)
        draw.text((1055, oy + 28), desc, font=get_font("reg", 16), fill=TEXT_MUTED)
        oy += 80

    draw_footer(draw, 2, 7)
    return img

def create_slide_4():
    """3-Way Mirrored Library Management & Sync"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "3-Way Mirrored Library Management & Sync", "Symmetrical Folder Synchronization across GM, PSS-790, and PSS-680", 4, 7)
    
    # Central Architecture Diagram
    draw.rounded_rectangle([150, 180, 580, 310], radius=16, fill=PANEL_BG, outline=CYAN_ACCENT, width=2)
    draw.text((220, 210), "📁 GM Midi\\", font=get_font("bold", 28), fill=CYAN_ACCENT)
    draw.text((200, 260), "Source MIDI Files", font=get_font("reg", 18), fill=TEXT_MUTED)
    
    draw.text((630, 230), "⮂", font=get_font("bold", 45), fill=TEXT_WHITE)
    
    draw.rounded_rectangle([730, 180, 1190, 310], radius=16, fill=PANEL_BG, outline=BORDER_790, width=2)
    draw.text((790, 210), "⚡ PSS-790 Midi\\", font=get_font("bold", 28), fill=CYAN_ACCENT)
    draw.text((800, 260), "Native AWM Converted", font=get_font("reg", 18), fill=TEXT_MUTED)
    
    draw.text((1240, 230), "⮂", font=get_font("bold", 45), fill=TEXT_WHITE)
    
    draw.rounded_rectangle([1340, 180, 1770, 310], radius=16, fill=PANEL_BG, outline=BORDER_680, width=2)
    draw.text((1400, 210), "⚡ PSS-680 Midi\\", font=get_font("bold", 28), fill=AMBER_ACCENT)
    draw.text((1410, 260), "Native 2-Op FM Converted", font=get_font("reg", 18), fill=TEXT_MUTED)
    
    # 3 Major Feature Panels
    panels = [
        ("📁 Symmetrical Folder Operations", [
            "Create Folder: Creating a folder in the UI creates it across GM, PSS-790, and PSS-680 simultaneously",
            "Rename Folder: Renaming any folder symmetrically renames the counterpart folder in all three libraries",
            "Move Folder: Dragging or moving folders relocates matching directories across all libraries in one action"
        ], CYAN_ACCENT),
        ("🗑️ True 3-Way Linked Deletion & Move", [
            "Move Song with Linked: Moves the song and its converted PSS-790 and PSS-680 versions together",
            "Delete Song with Linked: Permanently removes the GM song and cleans up both PSS-790 and PSS-680 copies",
            "Prevents orphaned files, broken links, or ghost songs from accumulating in your hardware directories"
        ], EMERALD_ACCENT),
        ("🔄 1-Click Dual Library Sync Tool", [
            "Interactive analysis modal scanning all three directories to detect missing, outdated, or orphan files",
            "Instant Batch Sync automatically converts missing GM files to both PSS-790 and PSS-680 formats",
            "Re-converts files whose GM originals were modified and cleans up orphaned keys in overrides.json"
        ], AMBER_ACCENT)
    ]
    
    py = 360
    for title, bullets, acc in panels:
        draw.rounded_rectangle([100, py, 1820, py + 165], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
        draw.text((140, py + 18), title, font=get_font("bold", 24), fill=acc)
        by = py + 60
        for b in bullets:
            draw.text((145, by), "•", font=get_font("bold", 20), fill=acc)
            draw.text((170, by), b, font=get_font("reg", 18), fill=TEXT_WHITE)
            by += 32
        py += 190

    draw_footer(draw, 3, 7)
    return img

def create_slide_5():
    """Tab 2: MIDI Recording Studio & Take Vault"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Tab 2: MIDI Recording Studio & Take Vault", "Direct Hardware Capture, Peak VU Meters & Real-Time Chord Analyzer", 5, 7)
    
    features = [
        ("🔴 Direct 1:1 Keyboard Capture", [
            "Records live keyboard performances, vector synth joystick sweeps, and auto-accompaniment styles directly from PSS-790 MIDI OUT",
            "Records live playing, Custom Drummer rhythms, and 5-bank melody sequencer playout from PSS-680 MIDI OUT",
            "Anti-Feedback Loop Guard prevents MIDI echo loops between PC and keyboard soft-thru circuits",
            "Built-in metronome click track and configurable count-in banner assist tempo timing"
        ], (244, 63, 94)), # Rose
        ("📊 Master Peak Level VU Meters & Chord Analyzer", [
            "Dual 12-segment graduated LED ladder meters (L & R) with calibrated dB scale (-24dB to +3dB clip)",
            "60 FPS ballistic physics with instantaneous velocity peak attack and smooth exponential decay",
            "Real-Time Studio LCD Display detects chord names, roots, inversions, and musical intervals (e.g. C Maj / E)",
            "Live pitch and strike velocity displays on all 16 inbound MIDI channel monitor cards"
        ], EMERALD_ACCENT),
        ("💾 Non-Destructive Native Take Preservation", [
            "Automatically embeds PSS790_RECORDING:v1 or PSS680_RECORDING:v1 meta markers on Track 0",
            "Activates 100% Passthrough Mode when loaded in Player, eliminating double-translation or transposition",
            "Export to General MIDI reverse-remaps Ch 16 drums to Ch 10 and voices back to standard GM for your DAW",
            "1-Click Export to PSS-790 Midi/ or PSS-680 Midi/ preserves native hardware takes for instant recall"
        ], CYAN_ACCENT)
    ]
    
    fy = 190
    for title, bullets, acc in features:
        draw.rounded_rectangle([100, fy, 1820, fy + 225], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
        draw.text((140, fy + 22), title, font=get_font("bold", 25), fill=acc)
        by = fy + 70
        for b in bullets:
            draw.text((145, by), "•", font=get_font("bold", 20), fill=acc)
            draw.text((170, by), b, font=get_font("reg", 19), fill=TEXT_WHITE)
            by += 36
        fy += 250

    draw_footer(draw, 4, 7)
    return img

def create_slide_6():
    """Tab 3: SysEx Vault & Preset Librarian"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Tab 3: SysEx Vault & Preset Librarian", "Bidirectional System Exclusive Dumps, Mode 99 Init & Hex Inspection", 6, 7)
    
    # 2 Big Cards: PSS-790 SysEx vs PSS-680 SysEx
    draw.rounded_rectangle([100, 180, 930, 700], radius=16, fill=CARD_BG_790, outline=BORDER_790, width=2)
    draw.text((140, 215), "🎹 Yamaha PSS-790 SysEx Deck", font=get_font("bold", 26), fill=CYAN_ACCENT)
    
    sysex790 = [
        "One-Click GM System On (F0 7E 7F 09 01 F7)",
        "One-Click Yamaha XG System On (F0 43 10 4C 00 00 7E 00 F7)",
        "Master Volume Max 100% (F0 7F 7F 04 01 7F 7F F7)",
        "All-Notes-Off Panic Reset across all 16 MIDI channels",
        "Restore Vector Play memories and keyboard style snapshots",
        "Paced packet throttle eliminates '<bu full>' hardware lockups"
    ]
    y = 280
    for b in sysex790:
        draw.text((140, y), "✔", font=get_font("bold", 20), fill=CYAN_ACCENT)
        draw.text((175, y), b, font=get_font("reg", 19), fill=TEXT_WHITE)
        y += 65

    draw.rounded_rectangle([990, 180, 1820, 700], radius=16, fill=CARD_BG_680, outline=BORDER_680, width=2)
    draw.text((1030, 215), "⚡ Yamaha PSS-680 / 480 / 780 SysEx Deck", font=get_font("bold", 26), fill=AMBER_ACCENT)
    
    sysex680 = [
        "1-Click Sound Source Mode 99 Init (F0 43 76 00 05 99 F7)",
        "Req FM Voice Bank (Format 00H): 33 bytes / 66 nibbles per voice",
        "Req Melody Memory (Format 01H): 5,026-byte sequence dump for 5 banks",
        "Req Chord Memory (Format 02H): 202-byte chord sequence dumps",
        "Req Custom Drummer (Format 03H): 358-byte rhythm pattern dumps",
        "Strict Yamaha two's complement checksum validation on save/transmit"
    ]
    y = 280
    for b in sysex680:
        draw.text((1030, y), "⚡", font=get_font("bold", 20), fill=AMBER_ACCENT)
        draw.text((1065, y), b, font=get_font("reg", 19), fill=TEXT_WHITE)
        y += 65

    # Bottom Hex Inspection Card
    draw.rounded_rectangle([100, 730, 1820, 930], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
    draw.text((140, 755), "🔍 Integrated SysEx Hex Inspector & Memory Vault", font=get_font("bold", 24), fill=EMERALD_ACCENT)
    
    hex_bullets = [
        "Formatted byte-offset viewing with live ASCII representation and nibble decode preview",
        "Throttled inter-packet pacing prevents buffer overruns on vintage 31,250 baud serial hardware",
        "SysEx vault stored in Music\\Midi\\SysEx\\ with 1-click reveal in Windows Explorer"
    ]
    hy = 805
    for b in hex_bullets:
        draw.text((145, hy), "•", font=get_font("bold", 20), fill=EMERALD_ACCENT)
        draw.text((170, hy), b, font=get_font("reg", 18), fill=TEXT_WHITE)
        hy += 38

    draw_footer(draw, 5, 7)
    return img

def create_slide_7():
    """Summary & Getting Started"""
    img = Image.new("RGB", (1920, 1080), BG_COLOR)
    draw = ImageDraw.Draw(img)
    draw_header(draw, "Getting Started & Standalone Release", "Download, Connect & Elevate Your Vintage Yamaha Keyboards", 7, 7)
    
    # Left Box - Download Details
    draw.rounded_rectangle([100, 180, 1050, 920], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
    draw.text((140, 220), "🚀 Download & Portability", font=get_font("bold", 28), fill=CYAN_ACCENT)
    
    d_points = [
        ("Standalone Single-File Executable", "PSS-Studio.exe (~73 MB) — zero installation required"),
        ("Zero Runtime Dependencies", ".NET 8 runtime & Chromium WebView2 bundled self-contained"),
        ("Asus ROG Ally & Handheld Ready", "Touch-friendly targets, 120Hz kinetic scroll & fixed layout"),
        ("Standardized User Library", "Auto-initializes Music\\Midi\\GM, PSS-790, PSS-680 & Recordings"),
        ("Open Source on GitHub", "MIT Licensed open-source enthusiast software by Gouldin84")
    ]
    dy = 285
    for title, desc in d_points:
        draw.text((140, dy), "✔", font=get_font("bold", 22), fill=CYAN_ACCENT)
        draw.text((175, dy), title, font=get_font("bold", 21), fill=TEXT_WHITE)
        draw.text((175, dy + 30), desc, font=get_font("reg", 18), fill=TEXT_MUTED)
        dy += 85
        
    draw.rounded_rectangle([140, dy + 20, 1010, dy + 110], radius=12, fill=(15, 23, 42), outline=CYAN_ACCENT, width=1)
    draw.text((165, dy + 35), "GitHub Repository:", font=get_font("reg", 16), fill=TEXT_MUTED)
    draw.text((165, dy + 62), "https://github.com/Gouldin84/Yamaha-PortaSound-Studio", font=get_font("mono", 18), fill=CYAN_ACCENT)

    # Right Box - Quick 3-Step Setup
    draw.rounded_rectangle([1100, 180, 1820, 920], radius=16, fill=PANEL_BG, outline=PANEL_BORDER, width=2)
    draw.text((1140, 220), "⚡ 3-Step Quick Start", font=get_font("bold", 28), fill=AMBER_ACCENT)
    
    steps = [
        ("1. Connect USB-MIDI Interface", "Connect PC USB-MIDI OUT ➡ Keyboard MIDI IN.\nFor recording, connect Keyboard MIDI OUT ➡ PC IN."),
        ("2. Set Keyboard Multi-Timbre Mode", "On PSS-790: verify channels 1–16 enabled (<E 1>...<E 16>).\nOn PSS-680: press MIDI Mode [c] and select c.99."),
        ("3. Launch & Drop Songs", "Run PSS-Studio.exe, select your MIDI interface and active\nsynthesizer model, drop General MIDI songs in, and play!")
    ]
    sy = 290
    for title, desc in steps:
        draw.text((1140, sy), title, font=get_font("bold", 22), fill=AMBER_ACCENT)
        dy_sub = sy + 32
        for line in desc.split("\n"):
            draw.text((1140, dy_sub), line, font=get_font("reg", 18), fill=TEXT_WHITE)
            dy_sub += 26
        sy += 135

    draw_footer(draw, 6, 7)
    return img

# Narration Scripts for Fluent British Female Voice (en-GB-SoniaNeural)
SCRIPTS = [
    (create_slide_1, "chapter_1.png", "narration_1.mp3",
     "Welcome to Yamaha PortaSound Studio, the high performance standalone MIDI workstation and recording studio "
     "tailored for classic Yamaha PortaSound synthesizers. Today, we're exploring version 2.2, which brings full, "
     "symmetrical feature parity across two legendary synthesizer architectures. On the left, we have the Yamaha "
     "PSS-790 from 1990, powered by a twenty-eight voice polyphonic Advanced Wave Memory sample playback engine, "
     "complete with one hundred preset voices, onboard digital reverb, and vector synthesis. On the right, we have "
     "the iconic PSS-680, 480, and 780 family from 1988, powered by Yamaha's two-operator FM sound engine, featuring "
     "twelve-voice Sound Source Mode 99, one hundred FM presets, and thirty-two hand percussion instruments. "
     "You can effortlessly switch between these two hardware profiles on the fly via the top navigation dropdown."),
     
    (create_slide_2, "chapter_2.png", "narration_2.mp3",
     "In Tab 1, the Player workspace provides real-time voice remapping for standard General MIDI files. Because "
     "these vintage keyboards predate General MIDI, our engine automatically shifts drum tracks to Channel 16 and "
     "relocates colliding melodic parts to free channels. The sixteen-channel mixing console features live responsive "
     "LED meters with staccato pulse decay, volume sliders, and mute and solo channel isolation. You can assign custom "
     "voice overrides on any channel, which persist automatically per song. Crucially, the Smart Polyphony Guard protects "
     "your lead melody and bass from hardware voice stealing, dynamically restricting voices to twenty-eight on the "
     "PSS-790, and twelve in Mode 99 on the PSS-680 by safely culling low-priority background percussion."),

    (create_slide_3, "chapter_3.png", "narration_3.mp3",
     "When you want to convert entire music collections, the dual-model Batch Remapper provides side-by-side safeguard "
     "panels tailored specifically to each keyboard. For the PSS-790, you get onboard DSP reverb boost, velocity sweet "
     "spot scaling, and sustain emulation. For the PSS-680, the engine activates Mode 99 twelve-voice protection, "
     "purges reverb controller 91 and foreign System Exclusive to protect the 31,250 baud serial buffer, preserves "
     "linear FM dynamics, and translates drums to thirty-two hand percussion pads with hi-hat choking. With dual target "
     "directories, a single click converts your files for both physical keyboards simultaneously."),

    (create_slide_4, "chapter_4.png", "narration_4.mp3",
     "To keep your MIDI collection organized, Yamaha PortaSound Studio introduces symmetrical three-way library "
     "management. Creating, renaming, or moving a folder in the sidebar tree instantly synchronizes across your General "
     "MIDI, PSS-790, and PSS-680 directories. When moving or deleting songs, linked operations ensure matching files "
     "in both keyboard folders move or delete together, preventing orphaned or ghost files. Furthermore, the built-in "
     "Dual Library Sync tool scans all three folders in one click, detecting missing conversions, outdated edits, and "
     "orphans to ensure your libraries remain in perfect alignment."),

    (create_slide_5, "chapter_5.png", "narration_5.mp3",
     "Tab 2 is your MIDI Recording Studio and Take Vault. Here, you can capture live performances, vector joystick sweeps, "
     "auto-accompaniment styles, and Custom Drummer rhythm patterns directly from your keyboard's MIDI Out. The deck features "
     "stereo master peak level VU meters with ballistic physics and a real-time musical chord analyzer LCD that identifies "
     "chords, roots, inversions, and intervals as you play. Recorded takes are preserved with native metadata markers, "
     "enabling direct passthrough playback in the player and clean reverse-exporting to standard General MIDI for DAW use."),

    (create_slide_6, "chapter_6.png", "narration_6.mp3",
     "Tab 3 houses the SysEx Vault and Preset Librarian. The app provides dedicated hardware toolbars for each synthesizer. "
     "On the PSS-680, you can initialize Sound Source Mode 99 with a single click, and capture or restore full memory dumps "
     "for FM voice parameters, melody memory, chord sequences, and Custom Drummer rhythm patterns. An integrated SysEx Hex "
     "Inspector lets you inspect raw byte payloads with verified two's-complement checksum calculations, while paced packet "
     "delays prevent hardware buffer overruns across serial MIDI cables."),

    (create_slide_7, "chapter_7.png", "narration_7.mp3",
     "Yamaha PortaSound Studio is packaged as a standalone, single-file Windows executable with zero runtime dependencies. "
     "It is touch-optimized for handhelds like the Asus ROG Ally Z1 Extreme, as well as desktop setups. The software is "
     "free, open-source under the MIT license, and available directly on GitHub. Download PSS-Studio.exe, connect your "
     "MIDI interface, and breathe new life into your vintage Yamaha synthesizers today!")
]

def get_audio_duration(audio_file):
    cmd = [FFMPEG_EXE, "-i", str(audio_file)]
    res = subprocess.run(cmd, stderr=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
    m = re.search(r"Duration:\s*(\d+):(\d+):(\d+\.\d+)", res.stderr)
    if m:
        return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))
    return 15.0

async def generate_speech(text, output_file):
    communicate = edge_tts.Communicate(text, VOICE_NAME)
    await communicate.save(str(output_file))

def main():
    DIST_DIR.mkdir(parents=True, exist_ok=True)
    TEMP_DIR.mkdir(parents=True, exist_ok=True)
    
    chapter_mp4s = []
    
    print(f"=== Yamaha PortaSound Studio Tutorial Video Generator ===")
    print(f"Narration Voice: {VOICE_NAME} (Fluent Female Neural)")
    print(f"Output Video: {DIST_DIR / 'Yamaha-PortaSound-Studio-Tutorial.mp4'}")
    
    for idx, (slide_fn, img_name, audio_name, script_text) in enumerate(SCRIPTS, start=1):
        print(f"\n--- Processing Chapter {idx} of {len(SCRIPTS)} ---")
        
        # 1. Render slide
        slide_img = slide_fn()
        img_path = TEMP_DIR / img_name
        slide_img.save(img_path)
        print(f"Rendered slide: {img_name}")
        
        # 2. Synthesize audio
        audio_path = TEMP_DIR / audio_name
        print(f"Synthesizing fluent female narration...")
        asyncio.run(generate_speech(script_text, audio_path))
        
        # 3. Measure duration
        dur = get_audio_duration(audio_path)
        clip_dur = dur + 0.6  # breathing room
        print(f"Audio duration: {dur:.2f}s (Clip length: {clip_dur:.2f}s)")
        
        # 4. Render chapter clip with ffmpeg
        chapter_mp4 = TEMP_DIR / f"chapter_{idx}.mp4"
        cmd = [
            FFMPEG_EXE, "-y",
            "-loop", "1", "-i", str(img_path),
            "-i", str(audio_path),
            "-c:v", "libx264",
            "-tune", "stillimage",
            "-c:a", "aac",
            "-b:a", "192k",
            "-ar", "44100",
            "-pix_fmt", "yuv420p",
            "-r", "30",
            "-t", f"{clip_dur:.3f}",
            str(chapter_mp4)
        ]
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        print(f"Rendered chapter clip: {chapter_mp4.name}")
        chapter_mp4s.append(chapter_mp4)
        
    # 5. Concatenate all chapters into final video
    print("\n--- Assembling Final Tutorial Video ---")
    concat_txt = TEMP_DIR / "concat_list.txt"
    with open(concat_txt, "w", encoding="utf-8") as f:
        for c in chapter_mp4s:
            # use forward slashes for ffmpeg concat
            p_str = str(c).replace("\\", "/")
            f.write(f"file '{p_str}'\n")
            
    final_mp4 = DIST_DIR / "Yamaha-PortaSound-Studio-Tutorial.mp4"
    concat_cmd = [
        FFMPEG_EXE, "-y",
        "-f", "concat",
        "-safe", "0",
        "-i", str(concat_txt),
        "-c", "copy",
        str(final_mp4)
    ]
    subprocess.run(concat_cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
    
    total_dur = get_audio_duration(final_mp4)
    file_size_mb = final_mp4.stat().st_size / (1024 * 1024)
    
    print(f"\nSUCCESS! Video generated successfully:")
    print(f"Path: {final_mp4}")
    print(f"Duration: {int(total_dur // 60)}m {int(total_dur % 60)}s ({total_dur:.2f}s)")
    print(f"File Size: {file_size_mb:.2f} MB")
    print(f"Resolution: 1920x1080 @ 30 FPS (H.264 / AAC)")

if __name__ == "__main__":
    main()
