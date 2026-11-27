<#
.SYNOPSIS
    Packages non-Git project libraries, AppData settings, manuals, Antigravity rules/skills,
    handoff notes, standalone Release executable, and the Ally bootstrap script into a single
    portable zip archive ready to transfer to an ASUS ROG Ally Z1 Extreme (or any Windows x64 PC).
#>
param(
    [string]$RepoRoot = "",
    [string]$OutputZip = "",
    [string]$HandoffDocsDir = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-DownloadsDirectory {
    try {
        $shellKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"
        $val = (Get-ItemProperty -Path $shellKey -Name "{374DE290-123F-4565-9164-39C4925E467B}" -ErrorAction SilentlyContinue)."{374DE290-123F-4565-9164-39C4925E467B}"
        if (-not [string]::IsNullOrWhiteSpace($val) -and (Test-Path $val)) {
            return $val
        }
    }
    catch { }
    return Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads"
}

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path (Get-DownloadsDirectory) "PortaSound-Workstation-Migration.zip"
}

function Resolve-DotNetPath {
    $localDotNet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path $localDotNet) { return $localDotNet }
    return "dotnet"
}

function Invoke-RobocopySafe([string]$Source, [string]$Dest, [string[]]$ExtraArgs = @()) {
    if (-not (Test-Path $Dest)) {
        New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    }
    $args = @($Source, $Dest, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS", "/NP") + $ExtraArgs
    & robocopy @args | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed ($LASTEXITCODE) copying '$Source' -> '$Dest'"
    }
}

$dotnet = Resolve-DotNetPath
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PSS_MigrationStage_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

try {
    Write-Host "=== 1. Staging Bootstrap Script ===" -ForegroundColor Cyan
    $setupScript = Join-Path $PSScriptRoot "setup_rog_ally.ps1"
    if (-not (Test-Path $setupScript)) {
        throw "Bootstrap script not found at '$setupScript'."
    }
    Copy-Item $setupScript (Join-Path $stageRoot "setup_rog_ally.ps1") -Force

    # Convenience 1-click launcher for PowerShell execution policy bypass on destination machine
    $cmdWrapper = @"
@echo off
title Yamaha PortaSound Studio - ROG Ally Bootstrap
setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%setup_rog_ally.ps1"
if not exist "%PS_SCRIPT%" (
    echo [ERROR] Could not find setup_rog_ally.ps1 beside this script.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -ExtractedRoot "%SCRIPT_DIR%"
pause
"@
    Set-Content -Path (Join-Path $stageRoot "Run-ROG-Ally-Setup.bat") -Value $cmdWrapper -Encoding ASCII

    if (-not $SkipPublish) {
        Write-Host "=== 2. Publishing Self-Contained PSS-Studio.exe (win-x64) ===" -ForegroundColor Cyan
        $tmpPub = Join-Path $stageRoot "_tmp_publish"
        $csproj = Join-Path $RepoRoot "src\PortaSoundStudio\PortaSoundStudio.csproj"
        & $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $tmpPub --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }

        $standaloneDir = Join-Path $stageRoot "standalone_app"
        New-Item -ItemType Directory -Path $standaloneDir -Force | Out-Null
        Copy-Item (Join-Path $tmpPub "PSS-Studio.exe") (Join-Path $standaloneDir "PSS-Studio.exe") -Force
        foreach ($extraFile in @("Launch-Studio.bat", "ASUS_ROG_ALLY_SETUP.txt")) {
            $p = Join-Path $RepoRoot $extraFile
            if (Test-Path $p) { Copy-Item $p (Join-Path $standaloneDir $extraFile) -Force }
        }
        Remove-Item $tmpPub -Recurse -Force
    }

    Write-Host "=== 3. Copying Local MIDI & SysEx Libraries ===" -ForegroundColor Cyan
    $appDataDir = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "YamahaPortaSoundStudio"
    $settingsPath = Join-Path $appDataDir "settings.json"
    $midiRoot = Join-Path ([Environment]::GetFolderPath("MyMusic")) "Midi"
    if (Test-Path $settingsPath) {
        try {
            $cfg = Get-Content $settingsPath -Raw | ConvertFrom-Json
            if (-not [string]::IsNullOrWhiteSpace($cfg.CustomLibraryRoot) -and (Test-Path $cfg.CustomLibraryRoot)) {
                $midiRoot = $cfg.CustomLibraryRoot
            }
        }
        catch { }

        $stageAppData = Join-Path $stageRoot "app_data"
        New-Item -ItemType Directory -Path $stageAppData -Force | Out-Null
        Copy-Item $settingsPath (Join-Path $stageAppData "settings.json") -Force
    }

    if (Test-Path $midiRoot) {
        $stageMidi = Join-Path $stageRoot "midi_library"
        Invoke-RobocopySafe -Source $midiRoot -Dest $stageMidi -ExtraArgs @("/XF", "crash.log")
    }
    else {
        Write-Warning "MIDI library directory not found at '$midiRoot'; skipping."
    }

    Write-Host "=== 4. Copying Reference Manuals (gitignored) ===" -ForegroundColor Cyan
    $manualsDir = Join-Path $RepoRoot "Manuals"
    if (Test-Path $manualsDir) {
        Invoke-RobocopySafe -Source $manualsDir -Dest (Join-Path $stageRoot "manuals")
    }

    Write-Host "=== 5. Copying Custom Antigravity Rules & Skills ===" -ForegroundColor Cyan
    $geminiCfgSrc = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".gemini\config"
    $stageGemini = Join-Path $stageRoot "gemini_config"
    New-Item -ItemType Directory -Path $stageGemini -Force | Out-Null

    $rulesFile = Join-Path $geminiCfgSrc "GEMINI.md"
    if (Test-Path $rulesFile) {
        Copy-Item $rulesFile (Join-Path $stageGemini "GEMINI.md") -Force
    }

    $skillsSrc = Join-Path $geminiCfgSrc "skills"
    if (Test-Path $skillsSrc) {
        Invoke-RobocopySafe -Source $skillsSrc -Dest (Join-Path $stageGemini "skills")
    }

    Write-Host "=== 6. Writing Handoff Snapshot & Documentation ===" -ForegroundColor Cyan
    $handoffDir = Join-Path $stageRoot "handoff"
    New-Item -ItemType Directory -Path $handoffDir -Force | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($HandoffDocsDir) -and (Test-Path $HandoffDocsDir)) {
        foreach ($docName in @("walkthrough.md", "voicing_and_melody_fix_plan.md", "rog_ally_migration_plan.md")) {
            $docPath = Join-Path $HandoffDocsDir $docName
            if (Test-Path $docPath) {
                Copy-Item $docPath (Join-Path $handoffDir $docName) -Force
            }
        }
    }

    $remoteUrl = (& git -C $RepoRoot config --get remote.origin.url 2>$null)
    $branchName = (& git -C $RepoRoot branch --show-current 2>$null)
    $headHash = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
    $headSubject = (& git -C $RepoRoot log -1 --pretty=%s 2>$null)

    $handoffMd = @"
# Resuming Yamaha PortaSound Studio on ASUS ROG Ally Z1 Extreme

## Workspace & Git State at Export
- **Origin Remote**: $remoteUrl
- **Branch**: $branchName
- **Last Commit**: $headHash - $headSubject
- **Exported UTC**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ssZ")

## Quick Resume Checklist on ROG Ally
1. Extract this archive into a temporary folder on the Ally and double-click **Run-ROG-Ally-Setup.bat** (or run ``setup_rog_ally.ps1`` in PowerShell).
2. The script installs **Git**, **.NET 8 SDK**, and **WebView2** (if missing), clones ``$remoteUrl`` on branch ``$branchName``, restores your ``Music\Midi`` library (GM Midi, PSS-790 Midi, PSS-680 Midi, SysEx Library, Favorites, Voice Overrides), ``Manuals``, ``.gemini\config`` rules/skills, and standalone ``PSS-Studio.exe``, then runs ``dotnet test -c Release``.
3. Sign into **Antigravity** and **GitHub Credential Manager** (first ``git push`` or ``gh auth login``) on the Ally.
4. Connect a **USB-C PD Hub** to the Ally's top USB-C port (keeps AC power charging while plugged into your USB-MIDI interface for **PSS-790** / **PSS-680**). For trouble-free 31.25 kbps USB-MIDI timing, disable **USB selective suspend** in Windows Power Options.
5. After auditioning the voicing/cold-start/melody fixes on physical hardware, run **Sync -> Execute Sync** inside the app to upgrade ``PSS-790 Midi`` (`:v4`) and ``PSS-680 Midi`` (`:v5`), then run ``scripts\release.ps1 -Version 3.1.4`` when ready to tag and publish `v3.1.4` to GitHub.
"@
    Set-Content -Path (Join-Path $handoffDir "RESUMING_ON_ROG_ALLY.md") -Value $handoffMd -Encoding UTF8

    Write-Host "=== 7. Creating Portable Zip Archive ===" -ForegroundColor Cyan
    $outParent = Split-Path $OutputZip -Parent
    if (-not [string]::IsNullOrWhiteSpace($outParent) -and -not (Test-Path $outParent)) {
        New-Item -ItemType Directory -Path $outParent -Force | Out-Null
    }
    if (Test-Path $OutputZip) {
        Remove-Item $OutputZip -Force
    }

    # Use .NET ZipFile instead of Compress-Archive so dot-folders (like .pss790) are preserved cleanly.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageRoot,
        $OutputZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    $zipInfo = Get-Item $OutputZip
    Write-Host ("Export succeeded: {0} ({1:N2} MB)" -f $zipInfo.FullName, ($zipInfo.Length / 1MB)) -ForegroundColor Green
}
finally {
    if (Test-Path $stageRoot) {
        Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
