namespace MonthlyParkingSystem.Api.Models;

public static class ContractStatuses
{
    public const string Active = "Active";
    public const string Pending = "Pending";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

public sealed class ParkingContract
{
    public long ContractId { get; set; }
    public int SchoolId { get; set; }
    public int StudentId { get; set; }
    public int VehicleId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = ContractStatuses.Active;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Student Student { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public ICollection<VehicleChangeLog> VehicleChangeLogs { get; set; } = new List<VehicleChangeLog>();
}
