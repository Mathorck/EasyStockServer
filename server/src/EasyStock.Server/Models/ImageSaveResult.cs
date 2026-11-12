namespace EasyStock.Server.Models;

public enum ImageSaveStatus { Saved, TooLarge, NotJpeg, HashMismatch }

public readonly record struct ImageSaveResult(ImageSaveStatus Status, long Size = 0);
