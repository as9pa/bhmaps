using System.Security.Cryptography;

namespace BhMaps.Core.Hashing;

public static class FileHasher
{
    /// <summary>Lowercase hex SHA-256 of the file contents.</summary>
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
