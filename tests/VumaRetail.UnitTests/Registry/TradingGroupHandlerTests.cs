using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>
/// Stage 06e handlers against mocked ports: each handler resolves its ambient context, calls its
/// service once with the right identities, and returns the created id.
/// </summary>
public sealed class TradingGroupHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Suspend_resume_and_revoke_forward_link_reason_and_id()
    {
        var links = Substitute.For<ICompanyLinkService>();
        Guid linkId = UuidV7.NewGuid();

        await new SuspendCompanyLinkCommandHandler(links).HandleAsync(new SuspendCompanyLinkCommand(linkId, "Dispute under review."));
        await new ResumeCompanyLinkCommandHandler(links).HandleAsync(new ResumeCompanyLinkCommand(linkId));
        await new RevokeCompanyLinkCommandHandler(links).HandleAsync(new RevokeCompanyLinkCommand(linkId, "Fraud confirmed."));

        await links.Received(1).SuspendAsync(linkId, "Dispute under review.", Arg.Any<CancellationToken>());
        await links.Received(1).ResumeAsync(linkId, Arg.Any<CancellationToken>());
        await links.Received(1).RevokeAsync(linkId, "Fraud confirmed.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Premises_handlers_create_under_the_ambient_tenant()
    {
        var premises = Substitute.For<IPremisesService>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        Guid premisesId = UuidV7.NewGuid();
        premises.CreateAsync(TenantId, "P1", "Name", "Addr", "0,0", "9-5", Arg.Any<CancellationToken>())
            .Returns(Domain.Registry.Premises.Create(TenantId, "P1", "Name", "Addr", "0,0", "9-5"));
        premises.AddOccupancyAsync(premisesId, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(PremisesOccupancy.Create(TenantId, premisesId, UuidV7.NewGuid(), UuidV7.NewGuid(), Now));

        Guid created = await new CreatePremisesCommandHandler(premises, tenant)
            .HandleAsync(new CreatePremisesCommand("P1", "Name", "Addr", "0,0", "9-5"));
        Guid occupied = await new AddPremisesOccupancyCommandHandler(premises)
            .HandleAsync(new AddPremisesOccupancyCommand(premisesId, UuidV7.NewGuid(), UuidV7.NewGuid()));
        await new PublishPremisesBinLayoutCommandHandler(premises)
            .HandleAsync(new PublishPremisesBinLayoutCommand(premisesId));

        created.Should().NotBe(Guid.Empty);
        occupied.Should().NotBe(Guid.Empty);
        await premises.Received(1).PublishBinLayoutAsync(premisesId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_handlers_create_grant_and_revoke()
    {
        var users = Substitute.For<IRegistryUserService>();
        var tenant = Substitute.For<ITenantContext>();
        var operators = Substitute.For<IOperatorContext>();
        tenant.TenantId.Returns(TenantId);
        operators.RequireOperatorId().Returns(UuidV7.NewGuid());
        operators.Principal.Returns("user:owner");

        Guid userId = UuidV7.NewGuid();
        users.CreateAsync(TenantId, "login", "Name", Arg.Any<Guid>(), "contact", Arg.Any<CancellationToken>())
            .Returns(RegistryUser.Create(TenantId, "login", "Name", UuidV7.NewGuid(), "contact"));
        users.GrantAccessAsync(userId, Arg.Any<Guid>(), "Cashier", "user:owner", Arg.Any<CancellationToken>())
            .Returns(RegistryUserCompanyAccess.Create(TenantId, userId, UuidV7.NewGuid(), "Cashier", "user:owner", Now));

        Guid created = await new CreateRegistryUserCommandHandler(users, tenant)
            .HandleAsync(new CreateRegistryUserCommand("login", "Name", "contact", UuidV7.NewGuid()));
        Guid granted = await new GrantCompanyAccessCommandHandler(users, operators)
            .HandleAsync(new GrantCompanyAccessCommand(userId, UuidV7.NewGuid(), "Cashier"));
        await new RevokeCompanyAccessCommandHandler(users)
            .HandleAsync(new RevokeCompanyAccessCommand(userId, UuidV7.NewGuid()));

        created.Should().NotBe(Guid.Empty);
        granted.Should().NotBe(Guid.Empty);
        await users.Received(1).RevokeAccessAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Terminal_handlers_register_and_assign()
    {
        var terminals = Substitute.For<ITerminalService>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        Guid premisesId = UuidV7.NewGuid();
        terminals.RegisterAsync(TenantId, premisesId, "T1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RegistryTerminal.Create(TenantId, premisesId, "T1", new string('a', 64)));

        Guid terminalId = UuidV7.NewGuid();
        Guid registered = await new RegisterTerminalCommandHandler(terminals, tenant)
            .HandleAsync(new RegisterTerminalCommand(premisesId, "T1", new string('a', 64)));
        await new SetTerminalCompaniesCommandHandler(terminals)
            .HandleAsync(new SetTerminalCompaniesCommand(terminalId, [UuidV7.NewGuid()]));

        registered.Should().NotBe(Guid.Empty);
        await terminals.Received(1).SetCompaniesAsync(terminalId, Arg.Any<List<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provisioning_assigns_the_acting_operator_never_a_body_parameter()
    {
        var provisioner = Substitute.For<ICompanyProvisioner>();
        var tenant = Substitute.For<ITenantContext>();
        var operators = Substitute.For<IOperatorContext>();
        Guid operatorId = UuidV7.NewGuid();
        tenant.TenantId.Returns(TenantId);
        operators.RequireOperatorId().Returns(operatorId);

        Company? provisioned = null;
        provisioner.ProvisionAsync(Arg.Do<Company>(c => provisioned = c), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<Company>()));

        Guid id = await new ProvisionCompanyCommandHandler(provisioner, tenant, operators)
            .HandleAsync(new ProvisionCompanyCommand("HW", "Hardware", "Hardware", "ZAR", "en-ZA", "HW"));

        id.Should().NotBe(Guid.Empty);
        provisioned.Should().NotBeNull();
        provisioned!.OperatorId.Should().Be(operatorId);
    }
}
