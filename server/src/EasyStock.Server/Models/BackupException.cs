using System.Text.Json;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Models;

public sealed class BackupException(BackupError error, string message) : Exception(message)
{
    public BackupError Error { get; } = error;
}
