namespace MonthlyParkingSystem.Api.Models;

public static class NotificationStatuses
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

public static class NotificationTypes
{
    public const string ExpiryReminder = "ExpiryReminder";
}

public static class NotificationChannels
{
    public const string Email = "Email";
    public const string Sms = "SMS";
    public const string Zalo = "Zalo";
}

public sealed class NotificationLog
{
    public long NotificationLogId { get; set; }
    public int SchoolId { get; set; }
    public long ContractId { get; set; }
    public int StudentId { get; set; }
    public string NotificationType { get; set; } = NotificationTypes.ExpiryReminder;
    public string Channel { get; set; } = NotificationChannels.Email;
    public string Status { get; set; } = NotificationStatuses.Pending;
    public DateOnly DueDate { get; set; }
    public byte AttemptCount { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? DestinationMasked { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public ParkingContract Contract { get; set; } = null!;
}
