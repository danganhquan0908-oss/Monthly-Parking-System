using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using MonthlyParkingSystem.Api.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<MpsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("MpsDatabase")));
builder.Services.AddSingleton<IPhoneEncryptionService, AesGcmPhoneEncryptionService>();
var tokenService = new JwtTokenService(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtTokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtTokenService.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenService.SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirst("sub")?.Value;
                var role = context.Principal?.FindFirst("role")?.Value;
                var schoolIdValue = context.Principal?.FindFirst("school_id")?.Value;
                if (!int.TryParse(userIdValue, out var userId) || !int.TryParse(schoolIdValue, out var schoolId) ||
                    string.IsNullOrWhiteSpace(role))
                {
                    context.Fail("The token has no active school staff identity.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<MpsDbContext>();
                var isActive = await db.StaffUsers.AsNoTracking().AnyAsync(user =>
                    user.StaffUserId == userId && user.SchoolId == schoolId && user.IsActive && user.Role == role &&
                    (user.LockoutEndUtc == null || user.LockoutEndUtc <= DateTime.UtcNow));
                var schoolIsActive = await db.Schools.AsNoTracking().AnyAsync(item => item.SchoolId == schoolId && item.IsActive);
                if (!isActive || !schoolIsActive) context.Fail("The staff account or school is inactive or locked.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("school-registration-send", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("school-registration-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<DailyScanWorker>();
builder.Services.AddHostedService<NotificationProcessor>();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (builder.Environment.IsDevelopment())
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        return;
    }

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

app.UseExceptionHandler();
if (tokenService.UsesTemporaryDevelopmentKey)
    app.Logger.LogWarning("MPS_JWT_SIGNING_KEY is unset; development tokens will be invalidated when the API restarts.");
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (MpsDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        await db.Schools.AsNoTracking().Select(school => school.SchoolId).Take(1).ToListAsync(cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch
    {
        return Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
var sourceFrontendPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "MonthlyParkingSystem.Web"));
var frontendPath = Directory.Exists(sourceFrontendPath) ? sourceFrontendPath : Path.Combine(app.Environment.ContentRootPath, "Frontend");
var dashboardPath = Path.Combine(frontendPath, "index.html");
app.MapGet("/", () => Results.File(dashboardPath, "text/html; charset=utf-8"));
var registrationPagePath = Path.Combine(frontendPath, "register.html");
app.MapGet("/register", () => Results.File(registrationPagePath, "text/html; charset=utf-8"));
var platformPagePath = Path.Combine(frontendPath, "platform.html");
app.MapGet("/platform", () => Results.File(platformPagePath, "text/html; charset=utf-8"));
var logoPath = Path.Combine(frontendPath, "logo.svg");
app.MapGet("/logo.svg", () => Results.File(logoPath, "image/svg+xml"));
app.MapGet("/favicon.ico", () => Results.File(logoPath, "image/svg+xml"));
var logoPngPath = Path.Combine(frontendPath, "logo.png");
app.MapGet("/logo.png", () => Results.File(logoPngPath, "image/png"));
app.MapControllers();

app.Run();
