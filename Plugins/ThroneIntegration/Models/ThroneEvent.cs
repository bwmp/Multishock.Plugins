namespace ThroneIntegration.Models;

public class ThroneEvent
{
    public string Id { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public OverlayInformation? OverlayInformation { get; set; }
}

public class OverlayInformation
{
    public string GifterUsername { get; set; } = "Anonymous";
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Unknown";
    public string? ItemImage { get; set; }
    public string? ItemName { get; set; }
    public double? Amount { get; set; }
}
