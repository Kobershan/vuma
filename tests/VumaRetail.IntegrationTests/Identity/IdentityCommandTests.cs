using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Identity;

/// <summary>
/// The Stage 02 command handlers against real PostgreSQL and real migrations
/// (<c>docs/TESTING.md</c> §2).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdentityCommandTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Creating_a_user_stores_a_hash_and_never_the_password()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid userId = await harness.CreateUserAsync("nmokoena", "CorrectHorseBattery1");

        User user = (await harness.Users.FindAsync(userId))!;
        user.PasswordHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordHash.Should().NotContain("CorrectHorseBattery1");
        user.TenantId.Should().Be(harness.TenantId);
    }

    [Fact]
    public async Task Refuses_a_second_user_with_the_same_sign_in_name()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena");

        Func<Task> again = () => harness.CreateUserAsync("NMokoena");

        (await again.Should().ThrowAsync<IdentityConflictException>())
            .Which.Code.Should().Be("IDENTITY_USER_NAME_TAKEN");
    }

    [Fact]
    public async Task Refuses_a_password_shorter_than_the_policy()
    {
        // Through the pipeline the validator answers first, which is the point of having one: the
        // caller is told which property is wrong before a database round trip happens.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Func<Task> create = () => harness.CreateUserAsync("shorty", "short");

        (await create.Should().ThrowAsync<ValidationFailedException>())
            .Which.Errors.Should().ContainKey(nameof(CreateUserCommand.Password));
    }

    [Fact]
    public async Task The_handler_refuses_a_short_password_even_with_no_validator_in_front_of_it()
    {
        // Defence in depth, and the reason CONVENTIONS.md §4 keeps both: the validator is the
        // contract at the edge, the guard is what makes the rule true for every caller — including
        // the sync receiver and a seed script, neither of which goes through the pipeline.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        CreateUserCommandHandler handler = new(harness.Users, harness.PasswordHasher, harness.TenantContext);
        Func<Task> create = () => handler.HandleAsync(new CreateUserCommand("shorty", "Shorty", "short"));

        (await create.Should().ThrowAsync<WeakCredentialException>())
            .Which.Code.Should().Be("IDENTITY_PASSWORD_TOO_SHORT");
    }

    [Fact]
    public async Task Refuses_a_pin_that_is_not_four_to_eight_digits()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("cashier");

        foreach (string bad in new[] { "123", "123456789", "12a4" })
        {
            Func<Task> set = () => harness.SendAsync(new SetUserPinCommand(userId, bad));

            (await set.Should().ThrowAsync<ValidationFailedException>())
                .Which.Errors.Should().ContainKey(nameof(SetUserPinCommand.Pin));
        }
    }

    [Fact]
    public async Task The_handler_refuses_a_malformed_pin_even_with_no_validator_in_front_of_it()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("cashier");

        SetUserPinCommandHandler handler = new(harness.Users, harness.PasswordHasher);
        Func<Task> set = () => handler.HandleAsync(new SetUserPinCommand(userId, "12a4"));

        (await set.Should().ThrowAsync<WeakCredentialException>())
            .Which.Code.Should().Be("IDENTITY_PIN_MALFORMED");
    }

    [Fact]
    public async Task Refuses_a_pin_another_live_operator_already_uses()
    {
        // A PIN sign-in resolves the operator from the PIN alone. Two operators sharing one would
        // make the till attribute a sale to whichever row came back first.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("cashier1", pin: "1174");
        Guid second = await harness.CreateUserAsync("cashier2");

        Func<Task> collide = () => harness.SendAsync(new SetUserPinCommand(second, "1174"));

        (await collide.Should().ThrowAsync<IdentityConflictException>())
            .Which.Code.Should().Be("IDENTITY_PIN_TAKEN");
    }

    [Fact]
    public async Task Lets_an_operator_keep_their_own_pin_when_it_is_reset_to_the_same_value()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("cashier1", pin: "1174");

        Func<Task> same = () => harness.SendAsync(new SetUserPinCommand(userId, "1174"));

        await same.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Creating_a_role_grants_only_permissions_a_module_declares()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid roleId = await harness.CreateRoleAsync(
            "Store Manager",
            IdentityPermissions.UserView,
            PlatformPermissions.StoreView);

        IReadOnlyList<RolePermission> grants = await harness.Roles.ListPermissionsAsync(roleId);

        grants.Select(grant => grant.Permission)
            .Should().BeEquivalentTo([IdentityPermissions.UserView, PlatformPermissions.StoreView]);
    }

    [Fact]
    public async Task Refuses_a_role_granting_a_permission_no_module_declares()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Func<Task> create = () => harness.CreateRoleAsync("Ghost", "sales.refund.approve");

        (await create.Should().ThrowAsync<UndeclaredPermissionException>())
            .Which.Code.Should().Be("PERMISSION_NOT_DECLARED");
    }

    [Fact]
    public async Task Writes_no_role_at_all_when_one_of_its_permissions_is_undeclared()
    {
        // Validated before anything is written, so a role is never created half-granted.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Func<Task> create = () => harness.CreateRoleAsync("Half", IdentityPermissions.UserView, "sales.refund.approve");

        await create.Should().ThrowAsync<UndeclaredPermissionException>();
        (await harness.Roles.FindByNameAsync("Half")).Should().BeNull();
    }

    [Fact]
    public async Task Refuses_a_duplicate_role_assignment_at_the_same_scope()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync("cashier1", roleId: roleId, storeId: harness.StoreId);

        Func<Task> again = () => harness.SendAsync(new AssignRoleCommand(userId, roleId, harness.StoreId));

        (await again.Should().ThrowAsync<IdentityConflictException>())
            .Which.Code.Should().Be("IDENTITY_ROLE_ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task Allows_the_same_role_in_two_different_stores()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Supervisor", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync("supervisor", roleId: roleId, storeId: harness.StoreId);

        Guid otherStore = Guid.Parse("01900000-0000-7000-8000-0000000000aa");

        await harness.SendAsync(new AssignRoleCommand(userId, roleId, otherStore));

        (await harness.Roles.ListAssignmentsAsync(userId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Refuses_to_assign_a_role_to_a_user_who_does_not_exist()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);

        Func<Task> assign = () => harness.SendAsync(new AssignRoleCommand(Guid.NewGuid(), roleId));

        (await assign.Should().ThrowAsync<IdentityNotFoundException>())
            .Which.Code.Should().Be("IDENTITY_NOT_FOUND");
    }

    [Fact]
    public async Task Enrolling_a_terminal_returns_a_code_it_never_stores_in_plaintext()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        TerminalEnrolment enrolment = await harness.EnrolTerminalAsync("T01");

        Terminal terminal = (await harness.Terminals.FindAsync(enrolment.TerminalId))!;
        terminal.Status.Should().Be(TerminalStatus.Pending);
        terminal.EnrolmentCodeHash.Should().NotBeNullOrWhiteSpace();
        terminal.EnrolmentCodeHash.Should().NotContain(enrolment.EnrolmentCode);
    }

    [Fact]
    public async Task Refuses_a_second_terminal_with_the_same_code_in_a_store()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.EnrolTerminalAsync("T01");

        Func<Task> again = () => harness.EnrolTerminalAsync("t01");

        (await again.Should().ThrowAsync<IdentityConflictException>())
            .Which.Code.Should().Be("IDENTITY_TERMINAL_CODE_TAKEN");
    }

    [Fact]
    public async Task Activation_pins_the_certificate_and_spends_the_code()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        TerminalEnrolment enrolment = await harness.EnrolTerminalAsync("T01");
        string thumbprint = IdentityHarness.Thumbprint("T01");

        Guid terminalId = await harness.SendAsync(new ActivateTerminalCommand(
            harness.StoreId, enrolment.EnrolmentCode, thumbprint, "board-serial-1"));

        Terminal terminal = (await harness.Terminals.FindAsync(terminalId))!;
        terminal.Status.Should().Be(TerminalStatus.Active);
        terminal.CertificateThumbprint.Should().Be(thumbprint);

        Func<Task> reuse = () => harness.SendAsync(new ActivateTerminalCommand(
            harness.StoreId, enrolment.EnrolmentCode, thumbprint, "board-serial-1"));

        await reuse.Should().ThrowAsync<TerminalActivationException>();
    }

    [Fact]
    public async Task Refuses_an_expired_activation_code()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        TerminalEnrolment enrolment = await harness.EnrolTerminalAsync("T01", TimeSpan.FromMinutes(10));

        harness.Clock.Advance(TimeSpan.FromMinutes(11));

        Func<Task> activate = () => harness.SendAsync(new ActivateTerminalCommand(
            harness.StoreId, enrolment.EnrolmentCode, IdentityHarness.Thumbprint("T01"), "board-serial-1"));

        await activate.Should().ThrowAsync<TerminalActivationException>();
    }

    [Fact]
    public async Task Refuses_an_activation_code_from_another_store()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        TerminalEnrolment enrolment = await harness.EnrolTerminalAsync("T01");

        Func<Task> activate = () => harness.SendAsync(new ActivateTerminalCommand(
            Guid.Parse("01900000-0000-7000-8000-0000000000bb"),
            enrolment.EnrolmentCode,
            IdentityHarness.Thumbprint("T01"),
            "board-serial-1"));

        await activate.Should().ThrowAsync<TerminalActivationException>();
    }

    [Fact]
    public async Task Revoking_a_terminal_ends_its_ability_to_authenticate()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid terminalId = await harness.CreateActiveTerminalAsync();

        await harness.SendAsync(new RevokeTerminalCommand(terminalId, "Till decommissioned"));

        TerminalAuthenticationResult result = await harness.Authentication
            .AuthenticateTerminalAsync(IdentityHarness.Thumbprint("T01"));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(AuthenticationFailure.TerminalNotAuthorised);
    }

    [Fact]
    public async Task Changing_a_password_revokes_every_live_refresh_token()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("nmokoena", "CorrectHorseBattery1");

        AuthenticationResult signedIn = await harness.Authentication
            .SignInWithPasswordAsync("nmokoena", "CorrectHorseBattery1");
        signedIn.Succeeded.Should().BeTrue();

        await harness.SendAsync(new ChangePasswordCommand(userId, "AnEntirelyNewSecret1"));

        AuthenticationResult refreshed = await harness.Authentication.RefreshAsync(signedIn.RefreshToken!);

        refreshed.Succeeded.Should().BeFalse();
        (await harness.Tokens.ListLiveAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_users_rows_carry_the_tenant_and_are_invisible_to_another_one()
    {
        // Tenant isolation is a global query filter, not a habit — the point is that the handler
        // never wrote a Where clause for it.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena");

        harness.TenantContext.SetTenant(Guid.Parse("01900000-0000-7000-8000-0000000000cc"));

        (await harness.Users.FindByUserNameAsync("nmokoena")).Should().BeNull();
        (await harness.Context.Users.CountAsync()).Should().Be(0);
    }
}
