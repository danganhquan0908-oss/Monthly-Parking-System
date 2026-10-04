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
        CancellationToken cancellationToken,
        string? htmlBody = null)
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

        var displayName = configuration["Email:FromName"] ?? "MPS · Residence";
        var formattedHtml = htmlBody ?? MpsEmailTemplates.BuildGenericEmail(subject, message);

        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress, displayName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = formattedHtml,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        mail.To.Add(new MailAddress(destination));
        mail.ReplyToList.Add(new MailAddress(fromAddress, displayName));
        mail.Headers.Add("Auto-Submitted", "auto-generated");
        mail.Headers.Add("X-Auto-Response-Suppress", "All");

        // Cung cấp định dạng plain text và HTML kèm logo nhúng trực tiếp (CID)
        var plainView = AlternateView.CreateAlternateViewFromString(message, Encoding.UTF8, "text/plain");
        mail.AlternateViews.Add(plainView);

        var htmlView = AlternateView.CreateAlternateViewFromString(formattedHtml, Encoding.UTF8, "text/html");
        var logoPath = ResolveLogoPath();
        if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
        {
            var logoResource = new LinkedResource(logoPath, "image/png")
            {
                ContentId = "mps-logo",
                TransferEncoding = System.Net.Mime.TransferEncoding.Base64
            };
            htmlView.LinkedResources.Add(logoResource);
        }
        mail.AlternateViews.Add(htmlView);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = Math.Clamp(timeoutSeconds, 1, 120) * 1000
        };

        // Avoid logging the recipient or message body; both contain personal data and a one-time code.
        logger.LogInformation("Sending MPS email with client reference {ClientReference}", clientReference);
        await client.SendMailAsync(mail, cancellationToken);
    }

    private static string? ResolveLogoPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            Path.Combine(AppContext.BaseDirectory, "Frontend", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "MonthlyParkingSystem.Web", "logo.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MonthlyParkingSystem.Web", "logo.png")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
