import os
import time
import logging
from typing import Optional, List, Dict, Any
from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel, Field
from faster_whisper import WhisperModel

from diarization import perform_diarization_and_matching

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("podtext_transcription")

app = FastAPI(
    title="PodtextCaption Transcription Service",
    description="Local audio transcription service using faster-whisper with word-level timestamps and speaker diarization",
    version="1.1.0"
)

# Global model cache to avoid reloading models unnecessarily
_cached_model: Optional[WhisperModel] = None
_cached_model_name: Optional[str] = None
_cached_device: str = "cpu"
_cached_compute_type: str = "int8"


class TranscribeRequest(BaseModel):
    file: str = Field(..., description="Absolute local path to audio file")
    language: Optional[str] = Field("auto", description="Language code (e.g. 'en', 'pt', 'es', 'auto')")
    model: Optional[str] = Field("small", description="Whisper model size (tiny, base, small, medium, large-v3)")
    title: Optional[str] = Field(None, description="Metadata title of the audio/video")
    description: Optional[str] = Field(None, description="Metadata description of the audio/video")


class WordItem(BaseModel):
    text: str
    start: float
    end: float
    probability: Optional[float] = None


class SpeakerItem(BaseModel):
    speaker_id: str
    label: str
    name: str
    inferred_name: Optional[str] = None
    confidence: float
    source: str
    is_confirmed: bool
    color_hex: str


class SegmentItem(BaseModel):
    id: int
    start: float
    end: float
    text: str
    words: List[WordItem]
    speaker_id: Optional[str] = None
    speaker_label: Optional[str] = None
    speaker_name: Optional[str] = None
    speaker_initials: Optional[str] = None
    speaker_color: Optional[str] = None


class TranscribeResponse(BaseModel):
    language: str
    duration: float
    segments: List[SegmentItem]
    speakers: List[SpeakerItem] = []


def get_whisper_model(model_size: str) -> WhisperModel:
    global _cached_model, _cached_model_name, _cached_device, _cached_compute_type
    
    if _cached_model is not None and _cached_model_name == model_size:
        return _cached_model

    logger.info(f"Loading Whisper model '{model_size}' on device={_cached_device} (compute_type={_cached_compute_type})...")
    
    # Try cpu with int8; fallback to float32 if int8 is unsupported on host CPU
    try:
        model = WhisperModel(model_size, device=_cached_device, compute_type=_cached_compute_type)
    except Exception as e:
        logger.warning(f"Failed to load with compute_type={_cached_compute_type}: {e}. Retrying with float32.")
        _cached_compute_type = "float32"
        model = WhisperModel(model_size, device=_cached_device, compute_type=_cached_compute_type)
        
    _cached_model = model
    _cached_model_name = model_size
    logger.info(f"Successfully loaded Whisper model '{model_size}'.")
    return model


@app.get("/health")
def health_check():
    return {
        "status": "ok",
        "engine": "faster-whisper",
        "cached_model": _cached_model_name,
        "device": _cached_device,
        "compute_type": _cached_compute_type
    }


@app.post("/transcribe", response_model=TranscribeResponse)
def transcribe_audio(req: TranscribeRequest):
    if not req.file or not os.path.exists(req.file):
        logger.error(f"File not found: {req.file}")
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Audio file not found: '{req.file}'"
        )

    model_size = req.model or "small"
    lang = None if (not req.language or req.language.lower() == "auto") else req.language.lower()

    try:
        model = get_whisper_model(model_size)
        logger.info(f"Starting transcription for '{req.file}' (language={lang or 'auto'})...")
        start_time = time.time()

        segments_raw, info = model.transcribe(
            req.file,
            language=lang,
            word_timestamps=True,
            beam_size=5
        )

        formatted_segments: List[SegmentItem] = []
        seg_id = 0

        for seg in segments_raw:
            seg_words: List[WordItem] = []
            if seg.words:
                for w in seg.words:
                    word_clean = w.word.strip()
                    if word_clean:
                        seg_words.append(WordItem(
                            text=word_clean,
                            start=round(w.start, 2),
                            end=round(w.end, 2),
                            probability=round(w.probability, 2) if w.probability else None
                        ))

            formatted_segments.append(SegmentItem(
                id=seg_id,
                start=round(seg.start, 2),
                end=round(seg.end, 2),
                text=seg.text.strip(),
                words=seg_words
            ))
            seg_id += 1

        # Perform Speaker Diarization and Name Matching
        segments_dict_list = [s.dict() for s in formatted_segments]
        diarized_segments_dicts, speaker_entities = perform_diarization_and_matching(
            req.file,
            segments_dict_list,
            req.title or "",
            req.description or ""
        )

        final_segments = [SegmentItem(**seg_d) for seg_d in diarized_segments_dicts]
        final_speakers = [SpeakerItem(**spk_d) for spk_d in speaker_entities]

        elapsed = round(time.time() - start_time, 2)
        duration = round(info.duration, 2) if info and info.duration else 0.0
        detected_lang = info.language if info and info.language else (lang or "unknown")

        logger.info(f"Transcription and diarization finished in {elapsed}s. Duration: {duration}s, Language: {detected_lang}, Segments: {len(final_segments)}, Speakers: {len(final_speakers)}")

        return TranscribeResponse(
            language=detected_lang,
            duration=duration,
            segments=final_segments,
            speakers=final_speakers
        )

    except Exception as ex:
        logger.exception("Error during transcription")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Transcription failed: {str(ex)}"
        )


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=5001)
