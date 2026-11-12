using EasyStock.Server.Helpers;

namespace EasyStock.Server.Configuration;

/// <summary>
/// Thrown by services for an expected failure; <see cref="ProblemExceptionHandler"/> turns it into Problem Details.
/// <paramref name="extensions"/> become extra top-level members (for example <c>errors</c>). With
/// <paramref name="retryAfter"/>, the answer also has a <c>Retry-After</c> header and a <c>retryAfter</c> member
/// (whole seconds).
/// </summary>
public sealed class ProblemException(
    int status,
    string type,
    string? detail = null,
    IReadOnlyDictionary<string, object?>? extensions = null,
    TimeSpan? retryAfter = null)
    : Exception(detail ?? type)
{
    public int Status { get; } = status;
    public string Type { get; } = type;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>Whole seconds, at least 1.</summary>
    public static int Seconds(TimeSpan t) => Math.Max(1, (int)Math.Ceiling(t.TotalSeconds));

    /// <summary>400 <c>validation-failed</c> with an <c>errors</c> map, like a model validation failure.</summary>
    public static ProblemException Validation(string field, params string[] messages) =>
        new(StatusCodes.Status400BadRequest, ProblemTypesHelper.ValidationFailed,
            extensions: new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { [field] = messages } });

    public static ProblemException Of(int status, string type, string? detail = null) => new(status, type, detail);
}
