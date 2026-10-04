namespace MonthlyParkingSystem.Api.Contracts;

public sealed record ContractResponse(
    long ContractId,
    string StudentCode,
    string FullName,
    string RoomNumber,
    string LicensePlate,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? EmailAddress,
    string? PhoneNumber);

public sealed record VehicleChangeResponse(
    long VehicleChangeLogId,
    string OldLicensePlate,
    string NewLicensePlate,
    string? ChangedBy,
    string? ChangeNote,
    DateTime ChangedAtUtc);
