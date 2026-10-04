using System.Data;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Domain.Contracts;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Api.Services;

public sealed class DailyScanWorker(
    IServiceScopeFactory scopeFactory,
    NotificationQueue queue,
    IConfiguration configuration,
    ILogger<DailyScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pendingCount = 0;
            try
            {
                var emailConfigured = IsEmailConfigured(configuration);
                if (!emailConfigured)
                    logger.LogWarning("Expiry email reminders are paused until SMTP email configuration is present.");
                var pending = await ScanAndQueueAsync(emailConfigured, stoppingToken);
                pendingCount = pending.Count;
                foreach (var item in pending)
                    await queue.EnqueueAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily parking expiry scan failed. The worker will try again in one hour.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                continue;
            }

            logger.LogInformation("Daily expiry scan queued {NotificationCount} notification(s)", pendingCount);
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    private async Task<List<NotificationQueueMessage>> ScanAndQueueAsync(bool enqueueReminders, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MpsDbContext>();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var throughDate = today.AddDays(3);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await db.ParkingContracts
            .Where(ContractRules.ActiveForExpiry(today))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(contract => contract.Status, ContractStatuses.Expired)
                .SetProperty(contract => contract.UpdatedAtUtc, now), cancellationToken);

        await db.ParkingContracts
            .Where(ContractRules.PendingForActivation(today))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(contract => contract.Status, ContractStatuses.Active)
                .SetProperty(contract => contract.UpdatedAtUtc, now), cancellationToken);

        List<NotificationQueueMessage> queueItems = [];
        if (enqueueReminders)
        {
            queueItems = await db.NotificationLogs.AsNoTracking()
                .Where(log => log.NotificationType == NotificationTypes.ExpiryReminder &&
                              log.Status == NotificationStatuses.Pending &&
                              log.Channel == NotificationChannels.Email &&
                              log.DueDate >= today &&
                              log.DueDate <= throughDate &&
                              log.Contract.Student.EmailAddress != null &&
                              log.Contract.Student.EmailAddress != string.Empty &&
                              log.Contract.Status != ContractStatuses.Cancelled)
                .Select(log => new NotificationQueueMessage(log.NotificationLogId))
                .ToListAsync(cancellationToken);
        }

        var dueContracts = await db.ParkingContracts.AsNoTracking()
            .Where(ContractRules.ReminderDue(today))
            .Select(contract => new { contract.SchoolId, contract.ContractId, contract.StudentId, contract.EndDate })
            .ToListAsync(cancellationToken);
        var dueContractIds = dueContracts.Select(contract => contract.ContractId).ToArray();

        var pendingIds = queueItems.Select(item => item.NotificationLogId).ToHashSet();
        if (enqueueReminders)
        {
            foreach (var contract in dueContracts)
            {
                var existing = await db.NotificationLogs.SingleOrDefaultAsync(log =>
                    log.SchoolId == contract.SchoolId &&
                    log.ContractId == contract.ContractId &&
                    log.NotificationType == NotificationTypes.ExpiryReminder &&
                    log.DueDate == contract.EndDate, cancellationToken);
                if (existing is not null)
                {
                    if (existing.Status == NotificationStatuses.Pending && pendingIds.Add(existing.NotificationLogId))
                        queueItems.Add(new NotificationQueueMessage(existing.NotificationLogId));
                    continue;
                }

                var log = new NotificationLog
                {
                    SchoolId = contract.SchoolId,
                    ContractId = contract.ContractId,
                    StudentId = contract.StudentId,
                    NotificationType = NotificationTypes.ExpiryReminder,
                    Channel = NotificationChannels.Email,
                    Status = NotificationStatuses.Pending,
                    DueDate = contract.EndDate,
                    CreatedAtUtc = now
                };
                db.NotificationLogs.Add(log);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var duePendingIds = dueContractIds.Length == 0
            ? []
            : await db.NotificationLogs.AsNoTracking()
                .Where(log => dueContractIds.Contains(log.ContractId) &&
                              log.NotificationType == NotificationTypes.ExpiryReminder &&
                              log.Status == NotificationStatuses.Pending &&
                              log.Channel == NotificationChannels.Email)
                .Select(log => log.NotificationLogId)
                .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var queued = queueItems.Where(item => item.NotificationLogId != 0).ToList();
        var queuedIds = queued.Select(item => item.NotificationLogId).ToHashSet();
        queued.AddRange(duePendingIds.Where(id => !queuedIds.Contains(id)).Select(id => new NotificationQueueMessage(id)));
        return queued;
    }

    private static bool IsEmailConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:SmtpUsername"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:SmtpPassword"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]);
}
