using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ConversationApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WhatsApp_webhook_rejects_invalid_signature_without_processing()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        const string body = "{\"channel\":0,\"address\":\"+27820000000\",\"text\":\"my statement\",\"messageId\":\"msg-invalid\"}";
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/conversations/webhook/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Vuma-Signature", "sha256=00");

        HttpResponseMessage response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhatsApp_webhook_gives_an_unbound_sender_onboarding_only()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        const string body = "{\"channel\":0,\"address\":\"+27820000001\",\"text\":\"send my statement\",\"messageId\":\"msg-unbound\"}";
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/conversations/webhook/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes("test-conversation-webhook-secret"));
        request.Headers.Add("X-Vuma-Signature", $"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()}");

        HttpResponseMessage response = await harness.Client.SendAsync(request);
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        result.GetProperty("state").GetString().Should().Be("onboarding");
        result.GetProperty("message").GetString().Should().NotContain("statement");
    }
}
