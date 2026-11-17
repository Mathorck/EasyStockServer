
namespace EasyStock.Server.Models.Backup;

/// <summary>Decompressed bytes an archive may still be read for, shared by its bounded streams.</summary>
internal sealed class ReadBudget(long total)
{
    private long _remaining = total;

    public void Take(int n)
    {
        _remaining -= n;
        if (_remaining < 0) throw new BackupException(BackupError.TooLarge, "Archive too large");
    }
}
