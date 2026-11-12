using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>Every Serilog event of one test host (Serilog reads sinks registered in DI).</summary>
public sealed class LogCapture : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly Lock _gate = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate) _events.Add(logEvent);
    }

    public IReadOnlyList<LogEvent> Events
    {
        get { lock (_gate) return [.. _events]; }
    }

    /// <summary>Rendered message, template, properties and exception of each event.</summary>
    public static string Text(LogEvent e)
    {
        var text = new StringBuilder(e.RenderMessage()).Append('\n').Append(e.MessageTemplate.Text);
        foreach (var (key, value) in e.Properties) text.Append('\n').Append(key).Append('=').Append(value);
        if (e.Exception != null) text.Append('\n').Append(e.Exception);
        return text.ToString();
    }

    /// <summary>The setup token values printed so far, oldest first.</summary>
    public IReadOnlyList<string> SetupTokens() =>
        [.. Events.Where(e => e.Properties.ContainsKey("SetupToken"))
            .Select(e => ((ScalarValue)e.Properties["SetupToken"]).Value?.ToString() ?? "")];
}
