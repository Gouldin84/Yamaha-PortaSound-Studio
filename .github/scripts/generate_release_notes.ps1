param(
    [string]$RefName = "v2.3.1"
)

$tag = $RefName.TrimStart('v')

$summary = @"
# Yamaha PortaSound Studio v${tag}

A high-performance standalone MIDI workstation, real-time voice remapper, and multi-tab recording & SysEx studio designed for the **Yamaha PortaSound PSS-790** (AWM) and **PSS-680 / PSS-480 / PSS-780** (2-Operator FM) keyboard synthesizers.

## Downloads
| Asset | Description |
| :--- | :--- |
| **PSS-Studio.exe** | Standalone single-file executable (no installation or .NET runtime required) |
| **PSS-Studio-win-x64.zip** | Complete package archive with executable, launcher, and Asus ROG Ally setup guide |
| **Launch-Studio.bat** | Windows launch script |
| **ASUS_ROG_ALLY_SETUP.txt** | Touchscreen optimization guide for Asus ROG Ally |

## Quick Start Guide
1. Download **`PSS-Studio.exe`** (or extract **`PSS-Studio-win-x64.zip`**).
2. Connect your PC's USB-MIDI Interface to your Yamaha PortaSound keyboard (`OUT` to `MIDI IN`, `IN` to `MIDI OUT`).
3. Run `PSS-Studio.exe`, select your keyboard model (**PSS-790** or **PSS-680**) from the top header dropdown, and choose your MIDI port.
"@

[System.IO.File]::WriteAllText("release_notes.md", $summary, [System.Text.Encoding]::UTF8)
Write-Host "release_notes.md generated for version $tag"
