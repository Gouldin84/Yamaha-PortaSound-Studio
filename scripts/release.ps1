param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$CommitMessage = ""
)

$ErrorActionPreference = "Stop"

$dotnet = "C:\Users\MichaelGoulding\AppData\Local\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$tag = "v$Version"
if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
    $CommitMessage = "chore(release): bump version to $Version"
}

Write-Host "=== 1. Running Release Tests ===" -ForegroundColor Cyan
& $dotnet test tests\PSS790Player.Tests\PSS790Player.Tests.csproj -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Tests failed. Aborting release." }

Write-Host "=== 2. Bumping Version to $Version ===" -ForegroundColor Cyan
$csprojPath = "src\PSS790Player\PSS790Player.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version</FileVersion>"
Set-Content -Path $csprojPath -Value $csproj -NoNewline

$indexPath = "src\PSS790Player\WebUI\index.html"
$index = Get-Content $indexPath -Raw
$index = $index -replace '\?v=\d+\.\d+\.\d+', "?v=$Version"
Set-Content -Path $indexPath -Value $index -NoNewline

Write-Host "=== 3. Publishing Self-Contained Win-x64 to dist/ ===" -ForegroundColor Cyan
$publishDir = "publish_output"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& $dotnet publish src\PSS790Player\PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Publish failed. Aborting release." }

if (-not (Test-Path "dist")) { New-Item -ItemType Directory -Path "dist" | Out-Null }
Copy-Item "Launch-Studio.bat" "dist\Launch-Studio.bat" -Force
Copy-Item "ASUS_ROG_ALLY_SETUP.txt" "dist\ASUS_ROG_ALLY_SETUP.txt" -Force
Copy-Item "$publishDir\PSS-Studio.exe" "dist\PSS-Studio.exe" -Force

# Remove stale versioned release archives in dist/ to prevent local bloat
Get-ChildItem "dist\PSS-Studio-v*-win-x64.zip" -ErrorAction SilentlyContinue | Remove-Item -Force

$versionZip = "dist\PSS-Studio-$tag-win-x64.zip"
$latestZip = "dist\PSS-Studio-win-x64.zip"
if (Test-Path $versionZip) { Remove-Item $versionZip -Force }
if (Test-Path $latestZip) { Remove-Item $latestZip -Force }

Compress-Archive -Path "$publishDir\PSS-Studio.exe", "dist\Launch-Studio.bat", "dist\ASUS_ROG_ALLY_SETUP.txt" -DestinationPath $versionZip -CompressionLevel Optimal
Copy-Item $versionZip $latestZip -Force
Remove-Item $publishDir -Recurse -Force

Write-Host "=== 4. Committing, Tagging ($tag), and Pushing ===" -ForegroundColor Cyan
git add -A
git commit -m $CommitMessage
if ($LASTEXITCODE -ne 0) { throw "Git commit failed." }

git tag -a $tag -m "Release $tag"
if ($LASTEXITCODE -ne 0) { throw "Git tag failed." }

git push origin main
if ($LASTEXITCODE -ne 0) { throw "Git push main failed." }

git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "Git push tag failed." }

Write-Host "=== 5. Verifying GitHub Actions & Release Assets ===" -ForegroundColor Cyan
$verifier = ".agents\skills\github-release-management\scripts\verify_release.ps1"
if (Test-Path $verifier) {
    powershell -ExecutionPolicy Bypass -File $verifier -Tag $tag
    if ($LASTEXITCODE -ne 0) { throw "Release verification failed." }
} else {
    Write-Warning "verify_release.ps1 not found at $verifier"
}

Write-Host "Release $tag completed and verified!" -ForegroundColor Green
