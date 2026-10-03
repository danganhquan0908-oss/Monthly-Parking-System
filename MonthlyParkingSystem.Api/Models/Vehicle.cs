namespace MonthlyParkingSystem.Api.Models;

public sealed class Vehicle
{
    public int VehicleId { get; set; }
    public int SchoolId { get; set; }
    public int StudentId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Student Student { get; set; } = null!;
    public ICollection<ParkingContract> Contracts { get; set; } = new List<ParkingContract>();
}
