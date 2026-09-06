using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Factories and transitions for the registry directory: operators, premises, users, tills.</summary>
public sealed class RegistryDirectoryTests
{
    [Fact]
    public void Operator_projects_from_the_licence_and_tracks_lapse()
    {
        Guid operatorId = UuidV7.NewGuid();

        Operator row = Operator.Create(operatorId, "Op", "fp-1", UuidV7.NewGuid());

        row.IsActive.Should().BeTrue();
        row.LicenceFingerprint.Should().Be("fp-1");

        row.Deactivate();
        row.IsActive.Should().BeFalse();

        row.Reactivate();
        row.IsActive.Should().BeTrue();

        var empty = () => Operator.Create(Guid.Empty, "Op", "fp", UuidV7.NewGuid());
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Premises_manages_details_and_active_state()
    {
        Premises premises = Premises.Create(UuidV7.NewGuid(), "P1", "Name", "Addr", "0,0", "9-5");

        premises.IsActive.Should().BeTrue();
        premises.TradingHours.Should().Be("9-5");

        premises.Update(name: "New name", address: null, geoLocation: null, tradingHours: "10-6");
        premises.Name.Should().Be("New name");
        premises.Address.Should().Be("Addr");

        premises.Deactivate();
        premises.IsActive.Should().BeFalse();
        premises.Reactivate();
        premises.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Registry_user_enables_disables_and_updates_contact()
    {
        RegistryUser user = RegistryUser.Create(UuidV7.NewGuid(), "login", "Name", UuidV7.NewGuid(), "old");

        user.IsEnabled.Should().BeTrue();

        user.Disable();
        user.IsEnabled.Should().BeFalse();

        user.Enable();
        user.UpdateContactDetails("new");
        user.ContactDetails.Should().Be("new");
    }

    [Fact]
    public void Terminal_tracks_its_company_set()
    {
        Guid first = UuidV7.NewGuid();
        Guid second = UuidV7.NewGuid();
        RegistryTerminal terminal = RegistryTerminal.Create(UuidV7.NewGuid(), UuidV7.NewGuid(), "T1", new string('a', 64));

        terminal.CompanyIds.Should().BeEmpty();

        terminal.AddCompany(first);
        terminal.AddCompany(first);
        terminal.CompanyIds.Should().ContainSingle().Which.Should().Be(first);

        terminal.SetCompanies([first, second, Guid.Empty]);
        terminal.CompanyIds.Should().BeEquivalentTo([first, second]);

        terminal.RemoveCompany(first);
        terminal.CompanyIds.Should().ContainSingle().Which.Should().Be(second);

        terminal.Deactivate();
        terminal.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Occupancy_can_end()
    {
        DateTimeOffset now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
        PremisesOccupancy occupancy = PremisesOccupancy.Create(UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), now);

        occupancy.OccupiesTo.Should().BeNull();

        occupancy.EndOccupancy(now.AddDays(30));

        occupancy.OccupiesTo.Should().Be(now.AddDays(30));
    }
}
