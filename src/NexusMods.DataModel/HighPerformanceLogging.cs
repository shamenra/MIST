using Microsoft.Extensions.Logging;
using NexusMods.Paths;

namespace NexusMods.DataModel;

internal static partial class HighPerformanceLogging
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Trace,
        Message = "Processing jobs")]
    public static partial void ProcessingJobs(this ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Trace,
        Message = "Done processing")]
    public static partial void DoneProcessing(this ILogger logger);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Trace,
        Message = "Sending {bytes} byte message to queue {queue}")]
    public static partial void SendingByteMessageToQueue(
        this ILogger logger,
        Size bytes,
        string queue);
}
