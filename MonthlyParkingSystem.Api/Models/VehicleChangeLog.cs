namespace MonthlyParkingSystem.Api.Models;

public sealed class VehicleChangeLog
{
    public long VehicleChangeLogId { get; set; }
    public int SchoolId { get; set; }
    public long ContractId { get; set; }
    public int VehicleId { get; set; }
    public int StudentId { get; set; }
    public string OldLicensePlate { get; set; } = string.Empty;
    public string NewLicensePlate { get; set; } = string.Empty;
    public string? ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? ChangeNote { get; set; }
    public ParkingContract Contract { get; set; } = null!;
}
