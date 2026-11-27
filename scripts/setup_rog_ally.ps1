<#
.SYNOPSIS
    Bootstraps a target ASUS ROG Ally Z1 Extreme (or Windows x64 dev PC) from a
    PortaSound-Workstation-Migration archive: installs Git, .NET 8 SDK, WebView2, clones the repo,
    restores local MIDI/SysEx libraries, AppData, manuals, Antigravity rules/skills, and verifies
    with dotnet test.
#>
param(
    [string]$BundleZip = "",
    [string]$ExtractedRoot = "",
    [string]$WorkspaceRoot = "",
    [string]$RepoUrl = "https://github.com/Gouldin84/Yamaha-PortaSound-Studio.git",
    [string]$Branch = "main",
    [switch]$SkipWinget,
    [switch]$SkipDesktopShortcut
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $sysDrive = if ($env:SystemDrive) { $env:SystemDrive } else { "C:" }
    $WorkspaceRoot = Join-Path ($sysDrive + "\Projects") "Yamaha-PortaSound-Studio"
}

function Refresh-ProcessPath {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = ($machinePath, $userPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ";"
}

function Resolve-DotNetPath {
    $localDotNet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path $localDotNet) { return $localDotNet }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Ensure-WingetPackage([string]$PackageId, [string]$DisplayName) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Warning "winget not found; please install $DisplayName ($PackageId) manually."
        return
    }
    Write-Host "Installing $DisplayName ($PackageId) via winget..." -ForegroundColor Yellow
    & winget install --id $PackageId --exact --silent --accept-package-agreements --accept-source-agreements
    Refresh-ProcessPath
}

function Invoke-RobocopySafe([string]$Source, [string]$Dest) {
    if (-not (Test-Path $Source)) { return }
    if (-not (Test-Path $Dest)) {
        New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    }
    & robocopy $Source $Dest /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed ($LASTEXITCODE) restoring '$Source' -> '$Dest'"
    }
}

$cleanupTempStage = $false
$sourceDir = $ExtractedRoot

if ([string]::IsNullOrWhiteSpace($sourceDir)) {
    if ([string]::IsNullOrWhiteSpace($BundleZip)) {
        $dlZip = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads\PortaSound-Workstation-Migration.zip"
        $localZip = Join-Path $PSScriptRoot "PortaSound-Workstation-Migration.zip"
        if (Test-Path $localZip) { $BundleZip = $localZip }
        elseif (Test-Path $dlZip) { $BundleZip = $dlZip }
        else { throw "Specify -BundleZip <path-to-PortaSound-Workstation-Migration.zip> or -ExtractedRoot <dir>." }
    }

    if (-not (Test-Path $BundleZip)) {
        throw "Bundle zip not found at '$BundleZip'."
    }

    Unblock-File -Path $BundleZip -ErrorAction SilentlyContinue
    $sourceDir = Join-Path ([System.IO.Path]::GetTempPath()) ("PSS_AllyRestore_" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
    $cleanupTempStage = $true

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Write-Host "Extracting '$BundleZip'..." -ForegroundColor Cyan
    [System.IO.Compression.ZipFile]::ExtractToDirectory($BundleZip, $sourceDir)
}

try {
    Write-Host "=== 1. Verifying Developer Prerequisites ===" -ForegroundColor Cyan
    if (-not $SkipWinget) {
        if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
            Ensure-WingetPackage -PackageId "Git.Git" -DisplayName "Git for Windows"
        }

        $dotnetBin = Resolve-DotNetPath
        $hasNet8Sdk = $false
        if ($dotnetBin) {
            $sdks = (& $dotnetBin --list-sdks 2>$null) -join "`n"
            if ($sdks -match '(^|\n)8\.') { $hasNet8Sdk = $true }
        }
        if (-not $hasNet8Sdk) {
            Ensure-WingetPackage -PackageId "Microsoft.DotNet.SDK.8" -DisplayName ".NET 8 SDK"
        }

        $wv2Keys = @(
            "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
        )
        $hasWv2 = $wv2Keys | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $hasWv2) {
            Ensure-WingetPackage -PackageId "Microsoft.EdgeWebView2Runtime" -DisplayName "Microsoft Edge WebView2 Runtime"
        }

        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            Ensure-WingetPackage -PackageId "GitHub.cli" -DisplayName "GitHub CLI (optional)"
        }
    }

    $gitCmd = Get-Command git -ErrorAction SilentlyContinue
    if (-not $gitCmd) { throw "Git is required but was not found on PATH." }

    if ([string]::IsNullOrWhiteSpace((& git config --global --get user.name 2>$null))) {
        & git config --global user.name "Gouldin84"
    }
    if ([string]::IsNullOrWhiteSpace((& git config --global --get user.email 2>$null))) {
        & git config --global user.email "Gouldin84@users.noreply.github.com"
    }
    if ([string]::IsNullOrWhiteSpace((& git config --global --get credential.helper 2>$null))) {
        & git config --global credential.helper manager
    }

    Write-Host "=== 2. Cloning or Syncing Repository -> '$WorkspaceRoot' ===" -ForegroundColor Cyan
    $wsParent = Split-Path $WorkspaceRoot -Parent
    if (-not (Test-Path $wsParent)) {
        New-Item -ItemType Directory -Path $wsParent -Force | Out-Null
    }
    if (-not (Test-Path (Join-Path $WorkspaceRoot ".git"))) {
        & git clone --branch $Branch $RepoUrl $WorkspaceRoot
        if ($LASTEXITCODE -ne 0) { throw "git clone failed ($LASTEXITCODE)." }
    }
    else {
        & git -C $WorkspaceRoot fetch origin
        & git -C $WorkspaceRoot checkout $Branch
        & git -C $WorkspaceRoot pull --ff-only origin $Branch
    }

    Write-Host "=== 3. Restoring MIDI Libraries, AppData Settings & Manuals ===" -ForegroundColor Cyan
    $srcAppData = Join-Path $sourceDir "app_data\settings.json"
    if (Test-Path $srcAppData) {
        $dstAppData = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "YamahaPortaSoundStudio"
        New-Item -ItemType Directory -Path $dstAppData -Force | Out-Null
        Copy-Item $srcAppData (Join-Path $dstAppData "settings.json") -Force
    }

    $srcMidi = Join-Path $sourceDir "midi_library"
    if (Test-Path $srcMidi) {
        $dstMidi = Join-Path ([Environment]::GetFolderPath("MyMusic")) "Midi"
        Invoke-RobocopySafe -Source $srcMidi -Dest $dstMidi
        Write-Host "Restored MIDI & SysEx libraries to '$dstMidi'." -ForegroundColor Gray
    }

    $srcManuals = Join-Path $sourceDir "manuals"
    if (Test-Path $srcManuals) {
        Invoke-RobocopySafe -Source $srcManuals -Dest (Join-Path $WorkspaceRoot "Manuals")
    }

    Write-Host "=== 4. Restoring Antigravity Rules, Custom Skills & Handoff Notes ===" -ForegroundColor Cyan
    $dstGeminiCfg = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".gemini\config"
    New-Item -ItemType Directory -Path $dstGeminiCfg -Force | Out-Null

    $srcRules = Join-Path $sourceDir "gemini_config\GEMINI.md"
    if (Test-Path $srcRules) {
        Copy-Item $srcRules (Join-Path $dstGeminiCfg "GEMINI.md") -Force
    }

    $srcSkills = Join-Path $sourceDir "gemini_config\skills"
    if (Test-Path $srcSkills) {
        Invoke-RobocopySafe -Source $srcSkills -Dest (Join-Path $dstGeminiCfg "skills")
    }

    $srcHandoff = Join-Path $sourceDir "handoff"
    if (Test-Path $srcHandoff) {
        $dstHandoff = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".gemini\handoff\Yamaha-PortaSound-Studio"
        Invoke-RobocopySafe -Source $srcHandoff -Dest $dstHandoff
        Write-Host "Handoff docs placed at '$dstHandoff'." -ForegroundColor Gray
    }

    Write-Host "=== 5. Installing Standalone App & Desktop Shortcut ===" -ForegroundColor Cyan
    $srcStandalone = Join-Path $sourceDir "standalone_app"
    if (Test-Path $srcStandalone) {
        $dstDist = Join-Path $WorkspaceRoot "dist"
        Invoke-RobocopySafe -Source $srcStandalone -Dest $dstDist
        Get-ChildItem $dstDist -File | Unblock-File -ErrorAction SilentlyContinue

        if (-not $SkipDesktopShortcut) {
            $launchBat = Join-Path $dstDist "Launch-Studio.bat"
            $iconPath = Join-Path $WorkspaceRoot "src\PortaSoundStudio\Assets\app.ico"
            if (Test-Path $launchBat) {
                $desktop = [Environment]::GetFolderPath("Desktop")
                $lnkPath = Join-Path $desktop "Yamaha PortaSound Studio.lnk"
                $wsh = New-Object -ComObject WScript.Shell
                $sc = $wsh.CreateShortcut($lnkPath)
                $sc.TargetPath = $launchBat
                $sc.WorkingDirectory = $dstDist
                if (Test-Path $iconPath) { $sc.IconLocation = $iconPath }
                $sc.Save()
                Write-Host "Created Desktop shortcut: '$lnkPath'." -ForegroundColor Gray
            }
        }
    }

    Write-Host "=== 6. Verifying Build & Automated Tests on Destination Device ===" -ForegroundColor Cyan
    $dotnet = Resolve-DotNetPath
    if (-not $dotnet) { throw ".NET 8 SDK was not found after setup." }
    $testProj = Join-Path $WorkspaceRoot "tests\PortaSoundStudio.Tests\PortaSoundStudio.Tests.csproj"
    & $dotnet test $testProj -c Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Test suite failed on destination device ($LASTEXITCODE)."
    }

    Write-Host "`nROG Ally migration verified cleanly! Open '$WorkspaceRoot' in Antigravity or launch 'Yamaha PortaSound Studio' from Desktop." -ForegroundColor Green
}
finally {
    if ($cleanupTempStage -and (Test-Path $sourceDir)) {
        Remove-Item $sourceDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
