using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Conversations;

namespace VumaRetail.Infrastructure.Conversations;

public sealed class TwilioWhatsAppOptions
{
    public const string SectionName = "Vuma:Twilio";
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.twilio.com";
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(From);
}

public sealed class TwilioWhatsAppSender(HttpClient httpClient, IOptions<TwilioWhatsAppOptions> options) : IWhatsAppSender
{
    public async Task SendAsync(string destination, string body, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) throw new InvalidOperationException("Twilio WhatsApp is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.ApiBaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{options.Value.AccountSid}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.AccountSid}:{options.Value.AuthToken}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["From"] = Prefix(options.Value.From), ["To"] = Prefix(destination), ["Body"] = body });
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Twilio WhatsApp returned {(int)response.StatusCode}.");
    }
    private static string Prefix(string value) => value.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? value : $"whatsapp:{value}";
}
