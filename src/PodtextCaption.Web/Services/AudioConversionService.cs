using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PodtextCaption.Web.Services;

public interface IAudioConversionService
{
    Task<string> NormalizeAudioAsync(string inputFilePath, string destinationDir, string fileBaseName, CancellationToken cancellationToken = default);
    Task<bool> IsFFmpegAvailableAsync();
}

public class AudioConversionService : IAudioConversionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AudioConversionService> _logger;

    public AudioConversionService(IConfiguration configuration, ILogger<AudioConversionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsFFmpegAvailableAsync()
    {
        string ffmpegPath = GetFFmpegPath();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
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

    public async Task<string> NormalizeAudioAsync(string inputFilePath, string destinationDir, string fileBaseName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException($"Input audio file not found: '{inputFilePath}'");

        Directory.CreateDirectory(destinationDir);

        string ffmpegPath = GetFFmpegPath();
        bool hasFFmpeg = await IsFFmpegAvailableAsync();

        string normalizedPath = Path.Combine(destinationDir, $"{fileBaseName}_normalized.wav");

        if (!hasFFmpeg)
        {
            _logger.LogWarning("FFmpeg is not available in system/tools. Copying input audio file as-is to normalized destination.");
            File.Copy(inputFilePath, normalizedPath, overwrite: true);
            return normalizedPath;
        }

        _logger.LogInformation("Converting/normalizing audio to 16kHz mono WAV using FFmpeg: {Input} -> {Output}", inputFilePath, normalizedPath);

        // FFmpeg command: convert to 16kHz mono 16-bit PCM WAV
        // -y: overwrite output
        // -i: input
        // -ar 16000: 16kHz sample rate
        // -ac 1: 1 channel (mono)
        // -c:a pcm_s16le: 16-bit PCM
        string arguments = $"-y -i \"{inputFilePath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{normalizedPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start FFmpeg process.");

        string stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg conversion failed with exit code {Code}: {Err}", process.ExitCode, stdErr);
            throw new Exception($"FFmpeg audio conversion failed: {stdErr}");
        }

        _logger.LogInformation("Audio normalization completed successfully: {Path}", normalizedPath);
        return normalizedPath;
    }

    private string GetFFmpegPath()
    {
        string configured = _configuration["Tools:FFmpegPath"] ?? "../../tools/ffmpeg/ffmpeg";
        if (File.Exists(configured)) return Path.GetFullPath(configured);

        string altPath1 = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../tools/ffmpeg/ffmpeg"));
        if (File.Exists(altPath1)) return altPath1;

        string altPath2 = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools/ffmpeg/ffmpeg"));
        if (File.Exists(altPath2)) return altPath2;

        return "ffmpeg"; // fallback to system PATH
    }
}
