using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Models;
using PodtextCaption.Web.Services;

namespace PodtextCaption.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PodcastController : ControllerBase
{
    private readonly IPodcastService _podcastService;
    private readonly IPodcastJobService _jobService;
    private readonly ITranscriptStorageService _transcriptStorage;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(
        IPodcastService podcastService,
        IPodcastJobService jobService,
        ITranscriptStorageService transcriptStorage,
        ILogger<PodcastController> logger)
    {
        _podcastService = podcastService;
        _jobService = jobService;
        _transcriptStorage = transcriptStorage;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var podcasts = await _podcastService.GetAllPodcastsAsync();
        return Ok(podcasts);
    }

    public class TranscribeRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Language { get; set; } = "auto";
        public string? Model { get; set; } = "small";
    }

    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeUrl([FromBody] TranscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "URL is required." });
        }

        try
        {
            var podcast = await _podcastService.CreateFromUrlAsync(request.Url, request.Title, request.Language, request.Model);
            return Ok(podcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating transcription for URL {Url}", request.Url);
            return StatusCode(500, new { error = $"Failed to start transcription: {ex.Message}" });
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(
        [FromForm] IFormFile file,
        [FromForm] string? title,
        [FromForm] string? language = "auto",
        [FromForm] string? model = "small")
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "Please provide a valid non-empty audio file." });
        }

        try
        {
            using var stream = file.OpenReadStream();
            var podcast = await _podcastService.CreateFromFileAsync(stream, file.FileName, title, language, model);
            return Ok(podcast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading audio file {FileName}", file.FileName);
            return StatusCode(500, new { error = $"Failed to process audio upload: {ex.Message}" });
        }
    }

    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(string id)
    {
        var job = await _jobService.GetJobAsync(id);
        if (job == null) return NotFound(new { error = "Job not found." });
        return Ok(job);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPodcast(string id)
    {
        var podcast = await _podcastService.GetPodcastByIdAsync(id);
        if (podcast == null) return NotFound(new { error = "Podcast not found." });
        return Ok(podcast);
    }

    [HttpGet("{id}/transcript")]
    public async Task<IActionResult> GetTranscript(string id)
    {
        var podcast = await _podcastService.GetPodcastByIdAsync(id);
        if (podcast == null) return NotFound(new { error = "Podcast not found." });

        if (string.IsNullOrEmpty(podcast.TranscriptPath) || !System.IO.File.Exists(podcast.TranscriptPath))
        {
            return NotFound(new { error = "Transcript is not ready or does not exist." });
        }

        var transcript = await _transcriptStorage.LoadTranscriptAsync(podcast.TranscriptPath);
        if (transcript == null) return NotFound(new { error = "Transcript file corrupted or empty." });

        return Ok(transcript);
    }

    [HttpGet("{id}/audio")]
    public async Task<IActionResult> StreamAudio(string id)
    {
        var podcast = await _podcastService.GetPodcastByIdAsync(id);
        if (podcast == null) return NotFound("Podcast not found.");

        string audioPath = podcast.AudioPath;
        if (string.IsNullOrEmpty(audioPath) || !System.IO.File.Exists(audioPath))
        {
            if (!string.IsNullOrEmpty(podcast.NormalizedAudioPath) && System.IO.File.Exists(podcast.NormalizedAudioPath))
            {
                audioPath = podcast.NormalizedAudioPath;
            }
            else
            {
                return NotFound("Audio file not found on disk.");
            }
        }

        string ext = Path.GetExtension(audioPath).ToLowerInvariant();
        string contentType = ext switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            _ => "audio/mpeg"
        };

        // PhysicalFile supports HTTP 206 Partial Content automatically for range requests
        return PhysicalFile(Path.GetFullPath(audioPath), contentType, enableRangeProcessing: true);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        bool success = await _podcastService.DeletePodcastAsync(id);
        if (!success) return NotFound(new { error = "Podcast not found." });
        return Ok(new { success = true, message = "Podcast deleted successfully." });
    }
}
