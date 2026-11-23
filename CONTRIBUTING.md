# Contributing to Yamaha PortaSound Studio

Thank you for your interest in contributing to **Yamaha PortaSound Studio**! We welcome contributions from developers, musicians, and vintage synthesizer enthusiasts.

---

## 🛠️ Development Setup

### Prerequisites
- **Operating System**: Windows 10 or Windows 11 (x64)
- **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE**: Visual Studio 2022 (Community or higher) with *.NET desktop development* workload, or Visual Studio Code / JetBrains Rider.

### Clone & Build
```powershell
# Clone the repository
git clone https://github.com/Gouldin84/Yamaha-PortaSound-Studio.git
cd Yamaha-PortaSound-Studio

# Restore dependencies
dotnet restore

# Build Release binary
dotnet build -c Release
```

### Running Automated Tests
The repository includes an extensive automated test suite covering MIDI remapping, FM synthesis SysEx, polyphony optimization, and file synchronization:
```powershell
dotnet test -c Release
```
> [!IMPORTANT]
> All unit tests must pass before submitting a Pull Request.

### Building Standalone Executable
To package the standalone single-file Windows x64 executable:
```powershell
dotnet publish src/PSS790Player/PSS790Player.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish_output
```

---

## 📐 Architecture & Contribution Guidelines

1. **Environment-Agnostic Code & Tests**:
   - Never hardcode developer-local machine paths (`C:\Users\<username>\...`, `C:\Projects\...`) in source code or automated tests.
   - Always use `Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)` or create temporary isolated sandboxes via `Path.GetTempPath()`.
   - Tests that check local user files must gracefully skip when running on GitHub Actions or virtual machines.

2. **Yamaha Hardware Fidelity**:
   - **PSS-790**: Advanced Wave Memory (AWM) PCM engine. Supports 28-voice polyphony, Chart 2 Program Changes, CC 7 volume, CC 10 pan, CC 91 onboard DSP reverb, and 4-voice Vector Synthesis.
   - **PSS-680 / 480 / 780**: 2-Operator FM (YM3420) engine. Supports 12-voice polyphony in Sound Source Mode 99, 100 factory presets, 5 user synth banks (Program 100–104), and 32 percussion instruments on Channel 16.
   - **PSS-680 Checksum**: Format 00H SysEx messages use a 7-bit two's complement checksum across all 66 transmitted nibbles (`sysex[4..69]`). Any new presets in `SoundDesignSeeder.cs` must satisfy this hardware requirement.

3. **Code Style**:
   - Clean, readable C# following standard .NET conventions.
   - Keep WebView2 IPC messages batched or throttled (e.g., 30 FPS channel activity dispatch) to avoid flooding the UI dispatcher.

---

## 🔀 Submitting a Pull Request

1. Fork the repository and create your feature branch from `main`:
   ```bash
   git checkout -b feat/my-new-feature
   ```
2. Commit your changes with a clear commit message (e.g. `feat(synth): add Roland TB-303 FM acid preset`).
3. Run `dotnet test -c Release` to verify all tests pass.
4. Push to your branch and open a Pull Request against `main`.
5. Clearly describe the changes, hardware tested (if applicable), and motivation.
