namespace EasyStock.Server.Models;

public enum ImageSaveStatus { Saved, TooLarge, NotJpeg, HashMismatch, QuotaExceeded }

/// <summary>
/// A photo streamed to a temp file and checked (<see cref="Handlers.Images.ImageHandler.StageAsync"/>). Disposing it
/// deletes the temp file unless it was promoted into place.
/// </summary>
public sealed class StagedImage(string sha256, string tempPath) : IDisposable
{
    public string Sha256 { get; } = sha256;
    public string TempPath { get; } = tempPath;
    public ImageSaveStatus Status { get; internal set; }
    public long Size { get; internal set; }
    public bool Promoted { get; internal set; }

    internal StagedImage Failed(ImageSaveStatus status)
    {
        Status = status;
        Dispose();
        return this;
    }

    public void Dispose()
    {
        if (Promoted) return;
        try { File.Delete(TempPath); } catch (IOException) { }
    }
}
