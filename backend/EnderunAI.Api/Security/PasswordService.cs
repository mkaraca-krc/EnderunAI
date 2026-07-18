using System.Security.Cryptography;

namespace EnderunAI.Api.Security;

public sealed class PasswordService
{
    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string hashBase64, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var expected = Convert.FromBase64String(hashBase64);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
