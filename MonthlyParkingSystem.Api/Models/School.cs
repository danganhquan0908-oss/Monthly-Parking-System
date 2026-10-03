namespace MonthlyParkingSystem.Api.Models;

public sealed class School
{
    public int SchoolId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NormalizedCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<StaffUser> StaffUsers { get; set; } = new List<StaffUser>();
}
