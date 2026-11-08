<#
.SYNOPSIS
  One training run, start to finish: collect, build the dataset, train, convert, import, benchmark
  against the stock model, and write the audit report.

.DESCRIPTION
  Every stage writes into data/training/runs/<Run>/ and is skipped when its output is already
  there, so after a crash or a power cut the same command picks up where it stopped (training
  resumes from its last epoch checkpoint). Nothing is adopted: prod keeps its model until the owner
  has read report.md.

  Run it from a terminal that outlives any agent session (the PC's own terminal), with the dev
  instance's admin login in the environment:

    $env:PERSONAOS_USER = '…'; $env:PERSONAOS_PASSWORD = '…'
    ./training/overnight.ps1 -Run 2026-11-05 -Collect claims -Traces data/training/traces3.jsonl

  Needs: Docker Desktop running, Ollama with the stock model, Python 3.12+, the .NET SDK (the API is
  started if it is not already on :5080), and an NVIDIA GPU. The GPU power limit is checked, because
  this machine's PSU cut out twice at full GPU load on 2026-11-05.
#>
[CmdletBinding()]
param(
    [string]$Run = (Get-Date -Format 'yyyy-MM-dd'),
    # Capabilities to collect new turns for (training/capabilities). Empty: collect nothing new.
    [string[]]$Collect = @(),
    # Turns collected earlier, used as they are.
    [string[]]$Traces = @(),
    [string]$Base = 'unsloth/Qwen2.5-3B-Instruct',
    [double]$Epochs = 2,
    [double]$Lr = 1e-4,
    # The model that answers the collected turns; empty: whatever the instance is set to. A model
    # learns its own correct turns better than another's (run 2026-11-07-qwen3, trained on
    # qwen2.5:3b's turns, lost to the stock 4B it started from).
    [string]$CollectModel = '',
    [string]$Stock = 'qwen2.5:3b-instruct',
    [string]$Name = 'personaos-qwen2.5:3b',
    # The base model's chat template in Ollama (ollama_template.FAMILIES); also picks the Modelfile.
    [ValidateSet('qwen2.5', 'qwen3')]
    [string]$Family = 'qwen2.5',
    [int]$Runs = 3,
    [string[]]$Suites = @('all', 'holdout', 'holdout2', 'holdout3'),
    [int]$MaxGpuWatts = 120,
    [string]$Server = 'http://localhost:5080',
    [string]$Image = 'unsloth/unsloth:core-v0.1.908-beta'
)

$ErrorActionPreference = 'Stop'
# `powershell -File` hands "a,b" to a [string[]] parameter as one string; split it here.
$Collect, $Traces, $Suites = @($Collect, $Traces, $Suites) | ForEach-Object { , @($_ | ForEach-Object { $_ -split ',' } | Where-Object { $_ }) }
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$work = Join-Path $root 'data/training'
$dir = Join-Path $work "runs/$Run"
New-Item -ItemType Directory -Force -Path $dir, (Join-Path $dir 'bench') | Out-Null
Start-Transcript -Path (Join-Path $dir 'overnight.log') -Append | Out-Null
$inContainer = "/work/runs/$Run"

function Step([string]$text) { Write-Host ''; Write-Host ("== {0}  {1}" -f (Get-Date -Format 'HH:mm'), $text) }
function Native([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }
function Tag([string]$model) { $model -replace '[^A-Za-z0-9.-]+', '_' }
# Windows PowerShell turns a program's stderr into errors, which 'Stop' then throws on; cmd keeps
# both streams as plain text in the file.
function Into([string]$file, [string]$command) { cmd /c "$command > `"$file`" 2>&1" }

function Test-Api {
    # Any HTTP answer, a 401 included, means the API is up.
    try { Invoke-WebRequest -UseBasicParsing -Uri "$Server/api/setup" -TimeoutSec 5 | Out-Null; return $true }
    catch { return [bool]$_.Exception.Response }
}

function Clear-Gpu {
    # Ollama keeps models in VRAM; training needs the card to itself. Something (an editor's coding
    # agent) may load a model again at once, so this waits until the card stays free.
    for ($i = 0; $i -lt 60; $i++) {
        $loaded = @((Invoke-RestMethod -Uri 'http://localhost:11434/api/ps').models)
        foreach ($m in $loaded) {
            Invoke-RestMethod -Method Post -Uri 'http://localhost:11434/api/generate' -Body (@{ model = $m.name; keep_alive = 0 } | ConvertTo-Json) | Out-Null
        }
        $used = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits)
        if ($loaded.Count -eq 0 -and $used -lt 2500) { return }
        Start-Sleep -Seconds 5
    }
    throw 'The GPU did not come free (something keeps loading a model into Ollama).'
}

# ---- Preflight ----------------------------------------------------------------------------------
Step "Run $Run in $dir"
if (-not $env:PERSONAOS_USER -or -not $env:PERSONAOS_PASSWORD) { throw 'Set PERSONAOS_USER and PERSONAOS_PASSWORD (the dev instance admin).' }
docker info --format '{{.ServerVersion}}' | Out-Null; Native 'docker info (is Docker Desktop running?)'
# Ollama does not always come back after a reboot (2026-11-05: every collected turn failed).
try { $tags = Invoke-RestMethod -Uri 'http://localhost:11434/api/tags' -TimeoutSec 10 } catch { throw 'Ollama is not answering on :11434; start the Ollama app first.' }
if (-not ($tags.models | Where-Object { $_.name -eq $Stock })) { throw "Ollama has no $Stock." }
$watts = [double](nvidia-smi --query-gpu=power.limit --format=csv,noheader,nounits)
if ($watts -gt $MaxGpuWatts) { throw "GPU power limit is $watts W; set it to $MaxGpuWatts W or less first (admin: nvidia-smi -pl $MaxGpuWatts)." }
Write-Host "GPU power limit $watts W"
"base=$Base epochs=$Epochs rank=16 lr=$Lr collect=$(if ($CollectModel) { $CollectModel } else { 'instance' }) gpu=$watts W" | Set-Content (Join-Path $dir 'params.txt')
$Traces | ForEach-Object { (Resolve-Path $_).Path } | Set-Content (Join-Path $dir 'inputs.txt')

if (-not (Test-Api)) {
    Step 'Start the API'
    $env:ASPNETCORE_URLS = $Server; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Start-Process -WindowStyle Hidden dotnet -ArgumentList @('run', '--no-launch-profile') `
        -WorkingDirectory (Join-Path $root 'src/backend/PersonaOS.Api') -RedirectStandardOutput (Join-Path $dir 'api.log') -RedirectStandardError (Join-Path $dir 'api.err.log') | Out-Null
    for ($i = 0; $i -lt 120 -and -not (Test-Api); $i++) { Start-Sleep -Seconds 2 }
    if (-not (Test-Api)) { throw "The API did not start; see api.log." }
}

# ---- 1. Collect ---------------------------------------------------------------------------------
$traceFile = Join-Path $dir 'traces.jsonl'
if ($Collect.Count -gt 0) {
    Step "Collect: $($Collect -join ', ')"
    $proxy = Start-Process -PassThru -WindowStyle Hidden python -ArgumentList @('-I', 'training/proxy.py', '--listen', '11500', '--log', (Join-Path $dir 'calls.jsonl'))
    $token = (Invoke-RestMethod -Method Post -Uri "$Server/api/auth/login" -ContentType 'application/json' `
        -Body (@{ username = $env:PERSONAOS_USER; password = $env:PERSONAOS_PASSWORD } | ConvertTo-Json)).accessToken
    $auth = @{ Authorization = "Bearer $token" }
    $instanceModel = (Invoke-RestMethod -Uri "$Server/api/setup" -Headers $auth).aiModel
    try {
        if ($CollectModel) {
            Invoke-RestMethod -Method Put -Uri "$Server/api/setup" -Headers $auth -ContentType 'application/json' `
                -Body (@{ aiModel = $CollectModel } | ConvertTo-Json) | Out-Null
            Write-Host "collecting with $CollectModel (instance had $instanceModel)"
        }
        Start-Sleep -Seconds 2
        python -I training/collect.py --proxy-log (Join-Path $dir 'calls.jsonl') --out $traceFile --skip-done --only @Collect
        Native 'collect'
    } finally {
        Stop-Process -Id $proxy.Id -Force -ErrorAction SilentlyContinue
        if ($CollectModel) {
            Invoke-RestMethod -Method Put -Uri "$Server/api/setup" -Headers $auth -ContentType 'application/json' `
                -Body (@{ aiModel = $instanceModel } | ConvertTo-Json) | Out-Null
        }
    }
}

# ---- 2. Dataset ---------------------------------------------------------------------------------
Step 'Dataset'
$all = @($Traces) + @(if (Test-Path $traceFile) { $traceFile })
python -I training/build_dataset.py (Join-Path $dir 'dataset') @all | Tee-Object -FilePath (Join-Path $dir 'dataset.log')
Native 'build_dataset'

# ---- 3. Train -----------------------------------------------------------------------------------
if (Test-Path (Join-Path $dir 'model/config.json')) { Step 'Train: already done' } else {
    # Train only on prompts identical to what Ollama sends (run 2026-11-05 was not, and lost).
    Step 'Check the training template against Ollama'
    python -I training/check_template.py --model $Stock --family $Family
    Native 'check_template (ollama_template.py no longer matches Ollama)'
    Step 'Train'
    Clear-Gpu
    $container = "personaos-train-$Run"
    Into (Join-Path $dir 'docker-rm.log') "docker rm -f $container"
    $code = (Join-Path $root 'training') -replace '\\', '/'
    $data = $work -replace '\\', '/'
    docker run -d --name $container --gpus all -v "${code}:/code" -v "${data}:/work" -v personaos-hf-cache:/root/.cache/huggingface -w /code $Image `
        python train.py --data "$inContainer/dataset" --out "$inContainer/model" --checkpoints "$inContainer/checkpoints" --base $Base --family $Family --epochs $Epochs --lr $Lr | Out-Null
    Native 'docker run (train)'
    $exit = docker wait $container
    Into (Join-Path $dir 'train.log') "docker logs $container"
    Into (Join-Path $dir 'docker-rm.log') "docker rm $container"
    if ($exit -ne '0') { throw "Training exited with $exit; see train.log. Run the same command again to resume." }
}

# ---- 4. Convert and quantize (llama.cpp b11721, as the README) ----------------------------------
$gguf = Join-Path $dir 'model.q4_K_M.gguf'
if (Test-Path $gguf) { Step 'Convert: already done' } else {
    Step 'Convert to GGUF q4_K_M'
    $data = $work -replace '\\', '/'
    $script = "pip install -q cmake && git clone -q --depth 1 --branch b11721 https://github.com/ggml-org/llama.cpp /tmp/llama.cpp && " +
        "pip install -q /tmp/llama.cpp/gguf-py && " +
        "python /tmp/llama.cpp/convert_hf_to_gguf.py $inContainer/model --outtype f16 --outfile $inContainer/model.f16.gguf && " +
        "cd /tmp/llama.cpp && cmake -B build -DGGML_CUDA=OFF -DLLAMA_CURL=OFF -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_SERVER=OFF && " +
        "cmake --build build --target llama-quantize -j 8 && " +
        "./build/bin/llama-quantize $inContainer/model.f16.gguf $inContainer/model.q4_K_M.gguf Q4_K_M"
    Into (Join-Path $dir 'convert.log') "docker run --rm -e UNSLOTH_SKIP_GPU_CHECK=1 -v `"${data}:/work`" $Image bash -c `"$script`""
    Native 'convert (see convert.log)'
    Remove-Item (Join-Path $dir 'model.f16.gguf') -ErrorAction SilentlyContinue
}

# ---- 5. Import into Ollama ----------------------------------------------------------------------
Step "Import as $Name"
$modelfile = Join-Path $dir 'Modelfile'
$source = if ($Family -eq 'qwen2.5') { 'training/Modelfile' } else { "training/Modelfile.$Family" }
(Get-Content (Join-Path $root $source) -Raw) -replace '(?m)^FROM .*$', 'FROM ./model.q4_K_M.gguf' | Set-Content -Encoding utf8 $modelfile
Push-Location $dir
try { ollama create $Name -f Modelfile; Native 'ollama create' } finally { Pop-Location }
# The trained model must be prompted exactly as it was trained (its template comes from the GGUF for qwen3).
Step "Check $Name's template"
python -I training/check_template.py --model $Name --family $Family
Native "check_template ($Name is not prompted as it was trained)"

# ---- 6. Benchmark -------------------------------------------------------------------------------
foreach ($suite in $Suites) {
    foreach ($model in @($Stock, $Name)) {
        $json = Join-Path $dir ("bench/{0}-{1}.json" -f (Tag $model), $suite)
        if (Test-Path $json) { continue }
        Step "Benchmark $model on $suite"
        # One failed suite (a timeout, a restart) should not lose the others; the report says what is missing.
        try {
            & (Join-Path $root 'scripts/model-check.ps1') -Suite $suite -Model $model -Runs $Runs -Json $json -Server $Server |
                Tee-Object -FilePath ($json -replace '\.json$', '.txt')
        } catch { Write-Warning "Benchmark $model on $suite failed: $_" }
    }
}

# ---- 7. Report ----------------------------------------------------------------------------------
Step 'Report'
python -I training/report.py $dir --stock $Stock --trained $Name
Native 'report'
Step "Done. Read $dir\report.md"
Stop-Transcript | Out-Null
