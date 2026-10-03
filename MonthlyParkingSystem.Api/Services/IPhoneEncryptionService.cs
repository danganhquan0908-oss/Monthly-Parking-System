namespace MonthlyParkingSystem.Api.Services;

public interface IPhoneEncryptionService
{
    byte[]? Encrypt(string? phoneNumber);
    string? Decrypt(byte[]? encryptedPhoneNumber);
}

public sealed class PhoneEncryptionKeyMissingException()
    : InvalidOperationException("Set MPS_PHONE_ENCRYPTION_KEY to a Base64 encoded 32-byte AES key before saving a phone number.");
