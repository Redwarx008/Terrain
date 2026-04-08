# PreToolUse Hook for Stride Terrain Editor
# Blocks git commit if today's session log doesn't exist yet

$ErrorActionPreference = "SilentlyContinue"

$projectRoot = $env:CLAUDE_PROJECT_DIR
if (-not $projectRoot) {
    $projectRoot = $env:PWD
}
if (-not $projectRoot) {
    $projectRoot = Get-Location
}

$todayFormatted = Get-Date -Format "yyyy/MM/dd"
$logDir = Join-Path $projectRoot "docs/log/$todayFormatted"

# Check if today's session log exists
$todayLogs = @()
if (Test-Path $logDir) {
    $todayLogs = Get-ChildItem -Path $logDir -Filter "*.md" |
                  Where-Object { $_.Name -match "^\d{4}-\d{2}-\d{2}-" }
}

if ($todayLogs.Count -eq 0) {
    $today = Get-Date -Format "yyyy-MM-dd"
    $output = @{
        hookSpecificOutput = @{
            hookEventName = "PreToolUse"
            permissionDecision = "deny"
            permissionDecisionReason = "Please create today's session log before committing: docs/log/$todayFormatted/$today-[seq]-[description].md"
        }
    } | ConvertTo-Json -Depth 10 -Compress

    Write-Output $output
}

exit 0
