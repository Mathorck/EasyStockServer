using System.Buffers.Text;
using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// The ES256 key that signs access tokens (docs/04-server-implementation-plan.md, 3.5), kept in
/// <c>${DATA_DIR}/keys/jwt-es256.pem</c> (PKCS#8, mode 0600). Created on first use, written to a temporary file
/// and renamed, so a crash never leaves half a key. Deleting the file rotates the key: every phone refreshes once.
/// </summary>
public sealed partial class SigningKeyHandler(IOptions<ServerOptions> options, ILogger<SigningKeyHandler> log) : IDisposable
{
    public const string FileName = "jwt-es256.pem";

    private readonly Lock _gate = new();
    private ECDsaSecurityKey? _key;

    public string KeyPath => Path.Combine(options.Value.DataDir, "keys", FileName);

    public ECDsaSecurityKey Key
    {
        get
        {
            if (_key is { } k) return k;
            lock (_gate) return _key ??= LoadOrCreate();
        }
    }

    private ECDsaSecurityKey LoadOrCreate()
    {
        var path = KeyPath;
        if (!File.Exists(path)) Create(path);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(path));
        var kid = Base64Url.EncodeToString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()));
        LogLoaded(log, kid);
        return new ECDsaSecurityKey(ecdsa) { KeyId = kid };
    }

    private void Create(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();
        var temp = Path.Combine(dir, $".{FileName}.{Guid.NewGuid():N}.tmp");
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var w = new StreamWriter(temp, fileOptions))
            w.Write(pem);
        try
        {
            File.Move(temp, path, overwrite: false);
            LogCreated(log, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another instance won the race: use its key.
            File.Delete(temp);
        }
    }

    public void Dispose() => _key?.ECDsa.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created a new token signing key at {Path}")]
    private static partial void LogCreated(ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Token signing key loaded (kid {KeyId})")]
    private static partial void LogLoaded(ILogger log, string keyId);
}
