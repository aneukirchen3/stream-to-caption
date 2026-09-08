using System;
using System.ComponentModel.DataAnnotations;

namespace PodtextCaption.Web.Models;

public class Speaker
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string PodcastId { get; set; } = string.Empty;

    [Required]
    public string SpeakerId { get; set; } = string.Empty; // e.g. SPEAKER_00

    [Required]
    public string Label { get; set; } = string.Empty; // e.g. P1

    [Required]
    public string Name { get; set; } = string.Empty; // e.g. John Doe or Person 1

    public string? InferredName { get; set; }

    public double Confidence { get; set; } = 0.5;

    public string Source { get; set; } = "Fallback"; // Introduction, Metadata, Manual, Fallback

    public bool IsConfirmed { get; set; } = false;

    public string ColorHex { get; set; } = "#4f46e5";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
