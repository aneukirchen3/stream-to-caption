using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Data;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Services;

public interface IPodcastService
{
    Task<List<Podcast>> GetAllPodcastsAsync();
    Task<Podcast?> GetPodcastByIdAsync(string id);
    Task<Podcast> CreateFromUrlAsync(string url, string? title = null, string? language = null, string? model = null);
    Task<Podcast> CreateFromFileAsync(Stream fileStream, string originalFileName, string? title = null, string? language = null, string? model = null);
    Task<bool> DeletePodcastAsync(string id);
}

public class PodcastService : IPodcastService
{
    private readonly AppDbContext _db;
    private readonly IPodcastJobService _jobService;
    private readonly ITranscriptStorageService _transcriptStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PodcastService> _logger;

    public PodcastService(
        AppDbContext db,
        IPodcastJobService jobService,
        ITranscriptStorageService transcriptStorage,
        IConfiguration configuration,
        ILogger<PodcastService> logger)
    {
        _db = db;
        _jobService = jobService;
        _transcriptStorage = transcriptStorage;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<Podcast>> GetAllPodcastsAsync()
    {
        return await _db.Podcasts.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<Podcast?> GetPodcastByIdAsync(string id)
    {
        return await _db.Podcasts.FindAsync(id);
    }

    public async Task<Podcast> CreateFromUrlAsync(string url, string? title = null, string? language = null, string? model = null)
    {
        string derivedTitle = title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(derivedTitle))
        {
            try
            {
                var uri = new Uri(url);
                derivedTitle = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(derivedTitle)) derivedTitle = uri.Host;
            }
            catch
            {
                derivedTitle = "Direct Audio Link";
            }
        }

        var podcast = new Podcast
        {
            Title = derivedTitle,
            Url = url,
            Status = PodcastStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Podcasts.Add(podcast);
        await _db.SaveChangesAsync();

        await _jobService.StartJobAsync(podcast.Id, language, model);
        return podcast;
    }

    public async Task<Podcast> CreateFromFileAsync(Stream fileStream, string originalFileName, string? title = null, string? language = null, string? model = null)
    {
        string audioDir = _configuration["Storage:AudioPath"] ?? "data/audio";
        Directory.CreateDirectory(audioDir);

        string podcastId = Guid.NewGuid().ToString("N");
        string ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";

        string localFileName = $"{podcastId}_orig{ext}";
        string localPath = Path.Combine(audioDir, localFileName);

        using (var destStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
        {
            await fileStream.CopyToAsync(destStream);
        }

        string derivedTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(originalFileName) : title;

        var podcast = new Podcast
        {
            Id = podcastId,
            Title = derivedTitle,
            Url = originalFileName,
            AudioPath = localPath,
            Status = PodcastStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Podcasts.Add(podcast);
        await _db.SaveChangesAsync();

        await _jobService.StartJobAsync(podcast.Id, language, model);
        return podcast;
    }

    public async Task<bool> DeletePodcastAsync(string id)
    {
        var podcast = await _db.Podcasts.FindAsync(id);
        if (podcast == null) return false;

        // Delete audio files
        if (!string.IsNullOrEmpty(podcast.AudioPath) && File.Exists(podcast.AudioPath))
        {
            try { File.Delete(podcast.AudioPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete audio file {Path}", podcast.AudioPath); }
        }

        if (!string.IsNullOrEmpty(podcast.NormalizedAudioPath) && File.Exists(podcast.NormalizedAudioPath))
        {
            try { File.Delete(podcast.NormalizedAudioPath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete normalized audio file {Path}", podcast.NormalizedAudioPath); }
        }

        // Delete transcript JSON
        if (!string.IsNullOrEmpty(podcast.TranscriptPath))
        {
            _transcriptStorage.DeleteTranscript(podcast.TranscriptPath);
        }

        // Delete related processing jobs
        var jobs = await _db.ProcessingJobs.Where(j => j.PodcastId == id).ToListAsync();
        _db.ProcessingJobs.RemoveRange(jobs);

        // Delete podcast record
        _db.Podcasts.Remove(podcast);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted podcast {Id} and associated storage files.", id);
        return true;
    }
}
