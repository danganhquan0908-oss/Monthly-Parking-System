namespace MonthlyParkingSystem.Api.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string destination, string subject, string message, string clientReference, CancellationToken cancellationToken);
}
