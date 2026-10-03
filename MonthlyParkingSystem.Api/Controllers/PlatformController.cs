using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Contracts;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using MonthlyParkingSystem.Api.Services;

namespace MonthlyParkingSystem.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/platform/schools")]
public sealed class PlatformController(MpsDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<SchoolSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<SchoolSummary>>> GetSchools(
        [FromHeader(Name = "X-MPS-Platform-Key")] string? suppliedKey,
        CancellationToken cancellationToken)
    {
        if (!HasValidKey(suppliedKey, out var keyIsConfigured))
            return keyIsConfigured ? Unauthorized() : Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Platform provisioning is not configured");
        return Ok(await db.Schools.AsNoTracking().OrderBy(item => item.Name)
            .Select(item => new SchoolSummary(item.SchoolId, item.Code, item.Name, item.IsActive, item.CreatedAtUtc))
            .ToListAsync(cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<CreateSchoolResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateSchoolResponse>> CreateSchool(
        CreateSchoolRequest request,
        [FromHeader(Name = "X-MPS-Platform-Key")] string? suppliedKey,
        CancellationToken cancellationToken)
    {
        if (!HasValidKey(suppliedKey, out var keyIsConfigured))
            return keyIsConfigured ? Unauthorized() : Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Platform provisioning is not configured");

        var code = request.Code.Trim();
        var school = new School
        {
            Code = code,
            NormalizedCode = code.ToUpperInvariant(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var token = InvitationTokenService.CreateToken();
        var now = DateTime.UtcNow;
        var invitation = new StaffInvitation
        {
            StaffInvitationId = Guid.NewGuid(),
            School = school,
            Role = StaffRoles.Manager,
            TokenHash = InvitationTokenService.HashToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        db.Schools.Add(school);
        db.StaffInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            db.AccountAuditLogs.Add(new AccountAuditLog
            {
                SchoolId = school.SchoolId,
                Action = "SchoolCreated",
                Details = $"Code={school.Code};InitialRole={StaffRoles.Manager}",
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var response = new CreateSchoolResponse(school.SchoolId, school.Code, school.Name, token, invitation.ExpiresAtUtc);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Mã trường đã tồn tại.");
        }
    }

    [HttpPut("{id:int}/active")]
    public async Task<IActionResult> SetSchoolActive(
        int id,
        SetSchoolActiveRequest request,
        [FromHeader(Name = "X-MPS-Platform-Key")] string? suppliedKey,
        CancellationToken cancellationToken)
    {
        if (!HasValidKey(suppliedKey, out var keyIsConfigured))
            return keyIsConfigured ? Unauthorized() : Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Platform provisioning is not configured");
        var school = await db.Schools.SingleOrDefaultAsync(item => item.SchoolId == id, cancellationToken);
        if (school is null) return NotFound();
        school.IsActive = request.IsActive;
        db.AccountAuditLogs.Add(new AccountAuditLog
        {
            SchoolId = school.SchoolId,
            Action = request.IsActive ? "SchoolReactivated" : "SchoolSuspended",
            Details = $"Code={school.Code}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool HasValidKey(string? suppliedKey, out bool keyIsConfigured)
    {
        var expectedKey = configuration["MPS_PLATFORM_PROVISIONING_KEY"];
        keyIsConfigured = !string.IsNullOrWhiteSpace(expectedKey);
        return keyIsConfigured && KeysMatch(expectedKey!, suppliedKey);
    }

    private static bool KeysMatch(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
