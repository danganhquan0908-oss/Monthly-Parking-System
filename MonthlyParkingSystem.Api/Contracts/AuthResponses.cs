namespace MonthlyParkingSystem.Api.Contracts;

public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string Username, string Role, string SchoolCode, string SchoolName);
public sealed record StaffUserResponse(int StaffUserId, string Username, string Role, bool IsActive, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);
public sealed record StaffInvitationSummary(Guid InvitationId, string Role, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, bool IsAccepted, bool IsRevoked, bool IsExpired);
public sealed record StaffInvitationIssued(string Token, string Role, DateTime ExpiresAtUtc);
public sealed record AccountAuditSummary(long AccountAuditLogId, string Action, int? ActorStaffUserId, string? TargetUsername, string? Details, DateTime CreatedAtUtc);
