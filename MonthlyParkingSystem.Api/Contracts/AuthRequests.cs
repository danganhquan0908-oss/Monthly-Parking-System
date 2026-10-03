using System.ComponentModel.DataAnnotations;

namespace MonthlyParkingSystem.Api.Contracts;

public sealed class LoginRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{3,32}$")] public string SchoolCode { get; init; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,64}$")] public string Username { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = string.Empty;
}

public sealed class BootstrapAdminRequest
{
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,64}$")] public string Username { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = string.Empty;
}

public sealed class CreateStaffRequest
{
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,64}$")] public string Username { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = string.Empty;
    [Required, RegularExpression("^(Manager|Guard|Staff)$")] public string Role { get; init; } = string.Empty;
}

public sealed class SetStaffActiveRequest
{
    public bool IsActive { get; init; }
}

public sealed class CreateStaffInvitationRequest
{
    [Required, RegularExpression("^(Manager|Guard|Staff)$")] public string Role { get; init; } = string.Empty;
}

public sealed class AcceptStaffInvitationRequest
{
    [Required, StringLength(128, MinimumLength = 40)] public string Token { get; init; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,64}$")] public string Username { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = string.Empty;
}
