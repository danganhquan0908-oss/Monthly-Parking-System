using System.Security.Cryptography;
using System.Text;

namespace MonthlyParkingSystem.Api.Services;

public sealed class AesGcmPhoneEncryptionService(IConfiguration configuration) : IPhoneEncryptionService
{
    public byte[]? Encrypt(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;
        var key = GetKey();
        var plaintext = Encoding.UTF8.GetBytes(phoneNumber.Trim());
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, plaintext, ciphertext, tag);

            // Persist nonce || tag || ciphertext so encryption is authenticated and decryptable by the worker.
            var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string? Decrypt(byte[]? encryptedPhoneNumber)
    {
        if (encryptedPhoneNumber is null) return null;
        const int nonceLength = 12;
        const int tagLength = 16;
        if (encryptedPhoneNumber.Length < nonceLength + tagLength)
            throw new CryptographicException("Encrypted phone data is incomplete.");

        var key = GetKey();
        var plaintext = new byte[encryptedPhoneNumber.Length - nonceLength - tagLength];
        try
        {
            var nonce = encryptedPhoneNumber.AsSpan(0, nonceLength);
            var tag = encryptedPhoneNumber.AsSpan(nonceLength, tagLength);
            var ciphertext = encryptedPhoneNumber.AsSpan(nonceLength + tagLength);
            using (var aes = new AesGcm(key, tagLength)) aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] GetKey()
    {
        var encodedKey = configuration["MPS_PHONE_ENCRYPTION_KEY"];
        if (string.IsNullOrWhiteSpace(encodedKey)) throw new PhoneEncryptionKeyMissingException();

        byte[] key;
        try { key = Convert.FromBase64String(encodedKey); }
        catch (FormatException ex) { throw new InvalidOperationException("MPS_PHONE_ENCRYPTION_KEY must be valid Base64.", ex); }
        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("MPS_PHONE_ENCRYPTION_KEY must decode to exactly 32 bytes.");
        }
        return key;
    }
}
