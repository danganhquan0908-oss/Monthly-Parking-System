using System.Net;
using System.Net.Mail;
using System.Text;

namespace MonthlyParkingSystem.Api.Services;

public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendEmailAsync(
        string destination,
        string subject,
        string message,
        string clientReference,
        CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var username = configuration["Email:SmtpUsername"];
        var password = configuration["Email:SmtpPassword"];
        var fromAddress = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException(
                "Email SMTP settings are incomplete. Configure Email:SmtpHost, SmtpUsername, SmtpPassword, and FromAddress.");

        var port = configuration.GetValue<int?>("Email:SmtpPort") ?? 587;
        var timeoutSeconds = configuration.GetValue<int?>("Email:SmtpTimeoutSeconds") ?? 20;
        if (port is < 1 or > 65535) throw new InvalidOperationException("Email:SmtpPort must be between 1 and 65535.");

        var displayName = configuration["Email:FromName"] ?? "MPS";
        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress, displayName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(destination));

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = Math.Clamp(timeoutSeconds, 1, 120) * 1000
        };

        // Avoid logging the recipient or message body; both contain personal data and a one-time code.
        logger.LogInformation("Sending registration email with client reference {ClientReference}", clientReference);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
