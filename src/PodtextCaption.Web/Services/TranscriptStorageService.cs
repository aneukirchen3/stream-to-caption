using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Services;

public interface ITranscriptStorageService
{
    Task<string> SaveTranscriptAsync(string podcastId, TranscriptDto transcript);
    Task<TranscriptDto?> LoadTranscriptAsync(string transcriptPath);
    bool DeleteTranscript(string transcriptPath);
}

public class TranscriptStorageService : ITranscriptStorageService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranscriptStorageService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public TranscriptStorageService(IConfiguration configuration, ILogger<TranscriptStorageService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SaveTranscriptAsync(string podcastId, TranscriptDto transcript)
    {
        string dir = _configuration["Storage:TranscriptPath"] ?? "data/transcripts";
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, $"{podcastId}.json");
        string json = JsonSerializer.Serialize(transcript, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Saved transcript JSON to {Path}", filePath);
        return filePath;
    }

    public async Task<TranscriptDto?> LoadTranscriptAsync(string transcriptPath)
    {
        if (!File.Exists(transcriptPath))
        {
            _logger.LogWarning("Transcript file not found: {Path}", transcriptPath);
            return null;
        }

        string json = await File.ReadAllTextAsync(transcriptPath);
        return JsonSerializer.Deserialize<TranscriptDto>(json);
    }

    public bool DeleteTranscript(string transcriptPath)
    {
        if (File.Exists(transcriptPath))
        {
            try
            {
                File.Delete(transcriptPath);
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to delete transcript file {Path}", transcriptPath);
            }
        }
        return false;
    }
}
