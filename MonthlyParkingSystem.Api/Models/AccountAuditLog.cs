namespace MonthlyParkingSystem.Api.Models;

public sealed class AccountAuditLog
{
    public long AccountAuditLogId { get; set; }
    public int SchoolId { get; set; }
    public int? ActorStaffUserId { get; set; }
    public int? TargetStaffUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetUsername { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
