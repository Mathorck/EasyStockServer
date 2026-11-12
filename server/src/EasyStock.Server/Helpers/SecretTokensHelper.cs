using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Server.Helpers;

/// <summary>
/// Random bearer secrets (refresh, setup, invite, public and reset tokens): 32 bytes, Base64URL. Only the
/// SHA-256 of a token is stored.
/// </summary>
public static class SecretTokensHelper
{
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool Matches(string token, byte[]? hash) =>
        hash is not null && CryptographicOperations.FixedTimeEquals(Hash(token), hash);
}
