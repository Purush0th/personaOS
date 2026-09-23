using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace PersonaOS.Application.Ai.Guards;

/// <summary>
/// One place every guard reports firing: a log line, and the <c>personaos.chat.guard.fired</c>
/// counter tagged with the guard and the model. Together they show how often each guard earns
/// its keep, and which models need it, without reading chat transcripts.
/// </summary>
public static class GuardTelemetry
{
    public const string MeterName = "PersonaOS.Chat";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Fired = Meter.CreateCounter<long>(
        "personaos.chat.guard.fired", description: "Times a chat guard changed a tool call or a reply.");

    public static void Record(ILogger logger, string guard, string model, string reason)
    {
        Fired.Add(1, new KeyValuePair<string, object?>("guard", guard), new KeyValuePair<string, object?>("model", model));
        logger.LogInformation("Guard {Guard} fired for {Model}: {Reason}", guard, model, reason);
    }
}
