using System.ComponentModel.DataAnnotations;

namespace MonthlyParkingSystem.Api.Contracts;

public sealed class StartSchoolRegistrationRequest
{
    [Required, RegularExpression("^[A-Za-z0-9-]{3,32}$")]
    public string SchoolCode { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string SchoolName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(320)]
    public string EmailAddress { get; init; } = string.Empty;

    [StringLength(24)]
    public string? PhoneNumber { get; init; }

    [Required, RegularExpression("^[A-Za-z0-9._-]{3,64}$")]
    public string Username { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}

public sealed class VerifySchoolRegistrationRequest
{
    [Required]
    public Guid RegistrationId { get; init; }

    [Required, RegularExpression("^\\d{6}$")]
    public string Code { get; init; } = string.Empty;
}

public sealed record SchoolRegistrationOtpResponse(Guid RegistrationId, DateTime ExpiresAtUtc);
public sealed record SchoolRegistrationCompleted(int SchoolId, string SchoolCode, string SchoolName, string Username);
