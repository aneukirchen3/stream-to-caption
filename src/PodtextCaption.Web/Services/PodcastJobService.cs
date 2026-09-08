using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Data;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Services;

public interface IPodcastJobService
{
    Task<ProcessingJob> StartJobAsync(string podcastId, string? userLanguage = null, string? model = null);
    Task<ProcessingJob?> GetJobAsync(string jobId);
    Task ProcessPodcastPipelineAsync(string jobId, string podcastId, string? userLanguage, string? model, CancellationToken cancellationToken = default);
}

public class PodcastJobService : IPodcastJobService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PodcastJobService> _logger;

    public PodcastJobService(IServiceScopeFactory scopeFactory, ILogger<PodcastJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ProcessingJob> StartJobAsync(string podcastId, string? userLanguage = null, string? model = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = new ProcessingJob
        {
            PodcastId = podcastId,
            Status = PodcastStatus.Pending,
            Progress = 5,
            StatusMessage = "Job queued...",
            StartedAt = DateTime.UtcNow
        };

        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        // Queue pipeline execution asynchronously in background task
        _ = Task.Run(() => ProcessPodcastPipelineAsync(job.Id, podcastId, userLanguage, model));

        return job;
    }

    public async Task<ProcessingJob?> GetJobAsync(string jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessingJobs.FindAsync(jobId);
    }

    public async Task ProcessPodcastPipelineAsync(string jobId, string podcastId, string? userLanguage, string? model, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting pipeline for Job {JobId}, Podcast {PodcastId}", jobId, podcastId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var downloader = scope.ServiceProvider.GetRequiredService<IAudioDownloadService>();
        var converter = scope.ServiceProvider.GetRequiredService<IAudioConversionService>();
        var transcriber = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
        var storage = scope.ServiceProvider.GetRequiredService<ITranscriptStorageService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var podcast = await db.Podcasts.FindAsync(podcastId);
        var job = await db.ProcessingJobs.FindAsync(jobId);

        if (podcast == null || job == null)
        {
            _logger.LogError("Podcast or Job not found during pipeline execution: PodcastId={PodcastId}, JobId={JobId}", podcastId, jobId);
            return;
        }

        string audioDir = config["Storage:AudioPath"] ?? "data/audio";
        Directory.CreateDirectory(audioDir);

        try
        {
            // Step 1: Downloading
            await UpdateJobStatusAsync(db, job, podcast, PodcastStatus.Downloading, 15, "Downloading audio source...");

            string originalAudioPath;
            if (!string.IsNullOrWhiteSpace(podcast.Url) && (podcast.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || podcast.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                var downloadResult = await downloader.DownloadAudioAsync(podcast.Url, audioDir, podcast.Id, cancellationToken);
                originalAudioPath = downloadResult.FilePath;

                if (!string.IsNullOrWhiteSpace(downloadResult.MediaTitle))
                {
                    bool isGenericTitle = string.IsNullOrWhiteSpace(podcast.Title) ||
                        string.Equals(podcast.Title, "watch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(podcast.Title, "Vídeo do YouTube", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(podcast.Title, "Direct Audio Link", StringComparison.OrdinalIgnoreCase) ||
                        podcast.Title.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                        podcast.Title.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

                    if (isGenericTitle)
                    {
                        podcast.Title = downloadResult.MediaTitle;
                        _logger.LogInformation("Updated podcast {Id} title from media metadata: '{Title}'", podcast.Id, downloadResult.MediaTitle);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(podcast.AudioPath) && File.Exists(podcast.AudioPath))
            {
                originalAudioPath = podcast.AudioPath;
            }
            else
            {
                throw new FileNotFoundException($"Audio source file or URL not found for podcast {podcast.Id}.");
            }

            podcast.AudioPath = originalAudioPath;
            await db.SaveChangesAsync();

            // Step 2: Converting / Normalizing with FFmpeg
            await UpdateJobStatusAsync(db, job, podcast, PodcastStatus.Converting, 40, "Normalizing audio format with FFmpeg...");

            string normalizedPath = await converter.NormalizeAudioAsync(originalAudioPath, audioDir, podcast.Id, cancellationToken);
            podcast.NormalizedAudioPath = normalizedPath;
            await db.SaveChangesAsync();

            // Step 3: Transcribing with Python faster-whisper & Diarization
            await UpdateJobStatusAsync(db, job, podcast, PodcastStatus.Transcribing, 60, "Transcribing audio locally using Whisper...");

            string selectedModel = string.IsNullOrWhiteSpace(model) ? (config["Transcription:Model"] ?? "small") : model;
            string selectedLang = string.IsNullOrWhiteSpace(userLanguage) ? "auto" : userLanguage;

            var transcriptDto = await transcriber.TranscribeAsync(normalizedPath, selectedLang, selectedModel, podcast.Title, podcast.Title, cancellationToken);

            // Step 4: Identifying speakers & matching metadata
            await UpdateJobStatusAsync(db, job, podcast, PodcastStatus.IdentifyingSpeakers, 80, "Identifying speakers and mapping names...");

            if (transcriptDto.Speakers != null && transcriptDto.Speakers.Count > 0)
            {
                try
                {
                    // Clear existing speakers for this podcast if re-running
                    var existingSpeakers = db.Speakers.Where(s => s.PodcastId == podcast.Id);
                    db.Speakers.RemoveRange(existingSpeakers);

                    foreach (var spkDto in transcriptDto.Speakers)
                    {
                        db.Speakers.Add(new Speaker
                        {
                            PodcastId = podcast.Id,
                            SpeakerId = spkDto.SpeakerId,
                            Label = spkDto.Label,
                            Name = spkDto.Name,
                            InferredName = spkDto.InferredName,
                            Confidence = spkDto.Confidence,
                            Source = spkDto.Source,
                            IsConfirmed = spkDto.IsConfirmed,
                            ColorHex = spkDto.ColorHex,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Warning: Failed to save speakers to database for podcast {PodcastId}", podcast.Id);
                }
            }

            // Step 5: Saving transcript JSON
            await UpdateJobStatusAsync(db, job, podcast, PodcastStatus.Processing, 95, "Saving transcript data...");

            string transcriptPath = await storage.SaveTranscriptAsync(podcast.Id, transcriptDto);
            podcast.TranscriptPath = transcriptPath;
            podcast.Duration = transcriptDto.Duration;
            podcast.Language = transcriptDto.Language;

            // Step 6: Completed!
            podcast.Status = PodcastStatus.Completed;
            podcast.CompletedAt = DateTime.UtcNow;

            job.Status = PodcastStatus.Completed;
            job.Progress = 100;
            job.StatusMessage = "Transcription and speaker identification completed!";
            job.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("Successfully completed pipeline for Job {JobId}, Podcast {PodcastId}", jobId, podcastId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed for Job {JobId}, Podcast {PodcastId}", jobId, podcastId);

            podcast.Status = PodcastStatus.Failed;
            podcast.ErrorMessage = ex.Message;

            job.Status = PodcastStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.StatusMessage = $"Failed: {ex.Message}";
            job.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
    }

    private static async Task UpdateJobStatusAsync(AppDbContext db, ProcessingJob job, Podcast podcast, string status, int progress, string message)
    {
        job.Status = status;
        job.Progress = progress;
        job.StatusMessage = message;
        podcast.Status = status;
        await db.SaveChangesAsync();
    }
}
