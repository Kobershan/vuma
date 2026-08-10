using ZenithRetail.Domain.Identity;

namespace ZenithRetail.UnitTests.Identity;

/// <summary>
/// <see cref="Terminal"/> — trust on first enrolment, pinned thereafter.
/// </summary>
public sealed class TerminalTests
{
    private static readonly Guid Tenant = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid Store = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private const string Thumbprint = "AA11BB22CC33DD44EE55FF6600112233445566778899AABBCCDDEEFF0011223344";

    private static Terminal Enrolled(DateTimeOffset? expiresAt = null)
        => Terminal.Enrol(Tenant, Store, "t01", "Front counter 1", "code-hash", expiresAt ?? Now.AddHours(1));

    [Fact]
    public void Starts_pending_and_cannot_authenticate_yet()
    {
        Terminal terminal = Enrolled();

        terminal.Status.Should().Be(TerminalStatus.Pending);
        terminal.CanAuthenticate.Should().BeFalse();
        terminal.Code.Should().Be("T01");
        terminal.StoreId.Should().Be(Store);
    }

    [Fact]
    public void Activation_pins_the_thumbprint_and_spends_the_code()
    {
        Terminal terminal = Enrolled();

        terminal.Activate(Thumbprint, "board-serial-1", Now);

        terminal.Status.Should().Be(TerminalStatus.Active);
        terminal.CanAuthenticate.Should().BeTrue();
        terminal.CertificateThumbprint.Should().Be(Thumbprint);
        terminal.ActivatedAt.Should().Be(Now);

        // Spent, not merely expired: a still-comparable code is a second credential for a terminal
        // that already has a certificate.
        terminal.EnrolmentCodeHash.Should().BeNull();
        terminal.EnrolmentCodeExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Refuses_activation_after_the_code_expires()
    {
        Terminal terminal = Enrolled(Now.AddMinutes(-1));

        Action activate = () => terminal.Activate(Thumbprint, "board-serial-1", Now);

        activate.Should().Throw<TerminalActivationException>()
            .Which.Code.Should().Be("TERMINAL_ACTIVATION_REFUSED");
    }

    [Fact]
    public void Refuses_a_second_activation()
    {
        Terminal terminal = Enrolled();
        terminal.Activate(Thumbprint, "board-serial-1", Now);

        Action again = () => terminal.Activate(Thumbprint, "board-serial-1", Now);

        again.Should().Throw<TerminalActivationException>();
    }

    [Fact]
    public void A_changed_fingerprint_is_flagged_and_still_authenticates()
    {
        // ADR-026: detection never auto-disables anything. A replaced motherboard on a Saturday must
        // not close a till.
        Terminal terminal = Enrolled();
        terminal.Activate(Thumbprint, "board-serial-1", Now);

        terminal.RecordAuthentication("board-serial-2", Now.AddDays(1));

        terminal.HasFingerprintDrift.Should().BeTrue();
        terminal.CanAuthenticate.Should().BeTrue();
        terminal.LastSeenAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void An_unchanged_fingerprint_raises_no_flag()
    {
        Terminal terminal = Enrolled();
        terminal.Activate(Thumbprint, "board-serial-1", Now);

        terminal.RecordAuthentication("board-serial-1", Now.AddDays(1));

        terminal.HasFingerprintDrift.Should().BeFalse();
    }

    [Fact]
    public void Revoking_ends_authentication_and_records_why()
    {
        Terminal terminal = Enrolled();
        terminal.Activate(Thumbprint, "board-serial-1", Now);

        terminal.Revoke("Till decommissioned");

        terminal.Status.Should().Be(TerminalStatus.Revoked);
        terminal.CanAuthenticate.Should().BeFalse();
        terminal.RevocationReason.Should().Be("Till decommissioned");
    }

    [Fact]
    public void Revoking_a_pending_terminal_burns_its_unused_code()
    {
        Terminal terminal = Enrolled();

        terminal.Revoke("Enrolled by mistake");

        terminal.EnrolmentCodeHash.Should().BeNull();
    }

    [Fact]
    public void Refuses_to_exist_without_a_store()
    {
        Action enrol = () => Terminal.Enrol(Tenant, Guid.Empty, "T01", "Front counter", "hash", Now);

        enrol.Should().Throw<ArgumentException>();
    }
}
