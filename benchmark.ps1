# Opens the training Benchmark page (benchmark/page.html) for this repo: one button runs ~10 s of real mlagents-learn
# training (the newest Training/FootballBase*.yaml, 4 headless env players, on the CPU) and records step time, machine
# and machine load in benchmark/history.json (commit it to compare laptops). The first run on a machine builds
# Builds/FootballEnv in Unity batch mode, so the Unity Editor must be closed and Unity Hub signed in.
# -Run: no page; runs one Benchmark in this console, records it in benchmark/history.json and exits 0 (recorded) or 1.
$ErrorActionPreference = 'Stop'
$py = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path $py)) {
    # The two commands at the top of requirements.txt (CLAUDE.md: Python 3.10, torch pinned to 2.5.1+cu121).
    Write-Host 'Creating .venv (Python 3.10, requirements.txt)...'
    Push-Location $PSScriptRoot
    try {
        uv venv --python 3.10.11 .venv
        uv pip install --python $py -r requirements.txt --extra-index-url https://download.pytorch.org/whl/cu121
    } finally { Pop-Location }
}
& $py (Join-Path $PSScriptRoot 'benchmark/serve.py') @args
exit $LASTEXITCODE
