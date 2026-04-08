# Stop Hook for Stride Terrain Editor
# Checks if session end requirements are met before allowing stop
# Requires: session log exists, log is complete, code review done if code changed

$ErrorActionPreference = "SilentlyContinue"

# Read JSON from stdin
$stdin = @($input)
if ($stdin.Count -gt 0) {
    $jsonString = $stdin -join "`n"
    try {
        $InputObject = $jsonString | ConvertFrom-Json
    } catch {
        $InputObject = @{}
    }
} else {
    $InputObject = @{}
}

# Prevent infinite loop: if stop_hook_active is true, allow stop
if ($InputObject.stop_hook_active -eq $true) {
    exit 0
}

$projectRoot = $env:CLAUDE_PROJECT_DIR
if (-not $projectRoot) {
    $projectRoot = $env:PWD
}
if (-not $projectRoot) {
    $projectRoot = Get-Location
}

$today = Get-Date -Format "yyyy-MM-dd"
$todayFormatted = Get-Date -Format "yyyy/MM/dd"
$logDir = Join-Path $projectRoot "docs/log/$todayFormatted"

# Read transcript to check for code changes and code review
$transcriptPath = $InputObject.transcript_path
$codeReviewDone = $false
$hasCodeChanges = $false

if ($transcriptPath -and (Test-Path $transcriptPath)) {
    $transcript = Get-Content $transcriptPath -Raw -Encoding UTF8

    # Check if there were any code changes (Edit, Write tools used)
    $hasCodeChanges = $transcript -match '"tool_name"\s*:\s*"(Edit|Write|MultiEdit)"'

    # Check if code review was done (subagent_type=code-reviewer)
    $codeReviewDone = $transcript -match '"subagent_type"\s*:\s*"code-reviewer"'
}

# Check if today's session log exists
$todayLogs = @()
if (Test-Path $logDir) {
    $todayLogs = Get-ChildItem -Path $logDir -Filter "*.md" |
                  Where-Object { $_.Name -match "^\d{4}-\d{2}-\d{2}-" }
}

$sessionLogExists = $todayLogs.Count -gt 0

# Read the log content to check for required sections
$logComplete = $false
$missingSections = @()

if ($sessionLogExists) {
    $latestLog = $todayLogs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $logContent = Get-Content $latestLog.FullName -Raw

    # Check for required sections
    $requiredSections = @("Session Goal", "What We Did", "Decisions Made", "Next Session", "Quick Reference")
    foreach ($section in $requiredSections) {
        if ($logContent -notmatch [regex]::Escape($section)) {
            $missingSections += $section
        }
    }

    $logComplete = $missingSections.Count -eq 0
}

# Check 1: Session log must exist
if (-not $sessionLogExists) {
    $output = @{
        decision = "block"
        reason = "Please create session log file: docs/log/$todayFormatted/$today-[seq]-[description].md using TEMPLATE.md. Fill in: Session Goal, What We Did, Decisions Made, Next Session, Quick Reference."
    } | ConvertTo-Json -Depth 10 -Compress

    Write-Output $output
    exit 0
}

# Check 2: Log must be complete
if (-not $logComplete) {
    $missingList = $missingSections -join ", "
    $output = @{
        decision = "block"
        reason = "Session log is missing required fields: $missingList. Please complete the log before ending."
    } | ConvertTo-Json -Depth 10 -Compress

    Write-Output $output
    exit 0
}

# Check 3: Code review required if there were code changes
if ($hasCodeChanges -and -not $codeReviewDone) {
    $output = @{
        decision = "block"
        reason = "Code changes were made but no code review was performed. Use Agent tool with subagent_type=code-reviewer to review your changes before ending."
    } | ConvertTo-Json -Depth 10 -Compress

    Write-Output $output
    exit 0
}

# All checks passed, allow stop
exit 0
