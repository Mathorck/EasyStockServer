using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Helpers;

// Counts and ids only: never row contents.
internal static partial class BackupLogHelper
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Backup exported by user {UserId}: {Rows} rows, {Photos} photos")]
    public static partial void Exported(ILogger logger, Guid userId, int rows, int photos);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Backup export: {Missing} photos are missing from the image store and were left out")]
    public static partial void PhotosMissing(ILogger logger, int missing);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Backup imported by user {UserId}, mode {Mode}: {Added} rows added, {Updated} updated, {Photos} photos, {Conflicts} conflicts")]
    public static partial void Imported(ILogger logger, Guid userId, string mode, int added, int updated, int photos, int conflicts);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Backup import by user {UserId} refused: {Error}")]
    public static partial void Refused(ILogger logger, Guid userId, string error);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Backup import by user {UserId} failed in the database (SQLSTATE {SqlState}); nothing was changed")]
    public static partial void DatabaseRejected(ILogger logger, Guid userId, string? sqlState);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = "Backup import: the after-commit step failed ({ExceptionType})")]
    public static partial void AfterCommitFailed(ILogger logger, string exceptionType);
}
