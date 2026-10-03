namespace MonthlyParkingSystem.Api.Contracts;

public sealed record DashboardStatsResponse(int ActiveCount, int ExpiringSoonCount, int ExpiredCount);

public sealed record DashboardContractResponse(
    long ContractId,
    string StudentCode,
    string FullName,
    string RoomNumber,
    string LicensePlate,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record PagedDashboardResponse(
    IReadOnlyList<DashboardContractResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    DashboardStatsResponse Stats);
