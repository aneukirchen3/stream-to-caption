using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PodtextCaption.Web.Models;

public class TranscriptDto
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("segments")]
    public List<SegmentDto> Segments { get; set; } = new();

    [JsonPropertyName("speakers")]
    public List<SpeakerDto> Speakers { get; set; } = new();
}

public class SegmentDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("words")]
    public List<WordDto> Words { get; set; } = new();

    [JsonPropertyName("speaker_id")]
    public string? SpeakerId { get; set; }

    [JsonPropertyName("speaker_label")]
    public string? SpeakerLabel { get; set; }

    [JsonPropertyName("speaker_name")]
    public string? SpeakerName { get; set; }

    [JsonPropertyName("speaker_initials")]
    public string? SpeakerInitials { get; set; }

    [JsonPropertyName("speaker_color")]
    public string? SpeakerColor { get; set; }
}

public class WordDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("probability")]
    public double? Probability { get; set; }
}

public class SpeakerDto
{
    [JsonPropertyName("speaker_id")]
    public string SpeakerId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("inferred_name")]
    public string? InferredName { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("is_confirmed")]
    public bool IsConfirmed { get; set; }

    [JsonPropertyName("color_hex")]
    public string ColorHex { get; set; } = "#4f46e5";
}
