#!/usr/bin/env bash
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

cd "$ROOT_DIR"

# Export local tools (FFmpeg, ffprobe, yt-dlp) to PATH environment variable
export PATH="$ROOT_DIR/tools/ffmpeg:$ROOT_DIR/transcription/venv/bin:$PATH"

echo "=========================================="
echo " Starting PodtextCaption Services"
echo "=========================================="

# Clean up any previous background processes using ports 5000 & 5001
if command -v fuser &> /dev/null; then
    fuser -k 5000/tcp 5001/tcp 2>/dev/null || true
fi
pkill -f "transcription/app.py" 2>/dev/null || true
sleep 1

# Ensure cleanup on exit
cleanup() {
    echo ""
    echo "Stopping services..."
    kill $(jobs -p) 2>/dev/null || true
    exit 0
}
trap cleanup SIGINT SIGTERM EXIT

# Activate python venv
if [ -d "transcription/venv" ]; then
    source transcription/venv/bin/activate
fi

# Start Python FastAPI Transcription engine on 5001
echo "Starting Python Transcription Service on port 5001..."
python3 transcription/app.py &
TRANS_PID=$!

# Wait briefly for FastAPI to initialize
sleep 2

# Start ASP.NET Core Application on port 5000
echo "Starting ASP.NET Core Web Application on port 5000..."

echo ""
echo "------------------------------------------"
echo " PodtextCaption"
echo " ------------------------------------------"
echo " Web UI:      http://localhost:5000"
echo " Transcriber: http://localhost:5001"
echo "------------------------------------------"
echo ""

cd src/PodtextCaption.Web
dotnet run --urls "http://localhost:5000"
