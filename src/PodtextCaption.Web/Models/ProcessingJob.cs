using System;

namespace PodtextCaption.Web.Models;

public class ProcessingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PodcastId { get; set; } = string.Empty;
    public string Status { get; set; } = PodcastStatus.Pending;
    public int Progress { get; set; } = 0; // 0 to 100
    public string? StatusMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
