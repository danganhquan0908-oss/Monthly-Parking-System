namespace MonthlyParkingSystem.Api.Models;

public sealed class StaffInvitation
{
    public Guid StaffInvitationId { get; set; }
    public int SchoolId { get; set; }
    public string Role { get; set; } = StaffRoles.Staff;
    public string TokenHash { get; set; } = string.Empty;
    public int? CreatedByStaffUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public School School { get; set; } = null!;
}
