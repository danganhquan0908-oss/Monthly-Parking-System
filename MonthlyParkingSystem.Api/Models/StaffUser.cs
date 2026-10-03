namespace MonthlyParkingSystem.Api.Models;

public static class StaffRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Guard = "Guard";
    public const string Staff = "Staff";
    public const string ContractManagers = Admin + "," + Manager;
    public const string DashboardReaders = Admin + "," + Manager + "," + Guard + "," + Staff;
}

public sealed class StaffUser
{
    public int StaffUserId { get; set; }
    public int SchoolId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public bool EmailVerified { get; set; }
    public byte[]? PhoneNumberEncrypted { get; set; }
    public string Role { get; set; } = StaffRoles.Staff;
    public bool IsActive { get; set; } = true;
    public byte AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public School School { get; set; } = null!;
}
