using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace VumaRetail.Web.Diagnostics;

/// <summary>
/// Masks anything whose property name looks like a secret, anywhere in a log event.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/SECURITY.md</c> §4 requires <c>password</c>, <c>pin</c>, <c>token</c>, <c>secret</c> and
/// <c>certificate</c> to be redacted from logs. The obvious implementation is a rule at each call
/// site — "remember not to log the request body here" — which works until the day somebody adds a
/// diagnostic in a hurry on a Friday. This is the other implementation: the sink refuses to write
/// them regardless of who asked.
/// </para>
/// <para>
/// Matching is on a substring of the property name, case-insensitively, so
/// <c>NewPassword</c>, <c>refresh_token</c> and <c>CertificateThumbprint</c> are all caught without
/// enumerating spellings. It recurses into structures and sequences, because a redacted top-level
/// property is no use if the same value rides along inside a captured object.
/// </para>
/// <para>
/// The cost is over-redaction: a property called <c>TokenCount</c> becomes <c>***</c>. That trade is
/// made deliberately and in this direction — an unhelpful log line is a bad afternoon, a password in
/// a log file that gets emailed to a supplier is a breach notification under POPIA.
/// </para>
/// </remarks>
public sealed class RedactingEnricher : ILogEventEnricher
{
    /// <summary>What replaces a redacted value.</summary>
    public const string Mask = "***";

    /// <summary>The property-name fragments that trigger redaction.</summary>
    public static readonly string[] SensitiveFragments =
    [
        "password",
        "pin",
        "token",
        "secret",
        "certificate",
    ];

    /// <summary>How deep to recurse before giving up. Deeper than any Vuma log payload.</summary>
    private const int MaxDepth = 8;

    /// <summary>Whether a property with this name must be masked.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns><c>true</c> when the name matches a sensitive fragment.</returns>
    public static bool IsSensitive(string propertyName)
        => !string.IsNullOrEmpty(propertyName)
            && Array.Exists(
                SensitiveFragments,
                fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties.ToArray())
        {
            LogEventPropertyValue replacement = IsSensitive(property.Key)
                ? new ScalarValue(Mask)
                : Redact(property.Value, depth: 0);

            if (!ReferenceEquals(replacement, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, replacement));
            }
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value, int depth)
    {
        if (depth >= MaxDepth)
        {
            return value;
        }

        switch (value)
        {
            case StructureValue structure:
            {
                List<LogEventProperty> properties = [];
                bool changed = false;

                foreach (LogEventProperty property in structure.Properties)
                {
                    if (IsSensitive(property.Name))
                    {
                        properties.Add(new LogEventProperty(property.Name, new ScalarValue(Mask)));
                        changed = true;
                        continue;
                    }

                    LogEventPropertyValue redacted = Redact(property.Value, depth + 1);
                    changed |= !ReferenceEquals(redacted, property.Value);
                    properties.Add(new LogEventProperty(property.Name, redacted));
                }

                return changed ? new StructureValue(properties, structure.TypeTag) : structure;
            }

            case DictionaryValue dictionary:
            {
                List<KeyValuePair<ScalarValue, LogEventPropertyValue>> elements = [];
                bool changed = false;

                foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> element in dictionary.Elements)
                {
                    if (element.Key.Value is string key && IsSensitive(key))
                    {
                        elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                            element.Key,
                            new ScalarValue(Mask)));
                        changed = true;
                        continue;
                    }

                    LogEventPropertyValue redacted = Redact(element.Value, depth + 1);
                    changed |= !ReferenceEquals(redacted, element.Value);
                    elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(element.Key, redacted));
                }

                return changed ? new DictionaryValue(elements) : dictionary;
            }

            case SequenceValue sequence:
            {
                LogEventPropertyValue[] elements = [.. sequence.Elements.Select(element => Redact(element, depth + 1))];
                bool changed = elements
                    .Where((element, index) => !ReferenceEquals(element, sequence.Elements[index]))
                    .Any();

                return changed ? new SequenceValue(elements) : sequence;
            }

            default:
                return value;
        }
    }
}

/// <summary>
/// Where logs and traces go once they leave the machine (<c>CLAUDE.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// Off unless <see cref="Endpoint"/> is set. A store server on a shop's back-office PC must not start
/// shipping its logs somewhere because a default said so — R10 caps what may ever leave a tenant's
/// premises for vendor purposes at counts and health, and application logs are neither.
/// </para>
/// <para>
/// The tenant's <em>own</em> collector is the intended destination, which is why this is
/// configuration rather than a vendor endpoint baked in. Vendor telemetry is Stage 30b's metering
/// payload, is a whitelist of aggregate counters, and does not travel this pipe.
/// </para>
/// </remarks>
public sealed class OpenTelemetryOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Telemetry:Otlp";

    /// <summary>The OTLP HTTP endpoint, or empty to send nothing anywhere.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Headers the collector needs, typically an API key. A secret; never committed.</summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.Ordinal);

    /// <summary>True when an endpoint is configured, so the sink is added.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}

/// <summary>Configures Serilog the same way in every Vuma host.</summary>
public static class VumaLogging
{
    /// <summary>
    /// Sends logs to the console and to a rolling daily file, with secrets redacted
    /// (<c>CLAUDE.md</c> §4, <c>docs/SECURITY.md</c> §4).
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="applicationName">What to stamp on every event, so two hosts are separable.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The file lives under <c>logs/</c> beside the application and keeps 31 days. A store server
    /// runs on a shop's back-office PC with a disk nobody monitors, so an unbounded log is a
    /// support call that starts "it stopped working" and ends in a full C: drive.
    /// </para>
    /// <para>
    /// Stage 04 adds the OTLP sink <c>CLAUDE.md</c> §4 names, and it is off unless an endpoint is
    /// configured. That default is not laziness: telemetry leaving a tenant's premises is a POPIA
    /// question and R10 caps what may ever leave at counts and health, so it is an explicit act of
    /// configuration rather than something a host does because it was built.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddVumaLogging(
        this IHostApplicationBuilder builder,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();

        OpenTelemetryOptions telemetry = builder.Configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        LoggerConfiguration configuration = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.With(new RedactingEnricher())
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // EF Core logs every statement it executes at Information. On a till doing ten sales a
            // minute that is the log, and the one line anybody wants is buried in it. Parameter
            // values are already withheld (sensitive data logging is off), so this is about volume.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "vuma-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                shared: true);

        if (telemetry.IsConfigured)
        {
            // The same RedactingEnricher runs before this sink as before the file, so a secret is
            // masked once, centrally, on its way to every destination — including the one that
            // leaves the building.
            configuration = configuration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = telemetry.Endpoint;
                options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf;
                options.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["service.name"] = applicationName,
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                };

                foreach (KeyValuePair<string, string> header in telemetry.Headers)
                {
                    options.Headers[header.Key] = header.Value;
                }
            });
        }

        Log.Logger = configuration.CreateLogger();

        // Services, not Logging: this overload also registers Serilog's DiagnosticContext, which
        // UseSerilogRequestLogging needs. Registering only the logging provider builds fine and then
        // fails on the first request, which is a poor place to find out.
        builder.Services.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>Adds Serilog's one-line-per-request logging.</summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IApplicationBuilder UseVumaRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseSerilogRequestLogging(options =>
            // The default template repeats the path, which for /api/v1/auth/token is fine and for a
            // future route with an id in it would put that id in every line. Keep it, but say which
            // version of the API answered.
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
    }
}
