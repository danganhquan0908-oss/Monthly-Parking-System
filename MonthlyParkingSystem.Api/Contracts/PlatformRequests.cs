using System.ComponentModel.DataAnnotations;

namespace MonthlyParkingSystem.Api.Contracts;

public sealed class CreateSchoolRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{3,32}$")] public string Code { get; init; } = string.Empty;
    [Required, StringLength(150, MinimumLength = 2)] public string Name { get; init; } = string.Empty;
}

public sealed record CreateSchoolResponse(int SchoolId, string Code, string Name, string ManagerInviteToken, DateTime InviteExpiresAtUtc);
public sealed record SchoolSummary(int SchoolId, string Code, string Name, bool IsActive, DateTime CreatedAtUtc);

public sealed class SetSchoolActiveRequest
{
    public bool IsActive { get; init; }
}
