using ZenithRetail.Domain.Identity;

namespace ZenithRetail.UnitTests.Identity;

/// <summary>
/// <see cref="RefreshToken"/> — rotation, expiry and the security stamp.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly Guid Tenant = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    private static RefreshToken Issued(string stamp = "stamp-1")
        => RefreshToken.Issue(Tenant, UserId, "digest", stamp, Now, Month);

    [Fact]
    public void Is_usable_while_live_unexpired_and_stamped_correctly()
    {
        Issued().IsUsable(Now.AddDays(1), "stamp-1").Should().BeTrue();
    }

    [Fact]
    public void Stops_being_usable_at_expiry()
    {
        RefreshToken token = Issued();

        token.IsUsable(Now + Month - TimeSpan.FromSeconds(1), "stamp-1").Should().BeTrue();
        token.IsUsable(Now + Month, "stamp-1").Should().BeFalse();
    }

    [Fact]
    public void Stops_being_usable_when_the_security_stamp_moves_on()
    {
        // This is what makes a password change actually end the sessions it is meant to end, without
        // having to find every token first.
        Issued().IsUsable(Now.AddDays(1), "stamp-2").Should().BeFalse();
    }

    [Fact]
    public void Rotation_revokes_it_and_points_at_the_successor()
    {
        RefreshToken token = Issued();
        Guid successor = Guid.Parse("01900000-0000-7000-8000-000000000009");

        token.Rotate(successor, Now.AddMinutes(5));

        token.RevocationReason.Should().Be(RefreshTokenRevocation.Rotated);
        token.ReplacedByTokenId.Should().Be(successor);
        token.IsUsable(Now.AddMinutes(6), "stamp-1").Should().BeFalse();
    }

    [Fact]
    public void Keeps_the_first_revocation_reason()
    {
        // "Reused" is the row that explains an incident. A sweep that follows it must not overwrite
        // the one piece of evidence.
        RefreshToken token = Issued();
        token.Revoke(RefreshTokenRevocation.Reused, Now);

        token.Revoke(RefreshTokenRevocation.SignedOut, Now.AddMinutes(1));

        token.RevocationReason.Should().Be(RefreshTokenRevocation.Reused);
        token.RevokedAt.Should().Be(Now);
    }

    [Fact]
    public void Carries_the_terminal_and_store_of_a_pos_session()
    {
        Guid store = Guid.Parse("01900000-0000-7000-8000-000000000002");
        Guid terminal = Guid.Parse("01900000-0000-7000-8000-000000000004");

        RefreshToken token = RefreshToken.Issue(Tenant, UserId, "digest", "stamp", Now, Month, store, terminal);

        token.StoreId.Should().Be(store);
        token.TerminalId.Should().Be(terminal);
    }

    [Fact]
    public void Refuses_to_exist_without_a_user_or_a_digest()
    {
        Action noUser = () => RefreshToken.Issue(Tenant, Guid.Empty, "digest", "stamp", Now, Month);
        Action noDigest = () => RefreshToken.Issue(Tenant, UserId, "  ", "stamp", Now, Month);

        noUser.Should().Throw<ArgumentException>();
        noDigest.Should().Throw<ArgumentException>();
    }
}
