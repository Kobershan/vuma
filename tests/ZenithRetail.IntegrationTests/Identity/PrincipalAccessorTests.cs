using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Domain.Identity;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Infrastructure.Security.Identity;
using ZenithRetail.IntegrationTests.Harness;
using ZenithRetail.Web.Identity;

namespace ZenithRetail.IntegrationTests.Identity;

/// <summary>
/// The authenticated <see cref="IPrincipalAccessor"/> — what closes R6's gap.
/// </summary>
/// <remarks>
/// Stage 01 stamped <c>system:host</c> on every row, which makes the audit trail a log of the process
/// rather than of a person. These assert the replacement both in isolation and, more importantly,
/// against a real database: what actually lands in <c>created_by</c>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PrincipalAccessorTests(PostgresFixture fixture)
{
    private static readonly Guid UserId = Guid.Parse("01900000-0000-7000-8000-000000000101");
    private static readonly Guid TerminalId = Guid.Parse("01900000-0000-7000-8000-000000000102");

    private static HttpContextPrincipalAccessor AccessorFor(params Claim[] claims)
    {
        DefaultHttpContext context = new();

        if (claims.Length > 0)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return new HttpContextPrincipalAccessor(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Reports_the_user_id_of_an_authenticated_caller()
    {
        HttpContextPrincipalAccessor accessor = AccessorFor(new Claim("sub", UserId.ToString()));

        accessor.Principal.Should().Be($"user:{UserId}");
        accessor.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void Reports_the_terminal_when_a_till_authenticated_with_its_certificate()
    {
        HttpContextPrincipalAccessor accessor = AccessorFor(
            new Claim(ZenithClaims.TerminalId, TerminalId.ToString()));

        accessor.Principal.Should().Be($"terminal:{TerminalId}");
        accessor.TerminalId.Should().Be(TerminalId);
    }

    [Fact]
    public void Carries_the_terminal_alongside_the_user_on_a_POS_session()
    {
        // R6 asks who changed what, when, and from which terminal. A POS row needs both.
        HttpContextPrincipalAccessor accessor = AccessorFor(
            new Claim("sub", UserId.ToString()),
            new Claim(ZenithClaims.TerminalId, TerminalId.ToString()));

        accessor.Principal.Should().Be($"user:{UserId}");
        accessor.TerminalId.Should().Be(TerminalId);
    }

    [Fact]
    public void Falls_back_to_a_named_system_principal_outside_a_request()
    {
        // Not "unknown" and not empty: a row nobody can be held to is a hole in the audit trail that
        // looks like data.
        HttpContextPrincipalAccessor accessor = new(new HttpContextAccessor(), "seed");

        accessor.Principal.Should().Be("system:seed");
        accessor.IsSystem.Should().BeTrue();
        accessor.TerminalId.Should().BeNull();
    }

    [Fact]
    public void Ignores_a_terminal_claim_that_is_not_a_guid()
    {
        AccessorFor(new Claim(ZenithClaims.TerminalId, "not-a-guid")).TerminalId.Should().BeNull();
    }

    [Fact]
    public async Task Writes_the_signed_in_user_into_created_by_on_a_real_row()
    {
        // The whole point of the stage, asserted against PostgreSQL rather than against the accessor.
        string connectionString = await fixture.CreateDatabaseAsync();

        DefaultHttpContext http = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
        };

        HttpContextPrincipalAccessor accessor = new(new HttpContextAccessor { HttpContext = http });
        TestTenantContext tenant = TestTenantContext.Unfiltered();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(
            connectionString, new TestClock(), accessor, tenant);

        Tenant seeded = Tenant.CreateWithSouthAfricanDefaults("Audited Retail (Pty) Ltd", "Audited");
        context.Tenants.Add(seeded);
        await context.CommitAsync();

        context.Users.Add(User.Create(seeded.Id, "nmokoena", "Naledi Mokoena"));
        await context.CommitAsync();

        User written = await context.Users.SingleAsync();

        written.CreatedBy.Should().Be($"user:{UserId}");
        written.UpdatedBy.Should().Be($"user:{UserId}");

        AuditEntry entry = await context.AuditEntries
            .SingleAsync(audit => audit.EntityId == written.Id);

        entry.Principal.Should().Be($"user:{UserId}");
        entry.IsSystemAction.Should().BeFalse();
    }
}
