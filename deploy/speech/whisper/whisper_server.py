"""OpenAI-style speech server for PersonaOS: faster-whisper, with text-to-speech passed through.

Serves POST /v1/audio/transcriptions (multipart: file, model, language, response_format),
GET /v1/models and GET /health. PersonaOS has one speech address for both directions, so
POST /v1/audio/speech and GET /v1/audio/voices are passed on to Kokoro (the `kokoro` service of
the same compose file), and a self-hoster who runs only one of the two still gets a clear error
for the other.

Settings come from environment variables:
  WHISPER_MODEL    Whisper model (default large-v3-turbo)
  WHISPER_DEVICE   cuda or cpu (default cuda)
  WHISPER_COMPUTE  CTranslate2 compute type (default int8_float16; use int8 on the CPU)
  WHISPER_HOST     listen address (default 0.0.0.0, inside the container)
  WHISPER_PORT     listen port (default 8000)
  KOKORO_URL       Kokoro's OpenAI-style base URL (default http://kokoro:8880/v1)
Models download once into the HF_HOME folder, a volume in the compose file.
"""

import os
import tempfile
import threading
from pathlib import Path

import httpx
import numpy
import uvicorn
from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import JSONResponse, PlainTextResponse, Response
from faster_whisper import WhisperModel

MODEL_NAME = os.environ.get("WHISPER_MODEL", "large-v3-turbo")
DEVICE = os.environ.get("WHISPER_DEVICE", "cuda")
COMPUTE_TYPE = os.environ.get("WHISPER_COMPUTE", "int8_float16")
HOST = os.environ.get("WHISPER_HOST", "0.0.0.0")
PORT = int(os.environ.get("WHISPER_PORT", "8000"))
KOKORO_URL = os.environ.get("KOKORO_URL", "http://kokoro:8880/v1").rstrip("/")
MAX_UPLOAD_BYTES = 25 * 1024 * 1024
# Long replies take a while to synthesize; a dead container should still fail quickly.
kokoro = httpx.Client(timeout=httpx.Timeout(120.0, connect=5.0))

print(f"Loading {MODEL_NAME} on {DEVICE} ({COMPUTE_TYPE})...", flush=True)
model = WhisperModel(MODEL_NAME, device=DEVICE, compute_type=COMPUTE_TYPE)
# One model, one transcription at a time.
model_lock = threading.Lock()
# The first transcription on the GPU takes about 40 s while CUDA warms up; pay that at start,
# not on the user's first voice message.
list(model.transcribe(numpy.zeros(16000, dtype=numpy.float32))[0])
print("Model loaded and warmed up.", flush=True)

app = FastAPI(title="PersonaOS speech")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/v1/models")
def models() -> dict[str, object]:
    return {"object": "list", "data": [{"id": MODEL_NAME, "object": "model", "owned_by": "local"}]}


@app.post("/v1/audio/transcriptions", response_model=None)
def transcribe(
    file: UploadFile = File(...),
    model_name: str = Form(MODEL_NAME, alias="model"),
    language: str | None = Form(None),
    response_format: str = Form("json"),
) -> JSONResponse | PlainTextResponse:
    # The model in the request is accepted for compatibility; the loaded model always answers.
    data = file.file.read(MAX_UPLOAD_BYTES + 1)
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(status_code=413, detail="The audio is larger than 25 MB.")
    if not data:
        raise HTTPException(status_code=400, detail="The audio file is empty.")

    suffix = Path(file.filename or "audio").suffix or ".bin"
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(data)
        path = tmp.name
    try:
        with model_lock:
            segments, info = model.transcribe(path, language=language or None, vad_filter=True, beam_size=5)
            text = " ".join(segment.text.strip() for segment in segments).strip()
    except Exception as error:  # a file it cannot decode is the caller's problem, not a crash
        raise HTTPException(status_code=400, detail=f"Could not transcribe the audio: {error}") from error
    finally:
        os.unlink(path)

    if response_format == "text":
        return PlainTextResponse(text)
    if response_format == "verbose_json":
        return JSONResponse({"text": text, "language": info.language, "duration": info.duration})
    return JSONResponse({"text": text})


def _to_kokoro(method: str, path: str, body: bytes | None = None, content_type: str | None = None) -> Response:
    """Passes a text-to-speech call on to Kokoro and hands back its answer as it is."""
    headers = {"Content-Type": content_type} if content_type else {}
    try:
        answer = kokoro.request(method, f"{KOKORO_URL}/{path}", content=body, headers=headers)
    except httpx.HTTPError as error:
        raise HTTPException(
            status_code=502, detail=f"The Kokoro text-to-speech service is not reachable: {error}"
        ) from error
    return Response(
        content=answer.content,
        status_code=answer.status_code,
        media_type=answer.headers.get("content-type"),
    )


@app.post("/v1/audio/speech")
async def speech(request: Request) -> Response:
    body = await request.body()
    # Off the event loop, so a long synthesis does not hold up a transcription.
    return await run_in_threadpool(_to_kokoro, "POST", "audio/speech", body, request.headers.get("content-type"))


@app.get("/v1/audio/voices")
def voices() -> Response:
    return _to_kokoro("GET", "audio/voices")


if __name__ == "__main__":
    uvicorn.run(app, host=HOST, port=PORT)
