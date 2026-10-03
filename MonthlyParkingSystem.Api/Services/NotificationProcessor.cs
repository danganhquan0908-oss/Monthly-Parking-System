using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using Polly;
using Polly.Retry;

namespace MonthlyParkingSystem.Api.Services;

public sealed class NotificationProcessor(
    NotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item.NotificationLogId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification log {NotificationLogId} could not be processed", item.NotificationLogId);
            }
        }
    }

    private async Task ProcessAsync(long notificationLogId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MpsDbContext>();
        var log = await db.NotificationLogs
            .Include(item => item.Contract).ThenInclude(contract => contract.Student)
            .Include(item => item.Contract).ThenInclude(contract => contract.Vehicle)
            .SingleOrDefaultAsync(item => item.NotificationLogId == notificationLogId, stoppingToken);
        if (log is null || log.Status != NotificationStatuses.Pending) return;

        if (log.Contract.Status == ContractStatuses.Cancelled)
        {
            MarkFailed(log, 0, "Contract was cancelled before notification delivery.");
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        var phoneEncryption = services.GetRequiredService<IPhoneEncryptionService>();
        string? destination;
        try
        {
            destination = phoneEncryption.Decrypt(log.Contract.Student.PhoneEncrypted);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            MarkFailed(log, 0, "Student phone number could not be decrypted.");
            await db.SaveChangesAsync(stoppingToken);
            logger.LogWarning("Notification log {NotificationLogId} has no usable encrypted phone number", notificationLogId);
            return;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            MarkFailed(log, 0, "Student phone number is missing.");
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        log.DestinationMasked = MaskPhone(destination);
        var message = $"MPS: Hợp đồng gửi xe biển số {log.Contract.Vehicle.LicensePlate} sẽ hết hạn ngày {log.DueDate:dd/MM/yyyy}. Vui lòng liên hệ Ban quản lý KTX để gia hạn.";
        var sender = services.GetRequiredService<INotificationSender>();
        byte attempts = 1;
        try
        {
            var pipeline = CreateRetryPipeline(notificationLogId, () => attempts = (byte)Math.Min(attempts + 1, 4));
            log.ProviderMessageId = await pipeline.ExecuteAsync(
                async token => await sender.SendAsync(log.Channel, destination, message, notificationLogId, token),
                stoppingToken);
            log.Status = NotificationStatuses.Sent;
            log.AttemptCount = attempts;
            log.ErrorMessage = null;
            log.SentAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            MarkFailed(log, attempts, SafeErrorMessage(exception));
            logger.LogWarning("Notification log {NotificationLogId} failed after {AttemptCount} attempt(s): {Failure}",
                notificationLogId, attempts, log.ErrorMessage);
        }

        await db.SaveChangesAsync(stoppingToken);
    }

    private ResiliencePipeline<string?> CreateRetryPipeline(long notificationLogId, Action onRetry) =>
        new ResiliencePipelineBuilder<string?>()
            .AddRetry(new RetryStrategyOptions<string?>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromSeconds(5),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<string?>()
                    .Handle<HttpRequestException>(IsTransientHttpError)
                    .Handle<TimeoutException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    onRetry();
                    logger.LogWarning("Retry {RetryNumber}/3 for notification log {NotificationLogId}",
                        args.AttemptNumber + 1, notificationLogId);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

    private static bool IsTransientHttpError(HttpRequestException exception) =>
        exception.StatusCode is null || exception.StatusCode == HttpStatusCode.RequestTimeout ||
        exception.StatusCode == HttpStatusCode.TooManyRequests || (int)exception.StatusCode >= 500;

    private static void MarkFailed(NotificationLog log, byte attempts, string error)
    {
        log.Status = NotificationStatuses.Failed;
        log.AttemptCount = attempts;
        log.ErrorMessage = error;
        log.SentAtUtc = null;
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => $"Notification gateway returned HTTP {(int)status}.",
        HttpRequestException => "Notification gateway network error.",
        TimeoutException or TaskCanceledException => "Notification gateway timed out.",
        InvalidOperationException => "Notification gateway is not configured correctly.",
        _ => "Notification delivery failed."
    };

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return new string('*', Math.Max(1, digits.Length));
        return new string('*', digits.Length - 4) + digits[^4..];
    }
}
