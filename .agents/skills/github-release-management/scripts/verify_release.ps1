<#
.SYNOPSIS
    Automated GitHub Actions CI & Release Asset Verification Script
.DESCRIPTION
    Uses Git Credential Manager to query the GitHub REST API for the current repository,
    monitors in-progress workflow runs, diagnoses step failures, and confirms release asset uploads.
.PARAMETER Tag
    Optional tag name (e.g. 'v2.3.1'). Defaults to latest git tag or active branch.
.PARAMETER TimeoutSeconds
    Maximum time to wait for in-progress workflow runs before timing out. Default is 300.
#>
param(
    [string]$Tag = "",
    [int]$TimeoutSeconds = 300
)

# 1. Resolve Remote Repo Owner & Name
$remoteUrl = git remote get-url origin 2>$null
if (-not $remoteUrl) {
    Write-Error "Not a git repository or no 'origin' remote found."
    exit 1
}

$repoMatch = [regex]::Match($remoteUrl, 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^\.]+)(\.git)?')
if (-not $repoMatch.Success) {
    Write-Error "Could not parse GitHub owner and repository from remote URL: $remoteUrl"
    exit 1
}

$owner = $repoMatch.Groups['owner'].Value
$repo = $repoMatch.Groups['repo'].Value

Write-Host "Target Repository: $owner/$repo" -ForegroundColor Cyan

# 2. Resolve Tag
if (-not $Tag) {
    $Tag = git describe --tags --exact-match 2>$null
    if (-not $Tag) {
        $Tag = git describe --tags --abbrev=0 2>$null
    }
}

if ($Tag) {
    Write-Host "Verifying Release / Tag: $Tag" -ForegroundColor Cyan
} else {
    Write-Host "No tag specified, checking latest workflow runs..." -ForegroundColor Yellow
}

# 3. Retrieve Token via Git Credential Manager
$gcmPath = "$env:LOCALAPPDATA\Programs\Git\mingw64\bin\git-credential-manager.exe"
if (-not (Test-Path $gcmPath)) {
    $gcmCmd = Get-Command "git-credential-manager" -ErrorAction SilentlyContinue
    if ($gcmCmd) { $gcmPath = $gcmCmd.Source }
}

$token = $null
if (Test-Path $gcmPath) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $gcmPath
    $psi.Arguments = "get"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.WriteLine("protocol=https")
    $p.StandardInput.WriteLine("host=github.com")
    $p.StandardInput.WriteLine("")
    $p.StandardInput.Flush()
    $p.StandardInput.Close()
    $output = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()

    foreach ($line in ($output -split "`r?`n")) {
        if ($line.StartsWith("password=")) {
            $token = $line.Substring("password=".Length).Trim()
            break
        }
    }
}

$headers = @{
    "User-Agent" = "PowerShell-GitHub-Verifier"
    "Accept" = "application/vnd.github+json"
}
if ($token) {
    $headers["Authorization"] = "Bearer $token"
}

# 4. Monitor GitHub Actions Workflow Runs
$startTime = [DateTime]::UtcNow
$runSuccess = $false

while (([DateTime]::UtcNow - $startTime).TotalSeconds -lt $TimeoutSeconds) {
    try {
        $runsUri = "https://api.github.com/repos/$owner/$repo/actions/runs"
        $runsData = Invoke-RestMethod -Uri $runsUri -Headers $headers
        $targetRun = $null

        if ($Tag) {
            $targetRun = $runsData.workflow_runs | Where-Object { $_.head_branch -eq $Tag } | Select-Object -First 1
            if (-not $targetRun) {
                Write-Host "Waiting for GitHub Actions workflow run for tag '$Tag' to register..."
            }
        } else {
            $targetRun = $runsData.workflow_runs | Select-Object -First 1
        }

        if ($targetRun) {
            Write-Host "`nWorkflow Run ID: $($targetRun.id) [$($targetRun.name)]" -ForegroundColor Green
            Write-Host "Status: $($targetRun.status) | Conclusion: $($targetRun.conclusion)" -ForegroundColor Yellow

            # Inspect Jobs & Steps
            $jobsUri = "https://api.github.com/repos/$owner/$repo/actions/runs/$($targetRun.id)/jobs"
            $jobsData = Invoke-RestMethod -Uri $jobsUri -Headers $headers

            foreach ($job in $jobsData.jobs) {
                Write-Host "  Job: $($job.name) -> $($job.status) / $($job.conclusion)"
                foreach ($step in $job.steps) {
                    $color = switch ($step.conclusion) {
                        "success" { "Green" }
                        "failure" { "Red" }
                        "skipped" { "DarkGray" }
                        default { "Yellow" }
                    }
                    Write-Host "    - $($step.name): $($step.status) $($step.conclusion)" -ForegroundColor $color
                }
            }

            if ($targetRun.status -eq "completed") {
                if ($targetRun.conclusion -eq "success") {
                    $runSuccess = $true
                    Write-Host "`n[SUCCESS] CI Workflow completed successfully!" -ForegroundColor Green
                    break
                } else {
                    Write-Host "`n[FAILURE] Workflow ended with conclusion: $($targetRun.conclusion)" -ForegroundColor Red
                    exit 1
                }
            }
        }
    } catch {
        Write-Warning "API Query Warning: $_"
    }

    Write-Host "Waiting for workflow to complete (checking again in 10s)..."
    Start-Sleep -Seconds 10
}

# 5. Verify Release Assets
if (-not $runSuccess) {
    Write-Error "Timed out waiting for GitHub Actions workflow run for tag '$Tag' to succeed."
    exit 1
}

Write-Host "`nVerifying GitHub Release Assets..." -ForegroundColor Cyan
try {
    $release = $null
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $releasesUri = "https://api.github.com/repos/$owner/$repo/releases"
        $releases = Invoke-RestMethod -Uri $releasesUri -Headers $headers

        if ($Tag) {
            $release = $releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
        } else {
            $release = $releases | Select-Object -First 1
        }

        if ($release -and -not $release.draft -and $release.assets.Count -gt 0) {
            break
        }
        Start-Sleep -Seconds 3
    }

    if ($release) {
        Write-Host "Release Found: $($release.name) ($($release.html_url))" -ForegroundColor Green
        Write-Host "Draft: $($release.draft) | Pre-release: $($release.prerelease)"
        Write-Host "Assets ($($release.assets.Count) total):" -ForegroundColor Cyan
        foreach ($asset in $release.assets) {
            $mb = [math]::Round($asset.size / 1MB, 2)
            Write-Host "  * $($asset.name) ($mb MB) -> $($asset.browser_download_url)" -ForegroundColor White
            if ($asset.size -le 0) {
                Write-Error "Asset '$($asset.name)' has 0 bytes!"
                exit 1
            }
        }

        if ($release.draft -or $release.assets.Count -eq 0) {
            Write-Error "Release '$($release.tag_name)' is draft or has 0 assets uploaded!"
            exit 1
        }
    } else {
        Write-Error "No release found on GitHub for tag $Tag!"
        exit 1
    }
} catch {
    Write-Error "Failed to fetch releases: $_"
    exit 1
}

Write-Host "`n[COMPLETE] All GitHub Actions tests passed and release assets are live." -ForegroundColor Green
