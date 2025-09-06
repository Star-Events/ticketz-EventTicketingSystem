using System.Security.Cryptography;

namespace EventTicketingSystem.Security
{
    public static class PasswordHasher
    {
        // Returns (hashBase64, saltBase64)
        public static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16); // 128-bit salt
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(32); // 256-bit hash
            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }
    }
}
