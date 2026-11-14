using EasyStock.Server.Filters;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;

namespace EasyStock.Server.Controllers;

[ApiController]
[Route("api/v1/admin/backup")]
[RequirePermission(Permission.Administer)]
public sealed class AdminBackupController(
    ICurrentUser user,
    BackupExportHandler exporter,
    BackupImportHandler importer,
    IOptions<ServerOptions> options,
    ILogger<AdminBackupController> logger) : ControllerBase
{
    /// <summary>A backup of all the shared data and its photos, in the app's backup format (plain zip).</summary>
    [HttpGet]
    [Produces("application/octet-stream")]
    [ProducesResponseType<FileResult>(StatusCodes.Status200OK, "application/octet-stream")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var backup = await exporter.ExportAsync(user.UserId, ct);
        return File(backup.Stream, "application/octet-stream", backup.FileName);
    }

    /// <summary>Imports a backup (raw request body). <c>mode=merge</c> keeps the data and merges, <c>mode=replace</c> wipes it first.</summary>
    [HttpPost]
    [Consumes("application/octet-stream", "application/zip", "application/x-zip-compressed", "application/x-www-form-urlencoded", "text/plain")]
    [RequestSizeLimit(BackupFormatHelper.MaxUploadBytes)]
    [RawBody("application/octet-stream", "application/zip", Description = "The backup zip.")]
    [ProducesResponseType<BackupImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemTypesHelper.ContentType)]
    public async Task<IActionResult> Import([FromQuery] string? mode, CancellationToken ct)
    {
        BackupImportMode parsed;
        switch (mode)
        {
            case "merge": parsed = BackupImportMode.Merge; break;
            case "replace": parsed = BackupImportMode.Replace; break;
            default:
                return ProblemTypesHelper.Problem(StatusCodes.Status400BadRequest, ProblemTypesHelper.ValidationFailed, detail: "mode must be 'merge' or 'replace'.");
        }

        var tempDir = Path.Combine(options.Value.DataDir, "tmp");
        Directory.CreateDirectory(tempDir);
        await using var upload = new FileStream(Path.Combine(tempDir, $"upload-{Guid.NewGuid():N}.zip"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await CopyBoundedAsync(Request.Body, upload, BackupFormatHelper.MaxUploadBytes, ct);
            upload.Position = 0;
            return Ok(await importer.ImportAsync(upload, parsed, user.UserId, ct));
        }
        catch (BackupException e)
        {
            BackupLogHelper.Refused(logger, user.UserId, e.Error.ToString());
            return Refusal(e.Error);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Refusal(BackupError.TooLarge);
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max) throw new BackupException(BackupError.TooLarge, "Upload too large");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static ObjectResult Refusal(BackupError error) => error switch
    {
        BackupError.TooLarge => ProblemTypesHelper.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypesHelper.BackupTooLarge,
            detail: "The backup is too large."),
        BackupError.Encrypted => ProblemTypesHelper.Problem(StatusCodes.Status422UnprocessableEntity, ProblemTypesHelper.BackupEncrypted,
            detail: "Encrypted backups cannot be imported by the server. Export an unencrypted backup from the app."),
        BackupError.NewerVersion => ProblemTypesHelper.Problem(StatusCodes.Status422UnprocessableEntity, ProblemTypesHelper.BackupNewerVersion,
            detail: "The backup comes from a newer version of EasyStock than this server understands."),
        BackupError.NotABackup => ProblemTypesHelper.Problem(StatusCodes.Status422UnprocessableEntity, ProblemTypesHelper.NotABackup,
            detail: "The file is not an EasyStock backup."),
        _ => ProblemTypesHelper.Problem(StatusCodes.Status422UnprocessableEntity, ProblemTypesHelper.BackupInvalid,
            detail: "The backup is damaged or inconsistent. Nothing was imported."),
    };
}
