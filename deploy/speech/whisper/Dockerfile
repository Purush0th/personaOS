# Optional speech service for PersonaOS (speech-to-text): faster-whisper, plus a pass-through
# to Kokoro for text-to-speech. Built by the `speech` profile of deploy/docker-compose.yml.
# The NVIDIA driver comes from the host (nvidia-container-toolkit, or Docker Desktop on
# Windows); CUDA 12 and cuDNN 9 come from pip, so the image needs no CUDA base image.
FROM python:3.12-slim

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# ctranslate2 loads cuBLAS and cuDNN by name; the pip packages keep them in their own folders.
ENV LD_LIBRARY_PATH=/usr/local/lib/python3.12/site-packages/nvidia/cublas/lib:/usr/local/lib/python3.12/site-packages/nvidia/cudnn/lib \
    HF_HOME=/models \
    HF_HUB_DISABLE_SYMLINKS_WARNING=1 \
    PYTHONUNBUFFERED=1

COPY whisper_server.py .

# No root needed: the model folder is a volume that is owned by this user.
RUN useradd --create-home --uid 10001 speech && mkdir -p /models && chown speech /models
USER speech

EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=5s --start-period=120s --retries=3 \
  CMD ["python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8000/health', timeout=4)"]
CMD ["python", "whisper_server.py"]
