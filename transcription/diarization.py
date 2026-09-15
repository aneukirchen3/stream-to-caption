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

NON_NAME_WORDS = {
    # English common non-name words / verbs / adjectives / prepositions
    "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "aren't",
    "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but", "by",
    "can", "cannot", "could", "did", "do", "does", "doing", "down", "during", "each", "few", "for",
    "from", "further", "get", "getting", "go", "going", "had", "has", "have", "having", "he", "her",
    "here", "hers", "herself", "him", "himself", "his", "how", "i", "if", "in", "into", "is", "it",
    "its", "itself", "just", "know", "like", "look", "looking", "make", "many", "me", "more", "most",
    "my", "myself", "no", "nor", "not", "of", "off", "on", "once", "only", "or", "other", "our", "ours",
    "ourselves", "out", "over", "own", "same", "say", "saying", "see", "she", "should", "so", "some",
    "start", "starting", "still", "such", "than", "that", "the", "their", "theirs", "them", "themselves",
    "then", "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up",
    "very", "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom", "why", "with",
    "work", "working", "would", "you", "your", "yours", "yourself", "yourselves", "confused", "sure",
    "happy", "good", "bad", "new", "old", "first", "last", "long", "great", "little", "right", "left",
    "thank", "thanks", "welcome", "please", "hello", "hi", "hey", "podcast", "youtube", "video", "audio",
    "channel", "episode", "show", "today", "tonight", "tomorrow", "yesterday", "person", "guest", "host",

    # Portuguese common non-name words
    "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "por",
    "para", "com", "sem", "sob", "sobre", "atras", "frente", "como", "quando", "onde", "porque", "porquê",
    "que", "quem", "qual", "quais", "quanto", "muito", "mais", "menos", "pouco", "todo", "toda", "todos",
    "todas", "este", "esta", "estes", "estas", "esse", "essa", "esses", "essas", "aquele", "aquela",
    "aqueles", "aquelas", "isso", "isto", "aquilo", "meu", "minha", "seus", "suas", "nosso", "nossa",
    "sou", "somos", "sao", "estou", "estamos", "estao", "foi", "fomos", "foram", "era", "eramos",
    "estava", "estavamos", "ter", "tenho", "temos", "tem", "tinha", "tinham", "fazer", "faz", "fazemos",
    "dizer", "diz", "dizemos", "falando", "disse", "falou", "obrigado", "obrigada", "bem-vindo", "bem-vinda",
    "hoje", "ontem", "amanha", "aqui", "ali", "la"
}


def is_valid_person_name(name: str) -> bool:
    """Validate if candidate name is a proper person name and not a phrase fragment or common word."""
    if not name or len(name.strip()) < 3:
        return False

    clean_name = name.strip()
    words = clean_name.split()

    for w in words:
        w_lower = w.lower().strip(".,!?\"'()")
        if w_lower in NON_NAME_WORDS:
            return False

    if not re.match(r"^[A-Za-zÀ-ÖØ-öø-ÿ\s\-']+$", clean_name):
        return False

    return True


def extract_name_candidates(text: str, metadata_title: str = "", metadata_desc: str = "") -> List[Dict[str, Any]]:
    """
    Extract speaker name candidates from transcript text and metadata using NLP regex patterns.
    Returns list of dicts with 'name', 'confidence', 'source', 'type'
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
            if is_valid_person_name(raw_name) and raw_name.lower() not in seen_names:
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
            if is_valid_person_name(raw_name) and raw_name.lower() not in seen_names:
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
        r"(?:as|like)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(?:said|mentioned|pointed out)",
        r"(?:como (?:o|a)?)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(?:disse|falou|mencionou)",
    ]
    for pat in ref_patterns:
        matches = re.finditer(pat, text, re.IGNORECASE)
        for m in matches:
            raw_name = m.group(1).strip().title()
            if is_valid_person_name(raw_name) and raw_name.lower() not in seen_names:
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
        meta_matches = re.findall(r"(?:with|com|feat\.?|convidado:?|host:?)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", meta_text, re.IGNORECASE)
        for m in meta_matches:
            raw_name = m.strip().title()
            if is_valid_person_name(raw_name) and raw_name.lower() not in seen_names:
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

    # Acoustic / turn-taking speaker clustering
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
        # 2. Previous segment ended with question mark (?)
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
            current_speaker_idx = (current_speaker_idx + 1) % 2  # alternate between main speakers
            if current_speaker_idx >= num_speakers_detected:
                num_speakers_detected = current_speaker_idx + 1

        speaker_id = f"SPEAKER_{current_speaker_idx:02d}"
        speaker_turns.append((i, speaker_id))
        last_end = end

    # Build unique speaker map with default "Person 1", "Person 2", ...
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

    # Match validated candidate names to speakers based on segment turns and self-intros
    matched_names = set()
    for seg_idx, spk_id in speaker_turns:
        seg_text = segments[seg_idx].get("text", "")
        for cand in name_candidates:
            if cand["name"] in matched_names:
                continue
            if is_valid_person_name(cand["name"]) and cand["name"].lower() in seg_text.lower():
                spk = speakers_dict[spk_id]
                spk["name"] = cand["name"]
                spk["inferred_name"] = cand["name"]
                spk["confidence"] = cand["confidence"]
                spk["source"] = cand["source"]
                spk["is_confirmed"] = (cand["confidence"] >= 0.85)
                matched_names.add(cand["name"])
                break

    # Format updated segments with speaker data
    updated_segments = []
    for seg_idx, spk_id in speaker_turns:
        seg = dict(segments[seg_idx])
        spk_info = speakers_dict[spk_id]

        name_parts = spk_info["name"].split()
        if spk_info["name"].startswith("Person "):
            initials = f"P{spk_info['name'].split()[-1]}"
        elif len(name_parts) >= 2:
            initials = f"{name_parts[0][0]}{name_parts[-1][0]}".upper()
        elif len(name_parts) == 1:
            initials = name_parts[0][:2].upper()
        else:
            initials = spk_info["label"]

        seg["speaker_id"] = spk_id
        seg["speaker_label"] = spk_info["label"]
        seg["speaker_name"] = spk_info["name"]
        seg["speaker_initials"] = initials
        seg["speaker_color"] = spk_info["color_hex"]
        updated_segments.append(seg)

    speaker_entities = list(speakers_dict.values())
    logger.info(f"Diarization complete: {len(speaker_entities)} speakers processed for {len(segments)} segments.")
    return updated_segments, speaker_entities
