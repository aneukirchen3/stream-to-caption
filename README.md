# PodtextCaption — Local Podcast Transcription & Synchronized Captioning

PodtextCaption is a local web application built with **ASP.NET Core (.NET 10)** and **Python (FastAPI + faster-whisper)** that transcribes podcast audio (local MP3s or direct MP3 URLs) with **word-level timestamps** and renders an interactive, synchronized captioning interface.

---

## 🚀 Key Features

- **100% Local & Private**: No cloud API keys or internet connection required for transcription.
- **Word-Level Timestamps**: Every word is timestamped (`start` and `end` in seconds).
- **Synchronized Audio & Transcript**:
  - Highlights currently spoken word (`.active` CSS highlight).
  - Highlights current sentence/segment with smooth opacity & backdrop.
  - Automatic smooth auto-scroll to keep active segment centered.
  - Click-to-seek: click any word or timestamp to jump audio position instantly.
  - Floating `[ Follow transcript ]` button when manually scrolling away.
  - Playback speed synchronization (0.50x to 2.00x).
- **Transcript Search**: Instant word/phrase search with match counter and jump-to-match.
- **Audio Acquisition**: Direct MP3/WAV/M4A/AAC/OGG/WEBM URLs, local file uploads, and yt-dlp fallback integration.
- **FFmpeg Normalization**: Converts input audio to 16kHz mono WAV for optimal Whisper accuracy while keeping original audio intact.

---

## 🏗️ Architecture Overview

```text
PodtextCaption/
├── PodtextCaption.slnx / PodtextCaption.sln
├── src/
│   └── PodtextCaption.Web/           # ASP.NET Core (.NET 10) Web App & REST API
│       ├── Controllers/              # API endpoints (/api/podcast, /api/health)
│       ├── Services/                 # Podcast, Job, Download, FFmpeg, Transcriber services
│       ├── Models/                   # Podcast, ProcessingJob, Transcript DTOs
│       ├── Data/                     # SQLite AppDbContext
│       ├── Pages/                    # Razor Pages (Index, Layout)
│       └── wwwroot/                  # CSS design system, JS sync (binary search)
├── transcription/
│   ├── app.py                        # FastAPI server wrapping faster-whisper with word timestamps
│   ├── requirements.txt              # Python dependencies (fastapi, uvicorn, faster-whisper)
│   └── README.md
├── tools/
│   ├── ffmpeg/                       # FFmpeg binary directory
│   └── yt-dlp/                       # yt-dlp binary directory
├── data/
│   ├── audio/                        # Original & normalized audio storage
│   ├── transcripts/                  # Timestamped transcript JSON files
│   └── database/                     # SQLite database file (podtext.db)
└── scripts/
    ├── install.sh / install.ps1      # Automated environment installer
    └── start.sh / start.ps1          # Service launcher
```

---

## 🛠️ System Requirements

- **Operating System**: Linux, macOS, or Windows 10/11
- **.NET SDK**: .NET 10 SDK
- **Python**: Python 3.10, 3.11, or 3.12
- **FFmpeg**: Bundled in `tools/ffmpeg/` or system PATH
- **RAM**: Minimum 4 GB (8 GB+ recommended)

---

## ⚙️ Installation & Setup

### Linux / macOS

1. Open your terminal in the project directory:
   ```bash
   cd PodtextCaption
   ```

2. Run the automated install script:
   ```bash
   ./scripts/install.sh
   ```

   This script will:
   - Create required directories (`data/`, `tools/`).
   - Create Python venv under `transcription/venv` and install `faster-whisper`, `fastapi`, `uvicorn`, `yt-dlp`.
   - Check and download static FFmpeg binaries if not found in PATH.
   - Restore and build the .NET 10 solution.

---

### Windows (PowerShell)

1. Open PowerShell as Administrator or standard user in the project folder:
   ```powershell
   cd PodtextCaption
   ```

2. Run the installation script:
   ```powershell
   .\scripts\install.ps1
   ```

---

## 🚀 Starting the Application

### Linux / macOS
```bash
./scripts/start.sh
```

### Windows (PowerShell)
```powershell
.\scripts\start.ps1
```

Once started, open your browser and navigate to:

- **Web UI**: `http://localhost:5000`
- **Transcription Service API**: `http://localhost:5001`

---

## 🖥️ How to Use

1. **Import Audio**:
   - Paste a direct MP3 URL (e.g. `https://example.com/episode.mp3`) OR
   - Click **Upload Local Audio File** tab to select an MP3/WAV/M4A file from your computer.
2. **Select Options**:
   - **Language**: Auto Detect, English, Portuguese, Spanish.
   - **Whisper Model**: `small` (recommended for balance of speed and accuracy), `tiny`, `base`, `medium`, or `large-v3`.
3. **Click Transcribe**:
   - The UI will display live progress (`Downloading` ➔ `Converting` ➔ `Transcribing` ➔ `Completed`).
4. **Interactive synchronized reading**:
   - Click **Play** or press **Spacebar**.
   - As audio plays, the current spoken word and sentence highlight automatically.
   - Click **any word** or timestamp to instantly seek audio to that exact moment.
   - Adjust playback speed (0.5x – 2.0x) as needed.
   - Search text in the transcript search bar to jump to matching phrases.

---

## ⚙️ Configuration & Customization

Configuration parameters can be modified in `src/PodtextCaption.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../../data/database/podtext.db"
  },
  "Transcription": {
    "BaseUrl": "http://localhost:5001",
    "Model": "small"
  },
  "Tools": {
    "FFmpegPath": "tools/ffmpeg/ffmpeg",
    "YtDlpPath": "tools/yt-dlp/yt-dlp"
  },
  "Storage": {
    "AudioPath": "../../data/audio",
    "TranscriptPath": "../../data/transcripts"
  }
}
```

---

## 🔍 Troubleshooting

- **Transcriber status is Offline**:
  - Verify Python virtualenv is set up and FastAPI is running on port 5001:
    ```bash
    source transcription/venv/bin/activate
    python3 transcription/app.py
    ```
  - Test `curl http://localhost:5001/health`.
- **FFmpeg missing warning**:
  - Ensure static FFmpeg is extracted in `tools/ffmpeg/ffmpeg` or installed via package manager (`sudo apt install ffmpeg`).
- **Audio Seeking or Scrubbing Issues**:
  - PodtextCaption uses HTTP 206 Partial Content range requests to allow instant scrubbing without reloading the audio file.

---

## 🎯 Verification & Walkthrough

A complete walkthrough of changes and verification steps is documented in [walkthrough.md](file:///home/aneuk/.gemini/antigravity/brain/aa725d6e-4e22-4c40-b69f-3ab289de55fd/walkthrough.md).
