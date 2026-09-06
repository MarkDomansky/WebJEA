using System.Threading.Channels;

namespace WebJEA.Telemetry;

/// <summary>
/// Queue between request-scoped TelemetryService instances and the background sender.
/// Replaces HostingEnvironment.QueueBackgroundWorkItem.
/// </summary>
public class TelemetryChannel
{
    private readonly Channel<Dictionary<string, object>> _channel =
        Channel.CreateUnbounded<Dictionary<string, object>>();

    public void Enqueue(Dictionary<string, object> metrics)
    {
        _channel.Writer.TryWrite(metrics);
    }

    public IAsyncEnumerable<Dictionary<string, object>> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
