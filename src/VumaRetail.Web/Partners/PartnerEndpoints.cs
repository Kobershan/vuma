using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Partners.Commands;
using VumaRetail.Application.Partners.Permissions;
using VumaRetail.Application.Partners.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Partners;
using VumaRetail.Contracts.Platform;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Partners;

/// <summary>The <c>partners</c> module's endpoints: suppliers, customers, and partners who are both.</summary>
public static class PartnerEndpoints
{
    /// <summary>Maps the partner endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaPartners(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder partners = endpoints.MapVumaApi().MapGroup("/partners").WithTags("Partners");

        partners.MapGet("/", ListPartnersAsync)
            .RequirePermission(PartnerPermissions.PartnerView)
            .Produces<PageResponse<PartnerResponse>>()
            .WithSummary("Partners, a keyset-paginated page at a time (docs/API_STANDARDS.md §8).");

        partners.MapGet("/{partnerId:guid}", GetPartnerAsync)
            .RequirePermission(PartnerPermissions.PartnerView)
            .Produces<PartnerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one partner by id.");

        partners.MapPost("/", CreatePartnerAsync)
            .RequirePermission(PartnerPermissions.PartnerCreate)
            .Produces<PartnerIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a supplier, a customer, or both.");

        partners.MapPut("/{partnerId:guid}", UpdatePartnerAsync)
            .RequirePermission(PartnerPermissions.PartnerUpdate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Updates a partner's trading details.");

        partners.MapPost("/{partnerId:guid}/deactivate", DeactivatePartnerAsync)
            .RequirePermission(PartnerPermissions.PartnerDeactivate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a partner from new trading. History is retained; nothing is deleted.");

        return endpoints;
    }

    private static async Task<IResult> ListPartnersAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int? limit = null,
        string? after = null)
    {
        PageResult<PartnerResult> page = await dispatcher
            .QueryAsync(new ListPartnersQuery(limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<PartnerResponse>(
            [.. page.Items.Select(ToResponse)],
            page.NextCursor,
            page.HasMore));
    }

    private static async Task<IResult> GetPartnerAsync(Guid partnerId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        PartnerResult partner = await dispatcher
            .QueryAsync(new GetPartnerQuery(partnerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(partner));
    }

    private static async Task<IResult> CreatePartnerAsync(
        CreatePartnerRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        PartnerType type = ParsePartnerType(request.Type);

        Guid id = await dispatcher
            .SendAsync(
                new CreatePartnerCommand(
                    request.Code,
                    request.Name,
                    type,
                    ToAddress(request.Address),
                    request.Email,
                    request.Phone,
                    request.TaxNumber),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/partners/{id}", new PartnerIdResponse(id));
    }

    private static async Task<IResult> UpdatePartnerAsync(
        Guid partnerId,
        UpdatePartnerDetailsRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new UpdatePartnerDetailsCommand(
                    partnerId,
                    request.Name,
                    ToAddress(request.Address),
                    request.Email,
                    request.Phone,
                    request.TaxNumber),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeactivatePartnerAsync(Guid partnerId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DeactivatePartnerCommand(partnerId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static PartnerResponse ToResponse(PartnerResult partner) => new(
        partner.Id,
        partner.Code,
        partner.Name,
        partner.Type.ToString(),
        ToDto(partner.Address),
        partner.Email,
        partner.Phone,
        partner.TaxNumber,
        partner.IsActive);

    private static AddressDto? ToDto(Address? address)
        => address is null
            ? null
            : new AddressDto(address.Line1, address.City, address.CountryCode, address.Line2, address.Region, address.PostalCode);

    private static Address? ToAddress(AddressDto? dto)
        => dto is null
            ? null
            : Address.Create(dto.Line1, dto.City, dto.CountryCode, dto.Line2, dto.Region, dto.PostalCode);

    /// <summary>
    /// Parses a comma-separated <c>PartnerType</c> flags combination, for example <c>"Customer, Supplier"</c>.
    /// </summary>
    private static PartnerType ParsePartnerType(string value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out PartnerType parsed) && parsed != PartnerType.None)
        {
            return parsed;
        }

        throw new ValidationFailedException(
            nameof(CreatePartnerCommand),
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["type"] = [$"'{value}' must be 'Customer', 'Supplier', or 'Customer, Supplier'."],
            });
    }
}
