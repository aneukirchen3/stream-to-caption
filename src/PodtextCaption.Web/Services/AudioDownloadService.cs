using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PodtextCaption.Web.Services;

public interface IAudioDownloadService
{
    Task<string> DownloadAudioAsync(string url, string destinationDir, string fileBaseName, CancellationToken cancellationToken = default);
    Task<bool> IsYtDlpAvailableAsync();
}

public class AudioDownloadService : IAudioDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AudioDownloadService> _logger;

    public AudioDownloadService(HttpClient httpClient, IConfiguration configuration, ILogger<AudioDownloadService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsYtDlpAvailableAsync()
    {
        string ytDlpPath = GetYtDlpPath();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> DownloadAudioAsync(string url, string destinationDir, string fileBaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty.", nameof(url));

        Directory.CreateDirectory(destinationDir);

        bool isDirectMediaUrl = IsDirectAudioUrl(url);
        if (isDirectMediaUrl)
        {
            return await DownloadDirectFileAsync(url, destinationDir, fileBaseName, cancellationToken);
        }

        // 1. Try extracting direct audio URL (e.g. .mp3, .m4a, transistor.fm) embedded in the HTML web page
        string? extractedAudioUrl = await TryExtractAudioUrlFromHtmlAsync(url, cancellationToken);
        if (!string.IsNullOrEmpty(extractedAudioUrl))
        {
            _logger.LogInformation("Successfully extracted direct audio URL from HTML page: {AudioUrl}", extractedAudioUrl);
            try
            {
                return await DownloadDirectFileAsync(extractedAudioUrl, destinationDir, fileBaseName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download extracted audio URL {Url}, falling back to yt-dlp...", extractedAudioUrl);
            }
        }

        // 2. Try yt-dlp with browser user-agent and impersonation flags if available
        bool hasYtDlp = await IsYtDlpAvailableAsync();
        if (hasYtDlp)
        {
            return await DownloadWithYtDlpAsync(url, destinationDir, fileBaseName, cancellationToken);
        }

        // 3. Fallback to direct HTTP download
        return await DownloadDirectFileAsync(url, destinationDir, fileBaseName, cancellationToken);
    }

    private bool IsDirectAudioUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            return ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".webm" or ".flac";
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> TryExtractAudioUrlFromHtmlAsync(string pageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            string html = await resp.Content.ReadAsStringAsync(cancellationToken);

            // Match og:audio meta tags
            var ogMatch = System.Text.RegularExpressions.Regex.Match(html, @"<meta\s+property=[""']og:audio(?::secure_url)?[""']\s+content=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ogMatch.Success && Uri.IsWellFormedUriString(ogMatch.Groups[1].Value, UriKind.Absolute))
            {
                return ogMatch.Groups[1].Value;
            }

            // Match audio or source src attributes
            var srcMatch = System.Text.RegularExpressions.Regex.Match(html, @"<(?:audio|source)[^>]+src=[""']([^""']+\.(?:mp3|m4a|wav|ogg|aac)[^""']*)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (srcMatch.Success && Uri.IsWellFormedUriString(srcMatch.Groups[1].Value, UriKind.Absolute))
            {
                return srcMatch.Groups[1].Value;
            }

            // Match any direct .mp3 / .m4a link in HTML text
            var mp3Match = System.Text.RegularExpressions.Regex.Match(html, @"https?://[^\s""'<>]+\.(?:mp3|m4a|wav|ogg|aac)(?:\?[^\s""'<>]*)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mp3Match.Success && Uri.IsWellFormedUriString(mp3Match.Value, UriKind.Absolute))
            {
                return mp3Match.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect HTML for audio link at {Url}", pageUrl);
        }

        return null;
    }

    private async Task<string> DownloadDirectFileAsync(string url, string destinationDir, string fileBaseName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading direct audio file from {Url}", url);
        
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        string ext = ".mp3";
        if (response.Content.Headers.ContentType?.MediaType != null)
        {
            string mediaType = response.Content.Headers.ContentType.MediaType.ToLowerInvariant();
            if (mediaType.Contains("wav")) ext = ".wav";
            else if (mediaType.Contains("m4a") || mediaType.Contains("mp4")) ext = ".m4a";
            else if (mediaType.Contains("ogg")) ext = ".ogg";
            else if (mediaType.Contains("aac")) ext = ".aac";
        }

        try
        {
            var uri = new Uri(url);
            string uriExt = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (!string.IsNullOrEmpty(uriExt) && uriExt.Length <= 5 && uriExt.Contains('.'))
            {
                ext = uriExt;
            }
        }
        catch { }

        string fileName = $"{fileBaseName}_orig{ext}";
        string filePath = Path.Combine(destinationDir, fileName);

        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        _logger.LogInformation("Direct audio file downloaded successfully to {Path}", filePath);
        return filePath;
    }

    private async Task<string> DownloadWithYtDlpAsync(string url, string destinationDir, string fileBaseName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading audio using yt-dlp from {Url}", url);
        string ytDlpPath = GetYtDlpPath();
        string? ffmpegDir = GetFFmpegDirectory();
        string outputTemplate = Path.Combine(destinationDir, $"{fileBaseName}_orig.%(ext)s");

        string ffmpegArg = !string.IsNullOrEmpty(ffmpegDir) ? $"--ffmpeg-location \"{ffmpegDir}\" " : "";

        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = $"--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\" --no-check-certificates {ffmpegArg}-x --audio-format mp3 -o \"{outputTemplate}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(ffmpegDir))
        {
            string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = ffmpegDir + Path.PathSeparator + existingPath;
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp process.");
        
        string stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("yt-dlp failed with exit code {Code}: {Err}", process.ExitCode, stdErr);
            throw new Exception($"Audio download/extraction failed for web page: {stdErr}");
        }

        string expectedPath = Path.Combine(destinationDir, $"{fileBaseName}_orig.mp3");
        if (File.Exists(expectedPath)) return expectedPath;

        // Find any file starting with fileBaseName_orig
        var matchingFiles = Directory.GetFiles(destinationDir, $"{fileBaseName}_orig.*");
        if (matchingFiles.Length > 0) return matchingFiles[0];

        throw new FileNotFoundException("Downloaded audio file was not found after execution.");
    }

    private string GetYtDlpPath()
    {
        string configured = _configuration["Tools:YtDlpPath"] ?? "../../tools/yt-dlp/yt-dlp";
        if (File.Exists(configured)) return Path.GetFullPath(configured);

        string[] candidatePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "transcription/venv/bin/yt-dlp"),
            Path.Combine(Directory.GetCurrentDirectory(), "../../transcription/venv/bin/yt-dlp"),
            Path.Combine(Directory.GetCurrentDirectory(), "../../tools/yt-dlp/yt-dlp"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools/yt-dlp/yt-dlp")
        };

        foreach (var candidate in candidatePaths)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        return "yt-dlp"; // fallback to system PATH
    }

    private string? GetFFmpegDirectory()
    {
        string configured = _configuration["Tools:FFmpegPath"] ?? "../../tools/ffmpeg/ffmpeg";
        if (File.Exists(configured)) return Path.GetDirectoryName(Path.GetFullPath(configured));

        string[] candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "../../tools/ffmpeg/ffmpeg"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools/ffmpeg/ffmpeg"),
            Path.Combine(Directory.GetCurrentDirectory(), "../../tools/ffmpeg"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools/ffmpeg")
        };

        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return Path.GetDirectoryName(full);
            if (Directory.Exists(full)) return full;
        }

        return null;
    }
}
