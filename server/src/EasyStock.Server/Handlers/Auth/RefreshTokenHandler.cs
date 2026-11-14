using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Refresh tokens that name their session (docs/03-server-spec.md, "Refresh tokens"): Base64URL of
/// <c>session id (16) | nonce (32) | tag (16)</c>, where the tag is an HMAC under a key derived from the signing
/// key. A token with a valid tag was issued by this server for that session, so presenting one that is no longer
/// current reveals reuse even several rotations later, without keeping every old hash.
/// <para>
/// The next token of a rotation is derived from the session id and the hash of the token it replaces, so a
/// refresh retried with the just-rotated token can be answered with the same new token.
/// </para>
/// </summary>
public sealed class RefreshTokenHandler(SigningKeyHandler keys)
{
    private const int IdLength = 16;
    private const int NonceLength = 32;
    private const int TagLength = 16;
    private const int TokenLength = IdLength + NonceLength + TagLength;

    private readonly Lazy<(byte[] Tag, byte[] Next)> _keys = new(() => DeriveKeys(keys));

    /// <summary>A fresh random token for a new session.</summary>
    public string New(Guid sessionId) => Build(sessionId, RandomNumberGenerator.GetBytes(NonceLength));

    /// <summary>The token that replaces the one whose hash is <paramref name="previousHash"/>.</summary>
    public string Next(Guid sessionId, byte[] previousHash)
    {
        Span<byte> input = stackalloc byte[IdLength + previousHash.Length];
        sessionId.TryWriteBytes(input[..IdLength]);
        previousHash.CopyTo(input[IdLength..]);
        return Build(sessionId, HMACSHA256.HashData(_keys.Value.Next, input));
    }

    /// <summary>
    /// The session a token was issued for, when its tag is valid. False for older 32-byte tokens, forged ones,
    /// and tokens issued under a signing key that was since replaced.
    /// </summary>
    public bool TryReadSession(string token, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        Span<byte> raw = stackalloc byte[TokenLength];
        if (Base64Url.GetMaxDecodedLength(token.Length) < TokenLength
            || !Base64Url.TryDecodeFromChars(token, raw, out var written) || written != TokenLength)
            return false;
        Span<byte> tag = stackalloc byte[32];
        HMACSHA256.HashData(_keys.Value.Tag, raw[..(IdLength + NonceLength)], tag);
        if (!CryptographicOperations.FixedTimeEquals(tag[..TagLength], raw[(IdLength + NonceLength)..]))
            return false;
        sessionId = new Guid(raw[..IdLength]);
        return true;
    }

    private string Build(Guid sessionId, ReadOnlySpan<byte> nonce)
    {
        Span<byte> raw = stackalloc byte[TokenLength];
        sessionId.TryWriteBytes(raw[..IdLength]);
        nonce[..NonceLength].CopyTo(raw[IdLength..]);
        Span<byte> tag = stackalloc byte[32];
        HMACSHA256.HashData(_keys.Value.Tag, raw[..(IdLength + NonceLength)], tag);
        tag[..TagLength].CopyTo(raw[(IdLength + NonceLength)..]);
        return Base64Url.EncodeToString(raw);
    }

    private static (byte[] Tag, byte[] Next) DeriveKeys(SigningKeyHandler keys)
    {
        var d = keys.Key.ECDsa.ExportParameters(includePrivateParameters: true).D!;
        try
        {
            return (
                HKDF.DeriveKey(HashAlgorithmName.SHA256, d, 32, info: Encoding.ASCII.GetBytes("easystock refresh-token tag")),
                HKDF.DeriveKey(HashAlgorithmName.SHA256, d, 32, info: Encoding.ASCII.GetBytes("easystock refresh-token next")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(d);
        }
    }
}
