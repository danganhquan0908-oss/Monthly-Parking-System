using System.Threading.Channels;

namespace MonthlyParkingSystem.Api.Services;

public sealed record NotificationQueueMessage(long NotificationLogId);

public sealed class NotificationQueue
{
    private readonly Channel<NotificationQueueMessage> _channel;

    public NotificationQueue(IConfiguration configuration)
    {
        var capacity = Math.Clamp(configuration.GetValue<int?>("Notification:QueueCapacity") ?? 1000, 100, 10000);
        _channel = Channel.CreateBounded<NotificationQueueMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(NotificationQueueMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<NotificationQueueMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
