# GitShip auto-commit on Cursor agent stop (fail-open: never blocks the IDE).
$ErrorActionPreference = 'Continue'
try {
    $top = git rev-parse --show-toplevel 2>$null
    if (-not $top) { exit 0 }

    Push-Location $top

    # Nothing changed
    if (-not (git status --porcelain)) {
        Pop-Location
        exit 0
    }

    git add -A
    # Nothing staged (e.g. only ignored noise)
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Pop-Location
        exit 0
    }

    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    git commit -m "cursor(gitship): sync workspace edits $ts"
    Pop-Location
    exit 0
}
catch {
    exit 0
}
