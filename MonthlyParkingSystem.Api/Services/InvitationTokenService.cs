using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace MonthlyParkingSystem.Api.Services;

public static class InvitationTokenService
{
    public static string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
