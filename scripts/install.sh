#!/usr/bin/env bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

echo "=========================================="
echo " PodtextCaption - Installation Script"
echo "=========================================="

cd "$ROOT_DIR"

# 1. Create data and tools directories
echo "[1/4] Creating project directories..."
mkdir -p data/audio data/transcripts data/database tools/ffmpeg tools/yt-dlp

# 2. Setup Python virtual environment
echo "[2/4] Setting up Python virtual environment..."
if [ ! -d "transcription/venv" ]; then
    python3 -m venv transcription/venv
fi

source transcription/venv/bin/activate
pip install --upgrade pip
pip install -r transcription/requirements.txt

# Option: install yt-dlp via pip if executable is not present
pip install yt-dlp || true

# 3. Check / Download FFmpeg binary if needed
echo "[3/4] Checking FFmpeg installation..."
if command -v ffmpeg &> /dev/null; then
    echo "FFmpeg found in system PATH."
else
    echo "FFmpeg not found in PATH. Checking static binaries in tools/ffmpeg..."
    if [ ! -f "tools/ffmpeg/ffmpeg" ]; then
        echo "Downloading static FFmpeg build..."
        mkdir -p tools/ffmpeg
        curl -sL https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz | tar -xJ --strip-components=1 -C tools/ffmpeg/ || true
    fi
    if [ -f "tools/ffmpeg/ffmpeg" ]; then
        chmod +x tools/ffmpeg/ffmpeg
        echo "FFmpeg installed to tools/ffmpeg/ffmpeg."
    else
        echo "Warning: Static FFmpeg download skipped. Make sure FFmpeg is installed."
    fi
fi

# 4. Build .NET Solution
echo "[4/4] Building .NET solution..."
dotnet build src/PodtextCaption.Web/PodtextCaption.Web.csproj

echo ""
echo "=========================================="
echo " Setup complete! Run ./scripts/start.sh to launch."
echo "=========================================="
