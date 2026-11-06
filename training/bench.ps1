<#
.SYNOPSIS
  Benchmark models on the accuracy suites without training: after an app change, re-measure the
  stock and trained models already in Ollama, then write the report beside the run's training data.

.EXAMPLE
  .\training\bench.ps1 -Run 2026-11-06b -Into bench-2
#>
param(
    [Parameter(Mandatory)][string]$Run,
    [string]$Into = 'bench-2',
    [string]$Stock = 'qwen2.5:3b-instruct',
    [string]$Name = 'personaos-qwen2.5:3b',
    [string[]]$Suites = @('all', 'holdout', 'holdout2', 'holdout3'),
    [int]$Runs = 3,
    [string]$Server = 'http://localhost:5080'
)
$ErrorActionPreference = 'Stop'
$Suites = @($Suites | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "data/training/runs/$Run"
$out = Join-Path $dir $Into
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($suite in $Suites) {
    foreach ($model in @($Stock, $Name)) {
        $json = Join-Path $out ("{0}-{1}.json" -f ($model -replace '[:/]', '_'), $suite)
        if (Test-Path $json) { continue }
        Write-Host ''
        Write-Host ("== {0}  Benchmark {1} on {2}" -f (Get-Date -Format 'HH:mm'), $model, $suite)
        try {
            & (Join-Path $root 'scripts/model-check.ps1') -Suite $suite -Model $model -Runs $Runs -Json $json -Server $Server |
                Tee-Object -FilePath ($json -replace '\.json$', '.txt')
        } catch { Write-Warning "Benchmark $model on $suite failed: $_" }
    }
}

python -I (Join-Path $root 'training/report.py') $dir --stock $Stock --trained $Name --bench $Into --out (Join-Path $dir "report-$Into.md")
if ($LASTEXITCODE -ne 0) { throw "report failed (exit $LASTEXITCODE)" }
Write-Host ("== {0}  Done. Read {1}" -f (Get-Date -Format 'HH:mm'), (Join-Path $dir "report-$Into.md"))
