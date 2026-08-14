using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Identity;

/// <summary>
/// Signing in: passwords, PINs, rotation and reuse detection, against a real database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthenticationServiceTests(PostgresFixture fixture)
{
    private const string Password = "CorrectHorseBattery1";

    [Fact]
    public async Task Signs_in_with_the_right_password_and_issues_both_tokens()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult result = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be(userId);
        result.AccessToken!.Value.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.AccessToken.ExpiresAt.Should().Be(harness.Clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Stores_only_a_digest_of_the_refresh_token()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult result = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);

        IReadOnlyList<RefreshToken> live = await harness.Tokens.ListLiveAsync(userId);
        live.Should().ContainSingle();
        live[0].TokenHash.Should().NotBe(result.RefreshToken);
        live[0].TokenHash.Should().Be(harness.TokenHasher.Hash(result.RefreshToken!));
    }

    [Fact]
    public async Task Gives_the_same_answer_for_an_unknown_user_and_a_wrong_password()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult unknown = await harness.Authentication.SignInWithPasswordAsync("nobody", Password);
        AuthenticationResult wrong = await harness.Authentication.SignInWithPasswordAsync("nmokoena", "not-it-at-all");

        unknown.Failure.Should().Be(AuthenticationFailure.InvalidCredentials);
        wrong.Failure.Should().Be(AuthenticationFailure.InvalidCredentials);
    }

    [Fact]
    public async Task Locks_out_after_five_wrong_passwords_and_refuses_the_right_one_until_it_expires()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", Password);

        for (int attempt = 0; attempt < CredentialPolicy.Default.MaxFailedAttempts; attempt++)
        {
            await harness.Authentication.SignInWithPasswordAsync("nmokoena", "wrong-password-1");
        }

        AuthenticationResult locked = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        locked.Failure.Should().Be(AuthenticationFailure.LockedOut);

        harness.Clock.Advance(CredentialPolicy.Default.LockoutDuration);

        AuthenticationResult afterwards = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        afterwards.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_counter()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        await harness.Authentication.SignInWithPasswordAsync("nmokoena", "wrong-password-1");
        await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);

        (await harness.Users.FindAsync(userId))!.FailedPasswordAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Refuses_a_deactivated_user()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        User user = (await harness.Users.FindAsync(userId))!;
        user.Deactivate();
        await harness.Context.CommitAsync();

        AuthenticationResult result = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);

        result.Failure.Should().Be(AuthenticationFailure.Inactive);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_retires_the_old_one()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult first = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        AuthenticationResult second = await harness.Authentication.RefreshAsync(first.RefreshToken!);

        second.Succeeded.Should().BeTrue();
        second.RefreshToken.Should().NotBe(first.RefreshToken);

        AuthenticationResult third = await harness.Authentication.RefreshAsync(second.RefreshToken!);
        third.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Reusing_a_rotated_token_kills_every_live_session_for_that_user()
    {
        // A token presented after it has been exchanged is either a replay or a theft, and there is
        // no way to tell which at that moment (docs/SECURITY.md §1).
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult first = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        AuthenticationResult second = await harness.Authentication.RefreshAsync(first.RefreshToken!);

        AuthenticationResult replay = await harness.Authentication.RefreshAsync(first.RefreshToken!);

        replay.Failure.Should().Be(AuthenticationFailure.TokenReused);
        (await harness.Tokens.ListLiveAsync(userId)).Should().BeEmpty();

        AuthenticationResult afterwards = await harness.Authentication.RefreshAsync(second.RefreshToken!);
        afterwards.Failure.Should().Be(AuthenticationFailure.TokenNotUsable);
    }

    [Fact]
    public async Task Refuses_an_expired_refresh_token()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult first = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        harness.Clock.Advance(TimeSpan.FromDays(31));

        AuthenticationResult result = await harness.Authentication.RefreshAsync(first.RefreshToken!);

        result.Failure.Should().Be(AuthenticationFailure.TokenNotUsable);
    }

    [Fact]
    public async Task Refuses_a_refresh_token_that_was_never_issued()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult result = await harness.Authentication.RefreshAsync("not-a-token-anybody-issued");

        result.Failure.Should().Be(AuthenticationFailure.TokenNotUsable);
    }

    [Fact]
    public async Task Signing_out_revokes_every_live_token()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", Password);

        AuthenticationResult session = await harness.Authentication.SignInWithPasswordAsync("nmokoena", Password);
        await harness.Authentication.SignOutAsync(userId);

        (await harness.Tokens.ListLiveAsync(userId)).Should().BeEmpty();
        (await harness.Authentication.RefreshAsync(session.RefreshToken!)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_terminal_authenticates_by_its_pinned_thumbprint()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid terminalId = await harness.CreateActiveTerminalAsync("T01");

        TerminalAuthenticationResult result = await harness.Authentication
            .AuthenticateTerminalAsync(IdentityHarness.Thumbprint("T01"), "board-serial-1");

        result.Succeeded.Should().BeTrue();
        result.Terminal!.Id.Should().Be(terminalId);
        result.Terminal.LastSeenAt.Should().Be(harness.Clock.UtcNow);
    }

    [Fact]
    public async Task An_unknown_thumbprint_authenticates_nothing()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateActiveTerminalAsync("T01");

        TerminalAuthenticationResult result = await harness.Authentication
            .AuthenticateTerminalAsync(IdentityHarness.Thumbprint("somebody-elses-certificate"));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(AuthenticationFailure.TerminalNotAuthorised);
    }

    [Fact]
    public async Task A_drifted_fingerprint_still_authenticates_and_raises_a_flag()
    {
        // ADR-026: detection never auto-disables anything.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateActiveTerminalAsync("T01");

        TerminalAuthenticationResult result = await harness.Authentication
            .AuthenticateTerminalAsync(IdentityHarness.Thumbprint("T01"), "a-different-motherboard");

        result.Succeeded.Should().BeTrue();
        result.Terminal!.HasFingerprintDrift.Should().BeTrue();
    }

    [Fact]
    public async Task A_pin_resolves_the_operator_from_the_store_and_the_pin_alone()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync("cashier1", pin: "1174", roleId: roleId, storeId: harness.StoreId);
        Guid terminalId = await harness.CreateActiveTerminalAsync("T01");

        AuthenticationResult result = await harness.Authentication.SignInWithPinAsync(terminalId, "1174");

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task A_wrong_pin_signs_nobody_in()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        await harness.CreateUserAsync("cashier1", pin: "1174", roleId: roleId, storeId: harness.StoreId);
        Guid terminalId = await harness.CreateActiveTerminalAsync("T01");

        AuthenticationResult result = await harness.Authentication.SignInWithPinAsync(terminalId, "9999");

        result.Failure.Should().Be(AuthenticationFailure.InvalidCredentials);
    }

    [Fact]
    public async Task A_pin_is_refused_on_a_terminal_that_cannot_authenticate()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        await harness.CreateUserAsync("cashier1", pin: "1174", roleId: roleId, storeId: harness.StoreId);
        TerminalEnrolment pending = await harness.EnrolTerminalAsync("T02");

        AuthenticationResult result = await harness.Authentication.SignInWithPinAsync(pending.TerminalId, "1174");

        result.Failure.Should().Be(AuthenticationFailure.TerminalNotAuthorised);
    }

    [Fact]
    public async Task Five_failed_pin_attempts_lock_the_operators_pin_but_not_their_password()
    {
        // R1 in miniature: a shift of mistyped PINs must not lock a manager out of the reports.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync(
            "cashier1", Password, pin: "1174", roleId: roleId, storeId: harness.StoreId);
        Guid terminalId = await harness.CreateActiveTerminalAsync("T01");

        for (int attempt = 0; attempt < CredentialPolicy.Default.MaxFailedAttempts; attempt++)
        {
            await harness.Authentication.RecordFailedPinAttemptAsync(userId);
        }

        AuthenticationResult pin = await harness.Authentication.SignInWithPinAsync(terminalId, "1174");
        pin.Failure.Should().Be(AuthenticationFailure.LockedOut);

        AuthenticationResult password = await harness.Authentication.SignInWithPasswordAsync("cashier1", Password);
        password.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_operator_with_no_role_in_the_store_cannot_sign_in_at_its_till()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        Guid otherStore = Guid.Parse("01900000-0000-7000-8000-0000000000dd");
        await harness.CreateUserAsync("cashier1", pin: "1174", roleId: roleId, storeId: otherStore);
        Guid terminalId = await harness.CreateActiveTerminalAsync("T01");

        AuthenticationResult result = await harness.Authentication.SignInWithPinAsync(terminalId, "1174");

        result.Failure.Should().Be(AuthenticationFailure.InvalidCredentials);
    }
}
