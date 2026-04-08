# SessionStart Hook for Stride Terrain Editor
# Reads project context and injects it into the agent context via additionalContext

$ErrorActionPreference = "SilentlyContinue"

$projectRoot = $env:CLAUDE_PROJECT_DIR
if (-not $projectRoot) {
    $projectRoot = $env:PWD
}
if (-not $projectRoot) {
    $projectRoot = Get-Location
}

$archFile = Join-Path $projectRoot "docs/ARCHITECTURE_OVERVIEW.md"
$logDir = Join-Path $projectRoot "docs/log"

# Read architecture overview (first 40 lines to stay under 10000 char limit)
$archContent = ""
if (Test-Path $archFile) {
    $archLines = Get-Content $archFile -Head 40
    $archContent = $archLines -join "`n"
} else {
    $archContent = "Architecture document not found"
}

# Find the latest log file
$latestLog = $null
$logContent = "No log found"
$logFileName = ""

if (Test-Path $logDir) {
    $latestLog = Get-ChildItem -Path $logDir -Recurse -Filter "*.md" |
                  Where-Object { $_.Name -ne "TEMPLATE.md" -and $_.Name -ne "README.md" } |
                  Sort-Object LastWriteTime -Descending |
                  Select-Object -First 1

    if ($latestLog) {
        $logFileName = $latestLog.Name
        $logLines = Get-Content $latestLog.FullName -Head 30
        $logContent = $logLines -join "`n"
    }
}

# Build additionalContext string
$context = @"
## Session Context (auto-loaded)

### Architecture Overview (docs/ARCHITECTURE_OVERVIEW.md)
$archContent

### Latest Log ($logFileName)
$logContent

---
**Next steps:** Use Explore subagent to read full context. Check Next Session section of the latest log.
"@

# Output JSON with additionalContext via hookSpecificOutput
$output = @{
    hookSpecificOutput = @{
        hookEventName = "SessionStart"
        additionalContext = $context
    }
} | ConvertTo-Json -Depth 10 -Compress

Write-Output $output
