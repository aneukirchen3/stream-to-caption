using System;

namespace PodtextCaption.Web.Models;

public class Podcast
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Title { get; set; }
    public string? Url { get; set; }
    public double Duration { get; set; }
    public string? Language { get; set; }
    public string AudioPath { get; set; } = string.Empty;
    public string? NormalizedAudioPath { get; set; }
    public string TranscriptPath { get; set; } = string.Empty;
    public string Status { get; set; } = PodcastStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class PodcastStatus
{
    public const string Pending = "Pending";
    public const string Downloading = "Downloading";
    public const string Converting = "Converting";
    public const string Transcribing = "Transcribing";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
