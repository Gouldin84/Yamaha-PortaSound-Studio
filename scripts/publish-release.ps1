param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch "^v\d+\.\d+\.\d+$") {
    Write-Error "Version must follow semver format prefixed with 'v', e.g., v1.0.0"
}

$DotnetPath = if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") {
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
} else {
    "dotnet"
}

Write-Host "==> Running Automated Tests..." -ForegroundColor Cyan
& $DotnetPath test -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed. Release aborted."
}

Write-Host "==> Publishing Standalone Executable for $Version..." -ForegroundColor Cyan
$OutputDir = "$PSScriptRoot\..\bin\Publish"
& $DotnetPath publish "$PSScriptRoot\..\src\PSS790Player\PSS790Player.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publishing failed. Release aborted."
}

Write-Host "==> Standalone executable successfully built at $OutputDir\PSS790Player.exe" -ForegroundColor Green

Write-Host "==> Committing and Tagging Release $Version..." -ForegroundColor Cyan
git add -A
git commit -m "chore(release): $Version" --allow-empty
git tag -a $Version -m "Release $Version"

Write-Host "==> Pushing to GitHub (main & tag $Version)..." -ForegroundColor Cyan
git push origin main
git push origin $Version

Write-Host "==> Release $Version successfully pushed! GitHub Actions will publish the release artifacts." -ForegroundColor Green
