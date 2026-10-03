using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Contracts;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using MonthlyParkingSystem.Api.Services;

namespace MonthlyParkingSystem.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    MpsDbContext db,
    IPasswordHasher<StaffUser> passwordHasher,
    JwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedSchoolCode = NormalizeCode(request.SchoolCode);
        var school = await db.Schools.AsNoTracking().SingleOrDefaultAsync(
            item => item.NormalizedCode == normalizedSchoolCode && item.IsActive, cancellationToken);
        if (school is null) return Unauthorized(new { message = "Mã trường, tên đăng nhập hoặc mật khẩu không hợp lệ." });
        var normalizedUsername = NormalizeUsername(request.Username);
        var user = await db.StaffUsers.SingleOrDefaultAsync(item =>
            item.SchoolId == school.SchoolId && item.NormalizedUsername == normalizedUsername, cancellationToken);
        var now = DateTime.UtcNow;
        if (user is null || !user.IsActive || (user.LockoutEndUtc is not null && user.LockoutEndUtc > now))
            return Unauthorized(new { message = "Mã trường, tên đăng nhập hoặc mật khẩu không hợp lệ." });

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            if (user.LockoutEndUtc is not null && user.LockoutEndUtc <= now)
            {
                user.AccessFailedCount = 0;
                user.LockoutEndUtc = null;
            }
            user.AccessFailedCount = (byte)Math.Min(5, user.AccessFailedCount + 1);
            if (user.AccessFailedCount >= 5) user.LockoutEndUtc = now.AddMinutes(15);
            await db.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "Mã trường, tên đăng nhập hoặc mật khẩu không hợp lệ." });
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(CreateLoginResponse(user, school));
    }

    [AllowAnonymous]
    [HttpPost("accept-invitation")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> AcceptInvitation(
        AcceptStaffInvitationRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var tokenHash = InvitationTokenService.HashToken(request.Token);
        var invitation = await db.StaffInvitations.Include(item => item.School)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        var now = DateTime.UtcNow;
        if (invitation is null || invitation.AcceptedAtUtc is not null || invitation.RevokedAtUtc is not null)
            return BadRequest("Lời mời không hợp lệ hoặc đã được sử dụng.");
        if (invitation.ExpiresAtUtc <= now) return BadRequest("Lời mời đã hết hạn. Hãy yêu cầu gửi lời mời mới.");
        if (!invitation.School.IsActive) return BadRequest("Trường đã tạm ngưng hoạt động.");

        var username = request.Username.Trim();
        var user = CreateUser(invitation.SchoolId, username, request.Password, invitation.Role);
        user.LastLoginAtUtc = now;
        db.StaffUsers.Add(user);
        invitation.AcceptedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            AddAudit(invitation.SchoolId, user.StaffUserId, user.StaffUserId, "InvitationAccepted", user.Username, $"Role={user.Role}");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(CreateLoginResponse(user, invitation.School));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Tên đăng nhập đã được sử dụng tại trường này.");
        }
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpGet("staff")]
    [ProducesResponseType<IEnumerable<StaffUserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StaffUserResponse>>> GetStaff(CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var users = await db.StaffUsers.AsNoTracking().Where(item => item.SchoolId == schoolId)
            .OrderBy(item => item.Username)
            .Select(item => new StaffUserResponse(item.StaffUserId, item.Username, item.Role, item.IsActive,
                item.CreatedAtUtc, item.LastLoginAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpPost("staff")]
    [ProducesResponseType<StaffUserResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<StaffUserResponse>> CreateStaff(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        if (request.Role == StaffRoles.Manager && !User.IsInRole(StaffRoles.Admin)) return Forbid();
        var username = request.Username.Trim();
        var user = CreateUser(CurrentSchoolId(), username, request.Password, request.Role);
        db.StaffUsers.Add(user);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            AddAudit(user.SchoolId, CurrentStaffUserId(), user.StaffUserId, "StaffCreated", user.Username, $"Role={user.Role}");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var response = ToStaffResponse(user);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("Tên đăng nhập đã được sử dụng tại trường này.");
        }
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpGet("staff/invitations")]
    [ProducesResponseType<IEnumerable<StaffInvitationSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StaffInvitationSummary>>> GetInvitations(CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var now = DateTime.UtcNow;
        var invitations = await db.StaffInvitations.AsNoTracking().Where(item => item.SchoolId == schoolId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new StaffInvitationSummary(item.StaffInvitationId, item.Role, item.CreatedAtUtc,
                item.ExpiresAtUtc, item.AcceptedAtUtc != null, item.RevokedAtUtc != null, item.ExpiresAtUtc <= now))
            .ToListAsync(cancellationToken);
        return Ok(invitations);
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpPost("staff/invitations")]
    [ProducesResponseType<StaffInvitationIssued>(StatusCodes.Status201Created)]
    public async Task<ActionResult<StaffInvitationIssued>> CreateInvitation(
        CreateStaffInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Role == StaffRoles.Manager && !User.IsInRole(StaffRoles.Admin)) return Forbid();
        var schoolId = CurrentSchoolId();
        var token = InvitationTokenService.CreateToken();
        var invitation = new StaffInvitation
        {
            StaffInvitationId = Guid.NewGuid(),
            SchoolId = schoolId,
            Role = request.Role,
            TokenHash = InvitationTokenService.HashToken(token),
            CreatedByStaffUserId = CurrentStaffUserId(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3)
        };
        db.StaffInvitations.Add(invitation);
        AddAudit(schoolId, CurrentStaffUserId(), null, "StaffInvitationCreated", null, $"InvitationId={invitation.StaffInvitationId};Role={request.Role}");
        await db.SaveChangesAsync(cancellationToken);
        var response = new StaffInvitationIssued(token, invitation.Role, invitation.ExpiresAtUtc);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpDelete("staff/invitations/{id:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var invitation = await db.StaffInvitations.SingleOrDefaultAsync(
            item => item.StaffInvitationId == id && item.SchoolId == schoolId, cancellationToken);
        if (invitation is null) return NotFound();
        if (invitation.AcceptedAtUtc is not null) return Conflict("Lời mời đã được sử dụng.");
        invitation.RevokedAtUtc = DateTime.UtcNow;
        AddAudit(schoolId, CurrentStaffUserId(), null, "StaffInvitationRevoked", null, $"InvitationId={invitation.StaffInvitationId}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpPut("staff/{id:int}/active")]
    public async Task<IActionResult> SetStaffActive(int id, SetStaffActiveRequest request, CancellationToken cancellationToken)
    {
        if (!request.IsActive && CurrentStaffUserId() == id) return BadRequest("Bạn không thể vô hiệu hóa tài khoản của chính mình.");
        var schoolId = CurrentSchoolId();
        var user = await db.StaffUsers.SingleOrDefaultAsync(
            item => item.StaffUserId == id && item.SchoolId == schoolId, cancellationToken);
        if (user is null) return NotFound();
        if (!User.IsInRole(StaffRoles.Admin) && user.Role is StaffRoles.Admin or StaffRoles.Manager) return Forbid();
        user.IsActive = request.IsActive;
        user.LockoutEndUtc = request.IsActive ? null : DateTime.MaxValue;
        AddAudit(schoolId, CurrentStaffUserId(), user.StaffUserId, request.IsActive ? "StaffReactivated" : "StaffDeactivated", user.Username, null);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = StaffRoles.ContractManagers)]
    [HttpGet("account-audit")]
    [ProducesResponseType<IEnumerable<AccountAuditSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AccountAuditSummary>>> GetAccountAudit(CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var events = await db.AccountAuditLogs.AsNoTracking().Where(item => item.SchoolId == schoolId)
            .OrderByDescending(item => item.CreatedAtUtc).Take(200)
            .Select(item => new AccountAuditSummary(item.AccountAuditLogId, item.Action, item.ActorStaffUserId,
                item.TargetUsername, item.Details, item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(events);
    }

    private LoginResponse CreateLoginResponse(StaffUser user, School school)
    {
        var (token, expires) = tokens.CreateToken(user, school);
        return new LoginResponse(token, "Bearer", expires, user.Username, user.Role, school.Code, school.Name);
    }

    private StaffUser CreateUser(int schoolId, string username, string password, string role)
    {
        var user = new StaffUser
        {
            SchoolId = schoolId,
            Username = username,
            NormalizedUsername = NormalizeUsername(username),
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        return user;
    }

    private static StaffUserResponse ToStaffResponse(StaffUser user) =>
        new(user.StaffUserId, user.Username, user.Role, user.IsActive, user.CreatedAtUtc, user.LastLoginAtUtc);

    private void AddAudit(int schoolId, int? actorId, int? targetId, string action, string? targetUsername, string? details) =>
        db.AccountAuditLogs.Add(new AccountAuditLog
        {
            SchoolId = schoolId,
            ActorStaffUserId = actorId,
            TargetStaffUserId = targetId,
            Action = action,
            TargetUsername = targetUsername,
            Details = details,
            CreatedAtUtc = DateTime.UtcNow
        });

    private int CurrentSchoolId() => int.Parse(User.FindFirst("school_id")!.Value);
    private int CurrentStaffUserId() => int.Parse(User.FindFirst("sub")!.Value);
    private static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
