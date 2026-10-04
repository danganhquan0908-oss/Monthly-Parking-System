using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Contracts;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using MonthlyParkingSystem.Api.Services;

namespace MonthlyParkingSystem.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/public/school-registrations")]
public sealed class SchoolRegistrationController(
    MpsDbContext db,
    IPasswordHasher<StaffUser> passwordHasher,
    IPhoneEncryptionService phoneEncryption,
    IEmailSender emailSender,
    JwtTokenService tokens,
    ILogger<SchoolRegistrationController> logger) : ControllerBase
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    private const int MaximumSendsPerHour = 3;
    private const byte MaximumAttempts = 5;

    [HttpPost("send-otp")]
    [EnableRateLimiting("school-registration-send")]
    [ProducesResponseType<SchoolRegistrationOtpResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<SchoolRegistrationOtpResponse>> SendOtp(
        StartSchoolRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        string emailAddress;
        try { emailAddress = NormalizeEmail(request.EmailAddress); }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
        string? phoneNumber = null;
        byte[]? encryptedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            try { phoneNumber = NormalizeVietnamesePhone(request.PhoneNumber); }
            catch (ArgumentException exception)
            {
                return BadRequest(new ProblemDetails { Title = exception.Message });
            }
            try { encryptedPhone = phoneEncryption.Encrypt(phoneNumber); }
            catch (PhoneEncryptionKeyMissingException)
            {
                logger.LogError("School registration is unavailable because the phone encryption key is not configured.");
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Dịch vụ đăng ký chưa được cấu hình đầy đủ.");
            }
        }
        var schoolCode = request.SchoolCode.Trim().ToUpperInvariant();
        var normalizedSchoolCode = schoolCode;
        var normalizedUsername = request.Username.Trim().ToUpperInvariant();

        if (await db.Schools.AsNoTracking().AnyAsync(item => item.NormalizedCode == normalizedSchoolCode, cancellationToken))
            return Conflict(new ProblemDetails { Title = "Mã trường đã được đăng ký. Hãy chọn mã khác hoặc đăng nhập vào trường hiện có." });

        var now = DateTime.UtcNow;
        await db.SchoolRegistrationChallenges
            .Where(item => item.ExpiresAtUtc < now.AddHours(-24) || item.ConsumedAtUtc < now.AddHours(-24))
            .ExecuteDeleteAsync(cancellationToken);
        var emailHash = HashValue("registration-email", emailAddress);
        await using var sendTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var recentRegistrations = db.SchoolRegistrationChallenges.AsNoTracking()
            .Where(item => item.EmailLookupHash == emailHash && item.CreatedAtUtc >= now - SendWindow);
        var recentCount = await recentRegistrations.CountAsync(cancellationToken);
        if (recentCount >= MaximumSendsPerHour)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ProblemDetails { Title = "Đã gửi quá nhiều mã đến email này. Vui lòng thử lại sau một giờ." });
        var lastSentAtUtc = await recentRegistrations.Select(item => (DateTime?)item.CreatedAtUtc)
            .MaxAsync(cancellationToken);
        if (lastSentAtUtc is not null && lastSentAtUtc > now - ResendDelay)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ProblemDetails { Title = "Vui lòng đợi một phút trước khi yêu cầu mã mới." });

        var registrationId = Guid.NewGuid();
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAtUtc = now + CodeLifetime;
        var pending = new SchoolRegistrationChallenge
        {
            RegistrationId = registrationId,
            SchoolCode = schoolCode,
            NormalizedSchoolCode = normalizedSchoolCode,
            SchoolName = request.SchoolName.Trim(),
            EmailAddress = emailAddress,
            EmailLookupHash = emailHash,
            PhoneLookupHash = phoneNumber is null ? null : HashValue("registration-phone", phoneNumber),
            PhoneNumberEncrypted = encryptedPhone,
            Username = request.Username.Trim(),
            NormalizedUsername = normalizedUsername,
            AdminPasswordHash = passwordHasher.HashPassword(new StaffUser(), request.Password),
            OtpHash = HashOtp(registrationId, otp),
            AttemptCount = 0,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };

        db.SchoolRegistrationChallenges.Add(pending);
        await db.SaveChangesAsync(cancellationToken);
        await sendTransaction.CommitAsync(cancellationToken);
        try
        {
            var otpHtml = MpsEmailTemplates.BuildOtpEmail(otp, request.SchoolName, request.Username);
            var plainMessage = $"[MPS - XÁC MINH ĐĂNG KÝ TRƯỜNG]\n\n" +
                $"Chào {request.Username}, bạn vừa thực hiện đăng ký trường {request.SchoolName} trên hệ thống MPS.\n\n" +
                $"MÃ XÁC MINH (OTP) CỦA BẠN LÀ: {otp}\n\n" +
                $"Mã có hiệu lực trong vòng 5 phút. Tuyệt đối không chia sẻ mã này cho người khác.\n" +
                $"Hệ thống Quản lý Gửi xe Tháng KTX (MPS Residence).";
            await emailSender.SendEmailAsync(emailAddress,
                "Mã xác minh đăng ký MPS",
                plainMessage,
                registrationId.ToString("N"), cancellationToken, otpHtml);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or TaskCanceledException or FormatException)
        {
            logger.LogWarning("School registration OTP delivery failed ({FailureType}).", exception.GetType().Name);
            pending.AttemptCount = MaximumAttempts;
            await db.SaveChangesAsync(CancellationToken.None);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Chưa gửi được email xác minh. Vui lòng kiểm tra cấu hình email hoặc thử lại sau.");
        }

        return Accepted(new SchoolRegistrationOtpResponse(registrationId, expiresAtUtc));
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("school-registration-verify")]
    [ProducesResponseType<SchoolRegistrationCompleted>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SchoolRegistrationCompleted>> VerifyOtp(
        VerifySchoolRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var registration = await db.SchoolRegistrationChallenges.SingleOrDefaultAsync(
            item => item.RegistrationId == request.RegistrationId, cancellationToken);
        var now = DateTime.UtcNow;
        if (registration is null || registration.ConsumedAtUtc is not null)
            return BadRequest("Yêu cầu đăng ký không hợp lệ hoặc đã được sử dụng.");
        if (registration.ExpiresAtUtc <= now)
            return BadRequest("Mã xác minh đã hết hạn. Hãy bắt đầu đăng ký lại.");
        if (registration.AttemptCount >= MaximumAttempts)
            return BadRequest("Mã xác minh đã bị khóa do nhập sai quá nhiều lần. Hãy bắt đầu đăng ký lại.");

        var submittedHash = Convert.FromHexString(HashOtp(registration.RegistrationId, request.Code));
        var storedHash = Convert.FromHexString(registration.OtpHash);
        var codeMatches = CryptographicOperations.FixedTimeEquals(submittedHash, storedHash);
        CryptographicOperations.ZeroMemory(submittedHash);
        CryptographicOperations.ZeroMemory(storedHash);
        if (!codeMatches)
        {
            registration.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BadRequest(registration.AttemptCount >= MaximumAttempts
                ? "Mã xác minh đã bị khóa do nhập sai quá nhiều lần. Hãy bắt đầu đăng ký lại."
                : "Mã xác minh không chính xác.");
        }

        var codeIsTaken = await db.Schools.AnyAsync(
            item => item.NormalizedCode == registration.NormalizedSchoolCode, cancellationToken);
        if (codeIsTaken) return Conflict("Mã trường đã được đăng ký. Hãy bắt đầu lại với mã khác.");

        var school = new School
        {
            Code = registration.SchoolCode,
            NormalizedCode = registration.NormalizedSchoolCode,
            Name = registration.SchoolName,
            IsActive = true,
            CreatedAtUtc = now
        };
        db.Schools.Add(school);
        registration.ConsumedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var manager = new StaffUser
            {
                SchoolId = school.SchoolId,
                Username = registration.Username,
                NormalizedUsername = registration.NormalizedUsername,
                PasswordHash = registration.AdminPasswordHash,
                EmailAddress = registration.EmailAddress,
                EmailVerified = registration.EmailAddress is not null,
                PhoneNumberEncrypted = registration.PhoneNumberEncrypted,
                Role = StaffRoles.Admin,
                IsActive = true,
                CreatedAtUtc = now,
                LastLoginAtUtc = now
            };
            db.StaffUsers.Add(manager);
            await db.SaveChangesAsync(cancellationToken);
            db.AccountAuditLogs.Add(new AccountAuditLog
            {
                SchoolId = school.SchoolId,
                TargetStaffUserId = manager.StaffUserId,
                TargetUsername = manager.Username,
                Action = "SchoolSelfRegistered",
                Details = registration.EmailAddress is null
                    ? "InitialRole=Admin;PhoneVerified=true"
                    : "InitialRole=Admin;EmailVerified=true",
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created,
                new SchoolRegistrationCompleted(school.SchoolId, school.Code, school.Name, manager.Username));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Mã trường hoặc tên đăng nhập đã được sử dụng. Hãy kiểm tra lại thông tin đăng ký.");
        }
    }

    private string HashOtp(Guid registrationId, string code) => HashValue("registration-otp", $"{registrationId:N}:{code}");

    private string HashValue(string purpose, string value)
    {
        var data = Encoding.UTF8.GetBytes($"{purpose}:{value}");
        try { return Convert.ToHexString(HMACSHA256.HashData(tokens.SigningKey.Key, data)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private static string NormalizeEmail(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Email không đúng định dạng.", nameof(input));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Email không đúng định dạng.", nameof(input));
        }
        return normalized;
    }

    private static string NormalizeVietnamesePhone(string input)
    {
        var value = input.Trim();
        if (value.Length == 0 || value.Any(character =>
                !char.IsDigit(character) && !char.IsWhiteSpace(character) && character is not '+' and not '-' and not '(' and not ')') ||
            (value.Count(character => character == '+') > 1) || (value.Contains('+') && !value.StartsWith('+')))
            throw new ArgumentException("Số điện thoại không đúng định dạng.", nameof(input));
        var digits = new string(value.Where(char.IsDigit).ToArray());
        string normalized;
        if (value.StartsWith('+')) normalized = $"+{digits}";
        else if (digits.StartsWith("00", StringComparison.Ordinal)) normalized = $"+{digits[2..]}";
        else if (digits.StartsWith("0", StringComparison.Ordinal)) normalized = $"+84{digits[1..]}";
        else if (digits.StartsWith("84", StringComparison.Ordinal)) normalized = $"+{digits}";
        else throw new ArgumentException("Nhập số điện thoại Việt Nam bắt đầu bằng 0 hoặc +84.", nameof(input));

        if (normalized.Length is < 10 or > 13 || !normalized.StartsWith("+84", StringComparison.Ordinal) ||
            normalized.Skip(1).Any(character => character is < '0' or > '9'))
            throw new ArgumentException("Số điện thoại Việt Nam không hợp lệ.", nameof(input));
        return normalized;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
