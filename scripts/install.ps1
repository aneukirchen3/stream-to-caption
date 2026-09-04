# Windows PowerShell Installation Script for PodtextCaption
$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path "$PSScriptRoot\.."

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " PodtextCaption - Installation Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

Set-Location $RootDir

# 1. Directories
Write-Host "[1/4] Creating project directories..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "data/audio", "data/transcripts", "data/database", "tools/ffmpeg", "tools/yt-dlp" | Out-Null

# 2. Python Virtual Environment
Write-Host "[2/4] Setting up Python virtual environment..." -ForegroundColor Yellow
if (-not (Test-Path "transcription/venv")) {
    python -m venv transcription/venv
}

& "transcription/venv/Scripts/Activate.ps1"
python -m pip install --upgrade pip
python -m pip install -r transcription/requirements.txt
python -m pip install yt-dlp

# 3. .NET Build
Write-Host "[3/4] Building .NET solution..." -ForegroundColor Yellow
dotnet build src/PodtextCaption.Web/PodtextCaption.Web.csproj

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host " Setup complete! Run .\scripts\start.ps1 to launch." -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
