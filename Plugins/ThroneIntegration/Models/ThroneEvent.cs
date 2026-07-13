namespace ThroneIntegration.Models;

public class ThroneEvent
{
    public string Id { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string CartId { get; set; } = string.Empty;
    public string ContentId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ForeignPaymentId { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public long? CompletedAt { get; set; }
    public bool Removed { get; set; }
    public long? RemovedAt { get; set; }
    public MoneyTotal? Total { get; set; }
    public MoneyTotal? TotalUsd { get; set; }
    public DisplayData? DisplayData { get; set; }
    public OverlayInformation? OverlayInformation { get; set; }
}

public class DisplayData
{
    public string? CustomerUsername { get; set; }
    public string? CustomerMessage { get; set; }
    public string? CustomerImage { get; set; }
}

public class MoneyTotal
{
    public double SubTotal { get; set; }
    public double Extras { get; set; }
    public string Currency { get; set; } = string.Empty;
    public double Shipping { get; set; }
    public double Total { get; set; }
    public double Price { get; set; }
    public double Tax { get; set; }
    public double Fees { get; set; }
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
