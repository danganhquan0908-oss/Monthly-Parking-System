namespace MonthlyParkingSystem.Api.Services;

public interface INotificationSender
{
    Task<string?> SendAsync(string channel, string destination, string message, long notificationLogId, CancellationToken cancellationToken);
}
