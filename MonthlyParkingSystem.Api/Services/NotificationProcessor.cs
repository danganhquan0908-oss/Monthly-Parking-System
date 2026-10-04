using System.Net.Mail;
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

        var destination = log.Contract.Student.EmailAddress?.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            MarkFailed(log, 0, "Student email address is missing.");
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        log.DestinationMasked = MaskEmail(destination);
        const string subject = "MPS - Nhắc gia hạn hợp đồng gửi xe";
        var studentName = log.Contract.Student?.FullName ?? "Sinh viên";
        var room = log.Contract.Student?.RoomNumber ?? "KTX";
        var message = $"[MPS - THÔNG BÁO NHẮC GIA HẠN HỢP ĐỒNG GỬI XE]\n\n" +
            $"Kính gửi: {studentName} (Phòng {room}),\n" +
            $"Hợp đồng gửi xe biển số {log.Contract.Vehicle.LicensePlate} của bạn sẽ hết hạn ngày {log.DueDate:dd/MM/yyyy}.\n\n" +
            $"Vui lòng liên hệ Văn phòng Quản sinh / Ban Quản lý KTX trước ngày hết hạn để gia hạn hợp đồng.\n" +
            $"Sau thời hạn trên, thẻ từ phương tiện sẽ tạm thời bị khóa tại cổng barrier.\n\n" +
            $"Email này được gửi tự động từ Hệ thống MPS Residence.";
        var sender = services.GetRequiredService<IEmailSender>();
        byte attempts = 1;
        try
        {
            var pipeline = CreateRetryPipeline(notificationLogId, () => attempts = (byte)Math.Min(attempts + 1, 4));
            var expiryHtml = MpsEmailTemplates.BuildContractExpiryEmail(
                log.Contract.Vehicle.LicensePlate,
                log.DueDate,
                log.Contract.Student?.FullName,
                log.Contract.Student?.RoomNumber,
                log.Contract.Student?.StudentCode);
            log.ProviderMessageId = await pipeline.ExecuteAsync(
                async _ =>
                {
                    await sender.SendEmailAsync(destination, subject, message, notificationLogId.ToString(), stoppingToken, expiryHtml);
                    return (string?)null;
                },
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
                    .Handle<SmtpException>(IsTransientSmtpError)
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

    private static bool IsTransientSmtpError(SmtpException exception) =>
        exception.StatusCode is SmtpStatusCode.GeneralFailure or SmtpStatusCode.MailboxBusy or
            SmtpStatusCode.InsufficientStorage or SmtpStatusCode.ServiceNotAvailable;

    private static void MarkFailed(NotificationLog log, byte attempts, string error)
    {
        log.Status = NotificationStatuses.Failed;
        log.AttemptCount = attempts;
        log.ErrorMessage = error;
        log.SentAtUtc = null;
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        SmtpException { StatusCode: { } status } => $"Email provider returned SMTP status {(int)status}.",
        TimeoutException or TaskCanceledException => "Email delivery timed out.",
        InvalidOperationException => "Email service is not configured correctly.",
        FormatException => "Student email address is invalid.",
        _ => "Notification delivery failed."
    };

    private static string MaskEmail(string email)
    {
        var separator = email.LastIndexOf('@');
        if (separator <= 0 || separator == email.Length - 1) return "***";
        return $"{email[0]}{new string('*', separator - 1)}{email[separator..]}";
    }
}
