namespace MonthlyParkingSystem.Api.Models;

public sealed class SchoolRegistrationChallenge
{
    public Guid RegistrationId { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public string NormalizedSchoolCode { get; set; } = string.Empty;
    public string SchoolName { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public string? EmailLookupHash { get; set; }
    public string? PhoneLookupHash { get; set; }
    public byte[]? PhoneNumberEncrypted { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string AdminPasswordHash { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public byte AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
