using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PodtextCaption.Web.Services;

namespace PodtextCaption.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAudioConversionService _audioConversionService;
    private readonly IAudioDownloadService _audioDownloadService;

    public HealthController(
        ITranscriptionService transcriptionService,
        IAudioConversionService audioConversionService,
        IAudioDownloadService audioDownloadService)
    {
        _transcriptionService = transcriptionService;
        _audioConversionService = audioConversionService;
        _audioDownloadService = audioDownloadService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealth()
    {
        bool transcriberOnline = await _transcriptionService.IsServiceAvailableAsync();
        bool ffmpegAvailable = await _audioConversionService.IsFFmpegAvailableAsync();
        bool ytDlpAvailable = await _audioDownloadService.IsYtDlpAvailableAsync();

        var status = new
        {
            status = "ok",
            webApp = "online",
            transcriber = transcriberOnline ? "online" : "offline",
            ffmpeg = ffmpegAvailable ? "available" : "missing",
            ytDlp = ytDlpAvailable ? "available" : "missing"
        };

        return Ok(status);
    }
}
