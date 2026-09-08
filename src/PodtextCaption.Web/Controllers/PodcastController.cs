using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Models;
using PodtextCaption.Web.Services;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using PodtextCaption.Web.Data;

namespace PodtextCaption.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PodcastController : ControllerBase
{
    private readonly IPodcastService _podcastService;
    private readonly IPodcastJobService _jobService;
    private readonly ITranscriptStorageService _transcriptStorage;
    private readonly AppDbContext _db;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(
        IPodcastService podcastService,
        IPodcastJobService jobService,
        ITranscriptStorageService transcriptStorage,
        AppDbContext db,
        ILogger<PodcastController> logger)
    {
        _podcastService = podcastService;
        _jobService = jobService;
        _transcriptStorage = transcriptStorage;
        _db = db;
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

        string? resolvedTranscriptPath = ResolvePhysicalPath(podcast.TranscriptPath, "transcripts");
        if (resolvedTranscriptPath == null)
        {
            return NotFound(new { error = "Transcript is not ready or does not exist." });
        }

        var transcript = await _transcriptStorage.LoadTranscriptAsync(resolvedTranscriptPath);
        if (transcript == null) return NotFound(new { error = "Transcript file corrupted or empty." });

        return Ok(transcript);
    }

    [HttpGet("{id}/audio")]
    public async Task<IActionResult> StreamAudio(string id)
    {
        var podcast = await _podcastService.GetPodcastByIdAsync(id);
        if (podcast == null) return NotFound("Podcast not found.");

        string? resolvedAudioPath = ResolvePhysicalPath(podcast.AudioPath, "audio")
            ?? ResolvePhysicalPath(podcast.NormalizedAudioPath, "audio");

        if (resolvedAudioPath == null)
        {
            return NotFound("Audio file not found on disk.");
        }

        string ext = Path.GetExtension(resolvedAudioPath).ToLowerInvariant();
        string contentType = ext switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            _ => "audio/mpeg"
        };

        return PhysicalFile(resolvedAudioPath, contentType, enableRangeProcessing: true);
    }

    private static string? ResolvePhysicalPath(string? path, string defaultSubDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (System.IO.File.Exists(path)) return Path.GetFullPath(path);

        string fileName = Path.GetFileName(path);
        string currentDir = Directory.GetCurrentDirectory();
        string baseDir = AppContext.BaseDirectory;

        string candidate1 = Path.Combine(currentDir, "data", defaultSubDir, fileName);
        string candidate2 = Path.Combine(baseDir, "data", defaultSubDir, fileName);
        string candidate3 = Path.GetFullPath(Path.Combine(currentDir, "../../data", defaultSubDir, fileName));

        if (System.IO.File.Exists(candidate1)) return candidate1;
        if (System.IO.File.Exists(candidate2)) return candidate2;
        if (System.IO.File.Exists(candidate3)) return candidate3;

        return null;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        bool success = await _podcastService.DeletePodcastAsync(id);
        if (!success) return NotFound(new { error = "Podcast not found." });
        return Ok(new { success = true, message = "Podcast deleted successfully." });
    }

    [HttpGet("{id}/speakers")]
    public async Task<IActionResult> GetSpeakers(string id)
    {
        var speakers = await _db.Speakers
            .Where(s => s.PodcastId == id)
            .OrderBy(s => s.SpeakerId)
            .ToListAsync();
        return Ok(speakers);
    }

    public class RenameSpeakerRequest
    {
        public string NewName { get; set; } = string.Empty;
    }

    [HttpPut("{id}/speakers/{speakerId}")]
    public async Task<IActionResult> RenameSpeaker(string id, string speakerId, [FromBody] RenameSpeakerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest(new { error = "New name is required." });

        var speaker = await _db.Speakers.FirstOrDefaultAsync(s => s.PodcastId == id && s.SpeakerId == speakerId);
        if (speaker == null) return NotFound(new { error = "Speaker not found." });

        speaker.Name = request.NewName.Trim();
        speaker.IsConfirmed = true;
        speaker.Source = "Manual";
        await _db.SaveChangesAsync();

        // Also update transcript JSON file
        var podcast = await _podcastService.GetPodcastByIdAsync(id);
        if (podcast != null && !string.IsNullOrWhiteSpace(podcast.TranscriptPath))
        {
            string? resolvedPath = ResolvePhysicalPath(podcast.TranscriptPath, "transcripts");
            if (resolvedPath != null && System.IO.File.Exists(resolvedPath))
            {
                var transcript = await _transcriptStorage.LoadTranscriptAsync(resolvedPath);
                if (transcript != null)
                {
                    // Update speaker list in transcript
                    var spkDto = transcript.Speakers?.FirstOrDefault(s => s.SpeakerId == speakerId);
                    if (spkDto != null)
                    {
                        spkDto.Name = speaker.Name;
                        spkDto.IsConfirmed = true;
                        spkDto.Source = "Manual";
                    }

                    // Update segments
                    string[] parts = speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[^1][0]}".ToUpper() : speaker.Label;

                    foreach (var seg in transcript.Segments)
                    {
                        if (seg.SpeakerId == speakerId)
                        {
                            seg.SpeakerName = speaker.Name;
                            seg.SpeakerInitials = initials;
                        }
                    }

                    await _transcriptStorage.SaveTranscriptAsync(id, transcript);
                }
            }
        }

        return Ok(speaker);
    }
}
