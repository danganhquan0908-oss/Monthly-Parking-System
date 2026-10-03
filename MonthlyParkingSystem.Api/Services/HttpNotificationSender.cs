using System.Net.Http.Json;
using System.Text.Json;

namespace MonthlyParkingSystem.Api.Services;

public sealed class HttpNotificationSender(HttpClient httpClient, IConfiguration configuration, ILogger<HttpNotificationSender> logger)
    : INotificationSender
{
    public async Task<string?> SendAsync(string channel, string destination, string message, long notificationLogId, CancellationToken cancellationToken)
    {
        return await SendMessageAsync(channel, destination, message, notificationLogId.ToString(), cancellationToken);
    }

    private async Task<string?> SendMessageAsync(string channel, string destination, string message, string clientReference, CancellationToken cancellationToken)
    {
        var endpoint = configuration["MPS_NOTIFICATION_ENDPOINT"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("MPS_NOTIFICATION_ENDPOINT must be configured as an HTTPS URL (HTTP is permitted for loopback development gateways).");

        // Do not log the destination or message body; they contain personal information.
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new NotificationGatewayRequest(channel, destination, message, clientReference))
        };
        var token = configuration["MPS_NOTIFICATION_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        logger.LogInformation("Sending {Channel} notification with client reference {ClientReference}", channel, clientReference);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is 0) return null;
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in new[] { "messageId", "id" })
                if (document.RootElement.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String)
                    return id.GetString();
        }
        catch (JsonException)
        {
            // A successful gateway may return a plain-text acknowledgement.
        }
        return null;
    }

    private sealed record NotificationGatewayRequest(string Channel, string To, string Message, string ClientReference);
}
