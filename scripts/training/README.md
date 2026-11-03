# Fine-tuning the local model on PersonaOS's own work

PersonaOS already decides much of a turn in code: the guards and routers fix dates, titles and the
choice of tool before anything reaches the user. This folder turns those checked results into
training data, so the small local model learns to get them right the first time. The app's code
teaches the model; the accuracy suite (`scripts/model-check.ps1`) measures whether it worked.

Everything here is for a development machine with an NVIDIA GPU (tested on an RTX 3060, 12 GB).

## Licence

Qwen2.5-3B is released under the Qwen Research Licence: non-commercial use only. Fine-tuning it
and running the result on your own server is fine; publishing the trained weights is not part of
this project. Keep the model on your own machines. The scripts here are Apache-2.0 like the rest.

## Steps

1. **Record the model calls.** Start the proxy, which passes every Ollama call through and logs it:

   ```bash
   python -I scripts/training/proxy.py --listen 11500 --log data/training/calls.jsonl
   ```

2. **Collect.** With the API running on a development instance that uses Ollama, run the training
   prompts (`prompts.py`) through it. The script points the instance at the proxy, records each
   turn and what it ended in, discards every proposal and deleted note, and restores the setting:

   ```bash
   PERSONAOS_USER=… PERSONAOS_PASSWORD=… python -I scripts/training/collect.py \
       --proxy-log data/training/calls.jsonl --out data/training/traces.jsonl
   ```

   The prompts are written apart from the accuracy suite; `python -I scripts/training/prompts.py
   scripts/model-check.ps1` lists any that come too close to a suite message.

3. **Build the dataset.** Only turns that ended where their prompt says become tool and reply
   examples; the intent call is always labelled from the prompt:

   ```bash
   python -I scripts/training/build_dataset.py data/training/traces.jsonl data/training/dataset
   ```

4. **Train** (LoRA on Qwen2.5-3B-Instruct, in the unsloth container, about half an hour):

   ```bash
   docker run --rm --gpus all -v "$PWD/scripts/training:/code" -v "$PWD/data/training:/work" \
       -v personaos-hf-cache:/root/.cache/huggingface -w /code unsloth/unsloth:core-v0.1.908-beta \
       python train.py --data /work/dataset --out /work/model
   ```

   Training text is rendered by `ollama_template.py`, a port of Ollama's own qwen2.5 template, so
   the model is trained on exactly what it will see when served.

5. **Import into Ollama** at the stock model's quantization. Ollama 0.40 cannot import a Qwen2
   safetensors model (its safetensors path is MLX-only) or quantize a GGUF at import, so llama.cpp
   (tag b11721) converts and quantizes it first, in the same container:

   ```bash
   docker run --rm -e UNSLOTH_SKIP_GPU_CHECK=1 -v "$PWD/data/training:/work" unsloth/unsloth:core-v0.1.908-beta bash -c "
     pip install -q cmake && git clone -q --depth 1 --branch b11721 https://github.com/ggml-org/llama.cpp /tmp/llama.cpp &&
     pip install -q /tmp/llama.cpp/gguf-py &&
     python /tmp/llama.cpp/convert_hf_to_gguf.py /work/model --outtype f16 --outfile /work/model.f16.gguf &&
     cd /tmp/llama.cpp && cmake -B build -DGGML_CUDA=OFF -DLLAMA_CURL=OFF -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_SERVER=OFF &&
     cmake --build build --target llama-quantize -j 8 &&
     ./build/bin/llama-quantize /work/model.f16.gguf /work/model.q4_K_M.gguf Q4_K_M"
   ollama create personaos-qwen2.5:3b -f scripts/training/Modelfile
   ```

6. **Measure.** Run every suite against the stock and the trained model and compare:

   ```powershell
   ./scripts/model-check.ps1 -Suite all -Model personaos-qwen2.5:3b -Runs 3
   ./scripts/model-check.ps1 -Suite holdout3 -Model personaos-qwen2.5:3b -Runs 3
   ```

   Ship it (point the instance at the new model) only when no suite drops.
