using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using VumaRetail.Contracts;

namespace VumaRetail.Web.Api;

/// <summary>
/// The OpenAPI document, served at <c>/openapi/v1.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §8 requires every endpoint to appear in <c>/openapi/v1.json</c> "with examples
/// and error responses". The examples are per endpoint; the error responses are not — every endpoint
/// in the system can return the same seven, and writing them out forty times would guarantee that
/// the fortieth is missing one.
/// </para>
/// <para>
/// Built on the OpenAPI generator that ships with ASP.NET Core 9 rather than Swashbuckle or NSwag:
/// one fewer third-party dependency on the surface a customer integrates against.
/// </para>
/// </remarks>
public static class VumaOpenApi
{
    /// <summary>The name of the bearer security scheme, referenced by every protected operation.</summary>
    public const string BearerScheme = "bearer";

    /// <summary>Registers the OpenAPI document for the current API version.</summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(VumaApi.VersionLabel(VumaApi.CurrentVersion), options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Vuma Retail — internal API",
                    Version = VumaApi.VersionLabel(VumaApi.CurrentVersion),
                    Description =
                        "The versioned REST API behind every Vuma capability (ADR-008). Errors are "
                        + "RFC 7807 problem documents carrying a stable machine-readable `code`; branch "
                        + "on the code, never on the message. See docs/API_STANDARDS.md.",
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "A 15-minute access token from POST /api/v1/auth/token.",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                foreach ((string status, string description) in StandardErrors)
                {
                    operation.Responses ??= [];
                    operation.Responses.TryAdd(status, ProblemResponse(status, description));
                }

                if (context.Description.RelativePath is { } path
                    && RequestExamples.TryGetValue(path, out IOpenApiAny? example)
                    && operation.RequestBody?.Content.TryGetValue("application/json", out OpenApiMediaType? media) == true)
                {
                    media.Example = example;
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>Serves the document.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    public static WebApplication UseVumaOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

        return app;
    }

    private static readonly (string Status, string Description)[] StandardErrors =
    [
        ("400", "The request is malformed. `errors` names the offending properties."),
        ("401", "No usable credential was presented."),
        ("403", "Authenticated, but without the permission this endpoint requires."),
        ("404", "No such resource — or none this tenant can see, which is the same answer."),
        ("409", "Something that must be unique already is not."),
        ("422", "A business rule refused a well-formed request."),
        ("500", "Something failed on the server. Quote `correlationId` when reporting it."),
    ];

    /// <summary>
    /// Request examples, keyed by route template. Kept here rather than on each endpoint because the
    /// built-in generator has no per-endpoint example hook that survives a route group.
    /// </summary>
    private static readonly Dictionary<string, IOpenApiAny> RequestExamples = new(StringComparer.Ordinal)
    {
        ["api/v1/auth/token"] = new OpenApiObject
        {
            ["userName"] = new OpenApiString("nmokoena"),
            ["password"] = new OpenApiString("CorrectHorseBattery1"),
        },
        ["api/v1/auth/pin"] = new OpenApiObject
        {
            ["terminalId"] = new OpenApiString("01926f2c-0000-7000-8000-000000000001"),
            ["pin"] = new OpenApiString("1174"),
        },
        ["api/v1/auth/refresh"] = new OpenApiObject
        {
            ["refreshToken"] = new OpenApiString("s6Bh…never-logged…Qq1"),
        },
        ["api/v1/auth/terminal/activate"] = new OpenApiObject
        {
            ["storeId"] = new OpenApiString("01926f2c-0000-7000-8000-0000000000aa"),
            ["enrolmentCode"] = new OpenApiString("K7QF-2M9X-VD4T"),
            ["certificateThumbprint"] = new OpenApiString(new string('A', 64)),
            ["deviceFingerprint"] = new OpenApiString("board-serial-1"),
        },
    };

    private static OpenApiResponse ProblemResponse(string status, string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            ["application/problem+json"] = new OpenApiMediaType
            {
                Example = new OpenApiObject
                {
                    ["status"] = new OpenApiInteger(int.Parse(status, System.Globalization.CultureInfo.InvariantCulture)),
                    ["title"] = new OpenApiString(description),
                    ["code"] = new OpenApiString(ExampleCodeFor(status)),
                    ["correlationId"] = new OpenApiString("0192a1b2c3d47e8f9a0b1c2d3e4f5a6b"),
                },
            },
        },
    };

    private static string ExampleCodeFor(string status) => status switch
    {
        "400" => ApiErrorCodes.ValidationFailed,
        "401" => ApiErrorCodes.Unauthenticated,
        "403" => ApiErrorCodes.Forbidden,
        "404" => ApiErrorCodes.NotFound,
        "409" => ApiErrorCodes.Conflict,
        "422" => ApiErrorCodes.RuleViolation,
        _ => ApiErrorCodes.InternalError,
    };
}
