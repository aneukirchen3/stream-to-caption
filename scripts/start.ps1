# Windows PowerShell Startup Script for PodtextCaption
$RootDir = Resolve-Path "$PSScriptRoot\.."
Set-Location $RootDir

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Starting PodtextCaption Services" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Start Python FastAPI service in background process
$PythonProcess = Start-Process -FilePath "$RootDir\transcription\venv\Scripts\python.exe" -ArgumentList "$RootDir\transcription\app.py" -PassThru -NoNewWindow

Start-Sleep -Seconds 2

Write-Host ""
Write-Host "------------------------------------------" -ForegroundColor Yellow
Write-Host " PodtextCaption" -ForegroundColor Green
Write-Host " ------------------------------------------" -ForegroundColor Yellow
Write-Host " Web UI:      http://localhost:5000" -ForegroundColor Cyan
Write-Host " Transcriber: http://localhost:5001" -ForegroundColor Cyan
Write-Host "------------------------------------------" -ForegroundColor Yellow
Write-Host ""

Set-Location "$RootDir\src\PodtextCaption.Web"
try {
    dotnet run --urls "http://localhost:5000"
} finally {
    if ($PythonProcess -and -not $PythonProcess.HasExited) {
        Stop-Process -Id $PythonProcess.Id -Force
    }
}
