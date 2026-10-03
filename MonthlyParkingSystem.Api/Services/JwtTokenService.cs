using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Api.Services;

public sealed class JwtTokenService
{
    public const string Issuer = "MPS";
    public const string Audience = "MPS-Internal";
    private readonly byte[] _signingKey;

    public JwtTokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var encodedKey = configuration["MPS_JWT_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("MPS_JWT_SIGNING_KEY must be set to a Base64 encoded key of at least 32 bytes.");
            _signingKey = RandomNumberGenerator.GetBytes(32);
            UsesTemporaryDevelopmentKey = true;
        }
        else
        {
            try { _signingKey = Convert.FromBase64String(encodedKey); }
            catch (FormatException ex) { throw new InvalidOperationException("MPS_JWT_SIGNING_KEY must be valid Base64.", ex); }
            if (_signingKey.Length < 32)
            {
                CryptographicOperations.ZeroMemory(_signingKey);
                throw new InvalidOperationException("MPS_JWT_SIGNING_KEY must decode to at least 32 bytes.");
            }
        }
    }

    public bool UsesTemporaryDevelopmentKey { get; }
    public SymmetricSecurityKey SigningKey => new(_signingKey);

    public (string Token, DateTime ExpiresAtUtc) CreateToken(StaffUser staffUser, School school)
    {
        var expires = DateTime.UtcNow.AddMinutes(60);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, staffUser.StaffUserId.ToString()),
            new Claim("name", staffUser.Username),
            new Claim("role", staffUser.Role),
            new Claim("school_id", staffUser.SchoolId.ToString()),
            new Claim("school_code", school.Code)
        };
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Audience, claims, notBefore: DateTime.UtcNow,
            expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
