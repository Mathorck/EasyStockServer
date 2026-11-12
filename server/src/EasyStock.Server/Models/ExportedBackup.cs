using System.Data;
using System.IO.Compression;
using System.Text.Json;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Models;

/// <summary>A finished backup in a temporary file. Disposing the stream deletes the file.</summary>
public sealed record ExportedBackup(FileStream Stream, string FileName, int Rows, int Photos);
