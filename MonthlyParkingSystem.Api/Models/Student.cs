namespace MonthlyParkingSystem.Api.Models;

public sealed class Student
{
    public int StudentId { get; set; }
    public int SchoolId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public byte[]? PhoneEncrypted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<ParkingContract> Contracts { get; set; } = new List<ParkingContract>();
}
