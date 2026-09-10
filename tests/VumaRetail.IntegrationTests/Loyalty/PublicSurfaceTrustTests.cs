using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VumaRetail.PublicApi.Loyalty;

namespace VumaRetail.IntegrationTests.Loyalty;

/// <summary>
/// The public surface's trust boundaries: the neutral read-only answer carries no billing
/// signal, and webhook signatures verify exactly (Stage 20, TESTING.md §7).
/// </summary>
public sealed class PublicSurfaceTrustTests
{
    [Fact]
    public async Task Read_only_answer_is_503_with_no_billing_words()
    {
        IResult result = LoyaltyEndpoints.ReadOnlyProblem();

        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection()
            .AddProblemDetails()
            .AddLogging()
            .BuildServiceProvider();
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

        http.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        string text = document.RootElement.GetRawText().ToLowerInvariant();

        foreach (string banned in new[] { "subscription", "licence", "license", "lapsed", "billing", "payment" })
        {
            text.Should().NotContain(banned, $"the neutral answer must never leak '{banned}'");
        }

        text.Should().Contain("loyalty_temporarily_unavailable");
    }

    [Fact]
    public void Webhook_signature_verifies_and_rejects()
    {
        var http = new DefaultHttpContext();
        const string secret = "test-secret";
        const string body = """{"companyId":"00000000-0000-0000-0000-000000000000"}""";

        string good;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            good = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        }

        http.Request.Headers["X-Orbit-Signature"] = good;
        LoyaltyEndpoints.VerifySignature(http, body, secret, NullLoggerFactory.Instance)
            .Should().BeTrue();

        var tampered = new DefaultHttpContext();
        tampered.Request.Headers["X-Orbit-Signature"] = good;
        LoyaltyEndpoints.VerifySignature(tampered, body + " ", secret, NullLoggerFactory.Instance)
            .Should().BeFalse("a tampered body must not verify");

        var unsigned = new DefaultHttpContext();
        LoyaltyEndpoints.VerifySignature(unsigned, body, secret, NullLoggerFactory.Instance)
            .Should().BeFalse("a missing signature must not verify");
    }

    [Fact]
    public void Empty_secret_accepts_for_dev()
    {
        var http = new DefaultHttpContext();
        LoyaltyEndpoints.VerifySignature(http, "{}", string.Empty, NullLoggerFactory.Instance)
            .Should().BeTrue("dev hosts configure no secret; acceptance is logged, not silent");
    }
}
