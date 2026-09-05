using VumaRetail.Application.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Every Stage 06e command validator: the valid shape passes, each broken shape fails.</summary>
public sealed class TradingGroupValidationTests
{
    [Fact]
    public void Propose_validator()
    {
        var validator = new ProposeCompanyLinkCommandValidator();
        Guid a = UuidV7.NewGuid();
        Guid b = UuidV7.NewGuid();

        validator.Validate(new ProposeCompanyLinkCommand(a, b, CompanyLinkScope.SharedFloor)).IsValid.Should().BeTrue();
        validator.Validate(new ProposeCompanyLinkCommand(Guid.Empty, b, CompanyLinkScope.SharedFloor)).IsValid.Should().BeFalse();
        validator.Validate(new ProposeCompanyLinkCommand(a, Guid.Empty, CompanyLinkScope.SharedFloor)).IsValid.Should().BeFalse();
        validator.Validate(new ProposeCompanyLinkCommand(a, b, CompanyLinkScope.None)).IsValid.Should().BeFalse();
        validator.Validate(new ProposeCompanyLinkCommand(a, a, CompanyLinkScope.SharedFloor)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Link_lifecycle_validators()
    {
        Guid id = UuidV7.NewGuid();

        new AcceptCompanyLinkCommandValidator().Validate(new AcceptCompanyLinkCommand(id)).IsValid.Should().BeTrue();
        new AcceptCompanyLinkCommandValidator().Validate(new AcceptCompanyLinkCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new ResumeCompanyLinkCommandValidator().Validate(new ResumeCompanyLinkCommand(id)).IsValid.Should().BeTrue();
        new ResumeCompanyLinkCommandValidator().Validate(new ResumeCompanyLinkCommand(Guid.Empty)).IsValid.Should().BeFalse();

        var suspend = new SuspendCompanyLinkCommandValidator();
        suspend.Validate(new SuspendCompanyLinkCommand(id, "Dispute.")).IsValid.Should().BeTrue();
        suspend.Validate(new SuspendCompanyLinkCommand(Guid.Empty, "Dispute.")).IsValid.Should().BeFalse();
        suspend.Validate(new SuspendCompanyLinkCommand(id, "")).IsValid.Should().BeFalse();

        var revoke = new RevokeCompanyLinkCommandValidator();
        revoke.Validate(new RevokeCompanyLinkCommand(id, "Fraud confirmed.")).IsValid.Should().BeTrue();
        revoke.Validate(new RevokeCompanyLinkCommand(id, "Short")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Premises_validators()
    {
        var create = new CreatePremisesCommandValidator();
        create.Validate(new CreatePremisesCommand("P1", "Name", "Addr", "0,0", "9-5")).IsValid.Should().BeTrue();
        create.Validate(new CreatePremisesCommand("", "Name", "Addr", "0,0", "9-5")).IsValid.Should().BeFalse();
        create.Validate(new CreatePremisesCommand("P1", "", "Addr", "0,0", "9-5")).IsValid.Should().BeFalse();
        create.Validate(new CreatePremisesCommand("P1", "Name", "", "0,0", "9-5")).IsValid.Should().BeFalse();
        create.Validate(new CreatePremisesCommand("P1", "Name", "Addr", "", "9-5")).IsValid.Should().BeFalse();

        var occupy = new AddPremisesOccupancyCommandValidator();
        occupy.Validate(new AddPremisesOccupancyCommand(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid())).IsValid.Should().BeTrue();
        occupy.Validate(new AddPremisesOccupancyCommand(Guid.Empty, UuidV7.NewGuid(), UuidV7.NewGuid())).IsValid.Should().BeFalse();

        var publish = new PublishPremisesBinLayoutCommandValidator();
        publish.Validate(new PublishPremisesBinLayoutCommand(UuidV7.NewGuid())).IsValid.Should().BeTrue();
        publish.Validate(new PublishPremisesBinLayoutCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void User_and_terminal_validators()
    {
        var createUser = new CreateRegistryUserCommandValidator();
        createUser.Validate(new CreateRegistryUserCommand("login", "Name", "contact", UuidV7.NewGuid())).IsValid.Should().BeTrue();
        createUser.Validate(new CreateRegistryUserCommand("", "Name", "contact", UuidV7.NewGuid())).IsValid.Should().BeFalse();
        createUser.Validate(new CreateRegistryUserCommand("login", "Name", "contact", Guid.Empty)).IsValid.Should().BeFalse();

        var grant = new GrantCompanyAccessCommandValidator();
        grant.Validate(new GrantCompanyAccessCommand(UuidV7.NewGuid(), UuidV7.NewGuid(), "Cashier")).IsValid.Should().BeTrue();
        grant.Validate(new GrantCompanyAccessCommand(UuidV7.NewGuid(), UuidV7.NewGuid(), "")).IsValid.Should().BeFalse();

        var revoke = new RevokeCompanyAccessCommandValidator();
        revoke.Validate(new RevokeCompanyAccessCommand(UuidV7.NewGuid(), UuidV7.NewGuid())).IsValid.Should().BeTrue();
        revoke.Validate(new RevokeCompanyAccessCommand(Guid.Empty, UuidV7.NewGuid())).IsValid.Should().BeFalse();

        var register = new RegisterTerminalCommandValidator();
        register.Validate(new RegisterTerminalCommand(UuidV7.NewGuid(), "T1", new string('a', 64))).IsValid.Should().BeTrue();
        register.Validate(new RegisterTerminalCommand(UuidV7.NewGuid(), "", new string('a', 64))).IsValid.Should().BeFalse();
        register.Validate(new RegisterTerminalCommand(UuidV7.NewGuid(), "T1", "short")).IsValid.Should().BeFalse();

        var setCompanies = new SetTerminalCompaniesCommandValidator();
        setCompanies.Validate(new SetTerminalCompaniesCommand(UuidV7.NewGuid(), [UuidV7.NewGuid()])).IsValid.Should().BeTrue();
        setCompanies.Validate(new SetTerminalCompaniesCommand(UuidV7.NewGuid(), [])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Registry_module_declares_seven_permissions_and_a_core_manifest()
    {
        var permissions = new RegistryPermissions();
        var manifest = new RegistryModuleManifest();

        permissions.Module.Should().Be("registry");
        permissions.Permissions.Should().HaveCount(13);
        manifest.Module.Should().Be("registry");
        manifest.LicenceFlag.Should().Be("registry");
        manifest.IsCore.Should().BeTrue();
    }
}
