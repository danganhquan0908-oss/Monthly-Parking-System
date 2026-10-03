namespace MonthlyParkingSystem.Api.Models;

public sealed class AuditLog
{
    public long AuditLogId { get; set; }
    public int SchoolId { get; set; }
    public string EntityName { get; set; } = null!;
    public long EntityId { get; set; }
    public string Action { get; set; } = null!;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedBy { get; set; }
    public System.DateTime ChangedAtUtc { get; set; }
}
