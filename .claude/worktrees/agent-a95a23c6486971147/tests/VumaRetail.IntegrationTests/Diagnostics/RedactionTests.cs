using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using VumaRetail.Web.Diagnostics;

namespace VumaRetail.IntegrationTests.Diagnostics;

/// <summary>
/// Nothing sensitive reaches a log file (<c>docs/SECURITY.md</c> §4).
/// </summary>
/// <remarks>
/// The store server writes its log to a shop's back-office PC, where it is routinely emailed to
/// whoever is helping. A password in it is a POPIA notification, so redaction is a property of the
/// sink rather than a discipline at the call site.
/// </remarks>
public sealed class RedactionTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("NewPassword")]
    [InlineData("pin")]
    [InlineData("UserPin")]
    [InlineData("RefreshToken")]
    [InlineData("access_token")]
    [InlineData("ClientSecret")]
    [InlineData("CertificateThumbprint")]
    public void Masks_a_property_whose_name_looks_like_a_secret(string propertyName)
    {
        RedactingEnricher.IsSensitive(propertyName).Should().BeTrue();
    }

    [Theory]
    [InlineData("UserName")]
    [InlineData("StoreId")]
    [InlineData("TerminalCode")]
    [InlineData("CorrelationId")]
    public void Leaves_an_ordinary_property_alone(string propertyName)
    {
        RedactingEnricher.IsSensitive(propertyName).Should().BeFalse();
    }

    [Fact]
    public void Masks_a_top_level_message_argument()
    {
        CapturingSink sink = Log("Signing in {UserName} with {Password}", "nmokoena", "CorrectHorseBattery1");

        sink.Rendered().Should().NotContain("CorrectHorseBattery1");
        sink.Property("Password").Should().Be(RedactingEnricher.Mask);
        sink.Property("UserName").Should().Be("nmokoena");
    }

    [Fact]
    public void Masks_a_secret_nested_inside_a_captured_object()
    {
        // The failure this catches: somebody logs the whole request object rather than a field of it,
        // and a top-level-only redaction happily writes the password one level down.
        CapturingSink sink = Log(
            "Handling {@Request}",
            new { UserName = "nmokoena", Password = "CorrectHorseBattery1", Terminal = new { Pin = "1174" } });

        string rendered = sink.Rendered();

        rendered.Should().NotContain("CorrectHorseBattery1");
        rendered.Should().NotContain("1174");
        rendered.Should().Contain("nmokoena");
    }

    [Fact]
    public void Masks_a_secret_inside_a_dictionary()
    {
        CapturingSink sink = Log(
            "Handling {@Scope}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Message"] = "CreateUserCommand",
                ["Password"] = "CorrectHorseBattery1",
            });

        sink.Rendered().Should().NotContain("CorrectHorseBattery1");
    }

    private static CapturingSink Log(string template, params object?[] arguments)
    {
        CapturingSink sink = new();

        using Logger logger = new LoggerConfiguration()
            .Enrich.With(new RedactingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        using SerilogLoggerFactory factory = new(logger);

        factory.CreateLogger("test").LogInformation(template, arguments);

        return sink;
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private LogEvent? _event;

        public void Emit(LogEvent logEvent) => _event = logEvent;

        public string Rendered()
        {
            using StringWriter writer = new();
            _event!.RenderMessage(writer);

            foreach (KeyValuePair<string, LogEventPropertyValue> property in _event.Properties)
            {
                writer.Write(' ');
                property.Value.Render(writer);
            }

            return writer.ToString();
        }

        public string? Property(string name)
            => _event!.Properties.TryGetValue(name, out LogEventPropertyValue? value)
                && value is ScalarValue { Value: string text }
                    ? text
                    : null;
    }
}
