# PodtextCaption Transcription Engine

This directory contains the Python FastAPI service that executes local audio transcription using `faster-whisper`.

## Requirements
- Python 3.10+
- `faster-whisper`
- `fastapi`
- `uvicorn`

## Endpoints
- `GET /health`: Health check and status of loaded model.
- `POST /transcribe`: Transcribe an audio file.

### Request Body (`POST /transcribe`)
```json
{
  "file": "/path/to/audio.mp3",
  "language": "auto",
  "model": "small"
}
```

### Response
```json
{
  "language": "en",
  "duration": 120.5,
  "segments": [
    {
      "id": 0,
      "start": 0.0,
      "end": 2.1,
      "text": "Hello world",
      "words": [
        { "text": "Hello", "start": 0.0, "end": 1.0 },
        { "text": "world", "start": 1.1, "end": 2.1 }
      ]
    }
  ]
}
```
