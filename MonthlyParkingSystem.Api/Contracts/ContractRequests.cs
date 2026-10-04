using System.ComponentModel.DataAnnotations;

namespace MonthlyParkingSystem.Api.Contracts;

public sealed class RegisterContractRequest
{
    [Required, StringLength(32, MinimumLength = 1)] public string StudentCode { get; init; } = string.Empty;
    [Required, StringLength(150, MinimumLength = 1)] public string FullName { get; init; } = string.Empty;
    [Required, StringLength(30, MinimumLength = 1)] public string RoomNumber { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(320)] public string EmailAddress { get; init; } = string.Empty;
    [StringLength(40)] public string? PhoneNumber { get; init; }
    [Required, StringLength(20, MinimumLength = 1)] public string LicensePlate { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

public sealed class UpdateContractRequest
{
    [Required, StringLength(150, MinimumLength = 1)] public string FullName { get; init; } = string.Empty;
    [Required, StringLength(30, MinimumLength = 1)] public string RoomNumber { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(320)] public string EmailAddress { get; init; } = string.Empty;
    [StringLength(40)] public string? PhoneNumber { get; init; }
    [Required, StringLength(20, MinimumLength = 1)] public string LicensePlate { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

public sealed class ChangeVehicleRequest
{
    [Required, StringLength(20, MinimumLength = 1)] public string LicensePlate { get; init; } = string.Empty;
    [StringLength(100)] public string? ChangedBy { get; init; }
    [StringLength(500)] public string? ChangeNote { get; init; }
}

public sealed class CancelContractRequest
{
    [StringLength(500)] public string? CancellationNote { get; init; }
}
