using VumaRetail.Domain.Identity;

namespace VumaRetail.UnitTests.Identity;

/// <summary>
/// <see cref="User"/> — the counting, the locking and the security stamp.
/// </summary>
public sealed class UserTests
{
    private static readonly Guid Tenant = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Create(Tenant, "  nmokoena ", "Naledi Mokoena");

    [Fact]
    public void Trims_and_normalises_the_sign_in_name()
    {
        User user = NewUser();

        user.UserName.Should().Be("nmokoena");
        user.NormalizedUserName.Should().Be("NMOKOENA");
    }

    [Fact]
    public void Starts_with_a_security_stamp_so_a_token_can_always_be_retired()
    {
        NewUser().SecurityStamp.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Refuses_to_exist_without_a_tenant()
    {
        Action create = () => User.Create(Guid.Empty, "nmokoena", "Naledi");

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Locks_the_password_login_on_the_fifth_failure_and_not_before()
    {
        User user = NewUser();
        CredentialPolicy policy = CredentialPolicy.Default;

        for (int attempt = 1; attempt < policy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedPasswordAttempt(Now, policy);
            user.IsPasswordLockedOut(Now).Should().BeFalse($"attempt {attempt} is within policy");
        }

        user.RecordFailedPasswordAttempt(Now, policy);

        user.IsPasswordLockedOut(Now).Should().BeTrue();
    }

    [Fact]
    public void Unlocks_exactly_when_the_lockout_window_ends()
    {
        User user = NewUser();
        CredentialPolicy policy = CredentialPolicy.Default;

        for (int attempt = 0; attempt < policy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedPasswordAttempt(Now, policy);
        }

        user.IsPasswordLockedOut(Now + policy.LockoutDuration - TimeSpan.FromSeconds(1)).Should().BeTrue();
        user.IsPasswordLockedOut(Now + policy.LockoutDuration).Should().BeFalse();
    }

    [Fact]
    public void Keeps_the_password_and_pin_lockouts_independent()
    {
        // R1: a shift of mistyped PINs at the till must not lock a manager out of the reports, and a
        // password-guessing attempt must not stop the till trading.
        User user = NewUser();
        CredentialPolicy policy = CredentialPolicy.Default;

        for (int attempt = 0; attempt < policy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedPinAttempt(Now, policy);
        }

        user.IsPinLockedOut(Now).Should().BeTrue();
        user.IsPasswordLockedOut(Now).Should().BeFalse();
    }

    [Fact]
    public void Forgives_only_the_credential_that_succeeded()
    {
        User user = NewUser();
        CredentialPolicy policy = CredentialPolicy.Default;

        user.RecordFailedPasswordAttempt(Now, policy);
        user.RecordFailedPinAttempt(Now, policy);

        user.RecordSuccessfulSignIn(Now, CredentialKind.Pin);

        user.FailedPinAttempts.Should().Be(0);
        user.FailedPasswordAttempts.Should().Be(1, "a correct PIN at the till says nothing about "
            + "whoever is guessing at the back-office password");
    }

    [Fact]
    public void Records_when_the_user_last_signed_in()
    {
        User user = NewUser();

        user.RecordSuccessfulSignIn(Now, CredentialKind.Password);

        user.LastSignedInAt.Should().Be(Now);
    }

    [Fact]
    public void Rotates_the_security_stamp_when_the_password_changes()
    {
        User user = NewUser();
        string before = user.SecurityStamp;

        user.SetPasswordHash("hashed");

        user.SecurityStamp.Should().NotBe(before);
    }

    [Fact]
    public void Does_not_rotate_the_security_stamp_when_a_pin_changes()
    {
        // A manager resetting a cashier's PIN mid-shift must not sign that cashier out of the sale
        // they are ringing up.
        User user = NewUser();
        string before = user.SecurityStamp;

        user.SetPinHash("hashed");

        user.SecurityStamp.Should().Be(before);
    }

    [Fact]
    public void Clears_the_lockout_when_a_credential_is_reset()
    {
        User user = NewUser();
        CredentialPolicy policy = CredentialPolicy.Default;

        for (int attempt = 0; attempt < policy.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedPinAttempt(Now, policy);
        }

        user.SetPinHash("fresh");

        user.IsPinLockedOut(Now).Should().BeFalse();
        user.FailedPinAttempts.Should().Be(0);
    }

    [Fact]
    public void Clearing_a_pin_removes_the_ability_to_sign_in_at_a_till()
    {
        User user = NewUser();
        user.SetPinHash("hashed");

        user.ClearPin();

        user.PinHash.Should().BeNull();
    }

    [Fact]
    public void Deactivating_ends_every_open_session()
    {
        User user = NewUser();
        string before = user.SecurityStamp;

        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.SecurityStamp.Should().NotBe(before);
    }

    [Fact]
    public void Normalises_the_email_for_lookup_and_clears_it_on_null()
    {
        User user = NewUser();

        user.SetEmail(" Naledi@Example.co.za ");
        user.Email.Should().Be("Naledi@Example.co.za");
        user.NormalizedEmail.Should().Be("NALEDI@EXAMPLE.CO.ZA");

        user.SetEmail(null);
        user.Email.Should().BeNull();
        user.NormalizedEmail.Should().BeNull();
    }
}
