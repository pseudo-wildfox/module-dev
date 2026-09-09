$ErrorActionPreference = "Stop"

$repoDir = $PSScriptRoot
$checkerDir = Join-Path $repoDir "autocheck"

py -3.12 (Join-Path $checkerDir "public_check.py") `
    --repo $repoDir `
    --fixtures (Join-Path $checkerDir "fixtures") `
    --output (Join-Path $repoDir "week-1-public-report.json") `
    @args

exit $LASTEXITCODE