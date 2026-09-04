using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Business rules for the company link: ordering, operator match shape and status machine.</summary>
public sealed class CompanyLinkTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_stores_the_smaller_guid_first_so_a_pair_has_one_row()
    {
        Guid bigger = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid smaller = Guid.Parse("11111111-1111-1111-1111-111111111111");

        CompanyLink link = CompanyLink.Create(TenantId, OperatorId, bigger, smaller, CompanyLinkScope.SharedFloor, Now, "Op");

        link.CompanyAId.Should().Be(smaller);
        link.CompanyBId.Should().Be(bigger);
        link.Status.Should().Be(CompanyLinkStatus.Proposed);
        link.OperatorId.Should().Be(OperatorId);
        link.OperatorName.Should().Be("Op");
        link.EffectiveFrom.Should().Be(Now);
    }

    [Fact]
    public void Create_refuses_a_self_link_an_empty_operator_and_no_scope()
    {
        Guid company = UuidV7.NewGuid();

        var self = () => CompanyLink.Create(TenantId, OperatorId, company, company, CompanyLinkScope.SharedFloor, Now);
        self.Should().Throw<ArgumentException>();

        var noOperator = () => CompanyLink.Create(TenantId, Guid.Empty, UuidV7.NewGuid(), UuidV7.NewGuid(), CompanyLinkScope.SharedFloor, Now);
        noOperator.Should().Throw<ArgumentException>();

        var noScope = () => CompanyLink.Create(TenantId, OperatorId, UuidV7.NewGuid(), UuidV7.NewGuid(), CompanyLinkScope.None, Now);
        noScope.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void First_acceptance_moves_to_accepted_which_grants_nothing()
    {
        CompanyLink link = NewLink();

        link.Accept(link.CompanyAId, "user:a", "fp-a", Now);

        link.Status.Should().Be(CompanyLinkStatus.Accepted);
        link.AcceptedByA.Should().BeTrue();
        link.AcceptedByB.Should().BeFalse();
        link.AcceptedAt.Should().BeNull();
        link.AcceptedByABy.Should().Be("user:a");
        link.AcceptedByAAt.Should().Be(Now);
        link.AcceptedByAFingerprint.Should().Be("fp-a");
    }

    [Fact]
    public void Second_acceptance_activates_and_records_both_sides()
    {
        CompanyLink link = NewLink();
        link.Accept(link.CompanyAId, "user:a", "fp-a", Now);

        link.Accept(link.CompanyBId, "user:b", "fp-b", Now.AddMinutes(5));

        link.Status.Should().Be(CompanyLinkStatus.Active);
        link.AcceptedAt.Should().Be(Now.AddMinutes(5));
        link.AcceptedByBBy.Should().Be("user:b");
        link.AcceptedByBFingerprint.Should().Be("fp-b");
    }

    [Fact]
    public void Re_accepting_from_the_same_side_is_a_no_op_for_retried_requests()
    {
        CompanyLink link = NewLink();
        link.Accept(link.CompanyAId, "user:a", "fp-a", Now);

        var act = () => link.Accept(link.CompanyAId, "user:a", "fp-a", Now.AddMinutes(1));

        act.Should().NotThrow();
        link.Status.Should().Be(CompanyLinkStatus.Accepted);
        link.AcceptedByAAt.Should().Be(Now);
    }

    [Fact]
    public void Accept_refuses_strangers_and_finished_links()
    {
        CompanyLink link = NewLink();

        var stranger = () => link.Accept(UuidV7.NewGuid(), "user:x", "fp", Now);
        stranger.Should().Throw<InvalidOperationException>();

        link.Accept(link.CompanyAId, "user:a", "fp-a", Now);
        link.Accept(link.CompanyBId, "user:b", "fp-b", Now);

        var afterActive = () => link.Accept(link.CompanyAId, "user:a", "fp-a", Now);
        afterActive.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_is_reversible_and_revoke_is_final_with_history()
    {
        CompanyLink link = NewLink();
        link.Accept(link.CompanyAId, "user:a", "fp-a", Now);
        link.Accept(link.CompanyBId, "user:b", "fp-b", Now);

        link.Suspend("Pricing dispute under review.", Now);

        link.Status.Should().Be(CompanyLinkStatus.Suspended);
        link.SuspendedReason.Should().Be("Pricing dispute under review.");

        link.Resume();

        link.Status.Should().Be(CompanyLinkStatus.Active);
        link.SuspendedReason.Should().BeNull();
        link.SuspendedAt.Should().BeNull();
    }

    [Fact]
    public void Revoke_needs_ten_characters_and_stamps_effective_to()
    {
        CompanyLink link = NewLink();

        var shortReason = () => link.Revoke("Too short", Now);
        shortReason.Should().Throw<ArgumentException>();

        link.Revoke("Fraud confirmed by both auditors.", Now);

        link.Status.Should().Be(CompanyLinkStatus.Revoked);
        link.EffectiveTo.Should().Be(Now);
        link.RevokedReason.Should().Be("Fraud confirmed by both auditors.");

        var again = () => link.Revoke("Fraud confirmed by both auditors.", Now);
        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_and_resume_refuse_the_wrong_states()
    {
        CompanyLink link = NewLink();

        var suspendProposed = () => link.Suspend("Dispute.", Now);
        suspendProposed.Should().Throw<InvalidOperationException>();

        var resumeProposed = () => link.Resume();
        resumeProposed.Should().Throw<InvalidOperationException>();
    }

    private static CompanyLink NewLink()
        => CompanyLink.Create(TenantId, OperatorId, UuidV7.NewGuid(), UuidV7.NewGuid(), CompanyLinkScope.SharedFloor | CompanyLinkScope.SharedTill, Now);
}
