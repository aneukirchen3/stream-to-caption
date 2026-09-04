using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Services;

public interface ITranscriptionService
{
    Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default);
    Task<TranscriptDto> TranscribeAsync(string audioFilePath, string language = "auto", string model = "small", CancellationToken cancellationToken = default);
}

public class TranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranscriptionService> _logger;

    public TranscriptionService(HttpClient httpClient, IConfiguration configuration, ILogger<TranscriptionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        string baseUrl = _configuration["Transcription:BaseUrl"] ?? "http://localhost:5001";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(30); // Transcription of long audio files can take minutes
    }

    public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TranscriptDto> TranscribeAsync(string audioFilePath, string language = "auto", string model = "small", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting transcription from Python service for '{File}' (language={Lang}, model={Model})", audioFilePath, language, model);

        var requestPayload = new
        {
            file = System.IO.Path.GetFullPath(audioFilePath),
            language = language,
            model = model
        };

        var response = await _httpClient.PostAsJsonAsync("transcribe", requestPayload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Transcription HTTP request failed with status {StatusCode}: {Error}", response.StatusCode, errContent);
            throw new Exception($"Transcription engine returned error ({response.StatusCode}): {errContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<TranscriptDto>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize transcript response from Python service.");
        }

        _logger.LogInformation("Received transcript with {Count} segments, duration {Duration}s", result.Segments.Count, result.Duration);
        return result;
    }
}
