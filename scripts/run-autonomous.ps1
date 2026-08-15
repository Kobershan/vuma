<#
    Vuma Retail — unattended stage runner.
    Runs one stage per Claude Code invocation, verifies real progress, logs everything, stops cleanly.
    Read docs/AUTONOMOUS_OPERATION.md before first use — there is one-time setup.
#>
param(
    [int]$MaxStages = 99,
    [string]$Model = "opus",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$stamp   = Get-Date -Format "yyyyMMdd-HHmmss"
$logDir  = Join-Path $repo "logs\autonomous\$stamp"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$summary = Join-Path $logDir "summary.md"

function Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $msg
    Write-Host $line
    Add-Content -Path $summary -Value $line
}

function Get-NextStage {
    $board = Get-Content "docs\PROGRESS.md" -Raw
    foreach ($line in ($board -split "`n")) {
        if ($line -match '^\|\s*(\d\d[b]?)\s*\|\s*([^|]+?)\s*\|\s*(NOT_STARTED|IN_PROGRESS)\s*\|') {
            return @{ Number = $Matches[1]; Title = $Matches[2].Trim() }
        }
    }
    return $null
}

$prompt = @"
Read CLAUDE.md and docs/PROGRESS.md, then execute the next incomplete stage to completion following
the session protocol in CLAUDE.md section 0. Do not ask me any questions - I am asleep and cannot
answer. Make every decision yourself and record it as an ADR in docs/DECISIONS.md. Finish at a green
build, update docs/PROGRESS.md, and commit. If you cannot complete the stage, mark it BLOCKED in
PROGRESS.md with the exact reason, leave the repo building, and commit that.
"@

Log "Vuma Retail autonomous run started. Model: $Model. Max stages: $MaxStages."

for ($i = 1; $i -le $MaxStages; $i++) {

    $stage = Get-NextStage
    if (-not $stage) { Log "No incomplete stages remain. All done."; break }

    Log "--- Stage $($stage.Number): $($stage.Title) ---"

    $before     = (git rev-parse HEAD).Trim()
    $beforeHash = (Get-FileHash "docs\PROGRESS.md").Hash
    $stageLog   = Join-Path $logDir ("stage-{0}.log" -f $stage.Number)

    if ($DryRun) { Log "DRY RUN - would invoke Claude here."; break }

    try {
        claude -p $prompt `
            --permission-mode bypassPermissions `
            --model $Model `
            --verbose 2>&1 | Tee-Object -FilePath $stageLog
    } catch {
        Log "Claude invocation failed: $_"
        break
    }

    # ---- verify real progress, three independent signals ----
    $after     = (git rev-parse HEAD).Trim()
    $afterHash = (Get-FileHash "docs\PROGRESS.md").Hash
    $committed = $after -ne $before
    $tracked   = $afterHash -ne $beforeHash

    Log "Committed: $committed | PROGRESS.md updated: $tracked"

    dotnet build -c Release --nologo 2>&1 | Tee-Object -FilePath (Join-Path $logDir "build-$($stage.Number).log") | Out-Null
    $built = $LASTEXITCODE -eq 0
    Log "Build green: $built"

    if (-not $built) {
        Log "STOPPING: build is broken. Do not run further stages on a red build."
        break
    }
    if (-not ($committed -and $tracked)) {
        Log "STOPPING: no verifiable progress on stage $($stage.Number). Check $stageLog."
        break
    }

    Log "Stage $($stage.Number) complete."
}

Log "Run finished. Logs in $logDir"
