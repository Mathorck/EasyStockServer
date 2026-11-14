using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Server.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// The ES256 key that signs access tokens (docs/04-server-implementation-plan.md, 3.5), kept in
/// <c>${DATA_DIR}/keys/jwt-es256.pem</c> (PKCS#8, mode 0600). Created on first use, written to a temporary file
/// and renamed, so a crash never leaves half a key. Deleting the file rotates the key: every phone refreshes once.
/// The refresh-token tag key is derived from it too (<see cref="RefreshTokenHandler"/>): current refresh tokens
/// keep working (looked up by hash), but tokens issued before the change can no longer reveal reuse.
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

        // A key readable by other users (e.g. restored from a backup with a loose umask) is tightened, not refused:
        // refusing would lock every phone out over a fixable mode.
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
        {
            LogLooseMode(log, path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(File.ReadAllText(path));
            if (ecdsa.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new InvalidOperationException($"The token signing key {path} is not a P-256 key, which ES256 requires.");
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
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
        using (var fs = new FileStream(temp, fileOptions))
        {
            fs.Write(new UTF8Encoding(false).GetBytes(pem));
            fs.Flush(flushToDisk: true); // fsync before the rename, so a crash never leaves an empty key
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The token signing key {Path} was accessible by other users; its mode is now 600")]
    private static partial void LogLooseMode(ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Token signing key loaded (kid {KeyId})")]
    private static partial void LogLoaded(ILogger log, string keyId);
}
