using System.Security.Cryptography;

namespace EasyStock.Server.Helpers;

/// <summary>
/// The 6-character code an Admin reads on a Guest's phone before promoting them: the first 30 bits of the SHA-256
/// of the user ID (RFC 4122 byte order), 5 bits per character, from an alphabet without 0/O, 1/I.
/// Stored without the dash, shown as XXX-XXX.
/// </summary>
public static class ShortCodeHelper
{
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string For(Guid userId)
    {
        var h = SHA256.HashData(userId.ToByteArray(bigEndian: true));
        var bits = (uint)(h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3]) >> 2;
        var chars = new char[6];
        for (var i = 0; i < 6; i++)
            chars[i] = Alphabet[(int)(bits >> (25 - 5 * i)) & 31];
        return new string(chars);
    }

    public static string Format(string code) => code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;

    /// <summary>What a person typed: dashes and spaces dropped, upper case.</summary>
    public static string Normalize(string? input) =>
        (input ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
}
