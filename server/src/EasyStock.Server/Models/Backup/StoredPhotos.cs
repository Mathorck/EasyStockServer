
namespace EasyStock.Server.Models.Backup;

/// <param name="Moved">Hashes whose file this call moved into place.</param>
/// <param name="Inserted">New <c>image</c> rows.</param>
public sealed record StoredPhotos(IReadOnlyList<string> Moved, int Inserted);
