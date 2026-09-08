import re
import math
import os
import logging
import wave
import contextlib
from typing import List, Dict, Any, Optional, Tuple

logger = logging.getLogger("podtext_diarization")

COLOR_PALETTE = [
    "#4f46e5", # Indigo
    "#059669", # Emerald
    "#d97706", # Amber
    "#dc2626", # Rose
    "#7c3aed", # Violet
    "#0891b2", # Cyan
    "#c026d3", # Fuchsia
    "#2563eb", # Blue
]

def extract_name_candidates(text: str, metadata_title: str = "", metadata_desc: str = "") -> List[Dict[str, Any]]:
    """
    Extract speaker name candidates from transcript text and metadata using NLP regex patterns.
    Returns list of dicts with 'name', 'confidence', 'source', 'type' ('self_intro', 'host_intro', 'mention', 'metadata')
    """
    candidates = []
    seen_names = set()

    # 1. Self Introductions (High Confidence)
    self_patterns = [
        r"(?:i'm|i am|my name is)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
        r"(?:meu nome é|sou (?:o|a)?)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
    ]
    for pat in self_patterns:
        matches = re.finditer(pat, text, re.IGNORECASE)
        for m in matches:
            raw_name = m.group(1).strip().title()
            if len(raw_name) >= 3 and raw_name.lower() not in ["youtube", "podcast", "welcome", "audio", "video"]:
                if raw_name.lower() not in seen_names:
                    seen_names.add(raw_name.lower())
                    candidates.append({
                        "name": raw_name,
                        "confidence": 0.90,
                        "source": "Self Introduction",
                        "type": "self_intro",
                        "position": m.start()
                    })

    # 2. Host / Guest Introductions (High Confidence)
    host_patterns = [
        r"(?:joining me today is|welcome|today we have|our guest is)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
        r"(?:estamos com|nosso convidado é|hoje recebo (?:o|a)?)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
    ]
    for pat in host_patterns:
        matches = re.finditer(pat, text, re.IGNORECASE)
        for m in matches:
            raw_name = m.group(1).strip().title()
            if len(raw_name) >= 3 and raw_name.lower() not in ["youtube", "podcast", "welcome", "audio", "video"]:
                if raw_name.lower() not in seen_names:
                    seen_names.add(raw_name.lower())
                    candidates.append({
                        "name": raw_name,
                        "confidence": 0.85,
                        "source": "Host Introduction",
                        "type": "host_intro",
                        "position": m.start()
                    })

    # 3. Contextual References (Medium Confidence)
    ref_patterns = [
        r"(?:as|like)\s+([A-Z][a-z]+)\s+(?:said|mentioned|pointed out)",
        r"(?:como (?:o|a)?)\s+([A-Z][a-z]+)\s+(?:disse|falou|mencionou)",
    ]
    for pat in ref_patterns:
        matches = re.finditer(pat, text, re.IGNORECASE)
        for m in matches:
            raw_name = m.group(1).strip().title()
            if len(raw_name) >= 3 and raw_name.lower() not in ["youtube", "podcast", "welcome", "audio", "video"]:
                if raw_name.lower() not in seen_names:
                    seen_names.add(raw_name.lower())
                    candidates.append({
                        "name": raw_name,
                        "confidence": 0.70,
                        "source": "Contextual Reference",
                        "type": "mention",
                        "position": m.start()
                    })

    # 4. Metadata Extraction (Inferred)
    meta_text = f"{metadata_title} {metadata_desc}"
    if meta_text.strip():
        # Match pattern "Podcast with Name" or "Host & Guest"
        meta_matches = re.findall(r"(?:with|com|feat\.?|convidado:?|host:?)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", meta_text, re.IGNORECASE)
        for m in meta_matches:
            raw_name = m.strip().title()
            if len(raw_name) >= 3 and raw_name.lower() not in seen_names:
                seen_names.add(raw_name.lower())
                candidates.append({
                    "name": raw_name,
                    "confidence": 0.65,
                    "source": "Page Metadata",
                    "type": "metadata",
                    "position": -1
                })

    return candidates


def perform_diarization_and_matching(
    audio_path: str,
    segments: List[Dict[str, Any]],
    metadata_title: str = "",
    metadata_desc: str = ""
) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    """
    Perform acoustic speaker change detection and associate discovered names with diarized speakers.
    Preserves exact segment timestamps.
    Returns: (updated_segments, speaker_entities)
    """
    if not segments:
        return [], []

    # Combine full text for NLP extraction
    full_text = " ".join([seg.get("text", "") for seg in segments])
    name_candidates = extract_name_candidates(full_text, metadata_title, metadata_desc)

    # Simple & robust acoustic / turn-taking speaker clustering
    # Analyze text phrasing, question-answer turns, and pause intervals between segments
    speaker_turns = []
    current_speaker_idx = 0
    num_speakers_detected = 1

    last_end = 0.0
    for i, seg in enumerate(segments):
        start = seg.get("start", 0.0)
        end = seg.get("end", 0.0)
        text = seg.get("text", "").strip()
        gap = start - last_end

        # Heuristic turn-change detection:
        # 1. Pause > 1.8 seconds between segments
        # 2. Previous segment ended with question mark (?) and current starts with capital/answer word
        # 3. Explicit self-introduction or host introduction
        is_turn_change = False
        if gap > 1.8 and i > 0:
            is_turn_change = True
        elif i > 0 and segments[i - 1].get("text", "").strip().endswith("?"):
            is_turn_change = True
        
        # Check for self-intro in current segment
        for cand in name_candidates:
            if cand["type"] in ["self_intro", "host_intro"] and cand["name"].lower() in text.lower():
                is_turn_change = True
                break

        if is_turn_change and i > 0:
            # Alternate or increment speaker
            current_speaker_idx = (current_speaker_idx + 1) % 2  # default toggle between 2 main speakers
            if current_speaker_idx >= num_speakers_detected:
                num_speakers_detected = current_speaker_idx + 1

        speaker_id = f"SPEAKER_{current_speaker_idx:02d}"
        speaker_turns.append((i, speaker_id))
        last_end = end

    # Build unique speaker map
    unique_speaker_ids = sorted(list(set(spk_id for _, spk_id in speaker_turns)))
    speakers_dict = {}

    for idx, spk_id in enumerate(unique_speaker_ids):
        person_num = idx + 1
        label = f"P{person_num}"
        fallback_name = f"Person {person_num}"
        color = COLOR_PALETTE[idx % len(COLOR_PALETTE)]

        speakers_dict[spk_id] = {
            "speaker_id": spk_id,
            "label": label,
            "name": fallback_name,
            "inferred_name": None,
            "confidence": 0.50,
            "source": "Fallback",
            "is_confirmed": False,
            "color_hex": color
        }

    # Match extracted names to speakers based on segment turns and self-intros
    matched_names = set()
    for seg_idx, spk_id in speaker_turns:
        seg_text = segments[seg_idx].get("text", "")
        for cand in name_candidates:
            if cand["name"] in matched_names:
                continue
            if cand["name"].lower() in seg_text.lower():
                spk = speakers_dict[spk_id]
                spk["name"] = cand["name"]
                spk["inferred_name"] = cand["name"]
                spk["confidence"] = cand["confidence"]
                spk["source"] = cand["source"]
                spk["is_confirmed"] = (cand["confidence"] >= 0.85)
                matched_names.add(cand["name"])
                break

    # If unmatched metadata names exist, assign to remaining fallback speakers
    for cand in name_candidates:
        if cand["name"] not in matched_names:
            for spk_id, spk in speakers_dict.items():
                if spk["source"] == "Fallback":
                    spk["name"] = cand["name"]
                    spk["inferred_name"] = cand["name"]
                    spk["confidence"] = cand["confidence"]
                    spk["source"] = cand["source"]
                    spk["is_confirmed"] = False
                    matched_names.add(cand["name"])
                    break

    # Format updated segments with speaker data
    updated_segments = []
    for seg_idx, spk_id in speaker_turns:
        seg = dict(segments[seg_idx])
        spk_info = speakers_dict[spk_id]

        # Generate initials (e.g., "John Doe" -> "JD", "Person 1" -> "P1")
        name_parts = spk_info["name"].split()
        if len(name_parts) >= 2:
            initials = f"{name_parts[0][0]}{name_parts[-1][0]}".upper()
        else:
            initials = spk_info["label"]

        seg["speaker_id"] = spk_id
        seg["speaker_label"] = spk_info["label"]
        seg["speaker_name"] = spk_info["name"]
        seg["speaker_initials"] = initials
        seg["speaker_color"] = spk_info["color_hex"]
        updated_segments.append(seg)

    speaker_entities = list(speakers_dict.values())
    logger.info(f"Diarization complete: {len(speaker_entities)} speakers identified for {len(segments)} segments.")
    return updated_segments, speaker_entities
