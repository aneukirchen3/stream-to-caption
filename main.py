#!/usr/bin/env python3
"""
PodtextCaption - Official Service Runner
Orchestrates the Python Faster-Whisper transcription engine (port 5001)
and the ASP.NET Core web application & queue worker (default port 5003).

Designed for production execution under systemd on Ubuntu/Linux.
"""

import os
import sys
import signal
import time
import logging
import subprocess
from pathlib import Path

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] [PodtextService] %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S"
)
logger = logging.getLogger("podtext_service")

ROOT_DIR = Path(__file__).resolve().parent
PORT = os.environ.get("PORT", "5003")

# Locate Python binary (prefer project virtualenv if available)
VENV_PYTHON = ROOT_DIR / "transcription" / "venv" / "bin" / "python"
if VENV_PYTHON.exists():
    PYTHON_BIN = str(VENV_PYTHON)
else:
    PYTHON_BIN = sys.executable

# Add local tools (ffmpeg, yt-dlp) to PATH if present
FFMPEG_DIR = ROOT_DIR / "tools" / "ffmpeg"
VENV_BIN_DIR = ROOT_DIR / "transcription" / "venv" / "bin"
current_path = os.environ.get("PATH", "")
path_additions = []
if FFMPEG_DIR.exists():
    path_additions.append(str(FFMPEG_DIR))
if VENV_BIN_DIR.exists():
    path_additions.append(str(VENV_BIN_DIR))
if path_additions:
    os.environ["PATH"] = ":".join(path_additions) + ":" + current_path

# Global child process handles
proc_transcription = None
proc_dotnet = None
is_shutting_down = False


def cleanup_stale_ports():
    """Terminate any lingering processes on ports 5001 or target PORT before startup."""
    ports = ["5001", str(PORT)]
    for p in ports:
        try:
            # Check if port is in use and kill using fuser if available
            subprocess.run(["fuser", "-k", f"{p}/tcp"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        except Exception:
            pass


def shutdown_processes(signum=None, frame=None):
    """Gracefully terminate child processes on SIGINT or SIGTERM."""
    global is_shutting_down, proc_transcription, proc_dotnet
    if is_shutting_down:
        return
    is_shutting_down = True

    sig_name = "SIGTERM" if signum == signal.SIGTERM else "SIGINT"
    logger.info(f"Received {sig_name}. Initiating graceful shutdown of services...")

    # Terminate .NET first so it can record queue_status as 'Inativo'
    if proc_dotnet and proc_dotnet.poll() is None:
        logger.info("Sending termination signal to ASP.NET Core Web application...")
        try:
            proc_dotnet.send_signal(signal.SIGINT)
        except Exception as e:
            logger.warning(f"Error signaling .NET process: {e}")

    # Terminate Python Whisper transcription service
    if proc_transcription and proc_transcription.poll() is None:
        logger.info("Sending termination signal to Python Transcription Service...")
        try:
            proc_transcription.send_signal(signal.SIGINT)
        except Exception as e:
            logger.warning(f"Error signaling transcription service: {e}")

    # Wait up to 10 seconds for graceful shutdown
    deadline = time.time() + 10
    for name, proc in [("ASP.NET Core", proc_dotnet), ("Transcription Service", proc_transcription)]:
        if proc and proc.poll() is None:
            remaining = max(0.5, deadline - time.time())
            try:
                proc.wait(timeout=remaining)
                logger.info(f"{name} exited cleanly.")
            except subprocess.TimeoutExpired:
                logger.warning(f"{name} did not stop in time. Force killing...")
                proc.kill()

    logger.info("All PodtextCaption services stopped.")
    sys.exit(0)


def main():
    global proc_transcription, proc_dotnet

    # Register system signal handlers for graceful shutdown
    signal.signal(signal.SIGINT, shutdown_processes)
    signal.signal(signal.SIGTERM, shutdown_processes)

    logger.info("==========================================")
    logger.info(" PodTextCaption Official Service Starting")
    logger.info(f" Web UI / API Port: {PORT}")
    logger.info(f" Transcription Port: 5001")
    logger.info(f" Working Directory: {ROOT_DIR}")
    logger.info("==========================================")

    # Clean up any leftover processes on target ports
    cleanup_stale_ports()

    # 1. Start Python Transcription Service (port 5001)
    transcription_script = ROOT_DIR / "transcription" / "app.py"
    if not transcription_script.exists():
        logger.error(f"Transcription script not found at: {transcription_script}")
        sys.exit(1)

    logger.info("Starting Python Transcription Service on port 5001...")
    env_trans = os.environ.copy()
    proc_transcription = subprocess.Popen(
        [PYTHON_BIN, str(transcription_script)],
        cwd=str(ROOT_DIR),
        env=env_trans
    )

    # Brief delay to allow transcription engine to initialize
    time.sleep(2)

    # 2. Start ASP.NET Core Web Application (.NET 9)
    web_project = ROOT_DIR / "src" / "PodtextCaption.Web" / "PodtextCaption.Web.csproj"
    if not web_project.exists():
        logger.error(f"Web project file not found at: {web_project}")
        if proc_transcription and proc_transcription.poll() is None:
            proc_transcription.kill()
        sys.exit(1)

    logger.info(f"Starting ASP.NET Core Web App listening on http://0.0.0.0:{PORT}...")
    env_web = os.environ.copy()
    env_web["PORT"] = str(PORT)
    env_web["Features__EnableProcessingWorker"] = "true"
    env_web["Features__EnableProcessingInProduction"] = "true"

    dotnet_cmd = [
        "dotnet", "run",
        "--project", str(web_project),
        "--urls", f"http://0.0.0.0:{PORT}",
        "--no-build"
    ]

    proc_dotnet = subprocess.Popen(
        dotnet_cmd,
        cwd=str(web_project.parent),
        env=env_web
    )

    logger.info(f"PodTextCaption running. Access http://localhost:{PORT} (Web) and http://localhost:5001 (Whisper API)")

    # 3. Supervise processes
    while not is_shutting_down:
        time.sleep(1)

        # Check if transcription service crashed
        if proc_transcription.poll() is not None and not is_shutting_down:
            exit_code = proc_transcription.poll()
            logger.error(f"Transcription service exited unexpectedly with code {exit_code}.")
            shutdown_processes()
            sys.exit(exit_code or 1)

        # Check if dotnet app crashed
        if proc_dotnet.poll() is not None and not is_shutting_down:
            exit_code = proc_dotnet.poll()
            logger.error(f"ASP.NET Core Web application exited unexpectedly with code {exit_code}.")
            shutdown_processes()
            sys.exit(exit_code or 1)


if __name__ == "__main__":
    main()

