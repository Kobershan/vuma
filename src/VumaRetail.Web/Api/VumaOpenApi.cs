using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
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
                    && RequestExamples.TryGetValue(path, out JsonNode? example)
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
    private static readonly Dictionary<string, JsonNode> RequestExamples = new(StringComparer.Ordinal)
    {
        ["api/v1/auth/token"] = new JsonObject
        {
            ["userName"] = JsonValue.Create("nmokoena"),
            ["password"] = JsonValue.Create("CorrectHorseBattery1"),
        },
        ["api/v1/auth/pin"] = new JsonObject
        {
            ["terminalId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000001"),
            ["pin"] = JsonValue.Create("1174"),
        },
        ["api/v1/auth/refresh"] = new JsonObject
        {
            ["refreshToken"] = JsonValue.Create("s6Bh…never-logged…Qq1"),
        },
        ["api/v1/auth/terminal/activate"] = new JsonObject
        {
            ["storeId"] = JsonValue.Create("01926f2c-0000-7000-8000-0000000000aa"),
            ["enrolmentCode"] = JsonValue.Create("K7QF-2M9X-VD4T"),
            ["certificateThumbprint"] = JsonValue.Create(new string('A', 64)),
            ["deviceFingerprint"] = JsonValue.Create("board-serial-1"),
        },
        ["api/v1/pos/till-sessions"] = new JsonObject
        {
            ["openingFloat"] = JsonValue.Create(500d),
            ["currency"] = JsonValue.Create("ZAR"),
        },
        ["api/v1/pos/till-sessions/{tillSessionId}/close"] = new JsonObject
        {
            ["countedCash"] = JsonValue.Create(1250.50d),
            ["currency"] = JsonValue.Create("ZAR"),
            ["note"] = JsonValue.Create("Drawer counted at shift close"),
        },
        ["api/v1/pos/sales"] = new JsonObject
        {
            ["saleId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000010"),
            ["locationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000011"),
            ["customerId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000012"),
        },
        ["api/v1/pos/sales/{saleId}/lines"] = new JsonObject
        {
            ["itemId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000020"),
            ["itemVariantId"] = JsonNullSentinel.JsonNull,
            ["quantity"] = JsonValue.Create(2),
            ["unitOfMeasure"] = JsonValue.Create("EA"),
            ["unitPrice"] = JsonValue.Create(59.99d),
            ["currency"] = JsonValue.Create("ZAR"),
            ["discountAmount"] = JsonValue.Create(0),
            ["saleLineId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000021"),
        },
        ["api/v1/pos/sales/{saleId}/tenders"] = new JsonObject
        {
            ["tenderType"] = JsonValue.Create("Cash"),
            ["amount"] = JsonValue.Create(120),
            ["currency"] = JsonValue.Create("ZAR"),
            ["reference"] = JsonValue.Create("DRAWER-1"),
            ["saleTenderId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000022"),
        },
        ["api/v1/pos/sales/{saleId}/void"] = new JsonObject
        {
            ["reason"] = JsonValue.Create("Customer cancelled before payment"),
        },
        ["api/v1/pos/sales/{saleId}/receipt/prints"] = new JsonObject
        {
            ["reason"] = JsonValue.Create("Customer requested a duplicate receipt"),
            ["receiptPrintId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000023"),
        },
        ["api/v1/trading-sessions"] = new JsonObject
        {
            ["premisesId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000030"),
            ["terminalId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000031"),
            ["cashierUserId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000032"),
            ["sessionCompanyId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000033"),
            ["currency"] = JsonValue.Create("ZAR"),
            ["idempotencyKey"] = JsonValue.Create("till-20260911-0001"),
            ["customerGroupPartnerId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000034"),
        },
        ["api/v1/trading-sessions/{sessionId}/lines"] = new JsonObject
        {
            ["barcode"] = JsonValue.Create("6009880999999"),
            ["quantityValue"] = JsonValue.Create(1),
            ["quantityUom"] = JsonValue.Create("EA"),
            ["unitPriceAmount"] = JsonValue.Create(49.99d),
            ["currency"] = JsonValue.Create("ZAR"),
            ["discountAmount"] = JsonValue.Create(0),
            ["taxCode"] = JsonValue.Create("STANDARD"),
            ["lineId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000035"),
        },
        ["api/v1/trading-sessions/{sessionId}/tender"] = new JsonObject
        {
            ["tenderType"] = JsonValue.Create("Cash"),
            ["amount"] = JsonValue.Create(49.99d),
            ["currency"] = JsonValue.Create("ZAR"),
            ["reference"] = JsonValue.Create("DRAWER-2"),
        },
        ["api/v1/trading-sessions/{sessionId}/tender/allocations"] = new JsonObject
        {
            ["allocations"] = new JsonArray
            {
                new JsonObject
                {
                    ["companyId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000033"),
                    ["amount"] = JsonValue.Create(49.99d),
                    ["currency"] = JsonValue.Create("ZAR"),
                },
            },
        },
        ["api/v1/trading-sessions/{sessionId}/void"] = new JsonObject
        {
            ["reason"] = JsonValue.Create("Customer cancelled before completion"),
        },
        ["api/v1/trading-sessions/{sessionId}/returns"] = new JsonObject
        {
            ["companyId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000033"),
            ["invoiceNumber"] = JsonValue.Create("INV-20260911-0001"),
            ["reason"] = JsonValue.Create("Damaged item"),
            ["lines"] = new JsonArray
            {
                new JsonObject
                {
                    ["sessionLineId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000035"),
                    ["quantityValue"] = JsonValue.Create(1),
                },
            },
        },
        ["api/v1/manufacturing/boms"] = new JsonObject
        {
            ["companyId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000040"),
            ["finishedItemId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000041"),
            ["finishedVariantId"] = JsonNullSentinel.JsonNull,
            ["version"] = JsonValue.Create(1),
            ["name"] = JsonValue.Create("Starter assembly"),
            ["lines"] = new JsonArray
            {
                new JsonObject
                {
                    ["componentItemId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000042"),
                    ["componentVariantId"] = JsonNullSentinel.JsonNull,
                    ["quantity"] = JsonValue.Create(2),
                    ["unitOfMeasure"] = JsonValue.Create("EA"),
                    ["scrapPercent"] = JsonValue.Create(2.5),
                },
            },
        },
        ["api/v1/manufacturing/production-orders"] = new JsonObject
        {
            ["operationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000050"),
            ["companyId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000040"),
            ["finishedItemId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000041"),
            ["quantity"] = JsonValue.Create(10),
            ["unitOfMeasure"] = JsonValue.Create("EA"),
            ["orderNumber"] = JsonValue.Create("PROD-2026-0001"),
            ["billOfMaterialsId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000043"),
        },
        ["api/v1/manufacturing/production-orders/{id}/release"] = new JsonObject
        {
            ["operationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000051"),
            ["billOfMaterialsId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000043"),
        },
        ["api/v1/manufacturing/production-orders/{id}/issues"] = new JsonObject
        {
            ["locationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000044"),
            ["operationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000052"),
            ["componentItemId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000042"),
            ["quantity"] = JsonValue.Create(20),
            ["unitOfMeasure"] = JsonValue.Create("EA"),
            ["unitCost"] = JsonValue.Create(12.5),
            ["currency"] = JsonValue.Create("ZAR"),
        },
        ["api/v1/manufacturing/production-orders/{id}/receipts"] = new JsonObject
        {
            ["locationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000044"),
            ["operationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000053"),
            ["quantity"] = JsonValue.Create(9),
            ["unitOfMeasure"] = JsonValue.Create("EA"),
            ["unitCost"] = JsonValue.Create(27.7778),
            ["currency"] = JsonValue.Create("ZAR"),
        },
        ["api/v1/manufacturing/production-orders/{id}/scrap"] = new JsonObject
        {
            ["operationId"] = JsonValue.Create("01926f2c-0000-7000-8000-000000000054"),
            ["quantity"] = JsonValue.Create(1),
            ["unitOfMeasure"] = JsonValue.Create("EA"),
            ["unitCost"] = JsonValue.Create(27.7778),
            ["currency"] = JsonValue.Create("ZAR"),
        },
    };

    private static OpenApiResponse ProblemResponse(string status, string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            ["application/problem+json"] = new OpenApiMediaType
            {
                Example = new JsonObject
                {
                    ["status"] = JsonValue.Create(int.Parse(status, System.Globalization.CultureInfo.InvariantCulture)),
                    ["title"] = JsonValue.Create(description),
                    ["code"] = JsonValue.Create(ExampleCodeFor(status)),
                    ["correlationId"] = JsonValue.Create("0192a1b2c3d47e8f9a0b1c2d3e4f5a6b"),
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
