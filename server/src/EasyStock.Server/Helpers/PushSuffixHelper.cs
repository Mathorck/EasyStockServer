namespace EasyStock.Server.Helpers;

/// <summary>The " (2)", " (3)"… suffix of BackupMerge: the base is cut so base plus suffix fits the column.</summary>
public static class PushSuffixHelper
{
    public static string Apply(string value, int n, int maxLength)
    {
        var suffix = $" ({n})";
        var keep = Math.Max(0, maxLength - suffix.Length);
        return (value.Length > keep ? value[..keep] : value).TrimEnd() + suffix;
    }

    /// <summary>The first free suffixed value, from " (2)".</summary>
    public static async Task<string> FirstFreeAsync(string value, int maxLength, Func<string, Task<bool>> isTaken)
    {
        for (var n = 2; ; n++)
        {
            var candidate = Apply(value, n, maxLength);
            if (!await isTaken(candidate)) return candidate;
        }
    }
}
